using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    // The 1.7 render tree caches draw requests and vanilla only recaches for
    // hediffs with graphic properties. Losing or restoring an ear or the tail
    // must rebuild the tree so the appendage graphic follows the body part.
    public static class HelodAppendageRecache
    {
        public static void Notify(Pawn pawn, Hediff hediff)
        {
            if (!(hediff is Hediff_MissingPart) || hediff.Part == null || !HelodRace.IsHelod(pawn)) return;
            if (!AffectsAppendage(hediff.Part)) return;
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
        }

        // A missing ear, tail or any ancestor (head, torso) removes the appendage.
        private static bool AffectsAppendage(BodyPartRecord part)
        {
            return Contains(HelodRace.TailParts, part) || Contains(HelodRace.LeftEarParts, part)
                || Contains(HelodRace.RightEarParts, part);
        }

        private static bool Contains(List<BodyPartRecord> appendages, BodyPartRecord part)
        {
            for (int i = 0; i < appendages.Count; i++)
                for (BodyPartRecord node = appendages[i]; node != null; node = node.parent)
                    if (node == part) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.AddDirect))]
    public static class Patch_HelodAppendageMissingAdded
    {
        public static void Postfix(HediffSet __instance, Hediff hediff)
            => HelodAppendageRecache.Notify(__instance.pawn, hediff);
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    public static class Patch_HelodAppendageMissingRemoved
    {
        public static void Postfix(Pawn ___pawn, Hediff hediff)
            => HelodAppendageRecache.Notify(___pawn, hediff);
    }
}
