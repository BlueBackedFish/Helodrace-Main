using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class PhotochlorogenShellExtension : DefModExtension
    {
        public HelodGasDef gasDef;
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

            ReleaseGas(def, impactMap, impactCell);
            base.Impact(hitThing, blockedByShield);
        }

        public static void ReleaseGas(ThingDef sourceDef, Map map, IntVec3 center)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            PhotochlorogenShellExtension extension =
                sourceDef?.GetModExtension<PhotochlorogenShellExtension>();
            HelodGasDef gasDef = extension?.gasDef ?? HelodGasDefOf.HD_PhotochlorogenGasGrid;
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

                if (!HelodGasStore.GasCanMoveTo(cell, map))
                {
                    continue;
                }

                float distanceFactor = Mathf.InverseLerp(radius, 0f, center.DistanceTo(cell));
                float cellDensity = density * Mathf.Lerp(edgeDensityFactor, 1f, distanceFactor);
                HelodGasStore.AddGas(cell, map, gasDef, cellDensity);
            }

            FleckMaker.ThrowSmoke(center.ToVector3Shifted(), map, 1.2f);
        }
    }

    public class Projectile_SweetGasShell : Projectile_Explosive
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            ReleaseGas(def, impactMap, impactCell);
            base.Impact(hitThing, blockedByShield);
        }

        public static void ReleaseGas(ThingDef sourceDef, Map map, IntVec3 center)
        {
            if (map == null || !center.InBounds(map)) return;

            PhotochlorogenShellExtension extension =
                sourceDef?.GetModExtension<PhotochlorogenShellExtension>();
            HelodGasDef gasDef = extension?.gasDef ?? HelodGasDefOf.HD_SweetGasGrid;
            if (gasDef == null) return;

            float radius = Mathf.Max(0.1f, extension?.emissionRadius ?? 2.6f);
            float density = Mathf.Clamp01(extension?.density ?? 0.85f);
            float edgeDensityFactor = Mathf.Clamp01(extension?.edgeDensityFactor ?? 0.4f);

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || !HelodGasStore.GasCanMoveTo(cell, map)) continue;
                float distanceFactor = Mathf.InverseLerp(radius, 0f, center.DistanceTo(cell));
                float cellDensity = density * Mathf.Lerp(edgeDensityFactor, 1f, distanceFactor);
                HelodGasStore.AddGas(cell, map, gasDef, cellDensity);
            }

            FleckMaker.ThrowSmoke(center.ToVector3Shifted(), map, 1.4f);
        }
    }

    public class WhitePhosphorusRocketExtension : DefModExtension
    {
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
            ReleaseWhitePhosphorus(def, impactMap, impactCell);
        }

        public static void ReleaseWhitePhosphorus(ThingDef sourceDef, Map map,
            IntVec3 center)
        {
            if (map == null || !center.InBounds(map))
            {
                return;
            }

            WhitePhosphorusRocketExtension extension =
                sourceDef?.GetModExtension<WhitePhosphorusRocketExtension>();
            float fireRadius = Mathf.Max(0.1f, extension?.fireRadius ?? 2.4f);
            float fireChance = Mathf.Clamp01(extension?.fireChance ?? 0.55f);
            float fireSize = Mathf.Max(0.1f, extension?.fireSize ?? 0.45f);

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

    public class Projectile_NuclearArtilleryShell : Projectile_Explosive
    {
        private const int PollutionCells = 1800;

        protected override void Explode()
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            Thing instigator = Launcher;
            ModernWar.FragmentationGrenadeExtension fragmentation =
                def.GetModExtension<ModernWar.FragmentationGrenadeExtension>();
            try
            {
                ModernWar.GrenadeExplosionEffectUtility.ThrowFragments(
                    impactCell,
                    impactMap,
                    fragmentation,
                    instigator,
                    this);
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce(
                    $"Helodrace W48 fragment calculation failed; continuing nuclear explosion. {exception}",
                    GetHashCode());
            }

            base.Explode();

            if (impactMap == null || !impactCell.InBounds(impactMap))
            {
                return;
            }

            ApplyAftermath(def, impactCell, impactMap, instigator);
        }

        public static void ApplyAftermath(ThingDef sourceDef, IntVec3 impactCell,
            Map impactMap, Thing instigator, bool createEmpExplosion = true)
        {
            if (impactMap == null || !impactCell.InBounds(impactMap))
            {
                return;
            }

            ModernWar.FlashbangProjectileExtension overpressure =
                sourceDef?.GetModExtension<ModernWar.FlashbangProjectileExtension>();
            ModernWar.ExplosiveGrenadeVisualExtension smoke =
                sourceDef?.GetModExtension<ModernWar.ExplosiveGrenadeVisualExtension>();

            ModernWar.FlashbangUtility.ApplySuppression(
                impactCell,
                impactMap,
                overpressure,
                instigator);
            ModernWar.GrenadeExplosionEffectUtility.ThrowExpandingSmokeRing(
                impactCell,
                impactMap,
                smoke);
            ModernWar.GrenadeExplosionEffectUtility.ThrowLingeringExplosionDust(
                impactCell,
                impactMap,
                smoke);

            if (ModsConfig.BiotechActive)
            {
                PollutionUtility.GrowPollutionAt(
                    impactCell,
                    impactMap,
                    PollutionCells,
                    null,
                    true);
            }

            FleckMaker.Static(impactCell, impactMap, FleckDefOf.ExplosionFlash, 18f);
            FleckMaker.ThrowFireGlow(impactCell.ToVector3Shifted(), impactMap, 8f);
            if (createEmpExplosion)
            {
                GenExplosion.DoExplosion(
                    impactCell,
                    impactMap,
                    15f,
                    DamageDefOf.EMP,
                    instigator,
                    damAmount: 35,
                    armorPenetration: 0f,
                    doVisualEffects: false,
                    doSoundEffects: false);
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(impactCell, 35f, true))
            {
                if (!cell.InBounds(impactMap) || !cell.Standable(impactMap))
                {
                    continue;
                }

                float distanceFactor = Mathf.InverseLerp(35f, 0f, impactCell.DistanceTo(cell));
                if (Rand.Chance(0.7f * distanceFactor))
                {
                    FireUtility.TryStartFireIn(cell, impactMap, Mathf.Lerp(0.2f, 0.9f, distanceFactor), instigator);
                }
            }
        }
    }

}
