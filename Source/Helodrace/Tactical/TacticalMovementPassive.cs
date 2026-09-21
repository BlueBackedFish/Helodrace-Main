using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Tactical
{
    public enum TacticalMovementMode
    {
        Accuracy,
        Response
    }

    public sealed class Hediff_TacticalMovement : Hediff
    {
        private TacticalMovementMode mode = TacticalMovementMode.Accuracy;

        public TacticalMovementMode Mode => mode;

        public void SetMode(TacticalMovementMode value)
        {
            mode = value;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref mode,
                "mode",
                TacticalMovementMode.Accuracy);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null
                || pawn.Dead
                || pawn.Downed
                || !TacticalAimUtility.HasCQBTraining(pawn))
            {
                pawn?.health?.RemoveHediff(this);
            }
        }

        public override string TipStringExtra
        {
            get
            {
                return "HD_TacticalMovement_CurrentMode".Translate(
                    TacticalMovementUtility.LabelFor(mode));
            }
        }
    }

    public static class TacticalMovementUtility
    {
        private const string MovementDefName = "HD_TacticalMovement";
        private const float AccuracyWarmupMultiplier = 1.8f;
        private const float ResponseWarmupMultiplier = 1.1f;
        private const float ResponseAccuracyMultiplier = 0.72f;
        private const int ShotStateLifetimeTicks = 2;

        private struct ShotState
        {
            public TacticalMovementMode mode;
            public int tick;
        }

        private static readonly Dictionary<int, ShotState> movingShots =
            new Dictionary<int, ShotState>();

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;

        public static bool CanUse(Pawn pawn)
        {
            return pawn?.Map != null
                && !pawn.Dead
                && !pawn.Downed
                && TacticalAimUtility.HasCQBTraining(pawn);
        }

        public static TacticalMovementMode ModeFor(Pawn pawn)
        {
            return Get(pawn)?.Mode ?? TacticalMovementMode.Accuracy;
        }

        public static Hediff_TacticalMovement Get(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(MovementDefName);
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) as Hediff_TacticalMovement;
        }

        public static void SetMode(Pawn pawn, TacticalMovementMode mode)
        {
            if (!CanUse(pawn))
            {
                return;
            }

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(MovementDefName);
            if (def == null)
            {
                Log.ErrorOnce(
                    "Helodrace: missing HD_TacticalMovement HediffDef.",
                    180426071);
                return;
            }

            Hediff_TacticalMovement movement = Get(pawn);
            if (movement == null)
            {
                movement = pawn.health.AddHediff(def) as Hediff_TacticalMovement;
            }

            movement?.SetMode(mode);
            Messages.Message(
                "HD_TacticalMovement_Selected".Translate(LabelFor(mode)),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static void ShowSelectionMenu(Pawn pawn)
        {
            if (!CanUse(pawn))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "HD_TacticalMovement_Accuracy".Translate().ToString(),
                    () => SetMode(pawn, TacticalMovementMode.Accuracy)),
                new FloatMenuOption(
                    "HD_TacticalMovement_Response".Translate().ToString(),
                    () => SetMode(pawn, TacticalMovementMode.Response))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static string LabelFor(TacticalMovementMode mode)
        {
            return mode == TacticalMovementMode.Response
                ? "HD_TacticalMovement_Response".Translate().ToString()
                : "HD_TacticalMovement_Accuracy".Translate().ToString();
        }

        public static Command CreateCommand(Pawn pawn)
        {
            return new Command_Action
            {
                defaultLabel = "HD_TacticalMovement_Command".Translate(
                    LabelFor(ModeFor(pawn))).ToString(),
                defaultDesc = "HD_TacticalMovement_CommandDesc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Move", false)
                    ?? ContentFinder<Texture2D>.Get("UI/Commands/Attack", false)
                    ?? BaseContent.BadTex,
                action = () => ShowSelectionMenu(pawn)
            };
        }

        public static bool TryGetWarmupMultiplier(Verb verb, out float multiplier)
        {
            multiplier = 1f;
            Pawn pawn = verb?.Caster as Pawn;
            if (!IsMovingTacticalShot(pawn, verb))
            {
                return false;
            }

            multiplier = ModeFor(pawn) == TacticalMovementMode.Response
                ? ResponseWarmupMultiplier
                : AccuracyWarmupMultiplier;
            return true;
        }

        public static void RegisterShot(ShotReport report, Pawn pawn, Verb verb)
        {
            if (!IsMovingTacticalShot(pawn, verb))
            {
                return;
            }

            movingShots[report.GetHashCode()] = new ShotState
            {
                mode = ModeFor(pawn),
                tick = CurrentTick
            };
        }

        public static bool TryGetAccuracyMultiplier(
            ShotReport report,
            out float multiplier)
        {
            multiplier = 1f;
            if (!movingShots.TryGetValue(report.GetHashCode(), out ShotState state))
            {
                return false;
            }

            if (CurrentTick - state.tick > ShotStateLifetimeTicks)
            {
                movingShots.Remove(report.GetHashCode());
                return false;
            }

            multiplier = state.mode == TacticalMovementMode.Response
                ? ResponseAccuracyMultiplier
                : 1f;
            return true;
        }

        private static bool IsMovingTacticalShot(Pawn pawn, Verb verb)
        {
            if (pawn?.pather?.MovingNow != true
                || !TacticalAimUtility.IsRangedVerb(verb))
            {
                return false;
            }

            return TacticalSuddenFireUtility.IsActive(verb)
                || TacticalAimUtility.IsAiming(pawn)
                || TacticalMovementFireUtility.IsActive(pawn)
                || TacticalMultiTargetFireUtility.IsActive(verb);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalMovement
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!TacticalCqbModeUtility.ShowCommands(__instance)) return;
            if (__instance?.Faction != Faction.OfPlayer
                || !TacticalMovementUtility.CanUse(__instance))
            {
                return;
            }

            __result = __result.Concat(new[]
            {
                TacticalMovementUtility.CreateCommand(__instance)
            });
        }
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_Verb_WarmupTime_TacticalMovement
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (TacticalMovementUtility.TryGetWarmupMultiplier(
                __instance,
                out float multiplier))
            {
                __result *= multiplier;
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_ShotReport_HitReportFor_TacticalMovement
    {
        public static void Postfix(
            Thing caster,
            Verb verb,
            ref ShotReport __result)
        {
            TacticalMovementUtility.RegisterShot(
                __result,
                caster as Pawn,
                verb);
        }
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_ShotReport_AimChance_TacticalMovement
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (TacticalMovementUtility.TryGetAccuracyMultiplier(
                __instance,
                out float multiplier))
            {
                __result = Mathf.Clamp01(__result * multiplier);
            }
        }
    }
}
