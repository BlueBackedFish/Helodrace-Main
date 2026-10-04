using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace
{
    internal static class RaidObservationSight
    {
        // Match Verb.TryFindShootLineFromTo's pawn geometry, without weapon/range restrictions.
        // A pawn in a doorway can be shot at its native lean cell even when its center is hidden.
        public static bool CanSeePawn(Map map, IntVec3 source, Pawn target, int radius,
            bool allowSourceLean, Func<IntVec3, IntVec3, bool> clearLine)
        {
            if (target?.Map != map || !source.InBounds(map) || !target.Position.InBounds(map)
                || source.DistanceToSquared(target.Position) > radius * radius) return false;
            return VisibleLine(source, allowSourceLean,
                (cell, list) => ShootLeanUtility.CalcShootableCellsOf(list, target, cell),
                list => ShootLeanUtility.LeanShootingSourcesFromTo(source, target.Position, map, list), clearLine);
        }

        internal static bool VisibleLine(IntVec3 source, bool allowSourceLean,
            Action<IntVec3, List<IntVec3>> targetCells, Action<List<IntVec3>> sourceCells,
            Func<IntVec3, IntVec3, bool> clearLine)
        {
            var destinations = new List<IntVec3>(5);
            bool From(IntVec3 cell)
            {
                destinations.Clear();
                targetCells(cell, destinations);
                foreach (IntVec3 destination in destinations)
                    if (clearLine(cell, destination)) return true;
                return false;
            }
            if (From(source)) return true;
            if (!allowSourceLean) return false;
            var sources = new List<IntVec3>(5);
            sourceCells(sources);
            foreach (IntVec3 cell in sources)
                if (cell != source && From(cell)) return true;
            return false;
        }

        // Buildings/empty cells have no target pawn to lean around a blocking wall.
        public static bool CanSeeCell(Map map, IntVec3 source, IntVec3 target, int radius,
            Func<IntVec3, IntVec3, bool> clearLine)
        {
            if (!source.InBounds(map) || !target.InBounds(map)
                || source.DistanceToSquared(target) > radius * radius) return false;
            if (clearLine(source, target)) return true;
            var sources = new List<IntVec3>(5);
            ShootLeanUtility.LeanShootingSourcesFromTo(source, target, map, sources);
            foreach (IntVec3 cell in sources)
                if (cell != source && clearLine(cell, target)) return true;
            return false;
        }
    }
}
