using System;
using System.Threading;

namespace Helodrace.Profiling
{
    public interface IMethodClock
    {
        long Timestamp();
        long Cpu100ns();
        long Frequency { get; }
    }

    public struct MethodToken
    {
        internal MethodCapture Owner;
        internal int Depth, Serial;
    }
    public struct ProfileCallContext
    {
        public int Tick, Frame, MapId, PawnId, Phase;
        public string SquadId, Job;
        public bool Identity;
    }
    public struct RecordedProfileCall
    {
        public int Method, CallId, ParentId, RootId, Depth;
        public long Start, Elapsed, Self, Cpu;
        public ProfileCallContext Context;
    }
    public sealed class RecordedTickSpike
    {
        public RecordedProfileCall Root;
        public readonly RecordedProfileCall[] Calls = new RecordedProfileCall[MethodCapture.SpikeCallCapacity];
        public int Count, Seen, Gc0, Gc1, Gc2;
    }

    // One game-main-thread capture. Other threads are explicitly rejected rather
    // than mixing their time into the main thread's nesting or CPU counters.
    public sealed class MethodCapture
    {
        private struct Entry
        {
            internal int Id, Serial, Parent, Root, Depth;
            internal long Start, Children, Cpu;
            internal ProfileCallContext Context;
        }
        private readonly Entry[] stack;
        private readonly bool[] cpu;
        private readonly IMethodClock clock;
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private int depth, serial;
        private bool accepting = true;
        public readonly long[] Calls, Errors, Inclusive, TrackedSelf, Maximum, CpuTicks;
        private readonly long[][] distribution;
        public const int DistributionCapacity = 2048;
        public readonly int[] SlowMethods = new int[16];
        public readonly long[] SlowElapsed = new long[16];
        public readonly RecordedProfileCall[] SlowRecords = new RecordedProfileCall[16];
        public const int SpikeCapacity = 8, SpikeCallCapacity = 512;
        public readonly RecordedTickSpike[] TickSpikes;
        private readonly RecordedProfileCall[] tickCalls;
        private readonly int tickMethod;
        private readonly long threshold, origin;
        private int traceDepth = -1, traceRoot, traceCount, traceSeen, gc0, gc1, gc2;
        public long SpikeCandidates;
        public bool Tracing => TickSpikes != null;
        public bool OnCaptureThread => Thread.CurrentThread.ManagedThreadId == thread;
        public long Dropped;
        public bool Ready => !accepting && depth == 0;

