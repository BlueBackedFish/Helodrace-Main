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
        private Apparel bag;
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
                if (h is Hediff_Injury || h is Hediff_PartHemostasis || h.def==HediffDefOf.BloodLoss) actor.health.RemoveHediff(h);
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
                        File.WriteAllText(Results,"Part-specific hemostasis native checks\n");
                        // The real bag is Helod-only apparel. A human colonist fixture
                        // cannot equip it and would invalidate the bag-job test.
                        actor=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("HD_GW_HelodRifleman"),Faction.OfPlayer);
                        GenSpawn.Spawn(actor,Find.CurrentMap.Center,Find.CurrentMap);
                        actor.drafter.Drafted=true;
                        actor.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining"));
                        var arms=actor.RaceProps.body.AllParts.Where(p=>p.def==BodyPartDefOf.Arm).ToArray();
                        left=arms[0]; right=arms[1];
                        leftWound=Wound(left); rightWound=Wound(right);
                        float leftBefore=leftWound.BleedRate, rightBefore=rightWound.BleedRate;
                        float leftScaled=leftWound.BleedRateScaled, totalBefore=actor.health.hediffSet.BleedRateTotal;
                        var weak=PartHemostasis.Apply(actor,left,"HD_FieldHemostasis",.30f,15000);
                        Check(Math.Abs(leftWound.BleedRate/leftBefore-.30f)<.001f && Math.Abs(rightWound.BleedRate/rightBefore-1f)<.001f,
                            "Actual patched getters reduce only the treated arm");
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal-(totalBefore-leftScaled*.70f))<.0001f,
                            "Actual total bleeding uses the part factor once");
                        var strong=PartHemostasis.Apply(actor,left,"HD_TCCC_SelfHemostasis",.05f,45000);
                        Check(Math.Abs(leftWound.BleedRate/leftBefore-.05f)<.001f,"TCCC and bag on one part select strongest without multiplying");
                        strong.expiresTick=GenTicks.TicksGame; PartHemostasis.Invalidate(actor.health.hediffSet);
                        Check(Math.Abs(leftWound.BleedRate/leftBefore-.30f)<.001f,"Expired strong marker exposes weaker active dressing");
                        actor.health.RemoveHediff(weak);
                        Check(Math.Abs(leftWound.BleedRate/leftBefore-1f)<.001f,"Removing dressing restores native part bleeding");
                        Check(!leftWound.IsTended(),"Direct dressing does not tend the wound");
                        Reset();
                        Thing dose=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_TCCC_HemostaticAgent"));
                        dose.stackCount=2; actor.inventory.innerContainer.TryAdd(dose);
                        TcccUtility.Start(actor,actor,TcccTreatment.Hemostatic,dose); Next(1); break;
                    case 1:
                        if (WorkTicks>=40 && !addedLater) {rightWound=Wound(right); addedLater=true;}
                        if (TcccUtility.Effect(actor,"HD_TCCC_Hemostatic")==null) break;
                        Check(addedLater && actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>()
                            .Count(h=>h.def.defName=="HD_TCCC_Hemostatic" && h.Part==left)==1
                            && PartHemostasis.Factor(actor.health.hediffSet,right,GenTicks.TicksGame)==1f,
                            "Real 3-second TCCC job latches treated parts and excludes a later opposite-arm wound");
                        Check(actor.inventory.innerContainer.Where(t=>t.def.defName=="HD_TCCC_HemostaticAgent").Sum(t=>t.stackCount)==1,
                            "Real TCCC job consumes exactly one dose");
                        Reset(); TcccUtility.Start(actor,actor,TcccTreatment.SelfHemostasis); Next(2); break;
                    case 2:
                        if (WorkTicks<1201) break;
                        Check(TcccUtility.Effect(actor,"HD_TCCC_Pressure")?.Part==left
                            && PartHemostasis.Factor(actor.health.hediffSet,left,GenTicks.TicksGame)==.30f,
                            "Real 20-second pressure phase uses a part marker");
                        actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        Check(TcccUtility.Effect(actor,"HD_TCCC_Pressure")==null
                            && PartHemostasis.Factor(actor.health.hediffSet,left,GenTicks.TicksGame)==1f,
                            "Interruption removes temporary pressure and cached reduction");
                        Reset(); TcccUtility.Start(actor,actor,TcccTreatment.SelfHemostasis); Next(3); break;
                    case 3:
                        if (WorkTicks>=1201 && !addedLater) {rightWound=Wound(right); addedLater=true;}
                        if (TcccUtility.Effect(actor,"HD_TCCC_SelfHemostasis")==null) break;
                        Check(addedLater && TcccUtility.Effect(actor,"HD_TCCC_SelfHemostasis")?.Part==left
                            && PartHemostasis.Factor(actor.health.hediffSet,left,GenTicks.TicksGame)==.05f
                            && PartHemostasis.Factor(actor.health.hediffSet,right,GenTicks.TicksGame)==1f
                            && TcccUtility.Effect(actor,"HD_TCCC_Pressure")==null,
                            "Real 30-second self-care retains its treated part, excludes new wounds elsewhere and removes pressure");
                        Reset(); rightWound=Wound(right);
                        ThingDef bagDef=DefDatabase<ThingDef>.GetNamed("HD_Apparel_GreatWarMedibag");
                        bag=(Apparel)ThingMaker.MakeThing(bagDef,GenStuff.DefaultStuffFor(bagDef)); actor.apparel.Wear(bag);
                        Check(bag.Wearer==actor && bag.TryGetComp<CompMedibag>().CurrentWearer==actor,
                            "Real Helod soldier can wear the bag and its comp resolves the actual wearer");
                        var comp=bag.TryGetComp<CompMedibag>();
                        AccessTools.Field(typeof(CompMedibag),"storedSupplies").SetValue(comp,2);
                        actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_MedibagHemostasis"),actor,bag),JobTag.Misc);
                        Next(4); break;
                    case 4:
                        if (GenTicks.TicksGame-started <= 300 && (GenTicks.TicksGame-started)%15==0)
                            File.AppendAllText(Results,"TRACE bag t="+(GenTicks.TicksGame-started)+" job="+actor.CurJob
                                +" wearer="+(bag.TryGetComp<CompMedibag>().CurrentWearer==actor)
                                +" supplies="+AccessTools.Field(typeof(CompMedibag),"storedSupplies").GetValue(bag.TryGetComp<CompMedibag>())+"\n");
                        if (!actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Any(h=>h.def.defName=="HD_FieldHemostasis")) break;
                        Check(actor.health.hediffSet.hediffs.OfType<Hediff_PartHemostasis>().Count(h=>h.def.defName=="HD_FieldHemostasis")==2
                            && !leftWound.IsTended() && !rightWound.IsTended(),"Real medic bag job dresses two parts without vanilla tending");
                        Check((int)AccessTools.Field(typeof(CompMedibag),"storedSupplies").GetValue(bag.TryGetComp<CompMedibag>())==1,
                            "Real medic bag job consumes one loaded supply");
                        File.AppendAllText(Results,"COMPLETE\n"); finished=true; Root.Shutdown(); break;
                }
            }
            catch (Exception error)
            { File.AppendAllText(Results,"FAIL "+error+"\n"); finished=true; Root.Shutdown(); }
        }
    }
}
#endif
