using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace.Tactics;
using RimWorld;
using Verse;

internal static class TacticalFurnitureTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        Check(TacticalFurniturePolicy.WorkTicks(280, 35, 75) == 600
            && TacticalFurniturePolicy.WorkTicks(281, 35, 75) > 600,
            "Quick destruction must include the final strike and cap at ten seconds.");
        Check(TacticalFurniturePolicy.WorkTicks(int.MaxValue, .001f, int.MaxValue) == int.MaxValue
            && TacticalFurniturePolicy.WorkTicks(100, 0, 75) == int.MaxValue
            && TacticalFurniturePolicy.WorkTicks(100, float.NaN, 75) == int.MaxValue,
            "Invalid/overflowing work estimates must never become cheap targets.");
        Check(!TacticalFurniturePolicy.Useful(13, 13, 13, 13, false, 13)
            && TacticalFurniturePolicy.Useful(4, 7, 13, 13, false, 13)
            && TacticalFurniturePolicy.Useful(13, 13, 0, 6, true, 13)
            && !TacticalFurniturePolicy.Useful(0, 0, 13, 13, true, 13),
            "Only improved usable footprints or restored mouths justify destruction.");
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(50, 1, 50) };
        map.cellIndices = new CellIndices(50, 50); map.thingGrid = new ThingGrid(map);
        map.terrainGrid = (TerrainGrid)RuntimeHelpers.GetUninitializedObject(typeof(TerrainGrid));
        var terrain = (TerrainDef)RuntimeHelpers.GetUninitializedObject(typeof(TerrainDef));
        terrain.passability = Traversability.Standable;
        foreach (var field in typeof(TerrainGrid).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(f => f.FieldType == typeof(TerrainDef[]))) field.SetValue(map.terrainGrid, new TerrainDef[2500]);
        AccessTools.Field(typeof(TerrainGrid), "topGrid").SetValue(map.terrainGrid,
            Enumerable.Repeat(terrain, 2500).ToArray());
        AccessTools.Field(typeof(TerrainGrid), "map").SetValue(map.terrainGrid, map);
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.defName = "FurnitureFixture"; def.category = ThingCategory.Building;
        def.building = new BuildingProperties(); def.size = new IntVec2(3, 1);
        def.passability = Traversability.Impassable; def.destroyable = def.useHitPoints = true;
        var target = (Building)RuntimeHelpers.GetUninitializedObject(typeof(Building)); target.def = def;
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(target, new IntVec3(21, 0, 20));
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(target, (sbyte)-1);
        foreach (IntVec3 cell in target.OccupiedRect()) map.thingGrid.ThingsListAt(cell).Add(target);
        Check(TacticalFurniturePolicy.IsFurniture(target), "Destructible furniture is eligible; definition access uses native game classes.");
        def.building.isNaturalRock = true;
        Check(!TacticalFurniturePolicy.IsFurniture(target), "Natural rocks must not become furniture-clearing targets.");
        def.building.isNaturalRock = false;
        var door = (Building_Door)RuntimeHelpers.GetUninitializedObject(typeof(Building_Door)); door.def = def;
        Check(!TacticalFurniturePolicy.IsFurniture(door), "Door breaching must stay separate from furniture recovery.");
        var freeMethod = AccessTools.Method(typeof(TacticalFurniturePolicy), "FreeIgnoring");
        var walkMethod = AccessTools.Method(typeof(TacticalFurniturePolicy), "WalkIgnoring");
        bool Free(IntVec3 cell, Func<IntVec3, bool> claimed) => (bool)freeMethod.Invoke(null, new object[] { map, cell, claimed, target });
        bool Walk(IntVec3 cell) => (bool)walkMethod.Invoke(null, new object[] { map, cell, target });
        Func<IntVec3, bool> unclaimed = _ => false;
        foreach (IntVec3 cell in target.OccupiedRect())
            Check(Free(cell, unclaimed) && Walk(cell) && map.thingGrid.ThingsListAt(cell).Contains(target),
                "Virtual removal must free the whole multi-tile footprint without removing the actual building.");
        IntVec3 blocked = target.Position;
        Check(!Free(blocked, _ => true), "Foreign claims still block virtual standing posts.");
        terrain.passability = Traversability.Impassable;
        Check(!Free(blocked, unclaimed) && !Walk(blocked), "Removal must not turn impassable terrain into usable floor.");
        terrain.passability = Traversability.Standable;
        var other = (Thing)RuntimeHelpers.GetUninitializedObject(typeof(Thing));
        other.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); other.def.passability = Traversability.Impassable;
        map.thingGrid.ThingsListAt(blocked).Add(other);
        Check(!Free(blocked, unclaimed) && !Walk(blocked), "A second obstruction must survive simulated removal of the selected target.");
        map.thingGrid.ThingsListAt(blocked).Remove(other);

        // Three-tile bench cuts across a cramped approach. All stack filter
        // tiers fail to fit six until the real multi-cell footprint is cleared.
        var opening = new IntVec3(23, 0, 20); int side = 1;
        bool Floor(IntVec3 c) => c.x >= 20 && c.x <= 22 && c.z >= 20 && c.z <= 22;
        var posts = new List<IntVec3>();
        Check(!TacticalStackFormation.Build(opening, IntVec3.East, 6, ref side,
            c => Floor(c) && !target.OccupiedRect().Contains(c), _ => true, posts),
            "Furniture at the mouth must reproduce genuine approach failure after all placement tiers.");
        Check(TacticalStackFormation.Build(opening, IntVec3.East, 6, ref side,
            c => Floor(c) && (!target.OccupiedRect().Contains(c) || Free(c, unclaimed)), _ => true, posts)
            && posts.Count == 6 && TacticalLocalPlanner.Connected(posts),
            "Proven removal must produce unique connected posts, rather than a remote unused furniture target.");

        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Furniture-" + Guid.NewGuid() + ".xml");
        try
        {
            // Native deep-load cross references require a running game loader;
            // exercise value persistence here, alongside the existing plan test.
            var action = new TacticalFurnitureAction {
                Started = 123, WorkCell = opening - IntVec3.East, Footprint = target.OccupiedRect().ToList(),
                TemporaryClaims = new List<IntVec3> { opening } };
            Scribe.saver.InitSaving(path, "action"); action.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalFurnitureAction(); Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            Check(loaded.Started == 123 && loaded.WorkCell == action.WorkCell && loaded.Footprint.SequenceEqual(action.Footprint)
                && loaded.TemporaryClaims.SequenceEqual(action.TemporaryClaims), "Native Scribe must retain work deadline, footprint and temporary claims.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        var service = new MapComponent_TacticalCommands(null);
        var own = new TacticalSquadCommand { Phase = TacticalCommandPhase.Clear, FurnitureAction = new TacticalFurnitureAction() };
        var peer = new TacticalSquadCommand();
        var claims = (Dictionary<IntVec3, TacticalSquadCommand>)AccessTools.Field(service.GetType(), "claims").GetValue(service);
        IntVec3 work = opening, original = opening + IntVec3.North, foreign = opening + IntVec3.South;
        claims[work] = claims[original] = own; claims[foreign] = peer;
        own.FurnitureAction.TemporaryClaims.AddRange(new[] { work, foreign });
        Check((bool)AccessTools.Method(service.GetType(), "OriginalClaim").Invoke(null, new object[] { own, work }),
            "Contact restoration must preserve the engineer's temporary claim in Clear.");
        AccessTools.Method(service.GetType(), "ReleaseFurnitureClaims").Invoke(service, new object[] { own, own.FurnitureAction });
        Check(!claims.ContainsKey(work) && claims[original] == own && claims[foreign] == peer,
            "Furniture completion must remove only its additional claims, preserving old posts and foreign ownership.");
        var frontierPlan = new TacticalLocalPlan { Opening = opening, Inward = IntVec3.East };
        own.Phase = TacticalCommandPhase.Pending; own.PlanRetryAt = 400;
        own.FurnitureAction = new TacticalFurnitureAction { Plan = frontierPlan };
        var advance = AccessTools.Method(service.GetType(), "AdvanceFurniture");
        Check(!(bool)advance.Invoke(service, new object[] { own, new List<TacticalMemberCommand>(), 200 })
            && own.FurnitureAction == null && own.FurnitureResumePlan == frontierPlan
            && own.Phase == TacticalCommandPhase.Pending && own.PlanRetryAt == 0,
            "Completed initial clearing must resume its exact opening, without pretending the squad entered.");
        own.Phase = TacticalCommandPhase.Clear; own.FrontierCursor = 7; own.Plan = frontierPlan;
        own.FurnitureAction = new TacticalFurnitureAction { Plan = new TacticalLocalPlan() };
        Check(!(bool)advance.Invoke(service, new object[] { own, new List<TacticalMemberCommand>(), 220 })
            && own.Phase == TacticalCommandPhase.Clear && own.FrontierCursor == 7 && own.Plan == frontierPlan,
            "Completed room clearing must preserve the selected frontier and already secured plan.");
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(target, (sbyte)0);
        own.FurnitureAction = new TacticalFurnitureAction { Plan = frontierPlan, Target = target };
        Game previousGame = Current.Game;
        try
        {
            var fixtureGame = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            AccessTools.Field(typeof(Game), "maps").SetValue(fixtureGame, new List<Map> { map }); Current.Game = fixtureGame;
            Check((bool)advance.Invoke(service, new object[] { own, new List<TacticalMemberCommand>(), 240 })
                && own.FurnitureAction == null && own.RejectedFurniture.Count == 0,
                "Lost engineer must unblock the action without permanently blacklisting furniture another engineer could clear.");
        }
        finally { Current.Game = previousGame; AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(target, (sbyte)-1); }
        XElement jobs = XDocument.Load("Defs/Organization/NewTacticalJobs.xml").Root;
        Check(jobs.Elements().Single(d => (string)d.Element("defName") == TacticalFurniturePolicy.JobName)
            .Element("driverClass").Value == typeof(JobDriver_TacticalClearFurniture).FullName,
            "Furniture job definition must bind to the installed driver.");
        Console.WriteLine("PASS: " + checks + " quick furniture work, native multi-cell removal, terrain/claims, connected space recovery and save/JobDef checks.");
    }
}
