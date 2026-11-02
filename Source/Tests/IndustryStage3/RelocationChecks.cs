using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.IndustryStage3;

public sealed partial class Checks
{
    private Building_WorkTable portableStill;
    private void ExposeRelocation()
    {
        Scribe_References.Look(ref portableStill, "portableStill");
    }
    private static MinifiedThing Packed(Map targetMap, Building building) => targetMap.listerThings
        .ThingsInGroup(ThingRequestGroup.MinifiedThing).OfType<MinifiedThing>().Single(t => t.InnerThing == building);
    private IEnumerable<int> Uninstall(Building building)
    {
        worker.workSettings.SetPriority(WorkTypeDefOf.Construction, 1);
        var designator = new Designator_Uninstall();
        Check(designator.CanDesignateThing(building).Accepted, "Native uninstall accepts " + building.def.defName);
        designator.DesignateThing(building);
        var giver = (WorkGiver_Uninstall)DefDatabase<WorkGiverDef>.AllDefsListForReading.Single(d => d.giverClass == typeof(WorkGiver_Uninstall)).Worker;
        Check(giver.HasJobOnThing(worker, building, true), "Native uninstall job is available: " + building.def.defName);
        Check(worker.jobs.TryTakeOrderedJob(giver.JobOnThing(worker, building, true)), "Worker begins real uninstall: " + building.def.defName);
        int elapsed = 0;
        while (building.Spawned && elapsed < 30000) { yield return 120; elapsed += 120; }
        Check(!building.Spawned && !building.Destroyed && Packed(map, building) != null, "Real work produces one packed building: " + building.def.defName);
    }
    private IEnumerable<int> Install(Building building, IntVec3 destination)
    {
        var blueprint = GenConstruct.PlaceBlueprintForInstall(Packed(map, building), destination, map, Rot4.North, Faction.OfPlayer);
        var giver = (WorkGiver_ConstructDeliverResourcesToBlueprints)DefDatabase<WorkGiverDef>.AllDefsListForReading
            .First(d => d.giverClass == typeof(WorkGiver_ConstructDeliverResourcesToBlueprints)).Worker;
        Check(giver.HasJobOnThing(worker, blueprint, true), "Native installation job is available: " + building.def.defName);
        Check(worker.jobs.TryTakeOrderedJob(giver.JobOnThing(worker, blueprint, true)), "Worker begins real installation: " + building.def.defName);
        int elapsed = 0;
        while (!building.Spawned && elapsed < 30000) { yield return 120; elapsed += 120; }
        Check(building.Spawned && building.Position == destination && blueprint.Destroyed,
            "Real installation retains the same building at its new position: " + building.def.defName);
        yield return 120;
    }
    private IEnumerable<int> RelocationBeforeSave()
    {
        var mill = map.listerThings.ThingsOfDef(Def<ThingDef>("HD_LineShaftMillingMachine")).OfType<Building>().Single();
        IntVec3 oldPosition = mill.Position;
        var node = mill.GetComp<CompMechanicalNode>();
        Check(node.Network != null, "Relocated mill begins connected to the steam-powered shaft network");
        foreach (int ticks in Uninstall(mill)) yield return ticks;
        var manager = map.GetComponent<MechanicalNetworkManager>();
        var nodes = (HashSet<CompMechanicalNode>)AccessTools.Field(typeof(MechanicalNetworkManager), "allNodes").GetValue(manager);
        Check(node.Network == null && !nodes.Contains(node), "Packed mill immediately leaves its network and manager registry");
        foreach (int ticks in Install(mill, oldPosition)) yield return ticks;
        Check(nodes.Count(n => n == node) == 1 && node.Network != null && mill.GetComp<CompMechanicalUser>().HasPower,
            "Reinstalled mill is registered once and regains mechanical power");
        // The basic worktable currently holds the interrupted electronic batch.
        int bills = componentBench.BillStack.Count;
        foreach (int ticks in Uninstall(componentBench)) yield return ticks;
        Check(componentBench.BillStack.Count == bills, "Packing retains every completed and pending production bill");
        var partial = componentBench.BillStack.Bills.OfType<Bill_ProductionWithUft>().Single(b => b.repeatCount > 0);
        Check(partial.BoundUft != null && partial.BoundUft.workLeft == savedComponentWork, "Packing retains the exact interrupted electronic batch");
        portableStill = (Building_WorkTable)Spawn("HD_BatchStill", -12, -12);
        portableStill.GetComp<CompRefuelable>().Refuel(37.5f);
        foreach (int ticks in Uninstall(portableStill)) yield return ticks;
        Check(portableStill.GetComp<CompRefuelable>().Fuel == 37.5f, "Packed batch still retains all fuel");
    }
    private void CheckRelocationLoaded()
    {
        Check(!componentBench.Spawned && !portableStill.Spawned && portableStill.GetComp<CompRefuelable>().Fuel == 37.5f,
            "Actual save/load preserves both packed worktables and exact fuel before the first tick");
    }
    private IEnumerable<int> RelocationAfterLoad()
    {
        var mill = map.listerThings.ThingsOfDef(Def<ThingDef>("HD_LineShaftMillingMachine")).OfType<Building>().Single();
        var oldPosition = mill.Position;
        map.listerThings.ThingsOfDef(Def<ThingDef>("HD_LargeSteamEngine")).Single().TryGetComp<CompRefuelable>().Refuel(120f);
        foreach (int ticks in Uninstall(mill)) yield return ticks;
        foreach (int ticks in Install(mill, oldPosition + new IntVec3(-2, 0, 0))) yield return ticks;
        var node = mill.GetComp<CompMechanicalNode>();
        var nodes = (HashSet<CompMechanicalNode>)AccessTools.Field(typeof(MechanicalNetworkManager), "allNodes").GetValue(map.GetComponent<MechanicalNetworkManager>());
        Check(mill.Position != oldPosition && nodes.Count(n => n == node) == 1 && node.Network != null && mill.GetComp<CompMechanicalUser>().HasPower,
            "Moving the mill to a different shaft cell reconnects it exactly once");
        foreach (int ticks in Install(componentBench, map.Center + new IntVec3(1, 0, -10))) yield return ticks;
        var gun = Def<RecipeDef>("HD_Make_Gun_SprMOne_Weapon");
        Check(gun.Worker.AvailableOnNow(componentBench), "Reinstalled basic worktable reconnects to its original machining facility");
        foreach (int ticks in Install(portableStill, map.Center + new IntVec3(-12, 0, -16))) yield return ticks;
        Check(portableStill.GetComp<CompRefuelable>().Fuel == 37.5f, "Reinstalled batch still retains its saved fuel");
    }
}