using System.Collections.Generic;
using System;

namespace Helodrace
{
    public static class RaidFormationTopology
    {
        // Restrict the search to a small visible staging area. Global room
        // connectivity permits routes around corners and through distant doors.
        public static HashSet<T> Connected<T>(IEnumerable<T> cells, T anchor,
            Func<T, IEnumerable<T>> neighbors, Func<T, bool> visible)
        {
            var available = new HashSet<T>(cells);
            var connected = new HashSet<T>();
            if (!available.Contains(anchor) || !visible(anchor)) return connected;
            var pending = new Queue<T>();
            connected.Add(anchor);
            pending.Enqueue(anchor);
            while (pending.Count > 0)
                foreach (T next in neighbors(pending.Dequeue()))
                    if (available.Contains(next) && !connected.Contains(next) && visible(next))
                    {
                        connected.Add(next);
                        pending.Enqueue(next);
                    }
            return connected;
        }
    }
}
