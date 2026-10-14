using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalFieldCoordinationTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value,string reason) { checks++; if (!value) throw new Exception(reason); }
        var origin = new IntVec3(40,0,40); var focus = origin + IntVec3.North * 40;
        var group = new TacticalFieldFormation { Coordinator = "A", Members = new List<string> { "A","B","C" },
            Seen = new Dictionary<string,int> { ["A"] = 100, ["B"] = 100, ["C"] = 100 }, Forward = IntVec3.North };
        group.Sectors["A"] = new TacticalFieldSector { Origin = origin, Focus = focus, At = 100, Available = 12, Ready = 2 };
        group.Sectors["B"] = new TacticalFieldSector { Origin = origin, Focus = focus, At = 100, Available = 12 };
        group.Sectors["C"] = new TacticalFieldSector { Origin = origin + IntVec3.East * 20, Focus = focus, At = 100, Available = 13 };
        Check(TacticalFieldCoordinationPolicy.Assignee(group,"A",120) == "B", "The crowded peer, not the coordinator's own squad, receives the sortie.");
        Check(TacticalFieldCoordinationPolicy.Assignee(group,"B",120) == null, "A recipient cannot independently nominate another squad.");
        group.Sectors["A"].Ready = 0; group.Sectors["B"].Ready = 2;
        Check(TacticalFieldCoordinationPolicy.Assignee(group,"A",120) == "C", "A non-shooting commander may use another reported supporting squad rather than freeze the group.");
        group.Sectors["A"].Ready = 2; group.Sectors["B"].Ready = 0;
        group.Sectors["C"].Busy = true;
        Check(TacticalFieldCoordinationPolicy.Assignee(group,"A",120) == null, "A reported moving sortie prevents a second concurrent agreed sortie.");
        group.Sectors["C"].Busy = false;
        Check(TacticalFieldCoordinationPolicy.Assignee(group,"A",700) == null, "Stale sector reports cannot authorize a new sortie.");
        var copied = group.Copy(); copied.Sectors["B"].Origin = focus;
        Check(group.Sectors["B"].Origin == origin, "Shared sectors must be independent value copies.");
        var offer = new TacticalFieldMission { Coordinator = "A", Assignee = "B", Epoch = 10, Number = 1,
            Started = 120, Until = 2520, Side = 1, Origin = origin, Focus = focus };
        TacticalFieldMission receiver = null, sender = offer.Copy();
        var premature = offer.Copy(); premature.Stage = TacticalFieldMissionStage.Committed;
        Check(!TacticalFieldCoordinationPolicy.Receive(ref receiver,premature,"A","B",group,true,130), "Start before receipt and acceptance must fail.");
        Check(!TacticalFieldCoordinationPolicy.Receive(ref receiver,offer,"C","B",group,true,130), "A third member cannot forge a coordinator order.");
        Check(!TacticalFieldCoordinationPolicy.Receive(ref receiver,offer,"A","B",group,false,130), "Busy or unavailable recipient must decline.");
        Check(TacticalFieldCoordinationPolicy.Receive(ref receiver,offer,"A","B",group,true,130)
            && receiver.Stage == TacticalFieldMissionStage.Accepted, "Offer receipt records acceptance, not permission to move.");
        Check(TacticalFieldCoordinationPolicy.Receive(ref receiver,offer,"A","B",group,true,140), "Duplicate offer may resend its acknowledgement without creating a new mission.");
        Check(TacticalFieldCoordinationPolicy.Receive(ref sender,receiver.Copy(),"B","A",group,true,150)
            && sender.Stage == TacticalFieldMissionStage.Committed, "Only the actual assignee acknowledgement commits the reservation.");
        Check(TacticalFieldCoordinationPolicy.Receive(ref receiver,sender.Copy(),"A","B",group,true,170)
            && receiver.Stage == TacticalFieldMissionStage.Committed, "Movement requires the delivered final commitment.");
        var altered = sender.Copy(); altered.Focus += IntVec3.East;
        Check(!TacticalFieldCoordinationPolicy.Receive(ref receiver,altered,"A","B",group,true,180), "Changed geometry cannot masquerade as the accepted order.");
        receiver.Stage = TacticalFieldMissionStage.Active;
        Check(TacticalFieldCoordinationPolicy.Receive(ref sender,receiver.Copy(),"B","A",group,true,200), "Delivered status records actual remote execution.");
        receiver.Stage = TacticalFieldMissionStage.Finished;
        Check(TacticalFieldCoordinationPolicy.Receive(ref sender,receiver.Copy(),"B","A",group,true,220), "Only a report from the assignee finishes the shared mission.");
        Check(!TacticalFieldCoordinationPolicy.Receive(ref receiver,offer,"A","B",group,true,230), "A finished order cannot be replayed into a second sortie.");
        Check(!TacticalFieldCoordinationPolicy.Receive(ref receiver,sender.Copy(),"A","B",group,true,2520), "Expired commitment never grants movement.");
        group.Sectors["B"].Flank = origin + IntVec3.West * 20;
        Check(TacticalFieldCoordinationPolicy.Assignee(group,"A",240) == "C", "A squad holding a flank is not repeatedly assigned the same sortie.");
        Check(new TacticalFieldResponse { Maneuver = new TacticalFieldManeuver { WholeSquad = true } }.FlankWidth > TacticalFieldDoctrine.LowFlankRadius,
            "A whole squad requires a larger spaced cover footprint than a three-person party.");
        offer.Supporter="A"; offer.FinishAcknowledged=true;
        RoundTrip(group,offer,Check);
        Console.WriteLine("PASS: " + checks + " W2 report-only assignment, acceptance/commitment, replay, expiry and native persistence checks.");
    }
    private static void RoundTrip(TacticalFieldFormation formation,TacticalFieldMission mission,Action<bool,string> check)
    {
        var cache = (System.Collections.IDictionary)AccessTools.Field(typeof(GenTypes),"typeCache").GetValue(null);
        Type key = typeof(GenTypes).GetNestedType("TypeCacheKey",System.Reflection.BindingFlags.NonPublic);
        foreach (Type type in new[] { typeof(TacticalFieldSector),typeof(TacticalFieldMission),typeof(TacticalFieldFormation) })
            cache[Activator.CreateInstance(key,new object[] { type.FullName,null })] = type;
        string path = Path.Combine(Path.GetTempPath(),"Helodrace-field-order-"+Guid.NewGuid()+".xml");
        bool oldHeader = ScribeMetaHeaderUtility.modListChanged; ScribeMetaHeaderUtility.modListChanged = true;
        try
        {
            var savedFormation = formation; var savedMission = mission;
            Scribe.saver.InitSaving(path,"field-order");
            Scribe_Deep.Look(ref savedFormation,"formation"); Scribe_Deep.Look(ref savedMission,"mission"); Scribe.saver.FinalizeSaving();
            TacticalFieldFormation loadedFormation = null; TacticalFieldMission loadedMission = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loadedFormation,"formation"); Scribe_Deep.Look(ref loadedMission,"mission"); Scribe.loader.ForceStop();
            check(loadedMission.Matches(mission) && loadedMission.Stage == mission.Stage,"Native save must retain exact commitment identity and stage.");
            check(loadedMission.Supporter=="A" && loadedMission.FinishAcknowledged,
                "Native save retains the explicit supporter and completion confirmation.");
            check(loadedFormation.Sectors.Count == formation.Sectors.Count
                && loadedFormation.Sectors["B"].Flank == formation.Sectors["B"].Flank,"Native save must retain received spatial evidence, not live peer objects.");
        }
        finally { Scribe.ForceStop(); ScribeMetaHeaderUtility.modListChanged = oldHeader; if (File.Exists(path)) File.Delete(path); }
    }
}
