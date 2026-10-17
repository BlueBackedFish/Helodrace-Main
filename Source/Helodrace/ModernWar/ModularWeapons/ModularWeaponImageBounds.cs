using UnityEngine;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponImageBounds
    {
        public static bool TryFind(byte[] alpha, int width, int height, out Rect uv)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (alpha[y * width + x] <= 8) continue;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            uv = new Rect(0f, 0f, 1f, 1f);
            if (maxX < minX || maxY < minY) return false;
            // Include antialiasing and narrow outline edges lost during downsampling.
            minX = Mathf.Max(0, minX - 2);
            minY = Mathf.Max(0, minY - 2);
            maxX = Mathf.Min(width - 1, maxX + 2);
            maxY = Mathf.Min(height - 1, maxY + 2);
            uv = new Rect(minX / (float)width, minY / (float)height,
                (maxX - minX + 1f) / width, (maxY - minY + 1f) / height);
            return true;
        }

        public static Rect Union(Rect a, Rect b)
        {
            float x = Mathf.Min(a.xMin, b.xMin), y = Mathf.Min(a.yMin, b.yMin);
            return new Rect(x, y, Mathf.Max(a.xMax, b.xMax) - x, Mathf.Max(a.yMax, b.yMax) - y);
        }

        public static Rect OnCanvas(Rect uv, Vector2 size)
            => new Rect((uv.x - 0.5f) * size.x, (uv.y - 0.5f) * size.y,
                uv.width * size.x, uv.height * size.y);
    }
}
