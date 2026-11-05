using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Helodrace.Economy;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageI;

public sealed partial class Checks : GameComponent
{
    private bool started, complete, load;
    private int phase;
    private string output;
    private Map map;
    private Pawn worker;
    private Thing notes;
    private float baseline;
    private bool uiShown;
    public Checks(Game game) { }
    private static T Def<T>(string n) where T : Verse.Def => DefDatabase<T>.GetNamed(n);
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Write("PASS: " + text); }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref phase, "stageIPhase"); Scribe_Values.Look(ref baseline, "stageIBaseline");
        Scribe_References.Look(ref notes, "stageINotes"); Scribe_References.Look(ref worker, "stageIWorker");
    }
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }
    private Pawn NewPawn(Faction faction, IntVec3 cell)
    {
        var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, faction, canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25); request.ForceNoBackstory = true;
        var pawn = PawnGenerator.GeneratePawn(request); Body(pawn); GenSpawn.Spawn(pawn, cell, map); if (pawn.drafter != null) pawn.drafter.Drafted = true; return pawn;
    }
    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdStageI", out output)) return;
        started = true;
        try
        {
            map = Find.CurrentMap; Find.TickManager.Pause(); DebugSettings.enableStoryteller = false; DebugSettings.noAnimals = false;
            Directory.CreateDirectory(output);
            if (phase == 1)
            {
                Check(notes.Spawned && notes.stackCount == 600 && notes.MarketValue == 5, "Native save/load preserves cash quantity and purchasing value");
                Check(Math.Abs(Wealth() - baseline - 750) < .1, "Cash wealth discount remains correct after save/load");
                Check(Find.Scenario.AllParts.OfType<ScenPart_ResearchTechBaseline>().Count() == 1
                    && Def<ResearchProjectDef>("Electricity").baseCost == 11100
                    && Def<ResearchProjectDef>("Electricity").CostApparent == 22200,
                    "Tribal research baseline and Helod base-game budgets survive native scenario save/load exactly once");
                Finish(); return;
            }
            File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 I economy and research audit\n");
            foreach (Corpse c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(c.InnerPawn);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList()) { Body(p); if (p.Faction != Faction.OfPlayer) p.Destroy(DestroyMode.Vanish); else if (p.drafter != null) p.drafter.Drafted = true; }
            foreach (IntVec3 c in CellRect.CenteredOn(map.Center, 18))
            {
                foreach (Thing t in c.GetThingList(map).ToList()) if (t is not Pawn && t.Spawned && t.def.destroyable) t.Destroy();
                map.fogGrid.Unfog(c);
            }
            worker = NewPawn(Faction.OfPlayer, map.Center); worker.skills.GetSkill(SkillDefOf.Social).Level = 15;
            ExportResearch(); ResearchChecks(); MoneyChecks(); phase = 1; GameDataSaveLoader.SaveGame("StageI"); load = true;
        }
        catch (Exception e) { Fail(e); }
    }
    public override void GameComponentUpdate()
    {
        if (!load || complete) return;
        Find.TickManager.Pause();
        if (!uiShown) { uiShown = true; Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research); return; }
        // Start the reload from base-game costs, so only the saved Helod scenario can restore its budgets.
        Find.MainTabsRoot.SetCurrentTab(null); load = false; HelodScenarioResearchBudgets.Apply(null);
        GameDataSaveLoader.LoadGame("StageI");
    }
    private float Wealth() { map.wealthWatcher.ForceRecount(); return map.wealthWatcher.WealthItems; }
    private void ExportResearch()
    {
        Scenario old = Current.Game.Scenario;
        try
        {
            Current.Game.Scenario = Def<ScenarioDef>("HD_Scenario_WWTech").scenario;
            var data = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Select(p => new
            {
                name=p.defName, label=p.label, source=p.modContentPack?.PackageId,
                baseCost=p.baseCost, helodScenarioCost=p.Cost, helodScenarioApparent=p.CostApparent, tech=p.techLevel.ToString(),
                tribalFactor=p.CostFactor(TechLevel.Neolithic), industrialFactor=p.CostFactor(TechLevel.Industrial),
                prerequisites=p.prerequisites?.Select(x => x.defName).ToArray() ?? Array.Empty<string>(),
                bench=p.requiredResearchBuilding?.defName, tags=p.tags?.Select(x => x.defName).ToArray() ?? Array.Empty<string>()
            }).OrderBy(x => x.name).ToArray();
            File.WriteAllText(Path.Combine(output, "research-current.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented=true }));
            Check(data.Count(p => p.name.StartsWith("Helod")) == 66, "Export all 66 Helod projects alongside effective vanilla costs and tech penalties");
            Check(Def<ResearchProjectDef>("Electricity").CostApparent == Def<ResearchProjectDef>("Electricity").baseCost * 2, "Scenario electricity uses native tribal penalty twice, with raw completion cost preserved");
        }
        finally { Current.Game.Scenario = old; }
    }
    private void MoneyChecks()
    {
        baseline = Wealth(); notes = ThingMaker.MakeThing(Def<ThingDef>("HD_Money")); notes.stackCount=600; GenSpawn.Spawn(notes, map.Center + new IntVec3(2,0,0), map);
        Check(CurrencyWealth.SthalerFactor == .25f && Math.Abs(Wealth() - baseline - 750) < .1, "600 ground STL contribute 750 item wealth, not 3000");
        Check(notes.MarketValue == 5 && notes.def.BaseMarketValue == 5, "Face value and market price stay five silver per STL");
        notes.DeSpawn(); worker.inventory.innerContainer.TryAdd(notes, false);
        Check(Math.Abs(Wealth() - baseline - 750) < .1, "Player inventory cash receives the same discount once");
        worker.inventory.innerContainer.Remove(notes); Check(worker.carryTracker.TryStartCarry(notes), "Native carry tracker holds real cash");
        Check(Math.Abs(Wealth() - baseline - 750) < .1, "Carried cash receives discount once");
        Check(worker.carryTracker.TryDropCarriedThing(worker.Position, ThingPlaceMode.Near, out notes), "Native carry tracker drops same notes");
        var provider = Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == "HD_HelodCivilLowFaction");
        var visitor = NewPawn(provider, map.Center + new IntVec3(6,0,0)); notes.DeSpawn(); visitor.inventory.innerContainer.TryAdd(notes, false);
        Check(Math.Abs(Wealth() - baseline) < .1, "Visitor-held cash is excluded using the native holder filter");
        visitor.inventory.innerContainer.Remove(notes); GenSpawn.Spawn(notes, map.Center + new IntVec3(2,0,0), map);
        map.fogGrid.Refog(CellRect.SingleCell(notes.Position));
        Check(Math.Abs(Wealth() - baseline) < .1, "Fogged cash causes no discount against unrelated visible wealth"); map.fogGrid.Unfog(notes.Position);
        notes.DeSpawn(); visitor.inventory.innerContainer.TryAdd(notes, false);
        var silver = new List<Thing>(); int left=3000;
        while (left>0) { Thing t=ThingMaker.MakeThing(ThingDefOf.Silver); t.stackCount=Math.Min(left,t.def.stackLimit); left-=t.stackCount; GenPlace.TryPlaceThing(t,map.Center,map,ThingPlaceMode.Near); silver.Add(t); }
        float silverWealth=Wealth(); float silverPoints=StorytellerUtility.DefaultThreatPointsNow(map);
        Check(Math.Abs(silverWealth-baseline-3000)<.1, "Equal face-value silver retains full 3000 item wealth");
        foreach(Thing t in silver) t.Destroy(); visitor.inventory.innerContainer.Remove(notes); GenSpawn.Spawn(notes,map.Center+new IntVec3(2,0,0),map);
        float cashWealth=Wealth(); float cashPoints=StorytellerUtility.DefaultThreatPointsNow(map);
        Check(Math.Abs(silverWealth-cashWealth-2250)<.1 && cashPoints <= silverPoints, "Switching equal-value silver to STL lowers only wealth and cannot raise threat points");
        Write($"WEALTH equalValue=3000 silverItems={silverWealth} cashItems={cashWealth} silverThreat={silverPoints} cashThreat={cashPoints}");
        provider.TryAffectGoodwillWith(Faction.OfPlayer,-provider.PlayerGoodwill,false,false); var prior=map.lordManager.lords.ToHashSet();
        var parms=StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category,map); parms.faction=provider; parms.traderKind=Def<TraderKindDef>(TelegraphCompanies.CommercialTrader); parms.points=260; parms.forced=true;
        Check(IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms), "Real trade caravan arrival for currency price regression");
        var trader=map.lordManager.lords.Single(l=>!prior.Contains(l)).ownedPawns.Single(p=>p.trader!=null);
        TradeSession.SetupWith(trader,worker,false); var quoted=TradeSession.deal.AllTradeables.First(t=>t.ThingDef==notes.def);
        Check(quoted.GetPriceFor(TradeAction.PlayerBuys)==5.25f && quoted.GetPriceFor(TradeAction.PlayerSells)==4.75f, "Actual STL buy/sell quotes remain 5.25 and 4.75"); TradeSession.Close();
        Check(Math.Abs(Wealth()-baseline-750)<.1, "Foreign caravan stock does not cause a second cash discount");
    }
    private void Finish(){complete=true;Write("COMPLETE");Waylith.EngineApplication.Instance.Quit(0);}
    private void Fail(Exception e){complete=true;Write("FAIL: "+e);Log.Error(e.ToString());Waylith.EngineApplication.Instance.Quit(1);}
}
