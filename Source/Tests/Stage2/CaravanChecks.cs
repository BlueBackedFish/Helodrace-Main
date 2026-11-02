using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.Tests.Stage2;

public sealed partial class Checks
{
    private readonly List<Pawn> visiting = new();
    private int Notes => map.listerThings.ThingsOfDef(Def<ThingDef>("HD_Money")).Sum(t => t.stackCount);
    private Thing SpawnItem(string name, int count, IntVec3 position)
    {
        Thing thing = ThingMaker.MakeThing(Def<ThingDef>(name));
        thing.stackCount = count;
        GenPlace.TryPlaceThing(thing, position, map, ThingPlaceMode.Near);
        thing.SetForbidden(false);
        return thing;
    }
    private Building Terminal(string name, int x)
    {
        ThingDef def = Def<ThingDef>(name);
        var building = (Building)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
        building.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(building, map.Center + new IntVec3(x, 0, -5), map);
        return building;
    }
    private Pawn CheckPetroleum(Lord lord, string route)
    {
        var pawns = lord.ownedPawns.ToList();
        Write("INVENTORY " + route + " " + string.Join(";", pawns.Select(p => p.kindDef.defName
            + " pack=" + p.RaceProps.packAnimal + " " + string.Join(",", p.inventory.innerContainer.Select(t => t.def.defName + "=" + t.stackCount)))));
        var merchant = pawns.Single(p => p.trader?.traderKind?.defName == "HD_WW_Caravan_HelodPetroleum");
        Check(pawns.Count(p => p.kindDef.defName is "HD_GW_HelodEliteCaravanGuard" or "HD_GW_HelodEliteCaravanGuardCaptain") >= 2,
            route + ": merchant has at least two authored elite escorts");
        Check(pawns.Any(p => p.RaceProps.packAnimal && p.inventory.innerContainer.Any(t => t.def.defName == "HD_Diesel")),
            route + ": pack animal carries real petroleum wares");
        Check(pawns.Count(p => p.kindDef.defName == "HD_GW_HelodEliteCaravanGuardCaptain") <= 1,
            route + ": captain limit remains one");
        visiting.AddRange(pawns);
        Write("CARAVAN " + route + " pawns=" + string.Join(",", pawns.Select(p => p.kindDef.defName)));
        return merchant;
    }
    private Pawn NewArrival(string kind, string route)
    {
        var prior = map.lordManager.lords.ToHashSet();
        IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category, map);
        parms.faction = Low;
        parms.points = 20f;
        parms.forced = true;
        parms.traderKind = kind == null ? null : Def<TraderKindDef>(kind);
        Check(IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms), route + ": actual arrival incident succeeds");
        Write("ARRIVAL " + route + " requestedPoints=20 resolvedPoints=" + parms.points);
        var lord = map.lordManager.lords.Single(l => !prior.Contains(l) && l.LordJob is LordJob_TradeWithColony);
        visiting.AddRange(lord.ownedPawns);
        return lord.ownedPawns.Single(p => p.trader != null);
    }
    private void BuyDiesel(Pawn merchant, string route)
    {
        Check(FinancialCaravanAccess.CanTradeWith(actor, Low, merchant.TraderKind, merchant).Accepted,
            route + ": actual merchant remains eligible for trade");
        TradeSession.SetupWith(merchant, actor, false);
        Tradeable diesel = TradeSession.deal.AllTradeables.First(t => t.ThingDef.defName == "HD_Diesel" && t.CountHeldBy(Transactor.Trader) > 0);
        int before = map.listerThings.ThingsOfDef(diesel.ThingDef).Sum(t => t.stackCount);
        diesel.AdjustTo(1);
        TradeSession.deal.UpdateCurrencyCount();
        Check(TradeSession.deal.TryExecute(out bool traded) && traded,
            route + ": actual trade deal purchases diesel");
        Check(map.listerThings.ThingsOfDef(diesel.ThingDef).Sum(t => t.stackCount) == before + 1,
            route + ": purchased diesel physically arrives without duplication");
        TradeSession.Close();
    }
    private IEnumerable<int> CaravanChecks()
    {
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -Low.PlayerGoodwill, false, false);
        // Ten low-point draws, in addition to the snapshot's three budgets.
        for (int i = 0; i < 10; i++)
        {
            var group = PawnGroupMakerUtility.GeneratePawns(Group(20f)).ToList();
            Check(group.Count(p => p.kindDef.defName.Contains("EliteCaravanGuard")) >= 2
                && group.Any(p => p.RaceProps.packAnimal), "Low-point petroleum generation includes escorts and a carrier: " + i);
        }
        Pawn installer = PawnGroupMakerUtility.GeneratePawns(Group(20f)).Single(p => p.trader != null);
        BodyType(installer);
        GenSpawn.Spawn(installer, map.Center + new IntVec3(2, 0, 0), map);
        Building wired = Terminal("HD_TelegraphTable", -6);
        var terminal = wired.TryGetComp<CompTelegraphTable>();
        wired.TryGetComp<CompRefuelable>().Refuel(10f);
        SpawnItem("HD_Money", 1000, actor.Position);
        SpawnItem("Silver", 10000, actor.Position);
        var network = map.GetComponent<MapComponent_TelegraphNetwork>();
        var ledger = GameComponent_FinancialGoodwill.Current;
        ledger.TrySet(Low, 0f);
        int beforeNotes = Notes;
        Check(network.TryRequestInstallation(installer, actor, out string reason), "Actual wired installation succeeds: " + reason);
        Check(Notes == beforeNotes - 250 && ledger.Get(Low) == 12.5f,
            "Actual installation consumes exactly 250 STL and awards exactly 12.5 finance");
        Check(!network.TryRequestInstallation(installer, actor, out _) && Notes == beforeNotes - 250,
            "Repeated actual installation cannot charge twice");
        installer.DeSpawn();
        // Advance only the service deadline; request and incident code remain real.
        AccessTools.Field(typeof(MapComponent_TelegraphNetwork), "readyTick").SetValue(network, Find.TickManager.TicksGame);
        while (network.LocalHour < 8 || network.LocalHour >= 18) yield return 2500;
        var companies = map.GetComponent<MapComponent_TelegraphCompanies>();
        var request = new[] { TelegraphCompany.ContinentalPetroleum };
        ledger.TrySet(Low, 100f);
        var prior = map.lordManager.lords.ToHashSet();
        Check(companies.TryRequestCaravans(actor, terminal, request, out reason), "Wired petroleum request succeeds: " + reason);
        var schedule = companies.Schedule(TelegraphCompany.ContinentalPetroleum);
        schedule.ArrivalTick = Find.TickManager.TicksGame;
        yield return 250;
        var wiredLord = map.lordManager.lords.Single(l => !prior.Contains(l) && l.LordJob is LordJob_TradeWithColony);
        Pawn wiredTrader = CheckPetroleum(wiredLord, "wired");
        BuyDiesel(wiredTrader, "wired");

        Building wireless = Terminal("HD_Radio", -9);
        wireless.TryGetComp<CompPowerTrader>().PowerOn = true;
        schedule.LastRequestTick = Find.TickManager.TicksGame - CompanyDispatchSchedule.CooldownTicks;
        prior = map.lordManager.lords.ToHashSet();
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -100 - Low.PlayerGoodwill, false, false);
        Check(Low.HostileTo(Faction.OfPlayer), "Wireless fixture has genuinely hostile diplomatic goodwill before requesting");
        Check(companies.TryRequestCaravans(actor, wireless.TryGetComp<CompTelegraphTable>(), request, out reason),
            "Hostile wireless petroleum request succeeds using financial authorization: " + reason);
        schedule.ArrivalTick = Find.TickManager.TicksGame;
        yield return 250;
        var radioLord = map.lordManager.lords.Single(l => !prior.Contains(l) && l.LordJob is LordJob_TradeWithColony);
        Pawn radioTrader = CheckPetroleum(radioLord, "wireless");
        Write($"SAFE CONDUCT job={radioLord.LordJob.GetType().FullName} protected={FinancialCaravanUtility.IsProtected(radioTrader)} hostile={Low.HostileTo(Faction.OfPlayer)} goodwill={Low.PlayerGoodwill} scheduled={schedule.SafeConduct}");
        Check(radioLord.LordJob is LordJob_FinancialCaravan && FinancialCaravanUtility.IsProtected(radioTrader),
            "Requested hostile caravan has scoped safe conduct");
        Check(Low.HostileTo(Faction.OfPlayer), "Requested hostile caravan leaves underlying diplomacy hostile");
        BuyDiesel(radioTrader, "wireless");
        Check(Low.HostileTo(Faction.OfPlayer), "Protected trade does not repair hostile diplomacy");
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -Low.PlayerGoodwill, false, false);
        Pawn randomTrader = null;
        for (int i = 0; i < 24 && randomTrader == null; i++)
        {
            Pawn candidate = NewArrival(null, "random-" + i);
            if (candidate.TraderKind.defName == "HD_WW_Caravan_HelodPetroleum") randomTrader = candidate;
        }
        Check(randomTrader != null, "Random trader-kind selection actually visits as petroleum");
        CheckPetroleum(randomTrader.GetLord(), "random petroleum");
        BuyDiesel(randomTrader, "random petroleum");
        ArmsTrade();
        // Observe native travel, visit and exit. No forced despawn or exit toil.
        int elapsed = 0;
        while (visiting.Any(p => p.Spawned) && elapsed < 180000)
        {
            yield return 1000;
            elapsed += 1000;
        }
        Check(visiting.All(p => !p.Spawned && !p.Dead), "All actual trade caravans leave the map alive through native return jobs");
        Write("CARAVAN all returned after " + elapsed + " ticks");
    }
    private void ArmsTrade()
    {
        Pawn merchant = NewArrival("HD_WW_Caravan_HelodArms", "arms");
        string[] names = { "Gun_Revolver", "Gun_Autopistol", "Gun_BoltActionRifle", "Gun_PumpShotgun" };
        foreach (string name in names.Append("HD_Gun_ColtSAA_Weapon")) SpawnItem(name, 1, actor.Position);
        var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
        silver.stackCount = 10000;
        merchant.inventory.innerContainer.TryAdd(silver);
        TradeSession.SetupWith(merchant, actor, false);
        Check(TradeSession.TradeCurrency == TradeCurrency.Silver, "Arms trading keeps the existing native silver settlement currency");
        Tradeable notes = TradeSession.deal.AllTradeables.First(t => t.ThingDef.defName == "HD_Money");
        Check(notes.GetPriceFor(TradeAction.PlayerBuys) == 5.25f && notes.GetPriceFor(TradeAction.PlayerSells) == 4.75f,
            "Sthaler exchange retains its 5.25/4.75 silver bid/ask prices");
        foreach (string name in names.Append("HD_Gun_ColtSAA_Weapon"))
        {
            Tradeable item = TradeSession.deal.AllTradeables.FirstOrDefault(t => t.ThingDef.defName == name && t.CountHeldBy(Transactor.Colony) > 0);
            Check(item != null && item.TraderWillTrade && item.GetPriceFor(TradeAction.PlayerSells) > 0f,
                "Arms merchant's actual sale list accepts " + name);
            item.AdjustTo(-1);
        }
        Check(!merchant.TraderKind.WillTrade(Def<ThingDef>("Gun_AssaultRifle")), "Civilian purchase rules do not include military assault rifles");
        Check(names.All(n => !merchant.Goods.Any(t => t.def.defName == n)), "Buy-only civilian guns were not added to sale stock");
        TradeSession.deal.UpdateCurrencyCount();
        Check(TradeSession.deal.TryExecute(out bool traded) && traded, "Actual arms transaction sells four Core civilian guns and a Helod gun");
        Check(names.All(n => !map.listerThings.ThingsOfDef(Def<ThingDef>(n)).Any()), "Sold civilian guns leave the colony inventory");
        TradeSession.Close();
    }
}
