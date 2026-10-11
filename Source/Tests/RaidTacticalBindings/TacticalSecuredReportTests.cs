using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactics;
using Verse;

internal static class TacticalSecuredReportTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        IntVec3 goal = new IntVec3(50, 0, 50), opening = new IntVec3(10, 0, 10);
        TacticalSecuredReport Packet(int sequence, int tick, IntVec3 cell) => new TacticalSecuredReport {
            Origin = "source", Sequence = sequence, SeenTick = tick, Goal = goal, Opening = opening,
            Inward = IntVec3.East, PortalUsable = true, Cells = new List<IntVec3> { cell },
            Frontiers = new List<TacticalRoomFrontier> { new TacticalRoomFrontier { Opening = new IntVec3(20, 0, 10), Inward = IntVec3.East } } };
        var report = Packet(1, 100, goal); var knowledge = new TacticalSecuredKnowledge();
        Check(knowledge.Receive(report, "source", goal, 150) && knowledge.Cleared(goal) && knowledge.Own.Count == 0,
            "Received clearance must remain detached from physical/local entry and must not auto-relay.");
        report.Cells.Clear(); report.Frontiers[0].Opening = IntVec3.Zero;
        Check(knowledge.Cleared(goal) && knowledge.Received[0].Frontiers[0].Opening != IntVec3.Zero,
            "A sender must not mutate a received floor or boundary snapshot.");
        Check(!knowledge.Receive(Packet(1, 100, goal), "source", goal, 150)
            && !knowledge.Receive(Packet(2, 200, goal), "source", goal, 150)
            && !knowledge.Receive(Packet(2, 100, goal), "different", goal, 150)
            && !knowledge.Receive(Packet(2, 100, goal), "source", goal + IntVec3.North, 150),
            "Duplicate, future, forged-origin and obsolete-mission reports must be rejected.");
        var oversized = Packet(2, 100, goal); oversized.Cells = Enumerable.Repeat(goal, 17).ToList();
        Check(!knowledge.Receive(oversized, "source", goal, 150), "Floor packet size must be bounded before indexing.");
        knowledge.Receive(Packet(2, 110, opening + IntVec3.East), "source", goal, 150);
        knowledge.ObserveThreat(goal, goal, 200);
        Check(!knowledge.Cleared(goal) && !knowledge.Cleared(opening + IntVec3.East)
            && knowledge.Received.All(r => r.Recheck && r.PortalUsable),
            "Reinfiltration invalidates that room's clear claim while keeping its physical portal history.");
        Check(knowledge.Receive(Packet(3, 150, goal), "source", goal, 210) && !knowledge.Cleared(goal),
            "A delayed old floor report must not undo a newer enemy observation.");
        Check(knowledge.Receive(Packet(4, 220, goal), "source", goal, 230) && knowledge.Cleared(goal),
            "A genuinely newer clear report can restore its own reported cells.");
        for (int i = 5; i < 80; i++) knowledge.Receive(Packet(i, 230, new IntVec3(i, 0, 5)), "source", goal, 240);
        Check(knowledge.Received.Count == 64 && !knowledge.Cleared(goal) && knowledge.Cleared(new IntVec3(79, 0, 5)),
            "Evicted report cells must leave the index, keeping memory and lookup work bounded.");
        knowledge.Record(Packet(0, 300, goal));
        Check(knowledge.Sequence == 1 && knowledge.Own.Count == 1, "Only a local recorded clear result receives an outgoing revision.");
        knowledge.ObserveThreat(goal, goal, 310);
        Check(knowledge.Own[0].Recheck, "A known local reinfiltration must revoke unsent safe claims.");
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Secured-" + Guid.NewGuid() + ".xml");
        // The console harness has no running Unity mod registry/meta header.
        // Seed only the native resolver's type entries; data extraction and
        // nested ExposeData still use the installed game's actual Scribe.
        var cache = (IDictionary)AccessTools.Field(typeof(GenTypes), "typeCache").GetValue(null);
        var keyType = typeof(GenTypes).GetNestedType("TypeCacheKey", System.Reflection.BindingFlags.NonPublic);
        foreach (Type type in new[] { typeof(TacticalSecuredReport), typeof(TacticalRoomFrontier) })
            cache[Activator.CreateInstance(keyType, new object[] { type.FullName, null })] = type;
        bool previousHeader = ScribeMetaHeaderUtility.modListChanged;
        ScribeMetaHeaderUtility.modListChanged = true;
        try
        {
            Scribe.saver.InitSaving(path, "secured"); knowledge.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalSecuredKnowledge();
            Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            Check(loaded.Sequence == 1 && loaded.Own.Count == 1 && loaded.Own[0].Recheck
                && loaded.Received.Count == 64 && !loaded.Cleared(new IntVec3(79, 0, 5)) && !loaded.Cleared(goal)
                && loaded.Received.All(r => r.Recheck && r.ThreatAt == 310),
                "Native Scribe must preserve separate histories and rebuild the bounded clearance index correctly.");
        }
        finally { ScribeMetaHeaderUtility.modListChanged = previousHeader; if (File.Exists(path)) File.Delete(path); }
        var doctrine = (DoctrineDef)RuntimeHelpers.GetUninitializedObject(typeof(DoctrineDef)); doctrine.voiceReportTicks = 40;
        var group = new CombatGroup { id = "source" };
        var organization = new CombatOrganization { id = "org", doctrine = doctrine, rootGroups = new List<CombatGroup> { group } };
        organization.RestoreTreeLinks();
        var from = new TacticalSquadCommand { Id = "source", Goal = goal }; from.Link.Unit = RaidTacticalUnit.ForGroup(group);
        from.Link.Liaison = new Pawn { thingIDNumber = 101 }; from.SecuredKnowledge.Record(Packet(0, 300, goal));
        var to = new TacticalSquadCommand { Id = "receiver", Goal = goal }; to.Link.Liaison = new Pawn { thingIDNumber = 102 };
        var network = new TacticalCommunications();
        var send = AccessTools.Method(typeof(TacticalCommunications), "SendSecured");
        void Send() => send.Invoke(network, new object[] { from, to, 310, TacticalChannel.Voice });
        Send(); Send();
        var queued = (List<TacticalMessage>)AccessTools.Field(typeof(TacticalCommunications), "pending").GetValue(network);
        Check(network.PendingCount == 1 && queued[0].Due == 350 && queued[0].SecuredReport != from.SecuredKnowledge.Own[0],
            "Clear reports share the existing delayed/coalesced queue and detach their small payload.");
        queued.Clear(); from.Link.SecuredAcks[to.Id] = 1; Send();
        Check(network.PendingCount == 0, "An acknowledged revision must not be sent again each communication frame.");
        from.SecuredKnowledge.Record(Packet(0, 320, opening)); Send();
        Check(network.PendingCount == 1 && queued[0].SecuredReport.Sequence == 2,
            "A new local change must send only the next unacknowledged revision.");
        var receiver = new TacticalSquadCommand();
        var owner = new MapComponent_TacticalCommands(null);
        AccessTools.Method(typeof(MapComponent_TacticalCommands), "ReceiveSecuredReport").Invoke(owner, new object[] { receiver, queued[0].SecuredReport });
        Check(receiver.SecuredCells.Count == 0 && receiver.SecuredKnowledge.Own.Count == 0
            && receiver.Link.KnownPortals.Contains(opening) && receiver.Frontiers.Count == 1,
            "Report application must expose a reusable portal/known frontier without copying a peer's secured state.");
        Console.WriteLine("PASS: " + checks + " secured report isolation, reinfiltration, eviction, native save and change-only packet checks.");
    }
}
