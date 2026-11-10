using System;
using System.Numerics;
using RimWorld;
using Verse;

namespace Helodrace.Ballistics
{
    /// <summary>
    /// A round that glanced off armour and flies on with its residual velocity (design doc section 6.5). It
    /// carries its own physical state, so a second body or armour piece it meets resolves it like any bullet.
    /// One generation only: a ricochet that glances off again is not relaunched.
    /// </summary>
    public sealed class Projectile_Ricochet : Bullet
    {
        /// <summary>Slower or steeper deflections end at the deflecting body. [game]</summary>
        public const float MinimumSpeed = 120f;
        public const float MinimumHorizontalShare = 0.5f;

        internal float massKg;
        internal float speedMetersPerSecond;
        internal float diameterM;
        internal PenetratorClass penetratorClass;
        private int damage = 1;
        private float penetration;

        public override int DamageAmount => damage;
        public override float ArmorPenetration => penetration;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref massKg, "massKg");
            Scribe_Values.Look(ref speedMetersPerSecond, "speedMetersPerSecond");
            Scribe_Values.Look(ref diameterM, "diameterM");
            Scribe_Values.Look(ref penetratorClass, "penetratorClass");
            Scribe_Values.Look(ref damage, "damage", 1);
            Scribe_Values.Look(ref penetration, "penetration");
        }

        internal static Projectile_Ricochet TryLaunch(HumanoidBallisticImpact impact)
        {
            if (impact.projectile is Projectile_Ricochet) return null;
            Pawn pawn = impact.pawn;
            Map map = pawn?.MapHeld;
            Vector3 velocity = impact.deflectedVelocity;
            float speed = velocity.Length();
            if (map == null || speed < MinimumSpeed) return null;
            float horizontal = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            // Steep deflections leave upward (crown hits) or bury in the ground nearby.
            if (horizontal / speed < MinimumHorizontalShare) return null;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("HD_Projectile_Ricochet");
            if (def == null) return null;
            Vector3 direction = new Vector3(velocity.X, 0f, velocity.Z) / horizontal;
            float speedRatio = impact.impactSpeed > 0f ? Math.Clamp(speed / impact.impactSpeed, 0f, 1f) : 0f;
            // Range in cells from the residual speed. [game]
            float range = Math.Clamp(speed / 25f, 3f, 30f);
            Vector3 start = pawn.DrawPos.ToNative() + direction * 1.1f;
            IntVec3 startCell = new IntVec3((int)MathF.Floor(start.X), 0, (int)MathF.Floor(start.Z));
            Vector3 end = start + direction * range;
            IntVec3 targetCell = new IntVec3(
                Math.Clamp((int)MathF.Floor(end.X), 0, map.Size.x - 1), 0,
                Math.Clamp((int)MathF.Floor(end.Z), 0, map.Size.z - 1));
            if (!startCell.InBounds(map) || targetCell == startCell) return null;

            var ricochet = (Projectile_Ricochet)GenSpawn.Spawn(def, startCell, map);
            ricochet.massKg = impact.penetrator.MassKg;
            ricochet.speedMetersPerSecond = speed;
            ricochet.diameterM = impact.penetrator.DiameterM;
            ricochet.penetratorClass = impact.penetratorClass;
            // Same wound law as armour: sqrt(E ratio) is the speed ratio for an unchanged mass.
            ricochet.damage = Math.Max(1, (int)MathF.Round(impact.impactDamage * speedRatio));
            ricochet.penetration = impact.impactPenetration * MathF.Pow(speedRatio, 1.2f);
            // Game contract: Launch takes Unity vectors for the origin.
            ricochet.Launch(impact.projectile.Launcher, start.ToGame(), new LocalTargetInfo(targetCell),
                new LocalTargetInfo(targetCell), ProjectileHitFlags.All);
            return ricochet;
        }
    }
}
