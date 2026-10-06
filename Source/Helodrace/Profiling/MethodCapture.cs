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

    // One game-main-thread capture. Other threads are explicitly rejected rather
    // than mixing their time into the main thread's nesting or CPU counters.
    public sealed class MethodCapture
    {
        private struct Entry { internal int Id, Serial; internal long Start, Children, Cpu; }
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
        public long Dropped;
        public bool Ready => !accepting && depth == 0;

        public MethodCapture(IMethodClock clock, bool[] cpu, int maximumDepth = 128)
        {
            this.clock = clock; this.cpu = (bool[])cpu.Clone(); stack = new Entry[maximumDepth];
            int n = cpu.Length;
            Calls = new long[n]; Errors = new long[n]; Inclusive = new long[n];
            TrackedSelf = new long[n]; Maximum = new long[n]; CpuTicks = new long[n];
            distribution = new long[n][];
            for (int i = 0; i < n; i++) distribution[i] = new long[DistributionCapacity];
        }
        public MethodToken Enter(int id)
        {
            if (!accepting) return default;
            if (Thread.CurrentThread.ManagedThreadId != thread) { Interlocked.Increment(ref Dropped); return default; }
            if (depth == stack.Length) { Dropped++; return default; }
            if (id < 0 || id >= Calls.Length) throw new ArgumentOutOfRangeException(nameof(id));
            int slot = depth++;
            stack[slot] = new Entry { Id = id, Serial = ++serial,
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
            Calls[id]++; if (error) Errors[id]++;
            distribution[id][(int)((Calls[id] - 1) % DistributionCapacity)] = elapsed;
            if (elapsed >= clock.Frequency / 200)
            {
                int minimum = 0;
                for (int i = 1; i < SlowElapsed.Length; i++) if (SlowElapsed[i] < SlowElapsed[minimum]) minimum = i;
                if (elapsed > SlowElapsed[minimum]) { SlowMethods[minimum] = id; SlowElapsed[minimum] = elapsed; }
            }
            Inclusive[id] += elapsed; TrackedSelf[id] += Math.Max(0, elapsed - entry.Children);
            Maximum[id] = Math.Max(Maximum[id], elapsed);
            if (entry.Cpu >= 0) CpuTicks[id] += Math.Max(0, clock.Cpu100ns() - entry.Cpu);
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
