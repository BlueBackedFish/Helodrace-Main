using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ModularWeaponBallisticProfile
    {
        public ThingDef Projectile { get; internal set; }
        public float ProjectileMassGrams { get; internal set; }
        public float ReferenceVelocityMetersPerSecond { get; internal set; }
        public float MuzzleVelocityMetersPerSecond { get; internal set; }
        public float MuzzleEnergyJoules { get; internal set; }
        public float BarrelLengthInches { get; internal set; }
        public float DamageTransfer { get; internal set; }
        public float PenetrationEfficiency { get; internal set; }
        public float MuzzleVelocityFactor { get; internal set; }
        public float VelocityHalfDistance { get; internal set; }
        public int ReferenceDamage { get; internal set; }
        public float ReferencePenetration { get; internal set; }

        public BulletBallisticsSnapshot Snapshot(int referenceDamage, float referencePenetration)
            => new BulletBallisticsSnapshot
            {
                referenceDamage = referenceDamage, referencePenetration = referencePenetration,
                muzzleVelocityFactor = MuzzleVelocityFactor, velocityHalfDistance = VelocityHalfDistance,
                damageTransfer = DamageTransfer, penetrationEfficiency = PenetrationEfficiency
            };

        public int DamageAt(float distance = 0f, float weaponMultiplier = 1f)
            => Snapshot(Mathf.RoundToInt(ReferenceDamage * weaponMultiplier), ReferencePenetration).DamageAt(distance);
        public float PenetrationAt(float distance = 0f, float weaponMultiplier = 1f)
            => Snapshot(ReferenceDamage, ReferencePenetration * weaponMultiplier).PenetrationAt(distance);
        public float FragmentationChanceAt(float distance = 0f, float penetratedArmor = 0f)
        {
            BulletFragmentationExtension settings = Projectile.GetModExtension<BulletFragmentationExtension>();
            return settings == null ? 0f : Mathf.Clamp01(settings.chance)
                * settings.Strength(BulletBallisticsRules.RetainedVelocity(
                    MuzzleVelocityFactor, distance, VelocityHalfDistance), penetratedArmor);
        }
    }

    internal static class ModularWeaponBallistics
    {
        internal static ModularWeaponBallisticProfile Resolve(ThingDef projectile, ModularWeaponConvertedStats stats)
        {
            BulletBallisticsExtension input = projectile?.GetModExtension<BulletBallisticsExtension>();
            if (input == null || projectile.projectile == null) return null;
            float reference = Mathf.Max(1f, input.referenceVelocityMetersPerSecond);
            float barrel = stats.BarrelLengthInches > 0f ? stats.BarrelLengthInches : input.referenceBarrelInches;
            float velocity = Mathf.Clamp(reference * BulletBallisticsRules.BarrelVelocityFactor(
                barrel, input.referenceBarrelInches, input.barrelVelocityExponent)
                    + stats.MuzzleVelocityOffsetMetersPerSecond, reference * 0.25f, reference * 1.5f);
            return new ModularWeaponBallisticProfile
            {
                Projectile = projectile, ProjectileMassGrams = input.projectileMassGrams,
                ReferenceVelocityMetersPerSecond = reference, MuzzleVelocityMetersPerSecond = velocity,
                MuzzleEnergyJoules = BulletBallisticsRules.Energy(input.projectileMassGrams, velocity),
                BarrelLengthInches = barrel, MuzzleVelocityFactor = velocity / reference,
                VelocityHalfDistance = Mathf.Max(1f, input.velocityHalfDistance),
                DamageTransfer = stats.DamageTransferFactor, PenetrationEfficiency = stats.PenetrationEfficiencyFactor,
                ReferenceDamage = projectile.projectile.GetDamageAmount(1f, null),
                ReferencePenetration = projectile.projectile.GetArmorPenetration()
            };
        }
    }
}
