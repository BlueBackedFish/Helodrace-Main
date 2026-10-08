#if HEMOSTASIS_TESTS
using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    // Short isolated -quicktest regression, absent from production builds.
    public sealed class PartHemostasisInGameChecks : GameComponent
    {
        private Pawn actor;
        private BodyPartRecord left, right;
        private Hediff leftWound, rightWound;
        private Hediff_PartHemostasis completedDressing;
        private Apparel bag;
        private Pawn patient;
        private BodyPartRecord[] patientWoundedParts;
        private int phase, started;
        private bool finished, addedLater;
        private string Results => Path.Combine(GenFilePaths.SaveDataFolderPath,"part-hemostasis-results.txt");
        public PartHemostasisInGameChecks(Game game) { }
        private void Check(bool value, string description)
        {
            if (!value) throw new Exception(description);
            File.AppendAllText(Results,"PASS "+description+"\n");
        }
        private void Next(int value) { phase=value; started=GenTicks.TicksGame; addedLater=false; }
        private int WorkTicks => actor?.jobs?.curDriver is JobDriver_TcccTreatment driver
            ? (int)AccessTools.Field(typeof(JobDriver_TcccTreatment),"treatmentTicks").GetValue(driver) : -1;
        private Hediff Wound(BodyPartRecord part)
        {
            Hediff wound=HediffMaker.MakeHediff(HediffDefOf.Cut,actor,part);
            wound.Severity=5f; actor.health.AddHediff(wound); return wound;
        }
        private void Reset()
        {
            actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
            foreach (Hediff h in actor.health.hediffSet.hediffs.ToArray())
                if (h is Hediff_Injury || h is Hediff_PartHemostasis || h is Hediff_SystemicHemostatic
                    || h.def==HediffDefOf.BloodLoss) actor.health.RemoveHediff(h);
            leftWound=Wound(left); rightWound=null;
        }
        public override void GameComponentUpdate()
        { if (!finished && Find.CurrentMap!=null) Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast; }
        public override void GameComponentTick()
        {
            if (finished || Find.CurrentMap==null) return;
            try
            {
                if (phase!=0 && GenTicks.TicksGame-started>5000) throw new Exception("Timeout phase "+phase+" job="+actor.CurJob);
                switch (phase)
                {
                    case 0:
                        File.WriteAllText(Results,"Unified dressings and systemic hemostatic native checks\n");
                        foreach (var colonist in Find.CurrentMap.mapPawns.FreeColonistsSpawned)
                            if (colonist.drafter != null && !colonist.Downed) colonist.drafter.Drafted=true;
                        actor=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("HD_GW_HelodRifleman"),Faction.OfPlayer);
                        actor.story.traits.allTraits.Clear();
                        GenSpawn.Spawn(actor,Find.CurrentMap.Center,Find.CurrentMap);
                        actor.drafter.Drafted=true;
                        actor.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining"));
                        var arms=actor.RaceProps.body.AllParts.Where(p=>p.def==BodyPartDefOf.Arm).ToArray();
                        left=arms[0]; right=arms[1];
                        leftWound=Wound(left); rightWound=Wound(right);
                        float leftBefore=leftWound.BleedRate, rightBefore=rightWound.BleedRate;
                        float leftScaled=leftWound.BleedRateScaled, totalBefore=actor.health.hediffSet.BleedRateTotal;
                        float painBefore=actor.health.hediffSet.PainTotal;
                        var dressing=PartHemostasis.ApplyDressing(actor,left);
                        Check(Math.Abs(leftWound.BleedRate/leftBefore-.20f)<.001f && Math.Abs(rightWound.BleedRate/rightBefore-1f)<.001f,
                            "Dressing reduces only treated arm bleeding by 80 percent");
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal-(totalBefore-leftScaled*.80f))<.0001f,
                            "Total bleeding applies the local factor once");
                        Check(dressing.expiresTick-GenTicks.TicksGame==30000,"Shared dressing lasts 12 hours");
                        Check(ReferenceEquals(dressing,PartHemostasis.ApplyDressing(actor,left))
                            && actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Count()==1,
                            "Reapplying a dressing refreshes the same part marker");
                        Check(Math.Abs(actor.health.hediffSet.PainTotal-painBefore-.05f)<.001f,
                            "One actual dressing adds five percentage points of native pain");
                        float bloodBefore=actor.health.capacities.GetLevel(PawnCapacityDefOf.BloodPumping);
                        float consciousnessBefore=actor.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                        var drug=TcccUtility.ApplyTimed(actor,"HD_TCCC_Hemostatic",TcccRules.DrugEffectTicks);
                        Check(drug.Part==null && Math.Abs(leftWound.BleedRate/leftBefore-.06f)<.001f
                            && Math.Abs(rightWound.BleedRate/rightBefore-.30f)<.001f,
                            "Systemic drug combines with dressing: six percent bleeding on treated arm, thirty elsewhere");
                        Check(drug.CurStage.capMods.Any(c=>c.capacity==PawnCapacityDefOf.BloodPumping && c.offset==-.10f)
                            && drug.CurStage.capMods.Any(c=>c.capacity==PawnCapacityDefOf.Consciousness && c.offset==-.05f),
                            "Loaded drug stage has blood pumping minus ten and consciousness minus five percentage points");
                        Check(actor.health.capacities.GetLevel(PawnCapacityDefOf.BloodPumping)<=bloodBefore-.099f
                            && actor.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness)<=consciousnessBefore-.049f,
                            "Actual native capacities reflect drug penalties");
                        var leg=actor.RaceProps.body.AllParts.First(p=>p.def==BodyPartDefOf.Leg);
                        var later=Wound(leg); float laterDrugBleed=later.BleedRate;
                        actor.health.RemoveHediff(drug);
                        Check(Math.Abs(laterDrugBleed/later.BleedRate-.30f)<.001f,
                            "Systemic drug also reduces a wound acquired after administration");
                        Check(Math.Abs(leftWound.BleedRate/leftBefore-.20f)<.001f
                            && actor.health.capacities.GetLevel(PawnCapacityDefOf.BloodPumping)>=bloodBefore-.001f,
                            "Removing drug preserves local dressing and releases circulation penalty");
                        float painBase=actor.health.hediffSet.PainTotal-.05f;
                        foreach (var part in actor.RaceProps.body.AllParts.Where(p=>p!=left).Take(5)) PartHemostasis.ApplyDressing(actor,part);
                        Check(Math.Abs(actor.health.hediffSet.PainTotal-painBase-.20f)<.001f,
                            "Six actual dressed parts cap added native pain at twenty percent");
                        Check(!leftWound.IsTended(),"Dressings do not tend the wound");
                        Reset();
                        Check(Math.Abs(actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Sum(h=>h.PainOffset))<.001f,
                            "Removing dressings releases all dressing pain");
                        Thing dose=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_TCCC_HemostaticAgent"));
                        dose.stackCount=2; actor.inventory.innerContainer.TryAdd(dose);
                        TcccUtility.Start(actor,actor,TcccTreatment.Hemostatic,dose); Next(1); break;
                    case 1:
                        if (WorkTicks>=40 && !addedLater) {rightWound=Wound(right); addedLater=true;}
                        if (TcccUtility.Effect(actor,"HD_TCCC_Hemostatic")==null) break;
                        Check(addedLater && TcccUtility.Effect(actor,"HD_TCCC_Hemostatic") is Hediff_SystemicHemostatic
                            && !actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Any()
                            && PartHemostasis.Factor(actor.health.hediffSet,right,GenTicks.TicksGame)==.30f,
                            "Real three-second drug job creates one systemic effect covering the other arm");
                        Check(actor.inventory.innerContainer.Where(t=>t.def.defName=="HD_TCCC_HemostaticAgent").Sum(t=>t.stackCount)==1,
                            "Real drug job consumes exactly one dose");
                        Reset(); TcccUtility.Start(actor,actor,TcccTreatment.Hemostasis); Next(2); break;
                    case 2:
                        if (WorkTicks<1201) break;
                        Check(TcccUtility.Effect(actor,"HD_TCCC_Pressure")?.Part==left
                            && PartHemostasis.Factor(actor.health.hediffSet,left,GenTicks.TicksGame)==.30f,
                            "Twenty-second temporary pressure stage remains part-specific");
                        actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        Check(TcccUtility.Effect(actor,"HD_TCCC_Pressure")==null
                            && PartHemostasis.Factor(actor.health.hediffSet,left,GenTicks.TicksGame)==1f,
                            "Interruption removes temporary pressure and its cached reduction");
                        Reset(); TcccUtility.Start(actor,actor,TcccTreatment.Hemostasis); Next(3); break;
                    case 3:
                        if (WorkTicks>=1201 && !addedLater) {rightWound=Wound(right); addedLater=true;}
                        if (TcccUtility.Effect(actor,PartHemostasis.DressingDefName)==null) break;
                        completedDressing=(Hediff_PartHemostasis)TcccUtility.Effect(actor,PartHemostasis.DressingDefName);
                        Check(addedLater && completedDressing.Part==left
                            && PartHemostasis.Factor(actor.health.hediffSet,left,GenTicks.TicksGame)==.20f
                            && PartHemostasis.Factor(actor.health.hediffSet,right,GenTicks.TicksGame)==1f
                            && TcccUtility.Effect(actor,"HD_TCCC_Pressure")==null,
                            "Completed self-care creates shared dressing on latched part, not the later opposite-arm wound");
                        ThingDef bagDef=DefDatabase<ThingDef>.GetNamed("HD_Apparel_GreatWarMedibag");
                        bag=(Apparel)ThingMaker.MakeThing(bagDef,GenStuff.DefaultStuffFor(bagDef)); actor.apparel.Wear(bag);
                        Check(bag.Wearer==actor && bag.TryGetComp<CompMedibag>().CurrentWearer==actor,
                            "Real Helod soldier wears the bag and its comp resolves wearer");
                        AccessTools.Field(typeof(CompMedibag),"storedSupplies").SetValue(bag.TryGetComp<CompMedibag>(),2);
                        actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_MedibagHemostasis"),actor,bag),JobTag.Misc);
                        Next(4); break;
                    case 4:
                        if (actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Count(h=>h.def.defName==PartHemostasis.DressingDefName)<2) break;
                        Check(actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Count()==2
                            && actor.health.hediffSet.hediffs.Contains(completedDressing)
                            && !leftWound.IsTended() && !rightWound.IsTended(),
                            "Real bag job reuses self-care dressing and adds only one new part without tending");
                        Check((int)AccessTools.Field(typeof(CompMedibag),"storedSupplies").GetValue(bag.TryGetComp<CompMedibag>())==1,
                            "Real bag job consumes one loaded supply");
                        Check(GenTicks.TicksGame-started>=1800 && GenTicks.TicksGame-started<1830,
                            "Actual bag hemostasis takes thirty seconds");
                        Check(Math.Abs(actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Sum(h=>h.PainOffset)-.10f)<.001f,
                            "Two shared dressings add ten percent pain, not three markers worth");
                        patient=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("HD_GW_HelodRifleman"),Faction.OfPlayer);
                        GenSpawn.Spawn(patient,actor.Position+IntVec3.East,actor.Map);
                        patient.health.AddHediff(HediffDefOf.Anesthetic);
                        var targets=patient.RaceProps.body.AllParts.Where(p=>new[]{"Brain","Heart","Liver","Lung","Arm","Leg"}
                            .Contains(p.def.defName)).Take(8).ToArray();
                        patientWoundedParts=targets;
                        Check(targets.Length==8,"Other-pawn fixture has eight distinct bleeding body parts");
                        foreach (var part in targets)
                        {
                            var wound=HediffMaker.MakeHediff(HediffDefOf.Cut,patient,part);
                            wound.Severity=part.def.defName=="Brain" ? 4f : 2f; patient.health.AddHediff(wound);
                        }
                        var selectedPatientParts=PartHemostasis.BleedingParts(patient,TcccRules.HemostasisMaxParts).ToArray();
                        Check(TcccUtility.CanTreat(actor,patient) && selectedPatientParts.Length==6
                            && selectedPatientParts.Any(p=>p.def.defName=="Brain"),
                            "Other pawn is eligible and vital brain part is not excluded from six-part skill selection");
                        TcccUtility.Start(actor,patient,TcccTreatment.Hemostasis); Next(5); break;
                    case 5:
                        if (WorkTicks<1201) break;
                        Check(patient.health.hediffSet.hediffs.Count(h=>h.def.defName=="HD_TCCC_Pressure")==6
                            && TcccUtility.Effect(actor,"HD_TCCC_Pressure")==null,
                            "Actual other-pawn hemostasis applies pressure to six patient parts, never the caregiver");
                        actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        Check(TcccUtility.Effect(patient,"HD_TCCC_Pressure")==null,
                            "Interrupted other-pawn hemostasis removes patient pressure");
                        TcccUtility.Start(actor,patient,TcccTreatment.Hemostasis); Next(6); break;
                    case 6:
                        var dressings=patient.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>()
                            .Where(h=>h.def.defName==PartHemostasis.DressingDefName).ToArray();
                        if (dressings.Length==0) break;
                        File.AppendAllText(Results,"TRACE patient completed parts="+string.Join(",",dressings.Select(h=>h.Part.def.defName))
                            +" pressure="+(TcccUtility.Effect(patient,"HD_TCCC_Pressure")!=null)+"\n");
                        Check(dressings.Length==6 && dressings.All(h=>patientWoundedParts.Contains(h.Part))
                            && dressings.Any(h=>h.Part.def.defName=="Brain")
                            && TcccUtility.Effect(patient,"HD_TCCC_Pressure")==null,
                            "Actual other-pawn thirty-second skill dresses exactly six wounded parts including brain");
                        File.AppendAllText(Results,"COMPLETE\n"); finished=true; Root.Shutdown(); break;
                }
            }
            catch (Exception error)
            { File.AppendAllText(Results,"FAIL "+error+"\n"); finished=true; Root.Shutdown(); }
        }
    }
}
#endif
