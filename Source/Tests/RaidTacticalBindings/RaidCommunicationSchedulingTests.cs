using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Squads;
using Verse;

internal static class RaidCommunicationSchedulingTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        Game previous = Current.Game;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        game.components = new List<GameComponent>();
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var registry = new GameComponent_CombatOrganizations(game);
        game.components.Add(registry); game.components.Add(new GameComponent_RaidExecutionScheduler(game));
        var execution = new MapComponent_RaidTacticalExecution(map);
        var comm = new MapComponent_RaidTacticalCommunications(map);
        AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent> { execution, comm });
        var org = new CombatOrganization { id = "Frames", rootGroups = Enumerable.Range(0,16)
            .Select(i => new CombatGroup { id = "group" + i }).ToList() };
        org.RestoreTreeLinks();
        AccessTools.Field(registry.GetType(), "organizations").SetValue(registry, new List<CombatOrganization> { org });
        Current.Game = game;
        try
        {
            var units = RaidTacticalUnit.ForOrganization(org).ToList();
            foreach (var unit in units)
                AccessTools.Method(execution.GetType(), "RegisterUnit").Invoke(execution, new object[] { unit, 0 });
            int Pending() => (int)AccessTools.Property(comm.GetType(), "FramePending").GetValue(comm);
            void Tick(int tick)
            {
                AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, tick);
                comm.MapComponentTick();
            }
            for (int tick = 0; tick < 50; tick++) Tick(tick);
            Check(Pending() == 16, "All registered units retain one periodic frame deadline while their plan is pending.");
            Check((int)AccessTools.Field(comm.GetType(), "MaximumFrameWork").GetValue(comm) <= 2,
                "Actual map callbacks never refresh the whole sixteen-unit roster in one tick.");
            var due = AccessTools.Field(comm.GetType(), "frameWork").GetValue(comm);
            for (int i = 0; i < 40; i++) AccessTools.Method(comm.GetType(), "InvalidateUnit").Invoke(comm, new object[] { units[0].Id });
            Check(Pending() == 16, "Repeated casualty invalidation cannot multiply scheduled frame work.");
            Tick(50);
            Check(Pending() == 16, "An invalidated unit is refreshed without dropping its periodic fallback deadline.");
            var equipment = (System.Collections.IDictionary)AccessTools.Field(comm.GetType(), "equipment").GetValue(comm);
            var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); pawnDef.defName = "FrameCacheFixture";
            equipment.Add(new Pawn { thingIDNumber=93901, def=pawnDef }, null);
            execution.MapRemoved(); Tick(180);
            Check(Pending() == 0 && equipment.Count == 0,
                "Raid departure removes obsolete frame deadlines and cached equipment without requiring map destruction.");
            comm.MapRemoved();
            Check(Pending() == 0, "Map removal releases all remaining frame work.");
        }
        finally { Current.Game = previous; }
        Console.WriteLine($"PASS: {checks} actual distributed communication frame, coalescing and departure checks.");
    }
}
