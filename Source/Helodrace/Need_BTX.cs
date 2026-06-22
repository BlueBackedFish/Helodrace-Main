using RimWorld;
using Verse;

namespace Helodrace
{
    public class Need_BTX : Need
    {
        private const int NeedIntervalTicks = 150;
        private const float DeficiencyRisePerDay = 0.20f;
        private const float DeficiencyFallPerDay = 0.50f;

        public Need_BTX(Pawn pawn) : base(pawn)
        {
            threshPercents = new System.Collections.Generic.List<float> { 0.20f, 0.50f };
        }

        public override bool ShowOnNeedList => BTXUtility.IsHelod(pawn);

        public override void SetInitialLevel()
        {
            CurLevel = MaxLevel;
        }

        public override void NeedInterval()
        {
            if (!BTXUtility.IsHelod(pawn))
            {
                CurLevel = MaxLevel;
                return;
            }

            BTXUtility.RemoveLegacyBTXGeneticDependency(pawn);
            CurLevel -= def.fallPerDay * NeedIntervalTicks / GenDate.TicksPerDay;
            UpdateDeficiency();
        }

        private void UpdateDeficiency()
        {
            HediffDef deficiencyDef = DefDatabase<HediffDef>.GetNamedSilentFail(BTXUtility.DeficiencyHediffDefName);
            if (deficiencyDef == null || pawn?.health?.hediffSet == null)
            {
                return;
            }

            Hediff deficiency = pawn.health.hediffSet.GetFirstHediffOfDef(deficiencyDef);
            float deltaPerInterval = NeedIntervalTicks / (float)GenDate.TicksPerDay;

            if (CurLevel <= 0.001f)
            {
                if (deficiency == null)
                {
                    deficiency = HediffMaker.MakeHediff(deficiencyDef, pawn);
                    deficiency.Severity = 0f;
                    pawn.health.AddHediff(deficiency);
                }

                deficiency.Severity += DeficiencyRisePerDay * deltaPerInterval;
            }
            else if (deficiency != null)
            {
                deficiency.Severity -= DeficiencyFallPerDay * deltaPerInterval;
                if (deficiency.Severity <= 0.001f)
                {
                    pawn.health.RemoveHediff(deficiency);
                }
            }
        }
    }
}
