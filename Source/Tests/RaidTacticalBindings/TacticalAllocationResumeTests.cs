using System;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalAllocationResumeTests
{
    internal static void Run()
    {
        var method = AccessTools.Method(typeof(MapComponent_TacticalCommands), "ResumeExpiredAllocation");
        bool Resume(TacticalSquadCommand command, int tick) => (bool)method.Invoke(null, new object[] { command, tick });
        TacticalSquadCommand Make()
        {
            var c = new TacticalSquadCommand { Id = "a", Phase = TacticalCommandPhase.Complete,
                Goal = new IntVec3(20,0,10), Plan = new TacticalLocalPlan { Opening = new IntVec3(10,0,10) },
                FrontierCursor = 1, FrontierBusy = true, PlanRetryAt = 5000 };
            c.SecuredCells.Add(new IntVec3(11,0,10)); c.SecuredPlans.Add(c.Plan);
            c.Frontiers.Add(new TacticalRoomFrontier { Opening = new IntVec3(19,0,10), Inward = IntVec3.East });
            c.Link.Cooperation.Agenda = new TacticalCooperationAgenda("pair", "a", "b", c.Goal, IntVec3.East, 90, 1000);
            c.Link.Cooperation.Stage = TacticalAgreementStage.Finished;
            c.Members.Add(new TacticalMemberCommand { Passed = true, Crossed = true, Entered = true, EverEntered = true });
            return c;
        }
        var c = Make(); var plan = c.Plan; var cells = c.SecuredCells; var frontiers = c.Frontiers;
        if (Resume(c, 999) || !Resume(c, 1000) || c.Phase != TacticalCommandPhase.Clear
            || c.Link.Cooperation.Stage != TacticalAgreementStage.Aborted || c.FrontierCursor != 0
            || c.FrontierBusy || c.PlanRetryAt != 0 || c.Due != 1001 || c.PhaseStarted != 1000
            || c.Plan != plan || c.SecuredCells != cells || c.Frontiers != frontiers
            || c.SecuredPlans.Count != 1 || !c.Members[0].Passed || !c.Members[0].Crossed || !c.Members[0].EverEntered)
            throw new Exception("Expired unconfirmed allocation must resume the retained room mission without losing entry history.");
        c = Make(); c.Link.Cooperation.Stage = TacticalAgreementStage.Aborted;
        c.Link.Cooperation.PeerFinished = c.Link.Cooperation.PeerGoalSecured = true;
        if (!Resume(c, 200)) throw new Exception("Explicit peer abort must invalidate an older successful report.");
        c = Make(); c.Link.Cooperation.PeerFinished = c.Link.Cooperation.PeerGoalSecured = true;
        if (Resume(c, 1000)) throw new Exception("An actual received peer completion/goal report must preserve completed cooperation.");
        c = Make(); c.Link.Cooperation.PeerFinished = true;
        if (!Resume(c, 1000)) throw new Exception("Two allocated areas without any confirmed goal still need the original mission.");
        c = Make(); c.GoalSecured = true; c.SecuredCells.Add(c.Goal);
        if (Resume(c, 1000)) throw new Exception("Fully secured local mission must not be reopened merely because its deadline expired.");
        c = Make(); c.GoalSecured = true; c.SecuredCells.Add(c.Goal);
        c.Frontiers.Add(new TacticalRoomFrontier { Opening = new IntVec3(25,0,10), Inward = IntVec3.East });
        if (!Resume(c, 1000)) throw new Exception("Unfinished adjacent rooms still require clearing after unconfirmed cooperation expires.");
        TacticalSquadCommand CompletedSide()
        {
            var done = Make(); done.GoalSecured = true;
            done.SecuredCells.Add(done.Goal);
            done.SecuredCells.Add(done.Link.Cooperation.Agenda.Area(done.Id));
            done.Frontiers.Add(new TacticalRoomFrontier { Opening = new IntVec3(25,0,15), Inward = IntVec3.East });
            return done;
        }
        c = CompletedSide();
        if (Resume(c, 1000) || c.Phase != TacticalCommandPhase.Complete
            || c.Link.Cooperation.Stage != TacticalAgreementStage.Finished)
            throw new Exception("Expiry must not reopen completed own area/goal solely for the peer's rooms.");
        c = CompletedSide(); c.GoalSecured = false;
        if (!Resume(c, 1000)) throw new Exception("An unsecured mission goal still requires independent recovery.");
        c = CompletedSide(); c.SecuredCells.Remove(c.Link.Cooperation.Agenda.Area(c.Id));
        if (!Resume(c, 1000)) throw new Exception("An unsecured assigned area cannot be delegated to the peer.");
        c = CompletedSide(); c.Link.Cooperation.Stage = TacticalAgreementStage.Aborted;
        if (!Resume(c, 200)) throw new Exception("Explicit abort must recover peer-side rooms even after local completion.");
        foreach (int z in new[] { 5, 10 })
        {
            c = CompletedSide();
            c.Frontiers.Add(new TacticalRoomFrontier { Opening = new IntVec3(25,0,z), Inward = IntVec3.East });
            if (!Resume(c, 1000)) throw new Exception("Own-side and centre-line unknown rooms still require clearing.");
        }
        foreach (IntVec3 forward in new[] { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South })
        {
            var agenda = new TacticalCooperationAgenda("pair", "a", "b", new IntVec3(20,0,10), forward, 90, 1000);
            foreach (string own in new[] { "a", "b" })
                if (!agenda.OwnsSide(own, agenda.Area(own)) || !agenda.OwnsSide(own, agenda.Goal)
                    || agenda.OwnsSide(own, agenda.Area(agenda.Peer(own))))
                    throw new Exception("Agreed responsibility must rotate with approach and keep the centre line shared.");
        }
        foreach (TacticalCommandPhase phase in new[] { TacticalCommandPhase.Pending, TacticalCommandPhase.Enter,
            TacticalCommandPhase.Clear, TacticalCommandPhase.BlastWait, TacticalCommandPhase.Returning, TacticalCommandPhase.Released })
        { c = Make(); c.Phase = phase; if (Resume(c, 1000)) throw new Exception("Only completed allocations may be resumed: " + phase); }
        c = Make(); c.Defensive = true;
        if (Resume(c, 1000)) throw new Exception("Fixed defense must not become room assault.");
        c = Make(); c.Link.Cooperation.Agenda = null;
        if (Resume(c, 1000)) throw new Exception("An ordinary completed mission has no allocation to resume.");
        Console.WriteLine("PASS: failed/expired unconfirmed allocated completion resumes latched room mission; confirmed peers, full completion, defense and live ingress remain unchanged.");
    }
}
