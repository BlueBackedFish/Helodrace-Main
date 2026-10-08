using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalDestinationFootprint
    {
        public const int Radius = 8, CellLimit = 289;

        // One bounded connected footprint around a direct objective. Traversal
        // may include furniture, but only usable floor becomes a pawn's post.
        public static List<IntVec3> Find(IntVec3 goal, int count,
            Func<IntVec3, bool> traversable, Func<IntVec3, bool> usable)
        {
            var positions = new List<IntVec3>();
            if (!traversable(goal)) return positions;
            var visited = new HashSet<IntVec3> { goal };
            var pending = new Queue<IntVec3>(); pending.Enqueue(goal);
            while (pending.Count > 0 && positions.Count < count)
            {
                IntVec3 cell = pending.Dequeue();
                if (usable(cell)) positions.Add(cell);
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (Math.Abs(next.x - goal.x) > Radius || Math.Abs(next.z - goal.z) > Radius
                        || visited.Count >= CellLimit || visited.Contains(next) || !traversable(next)) continue;
                    visited.Add(next); pending.Enqueue(next);
                }
            }
            return positions;
        }
    }
}
