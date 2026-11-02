using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Helodrace;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.ProductionStage1;

public sealed partial class Checks : GameComponent
{
    private bool complete;
    private string output;
    private Map map;
    private Pawn worker;
    private IEnumerator<int> steps;
    private int remainingTicks;
    private bool started;
    private Action sampleTick;
    private Building_WorkTable mechanicalTable;
    private WorkGiver_DoBill mechanicalGiver;
    private Bill_Production mechanicalBill;
    public Checks(Game game) { }

    public override void GameComponentTick()
    {
        if (started || complete || Find.CurrentMap == null
            || !GenCommandLine.TryGetCommandLineArg("hdProductionStage1", out output)) return;
        started = true;
        try
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "checks.txt"), "Actual RimWorld 1.7 production checks\n");
            map = Find.CurrentMap;
            ConfigureFixture();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(p => p.Faction != Faction.OfPlayer).ToList()) pawn.DeSpawn();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                EnsureBodyType(pawn);
                pawn.drafter.Drafted = true;
            }
            foreach (IntVec3 cell in CellRect.CenteredOn(map.Center, 30))
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing is not Pawn && thing.Spawned)
                    {
                        if (thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
                        else thing.DeSpawn();
                    }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
                map.fogGrid.Unfog(cell);
                map.areaManager.Home[cell] = true;
            }
            var request = new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"), Faction.OfPlayer,
                canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25);
            request.ForceNoBackstory = true;
            worker = PawnGenerator.GeneratePawn(request);
            EnsureBodyType(worker);
            GenSpawn.Spawn(worker, map.Center + new IntVec3(0, 0, -8), map);
            worker.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
            worker.workSettings.EnableAndInitialize();
            worker.workSettings.SetPriority(WorkTypeDefOf.Crafting, 1);
            worker.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Tailoring"), 1);
            worker.workSettings.SetPriority(WorkTypeDefOf.Hauling, 2);
            for (int hour = 0; hour < 24; hour++) worker.timetable.SetAssignment(hour, TimeAssignmentDefOf.Work);
            foreach (SkillRecord skill in worker.skills.skills) skill.Level = 12;
            bool fuelOnly = GenCommandLine.TryGetCommandLineArg("hdProductionFuelOnly", out _);
            if (!fuelOnly) Snapshot();
            if (GenCommandLine.TryGetCommandLineArg("hdProductionBaseline", out _)) Finish();
            else
            {
                Find.TickManager.Pause();
                steps = (fuelOnly ? FuelJobs() : AllJobs()).GetEnumerator();
            }
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL: " + error + "\n");
            Log.Error("Production stage 1 checks: " + error);
            Waylith.EngineApplication.Instance.Quit(1);
        }
    }

    public override void GameComponentUpdate()
    {
        if (complete || steps == null) return;
        try
        {
            if (remainingTicks == 0)
            {
                if (!steps.MoveNext()) { Finish(); return; }
                if (loadRequested)
                {
                    loadRequested = false;
                    steps = null;
                    GameDataSaveLoader.LoadGame("ProductionStage1Roundtrip");
                    return;
                }
                remainingTicks = steps.Current;
            }
            int batch = Math.Min(remainingTicks, 200);
            for (int i = 0; i < batch; i++)
            {
                Find.TickManager.DoSingleTick();
                sampleTick?.Invoke();
            }
            remainingTicks -= batch;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                foreach (Need need in pawn.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        }
        catch (Exception error)
        {
            complete = true;
            Write("FAIL: " + error);
            Log.Error("Production stage 1 checks: " + error);
            Waylith.EngineApplication.Instance.Quit(1);
        }
    }

    private IEnumerable<int> AllJobs()
    {
        foreach (int ticks in WorktableJobs()) yield return ticks;
        foreach (int ticks in FuelJobs()) yield return ticks;
    }

    private void Finish()
    {
        complete = true;
        Write("COMPLETE");
        Waylith.EngineApplication.Instance.Quit(0);
    }

    private static void ConfigureFixture()
    {
        DebugSettings.noAnimals = true;
        DebugSettings.enableRandomMentalStates = false;
        Find.Storyteller.difficulty.allowBigThreats = false;
        Find.Storyteller.difficulty.allowIntroThreats = false;
        Find.Storyteller.difficulty.allowExtremeWeatherIncidents = false;
        Find.Storyteller.difficulty.diseaseIntervalFactor = 100000f;
    }

    private static void EnsureBodyType(Pawn pawn)
    {
        // ForceNoBackstory leaves this required renderer input unset in the
        // current host. The fixture must still provide a valid pawn body type.
        if (pawn.story != null && pawn.story.bodyType == null)
            pawn.story.bodyType = pawn.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
    }

    private void Snapshot()
    {
        Building_WorkTable table = Table("HD_LineShaftTurretLathe", new IntVec3(-8, 0, 0));
        RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed("HD_MakeShaft");
        if (recipe.researchPrerequisite != null) Find.ResearchManager.FinishProject(recipe.researchPrerequisite, doCompletionDialog: false);
        Bill_Production bill = (Bill_Production)recipe.MakeNewBill();
        table.BillStack.AddBill(bill);
        foreach (IngredientCount ingredient in recipe.ingredients)
        {
            ThingDef def = ingredient.filter.AnyAllowedDef;
            Thing stack = ThingMaker.MakeThing(def);
            stack.stackCount = (int)Math.Ceiling(ingredient.GetBaseCount());
            GenPlace.TryPlaceThing(stack, table.InteractionCell + new IntVec3(1, 0, -1), map, ThingPlaceMode.Near);
        }
        WorkGiver_DoBill giver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsHD_LineShaftTurretLathe").Worker;
        mechanicalTable = table; mechanicalGiver = giver; mechanicalBill = bill;
        bool baseline = GenCommandLine.TryGetCommandLineArg("hdProductionBaseline", out _);
        bool available = table.UsableForBillsAfterFueling();
        Job job = giver.JobOnThing(worker, table, true);
        Write("SNAPSHOT: unpowered usableAfterFueling=" + available + "; job=" + job?.def.defName);
        if (!baseline) Check(!available && job == null, "unpowered mechanical bench cannot generate a bill job");
        table.GetComp<CompMechanicalUser>().UpdatePowerStatus(true, 1f);
        job = giver.JobOnThing(worker, table, true);
        Write("SNAPSHOT: powered job=" + job?.def.defName + "; shouldDo=" + bill.ShouldDoNow()
            + "; allowed=" + bill.PawnAllowedToStartAnew(worker) + "; recipe=" + recipe.AvailableNow
            + "; reason=" + JobFailReason.Reason);
        if (!baseline) Check(job?.def == JobDefOf.DoBill, "powered mechanical bench generates a bill job");

        ThingDef clothing = DefDatabase<ThingDef>.GetNamed("HD_ClothingBench");
        var clothingGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading
            .Where(def => typeof(WorkGiver_DoBill).IsAssignableFrom(def.giverClass)
                && def.fixedBillGiverDefs?.Contains(clothing) == true).ToList();
        Write("SNAPSHOT: clothing work givers=" + string.Join(",", clothingGivers.Select(def => def.defName)));
        if (!baseline) Check(clothingGivers.Count > 0, "clothing bench has an automatic/ordered bill work giver");
    }

    private IEnumerable<int> WorktableJobs()
    {
        ThingDef engineDef = DefDatabase<ThingDef>.GetNamed("HD_SmallSteamEngine");
        var engine = (Building)ThingMaker.MakeThing(engineDef, GenStuff.DefaultStuffFor(engineDef));
        engine.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(engine, map.Center + new IntVec3(-11, 0, 0), map);
        engine.GetComp<CompRefuelable>().Refuel(25f);
        var shaft = (Building)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_LineShaft"));
        shaft.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(shaft, map.Center + new IntVec3(-9, 0, 0), map);
        yield return 120;
        Check(mechanicalTable.GetComp<CompMechanicalUser>().HasPower, "actual engine and shaft supply the worktable");
        Job job = mechanicalGiver.JobOnThing(worker, mechanicalTable, true);
        Check(worker.CurJob?.bill == mechanicalBill || job?.def == JobDefOf.DoBill && worker.jobs.TryTakeOrderedJob(job),
            "mechanical bill starts automatically or through an ordered job");
        int waited = 0;
        while (worker.jobs.curDriver is not JobDriver_DoBill driver || driver.ticksSpentDoingRecipeWork < 100)
        {
            if (waited >= 5000) throw new InvalidOperationException("Mechanical bill did not reach recipe work; current=" + worker.CurJobDef);
            waited += 60;
            yield return 60;
        }
        shaft.DeSpawn();
        yield return 120;
        Check(!mechanicalTable.CurrentlyUsableForBills() && worker.CurJobDef != JobDefOf.DoBill,
            "removing the shaft ends active work without a recovery job");
        Check(mechanicalGiver.JobOnThing(worker, mechanicalTable, true) == null, "disconnected bench cannot reschedule the bill");
        GenSpawn.Spawn(shaft, map.Center + new IntVec3(-9, 0, 0), map);
        yield return 120;
        Check(mechanicalTable.GetComp<CompMechanicalUser>().HasPower, "reinstalling the shaft restores power");
        waited = 0;
        while (mechanicalBill.repeatCount > 0 && waited < 8000)
        {
            if (worker.CurJob?.bill != mechanicalBill && worker.CurJob?.playerForced != true)
            {
                job = mechanicalGiver.JobOnThing(worker, mechanicalTable, true);
                if (job != null) worker.jobs.TryTakeOrderedJob(job);
            }
            waited += 120;
            yield return 120;
        }
        Check(mechanicalBill.repeatCount == 0, "reconnected worktable completes exactly one product; current="
            + worker.CurJobDef + "; reason=" + JobFailReason.Reason + "; workLeft="
            + (worker.jobs.curDriver as JobDriver_DoBill)?.workLeft + "; billAllowed=" + mechanicalBill.PawnAllowedToStartAnew(worker)
            + "; ingredients=" + map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_CarbonSteel")).Sum(t => t.stackCount));
        Check(map.listerThings.ThingsOfDef(mechanicalBill.recipe.ProducedThingDef).Sum(t => t.stackCount) == 1,
            "power interruption does not duplicate output");

        worker.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Tailoring"), 0);
        Building_WorkTable clothing = Table("HD_ClothingBench", new IntVec3(8, 0, 0));
        RecipeDef recipe = clothing.def.AllRecipes.First(r => r.ProducedThingDef?.defName == "HD_Apparel_WildWestFormalShirt");
        if (recipe.researchPrerequisite != null) Find.ResearchManager.FinishProject(recipe.researchPrerequisite, doCompletionDialog: false);
        Bill_Production bill = AddBillAndIngredients(clothing, recipe);
        var giver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsHD_ClothingBench").Worker;
        yield return 60;
        Check(!giver.ShouldSkip(worker) && giver.JobOnThing(worker, clothing)?.def == JobDefOf.DoBill,
            "automatic tailoring finds the clothing bill");
        job = giver.JobOnThing(worker, clothing, true);
        Check(job?.def == JobDefOf.DoBill && worker.jobs.TryTakeOrderedJob(job), "forced clothing bill starts");
        worker.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Tailoring"), 1);
        waited = 0;
        while (bill.repeatCount > 0 && waited < 12000) { waited += 120; yield return 120; }
        Check(bill.repeatCount == 0 && map.listerThings.ThingsOfDef(recipe.ProducedThingDef).Count == 1,
            "clothing worktable completes the actual garment");
        int beforeAutomatic = CountPhysical(recipe.ProducedThingDef);
        Bill_Production automaticBill = AddBillAndIngredients(clothing, recipe);
        waited = 0;
        while (automaticBill.repeatCount > 0 && waited < 12000) { waited += 120; yield return 120; }
        Check(automaticBill.repeatCount == 0 && CountPhysical(recipe.ProducedThingDef) == beforeAutomatic + 1,
            "normal pawn work scheduling completes another garment without an ordered job");

        var preset = DefDatabase<ModularArmorPresetDef>.GetNamed("HD_ArmorPreset_IBTV_Rifleman");
        var apparel = (Apparel)ThingMaker.MakeThing(preset.armorDef, GenStuff.DefaultStuffFor(preset.armorDef));
        var armor = apparel.GetComp<CompModularArmor>();
        Check(armor.TryApplyPreset(preset, out string reason), "actual armor preset created: " + reason);
        worker.apparel.Wear(apparel, true);
        var plate = armor.InstalledParts.First(part => part.CanSwapFrontRearPlates);
        bool swapped = plate.plateOrderSwapped;
        Check(armor.TryStartFrontRearSwap(out reason, clothing), "plate swap job starts at the clothing bench: " + reason);
        yield return 1200;
        Check(plate.plateOrderSwapped != swapped, "actual plate swap job changes plate order");
        var draft = (CompModularArmor)AccessTools.Method(typeof(CompModularArmor), "CreateEditorDraft").Invoke(armor, null);
        string signature = (string)AccessTools.Method(typeof(CompModularArmor), "ConfigurationSignature").Invoke(armor, null);
        var pouch = draft.InstalledParts.First(part => part.IsPalsMounted);
        int previousCount = armor.InstalledParts.Count;
        Check(draft.RemovePalsPart(pouch), "armor editor draft removes an installed pouch");
        object[] arguments = { draft, signature, clothing, null };
        Check((bool)AccessTools.Method(typeof(CompModularArmor), "TryStartCustomization").Invoke(armor, arguments),
            "armor customization job starts: " + arguments[3]);
        yield return 1600;
        Check(armor.InstalledParts.Count == previousCount - 1, "actual customization job applies the edited configuration");
    }

    private Bill_Production AddBillAndIngredients(Building_WorkTable table, RecipeDef recipe)
    {
        var bill = (Bill_Production)recipe.MakeNewBill();
        bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor, null);
        table.BillStack.AddBill(bill);
        foreach (IngredientCount ingredient in recipe.ingredients)
        {
            ThingDef def = ingredient.filter.Allows(ThingDefOf.Cloth) ? ThingDefOf.Cloth : ingredient.filter.AnyAllowedDef;
            int count = ingredient.CountRequiredOfFor(def, recipe, bill);
            while (count > 0)
            {
                Thing stack = ThingMaker.MakeThing(def);
                stack.stackCount = Math.Min(count, def.stackLimit);
                count -= stack.stackCount;
                GenPlace.TryPlaceThing(stack, table.InteractionCell + new IntVec3(1, 0, -1), map, ThingPlaceMode.Near);
            }
        }
        return bill;
    }

    private Building_WorkTable Table(string name, IntVec3 offset)
    {
        ThingDef def = DefDatabase<ThingDef>.GetNamed(name);
        var table = (Building_WorkTable)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
        table.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(table, map.Center + offset, map);
        return table;
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Write("PASS: " + message);
    }
    private void Write(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + "\n");
}
