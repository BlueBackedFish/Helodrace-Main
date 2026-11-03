using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests.PowerStageG;

public sealed partial class Checks
{
    private IEnumerable<int> OilBalanceChecks()
    {
        var cable = Def<ThingDef>("HD_CableToolRig").GetCompProperties<CompProperties_OilFieldDevelopment>();
        var rotary = Def<ThingDef>("HD_RotaryDrill").GetCompProperties<CompProperties_OilFieldDevelopment>();
        Near(cable.minimumQuality, .75f, "Loaded cable well minimum is 75 percent");
        Check(cable.maximumQuality == .8f && rotary.minimumQuality == 1f && rotary.maximumQuality == 1.3f,
            "Loaded drilling quality ranges are 75-80 and 100-130 percent");
        foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList()) pawn.DeSpawn();
        foreach (var cell in CellRect.CenteredOn(map.Center, 29))
        {
            foreach (var thing in cell.GetThingList(map).ToList()) if (thing.Spawned && thing.def.destroyable) thing.Destroy();
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null);
        }
        var crafter = Colonist(0, 1); Colonist(1, 1);
        var engine = Spawn("HD_IndustrialDieselEngine", -18, 4);
        var genBuilding = Spawn("HD_EarlyIndustrialGenerator", -12, 4);
        var generator = genBuilding.GetComp<CompMechanicalGenerator>(); generator.TargetElectricalOutput = 4800f;
        Shafts(-21, -10, 2, 6);
        var pumps = new[] { Spawn("HD_MetalPumpJack", -3, 4), Spawn("HD_MetalPumpJack", 8, 4) };
        var fields = map.GetComponent<MapComponent_OilFields>();
        foreach (var pump in pumps)
        {
            map.terrainGrid.SetTerrain(pump.Position, Def<TerrainDef>(MapComponent_OilFields.OilFieldTerrainDefName));
            fields.CreateOrReplace(pump.Position, 1f, .85f);
            var producer = pump.GetComp<CompOilProducer>();
            Check(producer.Props.amount == 75 && producer.Props.daysToProduce == 1,
                "Actual steel pump nominal output is 75 crude per day");
        }
        var tower = (Building_WorkTable)Spawn("HD_DistillationTower", 0, -5);
        for (int x = -14; x <= 12; x++) Conduit(x, 2);
        for (int z = -7; z <= 2; z++) Conduit(0, z);
        var recipe = Def<RecipeDef>("HD_FractionateHeavyCrudeOil");
        foreach (var research in recipe.researchPrerequisites ?? new List<ResearchProjectDef>())
            if (!research.IsFinished) Find.ResearchManager.FinishProject(research, doCompletionDialog: false);
        if (recipe.researchPrerequisite != null && !recipe.researchPrerequisite.IsFinished)
            Find.ResearchManager.FinishProject(recipe.researchPrerequisite, doCompletionDialog: false);
        var bill = (Bill_Production)recipe.MakeNewBill(); bill.repeatMode = BillRepeatModeDefOf.Forever;
        bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor); tower.BillStack.AddBill(bill);
        var engineFuel = engine.GetComp<CompRefuelable>(); var towerFuel = tower.GetComp<CompRefuelable>();
        Near(engineFuel.Props.fuelConsumptionRate, 20, "Actual engine consumes 20 diesel per day");
        engineFuel.Refuel(80f); towerFuel.Refuel(50f);
        map.regionAndRoomUpdater.RebuildAllRegionsAndRooms(); map.powerNetManager.UpdatePowerNetsAndConnections_First();
        for (int elapsed = 0; elapsed < 1200 && (!pumps.All(p => p.GetComp<CompPowerTrader>().PowerOn) || !tower.GetComp<CompPowerTrader>().PowerOn); elapsed += 120) yield return 120;
        Check(pumps.All(p => p.GetComp<CompPowerTrader>().PowerOn) && tower.GetComp<CompPowerTrader>().PowerOn,
            "One real diesel engine powers both pumps and the tower through a 4800 W generator");
        var diesel = Def<ThingDef>("HD_Diesel"); var crude = Def<ThingDef>("HD_CrudeOil_Low");
        Check(PhysicalCount(diesel) == 0 && PhysicalCount(crude) == 0,
            "Closed oil loop starts with tank fuel only and no supplied crude or loose diesel");
        float initialEngine = engineFuel.Fuel, initialTotal = engineFuel.Fuel + towerFuel.Fuel;
        float lastFuel = engineFuel.Fuel; double added = 0; int refills = 0, sampled = 0;
        float minFuel = engineFuel.Fuel;
        sampleTick = () =>
        {
            sampled++; minFuel = MathF.Min(minFuel, engineFuel.Fuel);
            if (!engineFuel.HasFuel || !pumps.All(p => p.GetComp<CompPowerTrader>().PowerOn) || !tower.GetComp<CompPowerTrader>().PowerOn)
                throw new InvalidOperationException("Oil loop lost fuel or electricity at tick " + sampled);
            if (engineFuel.Fuel > lastFuel) { added += engineFuel.Fuel - lastFuel; refills++; }
            lastFuel = engineFuel.Fuel;
        };
        try { for (int elapsed = 0; elapsed < 8 * GenDate.TicksPerDay; elapsed += 240) yield return 240; }
        finally { sampleTick = null; }
        double consumed = initialEngine + added - engineFuel.Fuel;
        float finalTotal = engineFuel.Fuel + towerFuel.Fuel + PhysicalCount(diesel);
        Check(sampled == 8 * GenDate.TicksPerDay && Math.Abs(consumed - 160) < 1,
            "Eight actual game days consume 160 engine diesel; consumed=" + consumed);
        Check(refills > 0 && finalTotal > initialTotal,
            "Autonomous refining and hauling refill the engine from its own oil and grow total diesel reserve; refills=" + refills + "; initial=" + initialTotal + "; final=" + finalTotal);
        Check(fields.FieldAt(pumps[0].Position).pressure < .85f && fields.FieldAt(pumps[0].Position).pressure >= .35f,
            "Real extraction retains pressure depletion instead of fixing pressure at the balance reference");
        File.WriteAllLines(Path.Combine(output, "oil-loop.csv"), new[] {
            "days,initial_well_quality,initial_pressure,engine_diesel_per_day,engine_consumed,engine_refills,minimum_engine_fuel,initial_total_diesel,final_total_diesel,final_pressure,remaining_crude",
            Csv(8, 1, .85, engineFuel.Props.fuelConsumptionRate, consumed, refills, minFuel, initialTotal, finalTotal,
                fields.FieldAt(pumps[0].Position).pressure, PhysicalCount(crude)) });
    }
}
