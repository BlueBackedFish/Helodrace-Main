using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.ProductionPlanning;

public sealed class Checks : GameComponent
{
    private bool started, complete, load;
    private int stage, remaining, savedTickets;
    private float savedWork;
    private string savedBillId;
    private string output;
    private Map map;
    private Pawn worker;
    private Building engine;
    private Bill_ProductionWithUft savedBill;
    private IEnumerator<int> steps;
    private MapComponent_ProductionPlanning Plan => map.GetComponent<MapComponent_ProductionPlanning>();
    private static readonly string[] Six = { "HD_GeneralMachinePart", "HD_PressComponent", "HD_Shaft", "HD_UniformRolledSteelPlate", "HD_BasicElectricalComponents", "HD_BasicElectronicComponents" };
    public Checks(Game game) { }
    private static T Def<T>(string name) where T : Verse.Def => DefDatabase<T>.GetNamed(name);
    private void Write(string t) => File.AppendAllText(Path.Combine(output, "checks.txt"), t + "\n");
    private void Check(bool ok, string t) { if (!ok) throw new InvalidOperationException(t); Write("PASS: " + t); }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref stage, "planningStage"); Scribe_Values.Look(ref savedWork, "savedWork"); Scribe_Values.Look(ref savedTickets, "savedTickets");
        Scribe_References.Look(ref worker, "planningWorker"); Scribe_References.Look(ref engine, "planningEngine"); Scribe_Values.Look(ref savedBillId, "planningBillId");
    }
    private static void Body(Pawn p) { if (p.story != null && p.story.bodyType == null) p.story.bodyType = p.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male; }
    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null) return;
        if (GenCommandLine.TryGetCommandLineArg("hdProductionPlanningPerf", out output))
        {
            started = true; map = Find.CurrentMap; Find.TickManager.Pause();
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 production planning performance\n");
            steps = Perf().GetEnumerator(); return;
        }
        if (!GenCommandLine.TryGetCommandLineArg("hdProductionPlanning", out output)) return;
        started = true;
        try
        {
            map = Find.CurrentMap; Find.TickManager.Pause(); DebugSettings.enableStoryteller = false; DebugSettings.enableRandomMentalStates = false;
            if (stage > 0) { steps = Resume().GetEnumerator(); return; }
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 production planning checks\n");
            foreach (Corpse c in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()) Body(c.InnerPawn);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                Body(p); if (p.Faction != Faction.OfPlayer) p.Destroy(DestroyMode.Vanish);
                else
                {
                    if (p.drafter != null) p.drafter.Drafted = true;
                    if (p.workSettings != null) foreach (WorkTypeDef d in DefDatabase<WorkTypeDef>.AllDefsListForReading) p.workSettings.SetPriority(d, 0);
                }
            }
            foreach (IntVec3 c in CellRect.CenteredOn(map.Center, 24))
            {
                foreach (Thing t in c.GetThingList(map).ToList()) if (t is not Pawn && t.Spawned && t.def.destroyable) t.Destroy();
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null); map.fogGrid.Unfog(c); map.areaManager.Home[c] = true;
            }
            var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25); req.ForceNoBackstory = true;
            worker = PawnGenerator.GeneratePawn(req); Body(worker); GenSpawn.Spawn(worker, map.Center, map);
            worker.skills.GetSkill(SkillDefOf.Crafting).Level = 20; worker.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef d in DefDatabase<WorkTypeDef>.AllDefsListForReading) worker.workSettings.SetPriority(d, 0);
            worker.workSettings.SetPriority(WorkTypeDefOf.Crafting, 1);
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
                if (load) { steps = null; GameDataSaveLoader.LoadGame("ProductionPlanning"); return; }
                remaining = steps.Current;
            }
            int n = Math.Min(remaining, 200);
            for (int i = 0; i < n; i++) Find.TickManager.DoSingleTick(); remaining -= n;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned) foreach (Need need in p.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        }
        catch (Exception e) { Fail(e); }
    }
    private void Fail(Exception e) { complete = true; Write("FAIL: " + e); Log.Error(e.ToString()); Waylith.EngineApplication.Instance.Quit(1); }
    private Building Spawn(string name, int x, int z)
    {
        var def = Def<ThingDef>(name); var b = (Building)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)); b.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(b, map.Center + new IntVec3(x, 0, z), map); return b;
    }
    private void Items(string name, int count)
    {
        var def = Def<ThingDef>(name);
        while (count > 0)
        {
            Thing t = ThingMaker.MakeThing(def); t.stackCount = Math.Min(count, def.stackLimit); count -= t.stackCount;
            GenPlace.TryPlaceThing(t, map.Center + new IntVec3(0, 0, 3), map, ThingPlaceMode.Near); t.SetForbidden(false);
        }
    }
    private IEnumerable<int> Run()
    {
        engine = Spawn("HD_LargeSteamEngine", -16, 12);
        var mill = (Building_WorkTable)Spawn("HD_LineShaftMillingMachine", -8, 12);
        Spawn("HD_LineShaftHydraulicPress", -2, 12); Spawn("HD_LineShaftRollingMachine", 4, 12);
        var lathe = (Building_WorkTable)Spawn("HD_LineShaftTurretLathe", 10, 12); Spawn("HD_BasicWorkbench", -5, -5);
        for (int x = -18; x <= 12; x++) if ((map.Center + new IntVec3(x, 0, 11)).GetEdifice(map)?.TryGetComp<CompMechanicalNode>() == null) Spawn("HD_LineShaft", x, 11);
        engine.GetComp<CompRefuelable>().Refuel(120); yield return 240;
        Plan.GoalFor(Def<ThingDef>("HD_BasicElectricalComponents")).Target = 1;
        Plan.Refresh(); Check(Plan.Tickets.Count == 0, "No desk means no automatic orders");
        var desk = Spawn("HD_ProductionPlanningDesk", -13, -5); Spawn("HD_ProductionPlanningDesk", -10, -5);
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
        map.zoneManager.RegisterZone(stockpile);
        foreach (IntVec3 cell in new CellRect(map.Center.x - 12, map.Center.z - 2, 25, 9))
            if (cell.GetZone(map) == null) stockpile.AddCell(cell);
        Items("Steel", 1000); Items("Chemfuel", 500); Items("HD_CarbonSteel", 30);
        Find.ResearchManager.ResetAllProgress();
        Check(!Def<RecipeDef>("HD_MakeBasicElectricalComponents").AvailableNow, "Research fixture actually locks electrical components");
        Plan.Refresh(); Check(Plan.Tickets.Count == 0, "Unresearched recipes are not queued");
        foreach (var goal in Plan.Goals) goal.Target = 0;
        Plan.Refresh(); Plan.Enabled = false;
        foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            if (recipe.researchPrerequisite != null) Find.ResearchManager.FinishProject(recipe.researchPrerequisite, doCompletionDialog: false);
        foreach (string name in Six) Plan.GoalFor(Def<ThingDef>(name)).Target = name == "HD_GeneralMachinePart" ? 10 : name is "HD_Shaft" or "HD_UniformRolledSteelPlate" ? 1 : 5;
        var manual = (Bill_Production)Def<RecipeDef>("HD_MakeShaft").MakeNewBill(); manual.repeatMode = BillRepeatModeDefOf.TargetCount; manual.targetCount = 1; lathe.BillStack.AddBill(manual);
        manual.SetStoreMode(BillStoreModeDefOf.BestStockpile, null);
        Plan.Enabled = false; Plan.Refresh(); Check(Plan.Tickets.Count == 0, "Disabled plan does not queue");
        Plan.Enabled = true; Plan.Refresh();
        Check(Plan.Tickets.Count > 0 && Plan.Tickets.All(t => t.Bill.recipe.ProducedThingDef.defName != "HD_Shaft"), "Manual pending orders satisfy target without duplicate auto bill");
        int reserved = Plan.Tickets.SelectMany(t => t.Bill.recipe.ingredients.Select(i => (i, t.Bill))).Where(x => x.i.filter.Allows(Def<ThingDef>("HD_CarbonSteel"))).Sum(x => x.i.CountRequiredOfFor(Def<ThingDef>("HD_CarbonSteel"), x.Bill.recipe, x.Bill));
        Check(reserved + 5 <= 30, "Automatic queues share physical carbon-steel budget with manual job");
        int tickets = Plan.Tickets.Count;
        for (int i = 0; i < 10; i++) Plan.Refresh();
        Check(Plan.Tickets.Count == tickets && Plan.Tickets.Select(t => t.Bill.recipe.ProducedThingDef).Distinct().Count() == tickets, "Two desks and repeated updates do not duplicate any product order");
        Items("HD_CarbonSteel", 1000); Plan.Refresh();
        worker.drafter.Drafted = false; worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        int elapsed = 0;
        while ((savedBill = Plan.Tickets.Select(t => t.Bill).OfType<Bill_ProductionWithUft>().FirstOrDefault(b => b.BoundUft != null && b.BoundUft.workLeft > 0)) == null && elapsed < 20000) { yield return 60; elapsed += 60; }
        Check(savedBill?.BoundUft != null, "Native worker starts real queued batch and stores unfinished ingredients");
        worker.drafter.Drafted = true; worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        savedBillId = savedBill.GetUniqueLoadID(); savedWork = savedBill.BoundUft.workLeft; savedTickets = Plan.Tickets.Count;
        stage = 1; GameDataSaveLoader.SaveGame("ProductionPlanning"); load = true; yield return 1;
    }
    public static readonly List<double> DrawTimes = new();
    public static void BeforeDraw(out long __state) => __state = System.Diagnostics.Stopwatch.GetTimestamp();
    public static void AfterDraw(long __state) => DrawTimes.Add(System.Diagnostics.Stopwatch.GetElapsedTime(__state).TotalMilliseconds);
    private static string Stats(List<double> times)
    {
        if (times.Count == 0) return "n=0";
        var sorted = times.OrderBy(t => t).ToList();
        return $"n={times.Count} first={times[0]:F2}ms avg={times.Average():F2}ms p95={sorted[(int)(sorted.Count * 0.95)]:F2}ms max={sorted[^1]:F2}ms";
    }
    // A mid-game sized colony: many item stacks, several machines with queued bills. Measures window draw and planner refresh cost.
    private IEnumerable<int> Perf()
    {
        bool shots = GenCommandLine.TryGetCommandLineArg("hdProductionPlanningShots", out _);
        new HarmonyLib.Harmony("Helodrace.ProductionPlanningPerf").Patch(HarmonyLib.AccessTools.Method(typeof(Dialog_ProductionPlanning), nameof(Dialog_ProductionPlanning.DoWindowContents)),
            prefix: new HarmonyLib.HarmonyMethod(typeof(Checks), nameof(BeforeDraw)), postfix: new HarmonyLib.HarmonyMethod(typeof(Checks), nameof(AfterDraw)));
        foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            if (recipe.researchPrerequisite != null && !recipe.researchPrerequisite.IsFinished) Find.ResearchManager.FinishProject(recipe.researchPrerequisite, doCompletionDialog: false);
        var rand = new Random(17);
        var itemDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.category == ThingCategory.Item && d.stackLimit > 1 && !d.MadeFromStuff && d.EverHaulable && (d.thingClass == typeof(Thing) || d.thingClass == typeof(ThingWithComps))).OrderBy(d => d.defName).ToList();
        var cells = map.AllCells.Where(c => c.Standable(map) && c.GetFirstItem(map) == null).OrderBy(_ => rand.Next()).Take(3000).ToList();
        foreach (IntVec3 cell in cells)
        {
            Thing t = ThingMaker.MakeThing(itemDefs[rand.Next(itemDefs.Count)]); t.stackCount = Math.Max(1, rand.Next(t.def.stackLimit));
            GenSpawn.Spawn(t, cell, map);
        }
        int x = -20;
        foreach (string name in new[] { "HD_BasicWorkbench", "HD_LineShaftMillingMachine", "HD_LineShaftHydraulicPress", "HD_LineShaftRollingMachine", "HD_LineShaftTurretLathe", "HD_BasicWorkbench", "HD_BasicWorkbench" })
        {
            var table = (Building_WorkTable)Spawn(name, x, -14); x += 6;
            foreach (RecipeDef recipe in table.def.AllRecipes.Take(10))
            { var bill = recipe.MakeNewBill(); if (bill is Bill_Production p) { p.repeatMode = BillRepeatModeDefOf.RepeatCount; p.repeatCount = 3; } table.BillStack.AddBill(bill); }
        }
        Spawn("HD_ProductionPlanningDesk", -20, -18);
        foreach (var goal in Plan.Goals) goal.Target = 0;
        Write($"FIXTURE things={map.listerThings.AllThings.Count} items={map.listerThings.AllThings.Count(t => t.def.category == ThingCategory.Item)} tables={map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>().Count()} goals={Plan.Goals.Count}");
        var refresh = new List<double>();
        for (int i = 0; i < 20; i++) { long s = System.Diagnostics.Stopwatch.GetTimestamp(); Plan.Refresh(); refresh.Add(System.Diagnostics.Stopwatch.GetElapsedTime(s).TotalMilliseconds); }
        Write("PERF refresh " + Stats(refresh));
        long opened = System.Diagnostics.Stopwatch.GetTimestamp();
        var dialog = new Dialog_ProductionPlanning(Plan); Find.WindowStack.Add(dialog);
        Write($"PERF construct+add {System.Diagnostics.Stopwatch.GetElapsedTime(opened).TotalMilliseconds:F2}ms");
        int waited = 0;
        while (DrawTimes.Count < 240 && waited++ < 20000) yield return 0;
        Write("PERF draw " + Stats(DrawTimes));
        Check(DrawTimes.Count >= 240, "Planning window drew 240 real frames");
        // Coverage: every Helod item is listed; every item a recipe makes has that recipe in its tree.
        var materials = DefDatabase<ThingDef>.AllDefsListForReading.Where(ProductionCatalog.IsPlanMaterial).ToList();
        var listed = ProductionCatalog.PlanItems.Select(i => i.Def).ToHashSet();
        var made = DefDatabase<RecipeDef>.AllDefsListForReading.Where(r => r.AllRecipeUsers.Any()).SelectMany(r => r.products ?? new()).Select(p => p.thingDef).ToHashSet();
        var plannable = ProductionCatalog.PlanItems.Where(i => i.Plannable.Count > 0).ToList();
        Write($"CATALOG listed={listed.Count} withRecipe={ProductionCatalog.PlanItems.Count(i => !i.Raw)} plannable={plannable.Count} raw={ProductionCatalog.PlanItems.Count(i => i.Raw)} manualOnly={ProductionCatalog.PlanItems.Count(i => !i.Raw && i.Plannable.Count == 0)}");
        Write("LISTED " + string.Join(", ", ProductionCatalog.PlanItems.Select(i => i.Def.defName + (i.Raw ? "(raw)" : i.Plannable.Count == 0 ? "(manual)" : ""))));
        Check(materials.Count == listed.Count && materials.All(listed.Contains), "Planner lists exactly the Helod resources and intermediates");
        Check(new[] { "HD_Alumina", "HD_PigIron", "HD_Charcoal", "HD_AromaticBaseOil", "HD_GeneralMachinePart", "HD_PressComponent" }.All(n => listed.Contains(Def<ThingDef>(n))), "Named resources and intermediates are listed");
        Check(new[] { "WoodLog", "Silver", "Steel", "Plasteel", "HD_CrudeOil_Low", "HD_M60Igniter", "HD_M81Igniter", "HD_ModularP320FluxPartsBox", "HD_TCCC_HemostaticAgent" }.All(n => !listed.Contains(Def<ThingDef>(n))), "Nine requested materials are off the plan list");
        Check(ProductionCatalog.For(Def<ThingDef>("HD_GeneralMachinePart")).Recipes.All(r => r.Recipe.defName != "HD_Convert_SprMOne_To_SprMThreeHigh_Weapon")
            && ProductionCatalog.For(ThingDefOf.ComponentIndustrial).Recipes.All(r => !r.Users.Any(u => u.defName == "HD_SemiconductorFab")), "Rifle conversion is hidden and the fab makes no vanilla components");
        Check(listed.All(d => !d.IsApparel && !d.IsRangedWeapon && d.thingCategories.All(c => c.defName is "ResourcesRaw" or "Manufactured" or "Textiles" or "HD_BlastFurnaceFuel"))
            && new[] { "HD_Ammunition_556M855_Ammo", "HD_Money", "HD_Hardtack", "HD_Grenade_M67_Item", "HD_155mmShell_M107HE", "HD_ArmorPlate_SAPI", "HD_Apparel_GreatWarM1Helmet" }.All(n => !listed.Contains(Def<ThingDef>(n))),
            "No weapon, apparel, food, ammunition or money in the plan");
        Check(ProductionCatalog.For(Def<ThingDef>("HD_AromaticBaseOil")).Plannable.Count > 0, "Two-product aromatic base oil recipe is plannable");
        Check(listed.Where(made.Contains).All(d => !ProductionCatalog.For(d).Raw) && listed.Where(d => !made.Contains(d)).All(d => ProductionCatalog.For(d).Raw), "Every recipe-made plan item shows its recipes; the rest are marked raw");
        Check(Six.All(n => ProductionCatalog.For(Def<ThingDef>(n)).Plannable.Count > 0) && ProductionCatalog.For(Def<ThingDef>("HD_GeneralMachinePart")).Plannable.All(r => !r.Recipe.defName.Contains("Convert")), "Six basic components stay plannable without emergency conversions");
        bool listedMaterial(ThingDef d) => listed.Contains(d);
        foreach (var item in plannable) Plan.GoalFor(item.Def).Target = Math.Min(MapComponent_ProductionPlanning.MaxTarget, Plan.Stock(item.Def) + 5);
        var refreshAll = new List<double>();
        for (int i = 0; i < 20; i++) { long s = System.Diagnostics.Stopwatch.GetTimestamp(); Plan.Refresh(); refreshAll.Add(System.Diagnostics.Stopwatch.GetElapsedTime(s).TotalMilliseconds); }
        Write($"PERF refresh-all-goals goals={Plan.Goals.Count(g => g.Target > 0)} tickets={Plan.Tickets.Count} " + Stats(refreshAll));
        Check(Plan.Tickets.Select(t => t.Product).Distinct().Count() == Plan.Tickets.Count && Plan.Tickets.All(t => listedMaterial(t.Product)), "Material plan refresh never duplicates or queues a non-plan item");
        // Make-quantity orders are separate from target stock; repeat-forever bills show endless pending.
        foreach (var goal in Plan.Goals) { goal.Target = 0; goal.Quantity = 0; }
        // The fixture bills reserve all bench materials; clear them so the order budget is known.
        foreach (var table in map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>()) foreach (Bill bill in table.BillStack.Bills.ToList()) table.BillStack.Delete(bill);
        Plan.Refresh();
        var electrical = Def<ThingDef>("HD_BasicElectricalComponents");
        foreach (var ingredient in ProductionCatalog.For(electrical).Plannable.SelectMany(r => r.Ingredients).Where(i => i.Def != null))
            Items(ingredient.Def.defName, (int)Math.Ceiling(ingredient.Count) * 12);
        Plan.GoalFor(electrical).Quantity = 3; Plan.Refresh();
        var quantityTicket = Plan.Tickets.SingleOrDefault(t => t.Product == electrical && t.Quantity);;
        Check(quantityTicket != null && Plan.Tickets.Count(t => t.Product == electrical) == 1, "Make quantity queues its own batch with no target stock");
        Plan.GoalFor(electrical).Target = Plan.Stock(electrical) + 1; Plan.Refresh();
        Check(Plan.Tickets.Count(t => t.Product == electrical && !t.Quantity) == 1 && Plan.Tickets.Contains(quantityTicket), "Target stock ignores make-quantity output and queues its own batch");
        int before = Plan.GoalFor(electrical).Quantity; Plan.RecordCompletion(quantityTicket.Bill);
        Check(Plan.GoalFor(electrical).Quantity == before - quantityTicket.Output, "Finished make-quantity batch counts the order down");
        Plan.GoalFor(electrical).Quantity = 0; Plan.Refresh();
        Check(!Plan.Tickets.Any(t => t.Product == electrical && t.Quantity), "Make quantity 0 cancels the unstarted batch");
        Plan.GoalFor(electrical).Target = 0; Plan.Refresh();
        var lathe = map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>().First(t => t.def.defName == "HD_LineShaftTurretLathe");
        var forever = (Bill_Production)Def<RecipeDef>("HD_MakeShaft").MakeNewBill(); forever.repeatMode = BillRepeatModeDefOf.Forever; lathe.BillStack.AddBill(forever);
        Plan.InvalidateCounts();
        Check(Plan.PendingIsEndless(Def<ThingDef>("HD_Shaft")) && !Plan.PendingIsEndless(electrical), "Repeat-forever bill marks pending as endless");
        forever.suspended = true; Plan.InvalidateCounts();
        Check(!Plan.PendingIsEndless(Def<ThingDef>("HD_Shaft")), "Suspended repeat-forever bill is not endless");
        lathe.BillStack.Delete(forever);
        // Deepest tree among plannable items, fully expanded.
        ThingDef deep = null; int deepest = 0;
        foreach (var item in plannable) { dialog.Select(item.Def); dialog.ExpandAll(); if (dialog.VisibleNodes > deepest) { deepest = dialog.VisibleNodes; deep = item.Def; } }
        dialog.Select(deep); long built = System.Diagnostics.Stopwatch.GetTimestamp(); dialog.ExpandAll();
        Write($"TREE {deep.defName} expandedNodes={dialog.VisibleNodes} build={System.Diagnostics.Stopwatch.GetElapsedTime(built).TotalMilliseconds:F2}ms");
        Check(dialog.VisibleNodes > 10, "Expand all builds a multi-level flowchart");
        DrawTimes.Clear(); waited = 0;
        while (DrawTimes.Count < 240 && waited++ < 20000) yield return 0;
        Write("PERF draw-expanded " + Stats(DrawTimes));
        if (shots) { UnityEngine.ScreenCapture.CaptureScreenshot(Path.Combine(output, "flowchart-expanded.png")); for (int i = 0; i < 30; i++) yield return 0; }
        dialog.Select(Def<ThingDef>("HD_BasicElectronicComponents"));
        int initial = dialog.VisibleNodes;
        for (int i = 0; i < 10; i++) yield return 0;
        if (shots) { UnityEngine.ScreenCapture.CaptureScreenshot(Path.Combine(output, "flowchart-electronic.png")); for (int i = 0; i < 30; i++) yield return 0; }
        dialog.ExpandAll(); int expandedRows = dialog.VisibleNodes;
        dialog.CollapseAll(); for (int i = 0; i < 3; i++) yield return 0;
        int folded = dialog.VisibleNodes; dialog.Toggle("HD_BasicElectronicComponents|HD_MakeBasicElectronicComponents");
        Check(dialog.VisibleNodes > folded, "Unfolding one recipe box shows its inputs");
        dialog.Toggle("HD_BasicElectronicComponents|HD_MakeBasicElectronicComponents");
        Write($"TREE HD_BasicElectronicComponents initial={initial} expanded={expandedRows} collapsed={dialog.VisibleNodes}");
        Check(expandedRows > initial && dialog.VisibleNodes == 1 + ProductionCatalog.For(Def<ThingDef>("HD_BasicElectronicComponents")).Recipes.Count, "Expand and collapse change the electronic-component flowchart");
        dialog.Close(); yield return 1;
    }
    private IEnumerable<int> Resume()
    {
        if (stage == 2)
        {
            Check(Plan.Goals.Sum(g => g.Completed) == 36 && Plan.Goals.Single(g => g.Product.defName == "HD_Shaft").Completed == 5, "Completed counters persist exactly after second native save/load");
            Plan.Refresh(); Check(Plan.Tickets.Count == 0 && Plan.Goals.All(g => Plan.Stock(g.Product) == g.Target), "Finished shared targets remain satisfied after second load");
            yield return 1; yield break;
        }
        savedBill = Plan.Tickets.Select(t => t.Bill).OfType<Bill_ProductionWithUft>().Single(b => b.GetUniqueLoadID() == savedBillId);
        Check(savedBill.BoundUft != null && savedBill.BoundUft.workLeft == savedWork && Plan.Tickets.Count == savedTickets, "Native bills, ownership and exact unfinished work survive save/load");
        Plan.Refresh(); Check(Plan.Tickets.Count == savedTickets, "First refresh after load does not duplicate pending jobs");
        var desks = map.listerBuildings.allBuildingsColonist.OfType<Building_ProductionPlanningDesk>().ToList();
        foreach (var d in desks) d.DeSpawn(); Plan.Refresh(); Check(Plan.Tickets.All(t => t.Bill.suspended), "Removing last desk pauses owned work");
        GenSpawn.Spawn(desks[0], map.Center + new IntVec3(-13, 0, -5), map); Plan.Refresh();
        Check(Plan.Tickets.All(t => !t.Bill.suspended) && savedBill.BoundUft.workLeft == savedWork, "Reinstalling desk resumes same bills without losing unfinished work");
        worker.drafter.Drafted = false; worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        int elapsed = 0;
        while (Plan.Goals.Any(g => Plan.Stock(g.Product) < g.Target) && elapsed < 180000)
        {
            engine.GetComp<CompRefuelable>().Refuel(120); yield return 500; elapsed += 500;
        }
        Write("FINAL " + string.Join(";", Plan.Goals.Select(g => $"{g.Product.defName}: stock={Plan.Stock(g.Product)} target={g.Target} completed={g.Completed} pending={Plan.Pending(g.Product)}")) + "; worker=" + worker.CurJobDef);
        Check(Plan.Goals.All(g => Plan.Stock(g.Product) >= g.Target), "Native AI completes all six component stock goals on real powered machines");
        yield return 500;
        Check(Plan.Goals.All(g => Plan.Stock(g.Product) == g.Target) && Plan.Tickets.Count == 0, "No surplus or leftover automatic bill after targets satisfied");
        Check(Plan.Goals.Single(g => g.Product.defName == "HD_Shaft").Completed == 0 && Plan.Goals.Where(g => g.Product.defName != "HD_Shaft").All(g => g.Completed >= g.Target), "Completion counters include only actual planner-made batches");
        var shaft = Plan.Goals.Single(g => g.Product.defName == "HD_Shaft"); shaft.Target += 5; Plan.Refresh();
        elapsed = 0;
        while (Plan.Stock(shaft.Product) < shaft.Target && elapsed < 60000) { yield return 250; elapsed += 250; }
        Check(shaft.Completed == 5 && Plan.Stock(shaft.Product) == 6, "Native AI also completes an automatically queued shaft batch");
        yield return 250;
        var target = Plan.Goals.Single(g => g.Product.defName == "HD_PressComponent"); worker.drafter.Drafted = true;
        IntVec3 enginePosition = engine.Position; engine.DeSpawn(); yield return 250;
        target.Target += 10; Plan.Refresh(); Check(Plan.Tickets.Count == 0, "Stalled mechanical machine receives no new automatic bill");
        GenSpawn.Spawn(engine, enginePosition, map); yield return 250; Plan.Refresh();
        Check(Plan.Tickets.Count == 1 && Plan.Tickets[0].Bill.recipe.defName.EndsWith("Bulk10"), "Deficit of ten chooses efficient ten-piece batch");
        target.Target -= 10; Plan.Refresh(); Check(Plan.Tickets.Count == 0, "Lowered target cancels unstarted extra automatic order");
        target.Target += 5; Plan.Refresh(); Check(Plan.Tickets.Count == 1, "New stock deficit queues replacement");
        Items("HD_PressComponent", 5); Plan.Refresh(); Check(Plan.Tickets.Count == 0, "Delivered/traded stock cancels unnecessary unstarted production");
        target.Target += 10; Plan.Refresh(); var oldBatch = Plan.Tickets.Single().Bill;
        Plan.Enabled = false; Plan.Refresh(); target.Target -= 5; Plan.Enabled = true; Plan.Refresh();
        Check(Plan.Tickets.Count == 1 && Plan.Tickets[0].Bill != oldBatch && Plan.Tickets[0].Bill.recipe.defName.EndsWith("Bulk5"), "Lowering target while paused replaces oversized unstarted batch before resuming");
        target.Target -= 5; Plan.Refresh();
        var dialog = new Dialog_ProductionPlanning(Plan); Find.WindowStack.Add(dialog); yield return 1; dialog.Close();
        Check(!Find.WindowStack.IsOpen<Dialog_ProductionPlanning>(), "Native planning window opens, draws and closes");
        savedBill = null; stage = 2; GameDataSaveLoader.SaveGame("ProductionPlanning"); load = true; yield return 1;
    }
}
