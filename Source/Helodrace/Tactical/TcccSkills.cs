using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public enum TcccTreatment { Hemostasis, AttachDrag, Analgesic, Hemostatic, Mist }

    // Pure rules are kept separate from jobs so boundary values can be regression-tested.
    public static class TcccRules
    {
        public const int HemostasisMaxParts = 6;
        public const int HemostaticTicks = 60;
        public const int DragAttachTicks = 60;
        public const int DrugEffectTicks = 15000;
        public const float HemostaticBleedingFactor = .30f;
        public static int HemostasisDuration(int parts) =>
            300 + Mathf.Clamp(parts - 3, 0, 3) * 300;
        public static int PressureStartsAt(int duration) => duration * 2 / 3;
    }

    public class Hediff_TcccTimed : Hediff
    {
        public int expiresTick;
        public string report;
        public override bool ShouldRemove => Find.TickManager.TicksGame >= expiresTick;
        public override string TipStringExtra => (report.NullOrEmpty() ? "" : report + "\n")
            + "HD_TCCC_Remaining".Translate(Math.Max(0, expiresTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref expiresTick, "expiresTick");
            Scribe_Values.Look(ref report, "report");
        }
    }

    public static class TcccUtility
    {
        public static bool HasTraining(Pawn pawn) => TacticalTraining.HasTcccLow(pawn);
        public static bool HasTraining(Pawn pawn, TcccTreatment treatment) =>
            treatment == TcccTreatment.Hemostasis || treatment == TcccTreatment.AttachDrag
                ? TacticalTraining.HasTcccLow(pawn) : TacticalTraining.HasTcccHigh(pawn);
        public static bool CanAct(Pawn pawn) => pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
            && ((pawn.Faction == Faction.OfPlayer && pawn.Drafted)
                || (OrganizationAPI.GetOrganization(pawn)?.faction?.def?.defName
                    == "HD_HelodCivilHighFaction") || OrganizationMedical.HasDuty(pawn))
            && pawn.RaceProps.Humanlike
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && !pawn.InMentalState
            && HasTraining(pawn);
        public static bool CanTreat(Pawn actor, Pawn patient) => CanAct(actor) && patient != null && patient.Spawned
            && !patient.Dead && patient.Map == actor.Map && patient.RaceProps.Humanlike
            && (!patient.HostileTo(actor) || TemporaryRestraint.CanAid(patient));
        public static bool CanTreat(Pawn actor, Pawn patient, TcccTreatment treatment) =>
            CanTreat(actor, patient) && HasTraining(actor, treatment)
                && MedicalPouchUtility.ForTreatment(actor, treatment) != null;
        public static Hediff_TcccTimed Effect(Pawn pawn, string name) => pawn?.health?.hediffSet?.hediffs
            .OfType<Hediff_TcccTimed>().FirstOrDefault(h => h.def.defName == name && !h.ShouldRemove);
        public static bool MistReady(Pawn pawn) => Effect(pawn, "HD_TCCC_Mist") != null;
        public static Hediff_TcccTimed ApplyTimed(Pawn pawn, string name, int duration)
        {
            var effect = pawn.health.hediffSet.hediffs.OfType<Hediff_TcccTimed>().FirstOrDefault(h => h.def.defName == name);
            if (effect == null)
            {
                effect = (Hediff_TcccTimed)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(name), pawn);
                effect.expiresTick = Find.TickManager.TicksGame + duration;
                pawn.health.AddHediff(effect);
            }
            else effect.expiresTick = Find.TickManager.TicksGame + duration;
            if (effect is Hediff_SystemicHemostatic) PartHemostasis.Invalidate(pawn.health.hediffSet);
            pawn.health.hediffSet.DirtyCache();
            return effect;
        }
        public static void RemoveEffect(Pawn pawn, string name)
        {
            foreach (var h in pawn.health.hediffSet.hediffs.Where(h => h.def.defName == name).ToList()) pawn.health.RemoveHediff(h);
        }
        public static void Reject(string key) => Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput);

        public static void Start(Pawn actor, Pawn patient, TcccTreatment treatment)
        {
            if (!CanTreat(actor, patient, treatment)) return;
            if ((treatment == TcccTreatment.Hemostasis || treatment == TcccTreatment.Hemostatic)
                && patient.health.hediffSet.BleedRateTotal <= 0f) { Reject("HD_TCCC_NoBleeding"); return; }
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_TCCC_Treat"), patient);
            job.count = (int)treatment;
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static void Target(Pawn actor, TcccTreatment treatment)
        {
            if (!CanAct(actor) || !HasTraining(actor, treatment)) return;
            if (MedicalPouchUtility.ForTreatment(actor, treatment) == null)
            { Reject(MissingPouchReason(treatment)); return; }
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetPawns = true, canTargetBuildings = false, canTargetItems = false,
                validator = t => CanTreat(actor, t.Thing as Pawn, treatment)
                    && (treatment != TcccTreatment.AttachDrag || t.Thing != actor && ((Pawn)t.Thing).Downed)
            }, target =>
            {
                Pawn patient = target.Pawn;
                if (treatment == TcccTreatment.Analgesic)
                {
                    if (patient.health.hediffSet.PainTotal <= 0f)
                    { Reject("HD_TCCC_NoPain"); return; }
                    if (TcccDrugs.SelectedDef(patient) == null) { Reject("HD_TCCC_NoAnalgesic"); return; }
                }
                Start(actor, patient, treatment);
            }, caster: actor);
        }

        private static string MissingPouchReason(TcccTreatment treatment) =>
            treatment == TcccTreatment.Hemostatic ? "HD_TCCC_NeedHemostatic"
                : treatment == TcccTreatment.Analgesic ? "HD_TCCC_NeedLargePouch" : "HD_TCCC_NeedPouch";

        public static IEnumerable<Gizmo> Gizmos(Pawn pawn)
        {
            foreach (TcccTreatment treatment in Enum.GetValues(typeof(TcccTreatment)))
            {
                TcccTreatment local = treatment;
                if (!HasTraining(pawn, local)) continue;
                var command = new Command_Action
                {
                    defaultLabel = ("HD_TCCC_" + local).Translate(),
                    defaultDesc = "HD_TCCC_MenuDesc".Translate(),
                    icon = IconFor(local),
                    action = () => Target(pawn, local)
                };
                if (MedicalPouchUtility.ForTreatment(pawn, local) == null)
                    command.Disable(MissingPouchReason(local).Translate());
                yield return command;
            }
            if (pawn.Map.GetComponent<MapComponent_TcccDragging>().IsDragging(pawn))
                yield return new Command_Action
                {
                    defaultLabel = "HD_TCCC_Detach".Translate(),
                    defaultDesc = "HD_TCCC_MenuDesc".Translate(),
                    icon = IconFor(TcccTreatment.AttachDrag),
                    action = () => pawn.Map.GetComponent<MapComponent_TcccDragging>().Detach(pawn)
                };
        }

        private static Texture2D IconFor(TcccTreatment treatment)
        {
            string skillIcon;
            switch (treatment)
            {
                case TcccTreatment.Hemostasis: skillIcon = "Skill/HD_TCCC_SelfHemostasis"; break;
                case TcccTreatment.AttachDrag: skillIcon = "Skill/HD_TCCC_Dragging"; break;
                case TcccTreatment.Hemostatic: skillIcon = "Skill/HD_TCCC_Hemostatic"; break;
                case TcccTreatment.Mist: skillIcon = "Skill/HD_TCCC_MIST"; break;
                default: skillIcon = "Skill/HD_TCCC_Analgesic"; break;
            }
            return ContentFinder<Texture2D>.Get(skillIcon, false)
                ?? ContentFinder<Texture2D>.Get("UI/Commands/Rescue", false)
                ?? BaseContent.BadTex;
        }

        public static string MistReport(Pawn patient)
        {
            var injuries = patient.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList();
            return "HD_TCCC_MistReport".Translate(
                patient.LabelShortCap,
                string.Join(", ", injuries.Select(h => h.def.label).Distinct().ToArray()),
                string.Join(", ", injuries.Select(h => h.LabelCap + " (" + h.Part?.Label + ")").ToArray()),
                patient.health.hediffSet.BleedRateTotal.ToString("P0"), patient.health.hediffSet.PainTotal.ToString("P0"),
                patient.Downed ? "HD_TCCC_Downed".Translate().ToString() : "HD_TCCC_Mobile".Translate().ToString(),
                string.Join(", ", patient.health.hediffSet.hediffs.Where(h => !(h is Hediff_Injury)
                    && h.def.defName != "HD_TCCC_Mist").Select(h => h.LabelCap.ToString()).ToArray()));
        }
    }

    public sealed class JobDriver_TcccTreatment : JobDriver
    {
        private int treatmentTicks;
        private bool pressureApplied;
        private List<int> treatedPartIndices = new List<int>();
        private TcccTreatment Treatment => (TcccTreatment)job.count;
        private Pawn Patient => job.targetA.Pawn;
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref treatmentTicks, "treatmentTicks");
            Scribe_Values.Look(ref pressureApplied, "pressureApplied");
            Scribe_Collections.Look(ref treatedPartIndices, "treatedPartIndices", LookMode.Value);
        }

        private int ApplyToSelectedParts(string effect, float factor, int duration)
        {
            int applied = 0;
            List<BodyPartRecord> body = Patient.RaceProps.body.AllParts;
            foreach (int index in treatedPartIndices)
                if (index >= 0 && index < body.Count
                    && PartHemostasis.Apply(Patient, body[index], effect, factor, duration) != null) applied++;
            return applied;
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Patient == pawn || pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !TcccUtility.CanTreat(pawn, Patient, Treatment));
            Job owned = job;
            AddFinishAction(condition => Tactics.TacticalCareBridge.Finished(pawn, owned, condition));
            AddFinishAction(condition =>
            {
                if (pressureApplied && Patient?.health?.hediffSet != null)
                    TcccUtility.RemoveEffect(Patient, "HD_TCCC_Pressure");
            });
            if (Patient != pawn)
            {
                yield return Tactics.TacticalCareBridge.Movement(pawn, job, () => ReadyForNextToil());
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            }
            int duration = Treatment == TcccTreatment.Hemostatic ? TcccRules.HemostaticTicks
                : Treatment == TcccTreatment.Mist ? 300 : Treatment == TcccTreatment.AttachDrag
                    ? TcccRules.DragAttachTicks : 120;
            Toil treatment = Treatment == TcccTreatment.Hemostasis
                ? new Toil { defaultCompleteMode = ToilCompleteMode.Never }
                : Toils_General.Wait(duration, TargetIndex.A);
            if (Treatment == TcccTreatment.Hemostasis)
            {
                treatment.AddPreInitAction(() =>
                {
                    if (treatedPartIndices != null && treatedPartIndices.Count > 0) return;
                    // Latch actual bleeding parts when care starts, not when it finishes:
                    // a later wound on another part is not retroactively treated.
                    bool tactical = Tactics.TacticalCareBridge.Owned(pawn, job);
                    treatedPartIndices = PartHemostasis.BleedingParts(Patient, TcccRules.HemostasisMaxParts,
                        tactical ? (Func<BodyPartRecord, bool>)(part => !PartHemostasis.HasDressing(Patient.health.hediffSet, part, Find.TickManager.TicksGame)) : null)
                        .Select(part => Patient.RaceProps.body.AllParts.IndexOf(part)).Where(index => index >= 0).ToList();
                    if (treatedPartIndices.Count == 0) EndJobWith(JobCondition.Incompletable);
                });
            }
            if (Treatment == TcccTreatment.Hemostasis)
                treatment.WithProgressBar(TargetIndex.A, () =>
                    Mathf.Clamp01(treatmentTicks / (float)TcccRules.HemostasisDuration(treatedPartIndices?.Count ?? 0)));
            else treatment.WithProgressBarToilDelay(TargetIndex.A);
            treatment.FailOn(() => Patient != pawn && !pawn.Position.AdjacentTo8WayOrInside(Patient.Position));
            treatment.tickAction = () =>
            {
                treatmentTicks++;
                if (Treatment != TcccTreatment.Hemostasis) return;
                int hemostasisDuration = TcccRules.HemostasisDuration(treatedPartIndices?.Count ?? 0);
                if (treatmentTicks >= TcccRules.PressureStartsAt(hemostasisDuration) && !pressureApplied)
                {
                    pressureApplied = true;
                    ApplyToSelectedParts("HD_TCCC_Pressure", .30f, hemostasisDuration);
                }
                if (treatmentTicks >= hemostasisDuration) ReadyForNextToil();
            };
            yield return treatment;
            yield return Toils_General.Do(CompleteTreatment);
        }
        private void CompleteTreatment()
        {
            switch (Treatment)
            {
                case TcccTreatment.Hemostasis:
                    if (ApplyToSelectedParts(PartHemostasis.DressingDefName, PartHemostasis.DressingFactor,
                        PartHemostasis.DressingTicks) > 0)
                        MedicalPouchUtility.ForTreatment(pawn, Treatment)?.TrySpend(Treatment);
                    break;
                case TcccTreatment.AttachDrag:
                    if (!pawn.Map.GetComponent<MapComponent_TcccDragging>().Attach(pawn, Patient))
                        TcccUtility.Reject("HD_TCCC_CannotDrag");
                    else MedicalPouchUtility.ForTreatment(pawn, Treatment)?.TrySpend(Treatment);
                    break;
                case TcccTreatment.Hemostatic:
                    if (Patient.health.hediffSet.BleedRateTotal <= 0f) { TcccUtility.Reject("HD_TCCC_NoBleeding"); return; }
                    TcccUtility.ApplyTimed(Patient, "HD_TCCC_Hemostatic", TcccRules.DrugEffectTicks);
                    MedicalPouchUtility.ForTreatment(pawn, Treatment)?.TrySpend(Treatment);
                    break;
                case TcccTreatment.Analgesic:
                    TcccDrugs.Administer(pawn, Patient);
                    break;
                case TcccTreatment.Mist:
                    TcccUtility.ApplyTimed(Patient, "HD_TCCC_Mist", 6 * GenDate.TicksPerHour).report = TcccUtility.MistReport(Patient);
                    MedicalPouchUtility.ForTreatment(pawn, Treatment)?.TrySpend(Treatment);
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_TcccGizmos
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        { if (__instance.Faction == Faction.OfPlayer && TcccUtility.CanAct(__instance))
              __result = __result.Concat(TcccUtility.Gizmos(__instance)); }
    }

}
