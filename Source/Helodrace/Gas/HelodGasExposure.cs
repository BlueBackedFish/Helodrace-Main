using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public static class Gas_Photochlorogen
    {
        private static HediffDef exposureHediff;
        private static HediffDef riskHediff;

        internal static void ApplyExposureTo(Pawn pawn, float density, float severityAtFullDensity)
        {
            HediffDef exposureDef = ExposureHediff;
            if (exposureDef == null || density <= 0f || !CanAffect(pawn))
                return;

            float resistance = pawn.GetStatValue(StatDefOf.ToxicEnvironmentResistance);
            if (resistance >= 1f)
                return;

            float gain = severityAtFullDensity * Mathf.Clamp01(density) * Mathf.Clamp01(1f - resistance);
            Hediff exposure = pawn.health.hediffSet.GetFirstHediffOfDef(exposureDef);
            if (exposure != null)
                exposure.Severity = Mathf.Min(exposure.Severity + gain, exposureDef.maxSeverity);
            else
            {
                exposure = HediffMaker.MakeHediff(exposureDef, pawn);
                exposure.Severity = gain;
                pawn.health.AddHediff(exposure);
            }

            EnsureRiskHediff(pawn);
        }

        private static bool CanAffect(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.health != null && pawn.RaceProps?.IsFlesh == true
                && (!pawn.RaceProps.Humanlike || GasUtility.IsAffectedByExposure(pawn));
        }

        private static void EnsureRiskHediff(Pawn pawn)
        {
            HediffDef riskDef = RiskHediff;
            if (riskDef == null || pawn.health.hediffSet.HasHediff(riskDef))
                return;

            Hediff risk = HediffMaker.MakeHediff(riskDef, pawn);
            risk.Severity = 0.04f;
            pawn.health.AddHediff(risk);
        }

        private static HediffDef ExposureHediff => exposureHediff ??
            (exposureHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_PhotochlorogenExposure"));

        public static HediffDef RiskHediff => riskHediff ??
            (riskHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_PhotochlorogenRisk"));
    }

    public static class Gas_SweetGas
    {
        private static HediffDef exposureHediff;
        private static HediffDef riskHediff;
        private static StatDef vacuumResistanceStat;
        private static bool vacuumResistanceResolved;

        internal static void ApplyExposureTo(Pawn pawn, float density, float severityAtFullDensity)
        {
            HediffDef exposureDef = ExposureHediff;
            if (exposureDef == null || density <= 0f || !CanAffect(pawn))
                return;

            float resistance = VacuumResistanceStat != null ? pawn.GetStatValue(VacuumResistanceStat) : 0f;
            if (resistance >= 1f)
                return;

            float gain = severityAtFullDensity * Mathf.Clamp01(density) * Mathf.Clamp01(1f - resistance);
            Hediff exposure = pawn.health.hediffSet.GetFirstHediffOfDef(exposureDef);
            if (exposure is Hediff_SweetGasExposure sweetExposure)
                sweetExposure.AddDose(gain);
            else if (exposure != null)
                exposure.Severity = Mathf.Min(exposure.Severity + gain, exposureDef.maxSeverity);
            else
            {
                exposure = HediffMaker.MakeHediff(exposureDef, pawn);
                exposure.Severity = Mathf.Min(gain, exposureDef.maxSeverity);
                pawn.health.AddHediff(exposure);
                if (exposure is Hediff_SweetGasExposure newSweetExposure)
                    newSweetExposure.MarkDoseChanged();
            }

            EnsureRiskHediff(pawn);
        }

        private static bool CanAffect(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.health != null && pawn.RaceProps?.IsFlesh == true
                && !GasMaskPouchUtility.HasActiveMask(pawn, requireSweetGasProtection: true);
        }

        private static void EnsureRiskHediff(Pawn pawn)
        {
            HediffDef riskDef = RiskHediff;
            if (riskDef == null || pawn.health.hediffSet.HasHediff(riskDef))
                return;

            Hediff risk = HediffMaker.MakeHediff(riskDef, pawn);
            risk.Severity = 0.04f;
            pawn.health.AddHediff(risk);
        }

        private static HediffDef ExposureHediff => exposureHediff ??
            (exposureHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_SweetGasExposure"));

        public static HediffDef RiskHediff => riskHediff ??
            (riskHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_SweetGasRisk"));

        private static StatDef VacuumResistanceStat
        {
            get
            {
                if (!vacuumResistanceResolved)
                {
                    vacuumResistanceResolved = true;
                    vacuumResistanceStat = DefDatabase<StatDef>.GetNamedSilentFail("VacuumResistance");
                }
                return vacuumResistanceStat;
            }
        }
    }
}

namespace Helodrace.ModernWar
{
    public static class Gas_CS
    {
        private static HediffDef exposureHediff;

        internal static void ApplyExposureTo(Pawn pawn, float density, float severityAtFullDensity)
        {
            if (pawn?.health == null || pawn.Dead || pawn.RaceProps?.IsFlesh != true
                || (pawn.RaceProps.Humanlike && !GasUtility.IsAffectedByExposure(pawn)))
                return;

            float resistance = pawn.GetStatValue(StatDefOf.ToxicEnvironmentResistance);
            if (resistance >= 1f)
                return;

            exposureHediff = exposureHediff ?? DefDatabase<HediffDef>.GetNamedSilentFail("HD_CSGasExposure");
            if (exposureHediff == null)
                return;

            float gain = severityAtFullDensity * Mathf.Clamp01(density) * Mathf.Clamp01(1f - resistance);
            Hediff exposure = pawn.health.hediffSet.GetFirstHediffOfDef(exposureHediff);
            if (exposure is Hediff_CSGasExposure csExposure)
                csExposure.AddDose(gain);
            else if (exposure != null)
                exposure.Severity = Mathf.Min(exposureHediff.maxSeverity, exposure.Severity + gain);
            else
            {
                Hediff newExposure = HediffMaker.MakeHediff(exposureHediff, pawn);
                newExposure.Severity = 0f;
                if (newExposure is Hediff_CSGasExposure newCSExposure)
                    newCSExposure.AddDose(gain);
                else
                    newExposure.Severity = gain;
                pawn.health.AddHediff(newExposure);
            }
        }
    }

    public static class Gas_CN
    {
        private static HediffDef exposureHediff;

        internal static void ApplyExposureTo(Pawn pawn, float density, float severityAtFullDensity)
        {
            if (pawn?.health == null || pawn.Dead || pawn.RaceProps?.IsFlesh != true
                || (pawn.RaceProps.Humanlike && !GasUtility.IsAffectedByExposure(pawn)))
                return;

            float resistance = pawn.GetStatValue(StatDefOf.ToxicEnvironmentResistance);
            if (resistance >= 1f)
                return;

            exposureHediff = exposureHediff ?? DefDatabase<HediffDef>.GetNamedSilentFail("HD_CNGasExposure");
            if (exposureHediff == null)
                return;

            float gain = severityAtFullDensity * Mathf.Clamp01(density) * Mathf.Clamp01(1f - resistance);
            Hediff exposure = pawn.health.hediffSet.GetFirstHediffOfDef(exposureHediff);
            if (exposure is Hediff_CNGasExposure cnExposure)
                cnExposure.AddDose(gain);
            else if (exposure != null)
                exposure.Severity = Mathf.Min(exposureHediff.maxSeverity, exposure.Severity + gain);
            else
            {
                Hediff newExposure = HediffMaker.MakeHediff(exposureHediff, pawn);
                newExposure.Severity = 0f;
                if (newExposure is Hediff_CNGasExposure newCNExposure)
                    newCNExposure.AddDose(gain);
                else
                    newExposure.Severity = gain;
                pawn.health.AddHediff(newExposure);
            }
        }
    }
}
