using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
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
            shell = new ShotgunShell { id = pellet.thingIDNumber, origin = origin, targetPoint = intended.Cell.ToVector3Shifted().ToNative() };
            if (intended.Thing is Pawn pawn)
            {
                shell.targetPoint = ShotgunBallistics.AimPoint(pawn, origin, pellet.def.projectile.SpeedTilesPerTick);
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
                    ? shell.targetPoint : used.Cell.ToVector3Shifted().ToNative();
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
                pellet.Launch(launcher, shell.origin.ToGame(), usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, cover);
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
        private bool flightStep;
        private float stepStart, stepEnd;
        internal ShotgunProjectileExtension Settings => def.GetModExtension<ShotgunProjectileExtension>() ?? defaults;
        private static readonly ShotgunProjectileExtension defaults = new ShotgunProjectileExtension();
        internal ShotgunShell Shell => shell ?? (shell = Map?.GetComponent<MapComponent_ShotgunShells>().ShellFor(this));
        internal float TravelDistance => impactDistance >= 0f ? impactDistance : SpatialMath.HorizontalLength(ExactPosition.ToNative() - origin.ToNative());
        internal float Energy => ShotgunBallistics.Energy(TravelDistance, Settings.energyHalfDistance);
        public override int DamageAmount => Shell == null ? base.DamageAmount : Math.Max(1, (int)Math.Round(base.DamageAmount * Energy));
        public override float ArmorPenetration => Shell == null ? base.ArmorPenetration : base.ArmorPenetration * (float)Math.Sqrt(Energy);
        // This must already be true when GenSpawn registers tickability, before
        // Launch attaches the shell. Off-screen pellets must not split a shell
        // into staggered 15-tick updates.
        public override int UpdateRateTicks => 1;

        public override void Launch(Thing launcher, UnityEngine.Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            ShotgunLaunch launch = ShotgunSpreadContext.Launching;
            if (launch == null)
            {
                base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
                return;
            }
            launch.Capture(this, launcher, origin.ToNative(), usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            shell = launch.shell;
            slope = ShotgunBallistics.Sample(launch.angle * Math.Max(0f, Settings.spreadMultiplier) * launch.handlingSpread);
            Vector3 endpoint = shell.targetPoint + shell.Right * (shell.aimError.X + slope.X * shell.AimDistance);
            LocalTargetInfo actualTarget = endpoint.ToGame().ToIntVec3();
            hitFlags |= ProjectileHitFlags.NonTargetWorld;
            if (intendedTarget.Thing is Pawn pawn)
            {
                // Let the ray test decide at impact too, including a target
                // moving into or out of the pattern after launch.
                hitFlags |= ProjectileHitFlags.IntendedTarget;
                actualTarget = pawn;
                // Test cover at the predicted interception point. Testing the
                // pawn's current position here would reject a valid lead shot.
                if (HitsAt(pawn, shell.targetPoint))
                {
                    if (launch.report.HasValue && !Rand.Chance(launch.report.Value.PassCoverChance))
                    {
                        Thing obstruction = launch.report.Value.GetRandomCoverToMissInto();
                        if (obstruction != null)
                        {
                            actualTarget = obstruction;
                            endpoint = obstruction.Position.ToVector3Shifted().ToNative();
                            targetCoverDef = obstruction.def;
                        }
                    }
                }
            }
            else
            {
                // World targets have no vertical silhouette. Preserve both
                // map-space axes of the shared miss and pellet spread.
                endpoint += shell.Forward * (shell.aimError.Y + slope.Y * shell.AimDistance);
                actualTarget = endpoint.ToGame().ToIntVec3();
                if (endpoint.ToGame().ToIntVec3() == intendedTarget.Cell)
                {
                    actualTarget = intendedTarget;
                    hitFlags |= ProjectileHitFlags.IntendedTarget;
                }
            }
            // Native flight time uses full Vector3 distance, while flight is
            // horizontal. Keep equal render heights so altitude is not added
            // to the travel distance and the moving-target lead stays valid.
            endpoint.Y = origin.y;
            base.Launch(launcher, origin, actualTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            // The base launch adds unrelated cell jitter. Preserve the sampled
            // continuous trajectory and recompute its flight time instead.
            destination = endpoint.ToGame();
            ticksToImpact = Math.Max(1, (int)Math.Ceiling(StartingTicksToImpact));
            lifetime = ticksToImpact;
            Map.GetComponent<MapComponent_ShotgunShells>().Register(this, shell);
        }

        protected override void TickInterval(int delta)
        {
            ShotgunShell current = Shell;
            if (current == null) { base.TickInterval(delta); return; }
            stepStart = current.DistanceTo(ExactPosition.ToNative());
            float fraction = Mathf.Clamp01(1f - (ticksToImpact - delta) / StartingTicksToImpact);
            stepEnd = current.DistanceTo(origin.ToNative() + (destination.ToNative() - origin.ToNative()) * fraction);
            flightStep = true;
            try
            {
                // Native flight resolves walls and projectile shields first.
                base.TickInterval(delta);
                // Resolve the aimed pawn deterministically when this segment
                // crosses its body. Vanilla's random cell interception can
                // otherwise let one early pellet stop a moving pawn before
                // the rest reach their endpoint.
                if (!Destroyed && usedTarget.Thing is Pawn target && intendedTarget.Thing == target && CanHit(target))
                    Impact(target);
            }
            finally { flightStep = false; }
        }

        internal bool HitsPawn(Pawn pawn)
        {
            ShotgunShell current = Shell;
            if (current == null) return true;
            ShotgunPose pose = current.PoseFor(pawn);
            float distance = current.DistanceTo(pose.position);
            return HitsAt(pose.position, pose.radii) && (!flightStep ||
                (distance >= Math.Min(stepStart, stepEnd) - pose.radii.X && distance <= Math.Max(stepStart, stepEnd) + pose.radii.X));
        }

        internal bool HitsAt(Pawn pawn, Vector3 position) => HitsAt(position,
            ShotgunBallistics.BodyRadii(pawn.BodySize, pawn.GetPosture() == PawnPosture.Standing));

        private bool HitsAt(Vector3 position, Vector2 radii)
        {
            ShotgunShell current = Shell;
            if (current == null) return true;
            return current.DistanceTo(position) > 0f
                && current.DistanceTo(position) <= current.AimDistance + radii.X
                && ShotgunBallistics.Hits(current.OffsetAt(position, slope),
                    radii);
        }

        protected override void ImpactSomething()
        {
            if (Shell == null) { base.ImpactSomething(); return; }
            // Pawn size/posture has already been tested against the ellipse.
            // Do not add vanilla's second random hit/posture roll at the end.
            if (usedTarget.HasThing && CanHit(usedTarget.Thing)
                && (!(usedTarget.Thing is Pawn target) || Math.Abs(Shell.DistanceTo(Shell.PoseFor(target).position) - Shell.AimDistance)
                    <= Shell.PoseFor(target).radii.X + 0.5f))
            { Impact(usedTarget.Thing); return; }
            List<Thing> things = VerbUtility.ThingsToHit(Position, Map, CanHit);
            things.Shuffle();
            foreach (Thing thing in things)
                if (thing is Pawn || Rand.Chance(1.5f * thing.def.fillPercent)) { Impact(thing); return; }
            Impact(null);
        }

        internal bool TryHitSolid(IntVec3 cell)
        {
            if (Shell == null) return false;
            foreach (Thing thing in cell.GetThingList(Map))
                if (!(thing is Pawn) && thing.def.Fillage == FillCategory.Full
                    && !(thing is Building_Door door && door.Open) && CanHit(thing))
                {
                    Impact(thing);
                    return true;
                }
            return false;
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            ShotgunImpact previous = ShotgunSpreadContext.Impact;
            impactDistance = SpatialMath.HorizontalLength(ExactPosition.ToNative() - origin.ToNative());
            ShotgunSpreadContext.Impact = null;
            if (!blockedByShield && Shell != null && hitThing is Pawn pawn)
            {
                ShotgunPose pose = Shell.PoseFor(pawn);
                ShotgunSpreadContext.Impact = new ShotgunImpact
                {
                    shell = Shell, pellet = this, pawn = pawn, damageDef = DamageDef,
                    energy = Energy, incomingDamage = DamageAmount,
                    relativeSpread = (slope * Shell.DistanceTo(pose.position)).Length() / Math.Min(pose.radii.X, pose.radii.Y)
                };
            }
            try { base.Impact(hitThing, blockedByShield); }
            finally { ShotgunSpreadContext.Impact = previous; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            NativeVectorScribe.Look(ref slope, "pelletSlope");
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

    [HarmonyPatch(typeof(Projectile), "CheckForFreeIntercept")]
    public static class Patch_Projectile_ShotgunSolidCollision
    {
        public static bool Prefix(Projectile __instance, IntVec3 c, ref bool __result)
        {
            if (!(__instance is Projectile_ShotgunPellet pellet) || !pellet.TryHitSolid(c)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Patch_Projectile_ShotgunWorldSweep
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var distanceFactor = AccessTools.Method(typeof(VerbUtility), nameof(VerbUtility.InterceptChanceFactorFromDistance));
            var allowSweep = AccessTools.Method(typeof(Patch_Projectile_ShotgunWorldSweep), nameof(AllowSweep));
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(distanceFactor)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, allowSweep);
            }
        }
        private static float AllowSweep(float factor, Projectile projectile) =>
            projectile is Projectile_ShotgunPellet pellet && pellet.Shell != null ? Math.Max(0.0001f, factor) : factor;
        // Only enable cell traversal here. The original distance factors for
        // incidental pawns and partial cover remain inside each cell check;
        // solid walls cannot be skipped merely for being near the shooter.
    }
}
