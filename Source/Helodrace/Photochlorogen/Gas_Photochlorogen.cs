using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Helodrace
{

    public class Hediff_PhotochlorogenRisk : HediffWithComps
    {
        private const int ProgressIntervalTicks = 120;
        private const int LungDamageIntervalTicks = 1800;
        private const float ReactionRecoveryPerDay = 0.45f;
        private const float CommittedMinimumRiskPerDay = 0.30f;
        private const float CommittedRiskPerDay = 3.00f;
        private const float ExposureRiskPerDay = 12.00f;
        private const float TreatmentProgressFactor = 0.20f;
        private const float LatentPhaseSeverity = 0.25f;
        private const float PulmonaryEdemaSeverity = 0.65f;
        private const float LungScarChance = 0.08f;
        private const int MaxPhotochlorogenLungScars = 2;

        private static HediffDef exposureHediff;
        private static HediffDef acidBurnHediff;

        public override bool ShouldRemove => base.ShouldRemove || this.Severity <= 0f;

        public override float TendPriority => 2.5f;

        public override bool TendableNow(bool ignoreTimer = false)
        {
            return this.pawn != null && !this.pawn.Dead && base.TendableNow(ignoreTimer);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);

            if (this.pawn == null || this.pawn.Dead)
            {
                return;
            }

            if (this.pawn.IsHashIntervalTick(ProgressIntervalTicks))
            {
                ProgressRisk(ProgressIntervalTicks);
            }

            if (this.Severity >= PulmonaryEdemaSeverity && this.pawn.IsHashIntervalTick(LungDamageIntervalTicks))
            {
                DamageLungs();
            }
        }

        private void ProgressRisk(int ticks)
        {
            float exposure = CurrentExposureSeverity;
            float riskPerDay = exposure * ExposureRiskPerDay;
            if (this.Severity >= LatentPhaseSeverity)
            {
                riskPerDay += CommittedMinimumRiskPerDay + CommittedRiskPerDay;
            }
            else if (exposure <= 0f)
            {
                riskPerDay -= ReactionRecoveryPerDay;
            }

            if (TreatmentEffectActive && riskPerDay > 0f)
            {
                riskPerDay *= TreatmentProgressFactor;
            }

            this.Severity = Mathf.Clamp(this.Severity + riskPerDay * ticks / GenDate.TicksPerDay, 0f, this.def.maxSeverity);
        }

        private void DamageLungs()
        {
            List<BodyPartRecord> lungs = this.pawn.health.hediffSet.GetNotMissingParts()
                .Where(part => part.def == BodyPartDefOf.Lung)
                .ToList();

            if (lungs.Count == 0)
            {
                return;
            }

            BodyPartRecord lung = lungs.RandomElement();
            float damageAmount = Mathf.Lerp(1.5f, 3.5f, Mathf.InverseLerp(PulmonaryEdemaSeverity, this.def.maxSeverity, this.Severity));
            DamageInfo damageInfo = new DamageInfo(DamageDefOf.AcidBurn, damageAmount, 999f, -1f, null, lung);
            this.pawn.TakeDamage(damageInfo);

            if (Rand.Chance(LungScarChance))
            {
                TryAddLungScar(lung);
            }
        }

        private void TryAddLungScar(BodyPartRecord lung)
        {
            HediffDef scarDef = AcidBurnHediff;
            if (scarDef == null || CountPhotochlorogenLungScars() >= MaxPhotochlorogenLungScars)
            {
                return;
            }

            Hediff scar = HediffMaker.MakeHediff(scarDef, this.pawn, lung);
            scar.Severity = Rand.Range(0.6f, 1.2f);
            HediffComp_GetsPermanent permanentComp = scar.TryGetComp<HediffComp_GetsPermanent>();
            if (permanentComp != null)
            {
                permanentComp.IsPermanent = true;
            }

            this.pawn.health.AddHediff(scar, lung);
        }

        private int CountPhotochlorogenLungScars()
        {
            HediffDef scarDef = AcidBurnHediff;
            if (scarDef == null)
            {
                return 0;
            }

            return this.pawn.health.hediffSet.hediffs.Count(hediff =>
                hediff.def == scarDef &&
                hediff.Part != null &&
                hediff.Part.def == BodyPartDefOf.Lung &&
                hediff.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent == true);
        }

        private float CurrentExposureSeverity
        {
            get
            {
                HediffDef exposureDef = ExposureHediff;
                if (exposureDef == null)
                {
                    return 0f;
                }

                Hediff exposure = this.pawn.health.hediffSet.GetFirstHediffOfDef(exposureDef);
                return exposure?.Severity ?? 0f;
            }
        }

        private bool TreatmentEffectActive
        {
            get
            {
                HediffComp_TendDuration tendComp = this.TryGetComp<HediffComp_TendDuration>();
                return tendComp != null && tendComp.IsTended;
            }
        }

        private static HediffDef ExposureHediff
        {
            get
            {
                if (exposureHediff == null)
                {
                    exposureHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_PhotochlorogenExposure");
                }

                return exposureHediff;
            }
        }

        private static HediffDef AcidBurnHediff
        {
            get
            {
                if (acidBurnHediff == null)
                {
                    acidBurnHediff = DefDatabase<HediffDef>.GetNamedSilentFail("AcidBurn");
                }

                return acidBurnHediff;
            }
        }
    }

    public class Hediff_PhotochlorogenExposure : HediffWithComps
    {
        private const float TreatmentSeverityReductionBase = 0.18f;
        private const float TreatmentSeverityReductionQualityFactor = 0.32f;

        public override bool ShouldRemove => base.ShouldRemove || this.Severity <= 0f;

        public override float TendPriority => 1.8f;

        public override bool TendableNow(bool ignoreTimer = false)
        {
            return this.pawn != null && !this.pawn.Dead;
        }

        public override void Tended(float quality, float maxQuality, int batchPosition = 0)
        {
            base.Tended(quality, maxQuality, batchPosition);

            float qualityFactor = maxQuality > 0f ? Mathf.Clamp01(quality / maxQuality) : Mathf.Clamp01(quality);
            float reduction = TreatmentSeverityReductionBase + qualityFactor * TreatmentSeverityReductionQualityFactor;
            this.Severity = Mathf.Max(0f, this.Severity - reduction);
        }
    }
}
