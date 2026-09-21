using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public static class TacticalPreemptiveFireUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const int PreparationTicks = 60;
        private const int ScanIntervalTicks = 5;
        private const int ReportLifetimeTicks = 2;
        private const float CorridorHalfWidth = 0.72f;
        private const float MinimumLineLength = 0.75f;
        private const float AccuracyMultiplier = 0.90f;

        private sealed class State
        {
            public Pawn pawn;
            public Verb verb;
            public IntVec3 origin;
            public IntVec3 destination;
            public Vector3 direction;
            public float length;
            public bool requiresAim;
            public int readyTick;
            public int nextScanTick;
            public bool firing;
            public int nextPairTick;
        }

        private struct ReportState
        {
            public int tick;
        }

        private static readonly Dictionary<int, State> states =
            new Dictionary<int, State>();
        private sealed class LineShot
        {
            public Thing candidate;
            // One roll per pawn per projectile, even if CanHit is queried again.
            public readonly Dictionary<int, bool> hitRolls = new Dictionary<int, bool>();
        }
        private static readonly ConditionalWeakTable<Projectile, LineShot> lineShots =
            new ConditionalWeakTable<Projectile, LineShot>();
        private static readonly Dictionary<int, ReportState> reports =
            new Dictionary<int, ReportState>();

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public static bool IsActive(Pawn pawn)
        {
            return pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.pawn == pawn;
        }

        public static bool IsActive(Verb verb)
        {
            Pawn pawn = verb?.Caster as Pawn;
            return pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.verb == verb
                && state.firing;
        }

        public static bool HasAccess(Pawn pawn)
        {
            Verb verb = PrimaryVerb(pawn);
            return pawn?.Faction == Faction.OfPlayer
                && pawn.Map != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Drafted
                && HasCQBTraining(pawn)
                && TacticalAimUtility.IsRangedVerb(verb)
                && !TacticalHighSpeedMovementUtility.IsSlideLocked(pawn)
                && !states.ContainsKey(pawn.thingIDNumber);
        }

        public static bool CanShow(Pawn pawn)
        {
            return HasAccess(pawn) || IsActive(pawn);
        }

        public static void BeginTargeting(Pawn pawn)
        {
            if (IsActive(pawn))
            {
                Cancel(pawn);
                return;
            }

            if (!HasAccess(pawn))
            {
                return;
            }

            Verb verb = PrimaryVerb(pawn);
            Map map = pawn.Map;
            Action<LocalTargetInfo> drawPreview = target =>
            {
                DrawLinePreview(
                    pawn.Position,
                    target.IsValid ? target.Cell : pawn.Position,
                    verb,
                    map,
                    target.IsValid && IsValidLine(pawn, verb, target));
            };

            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetLocations = true,
                    canTargetPawns = false,
                    canTargetBuildings = false,
                    validator = target => IsValidLine(
                        pawn,
                        verb,
                        new LocalTargetInfo(target.Cell))
                },
                target => Start(pawn, target.Cell),
                drawPreview);

            Helodrace.MapComponent_PersistentTargetingOverlay.Set(map, drawPreview);
        }

        private static bool IsValidLine(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo target)
        {
            if (pawn?.Map == null
                || verb == null
                || !target.IsValid
                || !target.Cell.InBounds(pawn.Map))
            {
                return false;
            }

            float length = pawn.Position.DistanceTo(target.Cell);
            return length >= MinimumLineLength
                && length <= verb.verbProps.range
                && GenSight.LineOfSight(pawn.Position, target.Cell, pawn.Map);
        }

        private static void DrawLinePreview(
            IntVec3 origin,
            IntVec3 destination,
            Verb verb,
            Map map,
            bool valid)
        {
            if (map == null || !origin.InBounds(map))
            {
                return;
            }

            Vector3 start = origin.ToVector3Shifted();
            Vector3 raw = destination.ToVector3Shifted() - start;
            raw.y = 0f;
            if (raw.sqrMagnitude < 0.001f)
            {
                return;
            }

            Vector3 direction = raw.normalized;
            float length = Mathf.Min(
                raw.magnitude,
                verb?.verbProps?.range ?? raw.magnitude);
            Vector3 end = start + direction * length;
            Vector3 lateral = new Vector3(-direction.z, 0f, direction.x)
                * CorridorHalfWidth;
            SimpleColor color = valid ? SimpleColor.Green : SimpleColor.Red;
            GenDraw.DrawLineBetween(start, end, color, 0.08f);
            GenDraw.DrawLineBetween(start - lateral, end - lateral, color, 0.04f);
            GenDraw.DrawLineBetween(start + lateral, end + lateral, color, 0.04f);
            GenDraw.DrawRadiusRing(destination, 0.5f, valid ? Color.green : Color.red);
        }

        private static void Start(Pawn pawn, IntVec3 destination)
        {
            Verb verb = PrimaryVerb(pawn);
            LocalTargetInfo target = new LocalTargetInfo(destination);
            if (!HasAccess(pawn) || !IsValidLine(pawn, verb, target))
            {
                return;
            }

            // This command supersedes an older move order. Later move orders
            // still cancel this firing mode through the ordered-job hook.
            if (!pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait_Combat), JobTag.Misc))
                return;
            pawn.pather?.StopDead();

            Vector3 direction = destination.ToVector3Shifted()
                - pawn.Position.ToVector3Shifted();
            direction.y = 0f;
            float length = Mathf.Min(direction.magnitude, verb.verbProps.range);
            direction.Normalize();

            State state = new State
            {
                pawn = pawn,
                verb = verb,
                origin = pawn.Position,
                destination = destination,
                direction = direction,
                length = length,
                requiresAim = TacticalAimUtility.IsAiming(pawn),
                readyTick = CurrentTick + PreparationTicks,
                nextScanTick = CurrentTick + PreparationTicks
            };
            states[pawn.thingIDNumber] = state;
            // Take control from any automatic attack already warming up.
            verb.Reset();
            if (pawn.stances?.curStance is Stance_Busy busy && busy.verb == verb)
                pawn.stances.CancelBusyStanceHard();
            Messages.Message(
                "HD_TacticalPreemptiveFire_Preparing".Translate(),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static void NotifyPawnTick(Pawn pawn)
        {
            if (!IsActive(pawn)
                || !states.TryGetValue(pawn.thingIDNumber, out State state))
            {
                return;
            }

            if (pawn.Dead
                || pawn.Downed
                || !pawn.Drafted
                || PrimaryVerb(pawn) != state.verb
                || pawn.Map == null
                || pawn.CurJobDef == JobDefOf.Goto
                || pawn.Position != state.origin
                || (state.requiresAim && !TacticalAimUtility.IsAiming(pawn)))
            {
                Cancel(pawn);
                return;
            }

            if (state.firing && !state.verb.Bursting
                && !(pawn.stances?.curStance is Stance_Warmup))
            {
                state.firing = false;
            }

            if (CurrentTick < state.readyTick
                || CurrentTick < state.nextScanTick)
            {
                return;
            }

            state.nextScanTick = CurrentTick + ScanIntervalTicks;
            if (!state.firing) TryFire(state);
        }

        public static void PrepareLineProjectile(Projectile projectile, Thing launcher,
            ref LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
            ref ProjectileHitFlags hitFlags, ref bool preventFriendlyFire)
        {
            if (!(launcher is Pawn pawn)
                || !states.TryGetValue(pawn.thingIDNumber, out State state)
                || !state.firing || intendedTarget.HasThing
                || intendedTarget.Cell != state.destination) return;
            // Keep the physical trajectory fixed; never steer toward a pawn.
            usedTarget = new LocalTargetInfo(state.destination);
            hitFlags = ProjectileHitFlags.All;
            preventFriendlyFire = false;
            lineShots.Remove(projectile);
            lineShots.Add(projectile, new LineShot());
        }

        public static bool RollLineHit(Projectile projectile, Thing thing, bool canHit)
        {
            if (lineShots.TryGetValue(projectile, out LineShot shot))
                shot.candidate = canHit ? thing : null;
            return canHit;
        }

        public static float InterceptDistanceFactor(float original, Projectile projectile)
        {
            // Ordinary stray bullets cannot intercept near their shooter.
            // A deliberately swept firing lane must also work at close range.
            return lineShots.TryGetValue(projectile, out _) ? 1f : original;
        }

        public static bool RollIntercept(float originalChance, Projectile projectile)
        {
            if (!lineShots.TryGetValue(projectile, out LineShot shot)
                || !(shot.candidate is Pawn pawn)) return Rand.Chance(originalChance);
            return RollInterceptForThing(originalChance, projectile, pawn);
        }

        public static bool RollInterceptForThing(float originalChance, Projectile projectile, Thing thing)
        {
            if (!lineShots.TryGetValue(projectile, out LineShot shot)
                || !(thing is Pawn pawn)) return Rand.Chance(originalChance);
            if (!shot.hitRolls.TryGetValue(pawn.thingIDNumber, out bool hit))
                shot.hitRolls[pawn.thingIDNumber] = hit = Rand.Chance(LineHitChance(pawn.BodySize));
            return hit;
        }

        public static float LineHitChance(float bodySize)
        {
            // Standard-sized pawn: 50%. Double the deviation from size 1,
            // rather than doubling the base chance for every pawn.
            return Math.Max(0f, Math.Min(1f, 0.5f * (1f + 2f * (bodySize - 1f))));
        }

        private static void TryFire(State state)
        {
            LocalTargetInfo local = new LocalTargetInfo(state.destination);
            if (state.verb.Bursting
                || CurrentTick < state.nextPairTick
                || state.pawn.stances?.curStance?.StanceBusy == true
                || !state.verb.CanHitTarget(local))
            {
                return;
            }

            state.firing = true;
            state.nextPairTick = CurrentTick + 60;
            if (!state.verb.TryStartCastOn(local, true, true, false, false))
            {
                state.firing = false;
            }
        }

        public static void NotifyShot(Verb verb)
        {
            // Keep the firing flag alive through a multi-shot weapon burst.
            // The tick handler releases it once the verb leaves Burst state.
            if (!IsActive(verb))
            {
                return;
            }
            Pawn pawn = verb.Caster as Pawn;
            if (pawn != null && states.TryGetValue(pawn.thingIDNumber, out State state))
                state.nextPairTick = CurrentTick + 60;
        }

        public static void RegisterReport(ShotReport report)
        {
            reports[report.GetHashCode()] = new ReportState
            {
                tick = CurrentTick
            };
            if (reports.Count <= 256)
            {
                return;
            }

            List<int> expired = reports
                .Where(pair => CurrentTick - pair.Value.tick > ReportLifetimeTicks)
                .Select(pair => pair.Key)
                .ToList();
            for (int i = 0; i < expired.Count; i++)
            {
                reports.Remove(expired[i]);
            }
        }

        public static bool IsPreemptiveReport(ShotReport report)
        {
            return reports.TryGetValue(report.GetHashCode(), out ReportState state)
                && CurrentTick - state.tick <= ReportLifetimeTicks;
        }

        public static void Cancel(Pawn pawn)
        {
            if (pawn != null && states.TryGetValue(pawn.thingIDNumber, out State state))
            {
                states.Remove(pawn.thingIDNumber);
                // Release the burst and stationary cooldown before pathing begins.
                state.verb.Reset();
                if (pawn.stances?.curStance is Stance_Busy busy && busy.verb == state.verb)
                    pawn.stances.CancelBusyStanceHard();
            }
        }

        public static bool AllowsCast(Verb verb, LocalTargetInfo target)
        {
            Pawn pawn = verb?.Caster as Pawn;
            return pawn == null || !states.TryGetValue(pawn.thingIDNumber, out State state)
                || (state.verb == verb && state.firing && !target.HasThing
                    && target.IsValid && target.Cell == state.destination);
        }

        public static void DrawActive(Map map)
        {
            foreach (State state in states.Values)
            {
                if (state.pawn?.Map != map || state.pawn.Position != state.origin)
                {
                    continue;
                }

                Vector3 start = state.origin.ToVector3Shifted();
                Vector3 end = start + state.direction * state.length;
                Vector3 lateral = new Vector3(-state.direction.z, 0f, state.direction.x)
                    * CorridorHalfWidth;
                SimpleColor color = CurrentTick < state.readyTick
                    ? SimpleColor.Yellow
                    : SimpleColor.Red;
                GenDraw.DrawLineBetween(start, end, color, 0.08f);
                GenDraw.DrawLineBetween(start - lateral, end - lateral, color, 0.035f);
                GenDraw.DrawLineBetween(start + lateral, end + lateral, color, 0.035f);
            }
        }

        public static Command CreateCommand(Pawn pawn)
        {
            bool active = IsActive(pawn);
            return new Command_Action
            {
                defaultLabel = (active
                    ? "HD_TacticalPreemptiveFire_Cancel"
                    : "HD_TacticalPreemptiveFire_Command").Translate().ToString(),
                defaultDesc = (active
                    ? "HD_TacticalPreemptiveFire_CancelDesc"
                    : "HD_TacticalPreemptiveFire_CommandDesc").Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false)
                    ?? BaseContent.BadTex,
                action = () => BeginTargeting(pawn)
            };
        }

        private static Verb PrimaryVerb(Pawn pawn)
        {
            return pawn?.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
        }

        private static bool HasCQBTraining(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(CqbTrainingDefName);
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }
    }

    [HarmonyPatch(typeof(Verb), "get_BurstShotCount")]
    public static class Patch_PreemptivePair_BurstCount
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Verb __instance, ref int __result)
        {
            if (TacticalPreemptiveFireUtility.IsActive(__instance)) __result = 2;
        }
    }

    [HarmonyPatch(typeof(Verb_Shoot), "get_ShotsPerBurst")]
    public static class Patch_PreemptivePair_ShotsPerBurst
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Verb_Shoot __instance, ref int __result)
        {
            if (TacticalPreemptiveFireUtility.IsActive(__instance)) __result = 2;
        }
    }

    [HarmonyPatch(typeof(Verb), "get_TicksBetweenBurstShots")]
    public static class Patch_PreemptivePair_Interval
    {
        public static void Postfix(Verb __instance, ref int __result)
        {
            if (TacticalPreemptiveFireUtility.IsActive(__instance)) __result = Math.Max(12, __result);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryStartAttack))]
    public static class Patch_Pawn_AutoAttack_TacticalPreemptiveFire
    {
        public static bool Prefix(Pawn __instance, ref bool __result)
        {
            if (!TacticalPreemptiveFireUtility.IsActive(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    public static class Patch_Verb_CastLock_TacticalPreemptiveFire
    {
        public static IEnumerable<MethodBase> TargetMethods()
            => typeof(Verb).GetMethods().Where(method => method.Name == nameof(Verb.TryStartCastOn));

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb __instance, LocalTargetInfo castTarg, ref bool __result)
        {
            if (TacticalPreemptiveFireUtility.AllowsCast(__instance, castTarg)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Patch_OrderedMove_TacticalPreemptiveFire
    {
        public static void Prefix(Pawn ___pawn, Job job, bool requestQueueing)
        {
            if (!requestQueueing && job?.def == JobDefOf.Goto)
                TacticalPreemptiveFireUtility.Cancel(___pawn);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] {
        typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class Patch_Projectile_Launch_TacticalPreemptiveFire
    {
        public static void Prefix(Projectile __instance, Thing launcher,
            ref LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
            ref ProjectileHitFlags hitFlags, ref bool preventFriendlyFire)
            => TacticalPreemptiveFireUtility.PrepareLineProjectile(__instance, launcher,
                ref usedTarget, intendedTarget, ref hitFlags, ref preventFriendlyFire);
    }

    [HarmonyPatch(typeof(Projectile), "CanHit")]
    public static class Patch_Projectile_CanHit_TacticalPreemptiveFire
    {
        public static void Postfix(Projectile __instance, Thing thing, ref bool __result)
            => __result = TacticalPreemptiveFireUtility.RollLineHit(__instance, thing, __result);
    }

    [HarmonyPatch(typeof(Projectile), "CheckForFreeIntercept")]
    public static class Patch_Projectile_Intercept_TacticalPreemptiveFire
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var distance = AccessTools.Method(typeof(VerbUtility), "InterceptChanceFactorFromDistance");
            var chance = AccessTools.Method(typeof(Rand), nameof(Rand.Chance), new[] { typeof(float) });
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(chance))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(
                        typeof(TacticalPreemptiveFireUtility), nameof(TacticalPreemptiveFireUtility.RollIntercept)));
                }
                else
                {
                    yield return instruction;
                    if (instruction.Calls(distance))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(
                            typeof(TacticalPreemptiveFireUtility), nameof(TacticalPreemptiveFireUtility.InterceptDistanceFactor)));
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "ImpactSomething")]
    public static class Patch_Projectile_Endpoint_TacticalPreemptiveFire
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            var chance = AccessTools.Method(typeof(Rand), nameof(Rand.Chance), new[] { typeof(float) });
            int roll = code.FindLastIndex(instruction => instruction.Calls(chance));
            var thingLocal = __originalMethod.GetMethodBody().LocalVariables
                .FirstOrDefault(local => local.LocalType == typeof(Thing));
            if (roll < 0 || thingLocal == null)
                throw new InvalidOperationException("Preemptive fire: endpoint hit roll not found.");
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            load.labels.AddRange(code[roll].labels);
            code[roll] = load;
            code.Insert(roll + 1, new CodeInstruction(OpCodes.Ldloc, thingLocal.LocalIndex));
            code.Insert(roll + 2, new CodeInstruction(OpCodes.Call, AccessTools.Method(
                typeof(TacticalPreemptiveFireUtility), nameof(TacticalPreemptiveFireUtility.RollInterceptForThing))));
            return code;
        }
    }

    public sealed class MapComponent_TacticalPreemptiveFire : MapComponent
    {
        public MapComponent_TacticalPreemptiveFire(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            TacticalPreemptiveFireUtility.DrawActive(map);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalPreemptiveFire
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction == Faction.OfPlayer
                && TacticalPreemptiveFireUtility.CanShow(__instance))
            {
                __result = __result.Concat(new[]
                {
                    TacticalPreemptiveFireUtility.CreateCommand(__instance)
                });
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_Pawn_Tick_TacticalPreemptiveFire
    {
        public static void Postfix(Pawn __instance)
        {
            TacticalPreemptiveFireUtility.NotifyPawnTick(__instance);
        }
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_TacticalPreemptiveFire
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (TacticalPreemptiveFireUtility.IsActive(__instance))
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_TacticalPreemptiveFire
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result)
            {
                TacticalPreemptiveFireUtility.NotifyShot(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_HitReportFor_TacticalPreemptiveFire
    {
        public static void Postfix(Verb verb, ref ShotReport __result)
        {
            if (TacticalPreemptiveFireUtility.IsActive(verb))
            {
                TacticalPreemptiveFireUtility.RegisterReport(__result);
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimChance_TacticalPreemptiveFire
    {
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (TacticalPreemptiveFireUtility.IsPreemptiveReport(__instance))
            {
                __result = Mathf.Clamp01(__result * 0.90f);
            }
        }
    }
}
