using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace.Tactical;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

internal static class TacticalPrisonerTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string reason) { if(!value) throw new Exception(reason); checks++; }
        Check(TacticalPrisonerPolicy.NeedsAid(true,true,15000) && TacticalPrisonerPolicy.NeedsAid(true,true,0)
            && !TacticalPrisonerPolicy.NeedsAid(true,true,15001) && !TacticalPrisonerPolicy.NeedsAid(true,true,int.MaxValue)
            && !TacticalPrisonerPolicy.NeedsAid(false,true,100) && !TacticalPrisonerPolicy.NeedsAid(true,false,100),
            "Only incapacitated enemies in secured rooms with at most six game hours of bleeding survival qualify for aid.");
        Check(TacticalPrisonerPolicy.CarePhase(TacticalCommandPhase.Complete)
            && TacticalPrisonerPolicy.CarePhase(TacticalCommandPhase.Clear)
            && !TacticalPrisonerPolicy.CarePhase(TacticalCommandPhase.Enter),
            "Captive handling must run during completed/native destruction, without stealing committed ingress.");
        var owner=new MapComponent_TacticalCommands(null);
        var parent=new TacticalSquadCommand {Phase=TacticalCommandPhase.Clear};
        parent.CqbTeams.Add(new TacticalSquadCommand {Phase=TacticalCommandPhase.Enter});
        Check(!(bool)AccessTools.Method(owner.GetType(),"AdvancePrisoners")
            .Invoke(owner,new object[]{parent,new List<TacticalMemberCommand>(),100}),
            "A HIGH root in Clear must still leave its entering child in control instead of scanning for captives.");
        var patient=new Pawn {thingIDNumber=70,def=(ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef))};
        patient.health=(Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        patient.health.hediffSet=new HediffSet(patient);
        var state=AccessTools.Field(typeof(Pawn_HealthTracker),"healthState");
        state.SetValue(patient.health,PawnHealthState.Down);
        Check(!TemporaryRestraint.CanAid(patient),"An unrestrained hostile cannot be admitted through the special aid predicate.");
        var restraint=(HediffDef)RuntimeHelpers.GetUninitializedObject(typeof(HediffDef)); restraint.defName=TemporaryRestraint.DefName;
        patient.health.hediffSet.hediffs.Add(new Hediff {def=restraint,pawn=patient});
        Check(TemporaryRestraint.CanAid(patient),"The actual restraint hediff and native downed state admit aid.");
        state.SetValue(patient.health,PawnHealthState.Mobile);
        Check(!TemporaryRestraint.CanAid(patient),"A mobile hostile must never gain treatment eligibility from a stale marker alone.");
        state.SetValue(patient.health,PawnHealthState.Dead);
        Check(!TemporaryRestraint.CanAid(patient),"Dead enemies cannot qualify as captive patients.");
        state.SetValue(patient.health,PawnHealthState.Down);

        var command=new TacticalSquadCommand();
        var failed=AccessTools.Method(typeof(MapComponent_TacticalCommands),"EvacuationFailed");
        var def=(JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef)); def.defName=TacticalCasualtyPolicy.JobName;
        var job=new Job {def=def,targetA=patient,exitMapOnArrival=true};
        command.Evacuees.Add(patient);
        failed.Invoke(null,new object[]{command,job,JobCondition.InterruptForced});
        failed.Invoke(null,new object[]{command,job,JobCondition.Succeeded});
        Check(command.EvacuationFailures.Count==0,"Successful delivery and safety interruption cannot count as extraction path failures.");
        for(int i=0;i<3;i++) failed.Invoke(null,new object[]{command,job,JobCondition.Incompletable});
        Check(command.EvacuationFailures[patient]==3 && command.EvacuationUnreachable.Contains(patient)
            && !command.Evacuees.Contains(patient),"Repeated real extraction failures must leave the work queue instead of trapping the whole raid forever.");
        command.MedicalCare=new TacticalMedicalCare {Captive=true};
        Check(command.PrisonerWorkPending,"Sweep completion must not interrupt an admitted captive treatment.");
        command.MedicalCare=null; command.PrisonerScanCursor=1;
        Check(command.PrisonerWorkPending,"A partial captive scan must finish before the sweep completion memo.");

        var actor=new Pawn();
        actor.jobs=(Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        var evacuation=new JobDriver_TacticalCasualtyMove {pawn=actor,job=job};
        actor.jobs.curDriver=evacuation;
        var toils=((IEnumerable<Toil>)AccessTools.Method(evacuation.GetType(),"MakeNewToils").Invoke(evacuation,null)).ToList();
        Check(toils.Count==7 && toils.Last().defaultCompleteMode==ToilCompleteMode.Never && toils.Last().tickAction!=null,
            "Actual evacuation graph must reserve/admit approach, attach, restrain, clear old cargo, carry and continue covering/retrieving.");
        var binding=new JobDriver_TemporaryRestrain {pawn=actor,job=new Job {def=def,targetA=patient}};
        toils=((IEnumerable<Toil>)AccessTools.Method(binding.GetType(),"MakeNewToils").Invoke(binding,null)).ToList();
        Check(toils.Count==4 && toils[2].defaultDuration==TemporaryRestraint.WorkTicks && toils.Last().initAction!=null,
            "Actual binding/release graph must approach and perform physical work rather than changing a remote pawn instantly.");

        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var defs=XDocument.Load(Path.Combine(root,"Defs/Tactical/TemporaryRestraint.xml"));
        var hediff=defs.Root.Element("HediffDef");
        Check(hediff.Descendants("capMods").Elements("li").Count()==2
            && hediff.Descendants("capMods").Elements("li").All(x=>(string)x.Element("setMax")=="0")
            && !hediff.Descendants("disappearsAfterTicks").Any(),
            "The actual restraint definition prevents movement/manipulation after recovery and persists until explicitly released.");
        Check((string)defs.Root.Element("JobDef").Element("driverClass")==typeof(JobDriver_TemporaryRestrain).FullName
            && typeof(FloatMenuOptionProvider).IsAssignableFrom(typeof(FloatMenuOptionProvider_TemporaryRestraint)),
            "Restraint must have a live native job binding and a player release provider.");
        Console.WriteLine($"PASS: {checks} captive thresholds, actual hediff eligibility, native work/evacuation toils, bounded failures and definition checks.");
    }
}
