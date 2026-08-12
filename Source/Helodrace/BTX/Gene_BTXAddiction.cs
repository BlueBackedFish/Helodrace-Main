using RimWorld;
using Verse;

namespace Helodrace
{
    public class Gene_BTXAddiction : Gene
    {
        private const string AddictionHediffDefName = "HD_BTXAddiction";
        private const string DeficiencyHediffDefName = "HD_BTXDeficiency";
        private const string ChemicalNeedDefName = "Chemical_BTX";
        private const float DeficiencyNeedThreshold = 0.30f;
        private const float DeficiencyDaysToDeath = 10f;
        private const float DeficiencyRecoveryPerDay = 0.5f;

        public override void PostAdd()
        {
            base.PostAdd();
            EnsureAddiction();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            EnsureAddiction();
            UpdateDeficiency(delta);
        }

        public override void PostRemove()
        {
            RemoveAddiction();
            base.PostRemove();
        }

        private void EnsureAddiction()
        {
            if (!Active)
            {
                RemoveAddiction();
                return;
            }

            HediffDef addictionDef = DefDatabase<HediffDef>.GetNamedSilentFail(AddictionHediffDefName);
            if (addictionDef == null || pawn?.health?.hediffSet == null
                || pawn.health.hediffSet.HasHediff(addictionDef))
            {
                return;
            }

            pawn.health.AddHediff(addictionDef);
        }

        private void RemoveAddiction()
        {
            HediffDef addictionDef = DefDatabase<HediffDef>.GetNamedSilentFail(AddictionHediffDefName);
            Hediff addiction = addictionDef == null
                ? null
                : pawn?.health?.hediffSet?.GetFirstHediffOfDef(addictionDef);
            if (addiction != null)
            {
                pawn.health.RemoveHediff(addiction);
            }

            HediffDef deficiencyDef = DefDatabase<HediffDef>.GetNamedSilentFail(DeficiencyHediffDefName);
            Hediff deficiency = deficiencyDef == null
                ? null
                : pawn?.health?.hediffSet?.GetFirstHediffOfDef(deficiencyDef);
            if (deficiency != null)
            {
                pawn.health.RemoveHediff(deficiency);
            }
        }

        private void UpdateDeficiency(int delta)
        {
            if (!Active || delta <= 0 || pawn?.health?.hediffSet == null)
            {
                return;
            }

            NeedDef needDef = DefDatabase<NeedDef>.GetNamedSilentFail(ChemicalNeedDefName);
            HediffDef deficiencyDef = DefDatabase<HediffDef>.GetNamedSilentFail(DeficiencyHediffDefName);
            Need chemicalNeed = needDef == null ? null : pawn.needs?.TryGetNeed(needDef);
            if (chemicalNeed == null || deficiencyDef == null)
            {
                return;
            }

            Hediff deficiency = pawn.health.hediffSet.GetFirstHediffOfDef(deficiencyDef);
            float elapsedDays = delta / 60000f;
            if (chemicalNeed.CurLevel < DeficiencyNeedThreshold)
            {
                if (deficiency == null)
                {
                    deficiency = HediffMaker.MakeHediff(deficiencyDef, pawn);
                    deficiency.Severity = 0.001f;
                    pawn.health.AddHediff(deficiency);
                }

                // Continuous withdrawal below 30% reaches lethal severity after
                // ten in-game days. Severity is saved with the pawn.
                deficiency.Severity += elapsedDays / DeficiencyDaysToDeath;
                return;
            }

            if (deficiency != null)
            {
                deficiency.Severity -= elapsedDays * DeficiencyRecoveryPerDay;
                if (deficiency.Severity <= 0.001f)
                {
                    pawn.health.RemoveHediff(deficiency);
                }
            }
        }
    }
}
