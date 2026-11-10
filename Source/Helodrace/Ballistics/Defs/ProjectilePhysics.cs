using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Vector3 = System.Numerics.Vector3;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// Flight profile of a projectile: descent angle at the target, positive downward (design doc 7.1). Top attack
    /// and plunging fire are this one number, not separate flags.
    /// </summary>
    public sealed class ProjectileFlightExtension : DefModExtension
    {
        public float descentDegrees;
    }

    /// <summary>
    /// Shaped-charge warhead of a projectile (design doc 4.2). The RHA figures are game values within public claims
    /// for each warhead [game]; mechanism is ShapedCharge, or Tandem when a precursor is given.
    /// </summary>
    public sealed class ShapedChargeExtension : DefModExtension
    {
        public float penetrationRhaMillimeters;
        public float precursorPenetrationRhaMillimeters;
        public float tandemDelayMicroseconds = 120f;
        public float jetDensityKgM3 = ShapedChargeModel.CopperDensity;
        public float tipVelocityMetersPerSecond = 8000f;
        public float tailVelocityMetersPerSecond = 2000f;
        public float scatter = 0.05f;

        private ShapedChargeWarhead model;

        public PenetrationMechanism Mechanism => precursorPenetrationRhaMillimeters > 0f ? PenetrationMechanism.Tandem : PenetrationMechanism.ShapedCharge;

        public ShapedChargeWarhead Model => model ?? (model = new ShapedChargeWarhead
        {
            PenetrationRhaM = penetrationRhaMillimeters * 0.001f,
            PrecursorPenetrationRhaM = precursorPenetrationRhaMillimeters * 0.001f,
            TandemDelayS = tandemDelayMicroseconds * 1e-6f,
            JetDensityKgM3 = jetDensityKgM3,
            TipVelocityMs = tipVelocityMetersPerSecond,
            TailVelocityMs = tailVelocityMetersPerSecond,
            Scatter = scatter
        });

        public override IEnumerable<string> ConfigErrors()
        {
            if (!(penetrationRhaMillimeters > 0f)) yield return "Shaped charge needs penetrationRhaMillimeters.";
            if (precursorPenetrationRhaMillimeters < 0f || tandemDelayMicroseconds < 0f) yield return "Shaped charge precursor and delay must not be negative.";
            if (!(jetDensityKgM3 > 0f && tipVelocityMetersPerSecond > tailVelocityMetersPerSecond && tailVelocityMetersPerSecond > 0f))
                yield return "Shaped charge jet needs density and tip velocity above tail velocity.";
        }
    }

    /// <summary>
    /// High-explosive warhead (design doc 4.2, 5.3, 5.4): TNT-equivalent charge, steel case for Gurney/Mott fragments,
    /// and the squash-head (HESH) flag. Values are game estimates within public descriptions [game].
    /// </summary>
    public sealed class BlastWarheadExtension : DefModExtension
    {
        public float explosiveTntKilograms;
        public float caseKilograms;
        /// <summary>Mott mean-mass parameter mu, grams (mean fragment mass is 2 mu). [game]</summary>
        public float mottMuGrams = 2f;
        public bool squashHead;
        public float calibreMillimeters;

        public override IEnumerable<string> ConfigErrors()
        {
            if (!(explosiveTntKilograms > 0f)) yield return "Blast warhead needs explosiveTntKilograms.";
            if (caseKilograms < 0f || !(mottMuGrams > 0f)) yield return "Blast warhead case and Mott mu must be positive.";
            if (squashHead && !(calibreMillimeters > 0f)) yield return "Squash-head warheads need calibreMillimeters.";
        }
    }

    /// <summary>Game boundary: reads a live projectile into the kinetic vehicle model's inputs.</summary>
    public static class ProjectilePhysics
    {
        /// <summary>Plunging fire without flight data. [game]</summary>
        public const float OverheadDescentDegrees = 60f;

        // Game contract: Projectile.origin is protected; the launch point is a Unity vector.
        private static readonly AccessTools.FieldRef<Projectile, UnityEngine.Vector3> Origin =
            AccessTools.FieldRefAccess<Projectile, UnityEngine.Vector3>("origin");

        public static float DescentDegrees(ThingDef projectile)
        {
            if (projectile == null) return 0f;
            ProjectileFlightExtension flight = projectile.GetModExtension<ProjectileFlightExtension>();
            if (flight != null) return flight.descentDegrees;
            return projectile.projectile?.flyOverhead == true ? OverheadDescentDegrees : 0f;
        }

        /// <summary>Travel heading of a live projectile (0 = north, clockwise).</summary>
        public static float HeadingDegrees(Projectile projectile) => projectile.ExactRotation.eulerAngles.y;

        /// <summary>Horizontal distance flown so far, cells.</summary>
        public static float DistanceFlown(Projectile projectile) =>
            SpatialMath.HorizontalLength(projectile.ExactPosition.ToNative() - Origin(projectile).ToNative());

        /// <summary>
        /// Kinetic round and its state at impact, velocity in world coordinates. False for rounds without physical
        /// data (vanilla bullets, explosives, missiles): those keep their existing damage path.
        /// </summary>
        public static bool TryKinetic(Projectile projectile, out VehicleKineticRound round, out PenetratorState state,
            out float headingDegrees, out float descentDegrees)
        {
            round = null;
            state = default;
            headingDegrees = descentDegrees = 0f;
            BulletBallisticsExtension physics = projectile?.def.GetModExtension<BulletBallisticsExtension>();
            if (physics == null || !physics.HasPhysics) return false;
            float velocityFactor = projectile is Projectile_BallisticBullet ballistic
                ? ballistic.ImpactVelocityFactor
                : BulletBallisticsRules.RetainedVelocity(1f, DistanceFlown(projectile), physics.velocityHalfDistance);
            float speed = physics.referenceVelocityMetersPerSecond * velocityFactor;
            headingDegrees = HeadingDegrees(projectile);
            descentDegrees = DescentDegrees(projectile.def);
            Vector3 travel = BallisticGeometry.TravelDirection(headingDegrees, descentDegrees);
            round = RoundOf(physics);
            state = PenetratorState.Projectile(physics.mechanism, physics.projectileMassGrams * 0.001f, travel * speed,
                physics.penetratorLengthMillimeters * 0.001f, physics.diameterMillimeters * 0.001f);
            return true;
        }

        public static VehicleKineticRound RoundOf(BulletBallisticsExtension physics) => new VehicleKineticRound
        {
            Mechanism = physics.mechanism,
            SmallArmsClass = physics.penetratorClass,
            Core = physics.penetratorMaterial?.PenetratorModel
        };
    }
}
