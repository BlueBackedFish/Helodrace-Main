using System;
using Helodrace.ModernWar;
using UnityEngine;

internal static class CameraTests
{
    public static void Run()
    {
        var camera = new ModularWeaponPreviewCamera();
        var area = new Rect(340f, 170f, 620f, 300f);
        var pointer = new Vector2(720f, 260f);
        Vector2 point = (pointer - camera.OriginFor(area)) / camera.ScaleFor(area);
        camera.ZoomAt(area, 3f, pointer);
        Equal(point, (pointer - camera.OriginFor(area)) / camera.ScaleFor(area),
            "Zoom must keep the point under the cursor fixed in an off-origin viewport.");
        Vector2 origin = camera.OriginFor(area);
        camera.Move(new Vector2(-140f, 65f));
        Equal(camera.OriginFor(area), origin + new Vector2(-140f, 65f), "Pan must use screen pixels.");
        camera.ZoomAt(area, 100f, pointer);
        if (camera.Zoom != ModularWeaponPreviewCamera.MaximumZoom) throw new Exception("Maximum zoom not enforced.");
        point = (pointer - camera.OriginFor(area)) / camera.ScaleFor(area);
        camera.ZoomAt(area, 0f, pointer);
        if (camera.Zoom != ModularWeaponPreviewCamera.MinimumZoom) throw new Exception("Minimum zoom not enforced.");
        Equal(point, (pointer - camera.OriginFor(area)) / camera.ScaleFor(area),
            "Clamping zoom must still preserve the pointer anchor after panning.");
        camera.Reset();
        Equal(camera.OriginFor(area), area.center, "Reset must recenter the weapon.");
        if (camera.Zoom != 1f) throw new Exception("Reset must restore 100%.");
        var bounds = new Rect(2f, -3f, 4f, 2f);
        camera.SetBounds(bounds);
        foreach (var viewport in new[] { area, new Rect(20, 40, 220, 500), new Rect(0, 0, 300, 300) })
        {
            float scale = camera.ScaleFor(viewport);
            Equal(camera.OriginFor(viewport) + new Vector2(bounds.center.x, -bounds.center.y) * scale,
                viewport.center, "Fit must center an off-origin weapon group in every viewport shape.");
            if (bounds.width * scale > viewport.width || bounds.height * scale > viewport.height)
                throw new Exception("Fitted group exceeds its viewport.");
        }
        point = (pointer - camera.OriginFor(area)) / camera.ScaleFor(area);
        camera.ZoomAt(area, 4f, pointer);
        Equal(point, (pointer - camera.OriginFor(area)) / camera.ScaleFor(area),
            "Cursor anchoring must survive fitting an off-center group.");
        var fittedCamera = new ModularWeaponPreviewCamera();
        fittedCamera.SetBounds(bounds);
        foreach (var anchor in new[] { Vector2.zero, new Vector2(4, -2), new Vector2(-1, 3) })
        {
            Vector2 gui = new Vector2(anchor.x, -anchor.y);
            Vector2 before = fittedCamera.OriginFor(area) + gui * fittedCamera.ScaleFor(area);
            Vector2 after = camera.OriginFor(area) + gui * camera.ScaleFor(area);
            Equal(after - before, camera.OffsetFromFit(area, anchor),
                "Callouts must follow their weapon anchor through cursor zoom and pan.");
        }
        camera.Reset();
        camera.Move(new Vector2(130, -75));
        Equal(new Vector2(130, -75), camera.OffsetFromFit(area, new Vector2(4, -2)),
            "All boxes must translate exactly with middle-button panning.");
        Console.WriteLine("PASS: preview zoom anchoring, pan, zoom limits and reset.");
    }

    private static void Equal(Vector2 expected, Vector2 actual, string message)
    {
        if (Math.Abs(expected.x - actual.x) > 0.001f || Math.Abs(expected.y - actual.y) > 0.001f)
            throw new Exception(message);
    }
}

// Camera math is exercised without loading Unity's native graphics runtime.
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new Vector2(a.x / b, a.y / b);
    }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
    }
    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
