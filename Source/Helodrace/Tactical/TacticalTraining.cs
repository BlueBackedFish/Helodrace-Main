using RimWorld;
using Verse;

namespace Helodrace.Tactical
{
    public static class TacticalTraining
    {
        private static bool Has(Pawn pawn, string defName)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            return pawn?.health?.hediffSet != null && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }

        public static bool HasCqbLow(Pawn pawn) => Has(pawn, "HD_CQBTraining_LOW") || HasCqbHigh(pawn);
        public static bool HasCqbHigh(Pawn pawn) => Has(pawn, "HD_CQBTraining_HIGH");
        public static bool HasTcccLow(Pawn pawn) => Has(pawn, "HD_TCCCTraining_LOW") || HasTcccHigh(pawn);
        public static bool HasTcccHigh(Pawn pawn) => Has(pawn, "HD_TCCCTraining_HIGH");
    }
}
