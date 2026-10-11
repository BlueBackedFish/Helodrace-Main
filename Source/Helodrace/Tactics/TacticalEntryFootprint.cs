using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalEntryFootprint
    {
        public const int Depth = 4, Width = 8;
        public static bool Build(IntVec3 opening, IntVec3 inward, int count, int stackSide,
            Func<IntVec3, bool> walkable, Func<IntVec3, bool> free, Func<IntVec3, bool> wall,
            List<IntVec3> positions, HashSet<IntVec3> reachable, out IntVec3 entryLane)
        {
            positions.Clear(); reachable.Clear(); entryLane = IntVec3.Invalid;
            IntVec3 tangent = new IntVec3(-inward.z, 0, inward.x), inside = opening + inward;
            int Index(IntVec3 c)
            {
                IntVec3 delta = c - opening;
                int depth = delta.x * inward.x + delta.z * inward.z;
                int width = delta.x * tangent.x + delta.z * tangent.z;
                return depth < 1 || depth > Depth || Math.Abs(width) > Width
                    ? -1 : (depth - 1) * (Width * 2 + 1) + width + Width;
            }
            var visited = new byte[(Width * 2 + 1) * Depth];
            bool Walk(IntVec3 c)
            {
                int i = Index(c);
                if (i < 0) return false;
                if (visited[i] == 0) visited[i] = walkable(c) ? (byte)2 : (byte)1;
                return visited[i] == 2;
            }
            if (!Walk(inside)) return false;
            var flood = new TacticalStackFormation.Flood(Index, inward);
            flood.Add(inside, 0);
            while (flood.Pop(out IntVec3 c, out int cost))
            { reachable.Add(c); flood.Expand(c, cost, Walk); }
            IntVec3 lane = inside + inward;
            if (reachable.Contains(lane) && free(lane)) entryLane = lane;
            var candidates = new List<Seat>();
            foreach (IntVec3 c in reachable)
            {
                if (c == inside || c == entryLane || !free(c)) continue;
                bool adjacent = false, front = false;
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    adjacent |= wall(c + direction);
                    front |= wall(c + direction * 2) && Walk(c + direction);
                }
                IntVec3 delta = c - opening;
                int width = delta.x * tangent.x + delta.z * tangent.z;
                int depth = delta.x * inward.x + delta.z * inward.z;
                // First the strip in front of the wall, then its distant edge.
                // Near-mouth wall posts are last; unprotected centre is fallback.
                int zone = adjacent ? Math.Abs(width) <= 1 ? 3 : 1 : front ? 0 : 2;
                candidates.Add(new Seat { Cell = c, Zone = zone, Width = width, Depth = depth });
            }
            candidates.Sort((a, b) =>
            {
                int compare = a.Zone.CompareTo(b.Zone);
                if (compare == 0) compare = Math.Abs(b.Width).CompareTo(Math.Abs(a.Width));
                if (compare == 0) compare = a.Depth.CompareTo(b.Depth);
                if (compare == 0) compare = (a.Width * stackSide).CompareTo(b.Width * stackSide);
                if (compare == 0) compare = a.Width.CompareTo(b.Width);
                return compare;
            });
            for (int i = 0; i < Math.Min(count, candidates.Count); i++) positions.Add(candidates[i].Cell);
            return positions.Count == count;
        }
        private struct Seat { internal IntVec3 Cell; internal int Zone, Width, Depth; }
    }
}
