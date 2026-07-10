using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class PhotochlorogenShellExtension : DefModExtension
    {
        public ThingDef gasDef;
        public float emissionRadius = 1.6f;
        public float density = 0.75f;
        public float edgeDensityFactor = 0.45f;
    }

    public class Projectile_PhotochlorogenShell : Projectile_Explosive
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;

            ReleaseGas(impactMap, impactCell);
            base.Impact(hitThing, blockedByShield);
        }

        private void ReleaseGas(Map map, IntVec3 center)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            PhotochlorogenShellExtension extension = def.GetModExtension<PhotochlorogenShellExtension>();
            ThingDef gasDef = extension?.gasDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("HD_PhotochlorogenGas");
            if (gasDef == null)
            {
                return;
            }

            float radius = Mathf.Max(0.1f, extension?.emissionRadius ?? 1.6f);
            float density = Mathf.Clamp01(extension?.density ?? 0.75f);
            float edgeDensityFactor = Mathf.Clamp01(extension?.edgeDensityFactor ?? 0.45f);

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                MapComponent_PhotochlorogenGasGrid gasGrid = map.GetComponent<MapComponent_PhotochlorogenGasGrid>();
                if (gasGrid != null && !gasGrid.CanGasOccupy(cell))
                {
                    continue;
                }

                float distanceFactor = Mathf.InverseLerp(radius, 0f, center.DistanceTo(cell));
                float cellDensity = density * Mathf.Lerp(edgeDensityFactor, 1f, distanceFactor);
                Gas_Photochlorogen.AddGasAt(cell, map, gasDef, cellDensity);
            }

            FleckMaker.ThrowSmoke(center.ToVector3Shifted(), map, 1.2f);
        }
    }

    public class WhitePhosphorusRocketExtension : DefModExtension
    {
        public float smokeRadius = 3.6f;
        public float fireRadius = 2.4f;
        public float fireChance = 0.55f;
        public float fireSize = 0.45f;
    }

    public class Projectile_WhitePhosphorusRocket : Projectile_Explosive
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;

            base.Impact(hitThing, blockedByShield);
            ReleaseWhitePhosphorus(impactMap, impactCell);
        }

        private void ReleaseWhitePhosphorus(Map map, IntVec3 center)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            WhitePhosphorusRocketExtension extension = def.GetModExtension<WhitePhosphorusRocketExtension>();
            float smokeRadius = Mathf.Max(0.1f, extension?.smokeRadius ?? 3.6f);
            float fireRadius = Mathf.Max(0.1f, extension?.fireRadius ?? 2.4f);
            float fireChance = Mathf.Clamp01(extension?.fireChance ?? 0.55f);
            float fireSize = Mathf.Max(0.1f, extension?.fireSize ?? 0.45f);

            GasUtility.AddGas(center, map, GasType.BlindSmoke, smokeRadius);

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, fireRadius, true))
            {
                if (!cell.InBounds(map) || !cell.Standable(map))
                {
                    continue;
                }

                float distanceFactor = Mathf.InverseLerp(fireRadius, 0f, center.DistanceTo(cell));
                if (Rand.Chance(fireChance * distanceFactor))
                {
                    FireUtility.TryStartFireIn(cell, map, fireSize, null);
                }
            }
        }
    }

}
