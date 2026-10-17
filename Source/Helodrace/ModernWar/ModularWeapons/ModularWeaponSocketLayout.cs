using System;
using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponSocketLayout
    {
        public const float CardHeight = 56f;
        public sealed class Layout
        {
            public readonly List<Rect> Rects = new List<Rect>();
            public readonly List<int> Pages = new List<int>();
            public int PageCount => Pages.Count == 0 ? 1 : Pages[Pages.Count - 1] + 1;
            public int PageFor(int index) => index >= 0 && index < Pages.Count ? Pages[index] : 0;
        }

        public static Layout Arrange(Rect area, IList<bool> compact)
        {
            var result = new Layout();
            int page = 0, row = 0;
            int rows = area.height >= 200f ? 2 : 1;
            float x = area.x;
            float height = Math.Min(CardHeight, area.height);
            foreach (bool imageOnly in compact)
            {
                float cardHeight = imageOnly ? Math.Min(height, area.width) : height;
                float width = Math.Min(imageOnly ? cardHeight : 172f, area.width);
                if (x > area.x && x + width > area.xMax + 0.001f)
                {
                    x = area.x;
                    if (++row >= rows) { page++; row = 0; }
                }
                result.Rects.Add(new Rect(x, row == 0 ? area.y : area.yMax - cardHeight, width, cardHeight));
                result.Pages.Add(page);
                x += width + 8f;
            }
            return result;
        }

        public static Rect Follow(Rect box, Vector2 offset)
            => new Rect(box.x + offset.x, box.y + offset.y, box.width, box.height);

        public static Rect FloatingSlider(Rect area, Vector2 start, Vector2 end)
        {
            float width = Math.Min(240f, Math.Max(1f, area.width - 8f));
            float height = Math.Min(16f, Math.Max(1f, area.height));
            float x = Math.Max(area.x, Math.Min(area.xMax - width, (start.x + end.x - width) * 0.5f));
            float y = Math.Max(area.y, Math.Min(area.yMax - height, Math.Min(start.y, end.y) - height - 12f));
            return new Rect(x, y, width, height);
        }

        public static float Fraction(Vector2 point, Vector2 start, Vector2 end)
        {
            float x = end.x - start.x, y = end.y - start.y;
            float length = x * x + y * y;
            if (length < 0.00001f) return 0.5f;
            return Math.Max(0f, Math.Min(1f, ((point.x - start.x) * x + (point.y - start.y) * y) / length));
        }

        public static float Distance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 closest = start + (end - start) * Fraction(point, start, end);
            float x = point.x - closest.x, y = point.y - closest.y;
            return (float)Math.Sqrt(x * x + y * y);
        }
    }
}
