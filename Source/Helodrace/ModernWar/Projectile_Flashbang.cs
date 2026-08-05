using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class FlashbangProjectileExtension : DefModExtension
    {
        public HediffDef suppressionHediff;
        public float effectRadius = 12f;
        public float indoorRadiusFactor = 1.5f;
        public float outdoorDistanceExponent = 1.25f;
        public float indoorDistanceExponent = 0.65f;
        public float severityAtCenter = 1f;
        public float severityAtEdge = 0.12f;
        public float outdoorFactor = 0.7f;
        public int smallRoomCellCount = 16;
        public int largeRoomCellCount = 160;
        public float smallRoomFactor = 1.45f;
        public float largeRoomFactor = 0.85f;
        public float differentRoomFactor = 0.65f;
        public float blockedPathFactor = 0.2f;
        public float stunThreshold = 0.55f;
        public float stunFullIntensity = 1.05f;
        public int stunTicksMin = 120;
        public int stunTicksMax = 180;
        public float stunSuppressionMultiplierMax = 1f;
        public float stunSuppressionExponent = 1.15f;
        public int stunTicksAbsoluteMax = 600;
        public DamageDef intensityDamageDef;
        public float damageThreshold = float.MaxValue;
        public float damageFullIntensity = float.MaxValue;
        public float damageAtThreshold;
        public float damageAtFullIntensity;
        public float damageArmorPenetration;
    }

    internal static class FlashbangUtility
    {
        public static void ApplySuppression(
            IntVec3 center,
            Map map,
            FlashbangProjectileExtension extension,
            Thing instigator)
        {
            if (map == null || extension?.suppressionHediff == null || !center.InBounds(map))
            {
                return;
            }

            Room blastRoom = center.GetRoom(map);
            bool detonatedIndoors = blastRoom != null && !blastRoom.UsesOutdoorTemperature;
            float baseRadius = Mathf.Max(0.1f, extension.effectRadius);
            float radius = detonatedIndoors
                ? baseRadius * Mathf.Max(1f, extension.indoorRadiusFactor)
                : baseRadius;
            float distanceExponent = detonatedIndoors
                ? Mathf.Max(0.05f, extension.indoorDistanceExponent)
                : Mathf.Max(0.05f, extension.outdoorDistanceExponent);
            float environmentFactor = detonatedIndoors
                ? IndoorRoomFactor(blastRoom, extension)
                : Mathf.Max(0f, extension.outdoorFactor);

            List<Pawn> pawns = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
            foreach (Pawn pawn in pawns)
            {
                if (pawn == null
                    || pawn.Dead
                    || pawn.health == null
                    || pawn.RaceProps?.IsFlesh != true)
                {
                    continue;
                }

                float distance = center.DistanceTo(pawn.Position);
                if (distance > radius)
                {
                    continue;
                }

                float distanceProgress = 1f - Mathf.Clamp01(distance / radius);
                float distanceSeverity = Mathf.Lerp(
                    Mathf.Max(0f, extension.severityAtEdge),
                    Mathf.Max(0f, extension.severityAtCenter),
                    Mathf.Pow(distanceProgress, distanceExponent));

                bool directPath = pawn.Position == center
                    || GenSight.LineOfSight(center, pawn.Position, map, true);
                float pathFactor = directPath
                    ? 1f
                    : Mathf.Clamp01(extension.blockedPathFactor);

                float roomBoundaryFactor = 1f;
                if (detonatedIndoors && pawn.Position.GetRoom(map) != blastRoom)
                {
                    roomBoundaryFactor = Mathf.Clamp01(extension.differentRoomFactor);
                }

                float severityGain = distanceSeverity
                    * environmentFactor
                    * pathFactor
                    * roomBoundaryFactor;
                float previousSuppression = CurrentSuppression(
                    pawn,
                    extension.suppressionHediff);
                AddSuppression(pawn, extension.suppressionHediff, severityGain);
                TryApplyIntensityDamage(pawn, instigator, extension, severityGain);
                if (!pawn.Dead)
                {
                    TryApplyBriefStun(
                        pawn,
                        instigator,
                        extension,
                        severityGain,
                        previousSuppression);
                }
            }
        }

        private static float CurrentSuppression(Pawn pawn, HediffDef hediffDef)
        {
            Hediff suppression = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            return suppression == null
                ? 0f
                : Mathf.Clamp01(
                    suppression.Severity / Mathf.Max(0.001f, hediffDef.maxSeverity));
        }

        private static void TryApplyIntensityDamage(
            Pawn pawn,
            Thing instigator,
            FlashbangProjectileExtension extension,
            float intensity)
        {
            if (extension.intensityDamageDef == null
                || intensity < extension.damageThreshold)
            {
                return;
            }

            float threshold = Mathf.Max(0f, extension.damageThreshold);
            float fullIntensity = Mathf.Max(threshold + 0.01f, extension.damageFullIntensity);
            float damage = Mathf.Lerp(
                Mathf.Max(0f, extension.damageAtThreshold),
                Mathf.Max(0f, extension.damageAtFullIntensity),
                Mathf.InverseLerp(threshold, fullIntensity, intensity));
            if (damage <= 0.01f)
            {
                return;
            }

            pawn.TakeDamage(new DamageInfo(
                extension.intensityDamageDef,
                damage,
                Mathf.Max(0f, extension.damageArmorPenetration),
                -1f,
                instigator));
        }

        private static void TryApplyBriefStun(
            Pawn pawn,
            Thing instigator,
            FlashbangProjectileExtension extension,
            float intensity,
            float previousSuppression)
        {
            float threshold = Mathf.Max(0f, extension.stunThreshold);
            if (intensity < threshold || pawn.stances?.stunner == null)
            {
                return;
            }

            float fullIntensity = Mathf.Max(threshold + 0.01f, extension.stunFullIntensity);
            int minimumTicks = Mathf.Max(1, extension.stunTicksMin);
            int maximumTicks = Mathf.Max(minimumTicks, extension.stunTicksMax);
            int baseStunTicks = Mathf.RoundToInt(Mathf.Lerp(
                minimumTicks,
                maximumTicks,
                Mathf.InverseLerp(threshold, fullIntensity, intensity)));
            float suppressionProgress = Mathf.Pow(
                Mathf.Clamp01(previousSuppression),
                Mathf.Max(0.05f, extension.stunSuppressionExponent));
            float durationMultiplier = Mathf.Lerp(
                1f,
                Mathf.Max(1f, extension.stunSuppressionMultiplierMax),
                suppressionProgress);
            int absoluteMaximumTicks = Mathf.Max(
                maximumTicks,
                extension.stunTicksAbsoluteMax);
            int stunTicks = Mathf.Min(
                absoluteMaximumTicks,
                Mathf.RoundToInt(baseStunTicks * durationMultiplier));
            pawn.stances.stunner.StunFor(
                stunTicks,
                instigator ?? pawn,
                true,
                true,
                false);
        }

        private static float IndoorRoomFactor(
            Room room,
            FlashbangProjectileExtension extension)
        {
            int smallRoom = Mathf.Max(1, extension.smallRoomCellCount);
            int largeRoom = Mathf.Max(smallRoom + 1, extension.largeRoomCellCount);
            float roomSizeProgress = Mathf.InverseLerp(smallRoom, largeRoom, room.CellCount);
            return Mathf.Lerp(
                Mathf.Max(0f, extension.smallRoomFactor),
                Mathf.Max(0f, extension.largeRoomFactor),
                roomSizeProgress);
        }

        private static void AddSuppression(Pawn pawn, HediffDef hediffDef, float severityGain)
        {
            if (severityGain <= 0.001f)
            {
                return;
            }

            Hediff suppression = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (suppression == null)
            {
                suppression = HediffMaker.MakeHediff(hediffDef, pawn);
                suppression.Severity = Mathf.Min(severityGain, hediffDef.maxSeverity);
                pawn.health.AddHediff(suppression);
                return;
            }

            suppression.Severity = Mathf.Min(
                suppression.Severity + severityGain,
                hediffDef.maxSeverity);
        }
    }

    public sealed class Projectile_Flashbang : Projectile_ModernGrenade
    {
        protected override void Explode()
        {
            Map impactMap = Map;
            IntVec3 impactCell = Position;
            Thing instigator = Launcher;
            FlashbangProjectileExtension extension =
                def.GetModExtension<FlashbangProjectileExtension>();

            base.Explode();
            FlashbangUtility.ApplySuppression(
                impactCell,
                impactMap,
                extension,
                instigator);
            GrenadeExplosionEffectUtility.ThrowFlashbangEffects(impactCell, impactMap);
        }
    }
}
