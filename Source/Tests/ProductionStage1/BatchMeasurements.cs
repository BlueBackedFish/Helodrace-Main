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
    private IEnumerable<int> BatchMeasurements()
    {
        var fuel = furnace.GetComp<CompRefuelable>();
        var giver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsHD_BlastFurnace").Worker;
        var refueler = (WorkGiver_RefuelIndustrialMachine)DefDatabase<WorkGiverDef>.GetNamed("HD_RefuelIndustrialMachine").Worker;
        ThingDef fuelDef = fuel.Props.fuelFilter.AnyAllowedDef;
        string measurements = Path.Combine(output, "batch-measurements.md");
        File.WriteAllText(measurements, "Elapsed ticks include ingredient handling and refueling. Fuel used is the change in physical fuel items plus tank contents, including refueling rounding loss.\n\n| Recipe | Labor speed | Work | Elapsed ticks | Fuel used | Produced |\n| --- | --- | --- | --- | --- | --- |\n");
        SpawnFuel(furnace, 1000);
        SetWork(worker, WorkTypeDefOf.Crafting);
        foreach (bool slow in new[] { false, true })
        {
            if (slow) worker.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TestSlowLabor"));
            float speed = worker.GetStatValue(StatDefOf.GeneralLaborSpeed);
            foreach (string family in new[] { "HD_CastPigIron", "HD_RefineCarbonSteel" })
                foreach (string size in new[] { "Small", "Medium", "Large" })
                    {
                        RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed(family + size);
                        Bill_Production bill = AddBillAndIngredients(furnace, recipe);
                        int previous = CountPhysical(recipe.ProducedThingDef);
                        fuel.Refuel(fuel.Props.fuelCapacity);
                        double beforeFuel = fuel.Fuel + CountPhysical(fuelDef) * fuel.Props.FuelMultiplierCurrentDifficulty;
                        int begin = Find.TickManager.TicksGame;
                        while (bill.repeatCount > 0 && Find.TickManager.TicksGame - begin < 100000)
                        {
                            StartBill(giver, furnace, bill);
                            if (hauler.CurJob?.playerForced != true && refueler.HasJobOnThing(hauler, furnace))
                                hauler.jobs.TryTakeOrderedJob(refueler.JobOnThing(hauler, furnace));
                            yield return 60;
                        }
                        double consumed = beforeFuel - fuel.Fuel - CountPhysical(fuelDef) * fuel.Props.FuelMultiplierCurrentDifficulty;
                        int produced = CountPhysical(recipe.ProducedThingDef) - previous;
                        Check(bill.repeatCount == 0 && produced == recipe.products.Single().count,
                            recipe.defName + " completes at labor speed " + speed + " with exact output");
                        Check(consumed > 0d, recipe.defName + " consumes physical fuel");
                        File.AppendAllText(measurements, FormattableString.Invariant(
                            $"| {recipe.defName} | {speed:F2} | {recipe.workAmount:F0} | {Find.TickManager.TicksGame - begin} | {consumed:F2} | {produced} |\n"));
                    }
            if (slow)
                foreach (Hediff hediff in worker.health.hediffSet.hediffs.Where(h => h.def.defName == "HD_TestSlowLabor").ToList())
                    worker.health.RemoveHediff(hediff);
        }
        Check(map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_UnfinishedSmeltingBatch")).Count == 0,
            "all six smelting recipes finish at normal and slow speed without orphan unfinished batches");
    }
}
