using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Tactics;
using Verse;

internal static class TacticalRoomScanTests
{
    internal static void Run()
    {
        var walls = new HashSet<IntVec3>(Enumerable.Range(0, 20).Select(z => new IntVec3(20, 0, z)));
        bool Roof(IntVec3 cell) => cell.x >= 0 && cell.x < 40 && cell.z >= 0 && cell.z < 20;
        bool Floor(IntVec3 cell) => Roof(cell) && !walls.Contains(cell);
        var scan = new TacticalRoomScan(new IntVec3(0,0,10), new IntVec3(-1,0,10), Floor, walls.Contains);
        if (scan.Step() || scan.Cells.Count >= 400) throw new Exception("Large room work must yield between bounded chunks.");
        int steps = 1;
        while (!scan.Step()) { if (++steps > 20) throw new Exception("Room scan did not terminate."); }
        if (scan.Cells.Count != 400 || scan.Cells.Any(c => c.x >= 20) || scan.Frontiers.Count != 20
            || scan.Frontiers.Any(f => f.Opening.x != 20 || f.Inward != IntVec3.East))
            throw new Exception("Closed boundary must separate entered room from adjacent fronts.");
        // A later entry sees an unexpected hole. Both sides are now connected;
        // existing frontiers targeting these cells must be pruned, not rebreached.
        walls.Remove(new IntVec3(20,0,9));
        var opened = new TacticalRoomScan(new IntVec3(21,0,10), IntVec3.Invalid, Floor, walls.Contains);
        steps = 0;
        while (!opened.Step()) { if (++steps > 20) throw new Exception("Merged room scan did not terminate."); }
        if (opened.Cells.Count != 781 || !opened.Cells.Contains(new IntVec3(5,0,5))
            || !opened.Cells.Contains(new IntVec3(35,0,5))
            || opened.Frontiers.Any(f => !opened.Cells.Contains(f.Inside)))
            throw new Exception("Unexpected hole must merge reachable space and make old breach fronts redundant.");
        if (scan.Cells.Count != 400) throw new Exception("Other squad's snapshot must not refresh through shared live state.");
        var secured = new HashSet<IntVec3>();
        if (opened.CopyCellsTo(secured) || secured.Count != TacticalRoomScan.CellsPerStep)
            throw new Exception("Secured history copying must also yield between bounded chunks.");
        steps = 0;
        while (!opened.CopyCellsTo(secured)) { if (++steps > 20) throw new Exception("Snapshot copying did not terminate."); }
        if (secured.Count != 781) throw new Exception("Chunked secured history must preserve all room cells.");
        walls.Remove(new IntVec3(20,0,10)); walls.Remove(new IntVec3(20,0,11));
        if (!TacticalPortalGeometry.IsGap(new IntVec3(20,0,10), walls.Contains, Floor))
            throw new Exception("A three-cell blast gap must still separate the two room faces.");
        var gapScan = new TacticalRoomScan(new IntVec3(5,0,10), IntVec3.Invalid, Floor,
            cell => walls.Contains(cell) || Floor(cell) && TacticalPortalGeometry.IsGap(cell, walls.Contains, Floor));
        steps = 0;
        while (!gapScan.Step()) { if (++steps > 20) throw new Exception("Portal-separated room scan did not terminate."); }
        if (gapScan.Cells.Any(c => c.x >= 20) || !gapScan.Frontiers.Any(f => f.Opening.z == 10 && f.Opening.x == 20))
            throw new Exception("Opened wall must be reused as a portal instead of marking the far room secured.");
        bool CorridorWall(IntVec3 cell) => (cell.x == 19 || cell.x == 23) && cell.z >= 0 && cell.z < 20;
        bool CorridorFloor(IntVec3 cell) => Roof(cell) && !CorridorWall(cell);
        if (TacticalPortalGeometry.IsGap(new IntVec3(21,0,10), CorridorWall, CorridorFloor))
            throw new Exception("A narrow corridor must not become one portal per floor cell.");
        var outdoor = new TacticalRoomScan(new IntVec3(50,0,50), IntVec3.Invalid, Floor, walls.Contains);
        if (!outdoor.Step() || outdoor.Cells.Count != 0 || outdoor.Frontiers.Count != 0)
            throw new Exception("CQB exploration must not flood unroofed outdoor space.");
        var command = new TacticalSquadCommand(); command.SecuredCells.UnionWith(scan.Cells);
        var other = new TacticalSquadCommand();
        if (other.SecuredCells.Count != 0) throw new Exception("Secured history must belong to a squad.");
        Console.WriteLine("PASS: bounded local room scan, closed boundaries, unexpected-hole merging, stale snapshot and squad isolation.");
    }
}
