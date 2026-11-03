using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace.Tests.TradeStageH;

public sealed partial class Checks
{
    private const TelegraphCompany Hardware = TelegraphCompany.CentralHardwareSupply;
    private MapComponent_TelegraphCompanies Companies => map.GetComponent<MapComponent_TelegraphCompanies>();
    private CompTelegraphTable Terminal => map.listerThings.ThingsOfDef(Def<ThingDef>("HD_TelegraphTable")).Single().TryGetComp<CompTelegraphTable>();
    private void Save(int next) { stage = next; GameDataSaveLoader.SaveGame("TradeStageH"); load = true; }
    private IEnumerable<int> StartCommitments()
    {
        var building = (Building)ThingMaker.MakeThing(Def<ThingDef>("HD_TelegraphTable"), ThingDefOf.WoodLog); building.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(building, map.Center + new IntVec3(-8, 0, -4), map); building.TryGetComp<CompRefuelable>().Refuel(20);
        Item("HD_Money", 1000); Item("Silver", 50000);
        var network = map.GetComponent<MapComponent_TelegraphNetwork>();
        Pawn installer = map.lordManager.lords.SelectMany(l => l.ownedPawns).First(p => p.trader != null && p.Spawned);
        Check(network.TryRequestInstallation(installer, actor, out string reason), "Real wired network installation: " + reason);
        AccessTools.Field(typeof(MapComponent_TelegraphNetwork), "readyTick").SetValue(network, Find.TickManager.TicksGame);
        while (network.LocalHour < 8 || network.LocalHour >= 18) yield return 2500;
        map.wealthWatcher.ForceRecount();
        var ledger = GameComponent_FinancialGoodwill.Current; ledger.TrySet(Low, 0);
        Check(!Companies.TryRequestCaravans(actor, Terminal, new[] { Hardware }, out _), "Zero finance cannot use standard request");
        Check(Companies.CommitmentFailure(Terminal, TelegraphCompany.NationalIndustrialCredit) != null, "Currency-only finance company cannot use goods commitment");
        Check(Companies.TryRequestCommittedCaravan(actor, Terminal, Hardware, out reason), "Low-finance wealthy colony can request real contract: " + reason);
        Check(ledger.Get(Low) == 0 && !Companies.TryRequestCommittedCaravan(actor, Terminal, Hardware, out _), "No upfront finance charge and no duplicate contract");
        Save(3); yield return 1;
    }
    private void Ready(TelegraphCompany company)
    {
        var s = Companies.Schedule(company); s.LastRequestTick = Find.TickManager.TicksGame - CompanyDispatchSchedule.CooldownTicks;
    }
    private IEnumerable<int> Deliver(TelegraphCompany company)
    {
        Companies.Schedule(company).ArrivalTick = Find.TickManager.TicksGame;
        yield return 250;
        Check(Companies.ActiveCommitment(company)?.Merchant != null, "Contract bound to actual arriving merchant: " + company);
    }
    private float BuyGoods(TradeCommitment c, int count)
    {
        TradeSession.SetupWith(c.Merchant, actor, false);
        var item = TradeSession.deal.AllTradeables.Where(t => t.ThingDef != null && t.ThingDef != ThingDefOf.Silver && t.ThingDef.defName != "HD_Money" && t.CountHeldBy(Transactor.Trader) > 0)
            .OrderByDescending(t => t.ThingDef == ThingDefOf.Steel).ThenByDescending(t => t.GetPriceFor(TradeAction.PlayerBuys)).First();
        count = Math.Min(count, item.CountHeldBy(Transactor.Trader));
        item.AdjustTo(count); float value = count * item.GetPriceFor(TradeAction.PlayerBuys);
        TradeSession.deal.UpdateCurrencyCount(); Check(TradeSession.deal.TryExecute(out bool traded) && traded, "Contract real goods purchase executes"); TradeSession.Close(); return value;
    }
    private IEnumerable<int> ResumeCommitments()
    {
        var ledger = GameComponent_FinancialGoodwill.Current;
        if (stage == 3)
        {
            var pending = Companies.ActiveCommitment(Hardware);
            Check(pending != null && !pending.Arrived && Companies.Schedule(Hardware).Pending, "Pending contract/provider/request survive save/load");
            foreach (int n in Deliver(Hardware)) yield return n;
            var c = Companies.ActiveCommitment(Hardware); float value = BuyGoods(c, 5) + BuyGoods(c, 7);
            Check(Math.Abs(c.Turnover - value) < .01f, "Split trades accumulate actual gross goods prices");
            TradeSession.SetupWith(c.Merchant, actor, false);
            var notes = TradeSession.deal.AllTradeables.First(t => t.ThingDef.defName == "HD_Money" && t.CountHeldBy(Transactor.Trader) > 0); notes.AdjustTo(1);
            TradeSession.deal.UpdateCurrencyCount(); Check(TradeSession.deal.TryExecute(out bool traded) && traded, "Actual currency exchange executes"); TradeSession.Close();
            Check(Math.Abs(c.Turnover - value) < .01f, "Sthaler/silver exchange contributes zero goods turnover");
            TradeSession.SetupWith(c.Merchant, actor, true);
            var silver = TradeSession.deal.AllTradeables.First(t => t.ThingDef == ThingDefOf.Silver && t.CountHeldBy(Transactor.Colony) > 0); silver.AdjustTo(100);
            Check(TradeSession.deal.TryExecute(out _), "Actual gift executes"); TradeSession.Close();
            Check(Math.Abs(c.Turnover - value) < .01f, "Gifts do not satisfy contract");
            Save(4); yield return 1; yield break;
        }
        var loaded = Companies.ActiveCommitment(Hardware);
        Check(loaded is { Arrived: true, Turnover: > 0 } && loaded.Merchant.Spawned && loaded.CaravanLord == loaded.Merchant.GetLord(), "Arrived contract, exact cohort and accumulated trade survive save/load");
        float oldTurnover = loaded.Turnover;
        while (loaded.Turnover < loaded.Minimum) BuyGoods(loaded, 100);
        Check(loaded.Turnover > oldTurnover && !loaded.Closed, "Actual split purchases satisfy minimum before departure");
        int wait = 0;
        while (!loaded.Closed && wait < 120000) { yield return 1000; wait += 1000; }
        Check(loaded.Closed && loaded.Penalty == 0 && loaded.Outcome == "HD_Commitment_Fulfilled", "Native normal departure settles fulfilled contract with no penalty");
        var network = map.GetComponent<MapComponent_TelegraphNetwork>();
        while (network.LocalHour < 8 || network.LocalHour >= 18) yield return 2500;
        Ready(Hardware); ledger.TrySet(Low, 0);
        Check(Companies.TryRequestCommittedCaravan(actor, Terminal, Hardware, out string reason), "Second contract allowed after completed cooldown: " + reason);
        foreach (int n in Deliver(Hardware)) yield return n;
        var partial = Companies.ActiveCommitment(Hardware); BuyGoods(partial, 3); float before = ledger.Get(Low);
        // Native player dismissal transition, after a real partial purchase.
        partial.Merchant.mindState.traderDismissed = true;
        yield return 500;
        float expected = (float)Math.Round(10 * (1 - partial.Turnover / partial.Minimum), 2);
        Check(partial.Closed && Math.Abs(partial.Penalty - expected) < .01 && Math.Abs(ledger.Get(Low) - (before - expected)) < .01, "Native dismissal applies proportional shortfall penalty exactly once");
        before = ledger.Get(Low); partial.Settle(true); yield return 500;
        Check(ledger.Get(Low) == before, "Repeated departure cannot charge twice");
        Ready(Hardware);
        Check(Companies.TryRequestCommittedCaravan(actor, Terminal, Hardware, out reason), "Pending cancellation fixture request");
        var cancelled = Companies.ActiveCommitment(Hardware); before = ledger.Get(Low);
        Check(Companies.CancelCommitment(Hardware) && cancelled.Closed && ledger.Get(Low) == Math.Max(-100, before - 10), "Player cancellation settles once and removes pending arrival");
        Check(!Companies.CancelCommitment(Hardware) && !Companies.Schedule(Hardware).Pending, "Cancelled request cannot reappear or charge again");
        Ready(Hardware);
        Check(Companies.TryRequestCommittedCaravan(actor, Terminal, Hardware, out reason), "Nonarrival fixture request");
        var failed = Companies.ActiveCommitment(Hardware); before = ledger.Get(Low);
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -100 - Low.PlayerGoodwill, false, false);
        Companies.Schedule(Hardware).ArrivalTick = Find.TickManager.TicksGame; yield return 250;
        Check(failed.Closed && failed.Penalty == 0 && ledger.Get(Low) == before, "Hostile/nonarrival cancellation waives financial penalty");
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -Low.PlayerGoodwill, false, false);
        Ready(Hardware); Check(Companies.TryRequestCommittedCaravan(actor, Terminal, Hardware, out reason), "External interruption fixture request");
        foreach (int n in Deliver(Hardware)) yield return n;
        var external = Companies.ActiveCommitment(Hardware); before = ledger.Get(Low);
        external.CaravanLord.Notify_PawnDamaged(external.Merchant, new DamageInfo(DamageDefOf.Bullet, 1, instigator: null));
        Check(external.Closed && external.Penalty == 0 && ledger.Get(Low) == before, "External violence waives unfinished contract");
        Ready(Hardware); Check(Companies.TryRequestCommittedCaravan(actor, Terminal, Hardware, out reason), "Player attack fixture request");
        foreach (int n in Deliver(Hardware)) yield return n;
        var attacked = Companies.ActiveCommitment(Hardware); before = ledger.Get(Low);
        attacked.CaravanLord.Notify_PawnDamaged(attacked.Merchant, new DamageInfo(DamageDefOf.Bullet, 1, instigator: actor));
        Check(attacked.Closed && attacked.Penalty == 10 && ledger.Get(Low) == Math.Max(-100, before - 10), "Player violence defaults contract once");
        Ready(Hardware); ledger.TrySet(Low, 100);
        Check(Companies.TryRequestCaravans(actor, Terminal, new[] { Hardware }, out reason) && Companies.ActiveCommitment(Hardware) == null, "Standard financial route creates no commitment");
    }
}
