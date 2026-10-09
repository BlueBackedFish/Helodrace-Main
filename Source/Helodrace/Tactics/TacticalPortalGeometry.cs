using System;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalPortalGeometry
    {
        private static readonly IntVec3[] axes = { IntVec3.East, IntVec3.North };
        // Short gaps retain the two room faces of their wall. Parallel corridor
        // walls do not: their supports continue along the supposed inward axis.
        public static bool IsGap(IntVec3 cell, Func<IntVec3, bool> wall, Func<IntVec3, bool> walkable) =>
            TryGapNormal(cell, wall, walkable, out _);
        internal static bool TryWallNormal(IntVec3 cell, IntVec3 travel, Func<IntVec3, bool> support, out IntVec3 normal)
        {
            int alongZ = (support(cell - IntVec3.North) ? 1 : 0) + (support(cell + IntVec3.North) ? 1 : 0);
            int alongX = (support(cell - IntVec3.East) ? 1 : 0) + (support(cell + IntVec3.East) ? 1 : 0);
            normal = IntVec3.Invalid;
            if (alongZ == 0 && alongX == 0) return false;
            IntVec3 axis = alongZ > alongX || alongZ == alongX && Math.Abs(travel.x) >= Math.Abs(travel.z)
                ? IntVec3.East : IntVec3.North;
            int heading = axis.x * travel.x + axis.z * travel.z;
            if (heading == 0) return false;
            normal = axis * Math.Sign(heading); return true;
        }
        internal static bool TryGapNormal(IntVec3 cell, Func<IntVec3, bool> wall, Func<IntVec3, bool> walkable, out IntVec3 normal)
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
                IntVec3 candidate = new IntVec3(-lateral.z, 0, lateral.x);
                if (walkable(left + candidate) && walkable(right + candidate)
                    || walkable(left - candidate) && walkable(right - candidate))
                { normal = candidate; return true; }
            }
            normal = IntVec3.Invalid;
            return false;
        }
    }
}
