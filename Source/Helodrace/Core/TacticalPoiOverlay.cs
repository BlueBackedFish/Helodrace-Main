using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public static class TacticalPoiOverlaySettings
    {
        public static bool Enabled;
        public static bool ShowStrategic = true;

        public static bool Visible(TacticalPointOfInterest poi)
        {
            return poi != null && ShowStrategic;
        }
    }

    public sealed class MapComponent_TacticalPoiOverlay : MapComponent
    {
        private const float PanelMargin = 12f;
        private const float PanelTop = 90f;
        private const float PanelWidth = 500f;
        private const float PanelPadding = 8f;

        private static readonly Color StrategicColor = new Color(0.25f, 0.9f, 1f);
        private static readonly Color PanelBackground = new Color(0.035f, 0.045f, 0.06f, 0.92f);
        private static readonly Color PanelOutline = new Color(0.5f, 0.8f, 0.95f, 0.9f);

        public MapComponent_TacticalPoiOverlay(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (!ShouldDraw()) return;

            MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(2);
            foreach (TacticalPointOfInterest poi in analysis.PointsOfInterest)
            {
                if (!TacticalPoiOverlaySettings.Visible(poi) || !view.Contains(poi.Cell)) continue;
                Color color = ColorFor(poi);
                GenDraw.DrawFieldEdges(new List<IntVec3> { poi.Cell }, color, 0.16f);
                GenDraw.DrawRadiusRing(poi.Cell, 0.72f, color);
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            if (!ShouldDraw() || Mouse.IsInputBlockedNow) return;

            MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(2);
            foreach (TacticalPointOfInterest poi in analysis.PointsOfInterest)
            {
                if (!TacticalPoiOverlaySettings.Visible(poi) || !view.Contains(poi.Cell)) continue;
                GenMapUI.DrawThingLabel(
                    GenMapUI.LabelDrawPosFor(poi.Cell),
                    TacticalPoiPresentation.ShortLabel(poi),
                    ColorFor(poi));
            }

            IntVec3 mouseCell = UI.MouseCell();
            if (!mouseCell.InBounds(map)) return;
            List<TacticalPointOfInterest> hovered = analysis.PointsOfInterest
                .Where(poi => TacticalPoiOverlaySettings.Visible(poi)
                    && (poi.Contains(mouseCell) || poi.Cell.DistanceToSquared(mouseCell) <= 2f))
                .ToList();
            if (hovered.Count == 0) return;

            TacticalCellData cellData = analysis.At(mouseCell);
            StringBuilder text = new StringBuilder(512);
            text.Append("TACTICAL POI  ");
            text.Append(mouseCell);
            foreach (TacticalPointOfInterest poi in hovered)
            {
                text.AppendLine();
                text.AppendLine();
                text.Append(TacticalPoiPresentation.DetailedLabel(poi));
            }
            text.AppendLine();
            text.AppendLine();
            text.Append(TacticalPoiPresentation.CellThreatLabel(cellData));
            DrawInformationPanel(text.ToString());
        }

        private bool ShouldDraw()
        {
            return Prefs.DevMode && TacticalPoiOverlaySettings.Enabled && Find.CurrentMap == map;
        }

        private static Color ColorFor(TacticalPointOfInterest poi)
        {
            return StrategicColor;
        }

        private static void DrawInformationPanel(string label)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            float height = Text.CalcHeight(label, PanelWidth - PanelPadding * 2f) + PanelPadding * 2f;
            Rect rect = new Rect(PanelMargin, PanelTop, PanelWidth, height);
            Widgets.DrawBoxSolidWithOutline(rect, PanelBackground, PanelOutline, 1);
            Widgets.Label(rect.ContractedBy(PanelPadding), label);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }
    }

    public static class TacticalPoiPresentation
    {
        public static string ShortLabel(TacticalPointOfInterest poi)
        {
            return $"POI {poi.RoomRole}  V{poi.StrategicValue:0}";
        }

        public static string DetailedLabel(TacticalPointOfInterest poi)
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append(poi.RoomRole);
            builder.Append("  center=");
            builder.Append(poi.Cell);
            builder.Append("  bounds=");
            builder.Append(poi.Bounds);
            builder.AppendLine();
            builder.Append("Strategic value: ");
            builder.Append(poi.StrategicValue.ToString("0.0"));
            builder.Append("  [");
            builder.Append(string.Join(", ", poi.StrategicKinds.OrderBy(kind => kind)));
            builder.Append(']');
            string sources = string.Join(", ", poi.SourceLabels.OrderBy(label => label));
            if (!sources.NullOrEmpty())
            {
                builder.AppendLine();
                builder.Append("Sources: ");
                builder.Append(sources);
            }
            return builder.ToString();
        }

        public static string CellThreatLabel(TacticalCellData data)
        {
            return $"Cell: value={data.StrategicValue:0.0}  geometry={data.TotalThreat:0.0}\n"
                + $"Door {data.DoorThreat:0.0} | Wall/passage {data.WallThreat:0.0}";
        }
    }
}
