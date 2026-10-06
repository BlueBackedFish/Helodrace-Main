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
        public long Dropped;
        public bool Ready => !accepting && depth == 0;

        public MethodCapture(IMethodClock clock, bool[] cpu, int maximumDepth = 128)
        {
            this.clock = clock; this.cpu = (bool[])cpu.Clone(); stack = new Entry[maximumDepth];
            int n = cpu.Length;
            Calls = new long[n]; Errors = new long[n]; Inclusive = new long[n];
            TrackedSelf = new long[n]; Maximum = new long[n]; CpuTicks = new long[n];
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
            Inclusive[id] += elapsed; TrackedSelf[id] += Math.Max(0, elapsed - entry.Children);
            Maximum[id] = Math.Max(Maximum[id], elapsed);
            if (entry.Cpu >= 0) CpuTicks[id] += Math.Max(0, clock.Cpu100ns() - entry.Cpu);
            if (depth > 0) stack[depth - 1].Children += elapsed;
        }
        public void Stop() { accepting = false; }
        public double Milliseconds(long ticks) => ticks * 1000.0 / clock.Frequency;
    }
}
