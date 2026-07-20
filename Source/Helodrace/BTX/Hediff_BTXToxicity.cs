using RimWorld;
using Verse;

namespace Helodrace
{
    public class Hediff_BTXToxicity : HediffWithComps
    {
        private const float RisePerDay = 0.35f;
        private const float RecoveryPerDay = 0.60f;
        private const float SurvivalCheckSeverity = 0.92f;
        private const float SurvivalChance = 0.12f;
        private const float SurvivedSeverity = 0.65f;

        private bool survivedCriticalPhase;

        public override void PostTickInterval(int delta)
        {
            base.PostTickInterval(delta);

            if (pawn?.Dead != false)
            {
                return;
            }

            float days = delta / (float)GenDate.TicksPerDay;
            if (survivedCriticalPhase)
            {
                Severity -= RecoveryPerDay * days;
                if (Severity <= 0.001f)
                {
                    pawn.health.RemoveHediff(this);
                }
                return;
            }

            Severity += RisePerDay * days;
            if (Severity >= SurvivalCheckSeverity)
            {
                if (Rand.Chance(SurvivalChance))
                {
                    survivedCriticalPhase = true;
                    Severity = SurvivedSeverity;
                }
                else
                {
                    pawn.Kill(null, this);
                }
            }
        }

        public void AddBTXDose(float severityOffset)
        {
            survivedCriticalPhase = false;
            Severity += severityOffset;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref survivedCriticalPhase, "survivedCriticalPhase", false);
        }
    }
}
