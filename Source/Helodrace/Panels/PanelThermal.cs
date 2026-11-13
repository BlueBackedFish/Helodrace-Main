using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Panels
{
    public static class PanelThermal
    {
        private static readonly AccessTools.FieldRef<RoomTempTracker, Room> RoomOf = AccessTools.FieldRefAccess<RoomTempTracker, Room>("room");
        private static readonly AccessTools.FieldRef<RoomTempTracker, int> Cycle = AccessTools.FieldRefAccess<RoomTempTracker, int>("cycleIndex");
        public static float Factor(float resistance) => 1f / (1f + MathF.Max(0, resistance));

        public static float Resistance(Building host, int axis)
        {
            if (host == null || !host.Spawned || host is Building_Door door && door.Open) return 0;
            float resistance = host is Building_SandwichPanelWall ? 7f * CoreFraction(host) : 0f;
            foreach (Building_WallPanel panel in host.Map.GetComponent<MapComponent_WallPanels>().For(host))
                if ((panel.Rotation.AsInt & 1) == (axis & 1) && PanelRules.IsActive(panel))
                    resistance += panel.Properties.thermalResistance * CoreFraction(panel);
            return resistance;
        }
        private static float CoreFraction(ThingWithComps thing) => thing.GetComp<CompPolymerCore>()?.FractionRemaining ?? 1f;

        // equalizeCells stores the cell two steps away, beyond the wall. At a
        // corner several paths can share that cell: average only those actual
        // paths, never all of a room's walls.
        public static float PathFactor(Room room, IntVec3 destination, Map map)
        {
            float total = 0; int count = 0;
            for (int axis = 0; axis < 4; axis++)
            {
                IntVec3 delta = GenAdj.CardinalDirections[axis];
                IntVec3 wallCell = destination + delta, roomCell = wallCell + delta;
                if (!roomCell.InBounds(map) || roomCell.GetRoom(map) != room || !wallCell.InBounds(map)) continue;
                Building wall = PanelRules.HostAt(wallCell, map);
                if (wall == null) continue;
                float resistance = Resistance(wall, axis);
                // The engine uses its outdoor fallback for a filled second cell
                // in a thick wall. Retain that fallback and add only real layers.
                if (destination.InBounds(map) && PanelRules.HostAt(destination, map) is Building second)
                    resistance += Resistance(second, axis);
                total += Factor(resistance); count++;
            }
            return count > 0 ? total / count : 1;
        }

        public static bool ReadAir(IntVec3 cell, Map map, out float temperature, RoomTempTracker tracker)
        {
            bool found = GenTemperature.TryGetDirectAirTemperatureForCell(cell, map, out temperature);
            if (found) temperature = tracker.Temperature + (temperature - tracker.Temperature) * PathFactor(RoomOf(tracker), cell, map);
            return found;
        }
        public static float ReadOutdoor(MapTemperature temperatures, RoomTempTracker tracker)
        {
            List<IntVec3> cells = tracker.EqualizeCellsForReading;
            float factor = cells.Count == 0 ? 1 : PathFactor(RoomOf(tracker), cells[Cycle(tracker) % cells.Count], RoomOf(tracker).Map);
            return tracker.Temperature + (temperatures.OutdoorTemp - tracker.Temperature) * factor;
        }
    }

    [HarmonyPatch(typeof(RoomTempTracker), "WallEqualizationTempChangePerInterval")]
    public static class PanelWallHeatTransfer
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo air = AccessTools.Method(typeof(GenTemperature), nameof(GenTemperature.TryGetDirectAirTemperatureForCell));
            MethodInfo outdoor = AccessTools.PropertyGetter(typeof(MapTemperature), nameof(MapTemperature.OutdoorTemp));
            int reads = 0, fallbacks = 0;
            foreach (CodeInstruction code in instructions)
            {
                MethodInfo replacement = null;
                if (code.Calls(air)) { replacement = AccessTools.Method(typeof(PanelThermal), nameof(PanelThermal.ReadAir)); reads++; }
                if (code.Calls(outdoor)) { replacement = AccessTools.Method(typeof(PanelThermal), nameof(PanelThermal.ReadOutdoor)); fallbacks++; }
                if (replacement != null)
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(code.labels); code.labels.Clear();
                    load.blocks.AddRange(code.blocks); code.blocks.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else yield return code;
            }
            if (reads != 1 || fallbacks != 1) throw new InvalidOperationException("Wall panel thermal path no longer matches the installed engine.");
        }
    }

    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.EqualizeTemperaturesThroughBuilding))]
    public static class PanelDoorHeatTransfer
    {
        static void Prefix(Building b, ref float rate)
        {
            if (b is Building_Door door && !door.Open)
                rate *= PanelThermal.Factor(PanelThermal.Resistance(b, b.Rotation.AsInt));
        }
    }
}
