using System;
using System.Collections.Generic;

namespace Helodrace
{
    // FIFO grants for ordinary observation; combat interrupts use their own fast path.
    internal sealed class TacticalServiceBudget
    {
        private readonly int capacity, interval, minimum;
        private readonly Queue<string> waiting = new Queue<string>();
        private readonly Dictionary<string, int> requested = new Dictionary<string, int>();
        private int window = int.MinValue, remaining;
        internal long Deferred;
        internal TacticalServiceBudget(int capacity, int interval, int minimum)
        { this.capacity = capacity; this.interval = interval; this.minimum = minimum; }
        internal int Grant(string owner, int tick, int desired)
        {
            int nextWindow = tick / interval;
            if (window != nextWindow) { window = nextWindow; remaining = capacity; }
            if (!requested.ContainsKey(owner)) waiting.Enqueue(owner);
            requested[owner] = tick;
            // A departed unit must not permanently hold the head of the queue.
            while (waiting.Count > 0 && tick - requested[waiting.Peek()] > interval * 6)
                requested.Remove(waiting.Dequeue());
            int amount = Math.Min(desired, remaining);
            if (waiting.Peek() != owner || amount < Math.Min(desired, minimum)) { Deferred++; return 0; }
            waiting.Dequeue(); requested.Remove(owner); remaining -= amount;
            return amount;
        }
    }
}
