using System;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace
{
    // Bounded live connectivity. Room IDs are the raid's original IDs, so an
    // explosion can connect rooms without silently marking both as cleared.
    internal sealed class CqbLocalTopology
    {
        public readonly int Width, Height;
        public readonly int[] Rooms;
        public readonly bool[] Walkable, Portals;
        public CqbLocalTopology(int width, int height, int[] rooms, bool[] walkable, bool[] portals)
        {
            if (width <= 0 || height <= 0 || rooms.Length != width * height
                || walkable.Length != rooms.Length || portals.Length != rooms.Length)
                throw new ArgumentException("Invalid CQB window dimensions.");
            Width = width; Height = height;
            Rooms = rooms; Walkable = walkable; Portals = portals;
        }
        private IEnumerable<int> Neighbors(int index)
        {
            if (index % Width > 0) yield return index - 1;
            if (index % Width + 1 < Width) yield return index + 1;
            if (index >= Width) yield return index - Width;
            if (index + Width < Rooms.Length) yield return index + Width;
        }
        public int[] Distances(int start, out int[] previous)
        {
            int[] distance = Enumerable.Repeat(-1, Rooms.Length).ToArray();
            previous = Enumerable.Repeat(-1, Rooms.Length).ToArray();
            if (start < 0 || start >= Rooms.Length || !Walkable[start]) return distance;
            var pending = new Queue<int>();
            pending.Enqueue(start); distance[start] = 0;
            while (pending.Count > 0)
            {
                int cell = pending.Dequeue();
                foreach (int next in Neighbors(cell))
                    if (Walkable[next] && distance[next] < 0)
                    {
                        previous[next] = cell; distance[next] = distance[cell] + 1;
                        pending.Enqueue(next);
                    }
            }
            return distance;
        }
        public List<int> Path(int start, int target)
        {
            var route = new List<int>();
            if (target < 0 || target >= Rooms.Length) return route;
            int[] distance = Distances(start, out int[] previous);
            if (distance[target] < 0) return route;
            for (int cell = target; cell >= 0; cell = previous[cell]) route.Add(cell);
            route.Reverse();
            return route;
        }
        public IEnumerable<int> NeighborTargets(int start, ISet<int> cleared)
        {
            var candidates = new Dictionary<int, (int Cell, int Cost)>();
            int current = start >= 0 && start < Rooms.Length ? Rooms[start] : 0;
            if (start < 0 || start >= Rooms.Length || !Walkable[start]) return Enumerable.Empty<int>();
            int[] distance = Enumerable.Repeat(-1, Rooms.Length).ToArray();
            var pending = new Queue<int>();
            pending.Enqueue(start); distance[start] = 0;
            while (pending.Count > 0)
            {
                int cell = pending.Dequeue();
                foreach (int next in Neighbors(cell))
                {
                    if (!Walkable[next])
                    {
                        if (Portals[next])
                            foreach (int other in Neighbors(next).Where(index => Walkable[index])) Add(other, distance[cell] + 2);
                        continue;
                    }
                    if (Rooms[next] > 0 && Rooms[next] != current && !cleared.Contains(Rooms[next]))
                    {
                        Add(next, distance[cell] + 1);
                        continue;
                    }
                    if (distance[next] >= 0) continue;
                    distance[next] = distance[cell] + 1; pending.Enqueue(next);
                }
            }
            return candidates.Values.OrderBy(value => value.Cost).ThenBy(value => value.Cell).Select(value => value.Cell);
            void Add(int cell, int cost)
            {
                int room = Rooms[cell];
                if (room <= 0 || room == current || cleared.Contains(room)) return;
                if (!candidates.TryGetValue(room, out var old) || cost < old.Cost) candidates[room] = (cell, cost);
            }
        }
        public bool SameAs(CqbLocalTopology other) => other != null && Width == other.Width && Height == other.Height
            && Rooms.SequenceEqual(other.Rooms) && Walkable.SequenceEqual(other.Walkable)
            && Portals.SequenceEqual(other.Portals);
    }
}
