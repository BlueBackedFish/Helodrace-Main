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
            Check(updates == 2 && (int)AccessTools.Property(scheduler.GetType(), "Pending").GetValue(scheduler) == 2,
                "Sleeping real units execute only twice in 50 ticks, instead of recurring every ten ticks.");
            scheduler.GameComponentTick();
            Check((long)AccessTools.Field(scheduler.GetType(), "Updates").GetValue(scheduler) == updates,
                "Calling the scheduler twice at one game tick cannot spend its allowance twice.");
            var states = (Dictionary<string, MapComponent_RaidTacticalExecution.ExecutionState>)
                AccessTools.Field(execution.GetType(), "states").GetValue(execution);
            var quiet = new MapComponent_RaidTacticalExecution.ExecutionState {
                UnitId = units[0].Id, Phase = RaidExecutionPhase.Assemble };
            AccessTools.Field(quiet.GetType(), "SharedOpeningWait").SetValue(quiet, true);
            states[units[0].Id] = quiet;
            var next = AccessTools.Method(execution.GetType(), "NextExecutionTick");
            Check((int)next.Invoke(execution, new[] { first, (object)49 }) == 229,
                "Shared-opening wait sleeps the actual ticket, not just its expensive review.");
            quiet.Phase = RaidExecutionPhase.CrossBreach;
            Check((int)next.Invoke(execution, new[] { first, (object)49 }) == 59,
                "Native crossing progress retains short tactical admission checks.");
            quiet.Phase = RaidExecutionPhase.Assemble; quiet.DefenseUntil = 600;
            Check((int)next.Invoke(execution, new[] { first, (object)49 }) == 59,
                "An accepted support/defense event restores active reaction cadence.");
            AccessTools.Field(quiet.GetType(), "VisibleEnemiesTick").SetValue(quiet, 49);
            for (int i = 0; i < 40; i++)
                AccessTools.Method(execution.GetType(), "WakeUnit").Invoke(execution, new object[] { units[0].Id, 49 });
            Check((int)AccessTools.Field(first.GetType(), "ScheduledAfter").GetValue(first) == 50
                && (int)AccessTools.Property(scheduler.GetType(), "Pending").GetValue(scheduler) == 2,
                "Forty events coalesce into one early callback without multiplying the queue.");
            Check((int)AccessTools.Field(quiet.GetType(), "VisibleEnemiesTick").GetValue(quiet) < 0,
                "A wake invalidates cached enemy validation instead of hiding new tactical information.");
            states.Clear();
            AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, 50);
            scheduler.GameComponentTick();
            Check((long)AccessTools.Field(scheduler.GetType(), "Updates").GetValue(scheduler) == updates + 1
                && (int)AccessTools.Field(second.GetType(), "ReviewAfter").GetValue(second) == 1000,
                "An event wakes only its unit on the next tick; unrelated units remain asleep.");
            // Exercise the actual opening-release event rather than calling WakeUnit alone.
            object leases = AccessTools.Field(execution.GetType(), "openingLeases").GetValue(execution);
            Type keyType = leases.GetType().GenericTypeArguments[0];
            object key = Activator.CreateInstance(keyType, true);
            AccessTools.Method(leases.GetType(), "Acquire").Invoke(leases, new object[] { units[0].Id, key, null });
            var queued = new MapComponent_RaidTacticalExecution.ExecutionState { UnitId = units[1].Id };
            AccessTools.Field(queued.GetType(), "SharedOpeningWait").SetValue(queued, true);
            states[units[1].Id] = queued;
            AccessTools.Method(execution.GetType(), "ReleaseOpeningLease").Invoke(execution, new object[] { units[0].Id, 50 });
            Check((int)AccessTools.Field(second.GetType(), "ScheduledAfter").GetValue(second) == 51,
                "Releasing the leading team's workspace wakes its queued successor without waiting for fallback polling.");
            states.Clear();
            AccessTools.Field(second.GetType(), "ReviewAfter").SetValue(second, 1000);
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
            AccessTools.Method(execution.GetType(), "ScoresFor").Invoke(execution,
                new object[] { new RaidTacticalPlan(), new IntVec3(8,0,20), 50 });
            var reactiveContexts = (System.Collections.IDictionary)AccessTools.Field(execution.GetType(), "reactiveScores").GetValue(execution);
            Check(reactiveContexts.Count == 1, "A map owns its actual reactive physical score cache.");
            execution.MapRemoved();
            Check(reactiveContexts.Count == 0, "Map removal releases the shared physical score cache.");
            Check((int)AccessTools.Property(scheduler.GetType(), "Pending").GetValue(scheduler) == 0,
                "Removing a map cancels all its recurring work and releases retained map references.");
            states["orphan-from-load"] = new MapComponent_RaidTacticalExecution.ExecutionState();
            AccessTools.Method(execution.GetType(), "ScoresFor").Invoke(execution,
                new object[] { new RaidTacticalPlan(), new IntVec3(8,0,20), 50 });
            AccessTools.Method(execution.GetType(), "RefreshExecutionRoster").Invoke(execution, new object[] { 50 });
            Check(states.Count == 0, "Roster reconciliation removes loaded orphan state even without an existing runtime ticket.");
            Check(reactiveContexts.Count == 0, "An empty execution roster drops old reactive physical scores.");
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
