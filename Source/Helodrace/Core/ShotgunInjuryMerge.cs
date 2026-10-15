using System;
using HarmonyLib;
using Verse;

namespace Helodrace
{
    internal sealed class ShotgunInjuryMerge
    {
        [ThreadStatic] internal static ShotgunInjuryMerge Current;
        internal Hediff_Injury incoming;
        internal bool merged;

        internal static bool CanMerge(Hediff_Injury existing, Hediff_Injury incoming)
        {
            return existing != incoming && existing.pawn == incoming.pawn
                && incoming.Part != null && existing.Part == incoming.Part && existing.def == incoming.def
                && existing.destroysBodyParts == incoming.destroysBodyParts
                && !existing.IsTended() && !incoming.IsTended()
                && !existing.IsPermanent() && !incoming.IsPermanent();
        }
    }

    // Keep the game's injury addition, part destruction and health notifications.
    // Only enable merging during injury addition from an actual pellet impact.
    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.AddDirect))]
    public static class Patch_HediffSet_ShotgunInjuryMerge
    {
        internal static void Prefix(HediffSet __instance, Hediff hediff, DamageInfo? dinfo,
            out ShotgunInjuryMerge __state)
        {
            __state = ShotgunInjuryMerge.Current;
            ShotgunImpact impact = ShotgunSpreadContext.Impact;
            ShotgunInjuryMerge.Current = impact != null && impact.pawn == __instance.pawn
                && dinfo.HasValue && dinfo.Value.Amount > 0f && hediff is Hediff_Injury injury
                ? new ShotgunInjuryMerge { incoming = injury }
                : null;
        }

        internal static void Finalizer(ShotgunInjuryMerge __state)
        {
            ShotgunInjuryMerge.Current = __state;
        }
    }

    [HarmonyPatch(typeof(Hediff_Injury), nameof(Hediff_Injury.TryMergeWith))]
    public static class Patch_HediffInjury_ShotgunInjuryMerge
    {
        public static bool Prefix(Hediff_Injury __instance, Hediff other, ref bool __result)
        {
            ShotgunInjuryMerge context = ShotgunInjuryMerge.Current;
            if (context == null || context.incoming != other) return true;

            // AddDirect tries every existing hediff even after a successful merge.
            // A pellet's severity must be added to only one of those injuries.
            if (context.merged)
            {
                __result = false;
                return false;
            }

            if (!ShotgunInjuryMerge.CanMerge(__instance, context.incoming)) return true;
            context.merged = true;
            __instance.Severity += context.incoming.Severity;
            __instance.ageTicks = 0;
            if (__instance.comps != null)
                foreach (HediffComp comp in __instance.comps) comp.CompPostMerged(other);
            __result = true;
            return false;
        }
    }
}
