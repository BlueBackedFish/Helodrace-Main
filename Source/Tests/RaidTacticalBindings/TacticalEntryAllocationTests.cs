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
            || plan.RetainedOutside.Any(i => !plan.Stack.Contains(plan.Positions[i])))
            throw new Exception("Small entry must preserve member indices, eligibility, priority and unique outside posts.");
        IntVec3 replacedGuard = plan.Positions[2];
        if (!TacticalEntryAllocation.Promote(plan, 2, 4) || plan.RetainedOutside.Contains(2)
            || !plan.RetainedOutside.Contains(4) || plan.Positions[2] != new IntVec3(8,0,0)
            || plan.Positions[4] != replacedGuard || plan.Positions.Distinct().Count() != 12
            || TacticalEntryAllocation.Promote(plan, 2, 4))
            throw new Exception("Entry replacement must transfer its freed guard post without overlapping another reassigned guard.");
        var empty = new TacticalLocalPlan(); empty.Stack.Add(new IntVec3(0,0,0));
        if (TacticalEntryAllocation.Allocate(empty, new[] { 0 }))
            throw new Exception("No inside slot must never produce an all-guard secured room.");
        empty.Positions.Add(new IntVec3(1,0,0));
        if (TacticalEntryAllocation.Allocate(empty, new[] { 0, 0 }) || TacticalEntryAllocation.Allocate(empty, new[] { 1 })
            || empty.Positions.Count != 1 || empty.Positions[0] != new IntVec3(1,0,0))
            throw new Exception("Invalid eligibility must fail without modifying the footprint.");
        var overlap = new TacticalLocalPlan(); overlap.Stack.AddRange(new[] { new IntVec3(0,0,0), new IntVec3(0,0,1) });
        overlap.Positions.Add(new IntVec3(0,0,1));
        if (!TacticalEntryAllocation.Allocate(overlap, new[] { 0, 1 }) || overlap.Positions.Distinct().Count() != 2
            || overlap.Positions[1] != overlap.Stack[0])
            throw new Exception("An unused approach post must let a guard vacate an overlapping entry destination.");
        CheckEntranceGuards();
        var command = new TacticalSquadCommand { Plan = plan, Goal = new IntVec3(8,0,0) };
        var member = new TacticalMemberCommand { Entered = true, Passed = true, Crossed = true, EntryAssignmentDone = true, EverEntered = true };
        command.Members.Add(member); command.SecuredCells.Add(command.Goal);
        AccessTools.Method(typeof(MapComponent_TacticalCommands), "ResetAfterSupport").Invoke(new MapComponent_TacticalCommands(null), new object[] { command, 100 });
        if (member.EntryAssignmentDone || member.Entered || member.Crossed || member.Passed || !member.EverEntered
            || command.Plan != null || !command.GoalSecured || command.Phase != TacticalCommandPhase.Pending)
            throw new Exception("Post-support objective changes must reset current arrivals while preserving actual entry history.");
        Console.WriteLine("PASS: partial entry indexing, near covered entrance flanks, interrupted guard return and casualty replacement.");
    }
    private static void CheckEntranceGuards()
    {
        var plan = new TacticalLocalPlan { Opening = new IntVec3(20, 0, 20), Inward = IntVec3.East };
        for (int depth = 1; depth <= 3; depth++) foreach (int width in new[] { -1, 1, -2, 2 })
            plan.Stack.Add(plan.Opening - IntVec3.East * depth + IntVec3.North * width);
        plan.Stack.Add(plan.Opening - IntVec3.East * 4);
        for (int i = 0; i < 10; i++) plan.Positions.Add(plan.Opening + IntVec3.East * 2 + IntVec3.North * (i - 5));
        var probed = new System.Collections.Generic.HashSet<IntVec3>();
        bool Covered(IntVec3 c)
        {
            if (!probed.Add(c)) throw new Exception("Guard cover must only be queried once per candidate.");
            return Math.Abs(c.z - plan.Opening.z) == 2;
        }
        if (!TacticalEntryAllocation.Allocate(plan, Enumerable.Range(0, 13).ToArray(), Covered)
            || plan.Positions.Distinct().Count() != 13 || plan.RetainedOutside.Count != 3
            || plan.RetainedOutside.Any(i => Math.Abs(plan.Positions[i].z - plan.Opening.z) != 2
                || plan.Positions[i].DistanceToSquared(plan.Opening) >= plan.Stack[i].DistanceToSquared(plan.Opening))
            || !plan.RetainedOutside.Any(i => plan.Positions[i].z > plan.Opening.z)
            || !plan.RetainedOutside.Any(i => plan.Positions[i].z < plan.Opening.z))
            throw new Exception("Surplus members must move from distant original posts to the nearest covered entrance flanks on both sides.");
        var command = new TacticalSquadCommand { Plan = plan, Phase = TacticalCommandPhase.Enter };
        for (int i = 0; i < 13; i++) command.Members.Add(new TacticalMemberCommand());
        var retry = AccessTools.Method(typeof(MapComponent_TacticalCommands), "RetryPost");
        if ((IntVec3)retry.Invoke(null, new object[] { command, command.Members[12] }) != plan.Positions[12])
            throw new Exception("An interrupted entrance guard must return to its new guard post during entry.");
        command.Phase = TacticalCommandPhase.Stack;
        if ((IntVec3)retry.Invoke(null, new object[] { command, command.Members[12] }) != plan.Stack[12])
            throw new Exception("Before entry, retain the safe breach stack instead of moving guards early.");
        IntVec3 guard = plan.Positions[12];
        if (!TacticalEntryAllocation.Promote(plan, 12, 0) || plan.Positions[0] != guard
            || plan.Positions.Distinct().Count() != 13)
            throw new Exception("Promotion after guard relocation must preserve unique indexed guard/entry destinations.");
    }
}
