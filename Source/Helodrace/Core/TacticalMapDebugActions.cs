using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace Helodrace
{
    public static class TacticalMapDebugActions
    {
        private const int MaximumDrawnCells = 3000;

        [DebugAction(
            "Helodrace/Tactical AI",
            "Open raid tactical plans",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void OpenRaidTacticalPlans()
        {
            RaidTacticalDebugSession.Open(Find.CurrentMap);
        }

        [DebugAction(
            "Helodrace/Tactical AI",
            "Rebuild and summarize tactical maps",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RebuildAndSummarize()
        {
            Map map = Find.CurrentMap;
            MapComponent_TacticalMapAnalysis analysis = map?.GetComponent<MapComponent_TacticalMapAnalysis>();
            if (analysis == null) return;

            analysis.ForceRebuild();
            string objectives = string.Join(", ", analysis.StrategicTargets
                .GroupBy(target => target.Kind)
                .Select(group => $"{group.Key}={group.Count()}"));
            Log.Message($"[Helodrace Tactical AI] Objectives: {(objectives.NullOrEmpty() ? "none" : objectives)}. "
                + $"Geometry build={analysis.LastStaticBuildMilliseconds} ms.");
            Messages.Message(
                $"Tactical maps rebuilt: {analysis.StrategicTargets.Count} objectives, "
                    + $"{analysis.PointsOfInterest.Count} POIs.",
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        [DebugAction(
            "Helodrace/Tactical AI",
            "Toggle tactical POI overlay",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TogglePoiOverlay()
        {
            TacticalPoiOverlaySettings.Enabled = !TacticalPoiOverlaySettings.Enabled;
            Messages.Message(
                $"Tactical POI overlay: {(TacticalPoiOverlaySettings.Enabled ? "ON" : "OFF")}",
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        [DebugAction(
            "Helodrace/Tactical AI",
            "Toggle strategic room POIs",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ToggleStrategicPois()
        {
            TacticalPoiOverlaySettings.ShowStrategic = !TacticalPoiOverlaySettings.ShowStrategic;
            Messages.Message(
                $"Strategic POIs: {(TacticalPoiOverlaySettings.ShowStrategic ? "ON" : "OFF")}",
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        [DebugAction(
            "Helodrace/Tactical AI",
            "Inspect tactical data under mouse",
            allowedGameStates = AllowedGameStates.PlayingOnMap,
            actionType = DebugActionType.ToolMap)]
        public static void InspectMouseCell()
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            MapComponent_TacticalMapAnalysis analysis = map?.GetComponent<MapComponent_TacticalMapAnalysis>();
            if (analysis == null || !cell.InBounds(map)) return;

            TacticalCellData data = analysis.At(cell);
            List<TacticalPointOfInterest> pois = analysis.PointsOfInterest
                .Where(poi => poi.Contains(cell) || poi.Cell.DistanceToSquared(cell) <= 2f)
                .ToList();
            string details = pois.Count == 0
                ? "No POI at this cell."
                : string.Join(" | ", pois.Select(TacticalPoiPresentation.DetailedLabel));
            Log.Message($"[Helodrace Tactical AI] {cell}: {TacticalPoiPresentation.CellThreatLabel(data)} | {details}");
        }

        [DebugAction(
            "Helodrace/Tactical AI",
            "Draw strategic value map",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DrawStrategicValueMap()
        {
            DrawGrid(data => data.StrategicValue, 0.5f, "value");
        }

        [DebugAction(
            "Helodrace/Tactical AI",
            "Draw door and wall geometry map",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DrawThreatMap()
        {
            DrawGrid(data => data.TotalThreat, 4f, "geometry");
        }

        private static void DrawGrid(Func<TacticalCellData, float> selector, float threshold, string label)
        {
            Map map = Find.CurrentMap;
            MapComponent_TacticalMapAnalysis analysis = map?.GetComponent<MapComponent_TacticalMapAnalysis>();
            if (analysis == null) return;
            analysis.RequestAnalysis();

            List<KeyValuePair<IntVec3, float>> scored = map.AllCells
                .Select(cell => new KeyValuePair<IntVec3, float>(cell, selector(analysis.CachedAt(cell))))
                .Where(pair => pair.Value >= threshold)
                .OrderByDescending(pair => pair.Value)
                .Take(MaximumDrawnCells)
                .ToList();
            float maximum = scored.Count == 0 ? 1f : scored[0].Value;
            foreach (KeyValuePair<IntVec3, float> pair in scored)
            {
                map.debugDrawer.FlashCell(pair.Key, Math.Min(1f, 0.15f + pair.Value / maximum * 0.85f), null, 1200);
            }

            Messages.Message(
                $"Drew {scored.Count} highest {label} cells for 20 seconds.",
                MessageTypeDefOf.NeutralEvent,
                false);
        }
    }
}
