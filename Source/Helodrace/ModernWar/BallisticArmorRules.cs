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

        // Wear for hits that keep the vanilla roll (melee, explosions, projectiles without physics).
        // Physical bullets wear plates by absorbed energy instead. AP uses the game's normalized
        // units: displayed penetration 33 is 0.33.
        public static float PlateWear(float damage, float armorPenetration, float materialCoefficient)
        {
            return Math.Max(0f, damage)
                * Math.Max(0f, armorPenetration)
                * Math.Max(0f, materialCoefficient)
                * PlateWearMultiplier;
        }
    }
}
