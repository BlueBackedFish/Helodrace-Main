using System;
using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponSocketLayout
    {
        public const float CardHeight = 56f;
        public static int Capacity(Rect area)
            => Math.Max(1, (int)((area.width + 8f) / (CardHeight + 8f))) * (area.height >= 200f ? 2 : 1);
        public static int PageCount(Rect area, int count)
            => Math.Max(1, (count + Capacity(area) - 1) / Capacity(area));
        public static int PageFor(Rect area, int index) => Math.Max(0, index) / Capacity(area);

        public static List<Rect> Arrange(Rect area, int count, int page)
        {
            int capacity = Capacity(area);
            int columns = area.height >= 200f ? capacity / 2 : capacity;
            page = Math.Max(0, Math.Min(PageCount(area, count) - 1, page));
            int shown = Math.Max(0, Math.Min(capacity, count - page * capacity));
            float size = Math.Min(CardHeight, Math.Min(area.width, area.height));
            var result = new List<Rect>();
            for (int i = 0; i < shown; i++)
            {
                int rowCount = Math.Min(columns, shown - (i / columns) * columns);
                float left = area.x + (area.width - rowCount * size - (rowCount - 1) * 8f) * 0.5f;
                result.Add(new Rect(left + (i % columns) * (size + 8f),
                    i < columns ? area.y : area.yMax - size, size, size));
            }
            return result;
        }

        public static Rect FloatingSlider(Rect area, Vector2 start, Vector2 end, Vector2 anchor)
        {
            float width = Math.Min(240f, Math.Max(1f, area.width - 8f));
            float height = Math.Min(46f, Math.Max(1f, area.height));
            float x = Math.Max(area.x, Math.Min(area.xMax - width, anchor.x - width * 0.5f));
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
