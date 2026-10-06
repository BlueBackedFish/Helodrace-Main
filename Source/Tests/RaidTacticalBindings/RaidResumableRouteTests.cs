using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidResumableRouteTests
{
    internal static void Run()
    {
        const int width = 75, height = 5;
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        Assembly assembly = typeof(RaidTacticalPlanner).Assembly;
        Type inputType = assembly.GetType("Helodrace.TacticalGeometryInput"), rawType = assembly.GetType("Helodrace.TacticalRawCell");
        Type flagsType = assembly.GetType("Helodrace.TacticalRawFlags");
        object input = Activator.CreateInstance(inputType, new object[] { width, height });
        var cells = (Array)AccessTools.Field(inputType, "Cells").GetValue(input);
        for (int i = 0; i < cells.Length; i++)
        {
            object cell = Activator.CreateInstance(rawType);
            AccessTools.Field(rawType, "Room").SetValue(cell, 1);
            AccessTools.Field(rawType, "Flags").SetValue(cell, Enum.Parse(flagsType, "Standable"));
            cells.SetValue(cell, i);
        }
        object geometry = AccessTools.Method(assembly.GetType("Helodrace.TacticalGeometry"), "Calculate")
            .Invoke(null, new object[] { input, CancellationToken.None });
        var version = (TacticalStructureVersion)Activator.CreateInstance(typeof(TacticalStructureVersion),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { (object)19, geometry }, null);
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(width, 1, height) }; map.cellIndices = new CellIndices(width, height);
        var snapshot = (RaidStructureSnapshot)Activator.CreateInstance(typeof(RaidStructureSnapshot),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { map, version, "RouteTest" }, null);
        Type navType = typeof(RaidTacticalPlanner).GetNestedType("RaidNavigationSnapshot", BindingFlags.NonPublic);
        object nav = RuntimeHelpers.GetUninitializedObject(navType);
        AccessTools.Field(navType, "map").SetValue(nav, map);
        var walkable = new Dictionary<int, bool>(); for (int i = 0; i < cells.Length; i++) walkable[i] = true;
        AccessTools.Field(navType, "walkable").SetValue(nav, walkable);
        object work = Activator.CreateInstance(assembly.GetType("Helodrace.RaidPlanningWork"), true);
        AccessTools.Field(navType, "Work").SetValue(nav, work);
        var result = new List<IntVec3>(); var traps = new HashSet<IntVec3>();
        IntVec3 from = new IntVec3(1, 0, 2), to = new IntVec3(70, 0, 2);
        IEnumerator Run() => ((IEnumerable)AccessTools.Method(typeof(RaidTacticalPlanner), "RouteCoreSteps")
            .Invoke(null, new object[] { map, snapshot, null, from, to, traps, null, nav, result })).GetEnumerator();
        int Steps() => (int)AccessTools.Property(work.GetType(), "RouteSteps").GetValue(work);
        IEnumerator route = Run();
        Check(route.MoveNext() && Steps() == 32 && result.Count == 0,
            "A live route search yields after at most 32 frontier operations without publishing a partial route.");
        traps.Add(new IntVec3(50, 0, 2));
        int resumes = 1; while (route.MoveNext()) { resumes++; if (resumes > 100) throw new Exception("Route never completed"); }
        Check(resumes > 1 && result[0] == from && result[result.Count - 1] == to,
            "Resuming retains the frontier and predecessor chain until the full route is reconstructed.");
        Check(!result.Contains(new IntVec3(50, 0, 2)) && result.Count > 70,
            "The resumed real planner applies its avoidance predicate to subsequent expansions.");
        Check(Steps() > 32 && Steps() < 4096, "All slices share one accumulated route allowance.");
        result.Clear();
        while ((bool)AccessTools.Method(work.GetType(), "TryRouteStep").Invoke(work, null)) { }
        Check(!Run().MoveNext() && result.Count == 0 && (bool)AccessTools.Property(work.GetType(), "Limited").GetValue(work),
            "Exhausting the common allowance cannot start a fresh unbounded route on another slice.");
        // A native edge raid starts far farther out than the old 75x5 corridor.
        // Exercise real outdoor cost/heuristic with the same 4096-node allowance.
        const int outdoorSize = 210;
        input = Activator.CreateInstance(inputType, new object[] { outdoorSize, outdoorSize });
        cells = (Array)AccessTools.Field(inputType, "Cells").GetValue(input);
        for (int i = 0; i < cells.Length; i++)
        {
            object cell = Activator.CreateInstance(rawType);
            AccessTools.Field(rawType, "Flags").SetValue(cell, Enum.Parse(flagsType, "Standable"));
            cells.SetValue(cell, i);
        }
        geometry = AccessTools.Method(assembly.GetType("Helodrace.TacticalGeometry"), "Calculate")
            .Invoke(null, new object[] { input, CancellationToken.None });
        version = (TacticalStructureVersion)Activator.CreateInstance(typeof(TacticalStructureVersion),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { (object)20, geometry }, null);
        map.info.Size = new IntVec3(outdoorSize, 1, outdoorSize);
        map.cellIndices = new CellIndices(outdoorSize, outdoorSize);
        snapshot = (RaidStructureSnapshot)Activator.CreateInstance(typeof(RaidStructureSnapshot),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { map, version, "OutdoorRouteTest" }, null);
        walkable.Clear(); for (int i = 0; i < cells.Length; i++) walkable[i] = true;
        work = Activator.CreateInstance(assembly.GetType("Helodrace.RaidPlanningWork"), true);
        AccessTools.Field(navType, "Work").SetValue(nav, work);
        Type threatType = typeof(RaidTacticalPlanner).GetNestedType("FieldThreatSnapshot", BindingFlags.NonPublic);
        object threat = Activator.CreateInstance(threatType, new object[] { map, Array.Empty<Pawn>() });
        from = new IntVec3(3, 0, 3); to = new IntVec3(190, 0, 180);
        result.Clear(); traps.Clear(); traps.Add(new IntVec3(190, 0, 179));
        route = ((IEnumerable)AccessTools.Method(typeof(RaidTacticalPlanner), "RouteCoreSteps")
            .Invoke(null, new object[] { map, snapshot, threat, from, to, traps, null, nav, result })).GetEnumerator();
        resumes = 0; while (route.MoveNext()) { if (++resumes > 200) throw new Exception("Outdoor route never completed"); }
        Check(result.Count > 0 && result[0] == from && result[result.Count - 1] == to,
            "A distant native map-edge approach completes instead of exhausting every repeated first decision.");
        Check(Steps() < 4096 && !(bool)AccessTools.Property(work.GetType(), "Limited").GetValue(work),
            "The actual exposed-ground route fits the unchanged shared planning allowance.");
        Check(!result.Contains(new IntVec3(190, 0, 179)), "Goal-directed outdoor searches still enforce trap avoidance.");
        Console.WriteLine($"PASS: {checks} real resumable indoor/outdoor route, reconstruction and shared allowance checks.");
    }
}
