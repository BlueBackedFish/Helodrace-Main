using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.IntermediatePrices;

public sealed class Checks : GameComponent
{
    private bool started, complete, load;
    private int stage, remaining;
    private string output;
    private Map map;
    private Pawn worker;
    private IEnumerator<int> steps;
    private Thing savedElectronics;
    private Building_WorkTable fab;
    private static T Def<T>(string n) where T : Verse.Def => DefDatabase<T>.GetNamed(n);
    private ThingDef Electronics => Def<ThingDef>("HD_ElectronicComponent");
    public Checks(Game game) { }
    private void Write(string t) => File.AppendAllText(Path.Combine(output, "checks.txt"), t + "\n");
    private void Check(bool ok, string t) { if (!ok) throw new InvalidOperationException(t); Write("PASS: " + t); }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref stage, "pricesStage"); Scribe_References.Look(ref worker, "pricesWorker");
        Scribe_References.Look(ref savedElectronics, "savedElectronics"); Scribe_References.Look(ref fab, "pricesFab");
    }
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }
    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdIntermediatePrices", out output)) return;
        started = true;
        try
        {
            map = Find.CurrentMap; Find.TickManager.Pause(); DebugSettings.enableStoryteller = false; DebugSettings.noAnimals = false; DebugSettings.enableRandomMentalStates = false;
            if (stage > 0) { steps = Resume().GetEnumerator(); return; }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 intermediate prices and electronics\n");
            foreach (Corpse c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(c.InnerPawn);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                Body(p); if (p.Faction != Faction.OfPlayer) p.Destroy(DestroyMode.Vanish);
                else { if (p.drafter != null) p.drafter.Drafted = true; if (p.workSettings != null) foreach (WorkTypeDef d in DefDatabase<WorkTypeDef>.AllDefsListForReading) p.workSettings.SetPriority(d, 0); }
            }
            foreach (IntVec3 c in CellRect.CenteredOn(map.Center, 24))
            {
                foreach (Thing t in c.GetThingList(map).ToList()) if (t is not Pawn && t.Spawned && t.def.destroyable) t.Destroy();
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null); map.fogGrid.Unfog(c); map.areaManager.Home[c] = true;
            }
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25); request.ForceNoBackstory = true;
            worker = PawnGenerator.GeneratePawn(request); Body(worker); GenSpawn.Spawn(worker, map.Center, map);
            worker.workSettings.EnableAndInitialize(); foreach (WorkTypeDef d in DefDatabase<WorkTypeDef>.AllDefsListForReading) worker.workSettings.SetPriority(d, 0);
            worker.workSettings.SetPriority(WorkTypeDefOf.Crafting, 1); worker.skills.GetSkill(SkillDefOf.Crafting).Level = 20; worker.skills.GetSkill(SkillDefOf.Social).Level = 15;
            for (int h = 0; h < 24; h++) worker.timetable.SetAssignment(h, TimeAssignmentDefOf.Work);
            worker.drafter.Drafted = true; steps = Run().GetEnumerator();
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
                if (load) { steps = null; GameDataSaveLoader.LoadGame("IntermediatePrices"); return; }
                remaining = steps.Current;
            }
            int n = Math.Min(remaining, 200); for (int i = 0; i < n; i++) Find.TickManager.DoSingleTick(); remaining -= n;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned) foreach (Need need in p.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        }
        catch (Exception e) { Fail(e); }
    }
    private void Fail(Exception e) { complete = true; Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
    private int Count(ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount) + map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayer).Sum(p => (p.inventory?.innerContainer.Where(t => t.def == def).Sum(t => t.stackCount) ?? 0) + (p.carryTracker?.CarriedThing?.def == def ? p.carryTracker.CarriedThing.stackCount : 0));
    private void Items(string name, int count)
    {
        var def = Def<ThingDef>(name);
        while (count > 0) { Thing t = ThingMaker.MakeThing(def); t.stackCount = Math.Min(count, def.stackLimit); count -= t.stackCount; GenPlace.TryPlaceThing(t, map.Center + new IntVec3(0, 0, 3), map, ThingPlaceMode.Near); t.SetForbidden(false); }
    }
    private Building Spawn(string name, int x, int z)
    {
        var def = Def<ThingDef>(name); var b = (Building)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)); b.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(b, map.Center + new IntVec3(x, 0, z), map); return b;
    }
    private void Prices()
    {
        var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "expected-prices.json"))).RootElement;
        float vanilla = ThingDefOf.ComponentIndustrial.GetStatValueAbstract(StatDefOf.MarketValue);
        Check(vanilla == 32 && ThingDefOf.ComponentSpacer.GetStatValueAbstract(StatDefOf.MarketValue) == 200, "Vanilla component anchors remain 32 and 200");
        File.WriteAllText(Path.Combine(output, "prices.csv"), "defName,before,expected,actual\n");
        var old = new Dictionary<ThingDef, float>();
        foreach (var row in rows.EnumerateArray())
        {
            string name = row.GetProperty("defName").GetString(); float expected = row.GetProperty("after").GetSingle(), before = row.GetProperty("before").GetSingle();
            var def = Def<ThingDef>(name); float actual = ThingMaker.MakeThing(def).MarketValue; old[def] = before;
            Check(Math.Abs(actual - expected) < .0001 && actual < vanilla && actual < before, "Actual reduced intermediate value: " + name + "=" + actual);
            File.AppendAllText(Path.Combine(output, "prices.csv"), $"{name},{before},{expected},{actual}\n");
        }
        Check(rows.GetArrayLength() == 22 && ThingMaker.MakeThing(Electronics).MarketValue == vanilla && Def<ThingDef>("HD_BasicElectronicComponents").GetStatValueAbstract(StatDefOf.MarketValue) < vanilla, "All 22 intermediates below vanilla; distinct new electronics exactly equal vanilla");
        foreach (string name in new[] { "HD_LargeSteamEngine", "HD_Radio", "HD_SemiconductorFab", "HD_HydraulicPress1970Bench" })
        {
            var def = Def<ThingDef>(name); float beforeInputs = def.costList.Sum(x => x.count * (old.TryGetValue(x.thingDef, out float p) ? p : x.thingDef.GetStatValueAbstract(StatDefOf.MarketValue)));
            float nowInputs = def.costList.Sum(x => x.count * x.thingDef.GetStatValueAbstract(StatDefOf.MarketValue));
            float value = def.GetStatValueAbstract(StatDefOf.MarketValue);
            Check(nowInputs < beforeInputs && value > 0 && float.IsFinite(value), "Reduced material valuation feeds valid native equipment price: " + name);
            Write($"EQUIPMENT {name} oldMaterialSubtotal={beforeInputs} currentMaterialSubtotal={nowInputs} nativeMarketValue={value}");
        }
        Thing sample = ThingMaker.MakeThing(Electronics); sample.stackCount = 1; string one = sample.Graphic.MatSingleFor(sample).mainTexture.name;
        sample.stackCount = 50; string fifty = sample.Graphic.MatSingleFor(sample).mainTexture.name;
        Check(!string.IsNullOrEmpty(one) && !string.IsNullOrEmpty(fifty) && one != fifty, "Both new electronic stack textures load through native game graphics"); Write("GRAPHICS " + one + " / " + fifty);
        var fabDef = Def<ThingDef>("HD_SemiconductorFab"); Check(fabDef.costList.All(c => c.thingDef != Electronics), "Fab bootstrap needs only lower-tier electronics");
        foreach (string name in new[] { "HD_HydraulicPress1970Bench", "HD_MachiningCenter1970Bench", "HD_RollingMill1970Bench", "HD_MultiTaskMachine2000Bench" })
            Check(Def<ThingDef>(name).costList.Any(c => c.thingDef == Electronics), "Real post-transistor consumer: " + name);
    }
    private void Trade()
    {
        Faction provider = Find.FactionManager.AllFactionsListForReading.First(f => f.def.defName == "HD_HelodCivilLowFaction"); provider.TryAffectGoodwillWith(Faction.OfPlayer, -provider.PlayerGoodwill, false, false);
        var old = map.lordManager.lords.ToHashSet();
        var parms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category, map); parms.faction = provider; parms.traderKind = Def<TraderKindDef>(TelegraphCompanies.CommercialTrader); parms.points = 260; parms.forced = true;
        Check(IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms), "Real industrial supply caravan arrives");
        Pawn trader = map.lordManager.lords.Single(l => !old.Contains(l)).ownedPawns.Single(p => p.trader != null);
        Items("Silver", 10000); Items("HD_GeneralMachinePart", 10);
        TradeSession.SetupWith(trader, worker, false);
        var electronics = TradeSession.deal.AllTradeables.First(t => t.ThingDef == Electronics && t.CountHeldBy(Transactor.Trader) >= 4);
        var vanilla = TradeSession.deal.AllTradeables.First(t => t.ThingDef == ThingDefOf.ComponentIndustrial && t.CountHeldBy(Transactor.Trader) >= 4);
        Check(electronics.GetPriceFor(TradeAction.PlayerBuys) == vanilla.GetPriceFor(TradeAction.PlayerBuys) && electronics.GetPriceFor(TradeAction.PlayerSells) == vanilla.GetPriceFor(TradeAction.PlayerSells), "New electronics and vanilla components have equal real buy/sell prices");
        var cheap = TradeSession.deal.AllTradeables.First(t => t.ThingDef.defName == "HD_GeneralMachinePart" && t.CountHeldBy(Transactor.Colony) >= 10);
        Check(cheap.GetPriceFor(TradeAction.PlayerBuys) < vanilla.GetPriceFor(TradeAction.PlayerBuys), "Cheap intermediate remains below component in actual trade UI");
        electronics.AdjustTo(4); vanilla.AdjustTo(4); cheap.AdjustTo(-10); TradeSession.deal.UpdateCurrencyCount();
        int before = Count(Electronics); Check(TradeSession.deal.TryExecute(out bool traded) && traded && Count(Electronics) == before + 4, "Actual new-electronics purchase and cheap-material sale transfer physical stock"); TradeSession.Close();
    }
    private Bill_Production Bill(string name, int count)
    {
        var bill = (Bill_Production)Def<RecipeDef>(name).MakeNewBill(); bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = count; bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor, null); fab.BillStack.AddBill(bill); return bill;
    }
    private IEnumerable<int> Finish(Bill_Production bill)
    {
        int elapsed = 0; while (bill.repeatCount > 0 && elapsed < 90000) { yield return 120; elapsed += 120; }
        Check(bill.repeatCount == 0, "Native automatic production completes " + bill.recipe.defName + "; worker=" + worker.CurJobDef);
    }
    private IEnumerable<int> Run()
    {
        Prices(); Trade();
        Find.ResearchManager.ResetAllProgress(); Check(!Def<RecipeDef>("HD_FabricateSolidStateComponents").AvailableNow, "New electronics are research-gated");
        foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading) if (r.researchPrerequisite != null) Find.ResearchManager.FinishProject(r.researchPrerequisite, doCompletionDialog: false);
        fab = (Building_WorkTable)Spawn("HD_SemiconductorFab", -4, 4);
        var fabrication = Bill("HD_FabricateSolidStateComponents", 6);
        var giver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.AllDefsListForReading.Single(g => typeof(WorkGiver_DoBill).IsAssignableFrom(g.giverClass) && g.fixedBillGiverDefs?.Contains(fab.def) == true).Worker;
        Check(!fab.GetComp<CompPowerTrader>().PowerOn && giver.JobOnThing(worker, fab, true) == null, "Unpowered fab cannot fabricate electronics");
        for (int x = -19; x <= 0; x++) Spawn("PowerConduit", x, 0);
        for (int z = 1; z <= 2; z++) Spawn("PowerConduit", -4, z);
        for (int x = -18; x <= -9; x += 3) Spawn("VanometricPowerCell", x, -1);
        map.powerNetManager.UpdatePowerNetsAndConnections_First(); yield return 1200;
        Check(fab.GetComp<CompPowerTrader>().PowerOn, "Native electrical network powers fab");
        var raw = fabrication.recipe.ingredients.ToDictionary(i => i.filter.AnyAllowedDef, i => i.CountRequiredOfFor(i.filter.AnyAllowedDef, fabrication.recipe, fabrication) * 6);
        foreach (var input in raw) Items(input.Key.defName, input.Value);
        var beforeRaw = raw.Keys.ToDictionary(d => d, d => Count(d)); int before = Count(Electronics);
        worker.drafter.Drafted = false; worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        foreach (int n in Finish(fabrication)) yield return n;
        Check(Count(Electronics) == before + 6 && raw.All(i => Count(i.Key) == beforeRaw[i.Key] - i.Value), "Six fabricated electronic sets consume exact real feedstocks");
        before = Count(ThingDefOf.ComponentIndustrial); int electronicBefore = Count(Electronics); var conversion = Bill("HD_AssembleIndustrialComponent", 1);
        foreach (int n in Finish(conversion)) yield return n;
        Check(Count(ThingDefOf.ComponentIndustrial) == before + 1 && Count(Electronics) == electronicBefore - 1, "One-to-one industrial assembly consumes real electronics without duplication");
        worker.drafter.Drafted = true; worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        var press = Def<ThingDef>("HD_HydraulicPress1970Bench"); var frame = (Frame)ThingMaker.MakeThing(press.frameDef); frame.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(frame, map.Center + new IntVec3(10, 0, 4), map);
        int needed = press.costList.Single(c => c.thingDef == Electronics).count; int stockBefore = Count(Electronics);
        foreach (var cost in press.costList)
        {
            if (cost.thingDef == Electronics)
            {
                int left = cost.count;
                foreach (Thing item in map.listerThings.ThingsOfDef(Electronics).ToList()) { int n = Math.Min(left, item.stackCount); frame.resourceContainer.TryAdd(item.SplitOff(n)); left -= n; if (left == 0) break; }
                Check(left == 0, "Purchased/fabricated electronics physically enter construction frame");
            }
            else { Thing t = ThingMaker.MakeThing(cost.thingDef); t.stackCount = cost.count; frame.resourceContainer.TryAdd(t); }
        }
        frame.workDone = frame.WorkToBuild; frame.CompleteConstruction(worker);
        Check(Count(Electronics) == stockBefore - needed && map.listerThings.ThingsOfDef(press).Count == 1 && frame.Destroyed, "Native construction consumes six real electronic sets for 1970s press");
        savedElectronics = map.listerThings.ThingsOfDef(Electronics).First(); stage = 1; GameDataSaveLoader.SaveGame("IntermediatePrices"); load = true; yield return 1;
    }
    private IEnumerable<int> Resume()
    {
        Check(savedElectronics != null && savedElectronics.Spawned && Count(Electronics) == 3 && savedElectronics.MarketValue == 32, "Actual new electronic inventory and value persist after native save/load");
        Check(fab.Spawned && map.listerThings.ThingsOfDef(Def<ThingDef>("HD_HydraulicPress1970Bench")).Count == 1, "Fab and electronic consumer remain usable world objects after load");
        yield return 1;
    }
}
