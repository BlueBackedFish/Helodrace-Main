using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalMissionTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var opening = new IntVec3(0, 0, 4); var inside = opening + IntVec3.East;
        bool Floor(IntVec3 c) => c.x >= 1 && c.x <= 8 && c.z >= 1 && c.z <= 8;
        TacticalStructureClass Finish(TacticalStructureProbe probe, Func<IntVec3, bool> floor, Func<IntVec3, bool> evidence)
        {
            for (int tick = 0; tick < 30; tick++)
            {
                int allowance = TacticalStructureProbe.Slice;
                var result = probe.Step(floor, evidence, ref allowance);
                Check(allowance >= 0, "Structure work must respect the shared slice.");
                if (result != TacticalStructureClass.Pending) return result;
            }
            throw new Exception("Structure classification failed to terminate under a cell cap.");
        }
        var neutral = new TacticalStructureProbe(inside, opening);
        int left = 1;
        Check(neutral.Step(Floor, _ => false, ref left) == TacticalStructureClass.Pending && left == 0,
            "A neutral ruin must yield rather than classify a partial scan as cleared.");
        Check(Finish(neutral, Floor, _ => false) == TacticalStructureClass.Incidental,
            "An unrelated enclosed neutral ruin is not a mission target.");
        var goal = new IntVec3(7, 0, 7);
        Check(Finish(new TacticalStructureProbe(inside, opening), Floor, c => c == goal) == TacticalStructureClass.Target,
            "An explicitly supplied goal inside a reused neutral ruin qualifies that local structure.");
        Check(Finish(new TacticalStructureProbe(inside, opening), Floor, c => c == new IntVec3(12, 0, 4)) == TacticalStructureClass.Incidental,
            "A nearby disconnected owned structure must not qualify a ruin by distance.");
        Check(Finish(new TacticalStructureProbe(new IntVec3(30, 0, 30), opening), _ => false, _ => true) == TacticalStructureClass.Incidental,
            "Outdoor space must not flood to a remote settlement anchor.");
        Check(Finish(new TacticalStructureProbe(inside, opening), _ => true, _ => false) == TacticalStructureClass.Incidental,
            "An unbounded floor predicate must still terminate at the local cell cap.");
        var command = new TacticalSquadCommand { GoalSecured = true, Goal = goal };
        command.Frontiers.Add(new TacticalRoomFrontier { Opening = opening, Inward = IntVec3.East, Excluded = true });
        Check((IntVec3)AccessTools.Method(typeof(MapComponent_TacticalCommands), "PendingGoal").Invoke(null, new object[] { command }) == goal,
            "An excluded incidental frontier must not become the pending mission goal.");
        Console.WriteLine("PASS: " + checks + " bounded structure target, reused ruin, disconnected evidence and excluded-goal checks.");
    }
}
