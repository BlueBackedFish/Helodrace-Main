using System;
using System.Numerics;
using Waylith.Math;
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
        // Treat accuracy as continuous muzzle error, rather than a chance to
        // move the whole shell into a neighbouring tile. Close targets are
        // easier to point at even for an inexperienced shooter.
        internal static float AimDeviation(float accuracy, float distance)
        {
            distance = Math.Max(0f, distance);
            float inaccuracy = 1f - Mathf.Clamp01(accuracy);
            return 0.025f + distance * Slope(0.15f + 1.5f * inaccuracy * distance / (distance + 8f));
        }
        // Poor aim adds a modest handling penalty. Good aim approaches the
        // weapon's physical, unchoked pattern; it never removes that spread.
        internal static float HandlingSpread(float accuracy) => 1f + 0.25f * (1f - Mathf.Clamp01(accuracy));
        // Collision coordinates must not contain damage jitter, leaning or
        // frame-dependent render interpolation from Pawn.DrawPos.
        internal static Vector3 Position(Pawn pawn)
        {
            Vector3 position = pawn.Position.ToVector3Shifted().ToNative();
            var path = pawn.pather;
            return path != null && path.Moving && path.nextCell.IsValid
                ? SpatialMath.Interpolate(position, path.nextCell.ToVector3Shifted().ToNative(), path.MovePercentage)
                : position;
        }
        internal static Vector3 Velocity(Pawn pawn)
        {
            var path = pawn.pather;
            if (path == null || !path.MovingNow || !path.nextCell.IsValid) return Vector3.Zero;
            float step = 1f;
            if (pawn.stances?.stagger?.Staggered == true) step *= pawn.stances.stagger.StaggerMoveSpeedFactor;
            if (pawn.Flying) step *= pawn.RaceProps.flightSpeedFactor;
            step = Math.Max(step, path.nextCellCostTotal / 450f);
            return (path.nextCell - pawn.Position).ToVector3().ToNative() * (step / Math.Max(1f, path.nextCellCostTotal));
        }
        internal static float InterceptTicks(Vector3 offset, Vector3 velocity, float speed)
        {
            offset.Y = velocity.Y = 0f;
            float a = velocity.LengthSquared() - speed * speed;
            float b = 2f * Vector3.Dot(offset, velocity);
            float c = offset.LengthSquared();
            if (c < 0.0001f || speed <= 0f) return 0f;
            float time;
            if (Math.Abs(a) < 0.000001f) time = b < 0f ? -c / b : 0f;
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant < 0f) return 0f;
                float root = (float)Math.Sqrt(discriminant);
                time = (-b - root) / (2f * a);
                if (time < 0f) time = (-b + root) / (2f * a);
            }
            return Math.Max(0f, Math.Min(120f, time));
        }
        internal static Vector3 AimPoint(Pawn pawn, Vector3 origin, float speed)
        {
            Vector3 position = Position(pawn);
            Vector3 velocity = Velocity(pawn);
            Vector3 lead = velocity * InterceptTicks(position - origin, velocity, speed);
            // Do not lead past a known stopping point. Turns after firing can
            // still evade the fixed trajectory; pellets never home in flight.
            if (pawn.pather != null && pawn.pather.Destination.IsValid)
                lead = ClampLead(lead, SpatialMath.HorizontalLength(pawn.pather.Destination.Cell.ToVector3Shifted().ToNative() - position));
            return position + lead;
        }
        private static Vector3 ClampLead(Vector3 lead, float maximumDistance)
        {
            float squareLength = lead.LengthSquared();
            if (squareLength <= maximumDistance * maximumDistance) return lead;
            float length = MathF.Sqrt(squareLength);
            return new Vector3(lead.X / length * maximumDistance, lead.Y / length * maximumDistance, lead.Z / length * maximumDistance);
        }
        internal static float Energy(float distance, float halfDistance) =>
            (float)Math.Pow(0.5d, Math.Max(0f, distance) / Math.Max(1f, halfDistance));
        internal static Vector2 BodyRadii(float size, bool standing)
        {
            float scale = (float)Math.Pow(Math.Max(0.02f, size), 1d / 3d);
            return new Vector2(standing ? 0.32f : 0.55f, standing ? 0.55f : 0.20f) * scale;
        }
        internal static bool Hits(Vector2 offset, Vector2 radii) =>
            offset.X * offset.X / (radii.X * radii.X) + offset.Y * offset.Y / (radii.Y * radii.Y) <= 1f;
        internal static bool IsPresent(Pawn pawn, BodyPartRecord part)
        {
            for (BodyPartRecord node = part; node != null; node = node.parent)
                if (pawn.health.hediffSet.PartIsMissing(node)) return false;
            return part != null;
        }
    }
}
