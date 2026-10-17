using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;
using UnityEngine;

internal static class SocketLayoutTests
{
    public static void Run()
    {
        foreach (var area in new[] { new Rect(325, 120, 700, 360), new Rect(10, 20, 370, 280),
            new Rect(0, 0, 180, 160), new Rect(0, 0, 800, 420), new Rect(0, 0, 40, 140) })
            foreach (int count in new[] { 0, 1, 6, 17, 40 })
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var compact = Enumerable.Range(0, count)
                    .Select(index => mode == 1 || (mode == 2 && index % 2 == 0)).ToList();
                var anchors = Enumerable.Range(0, count).Select(index => area.center + new Vector2(
                    (float)Math.Cos(index * 2.399963) * area.width * 0.25f,
                    (float)Math.Sin(index * 2.399963) * area.height * 0.15f)).ToList();
                var weapon = new Rect(area.x + area.width * 0.25f, area.y + area.height * 0.4f,
                    area.width * 0.5f, area.height * 0.2f);
                var layout = Arrange(area, compact, anchors, weapon);
                int found = 0;
                int pages = layout.PageCount;
                for (int page = 0; page < pages; page++)
                {
                    var indices = Enumerable.Range(0, count).Where(index => layout.PageFor(index) == page).ToList();
                    var cards = indices.Select(index => layout.Rects[index]).ToList();
                    for (int i = 0; i < cards.Count; i++)
                    {
                        Rect card = cards[i];
                        float expectedWidth = compact[indices[i]] ? card.height : Math.Min(172f, area.width);
                        if (Math.Abs(card.width - expectedWidth) > 0.001f)
                            throw new Exception("Only multi-part rail boxes are square; fixed boxes keep their text/image width.");
                        if (card.xMin < area.xMin || card.yMin < area.yMin
                            || card.xMax > area.xMax + 0.001 || card.yMax > area.yMax + 0.001)
                            throw new Exception("Socket boxes exceed their preview bounds.");
                        for (int j = 0; j < i; j++)
                            if (card.xMin < cards[j].xMax && card.xMax > cards[j].xMin
                                && card.yMin < cards[j].yMax && card.yMax > cards[j].yMin)
                                throw new Exception("Socket boxes overlap each other.");
                        found++;
                    }
                }
                if (found != count) throw new Exception("Pagination hides or duplicates socket boxes.");
            }
        var distributed = Arrange(new Rect(0, 0, 700, 360),
            new[] { false, true, false, true, true, false },
            new[] { new Vector2(180, 160), new Vector2(300, 130), new Vector2(500, 175),
                new Vector2(350, 220), new Vector2(420, 190), new Vector2(260, 180) }, new Rect(175, 135, 350, 90));
        if (distributed.Rects.Select(box => (int)box.center.y).Distinct().Count() < 3)
            throw new Exception("Callouts must fan around their socket anchors instead of forming two fixed rows.");
        var repeated = Arrange(new Rect(0, 0, 700, 360),
            new[] { false, true, false, true, true, false },
            new[] { new Vector2(180, 160), new Vector2(300, 130), new Vector2(500, 175),
                new Vector2(350, 220), new Vector2(420, 190), new Vector2(260, 180) }, new Rect(175, 135, 350, 90));
        for (int i = 0; i < repeated.Rects.Count; i++)
            if (repeated.Rects[i].x != distributed.Rects[i].x || repeated.Rects[i].y != distributed.Rects[i].y)
                throw new Exception("Unchanged callouts must retain deterministic positions.");
        CheckRailPriority();
        var start = new Vector2(100, 200);
        var end = new Vector2(300, 100);
        var middle = new Vector2(200, 150);
        if (Math.Abs(ModularWeaponSocketLayout.Fraction(middle, start, end) - 0.5f) > 0.00001
            || ModularWeaponSocketLayout.Distance(middle, start, end) > 0.00001)
            throw new Exception("Rotated rail hit testing loses the clicked mounting position.");
        if (ModularWeaponSocketLayout.Fraction(new Vector2(-500, 800), start, end) != 0
            || ModularWeaponSocketLayout.Fraction(new Vector2(900, -700), start, end) != 1
            || ModularWeaponSocketLayout.Fraction(middle, start, start) != 0.5f)
            throw new Exception("Rail endpoint clamping and zero-length rails must be safe.");
        foreach (float zoom in new[] { 0.25f, 1f, 3f, 8f })
            foreach (var shift in new[] { Vector2.zero, new Vector2(-500, -500), new Vector2(2000, 2000) })
            {
                var slider = ModularWeaponSocketLayout.FloatingSlider(start * zoom + shift, end * zoom + shift);
                float expected = (float)Math.Sqrt(200f * 200f + 100f * 100f) * zoom;
                if (Math.Abs(slider.Rect.width - expected) > 0.001f || slider.Rect.height != 16f
                    || !slider.Contains((slider.Start + slider.End) * 0.5f)
                    || slider.Contains((slider.Start + slider.End) * 0.5f + new Vector2(0, 100)))
                    throw new Exception("The slider must match projected rail length at every zoom, retain its thickness and follow rotated rails.");
            }
        var above = ModularWeaponSocketLayout.FloatingSlider(new Vector2(100, 200), new Vector2(500, 200));
        if (above.Rect.yMax >= 200 || Math.Abs(above.Rect.center.x - 300f) > 0.001f || above.Rect.width != 400f)
            throw new Exception("Only the slider should appear, centered above the parent rail.");
        var original = new Rect(25, 80, 172, 56);
        var followed = ModularWeaponSocketLayout.Follow(original, new Vector2(1000, -500));
        if (followed.x != 1025 || followed.y != -420 || followed.width != 172 || followed.height != 56)
            throw new Exception("Boxes must follow the weapon without screen-edge clamping or changing their readable size.");
        Console.WriteLine("PASS: rail-first callouts, rail-side preservation, source-index pagination, camera following and rail-length sliders.");
    }

    private static ModularWeaponSocketLayout.Layout Arrange(Rect area, IList<bool> compact, IList<Vector2> anchors, Rect weapon)
        => ModularWeaponSocketLayout.Arrange(area, anchors.Select((anchor, i) =>
            new ModularWeaponSocketLayout.Card { Anchor = anchor, Compact = compact[i] }).ToList(), weapon);

    private static void CheckRailPriority()
    {
        var area = new Rect(0, 0, 500, 300);
        var weapon = new Rect(50, 100, 400, 100);
        var anchor = new Vector2(250, 100);
        foreach (bool compact in new[] { false, true })
        {
            var rail = new ModularWeaponSocketLayout.Card {
                Anchor = anchor, Compact = compact, Rail = true, Outward = new Vector2(0, -1) };
            var alone = ModularWeaponSocketLayout.Arrange(area, new[] { rail }, weapon);
            var crowded = Enumerable.Range(0, 15).Select(i => new ModularWeaponSocketLayout.Card {
                Anchor = new Vector2(80 + i * 20, 100) }).ToList();
            crowded.Add(rail); // XML order deliberately gives every ordinary upper socket a head start.
            crowded.Add(new ModularWeaponSocketLayout.Card {
                Anchor = new Vector2(250, 200), Rail = true, Compact = true, Outward = new Vector2(0, 1) });
            for (int i = 0; i < 18; i++) crowded.Add(rail);
            var layout = ModularWeaponSocketLayout.Arrange(area, crowded, weapon);
            if (layout.Rects[15].x != alone.Rects[0].x || layout.Rects[15].y != alone.Rects[0].y
                || layout.PageFor(15) != 0)
                throw new Exception("Ordinary sockets must not displace a rail, regardless of definition order or card style.");
            if (layout.Rects[16].yMin < 200 - 0.001f)
                throw new Exception("Bottom rail boxes must remain below their rail.");
            for (int i = 15; i < crowded.Count; i++)
                if (i != 16 && layout.Rects[i].yMax > anchor.y + 0.001f)
                    throw new Exception("Crowded top rails must use another page instead of moving underneath the gun.");
            if (layout.PageCount <= 1 || layout.PageCount != layout.Pages.Max() + 1)
                throw new Exception("Rail-first pagination must count pages independently of source order.");
            for (int i = 0; i < crowded.Count; i++)
            {
                Rect box = layout.Rects[i];
                float expectedWidth = crowded[i].Compact ? box.height : 172f;
                if (Math.Abs(box.width - expectedWidth) > 0.001f || layout.PageFor(i) < 0
                    || layout.PageFor(i) >= layout.PageCount || box.xMin < 0 || box.yMin < 0
                    || box.xMax > area.xMax + 0.001 || box.yMax > area.yMax + 0.001)
                    throw new Exception("Priority placement must preserve source identity, card style and viewport bounds.");
                for (int j = 0; j < i; j++)
                    if (layout.PageFor(i) == layout.PageFor(j) && box.xMin < layout.Rects[j].xMax
                        && box.xMax > layout.Rects[j].xMin && box.yMin < layout.Rects[j].yMax
                        && box.yMax > layout.Rects[j].yMin)
                        throw new Exception("Priority placement must check every box on the same page, including nonadjacent indices.");
            }
        }
        // A flipped/rotated parent sends the rail normal rightward on screen.
        var rotated = ModularWeaponSocketLayout.Arrange(area, new[] {
            new ModularWeaponSocketLayout.Card { Anchor = new Vector2(300, 150), Compact = true,
                Rail = true, Outward = new Vector2(1, 0) } }, weapon);
        if (rotated.Rects[0].xMin < 300 - 0.001f)
            throw new Exception("Rail side constraints must follow the parent's projected orientation.");
    }
}
