using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.IndustryStage3;

public sealed partial class Checks
{
    private IEnumerable<int> ComponentChecks()
    {
        var engine = Spawn("HD_LargeSteamEngine", -16, 12);
        var mill = (Building_WorkTable)Spawn("HD_LineShaftMillingMachine", -8, 12);
        var press = (Building_WorkTable)Spawn("HD_LineShaftHydraulicPress", -2, 12);
        var roller = (Building_WorkTable)Spawn("HD_LineShaftRollingMachine", 4, 12);
        var lathe = (Building_WorkTable)Spawn("HD_LineShaftTurretLathe", 10, 12);
        for (int x = -18; x <= 12; x++)
        {
            IntVec3 cell = map.Center + new IntVec3(x, 0, 11);
            if (cell.GetEdifice(map)?.TryGetComp<CompMechanicalNode>() == null) Spawn("HD_LineShaft", x, 11);
        }
        var bench = map.listerThings.ThingsOfDef(Def<ThingDef>("HD_BasicWorkbench")).OfType<Building_WorkTable>().Single();
        engine.GetComp<CompRefuelable>().Refuel(120f);
        yield return 240;
        Write("MECHANICAL " + string.Join(";", new[] { mill, press, roller, lathe }.Select(t =>
            t.def.defName + ":powered=" + t.GetComp<CompMechanicalUser>().HasPower + ":rpm=" + t.GetComp<CompMechanicalUser>().RealRPM)));
        Check(new[] { mill, press, roller, lathe }.All(t => t.GetComp<CompMechanicalUser>().HasPower),
            "Actual steam engine and shafts power all primary component machines");
        var families = new[] {
            ("HD_MakePressComponent", press), ("HD_MillGeneralMachinePart", mill), ("HD_MakeShaft", lathe),
            ("HD_RollUniformSteelPlate", roller), ("HD_ConvertIndustrialComponentToGeneralMachinePart", bench),
            ("HD_MakeBasicElectricalComponents", bench), ("HD_MakeBasicElectronicComponents", bench)
        };
        foreach (var (name, table) in families)
        {
            foreach (int quantity in new[] { 1, 5, 10 })
            {
                engine.GetComp<CompRefuelable>().Refuel(120f);
                var recipe = Def<RecipeDef>(name + (quantity == 1 ? "" : "Bulk" + quantity));
                Check(recipe.unfinishedThingDef?.defName == "HD_UnfinishedIndustrialBatch", "Component batch preserves work: " + recipe.defName);
                var bill = Bill(table, recipe.defName);
                int before = Count(recipe.ProducedThingDef);
                var costs = recipe.ingredients.Select(i => (def: i.filter.AnyAllowedDef, count: i.CountRequiredOfFor(i.filter.AnyAllowedDef, recipe, bill))).ToList();
                var rawBefore = costs.ToDictionary(i => i.def, i => Count(i.def));
                foreach (int ticks in CompleteBill(table, bill, false)) yield return ticks;
                Check(Count(recipe.ProducedThingDef) == before + quantity, "Exact physical component output: " + recipe.defName + " x" + quantity);
                foreach (var material in costs)
                    Check(Count(material.def) == rawBefore[material.def] - material.count, "Exact physical batch material: " + recipe.defName + "/" + material.def.defName + "; before=" + rawBefore[material.def] + "; after=" + Count(material.def) + "; cost=" + material.count + "; carry=" + worker.carryTracker.CarriedThing + "; job=" + worker.CurJobDef);
            }
        }
        foreach (string old in new[] { "HD_TurnGeneralMachinePart", "HD_TurnGeneralMachinePartBulk5", "HD_TurnGeneralMachinePartBulk10" })
            Check(DefDatabase<RecipeDef>.GetNamedSilentFail(old) == null, "Duplicate general-component process is removed: " + old);
        foreach (ThingDef table in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsWorkTable))
            foreach (var (name, _) in families)
                if (table.AllRecipes.Contains(Def<RecipeDef>(name)))
                    Check(table.AllRecipes.Contains(Def<RecipeDef>(name + "Bulk5")) && table.AllRecipes.Contains(Def<RecipeDef>(name + "Bulk10")),
                        "Every supported table retains both bulk sizes: " + table.defName + "/" + name);
        foreach (int ticks in OilRemovalChecks()) yield return ticks;
        componentBench = bench;
        var partial = (Bill_ProductionWithUft)Bill(bench, "HD_MakeBasicElectronicComponentsBulk10");
        int elapsed = 0;
        while ((partial.BoundUft == null || partial.BoundUft.workLeft >= partial.recipe.workAmount - 200f) && elapsed < 10000)
        {
            yield return 120; elapsed += 120;
        }
        Check(partial.BoundUft != null && partial.BoundUft.workLeft < partial.recipe.workAmount - 200f,
            "Long electronic-component batch reaches real partial work");
        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
        savedComponentWork = partial.BoundUft.workLeft;
        savedComponentIngredients = partial.BoundUft.ingredients.Sum(t => t.stackCount);
        componentRawAtPause = partial.recipe.ingredients.Select(i => i.filter.AnyAllowedDef).Distinct().ToDictionary(d => d, Count);
        Check(savedComponentIngredients == 70, "Partial electronic batch retains all 70 physical material units");
        foreach (int ticks in RelocationBeforeSave()) yield return ticks;
        stage = 2;
        GameDataSaveLoader.SaveGame("IndustryStage3Components");
        Write("SAVE: partial electronic components workLeft=" + savedComponentWork + "; materialUnits=" + savedComponentIngredients);
        loadRequested = true; yield return 0;
    }
    private IEnumerable<int> ResumeComponentBatch()
    {
        foreach (int ticks in RelocationAfterLoad()) yield return ticks;
        var bill = componentBench.BillStack.Bills.OfType<Bill_ProductionWithUft>().Single(b => b.repeatCount > 0);
        Thing unfinished = bill.BoundUft;
        int before = Count(bill.recipe.ProducedThingDef);
        foreach (int ticks in CompleteBill(componentBench, bill, false)) yield return ticks;
        Check(Count(bill.recipe.ProducedThingDef) == before + 10 && unfinished.Destroyed,
            "Loaded interrupted component batch completes exactly ten products and consumes its unfinished item");
        foreach (var pair in componentRawAtPause)
            Check(Count(pair.Key) == pair.Value, "Resuming the batch neither consumes extra nor duplicates raw material: " + pair.Key.defName);
    }
}
