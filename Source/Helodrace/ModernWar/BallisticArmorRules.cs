using System;

namespace Helodrace.ModernWar
{
    public enum BallisticPlateMaterial
    {
        Ceramic,
        UHMWPE,
        BallisticSteel,
        Composite
    }

    internal static class BallisticArmorRules
    {
        public const float PlateWearMultiplier = 1.5f;
        public const float PlateGuaranteedBlockMinimumDurability = 0.4f;
        public const float ShieldGuaranteedBlockMinimumDurability = 0.75f;

        public static float MaterialCoefficient(
            BallisticPlateMaterial material,
            float compositeCoefficient)
        {
            switch (material)
            {
                case BallisticPlateMaterial.UHMWPE: return 0.7f;
                case BallisticPlateMaterial.BallisticSteel: return 0.5f;
                case BallisticPlateMaterial.Composite: return Math.Max(0f, compositeCoefficient);
                default: return 1f;
            }
        }

        // AP uses the game's normalized units: displayed penetration 33 is 0.33.
        public static float PlateWear(float damage, float armorPenetration, float materialCoefficient)
        {
            return Math.Max(0f, damage)
                * Math.Max(0f, armorPenetration)
                * Math.Max(0f, materialCoefficient)
                * PlateWearMultiplier;
        }

        public static bool CanGuaranteeBlock(
            float armorPenetration,
            float penetrationCutoff,
            float durabilityRatio,
            float minimumDurability)
        {
            return penetrationCutoff > 0f
                && armorPenetration <= penetrationCutoff
                && durabilityRatio >= minimumDurability;
        }
    }
}
