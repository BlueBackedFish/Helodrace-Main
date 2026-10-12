using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalClearedTransitTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var knowledge = new TacticalSecuredKnowledge();
        TacticalLocalPlan Plan(int depth = 1) => new TacticalLocalPlan { Opening = IntVec3.Zero, Inward = IntVec3.East, Depth = depth };
        var plan = Plan(2);
        var surveyed = new IntVec3(4, 0, 1);
        knowledge.Record(new TacticalSecuredReport { Opening = plan.Opening, Inward = plan.Inward,
            Depth = 2, PortalUsable = true, SeenTick = 10 });
        Check(!TacticalSharedPassagePolicy.Cleared(knowledge, plan), "An open hole alone must not skip stack/observation into an uncleared room.");
        knowledge.Record(new TacticalSecuredReport { Opening = plan.Opening, Inward = plan.Inward,
            Depth = 2, PortalUsable = true, SeenTick = 20, Cells = new List<IntVec3> { surveyed } });
        Check(!knowledge.Cleared(plan.Inside) && TacticalSharedPassagePolicy.Cleared(knowledge, plan),
            "A cleared room retains its entrance evidence when the blasted, unroofed inside mouth was omitted from the survey.");
        Check(knowledge.ClearedPassage(plan.Opening, plan.Inward, 1)
            && !knowledge.ClearedPassage(plan.Opening, plan.Inward, 3)
            && !knowledge.ClearedPassage(plan.Opening, -plan.Inward, 2),
            "Destroyed layers may shorten the same entrance, but clearance cannot invent deeper or reverse entry.");
        knowledge.ObserveThreat(surveyed, surveyed, 30);
        Check(!TacticalSharedPassagePolicy.Cleared(knowledge, plan) && knowledge.KnownPassage(plan.Opening, plan.Inward, 2),
            "Observed reinfiltration restores normal entry preparation while retaining the physical gap history.");
        knowledge.Record(new TacticalSecuredReport { Opening = plan.Opening, Inward = plan.Inward,
            Depth = 2, PortalUsable = true, SeenTick = 40 });
        Check(!TacticalSharedPassagePolicy.Cleared(knowledge, plan), "A new portal-only report cannot restore revoked room clearance.");
        Check(TacticalSharedPassagePolicy.IngressLeg(false, false, true) == 1
            && TacticalSharedPassagePolicy.IngressLeg(false, false, false) == 0
            && TacticalSharedPassagePolicy.IngressLeg(false, true, false) == 1
            && TacticalSharedPassagePolicy.IngressLeg(true, true, true) == 2,
            "Cleared transit skips only the outside leg, retaining actual opening/inside crossing before final arrival.");
        plan = Plan();
        bool NarrowApproach(IntVec3 c) => c.x >= 1 && c.x <= 5 && Math.Abs(c.z) <= 2 || c.x >= -2 && c.x <= -1 && c.z == 0;
        Check(TacticalClearedTransit.Build(plan, 13, NarrowApproach, NarrowApproach)
            && plan.Positions.Count == 13 && plan.Positions.All(c => c.x >= 1 && c != plan.Inside)
            && plan.ReportedPassage && !plan.EntryLane.IsValid,
            "A thirteen-person squad can immediately enter clear destination space from an approach that cannot fit a thirteen-person stack.");
        Check(TacticalEntryAllocation.Allocate(plan, Enumerable.Range(0, 13).Reverse().ToArray())
            && plan.RetainedOutside.Count == 0 && plan.Positions.Distinct().Count() == 13,
            "Transit keeps unique indexed seats without requiring LOS, a connected stack or a gathered entry wave.");
        plan = Plan();
        bool SmallRoom(IntVec3 c) => c.x >= 1 && c.x <= 2 && c.z >= 0 && c.z <= 1 || c.x >= -4 && c.x <= -1 && Math.Abs(c.z) <= 1;
        Check(TacticalClearedTransit.Build(plan, 6, SmallRoom, SmallRoom) && plan.Positions.Count == 3,
            "A tiny room admits available inside space and builds ordinary approach destinations for surplus guards.");
        Check(TacticalEntryAllocation.Allocate(plan, Enumerable.Range(0, 6).ToArray())
            && plan.RetainedOutside.Count == 3 && plan.Positions.Distinct().Count() == 6
            && plan.Positions.All(c => c != plan.Opening && c != plan.Inside && c != plan.Outside),
            "Small-room surplus cannot reserve/hold the mouth, reuse another member's seat or require a pre-entry stack.");
        plan = Plan();
        var claimed = new IntVec3(1, 0, 1);
        Check(TacticalClearedTransit.Build(plan, 8, NarrowApproach, c => NarrowApproach(c) && c != claimed)
            && !plan.Stack.Contains(claimed), "Other squads' held destinations remain excluded without exclusively leasing their cleared passage.");
        plan = Plan();
        bool SplitRoom(IntVec3 c) => c.x >= 1 && c.x <= 4 && Math.Abs(c.z) <= 2 && c.x != 2;
        Check(TacticalClearedTransit.Build(plan, 4, SplitRoom, SplitRoom) && plan.Positions.All(c => c.x == 1),
            "Destination flooding cannot allocate across a blocking partition just because floor exists behind it.");
        plan = Plan(2);
        bool DeepRoom(IntVec3 c) => c.x >= -4 && c.x <= 5 && Math.Abs(c.z) <= 2;
        Check(TacticalClearedTransit.Build(plan, 6, DeepRoom, DeepRoom) && plan.Positions.All(c => c.x >= 2),
            "An adjoining double-wall channel remains transit space instead of becoming a standing destination.");
        Check(!TacticalClearedTransit.Build(Plan(), 6, c => c.x == 1 && c.z == 0, c => true),
            "An obstructed/no-seat destination must fail rather than holding members on the mouth.");
        // Recovery must not issue a former stack destination after a stopped
        // ingress/contact/identification interruption.
        var command = new TacticalSquadCommand { Plan = plan, Phase = TacticalCommandPhase.Enter };
        var member = new TacticalMemberCommand(); command.Members.Add(member);
        plan.Positions[0] = new IntVec3(5, 0, 1); plan.Stack[0] = new IntVec3(-4, 0, 0);
        Check((IntVec3)AccessTools.Method(typeof(MapComponent_TacticalCommands), "RetryPost").Invoke(null, new object[] { command, member }) == plan.Positions[0],
            "Interrupted transit resumes its destination even before physical crossing, never an obsolete stack post.");
        var originalClaim = AccessTools.Method(typeof(MapComponent_TacticalCommands), "OriginalClaim");
        Check(!(bool)originalClaim.Invoke(null, new object[] { command, plan.Opening })
            && !(bool)originalClaim.Invoke(null, new object[] { command, plan.Stack[0] })
            && (bool)originalClaim.Invoke(null, new object[] { command, plan.Positions[0] }),
            "Temporary contact/identification reservations cannot preserve cleared transit's retired staging claims.");
        Console.WriteLine("PASS: " + checks + " cleared-mouth evidence, recheck, transit legs, narrow/small-room destinations, claim exclusion and interrupted-transit recovery checks.");
    }
}
