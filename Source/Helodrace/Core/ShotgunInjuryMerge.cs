using System;
using HarmonyLib;
using Verse;

namespace Helodrace
{
    internal sealed class ShotgunInjuryMerge
    {
        [ThreadStatic] internal static ShotgunInjuryMerge Current;
        internal Hediff_Injury incoming;
        internal Hediff_Injury target;
        internal ShotgunImpact impact;
        internal GameComponent_ShotgunWounds ledger;
        internal bool merged;

        internal bool CanMerge(Hediff_Injury existing)
        {
            return ledger.For(existing)?.shellId == impact.shell.id
                && existing != incoming && existing.pawn == incoming.pawn
                && incoming.Part != null && existing.Part == incoming.Part && existing.def == incoming.def
                && existing.destroysBodyParts == incoming.destroysBodyParts
                && existing.Severity + incoming.Severity <= existing.def.maxSeverity
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
            GameComponent_ShotgunWounds ledger = GameComponent_ShotgunWounds.Current;
            ShotgunInjuryMerge.Current = impact != null && impact.pawn == __instance.pawn
                && ledger != null && dinfo.HasValue && dinfo.Value.Amount > 0f && hediff is Hediff_Injury injury
                ? new ShotgunInjuryMerge { incoming = injury, impact = impact, ledger = ledger }
                : null;
        }

        internal static void Postfix()
        {
            ShotgunInjuryMerge context = ShotgunInjuryMerge.Current;
            if (context?.impact.pellet != null)
                context.ledger.Register(context.target ?? context.incoming,
                    context.impact.shell.id, context.impact.pellet.thingIDNumber);
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

            if (!context.CanMerge(__instance))
            {
                // Even normally mergeable injury types stay separate across
                // shells, preserving previous hits and treatment history.
                __result = false;
                return false;
            }
            context.merged = true;
            context.target = __instance;
            __instance.Severity += context.incoming.Severity;
            __instance.ageTicks = 0;
            if (__instance.comps != null)
                foreach (HediffComp comp in __instance.comps) comp.CompPostMerged(other);
            __result = true;
            return false;
        }
    }
}
