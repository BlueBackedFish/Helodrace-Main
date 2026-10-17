using System;
using System.Collections.Generic;
using UnityEngine;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponSocketLayout
    {
        public const float CardHeight = 56f;
        public sealed class Card
        {
            public Vector2 Anchor, Outward;
            public bool Compact, Rail;
        }

        public sealed class Layout
        {
            public readonly List<Rect> Rects = new List<Rect>();
            public readonly List<int> Pages = new List<int>();
            public int PageCount { get; internal set; } = 1;
            public int PageFor(int index) => index >= 0 && index < Pages.Count ? Pages[index] : 0;
        }

        public static Layout Arrange(Rect area, IList<Card> cards, Rect weapon)
        {
            var result = new Layout();
            var order = new List<int>();
            // Reserve rail positions before ordinary sockets, preserving source indices for selection.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i].Rail == (pass == 0)) order.Add(i);
            for (int i = 0; i < cards.Count; i++)
            {
                result.Rects.Add(default(Rect));
                result.Pages.Add(-1);
            }
            float height = Math.Min(CardHeight, area.height);
            foreach (int index in order)
            {
                Card card = cards[index];
                bool imageOnly = card.Compact;
                float cardHeight = imageOnly ? Math.Min(height, area.width) : height;
                float width = Math.Min(imageOnly ? cardHeight : 172f, area.width);
                Rect? placed = null;
                int page;
                for (page = 0; page < result.PageCount; page++)
                {
                    placed = Place(area, width, cardHeight, card, weapon, result, page, false, index);
                    if (placed.HasValue) break;
                }
                if (!placed.HasValue)
                {
                    // A fresh page keeps a crowded top rail above the gun rather than moving it below.
                    if (!result.Pages.Contains(result.PageCount - 1)) page = result.PageCount - 1;
                    placed = Place(area, width, cardHeight, card, weapon, result, page, false, index)
                        ?? Place(area, width, cardHeight, card, weapon, result, page, true, index);
                }
                result.Rects[index] = placed ?? new Rect(area.x, area.y, width, cardHeight);
                result.Pages[index] = page;
                result.PageCount = Math.Max(result.PageCount, page + 1);
            }
            return result;
        }

        private static Rect? Place(Rect area, float width, float height, Card card, Rect weapon,
            Layout layout, int page, bool allowWeaponOverlap, int index)
        {
            Rect? best = null;
            float score = float.MaxValue;
            Vector2 anchor = card.Anchor;
            Vector2 normal = card.Rail ? card.Outward : Vector2.zero;
            bool constrained = Math.Abs(normal.x) + Math.Abs(normal.y) > 0.001f;
            double outward = constrained ? Math.Atan2(normal.y, normal.x)
                : Math.Atan2(anchor.y - weapon.center.y, anchor.x - weapon.center.x);
            if (!constrained && Math.Abs(anchor.x - weapon.center.x) + Math.Abs(anchor.y - weapon.center.y) < 1f)
                outward = index * 2.399963;
            for (int ring = 0; ring < 5; ring++)
                for (int step = 0; step < 24; step++)
                {
                    int turn = step % 2 == 0 ? step / 2 : -(step + 1) / 2;
                    double angle = outward + turn * Math.PI / 12.0;
                    Vector2 center = anchor + new Vector2((float)Math.Cos(angle) * (width * 0.5f + 26f + ring * 32f),
                        (float)Math.Sin(angle) * (height * 0.5f + 34f + ring * 28f));
                    Consider(area, width, height, center, anchor, normal, weapon, layout, page, allowWeaponOverlap,
                        ref best, ref score);
                }
            // Dense or very small viewports may need another page, but never drop a socket.
            if (!best.HasValue)
            {
                // Include exact corners when the rail faces a very narrow edge of the viewport.
                foreach (float x in new[] { area.x + width * 0.5f, area.xMax - width * 0.5f })
                    foreach (float y in new[] { area.y + height * 0.5f, area.yMax - height * 0.5f })
                        Consider(area, width, height, new Vector2(x, y), anchor, normal,
                            weapon, layout, page, allowWeaponOverlap, ref best, ref score);
                for (float y = area.y; y <= area.yMax - height + 0.001f; y += 12f)
                    for (float x = area.x; x <= area.xMax - width + 0.001f; x += 16f)
                        Consider(area, width, height, new Vector2(x + width * 0.5f, y + height * 0.5f),
                            anchor, normal, weapon, layout, page, allowWeaponOverlap, ref best, ref score);
            }
            return best;
        }

        private static void Consider(Rect area, float width, float height, Vector2 center, Vector2 anchor,
            Vector2 normal, Rect weapon, Layout layout, int page, bool allowWeaponOverlap, ref Rect? best, ref float score)
        {
            Rect box = new Rect(Math.Max(area.x, Math.Min(area.xMax - width, center.x - width * 0.5f)),
                Math.Max(area.y, Math.Min(area.yMax - height, center.y - height * 0.5f)), width, height);
            Vector2 delta = box.center - anchor;
            float extent = (Math.Abs(normal.x) * width + Math.Abs(normal.y) * height) * 0.5f;
            // Relax only when even an empty viewport cannot fit a box fully outside the rail.
            float maxProjection = normal.x * ((normal.x >= 0f ? area.xMax - width * 0.5f : area.x + width * 0.5f) - anchor.x)
                + normal.y * ((normal.y >= 0f ? area.yMax - height * 0.5f : area.y + height * 0.5f) - anchor.y);
            if (delta.x * normal.x + delta.y * normal.y < Math.Min(extent, maxProjection) - 0.001f) return;
            if (!allowWeaponOverlap && Overlap(box, weapon, 8f)) return;
            for (int i = 0; i < layout.Rects.Count; i++)
                if (layout.Pages[i] == page && Overlap(box, layout.Rects[i], 8f)) return;
            float candidate = delta.x * delta.x + delta.y * delta.y;
            if (candidate >= score) return;
            best = box;
            score = candidate;
        }

        private static bool Overlap(Rect a, Rect b, float gap)
            => a.xMin < b.xMax + gap && a.xMax + gap > b.xMin
                && a.yMin < b.yMax + gap && a.yMax + gap > b.yMin;

        public static Rect Follow(Rect box, Vector2 offset)
            => new Rect(box.x + offset.x, box.y + offset.y, box.width, box.height);

        public sealed class RailSlider
        {
            public Rect Rect;
            public float Angle;
            public Vector2 Start, End;
            public bool Contains(Vector2 point) => Rect.width > 0f && Distance(point, Start, End) <= 12f;
        }

        public static RailSlider FloatingSlider(Vector2 start, Vector2 end)
        {
            float dx = end.x - start.x, dy = end.y - start.y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            Vector2 offset = length < 0.001f ? Vector2.zero : new Vector2(dy / length, -dx / length) * 20f;
            if (offset.y > 0f) offset = offset * -1f;
            Vector2 center = (start + end) * 0.5f + offset;
            return new RailSlider { Rect = new Rect(center.x - length * 0.5f, center.y - 8f, length, 16f),
                Angle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI), Start = start + offset, End = end + offset };
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
