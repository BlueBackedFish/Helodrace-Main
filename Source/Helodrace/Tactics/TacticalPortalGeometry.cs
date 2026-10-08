using System;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalPortalGeometry
    {
        private static readonly IntVec3[] axes = { IntVec3.East, IntVec3.North };
        // Short gaps retain the two room faces of their wall. Parallel corridor
        // walls do not: their supports continue along the supposed inward axis.
        public static bool IsGap(IntVec3 cell, Func<IntVec3, bool> wall, Func<IntVec3, bool> walkable)
        {
            foreach (IntVec3 lateral in axes)
            {
                IntVec3 left = IntVec3.Invalid, right = IntVec3.Invalid;
                for (int distance = 1; distance <= 3; distance++)
                {
                    IntVec3 probe = cell - lateral * distance;
                    if (wall(probe)) { left = probe; break; }
                    if (!walkable(probe)) break;
                }
                if (!left.IsValid) continue;
                for (int distance = 1; distance <= 3; distance++)
                {
                    IntVec3 probe = cell + lateral * distance;
                    if (wall(probe)) { right = probe; break; }
                    if (!walkable(probe)) break;
                }
                if (!right.IsValid) continue;
                IntVec3 normal = new IntVec3(-lateral.z, 0, lateral.x);
                if (walkable(left + normal) && walkable(right + normal)
                    || walkable(left - normal) && walkable(right - normal)) return true;
            }
            return false;
        }
    }
}
