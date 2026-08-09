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
        private const int SolidPenetrationLayers = 3;

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
            HashSet<IntVec3> currentLayer = new HashSet<IntVec3>(
                vanillaCells.Where(cell => IsFullBlocker(cell.GetEdifice(map))));
            HashSet<IntVec3> visitedBlockers = new HashSet<IntVec3>(currentLayer);

            for (int layer = 2; layer <= SolidPenetrationLayers && currentLayer.Count > 0; layer++)
            {
                HashSet<IntVec3> nextLayer = new HashSet<IntVec3>();
                foreach (IntVec3 sourceCell in currentLayer)
                    AddAdjacentBlockers(
                        center,
                        sourceCell,
                        map,
                        radiusSquared,
                        visitedBlockers,
                        nextLayer);

                affectedCells.UnionWith(nextLayer);
                currentLayer = nextLayer;
            }

            return affectedCells;
        }

        private static void AddAdjacentBlockers(
            IntVec3 center,
            IntVec3 sourceCell,
            Map map,
            float radiusSquared,
            HashSet<IntVec3> visitedBlockers,
            HashSet<IntVec3> nextLayer)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                {
                    if (offsetX == 0 && offsetZ == 0)
                        continue;

                    IntVec3 candidate = sourceCell + new IntVec3(offsetX, 0, offsetZ);
                    if (!candidate.InBounds(map))
                        continue;

                    if (candidate.DistanceToSquared(center) > radiusSquared)
                        continue;

                    Building candidateBuilding = candidate.GetEdifice(map);
                    if (!IsFullBlocker(candidateBuilding) || !visitedBlockers.Add(candidate))
                        continue;

                    nextLayer.Add(candidate);
                }
            }
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
