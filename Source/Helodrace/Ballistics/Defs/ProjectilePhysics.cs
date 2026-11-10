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
