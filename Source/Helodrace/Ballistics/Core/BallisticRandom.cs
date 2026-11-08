using System;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Deterministic random stream for impact resolution. It never touches Verse.Rand, so resolving an impact
    /// does not shift the game's global random state, and each pipeline stage gets its own stream derived from
    /// the event seed: skipping or adding a stage does not change another stage's rolls.
    /// </summary>
    public struct BallisticRng
    {
        private ulong state;

        public BallisticRng(ulong seed)
        {
            state = seed;
        }

        public ulong NextULong()
        {
            // SplitMix64.
            state += 0x9E3779B97F4A7C15UL;
            return BallisticSeed.Mix(state);
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / (1 << 24));

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>Number of successes among n trials with probability p (exact for small n, normal approximation above).</summary>
        public int Binomial(int trials, float probability)
        {
            if (trials <= 0 || probability <= 0f) return 0;
            if (probability >= 1f) return trials;
            if (trials <= 64)
            {
                int hits = 0;
                for (int i = 0; i < trials; i++)
                {
                    if (NextFloat() < probability) hits++;
                }
                return hits;
            }
            // Box-Muller normal approximation for large fragment bundles.
            float u1 = MathF.Max(1e-7f, NextFloat());
            float u2 = NextFloat();
            float normal = MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
            float mean = trials * probability;
            float deviation = MathF.Sqrt(mean * (1f - probability));
            return Math.Clamp((int)MathF.Round(mean + normal * deviation), 0, trials);
        }
    }

    public static class BallisticSeed
    {
        public static ulong Mix(ulong value)
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        public static ulong Combine(ulong seed, ulong value) => Mix(seed ^ (Mix(value) + 0x9E3779B97F4A7C15UL + (seed << 6) + (seed >> 2)));

        /// <summary>Seed of a primary impact: same projectile, launch tick and target always resolve identically.</summary>
        public static ulong ForImpact(int projectileId, int launchTick, int targetId)
        {
            ulong seed = Mix(0x48454C4F44524143UL);
            seed = Combine(seed, unchecked((uint)projectileId));
            seed = Combine(seed, unchecked((uint)launchTick));
            return Combine(seed, unchecked((uint)targetId));
        }

        /// <summary>Seed of a secondary event (ricochet, fragment bundle, crew sub-impact) of a parent impact.</summary>
        public static ulong ForChild(ulong parentSeed, int childIndex, int targetId) =>
            Combine(Combine(parentSeed, 0xC0FFEEUL + unchecked((uint)childIndex)), unchecked((uint)targetId));

        /// <summary>Stable FNV-1a hash; string.GetHashCode is randomized per process and must not be used.</summary>
        public static ulong StableHash(string text)
        {
            ulong hash = 14695981039346656037UL;
            if (text == null) return hash;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static BallisticRng Stream(ulong eventSeed, string streamId) =>
            new BallisticRng(Combine(eventSeed, StableHash(streamId)));
    }
}
