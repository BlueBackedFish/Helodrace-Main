using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalSecuredReportTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        IntVec3 goal = new IntVec3(50,0,50), opening = new IntVec3(10,0,10);
        TacticalSecuredReport Packet(int tick, IntVec3 cell, IntVec3? mouth = null) => new TacticalSecuredReport {
            Origin = "low", SeenTick = tick, Goal = goal, Opening = mouth ?? opening,
            Inward = IntVec3.East, PortalUsable = true, Cells = new List<IntVec3> { cell },
            Frontiers = new List<TacticalRoomFrontier> { new TacticalRoomFrontier { Opening = new IntVec3(20,0,10), Inward = IntVec3.East } } };
        var owner = new MapComponent_TacticalCommands(null);
        var low = new TacticalSquadCommand { Id = "low", Owner = owner, Goal = goal };
        var high = new TacticalSquadCommand { Id = "high", Owner = owner, Goal = goal + IntVec3.North };
        var otherMap = new TacticalSquadCommand { Owner = new MapComponent_TacticalCommands(null) };
        Check(ReferenceEquals(low.SecuredKnowledge, high.SecuredKnowledge)
            && !ReferenceEquals(low.SecuredKnowledge, otherMap.SecuredKnowledge), "All map squads share one clearance ledger, independent of doctrine/contact/goal, while another map remains isolated.");
        var report = Packet(100,goal); var knowledge = owner.SharedSecured;
        Check(knowledge.Record(report) && high.SecuredKnowledge.Cleared(goal) && high.SecuredCells.Count == 0
            && !high.GoalSecured && high.Link.Liaison == null, "A clear is immediately visible without a liaison, radio, voice or fake physical entry.");
        report.Cells.Clear(); report.Frontiers[0].Opening = IntVec3.Zero;
        Check(knowledge.Cleared(goal) && knowledge.Records[0].Frontiers[0].Opening != IntVec3.Zero,
            "Published evidence must detach its small value payload from the scanner.");
        low.Contacts.Memory.Remember(5,goal,goal,false,true,100,"low");
        Check(high.Contacts.Memory.Entries.Count == 0 && new TacticalCommunications().PendingCount == 0,
            "Sharing clear geometry must not share enemy contacts or generate radio packets.");
        var sync = AccessTools.Method(typeof(MapComponent_TacticalCommands), "SyncSharedSecured");
        sync.Invoke(owner,new object[]{high,101});
        Check(high.Frontiers.Count == 1 && high.Frontiers[0].Approach && high.SecuredKnowledge.KnownOpening(opening)
            && high.SecuredCells.Count == 0, "Shared frontier hints enter the existing planner without fabricating local room survey/entry.");
        sync.Invoke(owner,new object[]{high,102});
        Check(high.Frontiers.Count == 1, "The revision cursor must suppress duplicate imports.");
        knowledge.Record(Packet(110, opening + IntVec3.East));
        IntVec3 otherOpening = opening + IntVec3.North * 10, unrelated = goal + IntVec3.East;
        knowledge.Record(Packet(120, unrelated,otherOpening));
        knowledge.ObserveThreat(goal,goal,200);
        Check(!high.SecuredKnowledge.Cleared(goal) && !high.SecuredKnowledge.Cleared(opening + IntVec3.East)
            && knowledge.Cleared(unrelated) && knowledge.KnownPassage(opening,IntVec3.East,1),
            "Indexed reinfiltration revokes all batches of the affected room across squads, preserves other rooms and remembers the usable passage.");
        int invalidatedRevision = knowledge.Sequence;
        knowledge.ObserveThreat(goal,goal,205);
        Check(knowledge.Sequence == invalidatedRevision && knowledge.Records[0].ThreatAt == 205,
            "Repeated observations in an already uncertain room update threat age without restarting every squad's structural import/cache.");
        knowledge.Record(Packet(150,goal));
        Check(!knowledge.Cleared(goal), "An older clear batch may not undo newer observed reinfiltration.");
        knowledge.Record(Packet(220,goal));
        Check(knowledge.Cleared(goal), "A newer completed clear can restore that room's cells.");
        knowledge.ObserveThreat(goal,goal,210);
        Check(knowledge.Cleared(goal), "Delayed enemy observations must not revoke a newer actual clear.");
        var oversized = Packet(250,goal); oversized.Cells = Enumerable.Repeat(goal,17).ToList();
        int revision = knowledge.Sequence;
        Check(!knowledge.Record(oversized) && knowledge.Sequence == revision, "The bounded floor payload must be checked before indexing.");
        var invalid = Packet(250,goal); invalid.Depth = TacticalPortalGeometry.LayerLimit + 1;
        Check(!knowledge.Record(invalid), "Impossible channel depths must not enter the common ledger.");
        // Force multiple stale/fresh overlapping batches out of the history.
        for(int i=0;i<TacticalSecuredReport.HistoryLimit+5;i++)
            knowledge.Record(Packet(300,new IntVec3(i,0,5),new IntVec3(i,0,6)));
        Check(knowledge.Records.Count == TacticalSecuredReport.HistoryLimit && !knowledge.Cleared(goal)
            && knowledge.Cleared(new IntVec3(TacticalSecuredReport.HistoryLimit+4,0,5)),
            "Eviction must remove cell/opening index membership and keep shared memory/work bounded.");
        Check(!knowledge.KnownOpening(opening), "Evicted opening evidence must not leave a stale index entry.");
        var bounded = new MapComponent_TacticalCommands(null);
        var follower = new TacticalSquadCommand { Id="follower", Owner=bounded, Phase=TacticalCommandPhase.Complete };
        for(int i=0;i<5;i++)
        {
            var batch=Packet(400,new IntVec3(40+i,0,4),new IntVec3(40+i,0,5));
            batch.Frontiers[0].Opening = new IntVec3(40+i,0,6);
            bounded.SharedSecured.Record(batch);
        }
        sync.Invoke(bounded,new object[]{follower,401});
        Check(follower.SharedSecuredCursor==2 && follower.Frontiers.Count==2 && follower.Phase==TacticalCommandPhase.Clear,
            "A shared revision imports at most two metadata batches per advance and resumes a completed squad only for new work.");
        sync.Invoke(bounded,new object[]{follower,402}); sync.Invoke(bounded,new object[]{follower,403});
        Check(follower.SharedSecuredCursor==bounded.SharedSecured.Sequence && follower.Frontiers.Count==5,
            "Bounded imports must eventually consume the revision without a permanent partial cursor.");
        CheckNativeSave(owner,goal,opening,Check);
        owner.MapRemoved();
        Check(owner.SharedSecured.Records.Count==0 && !low.SecuredKnowledge.Cleared(goal),
            "Map cleanup must discard the common evidence without leaving a ticking service or a stale ledger for future raids.");
        Check(TacticalSharedPassagePolicy.Reconsider(TacticalCommandPhase.Breach,true,false,false,false)
            && TacticalSharedPassagePolicy.Reconsider(TacticalCommandPhase.Support,true,false,false,false)
            && !TacticalSharedPassagePolicy.Reconsider(TacticalCommandPhase.Breach,true,false,true,false)
            && !TacticalSharedPassagePolicy.Reconsider(TacticalCommandPhase.Support,true,false,false,true)
            && !TacticalSharedPassagePolicy.Reconsider(TacticalCommandPhase.Enter,true,false,false,false)
            && !TacticalSharedPassagePolicy.Reconsider(TacticalCommandPhase.Stack,false,false,false,false),
            "An already selected redundant wall/preparation may replan, while C4, launched grenades and committed entry retain execution ownership.");
        Check(AccessTools.Method(typeof(TacticalCommunications),"SendSecured")==null
            && typeof(TacticalMessage).GetField("SecuredReport")==null && !Enum.GetNames(typeof(TacticalMessageKind)).Contains("Secured"),
            "Removed clearance transport/acks must not remain as parallel legacy paths.");
        Console.WriteLine("PASS: " + checks + " common clearance, doctrine/contact independence, enemy isolation, indexed recheck, bounded import/eviction and native map-save checks.");
    }
    private static void CheckNativeSave(MapComponent_TacticalCommands owner, IntVec3 goal, IntVec3 opening, Action<bool,string> check)
    {
        owner.SharedSecured.Record(new TacticalSecuredReport { Origin="low", SeenTick=500, Opening=opening, Inward=IntVec3.East,
            Depth=2, PortalUsable=true, Cells=new List<IntVec3>{goal},
            Frontiers=new List<TacticalRoomFrontier>{new TacticalRoomFrontier{Opening=opening+IntVec3.East*4,Inward=IntVec3.East,Depth=3}} });
        owner.SharedSecured.ObserveThreat(goal,goal,510);
        var cache=(IDictionary)AccessTools.Field(typeof(GenTypes),"typeCache").GetValue(null);
        Type keyType=typeof(GenTypes).GetNestedType("TypeCacheKey",System.Reflection.BindingFlags.NonPublic);
        foreach(Type type in new[]{typeof(TacticalSecuredKnowledge),typeof(TacticalSecuredReport),typeof(TacticalRoomFrontier)})
            cache[Activator.CreateInstance(keyType,new object[]{type.FullName,null})]=type;
        bool previousHeader=ScribeMetaHeaderUtility.modListChanged; ScribeMetaHeaderUtility.modListChanged=true;
        string path=Path.Combine(Path.GetTempPath(),"Helodrace-shared-clear-"+Guid.NewGuid()+".xml");
        try
        {
            Scribe.saver.InitSaving(path,"map"); owner.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded=new MapComponent_TacticalCommands(null);
            Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            var a=new TacticalSquadCommand{Owner=loaded}; var b=new TacticalSquadCommand{Owner=loaded};
            check(ReferenceEquals(a.SecuredKnowledge,b.SecuredKnowledge) && !a.SecuredKnowledge.Cleared(goal)
                && a.SecuredKnowledge.Records.Last().Recheck && a.SecuredKnowledge.KnownPassage(opening,IntVec3.East,2),
                "Native map-level Scribe restores one common ledger and rebuilds its clear/portal indexes.");
            check(a.SecuredKnowledge.Records.Last().Frontiers[0].Depth==3 && loaded.SharedSecured.Sequence==owner.SharedSecured.Sequence,
                "Native deep save preserves thick channel/frontier geometry and the common revision.");
            check(System.Xml.Linq.XDocument.Load(path).Root.Elements("tacticalSharedSecured").Count()==1,
                "Map must save the common ledger exactly once, outside per-squad payloads.");
        }
        finally { Scribe.ForceStop(); ScribeMetaHeaderUtility.modListChanged=previousHeader; if(File.Exists(path))File.Delete(path); }
    }
}
