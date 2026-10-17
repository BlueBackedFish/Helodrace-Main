using UnityEngine;

namespace Helodrace.ModernWar
{
    internal sealed class ModularWeaponPreviewCamera
    {
        public const float MinimumZoom = 0.25f;
        public const float MaximumZoom = 8f;
        public float Zoom { get; private set; } = 1f;
        public Vector2 Pan { get; private set; }
        private Rect? contentBounds;

        public float ScaleFor(Rect area)
        {
            if (!contentBounds.HasValue) return Mathf.Min(area.width, area.height) * 0.82f * Zoom;
            Rect bounds = contentBounds.Value;
            return Mathf.Min(area.width / Mathf.Max(0.001f, bounds.width),
                area.height / Mathf.Max(0.001f, bounds.height)) * 0.9f * Zoom;
        }

        private Vector2 CenterOffset(Rect area)
        {
            Vector2 center = contentBounds.HasValue ? contentBounds.Value.center : Vector2.zero;
            return new Vector2(center.x, -center.y) * ScaleFor(area);
        }

        public Vector2 OriginFor(Rect area) => area.center + Pan - CenterOffset(area);

        public void SetBounds(Rect bounds) => contentBounds = bounds;

        public void ZoomAt(Rect area, float zoom, Vector2 pointer)
        {
            float previous = Zoom;
            Vector2 origin = OriginFor(area);
            Zoom = Mathf.Clamp(zoom, MinimumZoom, MaximumZoom);
            Pan = pointer - area.center + CenterOffset(area) - (pointer - origin) * (Zoom / previous);
        }

        public void Move(Vector2 delta) => Pan += delta;

        public void Reset()
        {
            Zoom = 1f;
            Pan = Vector2.zero;
        }
    }
}
