using System;
using System.Collections.Generic;
using Helodrace.ModernWar;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    // Authoring/balance inputs, deliberately independent from projectile render speed.
    public sealed class BulletBallisticsExtension : DefModExtension
    {
        public float projectileMassGrams = 4f;
        public float referenceVelocityMetersPerSecond = 900f;
        public float referenceBarrelInches = 16f;
        public float barrelVelocityExponent = 0.35f;
        public float velocityHalfDistance = 55f;
        // Protection-system inputs (Docs/Ballistic_Protection_System.md). Zero diameter keeps the vanilla armor roll.
        public float diameterMillimeters;
        public Helodrace.Ballistics.PenetratorClass penetratorClass;
        // Vehicle armour (design doc 4.2, phase 5): KineticBullet = small-arms model, KineticHardCore = AP shot
        // (De Marre), KineticLongRod = Lanz-Odermatt with the penetrator material's sourced fit.
        public Helodrace.Ballistics.PenetrationMechanism mechanism = Helodrace.Ballistics.PenetrationMechanism.KineticBullet;
        public float penetratorLengthMillimeters;
        public Helodrace.Ballistics.ArmorMaterialDef penetratorMaterial;

        public bool HasPhysics => diameterMillimeters > 0f;

        public override IEnumerable<string> ConfigErrors()
        {
            if (!(projectileMassGrams > 0f)) yield return "Ballistic projectile mass must be positive.";
            if (!(referenceVelocityMetersPerSecond > 0f)) yield return "Ballistic reference velocity must be positive.";
            if (!(referenceBarrelInches > 0f)) yield return "Ballistic reference barrel must be positive.";
            if (!(velocityHalfDistance > 0f)) yield return "Ballistic velocity half-distance must be positive.";
            if (!(barrelVelocityExponent >= 0f && barrelVelocityExponent <= 0.5f))
                yield return "Ballistic barrel-velocity exponent must be between zero and 0.5.";
            if (diameterMillimeters < 0f) yield return "Ballistic penetrator diameter must not be negative.";
            if (mechanism == Helodrace.Ballistics.PenetrationMechanism.KineticLongRod &&
                (penetratorMaterial?.longRodPerforation == null || penetratorLengthMillimeters <= 0f))
                yield return "Long-rod rounds need penetratorLengthMillimeters and a penetratorMaterial with longRodPerforation.";
        }
    }

    public static class BulletBallisticsRules
    {
        public static float BarrelVelocityFactor(float barrel, float reference, float exponent)
        {
            reference = Math.Max(1f, reference);
            if (barrel <= 0f) barrel = reference;
            return (float)Math.Pow(Math.Max(0.25f, Math.Min(2f, barrel / reference)),
                Math.Max(0f, Math.Min(0.5f, exponent)));
        }

        public static float RetainedVelocity(float muzzleFactor, float distance, float halfDistance)
            => Math.Max(0f, muzzleFactor) * (float)Math.Pow(0.5,
                Math.Max(0f, distance) / Math.Max(1f, halfDistance));

        public static float Energy(float massGrams, float velocityMetersPerSecond)
            => Math.Max(0f, massGrams) * 0.0005f * velocityMetersPerSecond * velocityMetersPerSecond;

        // Primary damage/AP use only the cartridge's distance retention, never
        // barrel-derived muzzle velocity. That velocity affects fragmentation only.
        public static float DamageFactor(float velocityFactor, float transfer)
            => (float)Math.Pow(Math.Max(0f, velocityFactor), 0.7) * Math.Max(0f, transfer);

        public static float PenetrationFactor(float velocityFactor, float efficiency)
            => (float)Math.Pow(Math.Max(0f, velocityFactor), 1.2) * Math.Max(0f, efficiency);

        public static int Damage(int referenceDamage, float factor)
            => referenceDamage <= 0 ? 0 : Math.Max(1, Mathf.RoundToInt(referenceDamage * factor));
    }

    // Captured at launch, including quality/weapon modifiers. No reads of the live
    // weapon during flight; saving or changing parts cannot rewrite an existing shot.
    public sealed class BulletBallisticsSnapshot : IExposable
    {
        public int referenceDamage;
        public float referencePenetration, muzzleVelocityFactor = 1f, velocityHalfDistance = 55f;
        public float damageTransfer = 1f, penetrationEfficiency = 1f;

        public float VelocityAt(float distance) => BulletBallisticsRules.RetainedVelocity(
            muzzleVelocityFactor, distance, velocityHalfDistance);
        private float PrimaryRetentionAt(float distance) => BulletBallisticsRules.RetainedVelocity(
            1f, distance, velocityHalfDistance);
        public int DamageAt(float distance) => BulletBallisticsRules.Damage(referenceDamage,
            BulletBallisticsRules.DamageFactor(PrimaryRetentionAt(distance), damageTransfer));
        public float PenetrationAt(float distance) => referencePenetration
            * BulletBallisticsRules.PenetrationFactor(PrimaryRetentionAt(distance), penetrationEfficiency);

        public void ExposeData()
        {
            Scribe_Values.Look(ref referenceDamage, "referenceDamage");
            Scribe_Values.Look(ref referencePenetration, "referencePenetration");
            Scribe_Values.Look(ref muzzleVelocityFactor, "muzzleVelocityFactor", 1f);
            Scribe_Values.Look(ref velocityHalfDistance, "velocityHalfDistance", 55f);
            Scribe_Values.Look(ref damageTransfer, "damageTransfer", 1f);
            Scribe_Values.Look(ref penetrationEfficiency, "penetrationEfficiency", 1f);
        }
    }

    public class Projectile_BallisticBullet : Bullet
    {
        private BulletBallisticsSnapshot ballistics;
        protected float TravelDistance => SpatialMath.HorizontalLength(ExactPosition.ToNative() - origin.ToNative());
        protected internal float ImpactVelocityFactor => ballistics?.VelocityAt(TravelDistance)
            ?? BulletBallisticsRules.RetainedVelocity(1f, TravelDistance,
                def.GetModExtension<BulletBallisticsExtension>()?.velocityHalfDistance ?? 55f);

        public override int DamageAmount => ballistics?.DamageAt(TravelDistance) ?? base.DamageAmount;
        public override float ArmorPenetration => ballistics?.PenetrationAt(TravelDistance) ?? base.ArmorPenetration;

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false,
            Thing equipment = null, ThingDef targetCoverDef = null)
        {
            CompModularWeaponNode modular = equipment?.TryGetComp<CompModularWeaponNode>();
            ModularWeaponBallisticProfile profile = modular?.Props.isAssemblyRoot == true
                ? ModularWeaponBallistics.Resolve(def, modular.ConvertedStats) : null;
            ballistics = profile?.Snapshot(
                def.projectile.GetDamageAmount(equipment), def.projectile.GetArmorPenetration(equipment));
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref ballistics, "ballistics");
        }
    }
}
