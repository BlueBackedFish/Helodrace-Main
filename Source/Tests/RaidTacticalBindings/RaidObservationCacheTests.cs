using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidObservationCacheTests
{
    internal static void Run()
    {
        int checks = 0, rays = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(128, 1, 128) };
        map.cellIndices = new CellIndices(map);
        map.mapPawns = new MapPawns(map);
        Type cacheType = typeof(RaidTacticalPlan).Assembly.GetType("Helodrace.RaidPhysicalMapCache", true);
        object cache = AccessTools.Method(cacheType, "For").Invoke(null, new object[] { map });
        var source = new IntVec3(10, 0, 10); var target = new IntVec3(20, 0, 10);
        bool Line(int tick, bool result, IntVec3? end = null) => (bool)AccessTools.Method(cacheType, "ClearLine")
            .Invoke(cache, new object[] { source, end ?? target, tick, (Func<bool>)(() => { rays++; return result; }) });
        Check(Line(100, true) && Line(159, false) && rays == 1,
            "Actual LOS cache shares a prior result across ticks instead of raycasting each time.");
        Check(!Line(160, false) && rays == 2, "A stale observation is refreshed at its bounded lifetime.");
        Check(Line(160, true, source) && rays == 3, "Different endpoints cannot borrow another cached ray.");
        Check(Line(90, true) && rays == 4, "Rewinding ticks cannot reuse a snapshot from the future.");
        AccessTools.Method(cacheType, "Dirty", new[] { typeof(Map), typeof(IntVec3) })
            .Invoke(null, new object[] { map, new IntVec3(60, 0, 60) });
        Check(Line(91, false) && rays == 4 && (int)AccessTools.Property(cacheType, "StructureRevision").GetValue(cache) == 1,
            "Unrelated physical changes keep stale observation rays while still revising movement geometry.");
        for (int i = 0; i < 8300; i++) Line(100, true, map.cellIndices.IndexToCell(i));
        var lines = (System.Collections.IDictionary)AccessTools.Field(cacheType, "lines").GetValue(cache);
        Check(lines.Count <= 8192, "Observation reuse has a fixed memory bound even across many endpoint pairs.");

        Game previous = Current.Game;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        Current.Game = game;
        try
        {
            var first = new Pawn { thingIDNumber = 92601 }; var replacement = new Pawn { thingIDNumber = 92602 };
            foreach (Pawn pawn in new[] { first, replacement })
            {
                AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
                AccessTools.Field(typeof(Thing), "positionInt").SetValue(pawn, target);
            }
            var spawned = (List<Pawn>)map.mapPawns.AllPawnsSpawned;
            spawned.Add(first);
            Pawn Find(int id, int tick) => (Pawn)AccessTools.Method(cacheType, "FindPawn").Invoke(cache, new object[] { id, tick });
            Check(Find(first.thingIDNumber, 200) == first && Find(first.thingIDNumber, 259) == first
                && (long)AccessTools.Field(cacheType, "SpatialBuilds").GetValue(cache) == 1,
                "Pawn ID resolution shares the map's sixty-tick broad-phase rebuild across callers.");
            spawned.Add(replacement);
            Check(Find(replacement.thingIDNumber, 259) == replacement
                && (long)AccessTools.Field(cacheType, "SpatialBuilds").GetValue(cache) == 2,
                "New arrivals invalidate the pawn index before its normal expiry.");
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(first, (sbyte)-1);
            Check(Find(first.thingIDNumber, 259) == null, "A retained broad-phase entry never returns a despawned pawn.");
            spawned.Clear();
            Check(Find(replacement.thingIDNumber, 260) == null,
                "Raid departure rebuilds the index and releases stale pawn references.");
            var execution = new MapComponent_RaidTacticalExecution(map);
            var state = new MapComponent_RaidTacticalExecution.ExecutionState();
            var enemies = (List<Pawn>)AccessTools.Method(execution.GetType(), "VisibleArmedEnemies")
                .Invoke(execution, new object[] { new List<Pawn> { replacement }, state, 300 });
            Check(enemies.Count == 0 && state.Contacts.Entries.Count == 0,
                "Shared physical pawn indexing cannot grant a unit unobserved enemy knowledge.");
        }
        finally { Current.Game = previous; }
        Console.WriteLine($"PASS: {checks} native stale LOS, bounded cache, shared pawn index and knowledge isolation checks.");
    }
}
