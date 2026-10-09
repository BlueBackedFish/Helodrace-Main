using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Tactics;
using Verse;

internal static class TacticalCqbAuditTests
{
    internal static void Run()
    {
        IntVec3 goal = new IntVec3(120,0,116);
        var regions = new[] { new IntVec3(108,0,110), new IntVec3(120,0,110), new IntVec3(120,0,128) };
        var a = new TacticalSquadCommand { Id = "A", Phase = TacticalCommandPhase.Complete, Goal = goal, GoalSecured = true };
        var b = new TacticalSquadCommand { Id = "B", Phase = TacticalCommandPhase.Complete, Goal = goal };
        var agenda = new TacticalCooperationAgenda("AB", "A", "B", goal, IntVec3.East, 60, 12000);
        foreach (var command in new[] { a, b })
        {
            command.Link.Cooperation.Agenda = agenda; command.Link.Cooperation.Stage = TacticalAgreementStage.Finished;
            command.SecuredCells.Add(agenda.Area(command.Id));
            command.SecuredPlans.Add(new TacticalLocalPlan { Opening = new IntVec3(100,0,command == a ? 104 : 128) });
        }
        a.SecuredCells.UnionWith(new[] { goal, regions[0], regions[1] });
        var method = AccessTools.Method(typeof(MapComponent_TacticalEngineAudit), "TimedCqbMissionCoverage");
        bool Covered(params TacticalSquadCommand[] commands) => (bool)method.Invoke(null, new object[] { commands, regions });
        void Reject(string context) { if (Covered(a,b)) throw new Exception("CQB audit accepted " + context); }
        if (!Covered(a,b) || TacticalRoomProgress.CoversGoal(b, regions))
            throw new Exception("An actually completed agreed area and joint room/bed coverage must not require every squad to duplicate all rooms.");
        b.SecuredCells.Remove(agenda.Area(b.Id)); Reject("unsecured assigned room");
        a.SecuredCells.Add(regions[2]); Reject("another squad substituting for unfinished assigned work");
        a.SecuredCells.Remove(regions[2]); b.SecuredCells.Add(agenda.Area(b.Id));
        b.Phase = TacticalCommandPhase.Enter; Reject("live ingress"); b.Phase = TacticalCommandPhase.Complete;
        b.Link.Cooperation.Stage = TacticalAgreementStage.Offered; Reject("unconfirmed cooperation");
        b.Link.Cooperation.Stage = TacticalAgreementStage.Finished;
        b.Link.Cooperation.Agenda = new TacticalCooperationAgenda("AB", "A", "B", goal, IntVec3.North, 60, 12000);
        Reject("different peer allocation"); b.Link.Cooperation.Agenda = agenda;
        a.GoalSecured = false; Reject("missing bed goal"); a.GoalSecured = true;
        a.SecuredCells.Remove(goal); Reject("goal flag without actual surveyed floor"); a.SecuredCells.Add(goal);
        b.SecuredPlans.Add(b.SecuredPlans[0]); Reject("repeated opening history"); b.SecuredPlans.RemoveAt(1);
        b.Link.Cooperation.Stage = TacticalAgreementStage.None; Reject("unallocated incomplete independent mission");
        b.Link.Cooperation.Stage = TacticalAgreementStage.Finished;
        if (Covered(a) || Covered(a,b,b) || !Covered(a,b)) throw new Exception("Missing/duplicated squads must not pass collective coverage.");
        a.Link.Peer = b; b.Link.Peer = a;
        foreach (var command in new[] { a,b })
        {
            var pawn = new Pawn();
            pawn.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
            command.Members.Add(new TacticalMemberCommand {
                Pawn = pawn, EntryAssignmentDone = true, Passed = true, Crossed = true, EverEntered = true });
        }
        var readyMethod = AccessTools.Method(typeof(MapComponent_TacticalEngineAudit), "CooperationCompletionReady");
        bool Ready(int tick = 500) => (bool)readyMethod.Invoke(null, new object[] { new[] { a,b }, regions, tick });
        if (!Ready()) throw new Exception("Completed allocations with actual joint room/bed/entry coverage must record a milestone.");
        b.Members[0].EntryAssignmentDone = false;
        if (Ready()) throw new Exception("Unfinished rear assignment must not be captured as cooperative completion.");
        b.Members[0].EntryAssignmentDone = true;
        a.GoalSecured = false;
        if (Ready()) throw new Exception("Allocated rooms without the actual bed objective must not establish a mission milestone.");
        a.GoalSecured = true;
        b.Link.Peer = null;
        if (Ready()) throw new Exception("Missing actual peer binding must not establish cooperation history.");
        b.Link.Peer = a;
        if (Ready(12000)) throw new Exception("A completion first observed after expiry is not pre-deadline cooperation proof.");
        var signature = AccessTools.Method(typeof(MapComponent_TacticalEngineAudit), "Agenda");
        string agendaSignature = (string)signature.Invoke(null, new object[] { agenda });
        var evidence = new TacticalEngineAuditResult { newCooperationCompletedTick = 100,
            newCooperationCompletionAgendas = new[] { agendaSignature, agendaSignature },
            newCooperationCompletionStates = new[] { "A:Finished actual test state", "B:Finished actual test state" } };
        var preservedMethod = AccessTools.Method(typeof(MapComponent_TacticalEngineAudit), "CooperationCompletionPreserved");
        bool Preserved(int tick = 12500) => (bool)preservedMethod.Invoke(null,
            new object[] { evidence, new[] { a,b }, regions, 400, tick });
        a.Link.Cooperation.Stage = b.Link.Cooperation.Stage = TacticalAgreementStage.Aborted;
        if (!Preserved() || Preserved(11000)) throw new Exception("Recorded actual completion survives later expiry, never an early abort.");
        evidence.newCooperationCompletedTick = -1;
        if (Preserved()) throw new Exception("Final Aborted states without an actual earlier completion cannot pass.");
        evidence.newCooperationCompletedTick = 100;
        b.Link.Cooperation.Agenda = new TacticalCooperationAgenda("replacement", "A", "B", goal, IntVec3.East, 60, 12000);
        if (Preserved()) throw new Exception("History for an older agreement cannot cover a replacement mission.");
        b.Link.Cooperation.Agenda = agenda;
        a.SecuredCells.Remove(goal);
        if (Preserved()) throw new Exception("Final loss of actual objective coverage invalidates completion history.");
        a.SecuredCells.Add(goal);
        b.Phase = TacticalCommandPhase.Clear;
        if (Preserved()) throw new Exception("Active recovery must not pass final completed-cooperation proof.");
        b.Phase = TacticalCommandPhase.Complete;
        if (!Preserved()) throw new Exception("Restored retained coverage must preserve the independently proven milestone.");
        Console.WriteLine("PASS: actual agreed-area and joint-room CQB audit rejects incomplete, inconsistent, duplicate and unallocated progress.");
        Console.WriteLine("PASS: pre-deadline cooperation milestone requires actual entries/rooms/bed/peer binding; later recovery never substitutes for missing or different completion proof.");
    }
}
