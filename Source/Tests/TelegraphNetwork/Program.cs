using System;
using System.Linq;
using Helodrace;
using RimWorld;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        Check(TelegraphNetworkRules.MinimumInstallationTicks == 2 * 60000, "Installation cannot finish before two days");
        Check(TelegraphNetworkRules.MaximumInstallationTicks == 3 * 60000, "Installation finishes within three days");
        Check(TelegraphNetworkRules.IsProvider("HD_HelodCivilLowFaction"), "LOW is permitted");
        foreach (string faction in new[] { null, "HD_HelodCivilHighFaction", "HD_HelodPlayerColony", "OutlanderCivil", "HD_HelodOtherFaction" })
            Check(!TelegraphNetworkRules.IsProvider(faction), "Other factions cannot provide telegraph access");

        const int requestedAt = 60000;
        int readyAt = requestedAt + TelegraphNetworkRules.MinimumInstallationTicks;
        Check(TelegraphNetworkRules.Status(-1, 999999, 12, true, true) == TelegraphConnectionStatus.NotInstalled, "Construction and elapsed time alone never activate a terminal");
        Check(TelegraphNetworkRules.Status(readyAt, requestedAt, 12, true, true) == TelegraphConnectionStatus.Installing, "Paid request starts disconnected");
        Check(TelegraphNetworkRules.Status(readyAt, readyAt - 1, 12, true, true) == TelegraphConnectionStatus.Installing, "Still disconnected immediately before the deadline");
        Check(TelegraphNetworkRules.Status(readyAt, readyAt, 12, true, true) == TelegraphConnectionStatus.Online, "Connection opens exactly at its deadline");
        for (int hour = 0; hour < 24; hour++)
        {
            bool open = hour >= 8 && hour < 18;
            Check((TelegraphNetworkRules.Status(readyAt, readyAt + 60000, hour, true, true) == TelegraphConnectionStatus.Online) == open,
                "Correct opening and closing boundaries at local hour " + hour);
        }
        Check(TelegraphNetworkRules.Status(readyAt, readyAt, 12, false, true) == TelegraphConnectionStatus.ProviderUnavailable, "Lost or hostile provider prevents access");
        Check(TelegraphNetworkRules.Status(readyAt, readyAt, 12, true, false) == TelegraphConnectionStatus.NoPrimaryCells, "Empty terminal cannot discover stations");
        Check(TelegraphNetworkRules.Status(readyAt, readyAt, 20, true, false) == TelegraphConnectionStatus.RelayClosed, "Closed relay hides stations even without fuel");
        Check(TelegraphNetworkRules.Status(readyAt, readyAt + 60000, 8, true, true) == TelegraphConnectionStatus.Online, "The same installation reconnects the next morning");
        TransactionsAndPersistence();
        Console.WriteLine("PASS: " + checks + " telegraph network checks.");
    }

    private static (Map map, Pawn trader, Pawn actor, MapComponent_TelegraphNetwork network) Settlement()
    {
        var map = new Map();
        var faction = new Faction { def = new FactionDef { defName = TelegraphNetworkRules.ProviderDefName } };
        var trader = new Pawn { Map = map, Faction = faction, trader = new Pawn_TraderTracker() };
        var actor = new Pawn { Map = map, Faction = Faction.OfPlayer };
        map.listerThings.AllThings.Add(new Thing { Map = map, Faction = Faction.OfPlayer, Comp = new CompTelegraphTable() });
        return (map, trader, actor, new MapComponent_TelegraphNetwork(map));
    }

    private static Thing Notes(Map map, int count)
    {
        var notes = new Thing { Map = map, def = DefDatabase<ThingDef>.Value, stackCount = count };
        map.listerThings.AllThings.Add(notes);
        return notes;
    }

    private static int Balance(Map map) => map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.Value).Sum(t => t.stackCount);

    private static void TransactionsAndPersistence()
    {
        DefDatabase<ThingDef>.Value = new ThingDef { defName = "HD_Money" };
        Find.TickManager.TicksGame = 1000;
        var s = Settlement();
        Notes(s.map, 200);
        Check(!s.network.TryRequestInstallation(s.trader, s.actor, out _), "Insufficient funds reject installation");
        Check(!s.network.InstallationRequested && Balance(s.map) == 200, "Failed payment leaves state and balance unchanged");
        Notes(s.map, 400);
        var forbidden = Notes(s.map, 1000); forbidden.Forbidden = true;
        var unreachable = Notes(s.map, 1000); unreachable.Accessible = false;
        var fogged = Notes(s.map, 1000); fogged.Position.IsFogged = true;
        Check(s.network.TryRequestInstallation(s.trader, s.actor, out _), "Accessible notes from multiple stacks fund installation");
        Check(Balance(s.map) == 3100, "Payment removes exactly 500 notes");
        Check(forbidden.stackCount == 1000 && unreachable.stackCount == 1000 && fogged.stackCount == 1000,
            "Forbidden, unreachable, and fogged money stays untouched");
        Check(s.network.Provider == s.trader.Faction, "The actual LOW trader faction owns the connection");
        Check(!s.network.TryRequestInstallation(s.trader, s.actor, out _), "Duplicate pending request rejected");
        Check(Balance(s.map) == 3100 && s.network.TicksUntilReady == 120000, "Duplicate request neither charges nor reschedules installation");

        Scribe.Values.Clear(); Scribe.Loading = false;
        s.network.ExposeData();
        var loaded = new MapComponent_TelegraphNetwork(s.map);
        Scribe.Loading = true; loaded.ExposeData(); Scribe.Loading = false;
        Check(loaded.InstallationRequested && loaded.Provider == s.trader.Faction, "Save/load preserves request and provider");
        Check(loaded.TicksUntilReady == 120000, "Save/load preserves exact scheduled completion");
        Find.TickManager.TicksGame = 120999;
        loaded.MapComponentTick();
        Check(loaded.Status() == TelegraphConnectionStatus.Installing && Find.LetterStack.Count == 0, "No premature activation or notification");
        Find.TickManager.TicksGame++;
        loaded.MapComponentTick(); loaded.MapComponentTick();
        Check(loaded.Status() == TelegraphConnectionStatus.Online && Find.LetterStack.Count == 1, "Deadline activates and notifies exactly once");
        Check(!loaded.TryRequestInstallation(s.trader, s.actor, out _) && Balance(s.map) == 3100, "Active installation cannot be purchased again");
        loaded.ExposeData();
        var reloaded = new MapComponent_TelegraphNetwork(s.map);
        Scribe.Loading = true; reloaded.ExposeData(); Scribe.Loading = false;
        reloaded.MapComponentTick();
        Check(Find.LetterStack.Count == 1, "Reloading an activated line does not duplicate its letter");
        GenDate.LocalHour = 18;
        Check(reloaded.Status() == TelegraphConnectionStatus.RelayClosed, "Connected line goes offline at closing time");
        GenDate.LocalHour = 8;
        Check(reloaded.Status() == TelegraphConnectionStatus.Online, "Same connection reopens without another installation fee");
        s.trader.Faction.Hostile = true;
        Check(reloaded.Status() == TelegraphConnectionStatus.ProviderUnavailable, "Hostile LOW provider blocks an installed connection");
        s.trader.Faction.Hostile = false;

        var other = Settlement(); Notes(other.map, 500);
        Check(!other.network.InstallationRequested, "Another settlement does not inherit an installed line");
        other.trader.Faction.def.defName = "HD_HelodCivilHighFaction";
        Check(!other.network.TryRequestInstallation(other.trader, other.actor, out _) && Balance(other.map) == 500,
            "HIGH caravan cannot install or collect payment");
        other.trader.Faction.def.defName = TelegraphNetworkRules.ProviderDefName;
        other.trader.Spawned = false;
        Check(!other.network.TryRequestInstallation(other.trader, other.actor, out _), "Departed trader invalidates a pending confirmation");
        other.trader.Spawned = true;
        other.actor.Downed = true;
        Check(!other.network.TryRequestInstallation(other.trader, other.actor, out _) && Balance(other.map) == 500,
            "Incapacitated negotiator cannot pay from a stale confirmation");
        other.actor.Downed = false;
        other.map.listerThings.AllThings[0].Faction = other.trader.Faction;
        Check(!other.network.TryRequestInstallation(other.trader, other.actor, out _) && Balance(other.map) == 500,
            "Unowned terminal cannot qualify for installation");
        other.map.listerThings.AllThings[0].Faction = Faction.OfPlayer;
        Rand.UseMaximum = true;
        Check(other.network.TryRequestInstallation(other.trader, other.actor, out _) && Balance(other.map) == 0,
            "Exact funds can pay for an installation");
        Check(other.network.TicksUntilReady == 180000, "Maximum random delay is exactly three days");
        Rand.UseMaximum = false;

        var blocked = Settlement();
        Notes(blocked.map, 499);
        Notes(blocked.map, 1000).Forbidden = true;
        Notes(blocked.map, 1000).Accessible = false;
        Notes(blocked.map, 1000).Position.IsFogged = true;
        Check(!blocked.network.TryRequestInstallation(blocked.trader, blocked.actor, out _), "Unavailable notes cannot make up an insufficient accessible balance");
        Check(!blocked.network.InstallationRequested && Balance(blocked.map) == 3499, "Unavailable-funds rejection consumes nothing");
        blocked.map.listerThings.AllThings.RemoveAt(0);
        Notes(blocked.map, 500);
        Check(!blocked.network.TryRequestInstallation(blocked.trader, blocked.actor, out _), "Installation requires a constructed terminal");
        Check(!blocked.network.InstallationRequested && Balance(blocked.map) == 3999, "Missing terminal never charges a fee");
    }
}
