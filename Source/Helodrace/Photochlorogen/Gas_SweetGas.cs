using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{

    public class Hediff_SweetGasExposure : HediffWithComps
    {
        private const int StaleExposureTicks = GenDate.TicksPerHour * 4;
        private const int CheckIntervalTicks = 600;
        private const float TreatmentSeverityReductionBase = 0.18f;
        private const float TreatmentSeverityReductionQualityFactor = 0.32f;

        private int lastDoseChangedTick = -1;

        public override bool ShouldRemove => base.ShouldRemove || this.Severity <= 0f || ExposureIsStale;

        public override float TendPriority => 1.8f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastDoseChangedTick, "lastDoseChangedTick", -1);
        }

        public override bool TendableNow(bool ignoreTimer = false)
        {
            return this.pawn != null && !this.pawn.Dead;
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (lastDoseChangedTick < 0)
            {
                lastDoseChangedTick = Find.TickManager.TicksGame;
            }

            if (this.pawn != null && this.pawn.IsHashIntervalTick(CheckIntervalTicks) && ExposureIsStale)
            {
                this.Severity = 0f;
            }
        }

        public void AddDose(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            float previousSeverity = this.Severity;
            this.Severity = Mathf.Min(this.Severity + amount, this.def.maxSeverity);
            if (!Mathf.Approximately(previousSeverity, this.Severity))
            {
                MarkDoseChanged();
            }
        }

        public void MarkDoseChanged()
        {
            lastDoseChangedTick = Find.TickManager.TicksGame;
        }

        public override void Tended(float quality, float maxQuality, int batchPosition = 0)
        {
            base.Tended(quality, maxQuality, batchPosition);

            float qualityFactor = maxQuality > 0f ? Mathf.Clamp01(quality / maxQuality) : Mathf.Clamp01(quality);
            float reduction = TreatmentSeverityReductionBase + qualityFactor * TreatmentSeverityReductionQualityFactor;
            this.Severity = Mathf.Max(0f, this.Severity - reduction);
            MarkDoseChanged();
        }

        private bool ExposureIsStale => lastDoseChangedTick >= 0 && Find.TickManager.TicksGame - lastDoseChangedTick >= StaleExposureTicks;
    }

    public class Hediff_SweetGasRisk : HediffWithComps
    {
        private const int ProgressIntervalTicks = 120;
        private const int ScarIntervalTicks = 1200;
        private const int DeathCheckIntervalTicks = 2500;
        private const int ScarsPerInterval = 3;
        private const float ExposureRiskPerDay = 15.00f;
        private const float CommittedRiskPerDay = 4.00f;
        private const float TreatmentProgressFactor = 0.20f;
        private const float ActivePhaseSeverity = 0.35f;
        private const float LateActivePhaseSeverity = 0.72f;
        private const float LateActiveDeathChance = 0.65f;

        private static HediffDef exposureHediff;
        private static HediffDef acidBurnHediff;

        public override bool ShouldRemove => base.ShouldRemove || this.Severity <= 0f;

        public override string LabelInBrackets => "HD_SweetGasRisk_Progress".Translate((this.Severity * 100f).ToString("F0")).Resolve();

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

            if (this.Severity >= ActivePhaseSeverity && this.pawn.IsHashIntervalTick(ScarIntervalTicks))
            {
                ScarExternalParts();
            }

            if (this.Severity >= LateActivePhaseSeverity && this.pawn.IsHashIntervalTick(DeathCheckIntervalTicks) && Rand.Chance(LateActiveDeathChance))
            {
                this.pawn.Kill(null, this);
            }
        }

        private void ProgressRisk(int ticks)
        {
            float exposure = CurrentExposureSeverity;
            float riskPerDay = exposure * ExposureRiskPerDay;
            if (this.Severity >= ActivePhaseSeverity)
            {
                riskPerDay += CommittedRiskPerDay;
            }

            if (TreatmentEffectActive && riskPerDay > 0f)
            {
                riskPerDay *= TreatmentProgressFactor;
            }

            this.Severity = Mathf.Clamp(this.Severity + riskPerDay * ticks / GenDate.TicksPerDay, 0f, this.def.maxSeverity);
        }

        private void ScarExternalParts()
        {
            HediffDef scarDef = AcidBurnHediff;
            if (scarDef == null)
            {
                return;
            }

            List<BodyPartRecord> parts = this.pawn.health.hediffSet.GetNotMissingParts()
                .Where(part => part.depth == BodyPartDepth.Outside
                    && part.coverageAbs > 0f
                    && !HasSweetGasScar(part))
                .ToList();
            if (parts.Count == 0)
            {
                return;
            }

            int scarsToAdd = Mathf.Min(ScarsPerInterval, parts.Count);
            for (int i = 0; i < scarsToAdd; i++)
            {
                BodyPartRecord partToScar = parts.RandomElementByWeight(part => part.coverageAbs);
                parts.Remove(partToScar);
                Hediff scar = HediffMaker.MakeHediff(scarDef, this.pawn, partToScar);
                scar.Severity = Rand.Range(1.20f, 1.70f);
                HediffComp_GetsPermanent permanentComp = scar.TryGetComp<HediffComp_GetsPermanent>();
                if (permanentComp != null)
                {
                    permanentComp.IsPermanent = true;
                }

                this.pawn.health.AddHediff(scar, partToScar);
            }
        }

        private bool HasSweetGasScar(BodyPartRecord part)
        {
            HediffDef scarDef = AcidBurnHediff;
            if (scarDef == null)
            {
                return false;
            }

            return this.pawn.health.hediffSet.hediffs.Any(hediff =>
                hediff.def == scarDef &&
                hediff.Part == part &&
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
                    exposureHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_SweetGasExposure");
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

    public class CompProperties_SweetGasCan : CompProperties
    {
        public float spawnChance = 1f;
        public float densityPerPulse = 0.75f;
        public float emissionRadius = 2.4f;
        public float edgeDensityFactor = 0.55f;
        public int burnDurationTicks = 1800;
        public int gasSimulationIntervalTicks = 30;
        public int spreadIntervalTicks = 600;
        public HelodGasDef gasDef;
        public bool destroyOnUse = true;

        public CompProperties_SweetGasCan()
        {
            compClass = typeof(CompSweetGasCan);
        }
    }

    public class CompSweetGasCan : ThingComp
    {
        private const string IgniteJobDefName = "HD_IgniteSweetGasCan";

        private bool used;
        private bool burning;
        private int burnEndTick;
        private int nextSpreadTick;

        public CompProperties_SweetGasCan Props => (CompProperties_SweetGasCan)props;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref used, "used", false);
            Scribe_Values.Look(ref burning, "burning", false);
            Scribe_Values.Look(ref burnEndTick, "burnEndTick", 0);
            Scribe_Values.Look(ref nextSpreadTick, "nextSpreadTick", 0);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!burning || parent.Map == null)
            {
                return;
            }

            if (parent.IsHashIntervalTick(60))
            {
                ThrowBurningEffect();
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame >= nextSpreadTick)
            {
                int simulationTicks = Mathf.Clamp(Props.gasSimulationIntervalTicks, 1, Props.spreadIntervalTicks);
                SpreadGas(simulationTicks);
                nextSpreadTick = ticksGame + simulationTicks;
            }

            if (ticksGame >= burnEndTick)
            {
                FinishBurning();
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn == null || selPawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            string label = "HD_SweetGasCan_Deploy_Label".Translate();
            if (used)
            {
                yield return new FloatMenuOption(label + ": " + (burning ? "HD_SweetGasCan_Deploy_Burning" : "HD_SweetGasCan_Deploy_Used").Translate(), null);
                yield break;
            }

            if (!selPawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                yield break;
            }

            if (!selPawn.CanReserve(parent))
            {
                yield return new FloatMenuOption(label + ": " + "Reserved".Translate(), null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(IgniteJobDefName);
                if (jobDef == null)
                {
                    Log.ErrorOnce("Helodrace: HD_IgniteSweetGasCan JobDef is missing.", 93214701);
                    return;
                }

                Job job = JobMaker.MakeJob(jobDef, parent);
                selPawn.jobs.TryTakeOrderedJob(job);
            });
        }

        public void Ignite(Pawn igniter)
        {
            if (used || parent.Map == null || Props.gasDef == null)
            {
                return;
            }

            used = true;
            burning = true;
            burnEndTick = Find.TickManager.TicksGame + Props.burnDurationTicks;
            nextSpreadTick = Find.TickManager.TicksGame;
            ThrowIgnitionEffect();
            Messages.Message("HD_SweetGasCan_Deploy_Message".Translate(igniter?.LabelShort ?? parent.LabelShort, parent.LabelShort, Props.emissionRadius.ToString("F0")), parent, MessageTypeDefOf.NegativeEvent);
        }

        private void SpreadGas(int simulationTicks)
        {
            if (parent.Map == null || Props.gasDef == null)
            {
                return;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.emissionRadius, true))
            {
                if (!CanGasOccupy(cell))
                {
                    continue;
                }

                float distanceFactor = Mathf.InverseLerp(Props.emissionRadius, 0f, parent.Position.DistanceTo(cell));
                float densityFactor = Mathf.Lerp(Props.edgeDensityFactor, 1f, distanceFactor);
                float simulationFactor = Props.spreadIntervalTicks > 0 ? simulationTicks / (float)Props.spreadIntervalTicks : 1f;
                HelodGasStore.AddGas(cell, parent.Map, Props.gasDef,
                    Props.densityPerPulse * densityFactor * Props.spawnChance * simulationFactor);
            }
        }

        private bool CanGasOccupy(IntVec3 cell)
        {
            if (parent.Map == null || !cell.InBounds(parent.Map))
            {
                return false;
            }

            return HelodGasStore.GasCanMoveTo(cell, parent.Map);
        }

        private void FinishBurning()
        {
            burning = false;
            if (Props.destroyOnUse && !parent.Destroyed)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        private void ThrowIgnitionEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 2.0f);
            FleckMaker.ThrowMicroSparks(loc, parent.Map);
            FleckMaker.ThrowSmoke(loc, parent.Map, 1.4f);
        }

        private void ThrowBurningEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 1.25f);
            FleckMaker.ThrowSmoke(loc, parent.Map, 0.7f);
        }
    }

    public class JobDriver_IgniteSweetGasCan : JobDriver
    {
        private const TargetIndex CanInd = TargetIndex.A;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(CanInd);
            this.FailOnBurningImmobile(CanInd);
            this.FailOn(() => TargetThingA.TryGetComp<CompSweetGasCan>() == null);

            yield return Toils_Goto.GotoThing(CanInd, PathEndMode.Touch);

            Toil ignite = Toils_General.Wait(120);
            ignite.WithProgressBarToilDelay(CanInd);
            ignite.FailOnCannotTouch(CanInd, PathEndMode.Touch);
            yield return ignite;

            yield return new Toil
            {
                initAction = delegate
                {
                    Thing thing = job.GetTarget(CanInd).Thing;
                    thing?.TryGetComp<CompSweetGasCan>()?.Ignite(pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(CanInd), job, 1, -1, null, errorOnFailed);
        }
    }
}
