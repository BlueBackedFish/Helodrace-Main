using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Helodrace.Economy;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Helodrace.Tests.TradeStageH;

public sealed partial class Checks : GameComponent
{
    private bool started, complete, load;
    private int stage, remaining, visits;
    private string output;
    private Map map;
    private Pawn actor;
    private IEnumerator<int> steps;
    private static Map climateMap;
    private static bool patched;
    private static bool entryBlocked;
    private static bool Entry(ref bool __result) { if (!entryBlocked) return true; __result = false; return false; }
    public Checks(Game game) { }
    private static T Def<T>(string name) where T : Verse.Def => DefDatabase<T>.GetNamed(name);
    private static Faction Low => Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == "HD_HelodCivilLowFaction");
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Write("PASS: " + text); }
    private static bool Climate(MapTemperature __instance, ref float __result)
    {
        if (climateMap == null || AccessTools.Field(typeof(MapTemperature), "map").GetValue(__instance) != climateMap) return true;
        __result = 21; return false;
    }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref stage, "tradeStageH", 0);
        Scribe_Values.Look(ref visits, "starterVisits", 0);
        Scribe_References.Look(ref actor, "tradeActor");
    }
    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdTradeStageH", out output)) return;
        started = true;
        try
        {
            map = Find.CurrentMap; climateMap = map;
            if (!patched)
            {
                new Harmony("helod.tests.trade.climate").Patch(AccessTools.PropertyGetter(typeof(MapTemperature), nameof(MapTemperature.OutdoorTemp)), prefix: new HarmonyMethod(typeof(Checks), nameof(Climate)));
                new Harmony("helod.tests.trade.entry").Patch(AccessTools.Method(typeof(RCellFinder), nameof(RCellFinder.TryFindRandomPawnEntryCell)), prefix: new HarmonyMethod(typeof(Checks), nameof(Entry)));
                patched = true;
            }
            Find.TickManager.Pause(); DebugSettings.enableStoryteller = false;
            DebugSettings.noAnimals = false; DebugSettings.enableRandomMentalStates = false;
            if (stage > 0) { steps = Resume().GetEnumerator(); return; }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 H trade checks\n");
            foreach (Corpse corpse in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(corpse.InnerPawn);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                Body(p);
                if (p.Faction != Faction.OfPlayer) p.Destroy(DestroyMode.Vanish);
                else if (p.drafter != null) p.drafter.Drafted = true;
            }
            foreach (IntVec3 cell in CellRect.CenteredOn(map.Center, 20))
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing is not Pawn && thing.Spawned && thing.def.destroyable) thing.Destroy();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null);
                map.fogGrid.Unfog(cell); map.areaManager.Home[cell] = true;
            }
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25);
            req.ForceNoBackstory = true; actor = PawnGenerator.GeneratePawn(req); Body(actor);
            GenSpawn.Spawn(actor, map.Center, map); actor.drafter.Drafted = true;
            actor.skills.GetSkill(SkillDefOf.Social).Level = 15;
            Low.TryAffectGoodwillWith(Faction.OfPlayer, -Low.PlayerGoodwill, false, false);
            steps = Run().GetEnumerator();
        }
        catch (Exception e) { Fail(e); }
    }
    public override void GameComponentUpdate()
    {
        if (complete || steps == null) return;
        Find.TickManager.Pause();
        try
        {
            if (remaining == 0)
            {
                if (!steps.MoveNext()) { complete = true; Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); return; }
                if (load) { steps = null; GameDataSaveLoader.LoadGame("TradeStageH"); return; }
                remaining = steps.Current;
            }
            int n = Math.Min(remaining, 200);
            for (int i = 0; i < n; i++) Find.TickManager.DoSingleTick();
            remaining -= n;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                if (p.needs != null) foreach (Need need in p.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        }
        catch (Exception e) { Fail(e); }
    }
    private void Fail(Exception e) { complete = true; Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }
    private Thing Item(string name, int count, bool inventory = false)
    {
        ThingDef def = Def<ThingDef>(name); Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Cloth : null); t.stackCount = count;
        if (inventory) { actor.inventory.innerContainer.TryAdd(t); actor.GetCaravan()?.RecacheInventory(); }
        else { GenPlace.TryPlaceThing(t, actor.Position, map, ThingPlaceMode.Near); t.SetForbidden(false); }
        return t;
    }
    private int StarterVisits => map.lordManager.lords.Count(l => l.ownedPawns.Any(p => p.TraderKind?.defName == GameComponent_StarterSupplyCaravan.TraderDefName));
    private Pawn Arrive(string kind)
    {
        var old = map.lordManager.lords.ToHashSet();
        var parms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category, map);
        parms.faction = Low; parms.traderKind = kind == null ? null : Def<TraderKindDef>(kind); parms.points = 120; parms.forced = true;
        Check(IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms), "Real arrival: " + (kind ?? "random"));
        return map.lordManager.lords.Single(l => !old.Contains(l) && l.LordJob is LordJob_TradeWithColony).ownedPawns.Single(p => p.trader != null);
    }
    private void Trade(ITrader trader, bool inventory = false, bool startingBudget = false)
    {
        if (!startingBudget) { Item("Silver", 10000, inventory); Item("HD_Charcoal", 10, inventory); }
        TradeSession.SetupWith(trader, actor, false);
        Check(TradeSession.TradeCurrency == TradeCurrency.Silver, trader.TraderKind.defName + ": native silver currency");
        Tradeable steel = TradeSession.deal.AllTradeables.First(t => t.ThingDef == ThingDefOf.Steel && t.CountHeldBy(Transactor.Trader) > 0);
        steel.AdjustTo(startingBudget ? 100 : 1);
        if (startingBudget)
        {
            Tradeable notes = TradeSession.deal.AllTradeables.First(t => t.ThingDef.defName == "HD_Money" && t.CountHeldBy(Transactor.Colony) > 0);
            notes.AdjustTo(-100);
            foreach (string part in new[] { "HD_PigIron", "HD_CarbonSteel", "HD_GeneralMachinePart", "HD_PressComponent", "HD_Shaft", "HD_UniformRolledSteelPlate" })
                TradeSession.deal.AllTradeables.First(t => t.ThingDef.defName == part).AdjustTo(2);
        }
        else TradeSession.deal.AllTradeables.First(t => t.ThingDef.defName == "HD_Charcoal" && t.CountHeldBy(Transactor.Colony) > 0).AdjustTo(-1);
        TradeSession.deal.UpdateCurrencyCount();
        Check(TradeSession.deal.CurrencyTradeable.CountPostDealFor(Transactor.Colony) >= 0, "Chosen real basket is affordable " + trader.TraderKind.defName);
        int before = inventory ? actor.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => t.stackCount) : map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t => t.stackCount);
        Check(TradeSession.deal.TryExecute(out bool traded) && traded, "Real buy/sell deal executes " + trader.TraderKind.defName);
        int after = inventory ? actor.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => t.stackCount) : map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t => t.stackCount);
        Check(after == before + (startingBudget ? 100 : 1), "Physical steel delivered exactly once"); TradeSession.Close();
    }
    private IEnumerable<int> Run()
    {
        var starter = Current.Game.GetComponent<GameComponent_StarterSupplyCaravan>();
        Check(!starter.Armed, "Ordinary quicktest scenario does not arm Helod supply event");
        Check(Def<ScenarioDef>("HD_Scenario_WWTech").scenario.AllParts.OfType<ScenPart_StarterSupplyCaravan>().Count() == 1, "Helod scenario owns exactly one supply part");
        new ScenPart_StarterSupplyCaravan().PostGameStart();
        Check(starter.Armed && starter.ArrivalTick >= Find.TickManager.TicksGame + 59999, "Scenario callback schedules first-day visit");
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -100 - Low.PlayerGoodwill, false, false);
        AccessTools.Field(typeof(GameComponent_StarterSupplyCaravan), "nextRetryTick").SetValue(starter, Find.TickManager.TicksGame);
        starter.GameComponentTick(); Check(!starter.Delivered && StarterVisits == 0, "Hostile provider postpones supply rather than spawning an enemy caravan");
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -Low.PlayerGoodwill, false, false);
        entryBlocked = true;
        int attempts = starter.Attempts;
        AccessTools.Field(typeof(GameComponent_StarterSupplyCaravan), "nextRetryTick").SetValue(starter, Find.TickManager.TicksGame);
        starter.GameComponentTick(); entryBlocked = false;
        Check(!starter.Delivered && starter.Attempts == attempts + 1 && StarterVisits == 0, "Native incident entry-search failure keeps retry armed without duplicate visit");
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -100 - Low.PlayerGoodwill, false, false);
        stage = 1; GameDataSaveLoader.SaveGame("TradeStageH"); load = true; yield return 1;
    }
    private IEnumerable<int> Resume()
    {
        var starter = Current.Game.GetComponent<GameComponent_StarterSupplyCaravan>();
        if (stage == 2)
        {
            Check(starter.Delivered && StarterVisits == visits, "Delivered flag and real caravan survive save/load");
            int attempts = starter.Attempts; starter.Arm(map); starter.GameComponentTick();
            Check(starter.Attempts == attempts && StarterVisits == visits, "Load and repeated arm cannot duplicate visit");
            foreach (int ticks in StartCommitments()) yield return ticks;
            yield break;
        }
        if (stage >= 3) { foreach (int ticks in ResumeCommitments()) yield return ticks; yield break; }
        Check(starter.Armed && !starter.Delivered, "Pending hostile-delay schedule survives save/load");
        Low.TryAffectGoodwillWith(Faction.OfPlayer, -Low.PlayerGoodwill, false, false);
        AccessTools.Field(typeof(GameComponent_StarterSupplyCaravan), "nextRetryTick").SetValue(starter, Find.TickManager.TicksGame);
        starter.GameComponentTick(); Check(starter.Delivered && StarterVisits == 1, "Retry actually delivers one supply caravan");
        Pawn supply = map.lordManager.lords.SelectMany(l => l.ownedPawns).Single(p => p.TraderKind?.defName == GameComponent_StarterSupplyCaravan.TraderDefName);
        Item("HD_Money", 150); Trade(supply, startingBudget: true);
        var peddler = Def<TraderKindDef>("HD_Caravan_FrontierPeddler");
        Check(!peddler.requestable && Low.def.caravanTraderKinds.Contains(peddler) && peddler.commonality > 0, "Peddler is random eligible and not comms requestable");
        Check(TelegraphCompanies.CaravanCompanies.All(c => TelegraphCompanies.TraderKind(c) != peddler.defName), "Peddler absent from company calls");
        Pawn random = null;
        for (int i = 0; i < 40 && random == null; i++) { Pawn p = Arrive(null); if (p.TraderKind == peddler) random = p; }
        Check(random != null, "Native weighted random selection actually sends peddler");
        Check(random.Goods.Any(t => t.def.IsApparel) && random.Goods.Any(t => t.def == ThingDefOf.MedicineHerbal), "Peddler physically carries clothing and medicine"); Trade(random);
        var worldRequest = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25);
        worldRequest.ForceNoBackstory = true;
        Pawn worldActor = PawnGenerator.GeneratePawn(worldRequest); Body(worldActor); worldActor.skills.GetSkill(SkillDefOf.Social).Level = 15;
        var caravan = CaravanMaker.MakeCaravan(new[] { worldActor }, Faction.OfPlayer, map.Tile, true);
        Pawn mapActor = actor; actor = worldActor;
        foreach (string factionName in new[] { "HD_HelodCivilLowFaction", "HD_HelodCivilHighFaction" })
        {
            Faction faction = Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == factionName);
            faction.TryAffectGoodwillWith(Faction.OfPlayer, -faction.PlayerGoodwill, false, false);
            Settlement settlement = Find.WorldObjects.Settlements.First(s => s.Faction == faction);
            Check(settlement.TraderKind.defName == (faction == Low ? "HD_Settlement_HelodMercantile" : "HD_Settlement_VesenicaQuartermaster"), "Dedicated native settlement kind " + factionName);
            var stock = settlement.Goods.ToList(); Check(stock.Any(t => t.def.defName == "HD_GeneralMachinePart"), "Settlement industrial stock actually generated");
            Check(stock.Any(t => t.def.defName == "HD_Gun_SprMThree_Weapon") == (faction != Low), "Military stock restricted to quartermaster");
            Trade(settlement, true);
            int next = settlement.NextRestockTick;
            AccessTools.Field(typeof(Settlement_TraderTracker), "lastStockGenerationTicks").SetValue(settlement.trader, Find.TickManager.TicksGame - 1800001);
            settlement.trader.TraderTrackerTick();
            Check(settlement.RestockedSinceLastVisit && settlement.Goods.Any(t => t.def == ThingDefOf.Steel), "Native 30-day stock expires and regenerates");
            faction.TryAffectGoodwillWith(Faction.OfPlayer, -100 - faction.PlayerGoodwill, false, false);
            Check(!settlement.GetFloatMenuOptions(caravan).Any(o => !o.Disabled && o.Label.Contains("Trade".Translate().ToString())), "Hostile settlement offers no enabled trade action");
            faction.TryAffectGoodwillWith(Faction.OfPlayer, -faction.PlayerGoodwill, false, false);
        }
        actor = mapActor;
        visits = StarterVisits; stage = 2; GameDataSaveLoader.SaveGame("TradeStageH"); load = true; yield return 1;
    }
}
