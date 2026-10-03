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
            "Rebuild door and wall geometry map",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RebuildAndSummarize()
        {
            Map map = Find.CurrentMap;
            MapComponent_TacticalMapAnalysis analysis = map?.GetComponent<MapComponent_TacticalMapAnalysis>();
            if (analysis == null) return;

            analysis.ForceRebuild();
            Log.Message($"[Helodrace Tactical AI] Geometry rebuild queued; state={analysis.BuildStatus}. "
                + $"Last capture={analysis.LastCaptureMilliseconds:0.00} ms, "
                + $"calculation={analysis.LastCalculationMilliseconds:0.00} ms, "
                + $"max capture slice={analysis.MaximumCaptureSliceMilliseconds:0.00} ms, "
                + $"discarded={analysis.DiscardedCalculations}.");
            Messages.Message(
                "Door and wall geometry rebuild queued.",
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
            Log.Message($"[Helodrace Tactical AI] Cache={analysis.BuildStatus}; "
                + $"capture={analysis.LastCaptureMilliseconds:0.00} ms, "
                + $"worker={analysis.LastCalculationMilliseconds:0.00} ms, "
                + $"max slice={analysis.MaximumCaptureSliceMilliseconds:0.00} ms, "
                + $"total preparation={analysis.LastStaticBuildMilliseconds} ms.");

            TacticalCellData data = analysis.At(cell);
            Log.Message($"[Helodrace Tactical AI] {cell}: geometry={data.TotalThreat:0.0} "
                + $"(door={data.DoorThreat:0.0}, wall/passage={data.WallThreat:0.0}) "
                + $"structures={data.Structures} open={data.OpenDirections} "
                + $"exterior={data.ExteriorAccess}");
        }

        [DebugAction(
            "Helodrace/Tactical AI",
            "Draw exterior doors and openings",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DrawExteriorAccess()
        {
            DrawGrid(data => data.ExteriorAccess ? 1f : 0f, 1f, "exterior access");
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
            if (analysis.Completed == null)
            {
                Messages.Message("Geometry cache is preparing: " + analysis.BuildStatus,
                    MessageTypeDefOf.NeutralEvent, false);
                return;
            }

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
