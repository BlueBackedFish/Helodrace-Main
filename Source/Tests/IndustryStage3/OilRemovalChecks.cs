using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tests.IndustryStage3;

public sealed partial class Checks
{
    private IntVec3 removedOilCell, cancelledOilCell;
    private TerrainDef restoredOilTerrain;
    private void ExposeOilRemoval()
    {
        Scribe_Values.Look(ref removedOilCell, "removedOilCell");
        Scribe_Values.Look(ref cancelledOilCell, "cancelledOilCell");
        Scribe_Defs.Look(ref restoredOilTerrain, "restoredOilTerrain");
    }
    private static bool HasOilRecord(Map targetMap, IntVec3 cell) =>
        ((List<OilFieldRecord>)AccessTools.Field(typeof(MapComponent_OilFields), "oilFields")
            .GetValue(targetMap.GetComponent<MapComponent_OilFields>())).Any(r => r.cell == cell);
    private IEnumerable<int> OilRemovalChecks()
    {
        worker.workSettings.SetPriority(WorkTypeDefOf.Construction, 1);
        removedOilCell = map.Center + new IntVec3(18, 0, 18);
        cancelledOilCell = map.Center + new IntVec3(21, 0, 21);
        restoredOilTerrain = TerrainDefOf.Soil;
        var fields = map.GetComponent<MapComponent_OilFields>();
        foreach (IntVec3 cell in new[] { removedOilCell, cancelledOilCell })
        {
            map.terrainGrid.SetTerrain(cell, restoredOilTerrain);
            map.terrainGrid.SetTerrain(cell, Def<TerrainDef>(MapComponent_OilFields.OilFieldTerrainDefName));
            fields.CreateOrReplace(cell, 1.1f, 0.7f);
        }
        var designator = new Designator_RemoveFloor();
        var giver = (WorkGiver_ConstructRemoveFloor)DefDatabase<WorkGiverDef>.AllDefsListForReading
            .Single(d => d.giverClass == typeof(WorkGiver_ConstructRemoveFloor)).Worker;
        Check(designator.CanDesignateCell(removedOilCell).Accepted, "Native Remove floor accepts an idle oil field");
        var pump = Spawn("HD_BasicPumpJack", 18, 18);
        Check(!designator.CanDesignateCell(removedOilCell).Accepted, "A pump prevents the player's oil-field removal designation");
        map.designationManager.AddDesignation(new Designation(removedOilCell, DesignationDefOf.RemoveFloor));
        Check(!giver.HasJobOnCell(worker, removedOilCell, true), "A newly placed pump also blocks an existing removal order");
        map.designationManager.DesignationAt(removedOilCell, DesignationDefOf.RemoveFloor).Delete();
        pump.Destroy();
        var rig = Spawn("HD_CableToolRig", 18, 18);
        Check(!designator.CanDesignateCell(removedOilCell).Accepted, "A drilling rig prevents oil-field removal");
        rig.Destroy();
        designator.DesignateSingleCell(cancelledOilCell);
        map.designationManager.DesignationAt(cancelledOilCell, DesignationDefOf.RemoveFloor).Delete();
        Check(HasOilRecord(map, cancelledOilCell) && fields.IsOilFieldTerrain(cancelledOilCell),
            "Cancelling removal preserves the original terrain and oil record");
        designator.DesignateSingleCell(removedOilCell);
        Check(giver.HasJobOnCell(worker, removedOilCell, true), "Removal work becomes available after equipment is dismantled");
        Check(worker.jobs.TryTakeOrderedJob(giver.JobOnCell(worker, removedOilCell, true)), "Worker begins actual oil-field removal");
        int elapsed = 0;
        while (removedOilCell.GetTerrain(map) != restoredOilTerrain && elapsed < 20000) { yield return 60; elapsed += 60; }
        Check(removedOilCell.GetTerrain(map) == restoredOilTerrain && !HasOilRecord(map, removedOilCell),
            "Actual floor-removal work restores soil and immediately removes the oil record");
        var overwritten = map.Center + new IntVec3(20, 0, 18);
        map.terrainGrid.SetTerrain(overwritten, Def<TerrainDef>(MapComponent_OilFields.OilFieldTerrainDefName));
        fields.CreateOrReplace(overwritten, 1f, 1f);
        map.terrainGrid.SetTerrain(overwritten, TerrainDefOf.Concrete);
        Check(!HasOilRecord(map, overwritten), "Replacing oil terrain also immediately removes its record");
    }
    private void CheckOilRemovalLoaded()
    {
        Map targetMap = Find.CurrentMap;
        Check(removedOilCell.GetTerrain(targetMap) == restoredOilTerrain && !HasOilRecord(targetMap, removedOilCell),
            "Removed well remains removed after actual save/load before any record lookup");
        Check(HasOilRecord(targetMap, cancelledOilCell)
            && targetMap.GetComponent<MapComponent_OilFields>().FieldAt(cancelledOilCell).pressure == 0.7f
            && targetMap.designationManager.DesignationAt(cancelledOilCell, DesignationDefOf.RemoveFloor) == null,
            "Cancelled removal and original well pressure survive actual save/load");
    }
}