using System;

namespace Helodrace.ModernWar
{
    internal static class AmmoPouchRules
    {
        public const float CooldownMultiplier = 0.75f;
        public const int CarbonSteelForFullMagazine = 5;

        public static int EstimateCapacity(int burstShots, float projectileDamage)
        {
            // Stable fallback for weapons without an authored modular magazine.
            return Math.Min(60, Math.Max(1,
                (int)Math.Ceiling(Math.Max(1, burstShots) * 4f
                    + 144f / Math.Max(1f, projectileDamage))));
        }

        public static int RoundsFor(float remainingFraction, int capacity)
        {
            if (capacity <= 0) return 0;
            return Math.Max(0, Math.Min(capacity, (int)Math.Floor(
                capacity * Math.Max(0f, Math.Min(1f, remainingFraction)) + 0.000001f)));
        }

        public static float AfterShot(int rounds, int capacity)
        {
            return capacity > 0
                ? (float)Math.Max(0, rounds - 1) / capacity
                : 0f;
        }

        public static int SteelNeeded(float remainingFraction)
        {
            return Math.Max(0, (int)Math.Ceiling(
                (1f - Math.Max(0f, Math.Min(1f, remainingFraction)))
                    * CarbonSteelForFullMagazine - 0.000001f));
        }

        public static float AfterReplenish(float remainingFraction, int carbonSteelCount)
        {
            return Math.Min(1f, Math.Max(0f, remainingFraction)
                + Math.Max(0, carbonSteelCount) / (float)CarbonSteelForFullMagazine);
        }

        public static float Cooldown(float original, float mechanicalMinimum)
        {
            return Math.Max(Math.Max(0f, mechanicalMinimum), original * CooldownMultiplier);
        }
    }
}
