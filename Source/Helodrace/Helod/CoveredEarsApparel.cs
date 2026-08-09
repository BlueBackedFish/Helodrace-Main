using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    /// <summary>
    /// Keeps the Helod covered-ear hediff in sync with apparel which hides
    /// the ear render nodes. The hediff effects and associated thought remain
    /// data-driven in XML.
    /// </summary>
    public static class HelodCoveredEarsUtility
    {
        private const string HelodDefName = "Helod";
        private const string CoveredEarsHediffDefName = "HD_HelodCoveredEars";

        private static readonly HashSet<string> EarCoveringApparelDefNames =
            new HashSet<string>
            {
                "HD_Apparel_CarvalyHat",
                "Apparel_AdvancedHelmet",
                "Apparel_BowlerHat",
                "Apparel_CowboyHat",
                "Apparel_HatHood",
                "Apparel_ArmorHelmetRecon",
                "Apparel_PowerArmorHelmet",
                "Apparel_PsychicFoilHelmet",
                "Apparel_SimpleHelmet",
                "Apparel_TribalHeaddress",
                "Apparel_Tuque",
                "Apparel_WarMask",
                "Apparel_WarVeil"
            };

        private static HediffDef coveredEarsHediffDef;

        public static void Synchronize(Pawn pawn)
        {
            if (pawn?.def?.defName != HelodDefName
                || pawn.health?.hediffSet == null)
            {
                return;
            }

            HediffDef hediffDef = CoveredEarsHediffDef;
            if (hediffDef == null)
            {
                return;
            }

            bool shouldHaveHediff = IsWearingEarCoveringApparel(pawn);
            Hediff currentHediff =
                pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);

            if (shouldHaveHediff && currentHediff == null)
            {
                pawn.health.AddHediff(hediffDef);
            }
            else if (!shouldHaveHediff && currentHediff != null)
            {
                pawn.health.RemoveHediff(currentHediff);
            }
        }

        private static bool IsWearingEarCoveringApparel(Pawn pawn)
        {
            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel == null)
            {
                return false;
            }

            for (int i = 0; i < wornApparel.Count; i++)
            {
                string defName = wornApparel[i]?.def?.defName;
                if (defName != null
                    && EarCoveringApparelDefNames.Contains(defName))
                {
                    return true;
                }
            }

            return false;
        }

        private static HediffDef CoveredEarsHediffDef
        {
            get
            {
                if (coveredEarsHediffDef == null)
                {
                    coveredEarsHediffDef =
                        DefDatabase<HediffDef>.GetNamedSilentFail(
                            CoveredEarsHediffDefName);
                }

                return coveredEarsHediffDef;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelAdded")]
    public static class Patch_PawnApparelTracker_HelodCoveredEarsAdded
    {
        public static void Postfix(Pawn ___pawn)
        {
            HelodCoveredEarsUtility.Synchronize(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelRemoved")]
    public static class Patch_PawnApparelTracker_HelodCoveredEarsRemoved
    {
        public static void Postfix(Pawn ___pawn)
        {
            HelodCoveredEarsUtility.Synchronize(___pawn);
        }
    }

    /// <summary>
    /// Repairs the state for pawns loaded from saves made before this feature
    /// existed and acts as a low-frequency safeguard for external apparel mods.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), "ApparelTrackerTickRare")]
    public static class Patch_PawnApparelTracker_HelodCoveredEarsTickRare
    {
        public static void Postfix(Pawn ___pawn)
        {
            HelodCoveredEarsUtility.Synchronize(___pawn);
        }
    }
}
