using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.ProductionStage1;

public sealed partial class Checks
{
    private Building_WorkTable furnace;
    private Pawn hauler;
    private bool roundtripPending, loadRequested;
    private float savedWorkLeft;
    private int savedIngredients;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref worker, "productionWorker");
        Scribe_References.Look(ref hauler, "productionHauler");
        Scribe_References.Look(ref furnace, "productionFurnace");
        Scribe_Values.Look(ref roundtripPending, "productionRoundtrip");
        Scribe_Values.Look(ref savedWorkLeft, "productionWorkLeft");
        Scribe_Values.Look(ref savedIngredients, "productionIngredients");
    }

    public override void LoadedGame()
    {
        if (!roundtripPending || !GenCommandLine.TryGetCommandLineArg("hdProductionStage1", out output)) return;
        started = true;
        ConfigureFixture();
        foreach (Map loadedMap in Find.Maps)
            foreach (Pawn pawn in loadedMap.mapPawns.FreeColonistsSpawned) EnsureBodyType(pawn);
        SetWork(worker, WorkTypeDefOf.Crafting);
        Find.TickManager.Pause();
        steps = (GenCommandLine.TryGetCommandLineArg("hdProductionSustainOnly", out _)
            ? SustainOnly() : AfterLoad()).GetEnumerator();
    }

    private IEnumerable<int> SustainOnly()
    {
        map = Find.CurrentMap;
        foreach (int ticks in SustainedEngines()) yield return ticks;
    }

    private IEnumerable<int> FuelJobs()
    {
        SetWork(worker, null);
        hauler = NewHauler(new IntVec3(0, 0, 8));
        furnace = Table("HD_BlastFurnace", new IntVec3(-8, 0, 12));
        Table("HD_Converter", new IntVec3(-3, 0, 12));
        RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed("HD_RefineCarbonSteelLarge");
        Find.ResearchManager.FinishProject(recipe.researchPrerequisite, doCompletionDialog: false);
        var bill = (Bill_ProductionWithUft)AddBillAndIngredients(furnace, recipe);
        var fuel = furnace.GetComp<CompRefuelable>();
        fuel.allowAutoRefuel = false;
        var giver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsHD_BlastFurnace").Worker;
        SpawnFuel(furnace, 400);
        yield return 120;
        Check(!furnace.CurrentlyUsableForBills() && giver.JobOnThing(worker, furnace, true)?.def.defName == "HD_RefuelIndustrialMachine",
            "empty furnace offers refueling rather than recipe work");
        Check(recipe.Worker.AvailableOnNow(furnace), "actual linked converter unlocks carbon steel at the furnace");
        Check(!recipe.recipeUsers.Any(def => def.defName == "HD_Converter"), "converter is a facility rather than an unfueled second recipe giver");
        worker.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TestSlowLabor"));
        float speed = worker.GetStatValue(StatDefOf.GeneralLaborSpeed);
        Check(speed > 0f && speed < 0.74f, "slow worker needs more than one full fuel charge; speed=" + speed);
        Write("BUDGET: large carbon steel work=" + recipe.workAmount + "; speed=" + speed
            + "; nominalTicks=" + recipe.workAmount / speed + "; nominalFuel=" + recipe.workAmount / speed * fuel.Props.fuelConsumptionRate / 60000f);
        SetWork(worker, WorkTypeDefOf.Crafting);
        fuel.Refuel(fuel.Props.fuelCapacity);
        int elapsed = 0;
        while (fuel.HasFuel && elapsed < 30000)
        {
            StartBill(giver, furnace, bill);
            elapsed += 120;
            yield return 120;
        }
        Check(!fuel.HasFuel && !furnace.CurrentlyUsableForBills(), "actual slow production exhausts a full fuel charge and stops");
        Check(bill.BoundUft != null && bill.BoundUft.workLeft > 0f && bill.BoundUft.workLeft < recipe.workAmount,
            "fuel interruption preserves a partially completed smelting batch");
        savedWorkLeft = bill.BoundUft.workLeft;
        savedIngredients = bill.BoundUft.ingredients.Sum(t => t.stackCount);
        Check(savedIngredients == 224 && bill.repeatCount == 1, "unfinished batch owns exactly the original ingredients without producing output");
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        yield return 120;
        Check(bill.BoundUft.workLeft == savedWorkLeft, "manual interruption keeps the same saved progress");
        roundtripPending = true;
        GameDataSaveLoader.SaveGame("ProductionStage1Roundtrip");
        Write("SAVE: unfinished work=" + savedWorkLeft + "; ingredients=" + savedIngredients);
        loadRequested = true;
        yield return 0;
    }

    private IEnumerable<int> AfterLoad()
    {
        map = Find.CurrentMap;
        var bill = (Bill_ProductionWithUft)furnace.BillStack.Bills.Single();
        Check(bill.BoundUft != null && bill.BoundUft.Creator == worker && bill.BoundUft.workLeft == savedWorkLeft,
            "actual save/load preserves unfinished work, maker and bill binding before the next simulation tick");
        Check(bill.BoundUft.ingredients.Sum(t => t.stackCount) == savedIngredients && !furnace.GetComp<CompRefuelable>().HasFuel,
            "actual save/load preserves physical ingredients and empty fuel state");
        SpawnFuel(furnace, 1000);
        var refueler = (WorkGiver_RefuelIndustrialMachine)DefDatabase<WorkGiverDef>.GetNamed("HD_RefuelIndustrialMachine").Worker;
        var giver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsHD_BlastFurnace").Worker;
        CompRefuelable fuel = furnace.GetComp<CompRefuelable>();
        Check(!refueler.HasJobOnThing(hauler, furnace), "disabled automatic refueling stays disabled after load");
        fuel.allowAutoRefuel = true;
        Job refill = refueler.JobOnThing(hauler, furnace);
        Check(refill != null && hauler.jobs.TryTakeOrderedJob(refill), "hauler starts an actual industrial refuel job after load");
        int elapsed = 0;
        while (!fuel.HasFuel && elapsed < 3000) { elapsed += 60; yield return 60; }
        Check(fuel.HasFuel, "refuel job supplies physical fuel to the loaded furnace");
        elapsed = 0;
        while (bill.repeatCount > 0 && elapsed < 60000)
        {
            StartBill(giver, furnace, bill);
            if (hauler.CurJob?.playerForced != true && refueler.HasJobOnThing(hauler, furnace))
                hauler.jobs.TryTakeOrderedJob(refueler.JobOnThing(hauler, furnace));
            elapsed += 120;
            yield return 120;
        }
        Check(bill.repeatCount == 0 && map.listerThings.ThingsOfDef(bill.recipe.ProducedThingDef).Sum(t => t.stackCount) == 165,
            "slow large batch finishes after refueling with exactly 165 carbon steel");
        Check(map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_UnfinishedSmeltingBatch")).Count == 0,
            "completed smelting consumes its unfinished batch");
        foreach (Hediff hediff in worker.health.hediffSet.hediffs.Where(h => h.def.defName == "HD_TestSlowLabor").ToList()) worker.health.RemoveHediff(hediff);
        Write("BUDGET: normal labor speed=" + worker.GetStatValue(StatDefOf.GeneralLaborSpeed));

        bill = (Bill_ProductionWithUft)AddBillAndIngredients(furnace, bill.recipe);
        fuel.Refuel(fuel.Props.fuelCapacity);
        elapsed = 0;
        while ((worker.jobs.curDriver as JobDriver_DoBill)?.ticksSpentDoingRecipeWork < 60 || worker.CurJob?.bill != bill)
        {
            if (elapsed >= 5000) throw new InvalidOperationException("Normal furnace worker did not reach recipe work.");
            StartBill(giver, furnace, bill);
            elapsed += 60;
            yield return 60;
        }
        fuel.ConsumeFuel(fuel.Fuel - fuel.Props.fuelCapacity * 0.7f);
        Check(refueler.HasJobOnThing(hauler, furnace), "occupied furnace requests early refueling through its service reservation");
        Check(hauler.jobs.TryTakeOrderedJob(refueler.JobOnThing(hauler, furnace)), "another pawn can reserve the working furnace for fuel service");
        Pawn secondHauler = NewHauler(new IntVec3(3, 0, 8));
        Check(!refueler.HasJobOnThing(secondHauler, furnace), "service reservation prevents two simultaneous refuelers");
        secondHauler.DeSpawn();
        float before = fuel.Fuel;
        int workBefore = ((JobDriver_DoBill)worker.jobs.curDriver).ticksSpentDoingRecipeWork;
        yield return 900;
        Check(fuel.Fuel > before && worker.CurJob?.bill == bill && ((JobDriver_DoBill)worker.jobs.curDriver).ticksSpentDoingRecipeWork > workBefore,
            "fuel is replenished while the original worker continues production");
        fuel.ConsumeFuel(fuel.Fuel - fuel.Props.fuelCapacity * 0.7f);
        before = fuel.Fuel;
        SetWork(hauler, WorkTypeDefOf.Hauling);
        elapsed = 0;
        while (fuel.Fuel <= before && elapsed < 3000) { elapsed += 60; yield return 60; }
        Write("SERVICE: shouldAuto=" + fuel.ShouldAutoRefuelNow + "; allowAuto=" + fuel.allowAutoRefuel
            + "; full=" + fuel.IsFull + "; forbidden=" + furnace.IsForbidden(hauler)
            + "; burning=" + furnace.IsBurning() + "; serviceReservable="
            + hauler.CanReserve(furnace.Position, 1, -1, DefDatabase<ReservationLayerDef>.GetNamed("HD_IndustrialRefueling"))
            + "; fuelOnMap=" + map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(t => fuel.Props.fuelFilter.Allows(t)).Sum(t => t.stackCount)
            + "; makerMental=" + worker.InMentalState + "; haulerMental=" + hauler.InMentalState);
        Check(fuel.Fuel > before && worker.CurJob?.bill == bill,
            "normal hauling AI refuels an occupied furnace without an ordered refuel job; fuelBefore=" + before
            + "; fuel=" + fuel.Fuel + "; makerJob=" + worker.CurJobDef + "; remaining=" + bill.BoundUft?.workLeft
            + "; repeats=" + bill.repeatCount + "; haulerJob=" + hauler.CurJobDef + "; drafted=" + hauler.Drafted
            + "; disabledHauling=" + hauler.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)
            + "; eligible=" + refueler.HasJobOnThing(hauler, furnace));
        SetWork(hauler, null);
        elapsed = 0;
        while (bill.repeatCount > 0 && elapsed < 20000)
        {
            StartBill(giver, furnace, bill);
            elapsed += 120;
            yield return 120;
        }
        Check(bill.repeatCount == 0 && CountPhysical(bill.recipe.ProducedThingDef) == 330,
            "normal large batch finishes without lost ingredients or duplicate output; repeats=" + bill.repeatCount
            + "; output=" + CountPhysical(bill.recipe.ProducedThingDef) + "; currentJob=" + worker.CurJobDef
            + "; workLeft=" + bill.BoundUft?.workLeft + "; fuel=" + fuel.Fuel);
        Write("BUDGET: normal labor speed=" + worker.GetStatValue(StatDefOf.GeneralLaborSpeed));
        foreach (int ticks in BatchMeasurements()) yield return ticks;
        foreach (int ticks in LoadedWorktableJobs()) yield return ticks;
        foreach (int ticks in SustainedEngines()) yield return ticks;
    }

    private void StartBill(WorkGiver_DoBill giver, Building_WorkTable table, Bill_Production bill)
    {
        if (worker.CurJob?.bill == bill || worker.CurJob?.playerForced == true) return;
        Job job = giver.JobOnThing(worker, table, true);
        if (job != null) worker.jobs.TryTakeOrderedJob(job);
    }

    private Pawn NewHauler(IntVec3 offset)
    {
        var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            canGeneratePawnRelations: false, fixedBiologicalAge: 25, fixedChronologicalAge: 25);
        request.ForceNoBackstory = true;
        request.ForcedXenotype = XenotypeDefOf.Baseliner;
        Pawn pawn = PawnGenerator.GeneratePawn(request);
        EnsureBodyType(pawn);
        GenSpawn.Spawn(pawn, map.Center + offset, map);
        SetWork(pawn, null);
        for (int hour = 0; hour < 24; hour++) pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Work);
        return pawn;
    }

    private static void SetWork(Pawn pawn, WorkTypeDef only)
    {
        pawn.workSettings.EnableAndInitialize();
        foreach (WorkTypeDef def in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            pawn.workSettings.SetPriority(def, def == only ? 1 : 0);
        if (only != null)
            for (int hour = 0; hour < 24; hour++) pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Work);
    }

    private void SpawnFuel(Thing machine, int count, IntVec3? stockOffset = null)
    {
        ThingDef def = machine.TryGetComp<CompRefuelable>().Props.fuelFilter.AnyAllowedDef;
        while (count > 0)
        {
            Thing item = ThingMaker.MakeThing(def);
            item.stackCount = Math.Min(count, def.stackLimit);
            count -= item.stackCount;
            GenPlace.TryPlaceThing(item, machine.Position + (stockOffset ?? new IntVec3(4, 0, -3)), map, ThingPlaceMode.Near);
        }
    }
}
