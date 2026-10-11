using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalMissionRoutingTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var entry = new TacticalLocalPlan { Opening = new IntVec3(10, 0, 10), Inward = IntVec3.East, Depth = 2 };
        var command = new TacticalSquadCommand { Plan = entry, Goal = new IntVec3(15, 0, 10), GoalSecured = true };
        bool Floor(IntVec3 c) => c.x >= 12 && c.x <= 15 && c.z >= 9 && c.z <= 11;
        var scan = new TacticalRoomScan(entry.Inside, entry.InnerOpening, Floor, _ => false);
        while (!scan.Step()) { }
        while (!scan.CopyCellsTo(command.SecuredCells)) { }
        command.SecuredPlans.Add(entry); command.Mission.Traversed.Add(entry.Opening);
        Check(TacticalMissionRouting.CompletedChannel(command, entry.Opening, entry.Inward)
            && TacticalMissionRouting.CompletedChannel(command, entry.InnerOpening, entry.Inward),
            "Both layers of a physically traversed channel must be rejected as another breach.");
        Check(!TacticalMissionRouting.CompletedChannel(command, entry.Inside, entry.Inward)
            && !TacticalMissionRouting.CompletedChannel(command, entry.Opening + IntVec3.North, entry.Inward),
            "Physical history must not invent traversal of a further room or adjacent unentered wall.");
        command.SecuredCells.Clear();
        Check(TacticalMissionRouting.CompletedChannel(command, entry.InnerOpening, entry.Inward),
            "A roof loss or empty floor survey must not erase a completed layered passage.");
        command.SecuredCells.UnionWith(scan.Cells);
        var exit = new TacticalRoomFrontier { Opening = entry.InnerOpening, Inward = -entry.Inward, Depth = 2, Outdoors = true };
        var closedExit = new TacticalRoomFrontier { Opening = new IntVec3(16, 0, 10), Inward = IntVec3.East, Outdoors = true };
        var reportedExit = new TacticalRoomFrontier { Opening = new IntVec3(17, 0, 10), Inward = IntVec3.East, Outdoors = true, Approach = true };
        command.Frontiers.AddRange(new[] { exit, closedExit, reportedExit });
        var service = new MapComponent_TacticalCommands(null);
        IntVec3 Pending() => (IntVec3)AccessTools.Method(service.GetType(), "PendingGoal").Invoke(null, new object[] { command });
        Check(!command.Frontiers.Exists(f => TacticalMissionRouting.UnenteredRoom(command, f)),
            "Outdoor wall tiles and reported exits must not keep a fully secured building unresolved.");
        Check(!Pending().IsValid, "Outdoor frontiers must not restart the secured bed objective.");
        Check(TacticalMissionRouting.ExitForDestination(command, command.Goal, _ => true) == -1,
            "A building with no latched next destination must not select an outdoor exit.");
        object[] args = { command, new List<TacticalMemberCommand>(), exit, false, false };
        Check(AccessTools.Method(service.GetType(), "PlanRoomFrontier").Invoke(service, args) == null,
            "Unrequested outdoor breach must be rejected by the actual planner before geometry or reach checks.");
        Check(!TacticalMissionRouting.FacadeDestination(command, closedExit.Opening, closedExit.Inside, false),
            "An owned wall facing outdoor floor is not a next-building destination.");
        Check(!TacticalMissionRouting.FacadeDestination(command, new IntVec3(16, 0, 11), command.Goal, true),
            "A different wall into the secured room must not reopen that room's mission.");
        var room = new TacticalRoomFrontier { Opening = new IntVec3(16, 0, 11), Inward = IntVec3.East };
        command.Frontiers.Add(room);
        Check(TacticalMissionRouting.UnenteredRoom(command, room) && Pending() == room.Inside,
            "An actual unentered adjacent room must remain a mission target.");
        command.Mission.Traversed.Add(room.Opening);
        Check(!TacticalMissionRouting.UnenteredRoom(command, room) && !Pending().IsValid,
            "A traversed room boundary must not send a completed command back indoors.");

        var next = new IntVec3(25, 0, 10);
        Check(TacticalMissionRouting.FacadeDestination(command, new IntVec3(24, 0, 10), next, true),
            "A roofed unknown next building must still be eligible after the bed is secured.");
        command.Mission.NextGoal = next; command.Mission.Stage = TacticalMissionStage.Moving;
        Check(TacticalMissionRouting.ExitForDestination(command, next, f => f == exit) == 0,
            "The known two-layer open entry must be reused instead of a closer closed external wall.");
        Check(TacticalMissionRouting.ExitForDestination(command, next, _ => false) == -1,
            "A closed or obstructed passage must not be selected as open transit.");
        Check(TacticalMissionRouting.TransitGoal(command) == next && Pending() == next,
            "Passing outdoors must preserve the separate next-building destination.");
        command.Mission.FailedDestinations.Add(next);
        Check(!TacticalMissionRouting.FacadeDestination(command, new IntVec3(24, 0, 10), next, true),
            "A recently failed building must not be immediately rediscovered by another facade survey.");

        string path = Path.Combine(Path.GetTempPath(), "Helodrace-MissionTransit-" + Guid.NewGuid() + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "mission"); command.Mission.ExposeData(); Scribe.saver.FinalizeSaving();
            var restored = new TacticalAssaultMission();
            Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.ForceStop();
            command.Mission = restored;
            Check(command.Mission.Stage == TacticalMissionStage.Moving && TacticalMissionRouting.TransitGoal(command) == next,
                "Native save/load between exit and entry must retain the next building, not restore the old bed.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        var member = new TacticalMemberCommand { Passed = true, Crossed = true, Entered = true, EntryAssignmentDone = true };
        command.Members.Add(member);
        AccessTools.Method(service.GetType(), "SelectMissionDestination").Invoke(service, new object[] { command, next, 900, false });
        Check(command.Phase == TacticalCommandPhase.Pending && command.Plan == null && command.Due == 901 && Pending() == next
            && !member.Passed && !member.Crossed && !member.Entered && command.SecuredCells.Contains(command.Goal),
            "Resuming real mission movement must reset entry orders while retaining the destination and secured room history.");
        command.SecuredCells.Add(next);
        Check(!TacticalMissionRouting.TransitGoal(command).IsValid && !Pending().IsValid,
            "A next destination already entered by this squad must not start another transit.");
        command.SecuredCells.Remove(next);
        var packet = new TacticalSecuredReport { Origin = "peer", Sequence = 1, SeenTick = 1000,
            Goal = command.Goal, Opening = new IntVec3(24, 0, 10), Inward = IntVec3.East, Cells = new List<IntVec3> { next } };
        Check(command.SecuredKnowledge.Receive(packet, "peer", command.Goal, 1001)
            && !TacticalMissionRouting.TransitGoal(command).IsValid && !Pending().IsValid,
            "Received clearance must also stop a now-unnecessary transit.");
        Console.WriteLine("PASS: " + checks + " actual-room survey, outdoor-loop prevention, open-exit reuse, mission resumption and native transit-save checks.");
    }
}
