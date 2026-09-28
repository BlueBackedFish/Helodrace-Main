using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Helodrace.Tactical
{
    public static class TacticalMultiTargetAccuracy
    {
        private static readonly FieldInfo ShooterFactor = AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");
        private sealed class Entry
        {
            public ShotReport report;
            public int tick;
        }
        private static readonly Dictionary<int, Entry> closeReports = new Dictionary<int, Entry>();

        public static float AdjustShooter(float x)
        {
            x = Math.Max(0f, Math.Min(1f, x));
            return x * x * (3f - 2f * x);
        }

        public static float CloseRangeChance(float chance, float distance)
            => Math.Max(0f, Math.Min(1f, distance <= 7f ? chance * 2f : chance));

        public static void Register(Thing caster, Verb verb, LocalTargetInfo target, ref ShotReport report)
        {
            if (!TacticalMultiTargetFireUtility.IsActive(verb)) return;
            object boxed = report;
            ShooterFactor.SetValue(boxed, AdjustShooter((float)ShooterFactor.GetValue(boxed)));
            report = (ShotReport)boxed;
            TacticalMovementUtility.RegisterShot(report, caster as Pawn, verb);
            if (caster.Position.DistanceToSquared(target.Cell) > 49) return;
            int tick = Find.TickManager.TicksGame;
            if (closeReports.Count > 256) closeReports.Clear();
            closeReports[report.GetHashCode()] = new Entry { report = report, tick = tick };
        }

        public static bool IsCloseReport(ShotReport report)
            => closeReports.TryGetValue(report.GetHashCode(), out Entry entry)
                && entry.tick == (Find.TickManager?.TicksGame ?? -1) && entry.report.Equals(report);
    }

    [HarmonyPatch(typeof(Verb), "get_WarmupTime")]
    public static class Patch_MultiTarget_FixedWarmup
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (TacticalMultiTargetFireUtility.IsActive(__instance)) __result = 0.2f;
        }
    }

    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.SetStance))]
    public static class Patch_MultiTarget_FixedStanceTime
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(ref Stance newStance)
        {
            if (!(newStance is Stance_Busy busy) || !TacticalMultiTargetFireUtility.IsActive(busy.verb)) return;
            // Apply after all aiming-delay and moving-fire multipliers.
            if (newStance is Stance_Warmup)
                newStance = newStance is Stance_TacticalMovementWarmup
                    ? new Stance_TacticalMovementWarmup(12, busy.focusTarg, busy.verb)
                    : new Stance_Warmup(12, busy.focusTarg, busy.verb);
            else if (newStance is Stance_Cooldown)
                newStance = new Stance_Mobile();
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_MultiTarget_ShooterAccuracy
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Thing caster, Verb verb, LocalTargetInfo target, ref ShotReport __result)
            => TacticalMultiTargetAccuracy.Register(caster, verb, target, ref __result);
    }

    [HarmonyPatch(typeof(ShotReport), "get_AimOnTargetChance_StandardTarget")]
    public static class Patch_MultiTarget_CloseAccuracy
    {
        [HarmonyPriority(Priority.Last - 100)]
        public static void Postfix(ShotReport __instance, ref float __result)
        {
            if (TacticalMultiTargetAccuracy.IsCloseReport(__instance))
                __result = TacticalMultiTargetAccuracy.CloseRangeChance(__result, 7f);
        }
    }
}
