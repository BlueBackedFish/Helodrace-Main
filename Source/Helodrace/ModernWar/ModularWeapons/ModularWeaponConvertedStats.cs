using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    /// <summary>Part authoring points, not hit probabilities. Physical values use metres/kg.</summary>
    public sealed class ModularWeaponInternalStats
    {
        public float massKg = -1f;
        public float recoilImpulse;
        public float aimSpeed;
        public float muzzleRise;
        public float operatingReliability;
        public float gasFlow;
        // Root-relative physical mass position, NOT a sprite/pivot offset.
        public float massPositionMeters;
        public float momentOfInertia;
        public float aimStability;
        public float mechanicalPrecision;
        // Cartridge consistency: 0 = unset, 1 = service-ammo reference.
        // Only the selected primary ammunition contributes; never stack selectors.
        public float ammunitionPrecisionFactor;
        public float identificationDistanceMeters;
        public float barrelLengthInches;
        public float muzzleVelocityOffsetMetersPerSecond;
        public float damageTransfer;
        public float penetrationEfficiency;
        public float rangeAdjustmentPercent;
        public float receiverLengthMeters;
        public float lengthExtensionMeters;
        // Root-only normal-quality ceilings at mechanicalPrecision = 60.
        public float precisionTouchCeiling;
        public float precisionShortCeiling;
        public float precisionMediumCeiling;
        public float precisionLongCeiling;
        // Root-only natural hold: a pistol's two-hand baseline is not a stockless rifle.
        public float naturalSupport;
        public float warmupReferenceCost;
        public float recoveryReferenceCost;
        public ModularWeaponInternalStats Clone() => (ModularWeaponInternalStats)MemberwiseClone();
    }

    public sealed class ModularWeaponAccuracyBand
    {
        public float MechanicalCeiling { get; internal set; }
        public float SightEfficiency { get; internal set; }
        public float HoldEfficiency { get; internal set; }
        public float Accuracy => MechanicalCeiling * SightEfficiency * HoldEfficiency;
    }

    /// <summary>Shared conversion contract for vanilla, modding UI and the CE adapter.</summary>
    public sealed class ModularWeaponConvertedStats
    {
        public float MassKg { get; internal set; }
        public float RecoilControl { get; internal set; }
        public float AimSpeed { get; internal set; }
        public float MuzzleControl { get; internal set; }
        public float OperatingReliability { get; internal set; }
        public float GasEfficiency { get; internal set; }
        public float CenterOfMassMeters { get; internal set; }
        public float Handling { get; internal set; }
        public float AimStability { get; internal set; }
        public float MechanicalPrecision { get; internal set; }
        public float AmmunitionPrecisionFactor { get; internal set; } = 1f;
        public float StockSupport { get; internal set; }
        public float StockExtension { get; internal set; }
        public bool HasStock { get; internal set; }
        public float HandlingLengthMeters { get; internal set; }
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
        public ModularWeaponAccuracyBand Touch { get; internal set; }
        public ModularWeaponAccuracyBand Short { get; internal set; }
        public ModularWeaponAccuracyBand Medium { get; internal set; }
        public ModularWeaponAccuracyBand Long { get; internal set; }

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
            public float mass, weightedPosition, recoil, aimSpeed, muzzleRise;
            public float reliability, gasFlow, localInertia, aimStability, mechanicalPrecision;
            public float identificationMeters, barrelLength, velocityOffset, damageTransfer, penetrationEfficiency;
            public float rangeAdjustment, lengthExtension, stockLength, stockSupport, stockExtension;
            public bool hasStock;
            public int projectilePriority = int.MinValue;
            public float ammunitionPrecision = 1f;
        }

        // Secondary launchers add weight but cannot improve the primary gun's precision,
        // barrel, gas system, muzzle control or ammunition.
        internal static bool IsSecondary(ModularRenderNode node, List<ModularRenderNode> nodes)
        {
            for (int depth = 0; node != null && depth < 32; depth++)
            {
                if (node.Props.isWeaponSubassembly) return true;
                CompModularWeaponNode parent = node.parentComp;
                node = nodes.Find(n => n.comp == parent);
            }
            return false;
        }

        private static float Mass(ModularRenderNode node) => node.Props.internalStats?.massKg > 0f
            ? node.Props.internalStats.massKg
            : Mathf.Max(0f, node.thing.def.GetStatValueAbstract(StatDefOf.Mass));

        private static float PhysicalPosition(ModularRenderNode node)
        {
            float position = node.Props.internalStats?.massPositionMeters ?? 0f;
            if (node.Props.stock != null)
                position -= node.Props.stock.Length(node.railOffset) * 0.5f;
            return position;
        }

        public static ModularWeaponConvertedStats Resolve(
            CompModularWeaponNode root, List<ModularRenderNode> nodes,
            Dictionary<int, float> performanceWeights, float gasTubeFlowSetting,
            ModularSightResolution sights = null)
        {
            Aggregate a = new Aggregate();
            if (nodes == null || nodes.Count == 0) return new ModularWeaponConvertedStats();
            ModularWeaponInternalStats rootStats = root?.Props.internalStats ?? new ModularWeaponInternalStats();
            foreach (ModularRenderNode node in nodes)
            {
                var props = node?.Props;
                if (props == null || node.thing?.def == null) continue;
                var raw = props.internalStats;
                float mass = Mass(node);
                a.mass += mass;
                a.weightedPosition += mass * PhysicalPosition(node);
                a.localInertia += Mathf.Max(0f, raw?.momentOfInertia ?? 0f);
                bool secondary = IsSecondary(node, nodes);
                bool ammunition = !props.ammunitionType.NullOrEmpty();
                // Match the projectile resolver's priority/tie order, including a
                // non-ammo override winning and therefore providing no cartridge bonus.
                if (!secondary && props.projectileOverride != null && props.overridePriority >= a.projectilePriority)
                {
                    a.projectilePriority = props.overridePriority;
                    a.ammunitionPrecision = ammunition && raw?.ammunitionPrecisionFactor > 0f
                        ? Mathf.Clamp(raw.ammunitionPrecisionFactor, 0.5f, 1.35f) : 1f;
                }
                if (raw == null || secondary) continue;

                float weight = performanceWeights != null
                    && performanceWeights.TryGetValue(node.thing.thingIDNumber, out float resolved)
                    ? resolved : 1f;
                bool optic = props.sight != null && !props.sight.integrated;
                if (ModularWeaponPartPerformance.IsForegrip(node)
                    && ModularWeaponPartPerformance.MountSurface(node, nodes) == ModularRailSurface.Top)
                {
                    a.aimSpeed -= 10f;
                    a.aimStability -= 8f;
                }
                // Hardware costs remain for blocked/duplicate optics. Positive optic
                // accuracy/acquisition fields cannot create a second benefit channel.
                if (!ammunition)
                    a.aimSpeed += optic ? Mathf.Min(0f, raw.aimSpeed) : raw.aimSpeed * weight;
                if (optic) continue;
                if (!ammunition)
                {
                    a.aimStability += raw.aimStability * weight;
                    a.mechanicalPrecision += raw.mechanicalPrecision * weight;
                }
                a.reliability += raw.operatingReliability * weight;
                a.gasFlow += raw.gasFlow * weight;
                a.rangeAdjustment += raw.rangeAdjustmentPercent * weight;
                a.identificationMeters += raw.identificationDistanceMeters * weight;
                a.barrelLength = Mathf.Max(a.barrelLength, raw.barrelLengthInches);
                a.velocityOffset += raw.muzzleVelocityOffsetMetersPerSecond;
                a.lengthExtension += Mathf.Max(0f, raw.lengthExtensionMeters);
                if (props.isAssemblyRoot || raw.barrelLengthInches <= 0f)
                {
                    a.damageTransfer += raw.damageTransfer;
                    a.penetrationEfficiency += raw.penetrationEfficiency;
                }
                if (props.stock == null)
                {
                    a.recoil += raw.recoilImpulse * weight;
                    a.muzzleRise += raw.muzzleRise * weight;
                }
                else
                {
                    float support = props.stock.Support(node.railOffset);
                    if (!a.hasStock || support > a.stockSupport)
                    {
                        a.stockSupport = support;
                        a.stockExtension = props.stock.Extension(node.railOffset);
                    }
                    a.hasStock = true;
                    a.stockLength = Mathf.Max(a.stockLength, props.stock.Length(node.railOffset));
                }
            }

            float center = a.mass > 0.0001f ? a.weightedPosition / a.mass : 0f;
            float inertia = a.localInertia;
            foreach (ModularRenderNode node in nodes)
                if (node?.Props != null && node.thing?.def != null)
                {
                    float arm = PhysicalPosition(node) - center;
                    inertia += Mass(node) * arm * arm;
                }
            float balance = Mathf.Clamp01(Mathf.Abs(center + 0.03f) / 0.32f);
            float length = Mathf.Max(0.1f, rootStats.receiverLengthMeters + a.barrelLength * 0.0254f
                + a.stockLength + a.lengthExtension);
            float support01 = Mathf.Max(Mathf.Clamp01(rootStats.naturalSupport), a.stockSupport);
            float speed01 = Mathf.Clamp01(a.aimSpeed / 100f);
            float precision01 = Mathf.Clamp01(a.mechanicalPrecision / 100f);
            float stability01 = Mathf.Clamp01(a.aimStability / 100f + (support01 - 0.65f) * 0.40f);
            float reliability01 = Mathf.Clamp(a.reliability, 0.35f, 0.9995f);
            gasTubeFlowSetting = Mathf.Clamp(gasTubeFlowSetting,
                ModularWeaponGasSystemUtility.MinimumSetting, ModularWeaponGasSystemUtility.MaximumSetting);
            float gasRecoil = Mathf.Clamp(1f + (gasTubeFlowSetting - 1f) * 0.4f, 0.85f, 1.15f);
            float gasEfficiency = Mathf.Clamp(a.gasFlow * gasTubeFlowSetting, 0.45f, 1.35f);
            // Support changes control, never the cartridge/gas impulse itself.
            float recoil = a.recoil * gasRecoil * (1f - support01 * 0.18f);
            float rise = a.muzzleRise * Mathf.Clamp(1f + (gasTubeFlowSetting - 1f) * 0.28f, 0.88f, 1.12f)
                * (1f - support01 * 0.25f);
            float massDamping = Mathf.Pow(Mathf.Max(0.5f, a.mass), 0.18f);
            float recoilControl = Mathf.Clamp01(1f - recoil / (11f * massDamping));
            float muzzleControl = Mathf.Clamp01(1f - rise / (9f * massDamping));
            float physicalCost = Mathf.Max(0f, a.mass - 0.5f) * 0.045f
                + inertia / 0.35f + balance * 0.10f + length * 1.40f;
            float handling = 1f / (1f + physicalCost);
            var sight = (sights ?? ModularWeaponSightResolver.Resolve(root, nodes)).performance;
            var result = new ModularWeaponConvertedStats
            {
                MassKg = a.mass, AimSpeed = speed01, AimStability = stability01,
                MechanicalPrecision = precision01, RecoilControl = recoilControl, MuzzleControl = muzzleControl,
                AmmunitionPrecisionFactor = a.ammunitionPrecision,
                OperatingReliability = reliability01, GasEfficiency = gasEfficiency,
                CenterOfMassMeters = center, Handling = handling, HandlingLengthMeters = length,
                HasStock = a.hasStock, StockSupport = a.stockSupport, StockExtension = a.stockExtension,
                IdentificationDistanceCells = Mathf.Clamp(a.identificationMeters / 10f, 8f, 80f),
                GasTubeFlowSetting = gasTubeFlowSetting, GasRecoilMultiplier = gasRecoil,
                BarrelLengthInches = a.barrelLength, MuzzleVelocityOffsetMetersPerSecond = a.velocityOffset,
                DamageTransferFactor = Mathf.Clamp(1f + a.damageTransfer / 100f, 0.25f, 2f),
                PenetrationEfficiencyFactor = Mathf.Clamp(1f + a.penetrationEfficiency / 100f, 0.25f, 2f)
            };
            result.MechanicalRangeFactor = Mathf.Clamp(1f + a.rangeAdjustment / 100f, 0.65f, 1.35f);
            result.RangeFactor = Mathf.Clamp(result.MechanicalRangeFactor * sight.Range, 0.5f, 1.5f);
            result.VanillaWarmupFactor = Mathf.Clamp(
                (1.32f - speed01 * 0.48f + physicalCost * 0.30f) * sight.Warmup
                    / (rootStats.warmupReferenceCost > 0f ? rootStats.warmupReferenceCost : 1f), 0.65f, 2.2f);
            // No aimSpeed, sight magnification or mechanical cycle multiplier here.
            result.VanillaCooldownFactor = Mathf.Clamp(
                (1.18f - recoilControl * 0.20f - muzzleControl * 0.18f
                + (1f - support01) * 0.12f + inertia * 0.08f + balance * 0.025f)
                    / (rootStats.recoveryReferenceCost > 0f ? rootStats.recoveryReferenceCost : 1f), 0.72f, 1.55f);
            result.VanillaCycleFactor = Mathf.Clamp(1f + (0.97f - reliability01) * 1.8f
                + Mathf.Max(0f, 1f - gasEfficiency) * 0.65f
                - Mathf.Max(0f, gasEfficiency - 1f) * 0.65f, 0.78f, 1.55f);
            result.Touch = Band(rootStats.precisionTouchCeiling, 0.68f, a.mechanicalPrecision, 0.0004f, sight.Touch, stability01, 0.06f, a.ammunitionPrecision, 0.15f);
            result.Short = Band(rootStats.precisionShortCeiling, 0.78f, a.mechanicalPrecision, 0.0009f, sight.Short, stability01, 0.12f, a.ammunitionPrecision, 0.40f);
            result.Medium = Band(rootStats.precisionMediumCeiling, 0.80f, a.mechanicalPrecision, 0.0018f, sight.Medium, stability01, 0.22f, a.ammunitionPrecision, 0.75f);
            result.Long = Band(rootStats.precisionLongCeiling, 0.76f, a.mechanicalPrecision, 0.0030f, sight.Long, stability01, 0.38f, a.ammunitionPrecision, 1f);
            result.VanillaAccuracyTouchFactor = result.Touch.Accuracy / BaseAccuracy(root, "AccuracyTouch", 0.6f);
            result.VanillaAccuracyShortFactor = result.Short.Accuracy / BaseAccuracy(root, "AccuracyShort", 0.7f);
            result.VanillaAccuracyMediumFactor = result.Medium.Accuracy / BaseAccuracy(root, "AccuracyMedium", 0.65f);
            result.VanillaAccuracyLongFactor = result.Long.Accuracy / BaseAccuracy(root, "AccuracyLong", 0.55f);
            result.CERecoil = Mathf.Clamp(recoil * (1f + balance * 0.35f) / massDamping, 0.15f, 8f);
            result.CEBulk = Mathf.Clamp(a.mass * 1.75f + inertia * 7.5f + length * 2f, 1f, 30f);
            result.CESwayFactor = Mathf.Clamp(0.45f + (1f - stability01) * 1.35f, 0.45f, 1.8f);
            result.CEShotSpread = Mathf.Clamp((0.025f + (1f - precision01) * 0.14f) / a.ammunitionPrecision, 0.02f, 0.24f);
            result.CESightsEfficiency = Mathf.Clamp((sight.Medium + sight.Long) * 0.5f, 0.5f, 1.25f);
            return result;
        }

        private static ModularWeaponAccuracyBand Band(float ceiling, float fallback, float precision,
            float sensitivity, float sight, float stability, float holdSensitivity, float ammunitionPrecision, float ammunitionInfluence)
            => new ModularWeaponAccuracyBand
            {
                MechanicalCeiling = Mathf.Clamp((ceiling > 0f ? ceiling : fallback)
                    * (1f + (precision - 60f) * sensitivity)
                    * Mathf.Lerp(1f, ammunitionPrecision, ammunitionInfluence), 0.01f, 0.97f),
                SightEfficiency = Mathf.Clamp01(sight),
                HoldEfficiency = Mathf.Clamp01(1f - (1f - stability) * holdSensitivity)
            };

        private static float BaseAccuracy(CompModularWeaponNode root, string name, float fallback)
        {
            var bases = root?.parent?.def?.statBases;
            if (bases != null)
                foreach (var value in bases)
                    if (value.stat?.defName == name) return Mathf.Max(0.01f, value.value);
            return fallback;
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
