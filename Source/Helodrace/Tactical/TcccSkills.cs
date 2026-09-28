using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public enum TcccTreatment { SelfHemostasis, AttachDrag, Analgesic, Hemostatic, Mist }

    // Pure rules are kept separate from jobs so boundary values can be regression-tested.
    public static class TcccRules
    {
        public const int PartialHemostasisTicks = 1200;
        public const int SelfHemostasisTicks = 1800;
        public const int HemostaticTicks = 180;
        public const int SelfEffectTicks = 45000;
        public const int DrugEffectTicks = 15000;
        public const float TargetPain = .10f;
        public static float DoseFor(float pain, float fullDosePainFactor)
        { return Mathf.Clamp01((pain - TargetPain) / Mathf.Max(.01f, 1f - fullDosePainFactor)); }
        public static float AddictionChance(float fullDoseChance, float fraction, bool skilled)
        { return Mathf.Clamp01(fullDoseChance * Mathf.Clamp01(fraction) * (skilled ? .5f : 1f)); }
        public static float BleedingFactor(bool pressure, bool completed, bool hemostatic)
        { return completed ? .05f : pressure || hemostatic ? .30f : 1f; }
    }

    public sealed class Hediff_TcccTimed : Hediff
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
        public static bool CanAct(Pawn pawn) => pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
            && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike && pawn.Drafted
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && !pawn.InMentalState;
        public static bool CanTreat(Pawn actor, Pawn patient) => CanAct(actor) && patient != null && patient.Spawned
            && !patient.Dead && patient.Map == actor.Map && patient.RaceProps.Humanlike && !patient.HostileTo(actor);
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
            pawn.health.hediffSet.DirtyCache();
            return effect;
        }
        public static void RemoveEffect(Pawn pawn, string name)
        {
            foreach (var h in pawn.health.hediffSet.hediffs.Where(h => h.def.defName == name).ToList()) pawn.health.RemoveHediff(h);
        }
        public static void Reject(string key) => Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput);

        public static void Start(Pawn actor, Pawn patient, TcccTreatment treatment, Thing supply = null)
        {
            if (!CanTreat(actor, patient)) return;
            if ((treatment == TcccTreatment.SelfHemostasis || treatment == TcccTreatment.Hemostatic)
                && patient.health.hediffSet.BleedRateTotal <= 0f) { Reject("HD_TCCC_NoBleeding"); return; }
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_TCCC_Treat"), patient);
            job.count = (int)treatment;
            if (supply != null) job.targetB = supply;
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static IEnumerable<Thing> Supplies(Pawn actor, Func<Thing, bool> valid)
        {
            if (actor.inventory != null)
                foreach (Thing thing in actor.inventory.innerContainer.InnerListForReading)
                    if (valid(thing)) yield return thing;
            foreach (Thing thing in actor.Map.listerThings.AllThings.Where(t => t.def.category == ThingCategory.Item
                && valid(t) && !t.IsForbidden(actor) && actor.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly, 1, 1))
                .OrderBy(t => actor.Position.DistanceToSquared(t.Position))) yield return thing;
        }

        public static void Target(Pawn actor, TcccTreatment treatment)
        {
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetPawns = true, canTargetBuildings = false, canTargetItems = false,
                validator = t => CanTreat(actor, t.Thing as Pawn)
                    && (treatment != TcccTreatment.AttachDrag || t.Thing != actor && ((Pawn)t.Thing).Downed)
            }, target =>
            {
                Pawn patient = target.Pawn;
                if (treatment == TcccTreatment.Analgesic)
                {
                    if (patient.health.hediffSet.PainTotal <= TcccRules.TargetPain)
                    { Reject("HD_TCCC_NoPain"); return; }
                    var options = Supplies(actor, t => TcccDrugs.IsAnalgesic(t.def) && t.TryGetComp<CompTcccDose>() != null)
                        .GroupBy(t => t.def).Select(group =>
                        {
                            Thing drug = group.First();
                            return new FloatMenuOption(drug.LabelCap, () => Start(actor, patient, treatment, drug));
                        }).ToList();
                    if (options.Count == 0) { Reject("HD_TCCC_NoAnalgesic"); return; }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                else if (treatment == TcccTreatment.Hemostatic)
                {
                    Thing supply = Supplies(actor, t => t.def.defName == "HD_TCCC_HemostaticAgent").FirstOrDefault();
                    if (supply == null) { Reject("HD_TCCC_NoHemostatic"); return; }
                    Start(actor, patient, treatment, supply);
                }
                else Start(actor, patient, treatment);
            }, caster: actor);
        }

        public static IEnumerable<Gizmo> Gizmos(Pawn pawn)
        {
            foreach (TcccTreatment treatment in Enum.GetValues(typeof(TcccTreatment)))
            {
                TcccTreatment local = treatment;
                yield return new Command_Action
                {
                    defaultLabel = ("HD_TCCC_" + local).Translate(),
                    defaultDesc = "HD_TCCC_MenuDesc".Translate(),
                    icon = IconFor(local),
                    action = () =>
                    {
                        if (local == TcccTreatment.SelfHemostasis) Start(pawn, pawn, local);
                        else Target(pawn, local);
                    }
                };
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
                case TcccTreatment.SelfHemostasis: skillIcon = "Skill/HD_TCCC_SelfHemostasis"; break;
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
        private TcccTreatment Treatment => (TcccTreatment)job.count;
        private Pawn Patient => job.targetA.Pawn;
        public override void ExposeData()
        { base.ExposeData(); Scribe_Values.Look(ref treatmentTicks, "treatmentTicks"); Scribe_Values.Look(ref pressureApplied, "pressureApplied"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return (Patient == pawn || pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed))
                && (!job.targetB.HasThing || !job.targetB.Thing.Spawned || pawn.Reserve(job.targetB, job, 1, 1, null, errorOnFailed));
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !TcccUtility.CanTreat(pawn, Patient));
            AddFinishAction(condition =>
            {
                if (pressureApplied) TcccUtility.RemoveEffect(pawn, "HD_TCCC_Pressure");
            });
            if (Treatment == TcccTreatment.Analgesic || Treatment == TcccTreatment.Hemostatic)
            {
                this.FailOn(() => job.targetB.Thing == null || job.targetB.Thing.Destroyed);
                Toil collectSupply = Toils_General.Do(() =>
                {
                    Thing supply = job.targetB.Thing;
                    if (supply.ParentHolder == pawn.inventory) return;
                    if (!supply.Spawned || !pawn.Position.AdjacentTo8WayOrInside(supply.Position))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    Thing unit = supply.SplitOff(1);
                    if (unit.Spawned) unit.DeSpawn();
                    if (!pawn.inventory.innerContainer.TryAdd(unit, false))
                    { GenPlace.TryPlaceThing(unit, pawn.Position, pawn.Map, ThingPlaceMode.Near); EndJobWith(JobCondition.Incompletable); return; }
                    job.targetB = unit;
                });
                // Keep the exact same toil sequence after save/load, even once the supply
                // has moved into inventory. Conditional list construction shifts saved indices.
                yield return Toils_Jump.JumpIf(collectSupply, () => job.targetB.Thing?.ParentHolder == pawn.inventory);
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
                yield return collectSupply;
            }
            if (Patient != pawn) yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            int duration = Treatment == TcccTreatment.SelfHemostasis ? TcccRules.SelfHemostasisTicks
                : Treatment == TcccTreatment.Hemostatic ? TcccRules.HemostaticTicks
                : Treatment == TcccTreatment.Mist ? 300 : 120;
            Toil treatment = Toils_General.Wait(duration, TargetIndex.A);
            treatment.WithProgressBarToilDelay(TargetIndex.A);
            treatment.FailOn(() => Patient != pawn && !pawn.Position.AdjacentTo8WayOrInside(Patient.Position));
            treatment.tickAction = () =>
            {
                treatmentTicks++;
                if (Treatment == TcccTreatment.SelfHemostasis && treatmentTicks >= TcccRules.PartialHemostasisTicks && !pressureApplied)
                {
                    pressureApplied = true;
                    TcccUtility.ApplyTimed(pawn, "HD_TCCC_Pressure", TcccRules.SelfHemostasisTicks);
                }
            };
            yield return treatment;
            yield return Toils_General.Do(CompleteTreatment);
        }
        private void CompleteTreatment()
        {
            switch (Treatment)
            {
                case TcccTreatment.SelfHemostasis:
                    TcccUtility.ApplyTimed(pawn, "HD_TCCC_SelfHemostasis", TcccRules.SelfEffectTicks);
                    break;
                case TcccTreatment.AttachDrag:
                    if (!pawn.Map.GetComponent<MapComponent_TcccDragging>().Attach(pawn, Patient)) TcccUtility.Reject("HD_TCCC_CannotDrag");
                    break;
                case TcccTreatment.Hemostatic:
                    if (Patient.health.hediffSet.BleedRateTotal <= 0f) { TcccUtility.Reject("HD_TCCC_NoBleeding"); return; }
                    TcccUtility.ApplyTimed(Patient, "HD_TCCC_Hemostatic", TcccRules.DrugEffectTicks);
                    job.targetB.Thing.SplitOff(1).Destroy();
                    break;
                case TcccTreatment.Analgesic:
                    TcccDrugs.Administer(pawn, Patient, job.targetB.Thing);
                    break;
                case TcccTreatment.Mist:
                    TcccUtility.ApplyTimed(Patient, "HD_TCCC_Mist", 6 * GenDate.TicksPerHour).report = TcccUtility.MistReport(Patient);
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_TcccGizmos
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        { if (TcccUtility.CanAct(__instance)) __result = __result.Concat(TcccUtility.Gizmos(__instance)); }
    }

    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.BleedRateTotal), MethodType.Getter)]
    public static class Patch_TcccBleeding
    {
        public static void Postfix(HediffSet __instance, ref float __result)
        {
            if (__result <= 0f) return;
            Pawn pawn = __instance.pawn;
            __result *= TcccRules.BleedingFactor(TcccUtility.Effect(pawn, "HD_TCCC_Pressure") != null,
                TcccUtility.Effect(pawn, "HD_TCCC_SelfHemostasis") != null,
                TcccUtility.Effect(pawn, "HD_TCCC_Hemostatic") != null);
        }
    }
}
