using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Tactical
{
    public enum TacticalMultiTargetMode
    {
        FirePriority,
        SuppressionPriority
    }

    public static class TacticalMultiTargetFireUtility
    {
        public const int MaximumTargets = 3;
        public const float MaximumTargetSpreadAngle = 120f;

        private const int FirePriorityPasses = 2;
        private const int SuppressionShotsPerTarget = 3;
        private const int TickBetweenShots = 1;
        private const string CqbTrainingDefName = "HD_CQBTraining";

        private sealed class State
        {
            public Verb verb;
            public TacticalMultiTargetMode mode;
            public readonly List<LocalTargetInfo> targets =
                new List<LocalTargetInfo>();
            public bool selecting;
            public bool active;
            public bool shotLaunched;
            public int currentIndex;
            public int shotsOnCurrent;
            public int totalShots;
            public int nextActionTick;
        }

        private static readonly Dictionary<int, State> states =
            new Dictionary<int, State>();

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public static bool HasAccess(Pawn pawn)
        {
            return pawn?.Map != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Faction == Faction.OfPlayer
                && TacticalAimUtility.HasCQBTraining(pawn)
                && GetPrimaryVerb(pawn) != null
                && TacticalAimUtility.IsRangedVerb(GetPrimaryVerb(pawn))
                && !GetPrimaryVerb(pawn).Bursting
                && TacticalWeaponRules.AllowsMultiTarget(pawn, GetPrimaryVerb(pawn))
                && (!states.TryGetValue(pawn.thingIDNumber, out State state)
                    || !state.active);
        }

        public static bool IsActive(Verb verb)
        {
            return verb != null
                && states.TryGetValue((verb.Caster as Pawn)?.thingIDNumber ?? -1, out State state)
                && state.active
                && state.verb == verb;
        }

        public static bool IsSequenceActive(Pawn pawn)
        {
            return pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.active;
        }

        private static bool IsMoving(Pawn pawn)
        {
            return pawn?.pather?.MovingNow == true;
        }

        private static Verb GetPrimaryVerb(Pawn pawn)
        {
            return pawn?.equipment?.Primary
                ?.GetComp<CompEquippable>()?.PrimaryVerb;
        }

        public static void BeginModeSelection(Pawn pawn)
        {
            if (!HasAccess(pawn))
            {
                return;
            }

            // A canceled targeter has no completion callback. Reset only a
            // pending selection so the command can be used again cleanly.
            if (pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State pending)
                && !pending.active)
            {
                states.Remove(pawn.thingIDNumber);
            }

            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "HD_TacticalMultiTargetFire_FirePriority".Translate().ToString(),
                    () => BeginTargetSelection(pawn, TacticalMultiTargetMode.FirePriority)),
                new FloatMenuOption(
                    "HD_TacticalMultiTargetFire_SuppressionPriority".Translate().ToString(),
                    () => BeginTargetSelection(pawn, TacticalMultiTargetMode.SuppressionPriority))
            }));
        }

        private static void BeginTargetSelection(
            Pawn pawn,
            TacticalMultiTargetMode mode)
        {
            Verb verb = GetPrimaryVerb(pawn);
            if (pawn == null
                || pawn.Map == null
                || verb == null
                || !TacticalAimUtility.IsRangedVerb(verb)
                || verb.Bursting)
            {
                return;
            }

            State state = new State
            {
                verb = verb,
                mode = mode,
                selecting = true
            };
            states[pawn.thingIDNumber] = state;
            OpenTargeting(pawn, state);
        }

        private static void OpenTargeting(Pawn pawn, State state)
        {
            if (pawn?.Map == null
                || state == null
                || !states.TryGetValue(pawn.thingIDNumber, out State current)
                || current != state)
            {
                return;
            }

            state.selecting = true;

            Map map = pawn.Map;
            Action<LocalTargetInfo> drawPreview = target =>
            {
                for (int i = 0; i < state.targets.Count; i++)
                {
                    DrawTargetMarker(state.targets[i], Color.yellow);
                }

                if (target.IsValid && target.Cell.InBounds(map))
                {
                    DrawTargetMarker(
                        target,
                        IsValidTarget(pawn, target, state)
                            ? Color.green
                            : Color.red);
                }
            };

            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = true,
                    canTargetLocations = false,
                    validator = target => target.Cell.InBounds(map)
                        && IsValidTarget(pawn, target, state)
                },
                target => AddTarget(pawn, target),
                drawPreview);

            Helodrace.MapComponent_PersistentTargetingOverlay.Set(map, drawPreview);
        }

        private static void DrawTargetMarker(LocalTargetInfo target, Color color)
        {
            if (target.IsValid)
            {
                GenDraw.DrawRadiusRing(target.Cell, 0.55f, color);
            }
        }

        private static LocalTargetInfo ToLocalTarget(TargetInfo target)
        {
            return target.HasThing
                ? new LocalTargetInfo(target.Thing)
                : new LocalTargetInfo(target.Cell);
        }

        private static bool IsValidTarget(
            Pawn pawn,
            TargetInfo target,
            State state)
        {
            return IsValidTarget(pawn, ToLocalTarget(target), state);
        }

        private static bool IsValidTarget(
            Pawn pawn,
            LocalTargetInfo target,
            State state)
        {
            if (!CanShootTarget(pawn, target, state))
            {
                return false;
            }

            for (int i = 0; i < state.targets.Count; i++)
            {
                if (SameTarget(state.targets[i], target))
                {
                    return false;
                }
            }

            if (state.targets.Count == 0)
            {
                return true;
            }

            if (!TacticalAimUtility.TryGetTargetAngle(
                pawn,
                target,
                out float targetAngle))
            {
                return false;
            }

            for (int i = 0; i < state.targets.Count; i++)
            {
                if (!TacticalAimUtility.TryGetTargetAngle(
                    pawn,
                    state.targets[i],
                    out float selectedAngle)
                    || Mathf.Abs(Mathf.DeltaAngle(selectedAngle, targetAngle))
                        > MaximumTargetSpreadAngle)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CanShootTarget(Pawn pawn, LocalTargetInfo target, State state)
        {
            return pawn?.Map != null && state != null && target.HasThing
                && target.Thing != pawn && target.Thing.Spawned
                && target.Thing.Map == pawn.Map
                && !(target.Thing is Pawn victim && victim.Dead)
                && TacticalAimUtility.CanFireAt(pawn, target)
                && state.verb.CanHitTarget(target);
        }

        private static bool SameTarget(
            LocalTargetInfo first,
            LocalTargetInfo second)
        {
            return first.HasThing && second.HasThing
                ? first.Thing == second.Thing
                : !first.HasThing && !second.HasThing && first.Cell == second.Cell;
        }

        private static void AddTarget(Pawn pawn, LocalTargetInfo target)
        {
            if (!states.TryGetValue(pawn?.thingIDNumber ?? -1, out State state)
                || state == null)
            {
                return;
            }

            state.selecting = false;
            if (!IsValidTarget(pawn, target, state))
            {
                Messages.Message(
                    "HD_TacticalMultiTargetFire_InvalidTarget".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    false);
                states.Remove(pawn.thingIDNumber);
                return;
            }

            state.targets.Add(target);
            if (state.targets.Count >= MaximumTargets)
            {
                StartSequence(pawn, state);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "HD_TacticalMultiTargetFire_AddTarget".Translate(
                        state.targets.Count,
                        MaximumTargets).ToString(),
                    () => OpenTargeting(pawn, state)),
                new FloatMenuOption(
                    "HD_TacticalMultiTargetFire_Start".Translate(
                        state.targets.Count).ToString(),
                    () => StartSequence(pawn, state)),
                new FloatMenuOption(
                    "CancelButton".Translate().ToString(),
                    () => states.Remove(pawn.thingIDNumber))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void StartSequence(Pawn pawn, State state)
        {
            if (pawn == null
                || state == null
                || state.targets.Count == 0
                || !states.TryGetValue(pawn.thingIDNumber, out State current)
                || current != state)
            {
                return;
            }

            Verb verb = GetPrimaryVerb(pawn);
            if (verb == null
                || verb != state.verb
                || verb.Bursting
                || !HasValidState(pawn, state))
            {
                states.Remove(pawn.thingIDNumber);
                return;
            }

            if (!TacticalWeaponRules.AllowsMultiTarget(pawn, verb))
            {
                states.Remove(pawn.thingIDNumber);
                return;
            }

            state.selecting = false;
            state.active = true;
            state.currentIndex = 0;
            state.shotsOnCurrent = 0;
            state.totalShots = 0;
            state.nextActionTick = CurrentTick;
            TryFireCurrentTarget(pawn, state);
        }

        private static bool HasValidState(Pawn pawn, State state)
        {
            return pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Map != null
                && TacticalAimUtility.IsRangedVerb(state.verb)
                && GetPrimaryVerb(pawn) == state.verb;
        }

        private static void TryFireCurrentTarget(Pawn pawn, State state)
        {
            if (!state.active || state.selecting || CurrentTick < state.nextActionTick)
            {
                return;
            }

            if (!HasValidState(pawn, state)
                || state.verb.Bursting
                || TacticalMovementRunAndGunUtility.IsTacticalStance(pawn.stances?.curStance)
                || pawn.stances?.curStance?.StanceBusy == true)
            {
                return;
            }

            if (state.currentIndex >= state.targets.Count)
            {
                Finish(pawn, state);
                return;
            }

            LocalTargetInfo target = state.targets[state.currentIndex];
            if (!CanShootTarget(pawn, target, state))
            {
                AdvanceAfterUnavailableTarget(pawn, state);
                return;
            }

            state.shotLaunched = false;
            bool started = state.verb.TryStartCastOn(
                target,
                true,
                true,
                false,
                false);
            if (!started)
            {
                AdvanceAfterUnavailableTarget(pawn, state);
                return;
            }

            state.nextActionTick = CurrentTick + TickBetweenShots;
        }

        private static void AdvanceAfterUnavailableTarget(Pawn pawn, State state)
        {
            state.shotsOnCurrent = 0;
            state.currentIndex++;
            if (state.currentIndex >= state.targets.Count)
            {
                Finish(pawn, state);
                return;
            }

            state.nextActionTick = CurrentTick + TickBetweenShots;
        }

        public static void NotifyShot(Verb verb)
        {
            Pawn pawn = verb?.Caster as Pawn;
            if (pawn != null && states.TryGetValue(pawn.thingIDNumber, out State state)
                && state.active && state.verb == verb) state.shotLaunched = true;
        }

        public static void CompleteShot(Verb verb)
        {
            Pawn pawn = verb?.Caster as Pawn;
            if (pawn == null
                || !states.TryGetValue(pawn.thingIDNumber, out State state)
                || !state.active
                || state.verb != verb
                || !state.shotLaunched)
            {
                return;
            }

            // Advance once per shell, after the mobile cooldown is installed.
            state.shotLaunched = false;
            state.totalShots++;
            state.shotsOnCurrent++;

            if (state.mode == TacticalMultiTargetMode.FirePriority)
            {
                if (state.totalShots >= state.targets.Count * FirePriorityPasses)
                {
                    Finish(pawn, state);
                    return;
                }

                state.currentIndex = (state.currentIndex + 1) % state.targets.Count;
            }
            else if (TargetIsSuppressed(state.targets[state.currentIndex])
                || state.shotsOnCurrent >= SuppressionShotsPerTarget)
            {
                state.currentIndex++;
                state.shotsOnCurrent = 0;
                if (state.currentIndex >= state.targets.Count)
                {
                    Finish(pawn, state);
                    return;
                }
            }

            state.nextActionTick = CurrentTick + TickBetweenShots;
        }

        private static bool TargetIsSuppressed(LocalTargetInfo target)
        {
            Pawn pawn = target.Thing as Pawn;
            return pawn == null
                ? !target.IsValid || target.Thing.Destroyed
                : pawn.Dead
                    || pawn.Downed
                    || pawn.health?.summaryHealth?.SummaryHealthPercent <= 0.35f;
        }

        private static void Finish(Pawn pawn, State state)
        {
            if (pawn != null
                && states.TryGetValue(pawn.thingIDNumber, out State current)
                && current == state)
            {
                states.Remove(pawn.thingIDNumber);
                Messages.Message(
                    "HD_TacticalMultiTargetFire_Finished".Translate(),
                    pawn,
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }
        }

        public static int BurstCount(Verb verb)
        {
            return IsActive(verb) ? 1 : 0;
        }

        public static Command CreateCommand(Pawn pawn)
        {
            return new Command_Action
            {
                defaultLabel = "HD_TacticalMultiTargetFire_Command".Translate().ToString(),
                defaultDesc = "HD_TacticalMultiTargetFire_CommandDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_MovingFire", false)
                    ?? BaseContent.BadTex,
                action = () => BeginModeSelection(pawn)
            };
        }

        public static void Tick(Pawn pawn)
        {
            if (pawn == null
                || !states.TryGetValue(pawn.thingIDNumber, out State state))
            {
                return;
            }

            if (!state.active)
            {
                return;
            }

            if (!HasValidState(pawn, state))
            {
                states.Remove(pawn.thingIDNumber);
                return;
            }

            TryFireCurrentTarget(pawn, state);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalMultiTargetFire
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (TacticalMultiTargetFireUtility.HasAccess(__instance))
            {
                __result = __result.Concat(new[]
                {
                    TacticalMultiTargetFireUtility.CreateCommand(__instance)
                });
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "get_BurstShotCount")]
    public static class Patch_Verb_BurstShotCount_TacticalMultiTargetFire
    {
        public static void Postfix(Verb __instance, ref int __result)
        {
            int count = TacticalMultiTargetFireUtility.BurstCount(__instance);
            if (count > 0)
            {
                __result = count;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_Shoot), "get_ShotsPerBurst")]
    public static class Patch_Verb_ShotsPerBurst_TacticalMultiTargetFire
    {
        public static void Postfix(Verb_Shoot __instance, ref int __result)
        {
            int count = TacticalMultiTargetFireUtility.BurstCount(__instance);
            if (count > 0)
            {
                __result = count;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_TacticalMultiTargetFire
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result)
            {
                TacticalMultiTargetFireUtility.NotifyShot(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_BurstShotComplete_TacticalMultiTargetFire
    {
        public static void Postfix(Verb __instance)
            => TacticalMultiTargetFireUtility.CompleteShot(__instance);
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.EquipmentTrackerTick))]
    public static class Patch_EquipmentTrackerTick_TacticalMultiTargetFire
    {
        public static void Postfix(Pawn_EquipmentTracker __instance)
        {
            TacticalMultiTargetFireUtility.Tick(__instance?.pawn);
        }
    }
}
