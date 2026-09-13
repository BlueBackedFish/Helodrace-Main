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

        public float VanillaWarmupFactor { get; internal set; } = 1f;
        public float VanillaCycleFactor { get; internal set; } = 1f;
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
        }

        public static ModularWeaponConvertedStats Resolve(
            CompModularWeaponNode root,
            List<ModularRenderNode> nodes,
            Dictionary<int, float> sightWeights,
            float gasTubeFlowSetting)
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

                if (authored == null) continue;
                float performanceWeight = 1f;
                if (props.sight != null
                    && (sightWeights == null
                        || !sightWeights.TryGetValue(thing.thingIDNumber, out performanceWeight)))
                {
                    performanceWeight = 0f;
                }

                aggregate.recoil += authored.recoilImpulse;
                aggregate.ergonomics += authored.ergonomics;
                aggregate.muzzleRise += authored.muzzleRise;
                aggregate.reliability += authored.operatingReliability;
                aggregate.gasFlow += authored.gasFlow;
                aggregate.localInertia += Mathf.Max(0f, authored.momentOfInertia);
                aggregate.targetAcquisition += authored.targetAcquisition * performanceWeight;
                aggregate.aimingPrecision += authored.aimingPrecision * performanceWeight;
                aggregate.identificationMeters += authored.identificationDistanceMeters
                    * performanceWeight;
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

            // A root without authored data stays valid for old saves/Defs.
            float recoil = aggregate.recoil > 0f ? aggregate.recoil : 4.2f;
            float ergonomics = aggregate.ergonomics > 0f ? aggregate.ergonomics : 55f;
            float muzzleRise = aggregate.muzzleRise > 0f ? aggregate.muzzleRise : 3.2f;
            float reliability = aggregate.reliability > 0f ? aggregate.reliability : 0.96f;
            float gasFlow = aggregate.gasFlow > 0f ? aggregate.gasFlow : 1f;
            float targetAcquisition = aggregate.targetAcquisition > 0f
                ? aggregate.targetAcquisition : 55f;
            float aimingPrecision = aggregate.aimingPrecision > 0f
                ? aggregate.aimingPrecision : 60f;
            float identificationMeters = aggregate.identificationMeters > 0f
                ? aggregate.identificationMeters : 280f;

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
            float handling = 1f / (1f + Mathf.Max(0f, inertia) / 0.18f);
            float balancePenalty = Mathf.Clamp01(Mathf.Abs(center + 0.07f) / 0.32f);
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
                GasRecoilMultiplier = gasRecoilMultiplier
            };

            result.VanillaWarmupFactor = Mathf.Clamp(
                1.47f
                    - ergonomics01 * 0.38f
                    - acquisition01 * 0.24f
                    - handling * 0.13f
                    + balancePenalty * 0.18f,
                0.62f,
                1.55f);
            result.VanillaCycleFactor = Mathf.Clamp(
                1f + (0.97f - reliability01) * 1.8f
                    + Mathf.Max(0f, 1f - gasEfficiency) * 0.65f
                    - Mathf.Max(0f, gasEfficiency - 1f) * 0.65f,
                0.78f,
                1.55f);
            result.VanillaAccuracyTouchFactor = Mathf.Clamp(
                0.76f + muzzleControl * 0.22f + recoilControl * 0.18f,
                0.65f,
                1.22f);
            result.VanillaAccuracyShortFactor = Mathf.Clamp(
                0.72f + precision01 * 0.25f + recoilControl * 0.14f
                    + handling * 0.08f,
                0.62f,
                1.24f);
            result.VanillaAccuracyMediumFactor = Mathf.Clamp(
                0.67f + precision01 * 0.34f + handling * 0.08f
                    + acquisition01 * 0.07f,
                0.58f,
                1.28f);
            float identificationFactor = Mathf.Clamp(identificationCells / 30f, 0.65f, 1.35f);
            result.VanillaAccuracyLongFactor = Mathf.Clamp(
                (0.60f + precision01 * 0.38f + handling * 0.07f)
                    * identificationFactor,
                0.52f,
                1.35f);

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
