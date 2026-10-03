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
            => new HashSet<T>(Distances(cells, anchor, neighbors, visible).Keys);

        public static Dictionary<T, int> Distances<T>(IEnumerable<T> cells, T anchor,
            Func<T, IEnumerable<T>> neighbors, Func<T, bool> visible)
        {
            var available = new HashSet<T>(cells);
            var connected = new Dictionary<T, int>();
            if (!available.Contains(anchor) || !visible(anchor)) return connected;
            var pending = new Queue<T>();
            connected.Add(anchor, 0);
            pending.Enqueue(anchor);
            while (pending.Count > 0)
            {
                T current = pending.Dequeue();
                foreach (T next in neighbors(current))
                    if (available.Contains(next) && !connected.ContainsKey(next) && visible(next))
                    {
                        connected.Add(next, connected[current] + 1);
                        pending.Enqueue(next);
                    }
            }
            return connected;
        }
    }
}
