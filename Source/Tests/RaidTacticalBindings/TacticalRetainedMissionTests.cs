using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalRetainedMissionTests
{
    internal static void Run()
    {
        var service = new MapComponent_TacticalCommands(null);
        var command = new TacticalSquadCommand { Id = "same", Owner = service, Phase = TacticalCommandPhase.Returning,
            ReplanAfterReturn = true, Goal = new IntVec3(20,0,10), Plan = new TacticalLocalPlan { Opening = new IntVec3(10,0,10), Inward = IntVec3.East },
            GoalSecured = true, ContactRestoring = true };
        var first = command.Plan; command.SecuredPlans.Add(first); command.SecuredCells.Add(first.Inside);
        command.SecuredCells.Add(command.Goal);
        var frontier = new TacticalRoomFrontier { Opening = new IntVec3(25,0,10), Inward = IntVec3.East };
        command.Frontiers.Add(frontier); command.FrontierKeys.Add(frontier.Opening);
        var member = new TacticalMemberCommand { Pawn = new Pawn(), Passed = true, Crossed = true, Entered = true,
            EverEntered = true, EntryAssignmentDone = true, Parking = new IntVec3(12,0,10) };
        command.Members.Add(member);
        var members = command.Members; var secured = command.SecuredCells; var plans = command.SecuredPlans;
        var frontiers = command.Frontiers; var contacts = command.Contacts; var link = command.Link;
        AccessTools.Method(service.GetType(), "RestartRetainedMission").Invoke(null, new object[] { command, 300 });
        if (command.Terminal || command.Phase != TacticalCommandPhase.Pending || command.ReplanAfterReturn || command.ReleaseAfterReturn
            || command.Due != 540 || command.PlanRetryAt != 540 || command.PhaseStarted != 300
            || command.Plan != null || command.ContactRestoring || !command.GoalSecured
            || command.Members != members || command.SecuredCells != secured || command.SecuredPlans != plans
            || command.Frontiers != frontiers || command.Contacts != contacts || command.Link != link
            || plans.Count != 1 || plans[0] != first || !secured.Contains(first.Inside) || !command.FrontierKeys.Contains(frontier.Opening)
            || member.Passed || member.Crossed || member.Entered || member.EntryAssignmentDone || !member.EverEntered || member.Parking.IsValid)
            throw new Exception("Geometric retry must keep the same command/mission knowledge and previous ingress evidence, not terminally retire it.");
        var target = AccessTools.Method(service.GetType(), "PendingGoal");
        IntVec3 Target() => (IntVec3)target.Invoke(null, new object[] { command });
        if (Target() != frontier.Inside) throw new Exception("An already secured goal must continue to a known unentered frontier.");
        command.GoalSecured = false;
        if (Target() != command.Goal) throw new Exception("The original unentered objective remains first priority.");
        command.GoalSecured = true; command.SecuredCells.Add(frontier.Inside);
        if (Target() != command.Goal) throw new Exception("A cleared frontier must not become another replan target.");
        var known = (List<TacticalLocalPlan>)AccessTools.Field(service.GetType(), "knownOpenings").GetValue(service);
        known.Add(first);
        // A known secured portal must be rejected before any geometry/reach
        // access. The null map makes accidental old-room reuse observable.
        object[] args = { command, new Pawn(), command.Goal, 12, (Func<IntVec3,bool>)(_ => false), false, TacticalPlanFailure.None };
        object plan = AccessTools.Method(service.GetType(), "ReuseOpening").Invoke(service, args);
        if (plan != null || (bool)args[5] || (TacticalPlanFailure)args[6] != TacticalPlanFailure.None)
            throw new Exception("Already secured portal must not be retried as another room entry.");
        var abandon = AccessTools.Method(service.GetType(), "MayAbandonPendingPlan");
        bool Abandon(bool tool, bool agreed, int failures, int leases) => (bool)abandon.Invoke(null,
            new object[] { tool, agreed, failures, leases });
        foreach (int failures in new[] { 0, 3, 4, 20, 100 })
            if (Abandon(false, true, failures, 0) || Abandon(true, true, failures, 0))
                throw new Exception("A live shared-entry agreement must retain its original command until coordination expires it.");
        if (!Abandon(false, false, 0, 0) || !Abandon(true, false, 4, 0)
            || Abandon(true, false, 3, 0) || Abandon(false, false, 20, 1))
            throw new Exception("Ordinary/expired impossible plans retain the existing release threshold and occupied-portal wait.");
        Console.WriteLine("PASS: retained geometric retry preserves secured/observed mission identity, continues unknown frontiers and skips already secured portal reuse.");
        Console.WriteLine("PASS: shared-entry retries preserve active allocation and original command; expired/ordinary impossible plans retain bounded release policy.");
    }
}
