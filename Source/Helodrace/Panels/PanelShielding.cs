using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Panels
{
    public sealed class ShieldingReport
    {
        public int totalFaces, coveredFaces, roofHoles, openDoors, leaks;
        public bool enclosed;
        internal readonly HashSet<Building> supports = new();
        public bool Protected => enclosed && totalFaces > 0 && totalFaces == coveredFaces && roofHoles == 0 && openDoors == 0 && leaks == 0;
    }

    public sealed class MapComponent_PanelShielding : MapComponent
    {
        private readonly Dictionary<Room, ShieldingReport> cache = new();
        public MapComponent_PanelShielding(Map map) : base(map) { }
        public void Invalidate() => cache.Clear();
        public void Invalidate(Building support)
        {
            foreach (Room room in cache.Where(pair=>pair.Value.supports.Contains(support)).Select(pair=>pair.Key).ToArray()) cache.Remove(room);
        }
        public ShieldingReport Report(Room room)
        {
            // A cached room must not protect equipment between a wall mutation
            // and the engine's region rebuild, even within the same game tick.
            if (room == null || map.regionAndRoomUpdater.AnythingToRebuild) return new ShieldingReport();
            if (cache.TryGetValue(room, out ShieldingReport known)) return known;
            var result = new ShieldingReport { enclosed = !room.TouchesMapEdge && !room.UsesOutdoorTemperature && !room.IsDoorway };
            if (room.TouchesMapEdge || room.IsDoorway) return cache[room] = result;
            MapComponent_WallPanels panels = map.GetComponent<MapComponent_WallPanels>();
            foreach (IntVec3 cell in room.Cells)
            {
                if (!cell.Roofed(map)) result.roofHoles++;
                for (int direction = 0; direction < 4; direction++)
                {
                    IntVec3 boundary = cell + GenAdj.CardinalDirections[direction];
                    if (!boundary.InBounds(map)) { result.leaks++; continue; }
                    Building support = boundary.GetEdifice(map);
                    if (support is not Building_Door && boundary.GetRoom(map) == room) continue;
                    result.totalFaces++;
                    if(support!=null)result.supports.Add(support);
                    if (!PanelRules.IsHost(support?.def)) { result.leaks++; continue; }
                    if (support is Building_Door door && door.Open) result.openDoors++;
                    Rot4 face = new((direction + 2) % 4);
                    if (panels.For(support).Any(p => p.Rotation == face && p.Properties.electromagneticShielding && PanelRules.IsActive(p))) result.coveredFaces++;
                }
            }
            return cache[room] = result;
        }
        public bool Protects(Thing thing)
        {
            if (thing?.Spawned != true || thing.Map != map) return false;
            Room room = null;
            foreach (IntVec3 cell in thing.OccupiedRect())
            {
                Room next = cell.GetRoom(map);
                if (next == null || next.IsDoorway || room != null && next != room) return false;
                room = next;
            }
            return Report(room).Protected;
        }
    }

    public static class PanelShielding
    {
        public static void Invalidate(Map map,Building support=null)
        {
            var service=map?.GetComponent<MapComponent_PanelShielding>();
            if(support==null)service?.Invalidate();else service?.Invalidate(support);
        }
        public static bool Protected(Thing thing) => thing?.Spawned == true && thing.Map.GetComponent<MapComponent_PanelShielding>().Protects(thing);
        public static bool SolarActive(Map map) => HasBlackout(map, true);
        public static bool OtherBlackout(Map map) => HasBlackout(map, false);
        private static bool HasBlackout(Map map, bool solar)
        {
            for (GameConditionManager manager = map?.gameConditionManager; manager != null; manager = manager.Parent)
                for (int i=0;i<manager.ActiveConditions.Count;i++)
                {
                    GameCondition condition=manager.ActiveConditions[i];
                    if ((condition.def?.defName=="SolarFlare")==solar && condition.ElectricityDisabled && condition.CanApplyOnMap(map) && !condition.HiddenByOtherCondition(map)) return true;
                }
            return false;
        }
        public static bool SolarBlocked(Thing thing) => thing?.Spawned == true && SolarActive(thing.Map) && !Protected(thing);
    }

    [HarmonyPatch]
    public static class PanelShieldStructureChanged
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Building), nameof(Building.SpawnSetup));
            yield return AccessTools.Method(typeof(Building), nameof(Building.DeSpawn));
        }
        static void Prefix(Building __instance, out Map __state)
        {
            __state = __instance.def.building?.isEdifice==true?__instance.Map:null;
            PanelShielding.Invalidate(__state);
        }
        static void Postfix(Building __instance, Map __state)
        {
            PanelShielding.Invalidate(__state);
            if (__instance.def.building?.isEdifice==true && __instance.Map != __state) PanelShielding.Invalidate(__instance.Map);
        }
    }

    [HarmonyPatch]
    public static class PanelShieldDoorChanged
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Building_Door),"DoorOpen");
            yield return AccessTools.Method(typeof(Building_Door),"DoorTryClose");
        }
        static void Prefix(Building_Door __instance,MethodBase __originalMethod,out bool __state)
        {
            __state=__instance.Open;
            if(!__state&&__originalMethod.Name=="DoorOpen")PanelShielding.Invalidate(__instance.Map,__instance);
        }
        static void Postfix(Building_Door __instance,bool __state)
        {
            if(__state!=__instance.Open)PanelShielding.Invalidate(__instance.Map,__instance);
        }
    }

    [HarmonyPatch]
    public static class PanelShieldRoomChanged
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Room), nameof(Room.Notify_RoofChanged));
            yield return AccessTools.Method(typeof(Room), nameof(Room.Notify_RoomShapeChanged));
        }
        static void Prefix(Room __instance) => PanelShielding.Invalidate(__instance.Map);
    }

    [HarmonyPatch]
    public static class PanelShieldRoofChanged
    {
        private static readonly AccessTools.FieldRef<RoofGrid, Map> MapOf = AccessTools.FieldRefAccess<RoofGrid, Map>("map");
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(RoofGrid), nameof(RoofGrid.SetRoof));
            yield return AccessTools.Method(typeof(RoofGrid), nameof(RoofGrid.RemoveRoofUnsafe));
        }
        static void Prefix(RoofGrid __instance) => PanelShielding.Invalidate(MapOf(__instance));
        static void Postfix(RoofGrid __instance) => PanelShielding.Invalidate(MapOf(__instance));
    }

    [HarmonyPatch(typeof(PowerNet), nameof(PowerNet.PowerNetTick))]
    public static class PanelShieldPowerNet
    {
        private static readonly AccessTools.FieldRef<PowerNet, List<CompPowerTrader>> Parts = AccessTools.FieldRefAccess<PowerNet, List<CompPowerTrader>>("powerComps");
        private static readonly AccessTools.FieldRef<PowerNet, List<CompPowerBattery>> Batteries = AccessTools.FieldRefAccess<PowerNet, List<CompPowerBattery>>("batteryComps");
        static void Prefix(PowerNet __instance)
        {
            bool solar = PanelShielding.SolarActive(__instance.Map);
            if (solar)
            {
                foreach (CompPowerTrader part in Parts(__instance))
                {
                    bool shielded = PanelShielding.Protected(part.parent);
                    if (!shielded) part.PowerOn = false;
                }
            }
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => PanelBlackoutCall.Replace(instructions,AccessTools.Method(typeof(PanelShieldPowerNet),nameof(NetworkDisabled)));
        public static bool NetworkDisabled(GameConditionManager manager,Map map,PowerNet net)
        {
            bool original=manager.ElectricityDisabled(map);
            if(!original||!PanelShielding.SolarActive(map)||PanelShielding.OtherBlackout(map))return original;
            bool protectedPart=Parts(net).Any(p=>PanelShielding.Protected(p.parent))||Batteries(net).Any(b=>PanelShielding.Protected(b.parent));
            return !protectedPart;
        }
    }

    // Replace only the exact native consumer call. The global map query itself
    // is never patched, including calls made indirectly by callbacks/UI.
    public static class PanelBlackoutCall
    {
        public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,MethodInfo replacement)
        {
            MethodInfo query=AccessTools.Method(typeof(GameConditionManager),nameof(GameConditionManager.ElectricityDisabled));
            int count=0;
            foreach(CodeInstruction code in instructions)
            {
                if(!code.Calls(query)){yield return code;continue;}
                var load=new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code.labels);code.labels.Clear();
                load.blocks.AddRange(code.blocks);code.blocks.Clear();
                yield return load;yield return new CodeInstruction(OpCodes.Call,replacement);count++;
            }
            if(count!=1)throw new InvalidOperationException("Wall panel local-power call no longer matches the installed engine.");
        }
    }

    // These native components check the map blackout directly rather than only
    // CompPowerTrader.PowerOn. Preserve their ordinary power/facility gates.
    [HarmonyPatch]
    public static class PanelShieldLocalDevice
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(CompFacilityInactiveWhenElectricityDisabled),"CanBeActive");
            yield return AccessTools.PropertyGetter(typeof(CompAutoPowered),"AppearsPowered");
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => PanelBlackoutCall.Replace(instructions,AccessTools.Method(typeof(PanelShieldLocalDevice),nameof(DeviceDisabled)));
        public static bool DeviceDisabled(GameConditionManager manager,Map map,ThingComp component)
        {
            bool original=manager.ElectricityDisabled(map);
            return original&&!(PanelShielding.SolarActive(map)&&!PanelShielding.OtherBlackout(map)&&PanelShielding.Protected(component.parent));
        }
    }

    [HarmonyPatch(typeof(FlickUtility), nameof(FlickUtility.WantsToBeOn))]
    public static class PanelUnshieldedConsumers
    {
        static void Postfix(Thing t, ref bool __result)
        {
            if (__result && t.TryGetComp<CompPowerTrader>() != null && PanelShielding.SolarBlocked(t)) __result = false;
        }
    }

    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn), MethodType.Getter)]
    public static class PanelUnshieldedPowerState
    {
        static void Postfix(CompPowerTrader __instance, ref bool __result)
        {
            if (__result && PanelShielding.SolarBlocked(__instance.parent)) __result = false;
        }
    }

    [HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOutput), MethodType.Getter)]
    public static class PanelUnshieldedGeneration
    {
        static void Postfix(CompPowerTrader __instance, ref float __result)
        {
            if (__result != 0 && PanelShielding.SolarBlocked(__instance.parent)) __result = 0;
        }
    }

    [HarmonyPatch]
    public static class PanelUnshieldedBatteryEnergy
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(CompPowerBattery), nameof(CompPowerBattery.StoredEnergy));
            yield return AccessTools.PropertyGetter(typeof(CompPowerBattery), nameof(CompPowerBattery.AmountCanAccept));
        }
        static void Postfix(CompPowerBattery __instance, ref float __result)
        {
            if (PanelShielding.SolarBlocked(__instance.parent)) __result = 0;
        }
    }

    [HarmonyPatch]
    public static class PanelUnshieldedBatteryTransfer
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CompPowerBattery), nameof(CompPowerBattery.AddEnergy));
            yield return AccessTools.Method(typeof(CompPowerBattery), nameof(CompPowerBattery.DrawPower));
        }
        static bool Prefix(CompPowerBattery __instance) => !PanelShielding.SolarBlocked(__instance.parent);
    }
}
