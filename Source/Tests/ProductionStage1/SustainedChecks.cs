using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.ProductionStage1;

public sealed partial class Checks
{
    private IEnumerable<int> LoadedWorktableJobs()
    {
        ThingDef clothingDef = DefDatabase<ThingDef>.GetNamed("HD_ClothingBench");
        var clothing = map.listerThings.ThingsOfDef(clothingDef).OfType<Building_WorkTable>().FirstOrDefault();
        if (clothing == null) yield break; // The isolated -FuelOnly run has no A fixtures.
        var lathe = map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_LineShaftTurretLathe")).OfType<Building_WorkTable>().Single();
        var mechanical = lathe.GetComp<CompMechanicalUser>();
        var mechanicalGiver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsHD_LineShaftTurretLathe").Worker;
        Check(lathe.CurrentlyUsableForBills() == mechanical.HasPower, "loaded mechanical bench uses the same power gate as bill work");
        foreach (Thing engine in map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_SmallSteamEngine")))
            engine.TryGetComp<CompRefuelable>().Refuel(25f);
        yield return 120;
        Check(mechanical.HasPower, "loaded engine and shaft recover mechanical power");
        Bill_Production mechanicalBill = AddBillAndIngredients(lathe, DefDatabase<RecipeDef>.GetNamed("HD_TurnGeneralMachinePart"));
        int before = CountPhysical(mechanicalBill.recipe.ProducedThingDef);
        for (int waited = 0; mechanicalBill.repeatCount > 0 && waited < 8000; waited += 120)
        {
            StartBill(mechanicalGiver, lathe, mechanicalBill);
            yield return 120;
        }
        Check(mechanicalBill.repeatCount == 0 && CountPhysical(mechanicalBill.recipe.ProducedThingDef) == before + 1,
            "mechanical production completes after the actual save/load roundtrip");

        RecipeDef recipe = clothingDef.AllRecipes.First(r => r.ProducedThingDef?.defName == "HD_Apparel_WildWestFormalShirt");
        SetWork(worker, DefDatabase<WorkTypeDef>.GetNamed("Tailoring"));
        Bill_Production bill = AddBillAndIngredients(clothing, recipe);
        before = CountPhysical(recipe.ProducedThingDef);
        var tailoring = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("DoBillsHD_ClothingBench").Worker;
        for (int waited = 0; bill.repeatCount > 0 && waited < 12000; waited += 120)
        {
            StartBill(tailoring, clothing, bill);
            yield return 120;
        }
        Check(bill.repeatCount == 0 && CountPhysical(recipe.ProducedThingDef) == before + 1,
            "loaded clothing bench completes another actual garment");
    }

    private IEnumerable<int> SustainedEngines()
    {
        foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.Where(p => p != hauler).ToList())
        {
            SetWork(pawn, null);
            if (pawn.CurJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
        foreach (Thing machine in map.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable))
            if (machine.TryGetComp<CompRefuelable>() is { Props: CompProperties_IndustrialRefuelable } existing)
                existing.allowAutoRefuel = false;
        var roomRect = CellRect.CenteredOn(map.Center + new IntVec3(20, 0, 18), 3);
        IntVec3 pumpCell = map.Center + new IntVec3(20, 0, 21);
        foreach (IntVec3 cell in roomRect)
        {
            if (roomRect.IsOnEdge(cell) && cell != pumpCell)
            {
                Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, DefDatabase<ThingDef>.GetNamed("BlocksGranite"));
                wall.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(wall, cell, map);
            }
            map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
        }
        Thing pump = Building("HD_LineShaftHeatPump", new IntVec3(20, 0, 21));
        pump.TryGetComp<CompMechanicalTemperatureControl>().TargetColdHeatRemovalPerSecond = 40f;
        Building("HD_LineShaft", new IntVec3(20, 0, 22));
        var engines = new[] {
            Building("HD_SmallSteamEngine", new IntVec3(18, 0, 23)),
            Building("HD_LargeSteamEngine", new IntVec3(6, 0, 23)),
            Building("HD_IndustrialDieselEngine", new IntVec3(-5, 0, 23))
        };
        var fuels = engines.Select(t => t.TryGetComp<CompRefuelable>()).ToArray();
        foreach (Thing engine in engines) { engine.TryGetComp<CompRefuelable>().Refuel(1000f); SpawnFuel(engine, 600, new IntVec3(4, 0, 4)); }
        SetWork(hauler, WorkTypeDefOf.Hauling);
        Check(!map.mapPawns.FreeColonistsSpawned.Any(p => p != hauler && p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)),
            "only the designated hauler can refuel during the sustained test");
        yield return 120;
        foreach (CompRefuelable fuel in fuels)
            Check(map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Any(t => fuel.Props.fuelFilter.Allows(t)
                && hauler.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Some)),
                fuel.parent.def.defName + " has reachable fuel outside the enclosed cold room");
        Room room = (pumpCell + IntVec3.South).GetRoom(map);
        Check(room != null && !room.UsesOutdoorTemperature, "heat pump cold side is an actual enclosed refrigerated room");
        Check(pump.TryGetComp<CompMechanicalUser>().HasPower, "small steam engine supplies the actual heat pump");
        room.Temperature = 20f;
        var minima = fuels.Select(f => f.Fuel).ToArray();
        var refills = new int[3];
        int duration = 3 * GenDate.TicksPerDay;
        int elapsed = 0;
        int unavailableTicks = 0;
        float maximumCooledTemperature = float.NegativeInfinity;
        int startTick = Find.TickManager.TicksGame;
        float[] lastFuel = fuels.Select(f => f.Fuel).ToArray();
        sampleTick = () =>
        {
            if (map.mapPawns.FreeColonistsSpawned.Any(p => p != hauler && p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)))
                throw new InvalidOperationException("Another pawn became available to refuel during the sustained test.");
            for (int i = 0; i < fuels.Length; i++)
            {
                minima[i] = Math.Min(minima[i], fuels[i].Fuel);
                if (fuels[i].Fuel > lastFuel[i]) refills[i]++;
                lastFuel[i] = fuels[i].Fuel;
                if (!fuels[i].HasFuel) throw new InvalidOperationException(engines[i].def.defName + " ran out of fuel at tick " + Find.TickManager.TicksGame);
            }
            if (!pump.TryGetComp<CompMechanicalUser>().HasPower)
                throw new InvalidOperationException("Refueling caused a heat-pump power outage at tick " + Find.TickManager.TicksGame);
            if (Find.TickManager.TicksGame - startTick >= GenDate.TicksPerDay / 2)
                maximumCooledTemperature = Math.Max(maximumCooledTemperature, room.Temperature);
        };
        while (elapsed < duration)
        {
            // Simulate an eight-hour night without an available hauler, every
            // day; reserve fuel must cover this gap and the following trip.
            int dayTick = elapsed % GenDate.TicksPerDay;
            bool away = dayTick >= 40000;
            if (away && hauler.Spawned)
            {
                hauler.jobs.EndCurrentJob(JobCondition.InterruptForced);
                hauler.DeSpawn();
            }
            else if (!away && !hauler.Spawned) GenSpawn.Spawn(hauler, map.Center + new IntVec3(12, 0, 25), map);
            int step = Math.Min(120, away ? GenDate.TicksPerDay - dayTick : 40000 - dayTick);
            if (away) unavailableTicks += step;
            yield return step;
            elapsed += step;
            if (elapsed % GenDate.TicksPerDay == 0) Write("SUSTAIN: day=" + elapsed / GenDate.TicksPerDay + "; fuel="
                + string.Join(",", fuels.Select(f => f.Fuel.ToString("F2"))) + "; coldRoom=" + room.Temperature);
        }
        sampleTick = null;
        if (!hauler.Spawned) GenSpawn.Spawn(hauler, map.Center + new IntVec3(12, 0, 25), map);
        Check(unavailableTicks == 60000, "hauler is absent for exactly eight hours on each of three days");
        for (int i = 0; i < fuels.Length; i++) Check(refills[i] > 0 && minima[i] > 0f,
            engines[i].def.defName + " runs three days with automatic refueling; refills=" + refills[i] + "; minimumFuel=" + minima[i]);
        Check(maximumCooledTemperature < 5f, "refrigerated room remains below 5 C after initial cooling; maximum=" + maximumCooledTemperature);
        SetWork(hauler, null);
        CompRefuelable testFuel = fuels[0];
        testFuel.ConsumeFuel(testFuel.Fuel - testFuel.Props.fuelCapacity * .5f);
        var refueler = (WorkGiver_RefuelIndustrialMachine)DefDatabase<WorkGiverDef>.GetNamed("HD_RefuelIndustrialMachine").Worker;
        foreach (Thing item in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).ToList())
            if (testFuel.Props.fuelFilter.Allows(item)) item.SetForbidden(true);
        Check(!refueler.HasJobOnThing(hauler, engines[0]), "forbidden or unavailable fuel does not create a refuel job");
    }

    private Thing Building(string name, IntVec3 offset)
    {
        ThingDef def = DefDatabase<ThingDef>.GetNamed(name);
        Thing result = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
        result.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(result, map.Center + offset, map);
        return result;
    }

    private int CountPhysical(ThingDef def)
        => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount)
            + map.mapPawns.AllPawnsSpawned.Sum(p => (p.inventory?.innerContainer.Where(t => t.def == def).Sum(t => t.stackCount) ?? 0)
                + (p.apparel?.WornApparel.Count(t => t.def == def) ?? 0)
                + (p.carryTracker?.CarriedThing?.def == def ? p.carryTracker.CarriedThing.stackCount : 0));
}
