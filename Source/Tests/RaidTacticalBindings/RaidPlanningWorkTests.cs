using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidPlanningWorkTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        var owner = new MapComponent_RaidTacticalPlans(null);
        int Window(string id, IntVec3 goal, int room = 1, int version = 7) =>
            (int)AccessTools.Method(owner.GetType(), "BreachWindow").Invoke(owner, new object[] { id, goal, room, version, 100 });
        int Details(string id) => (int)AccessTools.Method(owner.GetType(), "BreachDetailWindow")
            .Invoke(owner, new object[] { id, 19 });
        void Fail(string id, int detailChecks) => AccessTools.Method(owner.GetType(), "AdvanceBreachWindow")
            .Invoke(owner, new object[] { id, 48, 100, detailChecks, 19 });
        var goal = new IntVec3(10, 0, 10);
        Check(Window("one", goal) == 0 && Details("one") == 0, "A new request starts at the first candidate batch.");
        Fail("one", 8);
        Check(Window("one", goal) == 0 && Details("one") == 8,
            "Failed detailed checks continue inside the same structure batch rather than skipping its remaining candidates.");
        Fail("one", 8); Fail("one", 3);
        Check(Window("one", goal) == 48 && Details("one") == 0,
            "Only exhaustion of detailed candidates advances the structure batch.");
        Check(Window("two", goal) == 0, "Independent squads must not advance each other's search windows.");
        Check(Window("one", goal, room: 2) == 0, "Moving into a different room invalidates the previous wall window.");
        Fail("one", 8);
        Check(Window("one", goal + IntVec3.North, room: 2) == 0 && Details("one") == 0,
            "Changing the goal invalidates both candidate cursors.");
        Fail("one", 8);
        Check(Window("one", goal + IntVec3.North, room: 2, version: 8) == 0 && Details("one") == 0,
            "A different structure version invalidates candidate ordering.");

        var plan = new RaidTacticalPlan();
        object work = AccessTools.Field(plan.GetType(), "Work").GetValue(plan);
        AccessTools.Method(work.GetType(), "MarkLimited").Invoke(work, null);
        var plans = (Dictionary<string, RaidTacticalPlan>)AccessTools.Field(owner.GetType(), "plans").GetValue(owner);
        plans["one"] = plan;
        bool Waiting(string id) => (bool)AccessTools.Method(owner.GetType(), "DecisionQueuedFor").Invoke(owner, new object[] { id });
        Check(Waiting("one"), "An allowance-limited first plan retains tactical ownership instead of falling back to another entrance.");
        Check(!Waiting("missing"), "A missing unit does not acquire tactical control.");
        plan.Selected = new RaidTacticalOption();
        Check(!Waiting("one"), "A successful plan no longer waits for its search allowance.");
        Console.WriteLine($"PASS: {checks} bounded breach retry windows and limited-plan ownership checks (real game classes).");
    }
}