        public MethodCapture(IMethodClock clock, bool[] cpu, int maximumDepth = 128,
            bool traceSpikes = false, int tickMethodId = 0, double spikeThresholdMs = 5, long? originTimestamp = null)
        {
            if (double.IsNaN(spikeThresholdMs) || double.IsInfinity(spikeThresholdMs) || spikeThresholdMs < .1 || spikeThresholdMs > 1000)
                throw new ArgumentOutOfRangeException(nameof(spikeThresholdMs));
            this.clock = clock; this.cpu = (bool[])cpu.Clone(); stack = new Entry[maximumDepth];
            origin = originTimestamp ?? clock.Timestamp(); tickMethod = tickMethodId;
            threshold = (long)Math.Ceiling(clock.Frequency * spikeThresholdMs / 1000);
            if (traceSpikes)
            {
                tickCalls = new RecordedProfileCall[SpikeCallCapacity];
                TickSpikes = new RecordedTickSpike[SpikeCapacity];
                for (int i = 0; i < TickSpikes.Length; i++) TickSpikes[i] = new RecordedTickSpike();
            }
            int n = cpu.Length;
            Calls = new long[n]; Errors = new long[n]; Inclusive = new long[n];
            TrackedSelf = new long[n]; Maximum = new long[n]; CpuTicks = new long[n];
            distribution = new long[n][];
            for (int i = 0; i < n; i++) distribution[i] = new long[DistributionCapacity];
        }
        public MethodToken Enter(int id, ProfileCallContext context = default)
        {
            if (!accepting) return default;
            if (Thread.CurrentThread.ManagedThreadId != thread) { Interlocked.Increment(ref Dropped); return default; }
            if (depth == stack.Length) { Dropped++; return default; }
            if (id < 0 || id >= Calls.Length) throw new ArgumentOutOfRangeException(nameof(id));
            int slot = depth++;
            if (Tracing)
            {
                if (slot > 0 && !context.Identity)
                {
                    ProfileCallContext parent = stack[slot - 1].Context;
                    context.MapId = parent.MapId; context.PawnId = parent.PawnId;
                    context.SquadId = parent.SquadId; context.Phase = parent.Phase; context.Job = parent.Job;
                }
                if (id == tickMethod && traceDepth < 0)
                {
                    traceDepth = slot; traceRoot = serial + 1; traceCount = traceSeen = 0;
                    gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
                }
                else if (traceDepth >= 0) context.Tick = stack[traceDepth].Context.Tick;
            }
            stack[slot] = new Entry { Id = id, Serial = ++serial,
                Parent = slot > 0 ? stack[slot - 1].Serial : 0,
                Root = traceDepth >= 0 ? traceRoot : slot > 0 ? stack[slot - 1].Root : serial,
                Depth = slot, Context = context,
                Cpu = cpu[id] ? clock.Cpu100ns() : -1, Start = clock.Timestamp() };
            return new MethodToken { Owner = this, Depth = slot, Serial = serial };
        }
        public void Leave(MethodToken token, bool error)
        {
            if (token.Owner != this || depth == 0 || token.Depth != depth - 1
                || stack[token.Depth].Serial != token.Serial) return;
            Entry entry = stack[--depth];
            long elapsed = Math.Max(0, clock.Timestamp() - entry.Start);
            int id = entry.Id;
            long cpuCost = entry.Cpu >= 0 ? Math.Max(0, clock.Cpu100ns() - entry.Cpu) : -1;
            long self = Math.Max(0, elapsed - entry.Children);
            RecordedProfileCall record = default;
            if (Tracing || elapsed >= clock.Frequency / 200)
                record = new RecordedProfileCall { Method = id, CallId = entry.Serial, ParentId = entry.Parent,
                    RootId = entry.Root, Depth = entry.Depth, Start = entry.Start - origin, Elapsed = elapsed,
                    Self = self, Cpu = cpuCost, Context = entry.Context };
            Calls[id]++; if (error) Errors[id]++;
            distribution[id][(int)((Calls[id] - 1) % DistributionCapacity)] = elapsed;
            if (elapsed >= clock.Frequency / 200)
            {
                int minimum = 0;
                for (int i = 1; i < SlowElapsed.Length; i++) if (SlowElapsed[i] < SlowElapsed[minimum]) minimum = i;
                if (elapsed > SlowElapsed[minimum])
                { SlowMethods[minimum] = id; SlowElapsed[minimum] = elapsed; SlowRecords[minimum] = record; }
            }
            if (Tracing && traceDepth >= 0)
            {
                traceSeen++;
                // Reserve the last slot for the enclosing tick even on overflow.
                if (depth == traceDepth) tickCalls[traceCount++] = record;
                else if (traceCount < SpikeCallCapacity - 1) tickCalls[traceCount++] = record;
                if (depth == traceDepth)
                {
                    if (elapsed >= threshold)
                    {
                        SpikeCandidates++;
                        int minimum = 0;
                        for (int i = 1; i < TickSpikes.Length; i++)
                            if (TickSpikes[i].Root.Elapsed < TickSpikes[minimum].Root.Elapsed) minimum = i;
                        if (elapsed > TickSpikes[minimum].Root.Elapsed)
                        {
                            RecordedTickSpike spike = TickSpikes[minimum];
                            spike.Root = record; spike.Count = traceCount; spike.Seen = traceSeen;
                            spike.Gc0 = GC.CollectionCount(0) - gc0; spike.Gc1 = GC.CollectionCount(1) - gc1;
                            spike.Gc2 = GC.CollectionCount(2) - gc2;
                            Array.Copy(tickCalls, spike.Calls, traceCount);
                        }
                    }
                    traceDepth = -1;
                }
            }
            Inclusive[id] += elapsed; TrackedSelf[id] += self;
            Maximum[id] = Math.Max(Maximum[id], elapsed);
            if (cpuCost >= 0) CpuTicks[id] += cpuCost;
            if (depth > 0) stack[depth - 1].Children += elapsed;
        }
        public void Stop() { accepting = false; }
        public double Milliseconds(long ticks) => ticks * 1000.0 / clock.Frequency;
        public double[] Percentiles(int id)
        {
            int count = (int)Math.Min(Calls[id], DistributionCapacity);
            if (count == 0) return new double[3];
            var values = new long[count]; Array.Copy(distribution[id], values, count); Array.Sort(values);
            return new[] { Milliseconds(values[(int)Math.Ceiling(count * .50) - 1]),
                Milliseconds(values[(int)Math.Ceiling(count * .95) - 1]), Milliseconds(values[(int)Math.Ceiling(count * .99) - 1]) };
        }
    }
}
