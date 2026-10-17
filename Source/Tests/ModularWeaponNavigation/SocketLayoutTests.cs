using System;
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
                var layout = ModularWeaponSocketLayout.Arrange(area, compact);
                int found = 0;
                int pages = layout.PageCount;
                for (int page = 0; page < pages; page++)
                {
                    var cards = layout.Rects.Where((box, index) => layout.PageFor(index) == page).ToList();
                    for (int i = 0; i < cards.Count; i++)
                    {
                        Rect card = cards[i];
                        float expectedWidth = compact[found] ? card.height : Math.Min(172f, area.width);
                        if (Math.Abs(card.width - expectedWidth) > 0.001f)
                            throw new Exception("Only multi-part rail boxes are square; fixed boxes keep their text/image width.");
                        if (card.xMin < area.xMin || card.yMin < area.yMin
                            || card.xMax > area.xMax + 0.001 || card.yMax > area.yMax + 0.001)
                            throw new Exception("Socket boxes exceed their preview bounds.");
                        for (int j = 0; j < i; j++)
                            if (card.xMin < cards[j].xMax && card.xMax > cards[j].xMin
                                && card.yMin < cards[j].yMax && card.yMax > cards[j].yMin)
                                throw new Exception("Socket boxes overlap each other.");
                        if (layout.PageFor(found) != page)
                            throw new Exception("New attachments must reveal the correct box page.");
                        found++;
                    }
                }
                if (found != count) throw new Exception("Pagination hides or duplicates socket boxes.");
            }
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
        Console.WriteLine("PASS: mixed fixed/rail boxes, pagination, camera-following boxes, rail projection and parent-centered bare sliders.");
    }
}
