using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    public static class Patch_TacticalSlide_DisableRenderCache
    {
        public static void Prefix(Pawn ___pawn, ref bool disableCache)
        {
            if (TacticalHighSpeedMovementUtility.IsSliding(___pawn))
                disableCache = true;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Patch_TacticalSlide_RejectOrders
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (!TacticalHighSpeedMovementUtility.IsSlideLocked(___pawn)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StopAll))]
    public static class Patch_TacticalSlide_RejectStop
    {
        public static bool Prefix(Pawn ___pawn)
            => !TacticalHighSpeedMovementUtility.IsSlideLocked(___pawn);
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_TacticalSlide_RejectInterrupt
    {
        public static bool Prefix(Pawn ___pawn, JobCondition condition)
        {
            return condition != JobCondition.InterruptForced
                || !TacticalHighSpeedMovementUtility.IsSlideLocked(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    public static class Patch_TacticalSlide_RejectUndraft
    {
        public static bool Prefix(Pawn ___pawn, bool value)
            => value || !TacticalHighSpeedMovementUtility.IsSlideLocked(___pawn);
    }
}
