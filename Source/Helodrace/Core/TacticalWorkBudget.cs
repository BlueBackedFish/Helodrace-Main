using System;
using System.Collections.Generic;

namespace Helodrace
{
    // Coarse admission budget. It never interrupts a live planner mid-operation.
    internal sealed class TacticalWorkBudget
    {
        private readonly long allowance;
        private readonly int maximumActions;
        private readonly Queue<string> queue = new Queue<string>();
        private readonly Dictionary<string, int> requested = new Dictionary<string, int>();
        private int frame = -1, actions;
        private long spent;
        internal long Deferred { get; private set; }
        internal TacticalWorkBudget(long allowance, int maximumActions) { this.allowance = allowance; this.maximumActions = maximumActions; }
        internal void BeginFrame(int current)
        { if (frame != current) { frame = current; actions = 0; spent = 0; } }
        internal bool Admit(string owner, int current)
        {
            BeginFrame(current);
            if (!requested.ContainsKey(owner)) queue.Enqueue(owner);
            requested[owner] = current;
            while (queue.Count > 0 && current - requested[queue.Peek()] > 120) requested.Remove(queue.Dequeue());
            if (queue.Peek() != owner || actions >= maximumActions || spent >= allowance) { Deferred++; return false; }
            requested.Remove(queue.Dequeue()); actions++; return true;
        }
        internal void Record(long ticks, int current) { BeginFrame(current); spent += Math.Max(0, ticks); }
    }
}
