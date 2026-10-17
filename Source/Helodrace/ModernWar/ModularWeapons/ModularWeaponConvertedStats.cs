using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    /// <summary>
    /// Authoring-space contribution stored on each modular part.  These values
    /// are deliberately not StatDefs: players see only the converted game
    /// values, while vanilla and CE can consume the same physical model.
    /// </summary>
    public sealed class ModularWeaponInternalStats
    {
        // Negative/zero mass means: use the part ThingDef's abstract Mass.
        public float massKg = -1f;
        public float recoilImpulse;
        public float ergonomics;
        public float muzzleRise;
        public float operatingReliability;
        public float gasFlow;
        public float centerOfMassOffsetMeters;
        public float momentOfInertia;
        public float targetAcquisition;
        public float aimingPrecision;
        public float identificationDistanceMeters;
        // Absolute length on the barrel; additive velocity and efficiency points
        // on functional parts. Zero means no contribution (100 points = +100%).
        public float barrelLengthInches;
        public float muzzleVelocityOffsetMetersPerSecond;
        public float damageTransfer;
        public float penetrationEfficiency;
        public float rangeAdjustmentPercent;
    }

    /// <summary>
    /// Stable public conversion contract.  The main assembly applies the
    /// Vanilla fields; a CE compatibility assembly can consume the CE fields
    /// without reinterpreting attachment definitions.
    /// </summary>
    public sealed class ModularWeaponConvertedStats
    {
        public float MassKg { get; internal set; }
        public float RecoilControl { get; internal set; }
        public float Ergonomics { get; internal set; }
        public float MuzzleControl { get; internal set; }
        public float OperatingReliability { get; internal set; }
        public float GasEfficiency { get; internal set; }
        public float CenterOfMassMeters { get; internal set; }
        public float Handling { get; internal set; }
        public float TargetAcquisition { get; internal set; }
        public float AimingPrecision { get; internal set; }
        public float IdentificationDistanceCells { get; internal set; }
        public float GasTubeFlowSetting { get; internal set; } = 1f;
        public float GasRecoilMultiplier { get; internal set; } = 1f;
        public float BarrelLengthInches { get; internal set; }
        public float MuzzleVelocityOffsetMetersPerSecond { get; internal set; }
        public float DamageTransferFactor { get; internal set; } = 1f;
        public float PenetrationEfficiencyFactor { get; internal set; } = 1f;
        public ModularWeaponBallisticProfile Ballistics { get; internal set; }
        public float MechanicalRangeFactor { get; internal set; } = 1f;
        public float RangeFactor { get; internal set; } = 1f;

        public float VanillaWarmupFactor { get; internal set; } = 1f;
        public float VanillaCycleFactor { get; internal set; } = 1f;
        public float VanillaCooldownFactor { get; internal set; } = 1f;
        public float VanillaAccuracyTouchFactor { get; internal set; } = 1f;
        public float VanillaAccuracyShortFactor { get; internal set; } = 1f;
        public float VanillaAccuracyMediumFactor { get; internal set; } = 1f;
        public float VanillaAccuracyLongFactor { get; internal set; } = 1f;

        public float CERecoil { get; internal set; }
        public float CEBulk { get; internal set; }
        public float CESwayFactor { get; internal set; } = 1f;
        public float CEShotSpread { get; internal set; }
        public float CESightsEfficiency { get; internal set; } = 1f;
    }

    internal static class ModularWeaponStatConverter
    {
        private sealed class Aggregate
        {
            public float mass;
            public float weightedPosition;
            public float recoil;
            public float ergonomics;
            public float muzzleRise;
            public float reliability;
            public float gasFlow;
            public float localInertia;
            public float targetAcquisition;
            public float aimingPrecision;
            public float identificationMeters;
            public float barrelLength, velocityOffset, damageTransfer, penetrationEfficiency;
            public float rangeAdjustment;
        }

        public static ModularWeaponConvertedStats Resolve(
            CompModularWeaponNode root,
            List<ModularRenderNode> nodes,
            Dictionary<int, float> performanceWeights,
            float gasTubeFlowSetting,
            ModularSightResolution sights = null)
        {
            Aggregate aggregate = new Aggregate();
            if (nodes == null || nodes.Count == 0)
            {
                return new ModularWeaponConvertedStats();
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                CompProperties_ModularWeaponNode props = node?.Props;
                Thing thing = node?.thing;
                if (props == null || thing?.def == null) continue;

                ModularWeaponInternalStats authored = props.internalStats;
                float mass = authored != null && authored.massKg > 0f
                    ? authored.massKg
                    : Mathf.Max(0f, thing.def.GetStatValueAbstract(StatDefOf.Mass));
                float position = node.transform.position.x
                    + (authored?.centerOfMassOffsetMeters ?? 0f);
                aggregate.mass += mass;
                aggregate.weightedPosition += mass * position;

                if (ModularWeaponPartPerformance.IsForegrip(node)
                    && ModularWeaponPartPerformance.MountSurface(node, nodes)
                        == ModularRailSurface.Top)
                {
                    aggregate.ergonomics -= 8f;
                    aggregate.targetAcquisition -= 12f;
                    aggregate.aimingPrecision -= 8f;
                }

                if (authored == null) continue;
                // Ballistic contributions are mechanical, never sight-efficiency
                // weighted. Optical accessories cannot increase terminal ballistics.
                if (props.sight == null || props.sight.integrated)
                {
                    aggregate.barrelLength = Mathf.Max(aggregate.barrelLength, authored.barrelLengthInches);
                    aggregate.velocityOffset += authored.muzzleVelocityOffsetMetersPerSecond;
                    // Barrel-specific terminal effects belong to fragmentation,
                    // not a second direct-damage/AP bonus in the authoring fields.
                    if (props.isAssemblyRoot || authored.barrelLengthInches <= 0f)
                    {
                        aggregate.damageTransfer += authored.damageTransfer;
                        aggregate.penetrationEfficiency += authored.penetrationEfficiency;
                    }
                }
                float performanceWeight = performanceWeights != null
                    && performanceWeights.TryGetValue(
                        thing.thingIDNumber, out float resolvedWeight)
                    ? resolvedWeight : 1f;
                // Sight benefits are resolved once per complete optical group.
                // Every installed optic still carries its mass/handling costs.
                bool opticalAccessory = props.sight != null && !props.sight.integrated;
                if (opticalAccessory) performanceWeight = 1f;

                aggregate.recoil += authored.recoilImpulse * performanceWeight;
                aggregate.ergonomics += authored.ergonomics * performanceWeight;
                aggregate.muzzleRise += authored.muzzleRise * performanceWeight;
                aggregate.reliability += authored.operatingReliability * performanceWeight;
                aggregate.gasFlow += authored.gasFlow * performanceWeight;
                aggregate.localInertia += Mathf.Max(0f, authored.momentOfInertia);
                if (!opticalAccessory)
                {
                    aggregate.rangeAdjustment += authored.rangeAdjustmentPercent * performanceWeight;
                    aggregate.targetAcquisition += authored.targetAcquisition * performanceWeight;
                    aggregate.aimingPrecision += authored.aimingPrecision * performanceWeight;
                    aggregate.identificationMeters += authored.identificationDistanceMeters
                        * performanceWeight;
                }
            }

            float center = aggregate.mass > 0.0001f
                ? aggregate.weightedPosition / aggregate.mass
                : 0f;
            float inertia = aggregate.localInertia;
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                CompProperties_ModularWeaponNode props = node?.Props;
                Thing thing = node?.thing;
                if (props == null || thing?.def == null) continue;
                ModularWeaponInternalStats authored = props.internalStats;
                float mass = authored != null && authored.massKg > 0f
                    ? authored.massKg
                    : Mathf.Max(0f, thing.def.GetStatValueAbstract(StatDefOf.Mass));
                float position = node.transform.position.x
                    + (authored?.centerOfMassOffsetMeters ?? 0f);
                float arm = position - center;
                inertia += mass * arm * arm;
            }

            float balancePenalty = Mathf.Clamp01(
                Mathf.Abs(center + 0.07f) / 0.32f);

            // Supply a baseline only when the root omits that field. This keeps
            // accessory bonuses and penalties effective on older weapon Defs.
            ModularWeaponInternalStats rootStats = root?.Props.internalStats;
            float recoil = aggregate.recoil
                + (rootStats?.recoilImpulse > 0f ? 0f : 4.2f);
            float ergonomics = aggregate.ergonomics
                + (rootStats?.ergonomics > 0f ? 0f : 55f);
            // Weight and the resulting center of mass affect the displayed
            // ergonomics directly, regardless of a part's special bonuses.
            ergonomics -= Mathf.Max(0f, aggregate.mass - 0.5f) * 3.5f
                + balancePenalty * 12f;
            float muzzleRise = aggregate.muzzleRise
                + (rootStats?.muzzleRise > 0f ? 0f : 3.2f);
            float reliability = aggregate.reliability
                + (rootStats?.operatingReliability > 0f ? 0f : 0.96f);
            float gasFlow = aggregate.gasFlow
                + (rootStats?.gasFlow > 0f ? 0f : 1f);
            float targetAcquisition = aggregate.targetAcquisition
                + (rootStats?.targetAcquisition > 0f ? 0f : 55f);
            targetAcquisition -= balancePenalty * 7f;
            float aimingPrecision = aggregate.aimingPrecision
                + (rootStats?.aimingPrecision > 0f ? 0f : 60f);
            float identificationMeters = aggregate.identificationMeters
                + (rootStats?.identificationDistanceMeters > 0f ? 0f : 280f);

            gasTubeFlowSetting = Mathf.Clamp(
                gasTubeFlowSetting,
                ModularWeaponGasSystemUtility.MinimumSetting,
                ModularWeaponGasSystemUtility.MaximumSetting);
            float gasRecoilMultiplier = Mathf.Clamp(
                1f + (gasTubeFlowSetting - 1f) * 0.4f,
                0.85f,
                1.15f);
            recoil *= gasRecoilMultiplier;
            muzzleRise *= Mathf.Clamp(
                1f + (gasTubeFlowSetting - 1f) * 0.28f,
                0.88f,
                1.12f);
            gasFlow *= gasTubeFlowSetting;

            float ergonomics01 = Mathf.Clamp01(ergonomics / 100f);
            float acquisition01 = Mathf.Clamp01(targetAcquisition / 100f);
            float precision01 = Mathf.Clamp01(aimingPrecision / 100f);
            float recoilControl = Mathf.Clamp01(1f - recoil / 11f);
            float muzzleControl = Mathf.Clamp01(1f - muzzleRise / 9f);
            float reliability01 = Mathf.Clamp(reliability, 0.35f, 0.9995f);
            float gasEfficiency = Mathf.Clamp(gasFlow, 0.45f, 1.35f);
            float handling = 1f / (1f + Mathf.Max(0f, inertia) / 0.18f
                + Mathf.Max(0f, aggregate.mass - 0.5f) * 0.05f
                + balancePenalty * 0.25f);
            float identificationCells = Mathf.Clamp(identificationMeters / 10f, 8f, 80f);

            ModularWeaponConvertedStats result = new ModularWeaponConvertedStats
            {
                MassKg = aggregate.mass,
                RecoilControl = recoilControl,
                Ergonomics = ergonomics01,
                MuzzleControl = muzzleControl,
                OperatingReliability = reliability01,
                GasEfficiency = gasEfficiency,
                CenterOfMassMeters = center,
                Handling = handling,
                TargetAcquisition = acquisition01,
                AimingPrecision = precision01,
                IdentificationDistanceCells = identificationCells,
                GasTubeFlowSetting = gasTubeFlowSetting,
                GasRecoilMultiplier = gasRecoilMultiplier,
                BarrelLengthInches = aggregate.barrelLength,
                MuzzleVelocityOffsetMetersPerSecond = aggregate.velocityOffset,
                DamageTransferFactor = Mathf.Clamp(1f + aggregate.damageTransfer / 100f, 0.25f, 2f),
                PenetrationEfficiencyFactor = Mathf.Clamp(1f + aggregate.penetrationEfficiency / 100f, 0.25f, 2f)
            };
            result.MechanicalRangeFactor = Mathf.Clamp(1f + aggregate.rangeAdjustment / 100f, 0.65f, 1.35f);

            result.VanillaWarmupFactor = Mathf.Clamp(
                1.47f
                    - ergonomics01 * 0.38f
                    - acquisition01 * 0.24f
                    - handling * 0.13f
                    + balancePenalty * 0.18f,
                0.62f,
                1.55f);
            // Recovery is affected by control and handling, independently from
            // the mechanical cycle/RPM. Better parts can improve this baseline.
            result.VanillaCooldownFactor = Mathf.Clamp(
                1.32f - recoilControl * 0.18f - muzzleControl * 0.14f
                    - ergonomics01 * 0.10f - handling * 0.10f
                    + balancePenalty * 0.06f,
                0.70f, 1.55f);
            result.VanillaCycleFactor = Mathf.Clamp(
                1f + (0.97f - reliability01) * 1.8f
                    + Mathf.Max(0f, 1f - gasEfficiency) * 0.65f
                    - Mathf.Max(0f, gasEfficiency - 1f) * 0.65f,
                0.78f,
                1.55f);
            result.VanillaAccuracyTouchFactor = Mathf.Clamp(
                0.78f + muzzleControl * 0.16f + recoilControl * 0.12f,
                0.70f,
                1.08f);
            result.VanillaAccuracyShortFactor = Mathf.Clamp(
                0.78f + precision01 * 0.18f + recoilControl * 0.10f
                    + handling * 0.05f,
                0.70f,
                1.08f);
            result.VanillaAccuracyMediumFactor = Mathf.Clamp(
                0.77f + precision01 * 0.23f + handling * 0.04f
                    + acquisition01 * 0.04f,
                0.65f,
                1.08f);
            result.VanillaAccuracyLongFactor = Mathf.Clamp(
                0.75f + precision01 * 0.25f + handling * 0.04f,
                0.60f,
                1.08f);

            ModularWeaponSightPerformance sightPerformance =
                (sights ?? ModularWeaponSightResolver.Resolve(root, nodes)).performance;
            result.VanillaWarmupFactor *= sightPerformance.Warmup;
            result.VanillaAccuracyTouchFactor *= sightPerformance.Touch;
            result.VanillaAccuracyShortFactor *= sightPerformance.Short;
            result.VanillaAccuracyMediumFactor *= sightPerformance.Medium;
            result.VanillaAccuracyLongFactor *= sightPerformance.Long;
            result.RangeFactor = Mathf.Clamp(result.MechanicalRangeFactor * sightPerformance.Range, 0.5f, 1.5f);

            // CE-ready values are generated now but applied only by the CE
            // compatibility assembly, avoiding any hard dependency here.
            float safeMass = Mathf.Max(0.5f, result.MassKg);
            result.CERecoil = Mathf.Clamp(
                recoil * (1f + balancePenalty * 0.35f) / Mathf.Pow(safeMass, 0.32f),
                0.15f,
                8f);
            result.CEBulk = Mathf.Clamp(
                safeMass * 1.75f + inertia * 7.5f + balancePenalty * 1.5f,
                1f,
                30f);
            result.CESwayFactor = Mathf.Clamp(
                result.VanillaWarmupFactor * (1.08f - handling * 0.16f),
                0.45f,
                1.8f);
            result.CEShotSpread = Mathf.Clamp(
                0.025f + (1f - precision01) * 0.14f
                    + (1f - muzzleControl) * 0.035f,
                0.02f,
                0.24f);
            result.CESightsEfficiency = Mathf.Clamp(
                0.55f + acquisition01 * 0.38f + precision01 * 0.22f,
                0.5f,
                1.25f);
            return result;
        }

        public static void ApplyVanillaFactors(
            ModularWeaponConvertedStats converted,
            Dictionary<StatDef, float> offsets,
            Dictionary<StatDef, float> factors,
            ThingDef rootDef)
        {
            if (converted == null || rootDef == null) return;
            float rootMass = Mathf.Max(0f, rootDef.GetStatValueAbstract(StatDefOf.Mass));
            Add(offsets, StatDefOf.Mass, converted.MassKg - rootMass);
            Multiply(factors, StatDefOf.RangedWeapon_WarmupMultiplier,
                converted.VanillaWarmupFactor);
            Multiply(factors, StatDefOf.RangedWeapon_RangeMultiplier, converted.RangeFactor);
            // CE performs angular dispersion through ShiftVecReport. Leaving the
            // vanilla range-band accuracy factors active in a CE session makes the
            // authoring system appear to use two independent hit models.
            if (ModsConfig.IsActive("ceteam.combatextended")) return;
            Multiply(factors, StatDefOf.AccuracyTouch,
                converted.VanillaAccuracyTouchFactor);
            Multiply(factors, StatDefOf.AccuracyShort,
                converted.VanillaAccuracyShortFactor);
            Multiply(factors, StatDefOf.AccuracyMedium,
                converted.VanillaAccuracyMediumFactor);
            Multiply(factors, StatDefOf.AccuracyLong,
                converted.VanillaAccuracyLongFactor);
        }

        private static void Add(
            Dictionary<StatDef, float> values,
            StatDef stat,
            float amount)
        {
            if (values == null || stat == null || Mathf.Approximately(amount, 0f)) return;
            values.TryGetValue(stat, out float current);
            values[stat] = current + amount;
        }

        private static void Multiply(
            Dictionary<StatDef, float> values,
            StatDef stat,
            float factor)
        {
            if (values == null || stat == null || Mathf.Approximately(factor, 1f)) return;
            if (!values.TryGetValue(stat, out float current)) current = 1f;
            values[stat] = current * factor;
        }
    }
}
