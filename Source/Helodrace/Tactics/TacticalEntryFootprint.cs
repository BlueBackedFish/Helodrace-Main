using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalEntryFootprint
    {
        public const int Depth = 8, Width = 8, CellLimit = (Width * 2 + 1) * Depth;
        public static bool Build(IntVec3 opening, IntVec3 inward, int count, int stackSide,
            Func<IntVec3, bool> walkable, Func<IntVec3, bool> free,
            List<IntVec3> positions, HashSet<IntVec3> reachable, out IntVec3 entryLane)
        {
            positions.Clear(); reachable.Clear(); entryLane = IntVec3.Invalid;
            if (count <= 0) return false;
            IntVec3 tangent = new IntVec3(-inward.z, 0, inward.x), inside = opening + inward;
            int Index(IntVec3 c)
            {
                IntVec3 delta = c - opening;
                int depth = delta.x * inward.x + delta.z * inward.z;
                int width = delta.x * tangent.x + delta.z * tangent.z;
                return depth < 1 || depth > Depth || Math.Abs(width) > Width
                    ? -1 : (depth - 1) * (Width * 2 + 1) + width + Width;
            }
            var visited = new byte[CellLimit];
            bool Walk(IntVec3 c)
            {
                int i = Index(c);
                if (i < 0) return false;
                if (visited[i] == 0) visited[i] = walkable(c) ? (byte)2 : (byte)1;
                return visited[i] == 2;
            }
            if (!Walk(inside)) return false;
            IntVec3 lane = inside + inward;
            if (Walk(lane) && free(lane)) { entryLane = lane; reachable.Add(lane); }
            IntVec3 reservedLane = entryLane;
            var candidates = new List<Seat>();
            var flood = new TacticalStackFormation.Flood(Index, inward);
            flood.Add(inside, 0);
            while (flood.Pop(out IntVec3 c, out int cost))
            {
                reachable.Add(c);
                if (c != inside && c != reservedLane && free(c))
                {
                    IntVec3 delta = c - opening;
                    candidates.Add(new Seat { Cell = c, Width = delta.x * tangent.x + delta.z * tangent.z,
                        Distance = c.DistanceToSquared(opening) });
                    // Select the local squad-sized space BEFORE ranking it.
                    // A distant wall cannot pull posts out of this footprint.
                    if (candidates.Count == count) break;
                }
                flood.Expand(c, cost, Walk);
            }
            // A two-cell closet has no side seat. Use its second floor cell
            // rather than rejecting entry solely because it was a transit lane.
            if (candidates.Count == 0 && reservedLane.IsValid)
            {
                candidates.Add(new Seat { Cell = reservedLane, Width = 0, Distance = reservedLane.DistanceToSquared(opening) });
                entryLane = IntVec3.Invalid;
            }
            candidates.Sort((a, b) =>
            {
                int compare = Math.Abs(b.Width).CompareTo(Math.Abs(a.Width));
                if (compare == 0) compare = b.Distance.CompareTo(a.Distance);
                if (compare == 0) compare = (a.Width * stackSide).CompareTo(b.Width * stackSide);
                if (compare == 0) compare = a.Width.CompareTo(b.Width);
                return compare;
            });
            for (int i = 0; i < Math.Min(count, candidates.Count); i++) positions.Add(candidates[i].Cell);
            return positions.Count == count;
        }
        private struct Seat { internal IntVec3 Cell; internal int Width, Distance; }
    }
}
