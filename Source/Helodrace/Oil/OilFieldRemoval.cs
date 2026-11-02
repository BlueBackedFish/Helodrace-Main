using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace;

// Reuse the native Remove floor designation, construction work and cancellation.
internal static class OilFieldRemoval
{
    internal static bool EquipmentBlocks(Map map, IntVec3 cell)
    {
        if (map == null || !cell.InBounds(map)
            || !map.GetComponent<MapComponent_OilFields>().IsOilFieldTerrain(cell)) return false;
        foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
        {
            if (thing is not Building building) continue;
            var rig = building.GetComp<CompOilFieldDevelopment>();
            if (rig != null && building.Position + rig.Props.floorOffset == cell) return true;
            if (building.GetComp<CompOilProducer>() != null && building.OccupiedRect().Contains(cell)) return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(Designator_RemoveFloor), nameof(Designator_RemoveFloor.CanDesignateCell))]
internal static class OilFieldRemovalDesignation
{
    private static void Postfix(Designator_RemoveFloor __instance, IntVec3 c, ref AcceptanceReport __result)
    {
        if (OilFieldRemoval.EquipmentBlocks(__instance.Map, c))
            __result = "HD_OilField_RemoveEquipmentFirst".Translate();
    }
}

[HarmonyPatch(typeof(WorkGiver_ConstructRemoveFloor), nameof(WorkGiver_ConstructRemoveFloor.HasJobOnCell))]
internal static class OilFieldRemovalWork
{
    private static void Postfix(Pawn pawn, IntVec3 c, ref bool __result)
    {
        if (__result && OilFieldRemoval.EquipmentBlocks(pawn.Map, c)) __result = false;
    }
}

[HarmonyPatch]
internal static class OilFieldTerrainRemoval
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.RemoveTopLayer));
        yield return AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.SetTerrain));
    }
    private static void Postfix(Map ___map, IntVec3 c)
    {
        ___map?.GetComponent<MapComponent_OilFields>()?.NotifyTerrainChanged(c);
    }
}
[HarmonyPatch(typeof(JobDriver_RemoveFloor), "DoEffect")]
internal static class OilFieldRemovalCompletion
{
    private static bool Prefix(JobDriver_RemoveFloor __instance, IntVec3 c) =>
        !OilFieldRemoval.EquipmentBlocks(__instance.pawn.Map, c);
}