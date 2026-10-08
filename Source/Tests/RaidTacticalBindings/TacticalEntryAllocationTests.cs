using System;
using System.Linq;
using Helodrace.Tactics;
using Verse;
using HarmonyLib;

internal static class TacticalEntryAllocationTests
{
    internal static void Run()
    {
        var plan = new TacticalLocalPlan();
        plan.Stack.AddRange(Enumerable.Range(0, 12).Select(i => new IntVec3(5,0,i)));
        plan.Positions.AddRange(Enumerable.Range(0, 3).Select(i => new IntVec3(8,0,i)));
        if (!TacticalEntryAllocation.Allocate(plan, new[] { 4, 0, 9, 1, 2, 3, 5, 6, 7, 8, 10 })
            || plan.Positions.Count != 12 || plan.Positions.Distinct().Count() != 12
            || plan.RetainedOutside.Count != 9 || plan.RetainedOutside.Contains(4) || plan.RetainedOutside.Contains(0)
            || plan.RetainedOutside.Contains(9) || !plan.RetainedOutside.Contains(11)
            || plan.Positions[4] != new IntVec3(8,0,0) || plan.Positions[9] != new IntVec3(8,0,2)
            || plan.RetainedOutside.Any(i => plan.Positions[i] != plan.Stack[i]))
            throw new Exception("Small entry must preserve member indices, eligibility, priority and unique outside posts.");
        plan.Stack[2] = new IntVec3(4,0,2); TacticalEntryAllocation.SyncOutside(plan);
        if (plan.Positions[2] != plan.Stack[2] || plan.Positions[4] != new IntVec3(8,0,0))
            throw new Exception("Throw-cover stack changes must update guards only.");
        if (!TacticalEntryAllocation.Promote(plan, 2, 4) || plan.RetainedOutside.Contains(2)
            || !plan.RetainedOutside.Contains(4) || plan.Positions[2] != new IntVec3(8,0,0)
            || plan.Positions[4] != plan.Stack[4] || plan.Positions.Distinct().Count() != 12
            || TacticalEntryAllocation.Promote(plan, 2, 4))
            throw new Exception("A lost entry operator must have a unique active replacement and its own unused outside post.");
        var empty = new TacticalLocalPlan(); empty.Stack.Add(new IntVec3(0,0,0));
        if (TacticalEntryAllocation.Allocate(empty, new[] { 0 }))
            throw new Exception("No inside slot must never produce an all-guard secured room.");
        empty.Positions.Add(new IntVec3(1,0,0));
        if (TacticalEntryAllocation.Allocate(empty, new[] { 0, 0 }) || TacticalEntryAllocation.Allocate(empty, new[] { 1 })
            || empty.Positions.Count != 1 || empty.Positions[0] != new IntVec3(1,0,0))
            throw new Exception("Invalid eligibility must fail without modifying the footprint.");
        var overlap = new TacticalLocalPlan(); overlap.Stack.AddRange(new[] { new IntVec3(0,0,0), new IntVec3(0,0,1) });
        overlap.Positions.Add(new IntVec3(0,0,1));
        if (TacticalEntryAllocation.Allocate(overlap, new[] { 0, 1 }) || overlap.Positions.Count != 1)
            throw new Exception("Partial entry must reject overlapping inside/outside posts before mutation.");
        var command = new TacticalSquadCommand { Plan = plan, Goal = new IntVec3(8,0,0) };
        var member = new TacticalMemberCommand { Entered = true, Passed = true, Crossed = true, EntryAssignmentDone = true, EverEntered = true };
        command.Members.Add(member); command.SecuredCells.Add(command.Goal);
        AccessTools.Method(typeof(MapComponent_TacticalCommands), "ResetAfterSupport").Invoke(new MapComponent_TacticalCommands(null), new object[] { command, 100 });
        if (member.EntryAssignmentDone || member.Entered || member.Crossed || member.Passed || !member.EverEntered
            || command.Plan != null || !command.GoalSecured || command.Phase != TacticalCommandPhase.Pending)
            throw new Exception("Post-support objective changes must reset current arrivals while preserving actual entry history.");
        Console.WriteLine("PASS: partial room entry indexing, outside guards, cover relocation, casualty replacement and invalid footprint rejection.");
    }
}
