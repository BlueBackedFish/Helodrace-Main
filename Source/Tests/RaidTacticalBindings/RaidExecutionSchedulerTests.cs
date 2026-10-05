using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Squads;
using Verse;

internal static class RaidExecutionSchedulerTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        Game previous = Current.Game;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        game.components = new List<GameComponent>();
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var registry = new GameComponent_CombatOrganizations(game);
        var scheduler = new GameComponent_RaidExecutionScheduler(game);
        game.components.Add(registry); game.components.Add(scheduler);
        var execution = new MapComponent_RaidTacticalExecution(map);
        var plans = new MapComponent_RaidTacticalPlans(map);
        AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent> { execution, plans });
        var organization = new CombatOrganization { id = "Scheduled", rootGroups = new List<CombatGroup> {
            new CombatGroup { id = "one" }, new CombatGroup { id = "two" } } };
        organization.RestoreTreeLinks();
        AccessTools.Field(registry.GetType(), "organizations").SetValue(registry, new List<CombatOrganization> { organization });
        Current.Game = game;
        try
        {
            var units = RaidTacticalUnit.ForOrganization(organization).ToList();
            object Register(int i) => AccessTools.Method(execution.GetType(), "RegisterUnit").Invoke(execution, new object[] { units[i], 0 });
            object first = Register(0), second = Register(1);
            Check(Register(0) == first && (int)AccessTools.Property(scheduler.GetType(), "Pending").GetValue(scheduler) == 2,
                "Roster reconciliation reuses one scheduled ticket per live unit instead of multiplying work.");
            foreach (object ticket in new[] { first, second })
            {
                AccessTools.Field(ticket.GetType(), "ReviewAfter").SetValue(ticket, 1000);
                AccessTools.Field(ticket.GetType(), "FallbackAfter").SetValue(ticket, 1000);
            }
            // Empty real units allow queue lifecycle validation without invoking Unity jobs.
            for (int tick = 0; tick < 50; tick++)
            {
                AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, tick);
                scheduler.GameComponentTick();
            }
            long updates = (long)AccessTools.Field(scheduler.GetType(), "Updates").GetValue(scheduler);
            Check(updates >= 2 && (int)AccessTools.Property(scheduler.GetType(), "Pending").GetValue(scheduler) == 2,
                "Recurring real execution work survives ticks and keeps both units scheduled.");
            scheduler.GameComponentTick();
            Check((long)AccessTools.Field(scheduler.GetType(), "Updates").GetValue(scheduler) == updates,
                "Calling the scheduler twice at one game tick cannot spend its allowance twice.");
            AccessTools.Field(execution.GetType(), "rosterAfter").SetValue(execution, 1000);
            var loss = new Pawn { thingIDNumber = 90201 };
            var pending = (Dictionary<string, HashSet<Pawn>>)AccessTools.Field(execution.GetType(), "pendingCasualties").GetValue(execution);
            pending[units[0].Id] = new HashSet<Pawn> { loss };
            execution.MapComponentTick();
            Check(pending.Count == 0 && (long)AccessTools.Field(scheduler.GetType(), "UrgentUpdates").GetValue(scheduler) == 1,
                "A casualty bypasses a distant regular deadline and is coalesced through the actual map callback.");
            Check((int)AccessTools.Field(first.GetType(), "ReviewAfter").GetValue(first) < 1000
                && (int)AccessTools.Field(second.GetType(), "ReviewAfter").GetValue(second) == 1000,
                "Urgent reconciliation changes only the affected unit's regular review deadline.");
            execution.MapRemoved();
            Check((int)AccessTools.Property(scheduler.GetType(), "Pending").GetValue(scheduler) == 0,
                "Removing a map cancels all its recurring work and releases retained map references.");
            var states = (Dictionary<string, MapComponent_RaidTacticalExecution.ExecutionState>)
                AccessTools.Field(execution.GetType(), "states").GetValue(execution);
            states["orphan-from-load"] = new MapComponent_RaidTacticalExecution.ExecutionState();
            AccessTools.Method(execution.GetType(), "RefreshExecutionRoster").Invoke(execution, new object[] { 50 });
            Check(states.Count == 0, "Roster reconciliation removes loaded orphan state even without an existing runtime ticket.");
            var orders = new MapComponent_RaidTacticalOrders(map);
            var directives = (Dictionary<Pawn, RaidPawnOrder>)AccessTools.Field(orders.GetType(), "orders").GetValue(orders);
            object reviews = AccessTools.Field(orders.GetType(), "reviews").GetValue(orders);
            var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            pawnDef.defName = "SchedulerFixturePawn"; pawnDef.race = new RaceProperties();
            for (int i = 0; i < 24; i++)
            {
                var pawn = new Pawn { thingIDNumber = 91000 + i, def = pawnDef };
                directives[pawn] = new RaidPawnOrder { Pawn = pawn };
                AccessTools.Method(reviews.GetType(), "Schedule").Invoke(reviews, new object[] { pawn, 0 });
            }
            orders.MapComponentTick();
            Check(directives.Count == 8 && (int)AccessTools.Property(reviews.GetType(), "Count").GetValue(reviews) == 8,
                "Real order reconciliation caps a burst of invalid/departed pawns and preserves the deferred tail.");
            AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, 50);
            orders.MapComponentTick();
            Check(directives.Count == 0 && (int)AccessTools.Property(reviews.GetType(), "Count").GetValue(reviews) == 0,
                "Deferred invalid orders are removed on subsequent ticks rather than leaked in the polling queue.");
        }
        finally { Current.Game = previous; }
        Console.WriteLine($"PASS: {checks} native execution deadline, recurring ticket, casualty bypass and map disposal checks.");
    }
}
