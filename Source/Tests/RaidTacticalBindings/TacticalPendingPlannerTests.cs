using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalPendingPlannerTests
{
    internal static void Run()
    {
        var members = new List<TacticalMemberCommand>();
        for (int x = 70; x < 82; x++) members.Add(new TacticalMemberCommand { Pawn = new Pawn { Position = new IntVec3(x,0,101) } });
        IntVec3 goal = new IntVec3(120,0,116), gap = new IntVec3(100,0,113);
        var method = AccessTools.Method(typeof(MapComponent_TacticalCommands), "PendingPlanner");
        Pawn Planner() => (Pawn)method.Invoke(null, new object[] { members, goal });
        if (Planner() != members[11].Pawn || Planner().Position.DistanceToSquared(gap) > 784
            || members[0].Pawn.Position.DistanceToSquared(gap) <= 784)
            throw new Exception("A forward member within actual portal-recognition distance must not be hidden by the first rear roster position.");
        members[0].Pawn.Position = members[11].Pawn.Position;
        if (Planner() != members[0].Pawn) throw new Exception("Equal geometric choices must retain stable roster order.");
        members[0].Pawn.Position = goal;
        if (Planner() != members[0].Pawn) throw new Exception("The nearest valid member must also work after partial indoor arrival.");
        if (members.Count != 12 || members[10].Pawn.Position != new IntVec3(80,0,101))
            throw new Exception("Choosing a planner must not move, repartition or mutate the squad.");
        Console.WriteLine("PASS: forward geometric planner sees real nearby portals, retains ties, and leaves squad membership/positions unchanged.");
    }
}
