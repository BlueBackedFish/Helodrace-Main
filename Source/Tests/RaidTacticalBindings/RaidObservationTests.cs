using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidObservationTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var type = typeof(RaidContactMemory).Assembly.GetType("Helodrace.RaidObservationSight");
        var method = AccessTools.Method(type, "VisibleLine");
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(17, 1, 17) };
        map.cellIndices = new CellIndices(map);
        map.edificeGrid = new EdificeGrid(map);
        var full = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        full.fillPercent = 1f; full.size = new IntVec2(1, 1);
        bool Sight(IntVec3 source, IntVec3 target, IntVec3 leanSource, IntVec3 leanTarget,
            bool lean = true, Func<IntVec3, IntVec3, bool> line = null) =>
            (bool)method.Invoke(null, new object[] { source, lean,
                new Action<IntVec3, List<IntVec3>>((cell, list) => { list.Add(target); if (leanTarget.IsValid) list.Add(leanTarget); }),
                new Action<List<IntVec3>>(list => { if (leanSource.IsValid) list.Add(leanSource); }),
                line ?? new Func<IntVec3, IntVec3, bool>((a, b) => GenSight.LineOfSight(a, b, map, true)) });
        // Feed known native lean candidates: test composition against actual grid LOS.
        // The native lean generator itself needs Unity quaternion ECalls and is checked in-game.
        foreach (IntVec3 forward in new[] { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West })
        {
            Array.Clear(map.edificeGrid.InnerArray, 0, map.edificeGrid.InnerArray.Length);
            var target = new IntVec3(8, 0, 8);
            var side = new IntVec3(-forward.z, 0, forward.x);
            IntVec3 source = target - forward * 2 + side * 2;
            IntVec3 obstruction = GenSight.PointsOnLineOfSight(source, target).Where(cell => cell != target).Last();
            map.edificeGrid.InnerArray[map.cellIndices.CellToIndex(obstruction)] = new Building { def = full };
            IntVec3 leanTarget = GenAdj.CardinalDirections.Select(direction => target + direction)
                .Where(cell => cell != obstruction && GenSight.LineOfSight(source, cell, map, true))
                .OrderBy(cell => cell.DistanceToSquared(source)).First();
            Check(!GenSight.LineOfSight(source, target, map, true), "The fixture must block a doorway pawn's center ray.");
            Check(Sight(source, target, IntVec3.Invalid, leanTarget), "A visible native target lean cell must establish contact despite a blocked center ray.");
            Check(!Sight(source, target, IntVec3.Invalid, IntVec3.Invalid), "No supplied native lean cell means a wall still blocks detection.");
            Check(!Sight(source, target, IntVec3.Invalid, leanTarget, line: (a, b) => false),
                "Smoke on every candidate ray prevents doorway contact.");
        }
        var root = new IntVec3(4, 0, 4);
        var destination = new IntVec3(8, 0, 8);
        var shifted = root + IntVec3.East;
        Check(Sight(root, destination, shifted, IntVec3.Invalid, line: (a, b) => a == shifted),
            "A native observer lean source can reveal a contact.");
        Check(!Sight(root, destination, shifted, IntVec3.Invalid, lean: false, line: (a, b) => a == shifted),
            "A dedicated peek does not add another source lean beyond its explicit observation source.");
        var pawn = new Pawn();
        Check(!(bool)AccessTools.Method(type, "CanSeePawn").Invoke(null, new object[] { map, root, pawn, 24, true,
            new Func<IntVec3, IntVec3, bool>((a, b) => true) }), "An unspawned target cannot enter contact memory.");
        Console.WriteLine($"PASS: {checks} doorway observation ray, smoke and explicit peek-source checks");
    }
}
