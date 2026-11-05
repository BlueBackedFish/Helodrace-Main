using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.PowerStageG;

public sealed partial class Checks
{
    private Action sampleTick;
    private static Map climateMap;
    private static bool climateInstalled;
    private static bool OutdoorTemperature(MapTemperature __instance, ref float __result)
    {
        if (climateMap == null || __instance != climateMap.mapTemperature) return true;
        __result = 21f; return false;
    }
    private void Conduit(int x, int z)
    {
        var cell = map.Center + new IntVec3(x, 0, z);
        if (!cell.GetThingList(map).Any(t => t.def.EverTransmitsPower && t.def.building?.isPowerConduit != true)) Spawn("PowerConduit", x, z);
    }
    private Pawn Colonist(int index, int crafters)
    {
        var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, canGeneratePawnRelations: false,
            fixedBiologicalAge: 25, fixedChronologicalAge: 25) { ForceNoBackstory = true };
        var pawn = PawnGenerator.GeneratePawn(request);
        if (pawn.story?.bodyType == null && pawn.story != null) pawn.story.bodyType = pawn.gender == Gender.Female ? BodyTypeDefOf.Female : BodyTypeDefOf.Male;
        GenSpawn.Spawn(pawn, map.Center + new IntVec3(-5 + index, 0, -12), map);
        pawn.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
        pawn.workSettings.EnableAndInitialize();
        foreach (var type in DefDatabase<WorkTypeDef>.AllDefsListForReading) pawn.workSettings.SetPriority(type, 0);
        if (index < crafters) pawn.workSettings.SetPriority(WorkTypeDefOf.Crafting, 1);
        if (index == crafters) pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
        foreach (var skill in pawn.skills.skills) skill.Level = 12;
        for (int hour = 0; hour < 24; hour++) pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Work);
        foreach (var need in pawn.needs.AllNeeds) need.CurLevel = need.MaxLevel;
        return pawn;
    }
    private void Supply(ThingDef def, int quantity, IntVec3 position)
    {
        while (quantity > 0)
        {
            var thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            thing.stackCount = Math.Min(quantity, def.stackLimit); quantity -= thing.stackCount;
            Check(GenPlace.TryPlaceThing(thing, position, map, ThingPlaceMode.Near), "Fixture material is physically placed: " + def.defName);
        }
    }
    private int PhysicalCount(ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount)
        + map.mapPawns.FreeColonistsSpawned.Sum(p => (p.inventory?.innerContainer.Where(t => t.def == def).Sum(t => t.stackCount) ?? 0)
            + (p.carryTracker?.CarriedThing?.def == def ? p.carryTracker.CarriedThing.stackCount : 0));
    private Bill_Production ProductionBill(Building_WorkTable table, string recipeName)
    {
        var recipe = Def<RecipeDef>(recipeName);
        Check(table.def.AllRecipes.Contains(recipe), "Scheduled recipe belongs to its actual worktable: " + table.def.defName + "/" + recipeName);
        foreach (var research in (recipe.researchPrerequisites ?? new List<ResearchProjectDef>()).Concat(recipe.researchPrerequisite == null ? Array.Empty<ResearchProjectDef>() : new[] { recipe.researchPrerequisite }))
            if (!research.IsFinished) Find.ResearchManager.FinishProject(research, doCompletionDialog: false);
        var bill = (Bill_Production)recipe.MakeNewBill(); bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = 1;
        bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor); table.BillStack.AddBill(bill);
        foreach (var ingredient in recipe.ingredients)
        {
            var def = ingredient.filter.AnyAllowedDef;
            Supply(def, ingredient.CountRequiredOfFor(def, recipe, bill), table.InteractionCell + new IntVec3(1, 0, -1));
        }
        return bill;
    }
    private Building ColdRoom(int x, int z)
    {
        var rect = CellRect.CenteredOn(map.Center + new IntVec3(x, 0, z), 3);
        var pumpCell = map.Center + new IntVec3(x, 0, z + 3);
        foreach (var cell in rect)
        {
            if (rect.IsOnEdge(cell) && cell != pumpCell)
            {
                var wall = ThingMaker.MakeThing(ThingDefOf.Wall, Def<ThingDef>("BlocksGranite")); wall.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(wall, cell, map);
            }
            map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
        }
        var pump = Spawn("HD_LineShaftHeatPump", x, z + 3);
        pump.GetComp<CompMechanicalTemperatureControl>().TargetColdHeatRemovalPerSecond = 40f;
        return pump;
    }
    private IEnumerable<int> ColonyBudgets()
    {
        ExportBudgetTables();
        climateMap = map;
        if (!climateInstalled)
        {
            new Harmony("helodrace.tests.power.climate").Patch(AccessTools.PropertyGetter(typeof(MapTemperature), nameof(MapTemperature.OutdoorTemp)),
                prefix: new HarmonyMethod(typeof(Checks), nameof(OutdoorTemperature))); climateInstalled = true;
        }
        var summary = new List<string> { "colonists,generator_target_W,fuel_def,initial_idle_shaft_W,peak_shaft_W,mechanical_Wd,electric_consumption_Wd,fuel_units_consumed,refills,max_cold_C_after_half_day,simulated_ticks" };
        var duties = new List<string> { "colonists,worktable,full_draw_ticks,waiting_ticks,duty_fraction,mean_requested_input_W" };
        foreach (int population in new[] { 4, 8 }.Where(n => !GenCommandLine.TryGetCommandLineArg("hdPowerPopulation", out string requested) || requested == n.ToString()))
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList()) pawn.DeSpawn();
            foreach (var cell in CellRect.CenteredOn(map.Center, 29))
            {
                foreach (var thing in cell.GetThingList(map).ToList()) if (thing.Spawned && thing.def.destroyable) thing.Destroy();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null);
            }
            int crafters = population == 4 ? 1 : 2;
            for (int i = 0; i < population; i++) Colonist(i, crafters);
            Check(map.mapPawns.FreeColonistsSpawned.Count == population, "Scenario has exactly " + population + " real colonists and one hauler");
            var engine = Spawn(population == 4 ? "HD_SmallSteamEngine" : "HD_LargeSteamEngine", -18, 4);
            var genBuilding = Spawn("HD_EarlyIndustrialGenerator", -12, 4);
            var generator = genBuilding.GetComp<CompMechanicalGenerator>(); generator.TargetElectricalOutput = population == 4 ? 300f : 1800f;
            var lathe = (Building_WorkTable)Spawn("HD_LineShaftTurretLathe", -6, 4);
            var mill = (Building_WorkTable)Spawn("HD_LineShaftMillingMachine", 0, 4);
            var press = (Building_WorkTable)Spawn("HD_LineShaftHydraulicPress", 6, 4);
            var roller = (Building_WorkTable)Spawn("HD_LineShaftRollingMachine", 12, 4);
            var machines = new List<Building_WorkTable> { lathe, mill, press, roller };
            var consumers = new List<CompPowerTrader>();
            if (population == 8) { var cnc = (Building_WorkTable)Spawn("HD_NCLathe", 18, 4); machines.Add(cnc); consumers.Add(cnc.GetComp<CompPowerTrader>()); }
            var pumps = new List<Building> { ColdRoom(-18, 18) };
            if (population == 8) pumps.Add(ColdRoom(5, 18));
            Shafts(-21, 15, 2, 6);
            for (int z = 7; z <= 22; z++) { Spawn("HD_LineShaft", -18, z); if (population == 8) Spawn("HD_LineShaft", 5, z); }
            for (int x = -14; x <= 20; x++) Conduit(x, 2);
            for (int i = 0; i < population; i++)
            {
                int x = -8 + i % 4 * 6, z = -4 - i / 4 * 4;
                consumers.Add(Spawn("StandingLamp", x, z).GetComp<CompPowerTrader>());
                for (int row = z; row <= 2; row++) Conduit(x, row);
            }
            consumers.Add(Spawn("HD_Radio", 16, -4).GetComp<CompPowerTrader>());
            for (int z = -4; z <= 2; z++) Conduit(16, z);
            var fuel = engine.GetComp<CompRefuelable>(); fuel.Refuel(fuel.Props.fuelCapacity);
            var fuelDef = fuel.Props.fuelFilter.Allows(ThingDefOf.WoodLog) ? ThingDefOf.WoodLog : fuel.Props.fuelFilter.AnyAllowedDef;
            Supply(fuelDef, 300, map.Center + new IntVec3(-22, 0, -4));
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms(); map.powerNetManager.UpdatePowerNetsAndConnections_First();
            var rooms = pumps.Select(p => (p.Position + IntVec3.South).GetRoom(map)).ToList();
            Check(rooms.All(r => r != null && !r.UsesOutdoorTemperature), "Cold rooms are actually enclosed and roofed");
            foreach (var room in rooms) room.Temperature = 20f;
            for (int startup = 0; startup < 6000; startup += 120)
            {
                if (machines.All(t => t.GetComp<CompMechanicalUser>()?.HasPower ?? t.GetComp<CompPowerTrader>().PowerOn)
                    && pumps.All(p => p.GetComp<CompMechanicalUser>().HasPower) && consumers.All(p => p.PowerOn)) break;
                yield return 120;
            }
            Write("STARTUP: " + string.Join(";", machines.Select(t => t.def.defName + ":spawned=" + t.Spawned + ":power=" + (t.GetComp<CompMechanicalUser>()?.HasPower ?? t.GetComp<CompPowerTrader>().PowerOn)))
                + "; source=" + engine.GetComp<CompMechanicalEmitter>().IsProducingPower + "; fuel=" + fuel.Fuel);
            Write("ELECTRICAL STARTUP: genOutput=" + generator.CurrentElectricalOutput + "; genOn=" + genBuilding.GetComp<CompPowerTrader>().PowerOn
                + "; net=" + (genBuilding.GetComp<CompPowerTrader>().PowerNet != null) + "; weather=" + map.weatherManager.curWeather.defName
                + "; consumers=" + string.Join(";", consumers.Select(p => p.parent.def.defName + ":on=" + p.PowerOn + ":draw=" + p.PowerOutput
                    + ":sameNet=" + ReferenceEquals(p.PowerNet, genBuilding.GetComp<CompPowerTrader>().PowerNet)
                    + ":wants=" + FlickUtility.WantsToBeOn(p.parent) + ":broken=" + p.parent.IsBrokenDown())));
            var net = genBuilding.GetComp<CompMechanicalUser>().Network;
            Check(net != null && machines.All(t => t.GetComp<CompMechanicalUser>()?.HasPower ?? t.GetComp<CompPowerTrader>().PowerOn)
                && pumps.All(p => p.GetComp<CompMechanicalUser>().HasPower), "One engine powers cold rooms and every waiting worktable");
            Check(consumers.All(p => p.PowerOn), "Real generator supplies every light, radio and CNC consumer");
            float initialIdle = net.CurrentPowerNeeded;
            var bills = new List<Bill_Production>();
            var expected = new Dictionary<ThingDef, int>();
            string[] recipeNames = { "HD_MakeShaftBulk10", "HD_MillGeneralMachinePartBulk10", "HD_MakePressComponentBulk10", "HD_RollUniformSteelPlateBulk10", "HD_MakeShaftBulk10" };
            for (int i = 0; i < machines.Count; i++)
            {
                var bill = ProductionBill(machines[i], recipeNames[i]); bills.Add(bill);
                var product = bill.recipe.products.Single();
                if (!expected.ContainsKey(product.thingDef)) expected[product.thingDef] = PhysicalCount(product.thingDef);
                expected[product.thingDef] += product.count;
            }
            int startTick = Find.TickManager.TicksGame;
            int ticksSampled = 0, refills = 0;
            double mechanicalWd = 0, electricWd = 0, fuelAdded = 0;
            float peak = 0, lastFuel = fuel.Fuel, startingFuel = fuel.Fuel, maxCold = float.NegativeInfinity;
            var fullTicks = new int[machines.Count]; var machineW = new double[machines.Count];
            sampleTick = () =>
            {
                ticksSampled++;
                if (!fuel.HasFuel || !pumps.All(p => p.GetComp<CompMechanicalUser>().HasPower) || !consumers.All(p => p.PowerOn))
                    throw new InvalidOperationException(population + "-colonist colony lost fuel, refrigeration or electrical supply at tick " + ticksSampled);
                if (net.GridRPM < net.NominalGridRPM) throw new InvalidOperationException("Representative colony exceeded its single-engine capacity");
                peak = MathF.Max(peak, net.CurrentPowerNeeded); mechanicalWd += net.CurrentPowerNeeded / GenDate.TicksPerDay;
                electricWd += consumers.Sum(p => MathF.Max(0, -p.PowerOutput)) / GenDate.TicksPerDay;
                if (fuel.Fuel > lastFuel) { fuelAdded += fuel.Fuel - lastFuel; refills++; } lastFuel = fuel.Fuel;
                if (ticksSampled >= GenDate.TicksPerDay / 2) maxCold = MathF.Max(maxCold, rooms.Max(r => r.Temperature));
                for (int i = 0; i < machines.Count; i++)
                {
                    var mech = machines[i].GetComp<CompMechanicalUser>(); var electrical = machines[i].GetComp<CompPowerTrader>();
                    float demand = mech?.GridPowerDemanded ?? MathF.Max(0, -electrical.PowerOutput);
                    float rated = mech?.Props.mechanicalPowerRequired ?? electrical.Props.PowerConsumption;
                    machineW[i] += demand;
                    if (demand >= rated * .99f) fullTicks[i]++;
                }
            };
            try { for (int elapsed = 0; elapsed < GenDate.TicksPerDay; elapsed += 240) yield return Math.Min(240, GenDate.TicksPerDay - elapsed); }
            finally { sampleTick = null; }
            Check(ticksSampled == GenDate.TicksPerDay && Find.TickManager.TicksGame - startTick == GenDate.TicksPerDay, "Exactly one full day is physically simulated for " + population + " colonists");
            Check(bills.All(b => b.repeatCount == 0), "All scheduled real mechanical/CNC batches complete automatically");
            foreach (var pair in expected) Check(PhysicalCount(pair.Key) == pair.Value, "Daily production has exact output: " + pair.Key.defName);
            Check(fullTicks.All(t => t > 0 && t < GenDate.TicksPerDay), "Every worktable demonstrates both actual work and reduced waiting draw");
            Check(maxCold < 5f && maxCold >= -5.1f, "Refrigeration stays below 5 C after initial cooling; maximum=" + maxCold);
            double consumed = startingFuel + fuelAdded - fuel.Fuel;
            Check(Math.Abs(consumed - fuel.Props.fuelConsumptionRate) < .75 && refills > 0, "Measured daily fuel and automatic refueling match engine rating; consumed=" + consumed + "; refills=" + refills);
            summary.Add(Csv(population, generator.TargetElectricalOutput, fuelDef.defName, initialIdle, peak, mechanicalWd, electricWd, consumed, refills, maxCold, ticksSampled));
            for (int i = 0; i < machines.Count; i++) duties.Add(Csv(population, machines[i].def.defName, fullTicks[i], GenDate.TicksPerDay - fullTicks[i], fullTicks[i] / (double)GenDate.TicksPerDay, machineW[i] / GenDate.TicksPerDay));
            File.WriteAllLines(Path.Combine(output, "colonies.csv"), summary); File.WriteAllLines(Path.Combine(output, "duty.csv"), duties);
            Write("COLONY: population=" + population + "; peakShaft=" + peak + "; fuel=" + consumed + "; coldMax=" + maxCold);
        }
    }
}