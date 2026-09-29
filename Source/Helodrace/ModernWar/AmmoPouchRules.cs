using System;

namespace Helodrace.ModernWar
{
    internal static class AmmoPouchRules
    {
        public const float CooldownMultiplier = 0.75f;

        public static int EstimateCapacity(int burstShots, float projectileDamage)
        {
            // Stable fallback for weapons without an authored modular magazine.
            return Math.Min(60, Math.Max(1,
                (int)Math.Ceiling(Math.Max(1, burstShots) * 4f
                    + Math.Max(0f, projectileDamage))));
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

        public static float AfterReplenish(float remainingFraction, int capacity, int addedRounds)
        {
            if (capacity <= 0) return remainingFraction;
            int rounds = RoundsFor(remainingFraction, capacity);
            return (float)Math.Min(capacity,
                rounds + Math.Max(0, addedRounds)) / capacity;
        }

        public static float Cooldown(float original, float mechanicalMinimum)
        {
            return Math.Max(Math.Max(0f, mechanicalMinimum), original * CooldownMultiplier);
        }
    }
}
