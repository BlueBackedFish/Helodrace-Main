using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public static class TacticalMovementFireUtility
    {
        private const string CqbTrainingDefName = "HD_CQBTraining";
        private const int PreviewRange = 30;

        private sealed class State
        {
            public Pawn pawn;
            public Verb verb;
            public IntVec3 destination;
            public LocalTargetInfo target;
            public int originCellIndex;
        }

        private static readonly Dictionary<int, State> states =
            new Dictionary<int, State>();

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
                && !verb.Bursting
                && !TacticalHighSpeedMovementUtility.IsActive(pawn)
                && !states.ContainsKey(pawn.thingIDNumber);
        }

        public static bool IsActive(Pawn pawn)
        {
            return pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.pawn == pawn;
        }

        public static bool IsActive(Verb verb)
        {
            return verb != null
                && states.TryGetValue((verb.Caster as Pawn)?.thingIDNumber ?? -1, out State state)
                && state.verb == verb;
        }

        public static bool IsCurrentTarget(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo target)
        {
            return IsActive(pawn)
                && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.verb == verb
                && SameTarget(state.target, target);
        }

        public static void BeginTargeting(Pawn pawn)
        {
            if (!HasAccess(pawn))
            {
                return;
            }

            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetLocations = true,
                    canTargetPawns = false,
                    canTargetBuildings = false,
                    validator = target => IsValidDestination(
                        pawn,
                        new LocalTargetInfo(target.Cell))
                },
                target => BeginTargetSelection(pawn, target.Cell),
                target => DrawDestinationPreview(
                    pawn,
                    target.HasThing
                        ? new LocalTargetInfo(target.Thing)
                        : new LocalTargetInfo(target.Cell)));
        }

        private static void DrawDestinationPreview(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn?.Map == null)
            {
                return;
            }

            GenDraw.DrawRadiusRing(pawn.Position, PreviewRange, Color.white);
            if (!target.IsValid || !target.Cell.InBounds(pawn.Map))
            {
                return;
            }

            GenDraw.DrawRadiusRing(
                target.Cell,
                0.55f,
                IsValidDestination(pawn, target)
                    ? Color.green
                    : Color.red);
        }

        private static bool IsValidDestination(Pawn pawn, LocalTargetInfo target)
        {
            return pawn?.Map != null
                && target.IsValid
                && target.Cell.InBounds(pawn.Map)
                && target.Cell.Walkable(pawn.Map)
                && pawn.Position.DistanceTo(target.Cell) <= PreviewRange
                && pawn.CanReach(target.Cell, PathEndMode.OnCell, Danger.Deadly);
        }

        private static void BeginTargetSelection(Pawn pawn, IntVec3 destination)
        {
            if (!IsValidDestination(pawn, new LocalTargetInfo(destination)))
            {
                return;
            }

            Verb verb = PrimaryVerb(pawn);
            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = true,
                    canTargetLocations = false,
                    validator = target => IsValidFireTarget(pawn, verb, target)
                },
                target => Start(pawn, destination, target),
                target => DrawTargetPreview(pawn, destination, verb, target));
        }

        private static void DrawTargetPreview(
            Pawn pawn,
            IntVec3 destination,
            Verb verb,
            LocalTargetInfo target)
        {
            if (pawn?.Map == null)
            {
                return;
            }

            GenDraw.DrawRadiusRing(destination, 0.55f, Color.cyan);
            if (!target.IsValid || !target.Cell.InBounds(pawn.Map))
            {
                return;
            }

            GenDraw.DrawRadiusRing(
                target.Cell,
                0.55f,
                IsValidFireTarget(pawn, verb, target)
                    ? Color.green
                    : Color.red);
        }

        private static bool IsValidFireTarget(
            Pawn pawn,
            Verb verb,
            TargetInfo target)
        {
            LocalTargetInfo local = target.HasThing
                ? new LocalTargetInfo(target.Thing)
                : new LocalTargetInfo(target.Cell);
            return IsValidFireTarget(pawn, verb, local);
        }

        private static bool IsValidFireTarget(
            Pawn pawn,
            Verb verb,
            LocalTargetInfo target)
        {
            return pawn?.Map != null
                && verb != null
                && TacticalAimUtility.IsRangedVerb(verb)
                && target.IsValid
                && target.HasThing
                && target.Thing != pawn
                && target.Thing.Spawned
                && target.Thing.Map == pawn.Map
                && !(target.Thing is Pawn victim && victim.Dead)
                && target.Cell.InBounds(pawn.Map);
        }

        private static bool CanFireNow(Pawn pawn, Verb verb, LocalTargetInfo target)
        {
            return IsValidFireTarget(pawn, verb, target)
                && verb.CanHitTarget(target)
                && (!TacticalAimUtility.IsAiming(pawn)
                    || TacticalAimUtility.CanFireAt(pawn, target));
        }

        private static void Start(
            Pawn pawn,
            IntVec3 destination,
            LocalTargetInfo target)
        {
            Verb verb = PrimaryVerb(pawn);
            if (!HasAccess(pawn)
                || !IsValidDestination(pawn, new LocalTargetInfo(destination))
                || !IsValidFireTarget(pawn, verb, target))
            {
                return;
            }

            State state = new State
            {
                pawn = pawn,
                verb = verb,
                destination = destination,
                target = target,
                originCellIndex = pawn.Map.cellIndices.CellToIndex(pawn.Position)
            };
            states[pawn.thingIDNumber] = state;

            Job job = JobMaker.MakeJob(JobDefOf.Goto, destination);
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                states.Remove(pawn.thingIDNumber);
                return;
            }

            Messages.Message(
                "HD_TacticalMovementFire_Started".Translate(),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static bool TryStartCurrentAttack(Pawn pawn)
        {
            if (!IsActive(pawn)
                || pawn.jobs?.curJob?.def != JobDefOf.Goto
                || TacticalMovementRunAndGunUtility.IsTacticalStance(pawn.stances?.curStance)
                || pawn.stances?.curStance?.StanceBusy == true)
            {
                return false;
            }

            if (!states.TryGetValue(pawn.thingIDNumber, out State state)
                || state.verb.Bursting
                || !CanFireNow(pawn, state.verb, state.target))
            {
                return false;
            }

            if (state.target.HasThing)
            {
                return state.verb.TryStartCastOn(state.target, false, true, false, false);
            }

            return false;
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
                || pawn.Map.cellIndices.CellToIndex(pawn.Position) == state.originCellIndex
                    && pawn.jobs?.curJob?.def != JobDefOf.Goto
                || !IsValidFireTarget(pawn, state.verb, state.target))
            {
                Cancel(pawn);
                return;
            }

            if (pawn.jobs?.curJob?.def != JobDefOf.Goto
                && pawn.pather?.MovingNow != true
                && pawn.stances?.curStance?.StanceBusy != true)
            {
                Cancel(pawn);
            }
        }

        public static void Cancel(Pawn pawn)
        {
            if (pawn != null)
            {
                states.Remove(pawn.thingIDNumber);
            }
        }

        public static Command CreateCommand(Pawn pawn)
        {
            return new Command_Action
            {
                defaultLabel = "HD_TacticalMovementFire_Command".Translate().ToString(),
                defaultDesc = "HD_TacticalMovementFire_CommandDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_MovingFire", false)
                    ?? ContentFinder<Texture2D>.Get("UI/Commands/Attack", false)
                    ?? BaseContent.BadTex,
                action = () => BeginTargeting(pawn)
            };
        }

        private static Verb PrimaryVerb(Pawn pawn)
        {
            return pawn?.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
        }

        private static bool SameTarget(
            LocalTargetInfo first,
            LocalTargetInfo second)
        {
            return first.HasThing && second.HasThing
                ? first.Thing == second.Thing
                : !first.HasThing && !second.HasThing && first.Cell == second.Cell;
        }

        private static bool HasCQBTraining(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(CqbTrainingDefName);
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalMovementFire
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (TacticalMovementFireUtility.HasAccess(__instance))
            {
                __result = __result.Concat(new[]
                {
                    TacticalMovementFireUtility.CreateCommand(__instance)
                });
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_Pawn_Tick_TacticalMovementFire
    {
        public static void Postfix(Pawn __instance)
        {
            TacticalMovementFireUtility.NotifyPawnTick(__instance);
        }
    }

}
