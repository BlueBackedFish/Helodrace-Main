using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.IndustryStage3;

public sealed partial class Checks : GameComponent
{
    private bool started, complete, loadRequested;
    private string output;
    private Map map;
    private Pawn worker;
    private Building_WorkTable reactor;
    private Building_WorkTable componentBench;
    private float savedComponentWork;
    private int savedComponentIngredients;
    private Dictionary<ThingDef, int> componentRawAtPause;
    private int stage, remaining;
    private IEnumerator<int> steps;
    private static Map fixtureMap;
    private static bool climatePatched;
    private static bool FixtureTemperature(MapTemperature __instance, ref float __result)
    {
        if (fixtureMap == null || AccessTools.Field(typeof(MapTemperature), "map").GetValue(__instance) != fixtureMap) return true;
        __result = 21f; return false;
    }
    public Checks(Game game) { }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref stage, "industryStage3", 0);
        Scribe_References.Look(ref worker, "industryWorker");
        Scribe_References.Look(ref reactor, "industryReactor");
        Scribe_References.Look(ref componentBench, "componentBench");
        Scribe_Values.Look(ref savedComponentWork, "componentWork", 0f);
        Scribe_Values.Look(ref savedComponentIngredients, "componentIngredients", 0);
        Scribe_Collections.Look(ref componentRawAtPause, "componentRawAtPause", LookMode.Def, LookMode.Value);
    }
    public override void LoadedGame()
    {
        if (stage != 2 || !GenCommandLine.TryGetCommandLineArg("hdIndustryStage3", out output)) return;
        try
        {
            var bill = componentBench.BillStack.Bills.OfType<Bill_ProductionWithUft>().Single(b => b.repeatCount > 0);
            Check(bill.BoundUft != null && bill.BoundUft.workLeft == savedComponentWork
                && bill.BoundUft.ingredients.Sum(t => t.stackCount) == savedComponentIngredients
                && bill.BoundUft.Creator == worker,
                "Actual component save/load preserves exact work, ingredients and maker before the first tick");
        }
        catch (Exception error) { Fail(error); }
    }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
    private void Check(bool ok, string text)
    {
        if (!ok) throw new InvalidOperationException(text);
        Write("PASS: " + text);
    }
    private static T Def<T>(string name) where T : Verse.Def => DefDatabase<T>.GetNamed(name);
    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null || !GenCommandLine.TryGetCommandLineArg("hdIndustryStage3", out output)) return;
        started = true;
        try
        {
            Directory.CreateDirectory(output);
            map = Find.CurrentMap;
            fixtureMap = map;
            if (!climatePatched)
            {
                new Harmony("helodrace.tests.industry.climate").Patch(AccessTools.PropertyGetter(typeof(MapTemperature), nameof(MapTemperature.OutdoorTemp)),
                    prefix: new HarmonyMethod(typeof(Checks), nameof(FixtureTemperature)));
                climatePatched = true;
            }
            Find.TickManager.Pause();
            DebugSettings.enableStoryteller = false;
            if (stage == 2) { steps = ResumeComponentBatch().GetEnumerator(); return; }
            if (stage == 1) { steps = AfterLoad().GetEnumerator(); return; }
            File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 industry checks\n");
            DebugSettings.noAnimals = true;
            DebugSettings.enableRandomMentalStates = false;
            Find.Storyteller.difficulty.allowBigThreats = false;
            Find.Storyteller.difficulty.allowIntroThreats = false;
            Find.Storyteller.difficulty.allowExtremeWeatherIncidents = false;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                Body(pawn);
                if (pawn.Faction != Faction.OfPlayer) pawn.DeSpawn();
                else
                {
                    if (pawn.drafter != null) pawn.drafter.Drafted = true;
                    if (pawn.workSettings != null)
                        foreach (WorkTypeDef type in DefDatabase<WorkTypeDef>.AllDefsListForReading) pawn.workSettings.SetPriority(type, 0);
                }
            }
            foreach (IntVec3 cell in CellRect.CenteredOn(map.Center, 24))
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing is not Pawn && thing.Spawned && thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
                map.fogGrid.Unfog(cell);
                map.areaManager.Home[cell] = true;
            }
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25);
            request.ForceNoBackstory = true;
            worker = PawnGenerator.GeneratePawn(request);
            Body(worker);
            GenSpawn.Spawn(worker, map.Center + new IntVec3(0, 0, -8), map);
            worker.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
            foreach (SkillRecord skill in worker.skills.skills) skill.Level = 12;
            worker.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef type in DefDatabase<WorkTypeDef>.AllDefsListForReading) worker.workSettings.SetPriority(type, 0);
            worker.workSettings.SetPriority(WorkTypeDefOf.Crafting, 1);
            for (int hour = 0; hour < 24; hour++) worker.timetable.SetAssignment(hour, TimeAssignmentDefOf.Work);
            reactor = (Building_WorkTable)Spawn("HD_HaberBoschHighPressureReactor", -4, 3);
            var givers = Givers(reactor.def);
            Write("SNAPSHOT high-pressure reactor givers=" + string.Join(",", givers.Select(g => g.defName)));
            if (GenCommandLine.TryGetCommandLineArg("hdIndustryBaseline", out _)) Finish();
            else steps = ReactorChecks().GetEnumerator();
        }
        catch (Exception error) { Fail(error); }
    }
    public override void GameComponentUpdate()
    {
        if (complete || steps == null) return;
        try
        {
            if (remaining == 0)
            {
                if (!steps.MoveNext()) { Finish(); return; }
                if (loadRequested) { steps = null; GameDataSaveLoader.LoadGame(stage == 2 ? "IndustryStage3Components" : "IndustryStage3"); return; }
                remaining = steps.Current;
            }
            int count = Math.Min(remaining, 200);
            for (int i = 0; i < count; i++) Find.TickManager.DoSingleTick();
            remaining -= count;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                foreach (Need need in pawn.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        }
        catch (Exception error) { Fail(error); }
    }
    private void Finish() { complete = true; Write("COMPLETE"); Waylith.EngineApplication.Instance.Quit(0); }
    private void Fail(Exception error) { complete = true; Write("FAIL: " + error); Log.Error("Industry checks: " + error); Waylith.EngineApplication.Instance.Quit(1); }
    private static void Body(Pawn pawn)
    {
        if (pawn.story != null && pawn.story.bodyType == null)
            pawn.story.bodyType = pawn.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
    }
    private Building Spawn(string name, int x, int z)
    {
        ThingDef def = Def<ThingDef>(name);
        var building = (Building)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
        building.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(building, map.Center + new IntVec3(x, 0, z), map);
        return building;
    }
    private static List<WorkGiverDef> Givers(ThingDef table) => DefDatabase<WorkGiverDef>.AllDefsListForReading
        .Where(g => typeof(WorkGiver_DoBill).IsAssignableFrom(g.giverClass) && g.fixedBillGiverDefs?.Contains(table) == true).ToList();
    private Bill_Production Bill(Building_WorkTable table, string recipeName)
    {
        var recipe = Def<RecipeDef>(recipeName);
        if (recipe.researchPrerequisite != null) Find.ResearchManager.FinishProject(recipe.researchPrerequisite, doCompletionDialog: false);
        var bill = (Bill_Production)recipe.MakeNewBill();
        bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = 1;
        bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor, null);
        table.BillStack.AddBill(bill);
        foreach (IngredientCount ingredient in recipe.ingredients)
        {
            ThingDef def = ingredient.filter.AnyAllowedDef;
            int count = ingredient.CountRequiredOfFor(def, recipe, bill);
            while (count > 0)
            {
                Thing stack = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                stack.stackCount = Math.Min(count, def.stackLimit); count -= stack.stackCount;
                GenPlace.TryPlaceThing(stack, table.InteractionCell + new IntVec3(1, 0, -1), map, ThingPlaceMode.Near);
            }
        }
        return bill;
    }
    private int Count(ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount)
        + map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayer).Sum(p =>
            (p.inventory?.innerContainer.Where(t => t.def == def).Sum(t => t.stackCount) ?? 0)
            + (p.carryTracker?.CarriedThing?.def == def ? p.carryTracker.CarriedThing.stackCount : 0));
    private IEnumerable<int> CompleteBill(Building_WorkTable table, Bill_Production bill, bool forced)
    {
        if (forced)
        {
            var giver = (WorkGiver_DoBill)Givers(table.def).Single().Worker;
            Job job = giver.JobOnThing(worker, table, true);
            Check(job?.def == JobDefOf.DoBill && worker.jobs.TryTakeOrderedJob(job), "Ordered bill starts at " + table.def.defName + ": " + JobFailReason.Reason);
        }
        int elapsed = 0;
        while (bill.repeatCount > 0 && elapsed < 90000) { yield return 120; elapsed += 120; }
        Check(bill.repeatCount == 0, (forced ? "Ordered" : "Automatic AI") + " completes " + bill.recipe.defName
            + "; current=" + worker.CurJobDef + "; available=" + bill.recipe.Worker.AvailableOnNow(table)
            + "; bound=" + (bill as Bill_ProductionWithUft)?.BoundUft?.workLeft + "; downed=" + worker.Downed);
    }
    private IEnumerable<int> ReactorChecks()
    {
        Check(Givers(reactor.def).Count == 1, "High-pressure reactor has a single Crafting bill giver");
        worker.drafter.Drafted = true;
        var recipe = Def<RecipeDef>("HD_SynthesizeAmmoniumNitrate");
        var bill = Bill(reactor, recipe.defName);
        var giver = (WorkGiver_DoBill)Givers(reactor.def).Single().Worker;
        Check(!reactor.GetComp<CompPowerTrader>().PowerOn && giver.JobOnThing(worker, reactor, true) == null,
            "Unpowered reactor rejects a bill without errors");
        for (int x = -19; x <= 7; x++)
        {
            Spawn("PowerConduit", x, 0);
            Spawn("PowerConduit", x, -1);
        }
        for (int x = -18; x <= 0; x += 3) Spawn("VanometricPowerCell", x, -2);
        map.powerNetManager.UpdatePowerNetsAndConnections_First();
        yield return 1200;
        Write("POWER reactor=" + reactor.GetComp<CompPowerTrader>().PowerOn + " cellSize=" + Def<ThingDef>("VanometricPowerCell").size
            + " reactorNet=" + (reactor.GetComp<CompPowerTrader>().PowerNet != null)
            + " generators=" + string.Join(";", map.listerThings.ThingsOfDef(Def<ThingDef>("VanometricPowerCell")).Select(t =>
                t.Position + ":" + t.TryGetComp<CompPowerTrader>().PowerOutput + ":connected=" + (t.TryGetComp<CompPowerTrader>().PowerNet != null))));
        Check(reactor.GetComp<CompPowerTrader>().PowerOn, "Actual electrical network powers the reactor");
        worker.drafter.Drafted = false;
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        int before = Count(recipe.ProducedThingDef);
        foreach (int ticks in CompleteBill(reactor, bill, true)) yield return ticks;
        Check(Count(recipe.ProducedThingDef) == before + 40, "Ordered batch produces exactly 40 ammonium nitrate");
        bill = Bill(reactor, recipe.defName);
        foreach (int ticks in CompleteBill(reactor, bill, false)) yield return ticks;
        Check(Count(recipe.ProducedThingDef) == before + 80, "Automatic batch produces another 40 without duplicates");
        stage = 1;
        GameDataSaveLoader.SaveGame("IndustryStage3");
        Write("SAVE: IndustryStage3 after ordered and automatic chemical production");
        loadRequested = true; yield return 0;
    }
    private IEnumerable<int> AfterLoad()
    {
        var recipe = Def<RecipeDef>("HD_SynthesizeAmmoniumNitrate");
        int before = Count(recipe.ProducedThingDef);
        var bill = Bill(reactor, recipe.defName);
        yield return 120;
        foreach (int ticks in CompleteBill(reactor, bill, false)) yield return ticks;
        Check(Count(recipe.ProducedThingDef) == before + 40, "Loaded reactor completes a new automatic batch");
        var stirred = (Building_WorkTable)Spawn("HD_StirredTankReactor", 4, 3);
        map.powerNetManager.UpdatePowerNetsAndConnections_First();
        yield return 1200;
        Check(stirred.GetComp<CompPowerTrader>().PowerOn && Givers(stirred.def).Count == 1,
            "Existing stirred-tank power and bill giver remain connected");
        var tnt = Def<RecipeDef>("HD_SynthesizeTNT");
        int tntBefore = Count(tnt.ProducedThingDef);
        var tntBill = Bill(stirred, tnt.defName);
        foreach (int ticks in CompleteBill(stirred, tntBill, false)) yield return ticks;
        Check(Count(tnt.ProducedThingDef) == tntBefore + tnt.products.Single().count, "Stirred-tank chemistry still produces the exact output");
        foreach (int ticks in BarrelChecks()) yield return ticks;
        foreach (int ticks in ComponentChecks()) yield return ticks;
    }
}
