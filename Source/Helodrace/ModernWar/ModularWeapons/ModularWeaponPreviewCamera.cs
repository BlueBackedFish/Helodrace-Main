using UnityEngine;

namespace Helodrace.ModernWar
{
    internal sealed class ModularWeaponPreviewCamera
    {
        public const float MinimumZoom = 0.25f;
        public const float MaximumZoom = 8f;
        public float Zoom { get; private set; } = 1f;
        public Vector2 Pan { get; private set; }

        public float ScaleFor(Rect area) => Mathf.Min(area.width, area.height) * 0.82f * Zoom;
        public Vector2 OriginFor(Rect area) => area.center + Pan;

        public void ZoomAt(Rect area, float zoom, Vector2 pointer)
        {
            float previous = Zoom;
            Vector2 origin = OriginFor(area);
            Zoom = Mathf.Clamp(zoom, MinimumZoom, MaximumZoom);
            Pan = pointer - area.center - (pointer - origin) * (Zoom / previous);
        }

        public void Move(Vector2 delta) => Pan += delta;

        public void Reset()
        {
            Zoom = 1f;
            Pan = Vector2.zero;
        }
    }
}
