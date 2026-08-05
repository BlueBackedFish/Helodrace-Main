using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public class DamageWorker_ANFOBomb : DamageWorker_AddInjury
    {
        private const float MiningLevel12Yield = 1.04f;

        private static readonly AccessTools.FieldRef<Mineable, float> MineableYieldPct =
            AccessTools.FieldRefAccess<Mineable, float>("yieldPct");

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            Mineable mineable = victim as Mineable;
            if (!IsResourceMineable(mineable))
                return base.Apply(dinfo, victim);

            ref float yieldPct = ref MineableYieldPct(mineable);
            float previousYieldPct = yieldPct;
            yieldPct = MiningLevel12Yield;

            DamageResult result = base.Apply(dinfo, victim);
            if (!mineable.Destroyed)
                yieldPct = previousYieldPct;

            return result;
        }

        public override IEnumerable<IntVec3> ExplosionCellsToHit(
            IntVec3 center,
            Map map,
            float radius,
            IntVec3? needLOSToCell1 = null,
            IntVec3? needLOSToCell2 = null,
            FloatRange? affectedAngle = null)
        {
            List<IntVec3> vanillaCells = base.ExplosionCellsToHit(
                    center,
                    map,
                    radius,
                    needLOSToCell1,
                    needLOSToCell2,
                    affectedAngle)
                .Distinct()
                .ToList();

            if (map == null)
                return vanillaCells;

            HashSet<IntVec3> affectedCells = new HashSet<IntVec3>(vanillaCells);
            float radiusSquared = radius * radius;

            foreach (IntVec3 firstLayerCell in vanillaCells)
            {
                Building firstLayerBuilding = firstLayerCell.GetEdifice(map);
                if (!IsFullBlocker(firstLayerBuilding))
                    continue;

                IntVec3 secondLayerCell = FindSecondLayerCell(
                    center,
                    firstLayerCell,
                    map,
                    radiusSquared);
                if (secondLayerCell.IsValid)
                    affectedCells.Add(secondLayerCell);
            }

            return affectedCells;
        }

        private static IntVec3 FindSecondLayerCell(
            IntVec3 center,
            IntVec3 firstLayerCell,
            Map map,
            float radiusSquared)
        {
            int directionX = firstLayerCell.x - center.x;
            int directionZ = firstLayerCell.z - center.z;
            int firstDistanceSquared = firstLayerCell.DistanceToSquared(center);
            IntVec3 bestCell = IntVec3.Invalid;
            int bestCrossProduct = int.MaxValue;
            int bestDistanceSquared = int.MaxValue;

            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                {
                    if (offsetX == 0 && offsetZ == 0)
                        continue;

                    int outwardDotProduct = offsetX * directionX + offsetZ * directionZ;
                    if (outwardDotProduct <= 0)
                        continue;

                    IntVec3 candidate = firstLayerCell + new IntVec3(offsetX, 0, offsetZ);
                    if (!candidate.InBounds(map))
                        continue;

                    int candidateDistanceSquared = candidate.DistanceToSquared(center);
                    if (candidateDistanceSquared <= firstDistanceSquared
                        || candidateDistanceSquared > radiusSquared)
                    {
                        continue;
                    }

                    Building candidateBuilding = candidate.GetEdifice(map);
                    if (!IsFullBlocker(candidateBuilding))
                        continue;

                    int candidateX = candidate.x - center.x;
                    int candidateZ = candidate.z - center.z;
                    int crossProduct = Math.Abs(directionX * candidateZ - directionZ * candidateX);

                    if (crossProduct < bestCrossProduct
                        || (crossProduct == bestCrossProduct
                            && candidateDistanceSquared < bestDistanceSquared))
                    {
                        bestCell = candidate;
                        bestCrossProduct = crossProduct;
                        bestDistanceSquared = candidateDistanceSquared;
                    }
                }
            }

            return bestCell;
        }

        private static bool IsFullBlocker(Building building)
        {
            if (building == null || building.def.Fillage != FillCategory.Full)
                return false;

            return !(building is Building_Door door) || !door.Open;
        }

        private static bool IsResourceMineable(Mineable mineable)
        {
            BuildingProperties building = mineable?.def?.building;
            return building != null
                && building.isResourceRock
                && building.mineableThing != null;
        }
    }
}
