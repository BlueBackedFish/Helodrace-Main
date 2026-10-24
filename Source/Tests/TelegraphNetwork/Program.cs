using System;
using System.Linq;
using Helodrace;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI.Group;

internal static partial class Program
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
        CaravanContact();
        FinancialGoodwill();
        Loans();
        Arrears();
        Console.WriteLine("PASS: " + checks + " telegraph network checks.");
    }

    private static void FinancialGoodwill()
    {
        var game = new Game(); Current.Game = game;
        var ledger = new GameComponent_FinancialGoodwill(game); game.Component = ledger;
        var faction = new Faction { PlayerGoodwill = 20, NativeNaturalGoodwill = -10 };
        Check(ledger.Get(faction) == 0, "Financial reputation starts independently at zero");
        Check(ledger.RecordServicePayment(faction, 25) == 1.25f && ledger.Get(faction) == 1.25f,
            "Paid services award one financial point per 20 notes, preserving fractional payments");
        Check(ledger.RecordServicePayment(faction, 0) == 0f && ledger.RecordServicePayment(faction, -20) == 0f,
            "Zero or invalid payments cannot award financial reputation");
        Check(faction.PlayerGoodwill == 20, "Service payments do not change diplomatic goodwill");
        ledger.TrySet(faction, 99f);
        Check(ledger.RecordServicePayment(faction, 500) == 1f && ledger.Get(faction) == 100f,
            "Service rewards respect the financial cap");
        ledger.TrySet(faction, 0f);
        float state = 0;
        bool rewardResult = false;
        Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 11, HistoryEventDefOf.GaveGift, ref state, ref rewardResult);
        Check(state == 5.5f, "Gift credit is half the evaluated diplomatic award, retaining half-points");
        faction.TryAffectGoodwillWith(Faction.OfPlayer, 11, reason: HistoryEventDefOf.GaveGift);
        Patch_FinancialEconomicReward.Postfix(Faction.OfPlayer, faction, state);
        Check(faction.PlayerGoodwill == 31 && ledger.Get(faction) == 5.5f, "Gifts increase both vanilla goodwill and half-sized financial reputation");
        Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 3, HistoryEventDefOf.Traded, ref state, ref rewardResult);
        faction.TryAffectGoodwillWith(Faction.OfPlayer, 3, reason: HistoryEventDefOf.Traded);
        Patch_FinancialEconomicReward.Postfix(Faction.OfPlayer, faction, state);
        Check(faction.PlayerGoodwill == 34 && ledger.Get(faction) == 7f, "Trade goodwill stays vanilla and adds half its award to finance");
        TradeSession.trader = new Pawn { Faction = faction, Protected = true };
        bool runVanilla = Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 6, HistoryEventDefOf.Traded, ref state, ref rewardResult);
        if (runVanilla) faction.TryAffectGoodwillWith(Faction.OfPlayer, 6, reason: HistoryEventDefOf.Traded);
        Patch_FinancialEconomicReward.Postfix(Faction.OfPlayer, faction, state);
        Check(!runVanilla && rewardResult && faction.PlayerGoodwill == 34 && ledger.Get(faction) == 10f,
            "Safe-conduct trading adds only the normal half-sized financial reward and never diplomatic goodwill");
        Check(Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 2, HistoryEventDefOf.GaveGift, ref state, ref rewardResult),
            "Safe-conduct trade rules do not replace gift diplomacy");
        Check(Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, new Faction(), 2, HistoryEventDefOf.Traded, ref state, ref rewardResult),
            "A safe-conduct merchant cannot redirect another faction's diplomatic rewards");
        TradeSession.trader.Protected = false;
        Check(Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 2, HistoryEventDefOf.Traded, ref state, ref rewardResult),
            "Ordinary or breached caravans keep vanilla diplomatic trade rewards");
        TradeSession.trader = null;
        ledger.TrySet(faction, 7f);
        Check(faction.NaturalGoodwill == -10, "Finance does not shift the natural diplomatic baseline");
        Find.TickManager.TicksGame = 999999; ledger.GameComponentTick(); ledger.LoadedGame();
        Check(faction.PlayerGoodwill == 34, "Financial reputation grants no automatic diplomatic recovery");
        faction.PlayerGoodwill = 100;
        Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 2, HistoryEventDefOf.Traded, ref state, ref rewardResult);
        Patch_FinancialEconomicReward.Postfix(Faction.OfPlayer, faction, state);
        Check(ledger.Get(faction) == 8f && faction.PlayerGoodwill == 100, "The separate half reward can still accumulate at the diplomatic cap");
        foreach (var reason in new[] { (HistoryEventDef)null, new HistoryEventDef(), HistoryEventDefOf.ReachNaturalGoodwill })
        {
            Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 10, reason, ref state, ref rewardResult);
            Check(state == 0f, "Non-economic diplomacy cannot award financial reputation");
        }
        Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, -10, HistoryEventDefOf.Traded, ref state, ref rewardResult);
        Check(state == 0f, "Negative economic changes are not half-sized rewards");
        Patch_FinancialEconomicReward.Prefix(faction, new Faction(), 10, HistoryEventDefOf.GaveGift, ref state, ref rewardResult);
        Check(state == 0f, "NPC-to-NPC gifts are not player financial rewards");
        faction.CanChange = false;
        Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 10, HistoryEventDefOf.GaveGift, ref state, ref rewardResult);
        Check(state == 0f, "Vanilla restrictions also gate financial rewards"); faction.CanChange = true;
        Check(!ledger.TrySet(null, 10) && !ledger.TrySet(Faction.OfPlayer, 10), "Invalid and self relationships cannot be written");
        Check(!ledger.TrySet(faction, float.NaN) && !ledger.TrySet(faction, float.PositiveInfinity), "Non-finite values cannot corrupt financial balances");
        ledger.TrySet(faction, 99.5f);
        Check(ledger.Adjust(faction, 10) == 0.5f && ledger.Get(faction) == 100f, "Fractional rewards respect the upper limit");
        Check(ledger.Adjust(faction, 10) == 0f, "Financial cap cannot overflow");
        Check(ledger.TrySpend(faction, 20f) && ledger.Get(faction) == 80f, "Hostile caravan fees spend exactly the financial cost");
        Check(!ledger.TrySpend(faction, 81f) && !ledger.TrySpend(faction, -20f) && ledger.Get(faction) == 80f, "Rejected fees cannot partially spend or credit a balance");
        ledger.Adjust(faction, -500f);
        Check(ledger.Get(faction) == -100f, "Attack penalties respect the financial floor");
        ledger.TrySet(faction, 12.5f);
        Scribe.Values.Clear(); Scribe.Loading = false; ledger.ExposeData();
        var loaded = new GameComponent_FinancialGoodwill(game);
        Scribe.Loading = true; loaded.ExposeData(); Scribe.Loading = false;
        Check(loaded.Get(faction) == 12.5f, "Faction references and half-point balances survive save/load");
        Check(!Scribe.Values.ContainsKey("financialGoodwillNextRecovery"), "No obsolete recovery schedule is serialized");
        Check(FinancialGoodwillRules.CaravanAttackPenalty(10) == -13 && FinancialGoodwillRules.CaravanAttackPenalty(200) == -130,
            "Protected-caravan damage uses the vanilla damage-scaled goodwill penalty");
        Check(FinancialGoodwillRules.CaravanAttackPenalty(0) == 0, "Zero damage cannot deduct financial reputation");
        Current.Game = null;
        Patch_FinancialEconomicReward.Prefix(Faction.OfPlayer, faction, 10, HistoryEventDefOf.GaveGift, ref state, ref rewardResult);
        Check(state == 0f, "The economic reward hook remains safe outside a game");
    }

    private static void CaravanContact()
    {
        var s = Settlement();
        s.trader.trader.CanTradeNow = false;
        Check(MapComponent_TelegraphNetwork.IsInstallationTrader(s.trader, s.map),
            "Installation is available independently of temporary merchandise-trading state");
        Check(MapComponent_TelegraphNetwork.FindInstallationTrader(s.trader, s.map) == s.trader,
            "Clicking the merchant resolves the installation contact");
        var lord = new Lord();
        var escort = new Pawn { Map = s.map, Faction = s.trader.Faction, Lord = lord };
        lord.ownedPawns.Add(escort); lord.ownedPawns.Add(s.trader);
        Check(MapComponent_TelegraphNetwork.FindInstallationTrader(escort, s.map) == s.trader,
            "Clicking an escort resolves the same caravan's merchant");
        Check(MapComponent_TelegraphNetwork.FindInstallationTrader(escort, new Map()) == null,
            "Another map cannot use this caravan contact");
        var visitor = new Pawn { Map = s.map, Faction = s.trader.Faction };
        Check(MapComponent_TelegraphNetwork.FindInstallationTrader(visitor, s.map) == null,
            "Unrelated LOW visitors do not borrow another caravan's merchant");
        s.trader.trader.traderKind = null;
        Check(!MapComponent_TelegraphNetwork.IsInstallationTrader(s.trader, s.map)
            && MapComponent_TelegraphNetwork.FindInstallationTrader(escort, s.map) == null,
            "A pawn without a trader kind cannot represent a caravan");
        s.trader.trader.traderKind = new TraderKindDef();
        s.trader.Faction.Hostile = true;
        Check(MapComponent_TelegraphNetwork.FindInstallationTrader(escort, s.map) == null,
            "Hostile caravan contacts stay unavailable");
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
        var terminal = (CompTelegraphTable)s.map.listerThings.AllThings[0].Comp;
        terminal.IsWireless = true;
        Check(s.network.InstallationRejection(s.trader) != null,
            "A wireless radio alone does not invite redundant wired installation");
        terminal.IsWireless = false;
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
        Check(reloaded.Status() == TelegraphConnectionStatus.Online, "An installed line stays available for financially authorized hostile trade requests");
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
