using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    internal sealed class ShotgunLaunch
    {
        internal readonly Verb verb;
        internal readonly int count;
        internal readonly float angle;
        internal ShotReport? report;
        internal ShotgunShell shell;
        internal float handlingSpread = 1f;
        private Projectile_ShotgunPellet template;
        private Thing launcher;
        private Thing equipment;
        private LocalTargetInfo usedTarget;
        private LocalTargetInfo intendedTarget;
        private ProjectileHitFlags hitFlags;
        private bool preventFriendlyFire;
        private ThingDef cover;

        internal ShotgunLaunch(Verb verb, int count, float angle)
        {
            this.verb = verb;
            this.count = Math.Max(1, count);
            this.angle = Math.Max(0f, angle);
        }

        internal void Capture(Projectile_ShotgunPellet pellet, Thing shooter, Vector3 origin, LocalTargetInfo used,
            LocalTargetInfo intended, ProjectileHitFlags flags, bool preventFriendly, Thing weapon, ThingDef coverDef)
        {
            if (shell != null) return;
            template = pellet;
            launcher = shooter;
            equipment = weapon;
            usedTarget = used;
            intendedTarget = intended;
            hitFlags = flags;
            preventFriendlyFire = preventFriendly;
            cover = coverDef;
            shell = new ShotgunShell { id = pellet.thingIDNumber, origin = origin, targetPoint = intended.Cell.ToVector3Shifted() };
            if (intended.Thing is Pawn pawn)
            {
                // Forced-miss launcher paths can bypass HitReportFor. Buckshot
                // still needs shooter/weather/cover data in that case.
                if (!report.HasValue) report = ShotReport.HitReportFor(shooter, verb, intended);
                // Size and posture are handled by the silhouette, not twice in
                // the aiming error. Ignore vanilla's all-or-nothing miss tile.
                float accuracy = report.Value.AimOnTargetChance_StandardTarget;
                float deviation = ShotgunBallistics.AimDeviation(accuracy, shell.AimDistance);
                shell.aimError = new Vector2(Rand.Gaussian(0f, deviation), Rand.Gaussian(0f, deviation));
                handlingSpread = ShotgunBallistics.HandlingSpread(accuracy);
                // Choose the anatomical centre before any pellet has impacted.
                shell.CentreFor(pawn, pellet.DamageDef);
            }
            else
            {
                // Keep map-space misses for buildings and ground targeting.
                Vector3 aim = used.HasThing && used.Thing != intended.Thing
                    ? shell.targetPoint : used.Cell.ToVector3Shifted();
                Vector3 error = aim - shell.targetPoint;
                float jitter = 0.03f + 0.12f * Math.Max(0f, 1f - Math.Min(1f, report?.AimOnTargetChance_IgnoringPosture ?? 1f));
                shell.aimError = new Vector2(Vector3.Dot(error, shell.Right) + Rand.Gaussian(0f, jitter),
                    Vector3.Dot(error, shell.Forward) + Rand.Gaussian(0f, jitter));
            }
        }

        internal void LaunchRemaining()
        {
            if (template == null || template.Map == null) return;
            for (int i = 1; i < count; i++)
            {
                var pellet = (Projectile_ShotgunPellet)GenSpawn.Spawn(template.def, template.Position, template.Map);
                pellet.damageDefOverride = template.damageDefOverride;
                pellet.extraDamages = template.extraDamages == null ? new List<ExtraDamage>() : new List<ExtraDamage>(template.extraDamages);
                pellet.Launch(launcher, shell.origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, cover);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_ShotgunAim
    {
        public static void Postfix(Verb verb, ShotReport __result)
        {
            ShotgunLaunch launch = ShotgunSpreadContext.Launching;
            if (launch?.verb == verb) launch.report = __result;
        }
    }

    public sealed class Projectile_ShotgunPellet : Bullet
    {
        private Vector2 slope;
        private ShotgunShell shell;
        private float impactDistance = -1f;
        internal ShotgunProjectileExtension Settings => def.GetModExtension<ShotgunProjectileExtension>() ?? defaults;
        private static readonly ShotgunProjectileExtension defaults = new ShotgunProjectileExtension();
        internal ShotgunShell Shell => shell ?? (shell = Map?.GetComponent<MapComponent_ShotgunShells>().ShellFor(this));
        internal float TravelDistance => impactDistance >= 0f ? impactDistance : (ExactPosition - origin).MagnitudeHorizontal();
        internal float Energy => ShotgunBallistics.Energy(TravelDistance, Settings.energyHalfDistance);
        public override int DamageAmount => Shell == null ? base.DamageAmount : Math.Max(1, (int)Math.Round(base.DamageAmount * Energy));
        public override float ArmorPenetration => Shell == null ? base.ArmorPenetration : base.ArmorPenetration * (float)Math.Sqrt(Energy);

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            ShotgunLaunch launch = ShotgunSpreadContext.Launching;
            if (launch == null)
            {
                base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
                return;
            }
            launch.Capture(this, launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            shell = launch.shell;
            slope = ShotgunBallistics.Sample(launch.angle * Math.Max(0f, Settings.spreadMultiplier) * launch.handlingSpread);
            Vector3 endpoint = shell.targetPoint + shell.Right * (shell.aimError.x + slope.x * shell.AimDistance);
            LocalTargetInfo actualTarget = endpoint.ToIntVec3();
            hitFlags |= ProjectileHitFlags.NonTargetWorld;
            if (intendedTarget.Thing is Pawn pawn)
            {
                // Let the ray test decide at impact too, including a target
                // moving into or out of the pattern after launch.
                hitFlags |= ProjectileHitFlags.IntendedTarget;
                if (HitsPawn(pawn))
                {
                    actualTarget = pawn;
                    if (launch.report.HasValue && !Rand.Chance(launch.report.Value.PassCoverChance))
                    {
                        Thing obstruction = launch.report.Value.GetRandomCoverToMissInto();
                        if (obstruction != null)
                        {
                            actualTarget = obstruction;
                            endpoint = obstruction.Position.ToVector3Shifted();
                            targetCoverDef = obstruction.def;
                        }
                    }
                }
            }
            else
            {
                // World targets have no vertical silhouette. Preserve both
                // map-space axes of the shared miss and pellet spread.
                endpoint += shell.Forward * (shell.aimError.y + slope.y * shell.AimDistance);
                actualTarget = endpoint.ToIntVec3();
                if (endpoint.ToIntVec3() == intendedTarget.Cell)
                {
                    actualTarget = intendedTarget;
                    hitFlags |= ProjectileHitFlags.IntendedTarget;
                }
            }
            base.Launch(launcher, origin, actualTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            // The base launch adds unrelated cell jitter. Preserve the sampled
            // continuous trajectory and recompute its flight time instead.
            destination = endpoint;
            ticksToImpact = Math.Max(1, (int)Math.Ceiling(StartingTicksToImpact));
            lifetime = ticksToImpact;
            Map.GetComponent<MapComponent_ShotgunShells>().Register(this, shell);
        }

        internal bool HitsPawn(Pawn pawn)
        {
            ShotgunShell current = Shell;
            if (current == null) return true;
            Vector2 radii = ShotgunBallistics.BodyRadii(pawn.BodySize, pawn.GetPosture() == PawnPosture.Standing);
            return current.DistanceTo(pawn.DrawPos) > 0f
                && current.DistanceTo(pawn.DrawPos) <= current.AimDistance + radii.x
                && ShotgunBallistics.Hits(current.OffsetAt(pawn.DrawPos, slope),
                    radii);
        }

        protected override void ImpactSomething()
        {
            if (Shell == null) { base.ImpactSomething(); return; }
            // Pawn size/posture has already been tested against the ellipse.
            // Do not add vanilla's second random hit/posture roll at the end.
            if (usedTarget.HasThing && CanHit(usedTarget.Thing)
                && (!(usedTarget.Thing is Pawn target) || Math.Abs(Shell.DistanceTo(target.DrawPos) - Shell.AimDistance)
                    <= ShotgunBallistics.BodyRadii(target.BodySize, target.GetPosture() == PawnPosture.Standing).x + 0.5f))
            { Impact(usedTarget.Thing); return; }
            List<Thing> things = VerbUtility.ThingsToHit(Position, Map, CanHit);
            things.Shuffle();
            foreach (Thing thing in things)
                if (thing is Pawn || Rand.Chance(1.5f * thing.def.fillPercent)) { Impact(thing); return; }
            Impact(null);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            ShotgunImpact previous = ShotgunSpreadContext.Impact;
            impactDistance = (ExactPosition - origin).MagnitudeHorizontal();
            ShotgunSpreadContext.Impact = null;
            if (!blockedByShield && Shell != null && hitThing is Pawn pawn)
            {
                Vector2 radii = ShotgunBallistics.BodyRadii(pawn.BodySize, pawn.GetPosture() == PawnPosture.Standing);
                ShotgunSpreadContext.Impact = new ShotgunImpact
                {
                    shell = Shell, pellet = this, pawn = pawn, damageDef = DamageDef,
                    energy = Energy, incomingDamage = DamageAmount,
                    relativeSpread = (slope * Shell.DistanceTo(pawn.DrawPos)).magnitude / Math.Min(radii.x, radii.y)
                };
            }
            try { base.Impact(hitThing, blockedByShield); }
            finally { ShotgunSpreadContext.Impact = previous; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref slope.x, "pelletSlopeX");
            Scribe_Values.Look(ref slope.y, "pelletSlopeY");
        }
    }

    [HarmonyPatch(typeof(Projectile), "CanHit")]
    public static class Patch_Projectile_ShotgunSilhouette
    {
        public static void Postfix(Projectile __instance, Thing thing, ref bool __result)
        {
            if (__result && __instance is Projectile_ShotgunPellet pellet && thing is Pawn pawn)
                __result = pellet.HitsPawn(pawn);
        }
    }
}
