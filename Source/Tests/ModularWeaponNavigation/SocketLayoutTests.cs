using System;
using Helodrace.ModernWar;
using UnityEngine;

internal static class SocketLayoutTests
{
    public static void Run()
    {
        foreach (var area in new[] { new Rect(325, 120, 700, 360), new Rect(10, 20, 370, 280),
            new Rect(0, 0, 180, 160), new Rect(0, 0, 800, 420) })
            foreach (int count in new[] { 0, 1, 6, 17, 40 })
            {
                int found = 0;
                int pages = ModularWeaponSocketLayout.PageCount(area, count);
                for (int page = 0; page < pages; page++)
                {
                    var cards = ModularWeaponSocketLayout.Arrange(area, count, page);
                    for (int i = 0; i < cards.Count; i++)
                    {
                        Rect card = cards[i];
                        if (card.xMin < area.xMin || card.yMin < area.yMin
                            || card.xMax > area.xMax + 0.001 || card.yMax > area.yMax + 0.001)
                            throw new Exception("Socket boxes exceed their preview bounds.");
                        for (int j = 0; j < i; j++)
                            if (card.xMin < cards[j].xMax && card.xMax > cards[j].xMin
                                && card.yMin < cards[j].yMax && card.yMax > cards[j].yMin)
                                throw new Exception("Socket boxes overlap each other.");
                        if (ModularWeaponSocketLayout.PageFor(area, found) != page)
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
        Console.WriteLine("PASS: socket box bounds/non-overlap, 40-item pagination, attachment reveal, rotated rail projection and endpoint clamping.");
    }
}
