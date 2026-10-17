using System;
using Helodrace.ModernWar;
using UnityEngine;

internal static class ImageBoundsTests
{
    public static void Run()
    {
        Rect body, outline;
        var alpha = new byte[64 * 32];
        if (ModularWeaponImageBounds.TryFind(alpha, 64, 32, out body))
            throw new Exception("Empty canvases must not reduce usable image scale.");
        alpha[12 * 64 + 26] = 255;
        alpha[18 * 64 + 40] = 64;
        alpha[0] = 8;
        if (!ModularWeaponImageBounds.TryFind(alpha, 64, 32, out body)
            || body.xMin <= 0 || body.width >= 1)
            throw new Exception("Off-center visible art must be cropped without counting nearly transparent noise.");
        var edgeAlpha = new byte[64 * 32];
        edgeAlpha[8 * 64 + 20] = 255;
        edgeAlpha[23 * 64 + 47] = 255;
        ModularWeaponImageBounds.TryFind(edgeAlpha, 64, 32, out outline);
        Rect union = ModularWeaponImageBounds.Union(
            ModularWeaponImageBounds.OnCanvas(body, new Vector2(2f, 1f)),
            ModularWeaponImageBounds.OnCanvas(outline, new Vector2(2f, 1f)));
        if (union.xMin > (20f / 64 - 0.5f) * 2 || union.xMax < (48f / 64 - 0.5f) * 2
            || union.yMin > 8f / 32 - 0.5f || union.yMax < 24f / 32 - 0.5f)
            throw new Exception("Crop must contain the outline beyond the body on every side.");
        var cornerAlpha = new byte[4 * 8];
        cornerAlpha[0] = 255;
        cornerAlpha[31] = 255;
        ModularWeaponImageBounds.TryFind(cornerAlpha, 4, 8, out outline);
        if (outline.xMin != 0 || outline.yMin != 0 || outline.xMax != 1 || outline.yMax != 1)
            throw new Exception("Padding must preserve edge pixels without exceeding the source canvas.");
        Console.WriteLine("PASS: transparent/off-center images, outline-inclusive union, canvas scale and edge padding.");
    }
}
