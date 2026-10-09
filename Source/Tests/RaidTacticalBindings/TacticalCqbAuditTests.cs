using System;
using System.Collections.Generic;
using System.Linq;
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
        Console.WriteLine("PASS: actual agreed-area and joint-room CQB audit rejects incomplete, inconsistent, duplicate and unallocated progress.");
    }
}
