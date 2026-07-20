using System.Text;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public class MapComponent_GasDensityMouseover : MapComponent
    {
        private const float MinShownDensity = 0.02f;
        private const float LabelPadding = 6f;
        private const float CornerMargin = 12f;
        private const float BottomOffset = 150f;

        private static readonly Color BackgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.78f);
        private static readonly Color OutlineColor = new Color(0.68f, 0.72f, 0.62f, 0.9f);

        private static ThingDef photochlorogenGasDef;
        private static ThingDef sweetGasDef;

        private readonly StringBuilder labelBuilder = new StringBuilder(96);
        private IntVec3 cachedCell = IntVec3.Invalid;
        private int cachedPhotoPercent = -1;
        private int cachedSweetPercent = -1;
        private string cachedLabel;

        public MapComponent_GasDensityMouseover(Map map) : base(map)
        {
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();

            if (Find.CurrentMap != map || Mouse.IsInputBlockedNow)
            {
                return;
            }

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map))
            {
                return;
            }

            int photoPercent = DensityPercent(map.GetComponent<MapComponent_PhotochlorogenGasGrid>()?.VisualDensityAt(cell) ?? 0f);
            int sweetPercent = DensityPercent(map.GetComponent<MapComponent_SweetGasGrid>()?.VisualDensityAt(cell) ?? 0f);
            if (photoPercent <= 0 && sweetPercent <= 0)
            {
                return;
            }

            string label = GetCachedLabel(cell, photoPercent, sweetPercent);
            DrawCornerLabel(label);
        }

        private static int DensityPercent(float density)
        {
            if (density <= MinShownDensity)
            {
                return 0;
            }

            return Mathf.Clamp(Mathf.RoundToInt(density * 100f), 1, 100);
        }

        private string GetCachedLabel(IntVec3 cell, int photoPercent, int sweetPercent)
        {
            if (cell == cachedCell && photoPercent == cachedPhotoPercent && sweetPercent == cachedSweetPercent && cachedLabel != null)
            {
                return cachedLabel;
            }

            cachedCell = cell;
            cachedPhotoPercent = photoPercent;
            cachedSweetPercent = sweetPercent;
            labelBuilder.Length = 0;
            labelBuilder.Append("Gas density");
            AppendGasLine(PhotochlorogenGasDef, photoPercent);
            AppendGasLine(SweetGasDef, sweetPercent);
            cachedLabel = labelBuilder.ToString();
            return cachedLabel;
        }

        private void AppendGasLine(ThingDef gasDef, int percent)
        {
            if (percent <= 0)
            {
                return;
            }

            labelBuilder.AppendLine();
            labelBuilder.Append(gasDef?.LabelCap ?? "Gas");
            labelBuilder.Append(": ");
            labelBuilder.Append(percent);
            labelBuilder.Append('%');
        }

        private static void DrawCornerLabel(string label)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Vector2 size = Text.CalcSize(label);
            Rect rect = new Rect(CornerMargin, UI.screenHeight - BottomOffset - size.y - LabelPadding * 2f, size.x + LabelPadding * 2f, size.y + LabelPadding * 2f);
            rect.y = Mathf.Max(CornerMargin, rect.y);

            Widgets.DrawBoxSolidWithOutline(rect, BackgroundColor, OutlineColor, 1);
            Widgets.Label(rect.ContractedBy(LabelPadding), label);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private static ThingDef PhotochlorogenGasDef
        {
            get
            {
                if (photochlorogenGasDef == null)
                {
                    photochlorogenGasDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_PhotochlorogenGas");
                }

                return photochlorogenGasDef;
            }
        }

        private static ThingDef SweetGasDef
        {
            get
            {
                if (sweetGasDef == null)
                {
                    sweetGasDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_SweetGas");
                }

                return sweetGasDef;
            }
        }
    }
}
