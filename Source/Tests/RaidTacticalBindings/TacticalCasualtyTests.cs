using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

internal static class TacticalCasualtyTests
{
    internal static void Run()
    {
        void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
        Check(TacticalCasualtyPolicy.Approach(true,1,true) && TacticalCasualtyPolicy.Approach(true,2,true)
            && !TacticalCasualtyPolicy.Approach(true,1,false) && !TacticalCasualtyPolicy.Approach(true,4,true),
            "Under-fire extraction permits only covered, immediate adjacent approaches.");
        Check(TacticalCasualtyPolicy.Approach(false,32*32,false) && !TacticalCasualtyPolicy.Approach(false,32*32+1,true),
            "Quiet casualty retrieval remains local, with a bounded 32-cell approach.");
        Check(!TacticalMedicalPolicy.PreferAgent(3) && TacticalMedicalPolicy.PreferAgent(4),
            "Few bleeding parts use dressings; widespread bleeding prefers the systemic agent.");
        var service=new MapComponent_TacticalCommands(null);
        var patient=new TacticalMemberCommand {Pawn=new Pawn()}; var helper=new TacticalMemberCommand();
        patient.Pawn.health=(Pawn_HealthTracker)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        AccessTools.Field(typeof(Pawn_HealthTracker),"healthState").SetValue(patient.Pawn.health,PawnHealthState.Mobile);
        var interrupted=new TacticalMedicalCare {Patient=patient,Helper=helper,Started=100};
        var command=new TacticalSquadCommand {MedicalCare=interrupted};
        AccessTools.Method(service.GetType(),"EndMedicalCare").Invoke(service,new object[]{command,interrupted,200,false});
        Check(command.MedicalCare==null && command.NextMedicalCheck==290
            && patient.LastCareAt+TacticalMedicalPolicy.RetryInterval==290,
            "Threat-interrupted care must become eligible again after 90 ticks instead of a 30-second lockout.");
        Check(TacticalCasualtyPolicy.RetreatPhase(new LordToil_ExitMap())
            && TacticalCasualtyPolicy.RetreatPhase(new LordToil_ExitMapAndDefendSelf())
            && !TacticalCasualtyPolicy.RetreatPhase(new LordToil_AssaultColony()),
            "Native exit phases must remain distinguishable from continuing assault.");
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var jobs=XDocument.Load(Path.Combine(root,"Defs/Organization/NewTacticalJobs.xml"));
        var job=jobs.Root.Elements("JobDef").Single(x=>(string)x.Element("defName")==TacticalCasualtyPolicy.JobName);
        Check((string)job.Element("driverClass")==typeof(JobDriver_TacticalCasualtyMove).FullName
            && (string)job.Element("casualInterruptible")=="false","Extraction must use the exclusive movement/attachment driver.");
        var kinds=XDocument.Load(Path.Combine(root,"Defs/Helod/Pawns/PawnKinds.xml"));
        var high=kinds.Root.Elements("PawnKindDef").Single(x=>(string)x.Element("defName")=="HD_MW_HelodRifleman");
        Check(!high.Descendants("armorPresets").Elements("li").Any(x=>x.Value.Contains("GPNVG")),
            "HIGH spawn presets must not silently reinstall night vision.");
        Check(AccessTools.Method(typeof(JobDriver_TacticalCasualtyMove),nameof(JobDriver_TacticalCasualtyMove.ExposeData)).DeclaringType==typeof(JobDriver_TacticalCasualtyMove),
            "Attachment and firing state need native driver persistence.");
        Console.WriteLine("PASS: casualty approach/threat/treatment boundaries, retreat phases, extraction JobDef and no NVG spawn.");
    }
}
