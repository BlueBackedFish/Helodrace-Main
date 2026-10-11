using System;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalPortalGeometry
    {
        public const int LayerLimit = 4;
        public static IntVec3 FirstBlocked(IntVec3 opening, IntVec3 inward, int depth, Func<IntVec3, bool> open)
        {
            for (int layer = 0; layer < depth; layer++)
            {
                IntVec3 cell = opening + inward * layer;
                if (!open(cell)) return cell;
            }
            return IntVec3.Invalid;
        }
        public static bool PassageOpen(IntVec3 opening, IntVec3 inward, int depth, Func<IntVec3, bool> open) =>
            !FirstBlocked(opening, inward, depth + 1, open).IsValid;
        // Contiguous layers share one outer mouth and one destination floor.
        // A walkable gap between two walls is a separate space, not thickness.
        public static bool TryDepth(IntVec3 opening, IntVec3 inward, Func<IntVec3, bool> layer,
            Func<IntVec3, bool> floor, out int depth)
        {
            depth = 1;
            for (int distance = 1; distance <= LayerLimit; distance++)
            {
                IntVec3 cell = opening + inward * distance;
                if (layer(cell)) continue;
                if (!floor(cell)) return false;
                depth = distance; return true;
            }
            return false;
        }

        private static readonly IntVec3[] axes = { IntVec3.East, IntVec3.North };
        // Floor excludes doors even when their path-grid cells are walkable.
        // A gap needs a wall run along its lateral axis, not two parallel walls.
        public static bool IsGap(IntVec3 cell, Func<IntVec3, bool> wall, Func<IntVec3, bool> floor) =>
            TryGapNormal(cell, wall, floor, out _);
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
        internal static bool TryGapNormal(IntVec3 cell, Func<IntVec3, bool> wall, Func<IntVec3, bool> floor, out IntVec3 normal)
        {
            foreach (IntVec3 lateral in axes)
            {
                IntVec3 left = IntVec3.Invalid, right = IntVec3.Invalid;
                for (int distance = 1; distance <= 3; distance++)
                {
                    IntVec3 probe = cell - lateral * distance;
                    if (wall(probe)) { left = probe; break; }
                    if (!floor(probe)) break;
                }
                if (!left.IsValid) continue;
                for (int distance = 1; distance <= 3; distance++)
                {
                    IntVec3 probe = cell + lateral * distance;
                    if (wall(probe)) { right = probe; break; }
                    if (!floor(probe)) break;
                }
                if (!right.IsValid) continue;
                // At least one end must continue away from the gap along the
                // wall. Keep fragmented ends, but reject corridor side walls
                // whose facing doors make their normal neighbours walkable.
                if (!wall(left - lateral) && !wall(right + lateral)) continue;
                IntVec3 candidate = new IntVec3(-lateral.z, 0, lateral.x);
                if (floor(left + candidate) && floor(right + candidate)
                    || floor(left - candidate) && floor(right - candidate))
                { normal = candidate; return true; }
            }
            normal = IntVec3.Invalid;
            return false;
        }
    }
}
