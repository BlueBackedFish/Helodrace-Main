using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Pan/zoom state for a preview panel. Shared by the customization window and the offset
    // tuner so both behave identically: drag to move, scroll to zoom.
    //
    // Pan is stored in PIXELS rather than world cells, so it stays put on screen while the
    // zoom changes - that is what feels right when you are lining a part up by eye.
    public class PreviewView
    {
        public float zoom = 1f;
        public Vector2 pan = Vector2.zero;

        public const float MinZoom = 0.25f;
        public const float MaxZoom = 8f;

        public void Reset()
        {
            zoom = 1f;
            pan = Vector2.zero;
        }

        // Call before drawing. Returns the origin the preview should be centred on.
        public Vector2 HandleInput(Rect area, Vector2 defaultOrigin)
        {
            Event e = Event.current;

            if (area.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    // Zoom toward the cursor: keep the point under the mouse anchored,
                    // otherwise zooming pushes the part you are looking at off-screen.
                    float old = zoom;
                    zoom = Mathf.Clamp(zoom * (1f - e.delta.y * 0.06f), MinZoom, MaxZoom);

                    if (!Mathf.Approximately(old, zoom))
                    {
                        Vector2 origin = defaultOrigin + pan;
                        Vector2 toCursor = e.mousePosition - origin;
                        pan -= toCursor * (zoom / old - 1f);
                    }
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && (e.button == 0 || e.button == 2))
                {
                    pan += e.delta;
                    e.Use();
                }
            }

            return defaultOrigin + pan;
        }

        // Small overlay controls in the corner of the preview.
        public void DrawControls(Rect area)
        {
            Rect resetRect = new Rect(area.xMax - 96f, area.y + 6f, 90f, 22f);
            if (Widgets.ButtonText(resetRect, "LGMW_ResetView".Translate()))
                Reset();

            Rect zoomLabel = new Rect(area.x + 8f, area.y + 6f, 120f, 22f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(zoomLabel, zoom.ToString("0.##") + "x");
            Text.Font = GameFont.Small;
        }
    }
}
