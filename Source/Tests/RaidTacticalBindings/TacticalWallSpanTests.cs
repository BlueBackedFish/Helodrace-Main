using System;
using System.Collections.Generic;
using System.IO;
using Helodrace.Tactics;
using Verse;

internal static class TacticalWallSpanTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var mouth = new IntVec3(10, 0, 10);
        var walls = new HashSet<IntVec3> { mouth, mouth + IntVec3.East };
        bool Open(IntVec3 c) => !walls.Contains(c);
        Check(TacticalPortalGeometry.TryDepth(mouth, IntVec3.East, walls.Contains, Open, out int depth) && depth == 2,
            "Double walls must produce one two-layer entry, not a blocked inside floor.");
        var plan = new TacticalLocalPlan { Opening = mouth, Inward = IntVec3.East, Depth = depth };
        Check(plan.Inside == mouth + IntVec3.East * 2 && plan.InnerOpening == mouth + IntVec3.East,
            "Room survey and indoor posts must begin beyond the final wall layer.");
        Check(TacticalPortalGeometry.FirstBlocked(plan.Opening, plan.Inward, plan.Depth, Open) == mouth,
            "The outer layer is always first.");
        walls.Remove(mouth);
        Check(!TacticalPortalGeometry.PassageOpen(plan.Opening, plan.Inward, plan.Depth, Open)
            && TacticalPortalGeometry.FirstBlocked(plan.Opening, plan.Inward, plan.Depth, Open) == mouth + IntVec3.East,
            "Opening only the outer layer must continue straight through the remaining layer before observation/entry.");
        walls.Clear();
        Check(TacticalPortalGeometry.PassageOpen(plan.Opening, plan.Inward, plan.Depth, Open)
            && !TacticalPortalGeometry.FirstBlocked(plan.Opening, plan.Inward, plan.Depth, Open).IsValid,
            "An open channel must not create another breach action.");
        walls.Add(mouth + IntVec3.East * 2);
        Check(!TacticalPortalGeometry.PassageOpen(plan.Opening, plan.Inward, plan.Depth, Open),
            "A blocked destination floor is not a usable passage.");
        walls.Clear(); walls.Add(mouth); walls.Add(mouth + IntVec3.East * 2);
        Check(TacticalPortalGeometry.TryDepth(mouth, IntVec3.East, walls.Contains, Open, out depth) && depth == 1,
            "A walkable space between walls must remain a separate entry, not another wall layer.");
        walls.Clear(); for (int i = 0; i < 5; i++) walls.Add(mouth + IntVec3.East * i);
        int reads = 0;
        Check(!TacticalPortalGeometry.TryDepth(mouth, IntVec3.East, c => { reads++; return walls.Contains(c); }, Open, out depth)
            && reads == TacticalPortalGeometry.LayerLimit, "Thick-wall probing must keep its four-cell work cap.");
        walls.Remove(mouth + IntVec3.East * 4);
        Check(TacticalPortalGeometry.TryDepth(mouth, IntVec3.East, walls.Contains, Open, out depth) && depth == 4,
            "Four contiguous wall layers must be supported without enlarging the search budget.");
        var reverse = new TacticalLocalPlan { Opening = plan.InnerOpening, Inward = -plan.Inward, Depth = plan.Depth };
        Check(reverse.Outside == plan.Inside && reverse.Inside == plan.Outside,
            "Reusing the exit must reverse both faces of the complete channel.");

        // Exercise the real room scanner's new thick boundary adapter.
        walls.Clear();
        for (int z = 0; z < 6; z++) { walls.Add(new IntVec3(5, 0, z)); walls.Add(new IntVec3(6, 0, z)); }
        bool Floor(IntVec3 c) => c.x >= 0 && c.x < 12 && c.z >= 0 && c.z < 6 && !walls.Contains(c);
        int Depth(IntVec3 cell, IntVec3 inward) => TacticalPortalGeometry.TryDepth(cell, inward, walls.Contains, Floor, out int found) ? found : 0;
        var scan = new TacticalRoomScan(new IntVec3(2, 0, 2), IntVec3.Invalid, Floor, walls.Contains, portalDepth: Depth);
        while (!scan.Step()) { }
        Check(scan.Frontiers.Count == 6 && scan.Frontiers.TrueForAll(f => f.Depth == 2 && f.Inside.x == 7)
            && scan.Cells.Count == 30, "Known double partitions must target the actual unentered floor, without flooding through walls.");

        string path = Path.Combine(Path.GetTempPath(), "Helodrace-WallSpan-" + Guid.NewGuid() + ".xml");
        try
        {
            plan.Stack.Add(plan.Outside); plan.Positions.Add(plan.Inside);
            Scribe.saver.InitSaving(path, "plan"); plan.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalLocalPlan(); Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            Check(loaded.Depth == 2 && loaded.Inside == plan.Inside && loaded.InnerOpening == plan.InnerOpening
                && loaded.Stack[0] == plan.Outside, "Native Scribe must preserve the channel depth and its two faces.");
            var frontier = new TacticalRoomFrontier { Opening = mouth, Inward = IntVec3.East, Depth = 3 };
            Scribe.saver.InitSaving(path, "frontier"); frontier.ExposeData(); Scribe.saver.FinalizeSaving();
            var restored = new TacticalRoomFrontier(); Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.ForceStop();
            Check(restored.Depth == 3 && restored.Inside == mouth + IntVec3.East * 3,
                "A saved frontier must retain the destination beyond its full thickness.");
            var action = new TacticalChargeAction { Position = mouth + IntVec3.East, Detonated = true };
            Scribe.saver.InitSaving(path, "charge"); action.ExposeData(); Scribe.saver.FinalizeSaving();
            var charge = new TacticalChargeAction(); Scribe.loader.InitLoading(path); charge.ExposeData(); Scribe.loader.ForceStop();
            Check(charge.Position == action.Position && charge.Detonated, "Inner-layer C4 safety must retain its actual blast position.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        var packet = new TacticalSecuredReport { Origin = "a", Goal = plan.Inside, Opening = mouth, Inward = IntVec3.East,
            Depth = 2, Sequence = 1, SeenTick = 10, Frontiers = new List<TacticalRoomFrontier> { new TacticalRoomFrontier { Depth = 3 } } };
        var copy = packet.Copy(); packet.Depth = 1; packet.Frontiers[0].Depth = 1;
        Check(copy.Depth == 2 && copy.Frontiers[0].Depth == 3, "Other squads must receive detached channel and frontier depths.");
        Console.WriteLine("PASS: " + checks + " wall-layer progression, completed passage, reverse exit, thick room frontier, bounded work and native save checks.");
    }
}
