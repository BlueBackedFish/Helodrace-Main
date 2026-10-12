using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalPlanPreparationTests
{
    internal static void Run()
    {
        int steps = 0; bool disposed = false;
        var result = new TacticalLocalPlan();
        IEnumerable<TacticalLocalPlan> Slices()
        {
            try { steps++; yield return null; steps++; yield return null; steps++; yield return result; }
            finally { disposed = true; }
        }
        var search = new TacticalPlanSearch(); search.Start(Slices());
        if (steps != 0 || search.Step() || steps != 1 || search.Plan != null || search.Step() || steps != 2)
            throw new Exception("Planning must execute one slice per advance, without publishing partial work or eagerly running a factory.");
        if (!search.Step() || search.Plan != result || steps != 3 || !disposed || !search.Step() || steps != 3)
            throw new Exception("A completed plan must publish exactly once and dispose its retained iterator.");
        steps = 0; disposed = false;
        search = new TacticalPlanSearch(); search.Start(Slices()); search.Step(); search.Dispose();
        if (!disposed || !search.Complete || search.Plan != null || !search.Step() || steps != 1)
            throw new Exception("Cancelling suspended work must release closures and never run future slices.");
        var command = new TacticalSquadCommand { Phase = TacticalCommandPhase.Pending };
        var a = new TacticalMemberCommand { Pawn = new Pawn { Position = new IntVec3(1,0,1) }, CombatFunction = "Sniper" };
        var b = new TacticalMemberCommand { Pawn = new Pawn { Position = new IntVec3(2,0,1) }, CombatFunction = "Rifle" };
        var active = new List<TacticalMemberCommand> { a,b }; command.Members.AddRange(active);
        IntVec3 goal = new IntVec3(5,0,5);
        var work = command.Preparation = new TacticalPlanPreparation(command, active, goal);
        if (!work.Matches(command, active, goal) || work.Matches(command, new List<TacticalMemberCommand> {b,a}, goal)
            || work.Matches(command, new List<TacticalMemberCommand> {a}, goal) || work.Matches(command, active, goal+IntVec3.East))
            throw new Exception("Roster loss/reordering and changed mission destinations must invalidate suspended planning.");
        command.Phase = TacticalCommandPhase.Enter;
        if (work.Matches(command, active, goal)) throw new Exception("Interrupted phase changes must not revive a stale planning iterator.");
        command.Phase = TacticalCommandPhase.Pending;
        command.Plan = new TacticalLocalPlan();
        if (work.Matches(command, active, goal)) throw new Exception("A replacement source plan must invalidate old room planning.");
        command.Plan = null;
        command.Link.Cooperation.Agenda = new TacticalCooperationAgenda("new", "a", "b", goal, IntVec3.East, 100, 6000);
        command.Link.Cooperation.Stage = TacticalAgreementStage.Agreed;
        if (work.Matches(command, active, goal)) throw new Exception("A newly confirmed cooperation frontage must invalidate the earlier unilateral plan.");
        command.Link.Cooperation.Stage = TacticalAgreementStage.None;
        var candidate = new TacticalLocalPlan { ReportedPassage = true, Opening = new IntVec3(4,0,4), Inward = IntVec3.East };
        candidate.Stack.AddRange(new[]{new IntVec3(2,0,4),new IntVec3(2,0,5)});
        candidate.Positions.AddRange(new[]{new IntVec3(6,0,4),new IntVec3(6,0,5)});
        AccessTools.Field(work.GetType(), "Candidate").SetValue(work, candidate);
        var stage = AccessTools.Field(work.GetType(), "Stage");
        stage.SetValue(work, Enum.Parse(stage.FieldType, "Allocate"));
        var finish = AccessTools.Method(typeof(MapComponent_TacticalCommands), "FinishPreparation");
        object[] arguments = {command, active, (Func<IntVec3,bool>)(_=>false),null,TacticalPlanFailure.None};
        var allocationOwner = new MapComponent_TacticalCommands(null);
        for (int i=0; i<8 && stage.GetValue(work).ToString() != "Commit"; i++)
            if ((bool)finish.Invoke(allocationOwner, arguments) || command.Plan != null || arguments[3] != null)
                throw new Exception("Incomplete seat assignment must stay private between slices.");
        if (candidate.Positions[1] != new IntVec3(6,0,4) || stage.GetValue(work).ToString() != "Commit")
            throw new Exception("Role-based allocation has its own slice, and movement cannot see the plan before final live-claim validation.");
        AccessTools.Method(typeof(TacticalSquadCommand), "CancelPreparation").Invoke(command, null);
        if (command.Preparation != null || command.Plan != null) throw new Exception("Cancellation must discard uncommitted seats without fabricating entry.");
        Console.WriteLine("PASS: bounded lazy planning slices, cancellation, roster/goal/phase invalidation, separate role allocation and no partial publication.");
    }
}
