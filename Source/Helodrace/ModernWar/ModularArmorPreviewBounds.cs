using System;
using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    // Owned by one customization dialog. Read each texture once, then reuse its
    // visible bounds while changing parts or rotating the preview.
    internal sealed class ModularArmorPreviewBounds
    {
        private readonly Dictionary<Texture2D, Rect> cache = new Dictionary<Texture2D, Rect>();

        public Rect CombinedBounds(List<Texture2D> layers)
        {
            bool found = false;
            Rect combined = new Rect();
            foreach (Texture2D texture in layers)
            {
                if (texture == null) continue;
                if (!cache.TryGetValue(texture, out Rect bounds))
                {
                    Color32[] pixels = ReadPixels(texture);
                    byte[] alpha = new byte[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
                    TryFindVisibleBounds(alpha, texture.width, texture.height, out bounds);
                    cache.Add(texture, bounds);
                }
                if (bounds.width <= 0f || bounds.height <= 0f) continue;
                combined = found
                    ? Rect.MinMaxRect(Math.Min(combined.xMin, bounds.xMin),
                        Math.Min(combined.yMin, bounds.yMin),
                        Math.Max(combined.xMax, bounds.xMax),
                        Math.Max(combined.yMax, bounds.yMax))
                    : bounds;
                found = true;
            }
            return found ? combined : new Rect(0f, 0f, 1f, 1f);
        }

        internal static bool TryFindVisibleBounds(byte[] alpha, int width, int height, out Rect bounds)
        {
            bounds = new Rect();
            if (alpha == null || width <= 0 || height <= 0
                || alpha.Length != (long)width * height) return false;
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Include translucent visor art, ignoring near-invisible
                    // antialiasing pixels that can expand an otherwise empty canvas.
                    if (alpha[y * width + x] < 8) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            if (maxX < minX) return false;
            bounds = Rect.MinMaxRect((float)minX / width, (float)minY / height,
                (float)(maxX + 1) / width, (float)(maxY + 1) / height);
            return true;
        }

        internal static Rect DestinationFor(Rect available, Rect bounds,
            int textureWidth, int textureHeight, Rect minimumCrop)
        {
            // The authored crop still controls minimum zoom, but never cuts off
            // installed parts. Both axes use one scale to preserve proportions.
            float canvasWidth = Math.Max(bounds.width * textureWidth + 8f,
                Math.Max(1f, minimumCrop.width * textureWidth));
            float canvasHeight = Math.Max(bounds.height * textureHeight + 8f,
                Math.Max(1f, minimumCrop.height * textureHeight));
            float scale = Math.Min(available.width / canvasWidth, available.height / canvasHeight);
            float width = bounds.width * textureWidth * scale;
            float height = bounds.height * textureHeight * scale;
            return new Rect(available.center.x - width * 0.5f,
                available.center.y - height * 0.5f, width, height);
        }

        private static Color32[] ReadPixels(Texture2D texture)
        {
            if (texture.isReadable) return texture.GetPixels32();
            RenderTexture previous = RenderTexture.active;
            RenderTexture buffer = RenderTexture.GetTemporary(texture.width, texture.height,
                0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(texture, buffer);
                RenderTexture.active = buffer;
                readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(buffer);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }
    }
}
