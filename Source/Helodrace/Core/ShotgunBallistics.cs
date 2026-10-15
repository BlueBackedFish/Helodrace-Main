using System;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class ShotgunProjectileExtension : DefModExtension
    {
        public float spreadMultiplier = 1f;
        public float energyHalfDistance = 40f;
        public float internalPenetrationChance = 0.35f;
    }

    internal static class ShotgunBallistics
    {
        // A two-dimensional Gaussian: 90% of pellets lie within 2.146 sigma.
        internal static float Slope(float angle) => (float)Math.Tan(Math.Max(0f, Math.Min(15f, angle)) * Math.PI / 180d);
        internal static float PatternDiameter(float angle, float distance) => 4.292f * Slope(angle) * Math.Max(0f, distance);
        internal static Vector2 Sample(float angle) => new Vector2(Rand.Gaussian(), Rand.Gaussian()) * Slope(angle);
        internal static float Energy(float distance, float halfDistance) =>
            (float)Math.Pow(0.5d, Math.Max(0f, distance) / Math.Max(1f, halfDistance));
        internal static Vector2 BodyRadii(float size, bool standing)
        {
            float scale = (float)Math.Pow(Math.Max(0.02f, size), 1d / 3d);
            return new Vector2(standing ? 0.32f : 0.55f, standing ? 0.55f : 0.20f) * scale;
        }
        internal static bool Hits(Vector2 offset, Vector2 radii) =>
            offset.x * offset.x / (radii.x * radii.x) + offset.y * offset.y / (radii.y * radii.y) <= 1f;
        internal static bool IsPresent(Pawn pawn, BodyPartRecord part)
        {
            for (BodyPartRecord node = part; node != null; node = node.parent)
                if (pawn.health.hediffSet.PartIsMissing(node)) return false;
            return part != null;
        }
    }
}
