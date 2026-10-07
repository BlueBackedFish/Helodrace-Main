using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Squads;
using Verse;
using Verse.AI;

internal static class RaidOrderLookupTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        Game previous = Current.Game;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var otherMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent>());
        AccessTools.Field(typeof(Map), "components").SetValue(otherMap, new List<MapComponent>());
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map, otherMap });
        var pawn = new Pawn { thingIDNumber = 51001 };
        string unitId = RaidTacticalUnitTests.BindPawn(game, pawn, "Lookup");
        var first = new MapComponent_RaidTacticalOrders(map);
        var second = new MapComponent_RaidTacticalOrders(otherMap);
        pawn.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        pawn.mindState = (Pawn_MindState)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_MindState));
        pawn.mindState.mentalStateHandler = (MentalStateHandler)RuntimeHelpers.GetUninitializedObject(typeof(MentalStateHandler));
        var health = AccessTools.Field(typeof(Pawn_HealthTracker), "healthState");
        var mapIndex = AccessTools.Field(typeof(Thing), "mapIndexOrState");
        var tick = AccessTools.Field(typeof(TickManager), "ticksGameInt");
        var bind = AccessTools.Method(typeof(MapComponent_RaidTacticalOrders), "Bind");
        var mark = AccessTools.Method(typeof(MapComponent_RaidTacticalOrders), "Validated");
        var lookup = AccessTools.Method(typeof(MapComponent_RaidTacticalOrders), "For");
        var forget = AccessTools.Method(typeof(MapComponent_RaidTacticalOrders), "ForgetPawn");
        Dictionary<Pawn, RaidPawnOrder> Orders(MapComponent_RaidTacticalOrders owner) =>
            (Dictionary<Pawn, RaidPawnOrder>)AccessTools.Field(owner.GetType(), "orders").GetValue(owner);
        RaidPawnOrder Read() => (RaidPawnOrder)lookup.Invoke(null, new object[] { pawn });
        void Attach(MapComponent_RaidTacticalOrders owner, RaidPawnOrder order)
        {
            Orders(owner)[pawn] = order;
            bind.Invoke(owner, new object[] { order });
            mark.Invoke(owner, new object[] { order, RaidTacticalUnit.ForPawn(pawn), true, 100 });
        }
        RaidPawnOrder Directive() => new RaidPawnOrder { Pawn = pawn, OrganizationId = "Tests", UnitId = unitId, GroupId = "Lookup" };
        Current.Game = game;
        try
        {
            tick.SetValue(game.tickManager, 100); mapIndex.SetValue(pawn, (sbyte)0);
            health.SetValue(pawn.health, PawnHealthState.Mobile);
            Check(Read() == null, "Unregistered pawns must return an immediate negative lookup.");
            var order = Directive(); Attach(first, order);
            Check(Read() == order && Read() == order, "Repeated reads must use the already validated bound directive.");
            health.SetValue(pawn.health, PawnHealthState.Down);
            Check(Read() == null, "Downed actors cannot use a cached positive verdict.");
            health.SetValue(pawn.health, PawnHealthState.Dead);
            Check(Read() == null, "Dead actors cannot use a cached positive verdict.");
            health.SetValue(pawn.health, PawnHealthState.Mobile);
            mapIndex.SetValue(pawn, (sbyte)-1);
            Check(Read() == null, "Off-map actors cannot expose cached orders.");
            mapIndex.SetValue(pawn, (sbyte)1);
            Check(Read() == null, "A directive is never readable on another map.");
            mapIndex.SetValue(pawn, (sbyte)0);
            order.UnitId = "WrongUnit";
            tick.SetValue(game.tickManager, 190);
            Check(Read() == null, "The fallback deadline must perform real ownership validation.");
            order.UnitId = unitId;
            Check(Read() == null, "A cached negative verdict must also be reusable until refreshed.");
            mark.Invoke(first, new object[] { order, RaidTacticalUnit.ForPawn(pawn), true, 190 });
            Check(Read() == order, "A validated command write must replace a previous negative verdict immediately.");
            var replacement = Directive(); Attach(first, replacement);
            Check(Read() == null, "An expired replacement must not inherit the old directive's verdict.");
            tick.SetValue(game.tickManager, 100); Attach(first, replacement);
            Check(Read() == replacement && Orders(first)[pawn] == replacement,
                "Rebinding the same actor must retain the replacement in its owner's dictionary.");
            RaidTacticalUnit.ForPawn(pawn).Organization.RestoreTreeLinks();
            Check(Read() == null, "Organization tree changes must invalidate a positive verdict before its deadline.");
            Attach(first, replacement);
            var saved = AccessTools.Field(first.GetType(), "saved");
            LoadSaveMode previousMode = Scribe.mode;
            try
            {
                saved.SetValue(first, new List<RaidPawnOrder> { replacement });
                Scribe.mode = LoadSaveMode.PostLoadInit;
                first.ExposeData();
            }
            finally { Scribe.mode = previousMode; }
            Check(Orders(first)[pawn] == replacement && Read() == null,
                "Loaded directives must restore their actor binding without carrying a runtime control verdict.");
            Attach(second, replacement); mapIndex.SetValue(pawn, (sbyte)1);
            AccessTools.Method(first.GetType(), "Forget").Invoke(first, new object[] { pawn });
            Check(Read() == replacement && !Orders(first).ContainsKey(pawn),
                "Old-map cleanup must not remove the new map's binding.");
            second.MapRemoved();
            Check(Read() == null && Orders(second).Count == 0, "Removing a map must release its actor bindings.");
            mapIndex.SetValue(pawn, (sbyte)0); Attach(first, replacement);
            OrganizationAPI.Registry.Detach(pawn);
            Check(Read() == null && Orders(first).Count == 0,
                "Organization detachment must release commands immediately, without waiting for the next review.");
        }
        finally
        {
            forget.Invoke(null, new object[] { pawn });
            Current.Game = previous;
        }
        Console.WriteLine($"PASS: {checks} cached order lookup, deadline, actor availability, rebinding and cleanup checks");
    }
}
