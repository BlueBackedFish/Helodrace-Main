using System;
using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponSocketLayout
    {
        public const float CardHeight = 56f;
        public static int Capacity(Rect area)
            => Math.Max(1, (int)((area.width + 8f) / 180f)) * (area.height >= 200f ? 2 : 1);
        public static int PageCount(Rect area, int count)
            => Math.Max(1, (count + Capacity(area) - 1) / Capacity(area));
        public static int PageFor(Rect area, int index) => Math.Max(0, index) / Capacity(area);

        public static List<Rect> Arrange(Rect area, int count, int page)
        {
            int capacity = Capacity(area);
            int columns = area.height >= 200f ? capacity / 2 : capacity;
            page = Math.Max(0, Math.Min(PageCount(area, count) - 1, page));
            int shown = Math.Max(0, Math.Min(capacity, count - page * capacity));
            float width = (area.width - 8f * (columns - 1)) / columns;
            var result = new List<Rect>();
            for (int i = 0; i < shown; i++)
                result.Add(new Rect(area.x + (i % columns) * (width + 8f),
                    i < columns ? area.y : area.yMax - CardHeight, width, Math.Min(CardHeight, area.height)));
            return result;
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
