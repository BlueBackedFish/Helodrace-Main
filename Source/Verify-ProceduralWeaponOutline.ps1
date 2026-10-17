param(
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) '.codex-staging/weapon-outline')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class HelodOutlinePrototype
{
    private static byte[] Alpha(Bitmap image)
    {
        var bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var result = new byte[image.Width * image.Height];
            var row = new byte[image.Width * 4];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                for (int x = 0; x < image.Width; x++) result[y * image.Width + x] = row[x * 4 + 3];
            }
            return result;
        }
        finally { image.UnlockBits(bits); }
    }

    private static Bitmap Stroke(byte[] source, int width, int height, int radius)
    {
        int w = width + radius * 2, h = height + radius * 2;
        var alpha = new byte[w * h];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(source, y * width, alpha, (y + radius) * w + radius, width);
        var horizontal = new byte[alpha.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte max = 0;
                for (int i = Math.Max(0, x - radius); i <= Math.Min(w - 1, x + radius); i++)
                    if (alpha[y * w + i] > max) max = alpha[y * w + i];
                horizontal[y * w + x] = max;
            }
        var output = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bits = output.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                Array.Clear(row, 0, row.Length);
                for (int x = 0; x < w; x++)
                {
                    byte max = 0;
                    for (int i = Math.Max(0, y - radius); i <= Math.Min(h - 1, y + radius); i++)
                        if (horizontal[i * w + x] > max) max = horizontal[i * w + x];
                    row[x * 4 + 3] = (byte)Math.Max(0, max - alpha[y * w + x]);
                }
                Marshal.Copy(row, 0, IntPtr.Add(bits.Scan0, y * bits.Stride), row.Length);
            }
        }
        finally { output.UnlockBits(bits); }
        return output;
    }

    private static void Verify()
    {
        var corner = new byte[25]; corner[0] = 255;
        using (var result = Stroke(corner, 5, 5, 2))
        {
            byte[] alpha = Alpha(result);
            if (result.Width != 9 || result.Height != 9 || alpha[0] != 255
                || alpha[2 * 9 + 2] != 0 || alpha[80] != 0)
                throw new Exception("Outline must extend beyond the source canvas, exclude opaque interiors and preserve empty space.");
        }
        var frame = new byte[49];
        for (int i = 0; i < 7; i++) { frame[i] = frame[42 + i] = frame[i * 7] = frame[i * 7 + 6] = 255; }
        using (var result = Stroke(frame, 7, 7, 1))
            if (Alpha(result)[4 * 9 + 4] != 0) throw new Exception("Large transparent holes must remain open.");
    }

    private static void DrawFit(Graphics graphics, Bitmap image, RectangleF area)
    {
        byte[] alpha = Alpha(image);
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (alpha[y * image.Width + x] > 8)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (right < left) throw new Exception("Fixture has no visible pixels.");
        float w = right - left + 1, h = bottom - top + 1;
        float scale = Math.Min(area.Width / w, area.Height / h) * 0.94f;
        var destination = new RectangleF(area.X + (area.Width - w * scale) / 2,
            area.Y + (area.Height - h * scale) / 2, w * scale, h * scale);
        graphics.DrawImage(image, destination, new RectangleF(left, top, w, h), GraphicsUnit.Pixel);
    }

    public static string Generate(string root, string output)
    {
        Verify();
        Directory.CreateDirectory(output);
        string[] paths = { "Tactical/HD_UB_M203A2", "Optics/HD_Sight_PointSightT2", "Optics/HD_IronSight_P320Front" };
        string[] names = { "M203A2", "PointSight T2", "P320 front sight" };
        string report = "PASS: padded canvas edges, outline-only alpha and preserved holes.\r\n";
        using (var montage = new Bitmap(1080, 850))
        using (var graphics = Graphics.FromImage(montage))
        using (var font = new Font("Segoe UI", 11))
        {
            graphics.Clear(Color.FromArgb(40, 46, 51));
            graphics.DrawString("Authored _Outline", font, Brushes.White, 15, 8);
            graphics.DrawString("Generated from body alpha", font, Brushes.White, 375, 8);
            graphics.DrawString("Body only", font, Brushes.White, 735, 8);
            for (int index = 0; index < paths.Length; index++)
            {
                string path = Path.Combine(root, "Textures/Weapons/Modular/" + paths[index]);
                using (var body = new Bitmap(path + ".png"))
                using (var authored = new Bitmap(body.Width, body.Height))
                {
                    using (var g = Graphics.FromImage(authored))
                    using (var outline = new Bitmap(path + "_Outline.png"))
                    { g.DrawImage(outline, 0, 0, body.Width, body.Height); g.DrawImage(body, 0, 0, body.Width, body.Height); }
                    int radius = Math.Max(2, body.Width / 256);
                    byte[] alpha = Alpha(body);
                    var watch = Stopwatch.StartNew();
                    using (var generated = Stroke(alpha, body.Width, body.Height, radius))
                    {
                        watch.Stop();
                        using (var g = Graphics.FromImage(generated))
                            g.DrawImage(body, new Rectangle(radius, radius, body.Width, body.Height),
                                0, 0, body.Width, body.Height, GraphicsUnit.Pixel);
                        // Source PNG DPI differs from Bitmap's default DPI. Explicit
                        // pixel coordinates must keep the body aligned to its stroke.
                        for (int pixelY = 0; pixelY < body.Height; pixelY++)
                            for (int pixelX = 0; pixelX < body.Width; pixelX++)
                            {
                                Color source = body.GetPixel(pixelX, pixelY);
                                if (source.A == 255 && generated.GetPixel(pixelX + radius, pixelY + radius).ToArgb() != source.ToArgb())
                                    throw new Exception("Generated outline changed or shifted an opaque body pixel.");
                            }
                        generated.Save(Path.Combine(output, names[index].Replace(' ', '_') + "_generated.png"), ImageFormat.Png);
                        float y = 40 + index * 270;
                        graphics.DrawString(names[index], font, Brushes.White, 15, y);
                        DrawFit(graphics, authored, new RectangleF(12, y + 28, 336, 228));
                        DrawFit(graphics, generated, new RectangleF(372, y + 28, 336, 228));
                        DrawFit(graphics, body, new RectangleF(732, y + 28, 336, 228));
                    }
                    report += string.Format("{0}: {1}x{2}, radius {3}px, generation {4:0.00} ms\r\n",
                        names[index], body.Width, body.Height, radius, watch.Elapsed.TotalMilliseconds);
                }
            }
            montage.Save(Path.Combine(output, "comparison.png"), ImageFormat.Png);
        }
        File.WriteAllText(Path.Combine(output, "results.txt"), report);
        return report;
    }
}
'@
[HelodOutlinePrototype]::Generate($repo, $OutputDirectory)
Write-Output (Join-Path $OutputDirectory 'comparison.png')
