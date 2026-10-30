using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Helodrace.ModernWar;
using NC = Waylith.Math.Color;
using NC32 = Waylith.Math.Color32;
using GC = UnityEngine.Color;
using GC32 = UnityEngine.Color32;

namespace Helodrace.Tests
{
    internal static class NativeIconChecks
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
            Type cache = typeof(ModularWeaponIconCache);
            var blend = (Func<NC32, NC, NC, NC>)cache.GetMethod("BlendPixel", methods).CreateDelegate(typeof(Func<NC32, NC, NC, NC>));
            Type boundary = cache.Assembly.GetType("Helodrace.GameGeometry", true);
            var toNative = (Func<GC, NC>)boundary.GetMethod("ToNative", methods, null, new[] { typeof(GC) }, null).CreateDelegate(typeof(Func<GC, NC>));
            var toGame = (Func<NC, GC>)boundary.GetMethod("ToGame", methods, null, new[] { typeof(NC) }, null).CreateDelegate(typeof(Func<NC, GC>));
            var tints = new[] { GC.white, GC.clear, new GC(1, .95f, .75f, .16f), new GC(.25f, .5f, .75f, .5f),
                new GC(1.2f, .1f, -.2f, 1), new GC(.5f, .5f, .5f, 2), new GC(1, 1, 1, -.2f), new GC(.1f, .2f, .3f, .01f) };
            var destinations = new[] { GC.clear, GC.white, new GC(.2f, .5f, .7f, .1f), new GC(.8f, .1f, .3f, .5f),
                new GC(1.2f, -.2f, .7f, 1), new GC(0, 0, 0, -.5f) };
            int cases = 0, components = 0;
            float maxError = 0;
            foreach (int pattern in new[] { 0, 1, 2 })
                for (int alpha = 0; alpha < 256; alpha++)
                    foreach (GC tint in tints)
                        foreach (GC destination in destinations)
                        {
                            var sample = pattern == 0 ? new GC32(255, 255, 255, (byte)alpha)
                                : pattern == 1 ? new GC32((byte)alpha, (byte)(255 - alpha), (byte)((alpha * 73) % 256), (byte)alpha)
                                    : new GC32(0, 127, 255, (byte)alpha);
                            // Frozen 7f5992db per-pixel source-over calculation.
                            GC source = (GC)sample * tint;
                            GC expected = destination;
                            if (source.a > 0)
                            {
                                float a = source.a + destination.a * (1 - source.a);
                                if (a > 0)
                                { expected = (source * source.a + destination * (destination.a * (1 - source.a))) / a; expected.a = a; }
                            }
                            GC actual = toGame(blend(new NC32(sample.r, sample.g, sample.b, sample.a), toNative(tint), toNative(destination)));
                            for (int channel = 0; channel < 4; channel++)
                            {
                                float error = Math.Abs(actual[channel] - expected[channel]);
                                maxError = Math.Max(maxError, error);
                                if (!float.IsFinite(actual[channel]) || error > 2e-6f * Math.Max(1f, Math.Abs(expected[channel])))
                                    throw new InvalidOperationException("Native icon float channel changed: " + actual[channel] + " != " + expected[channel]);
                                components++;
                            }
                            GC32 actualBytes = actual, expectedBytes = expected;
                            if (actualBytes.r != expectedBytes.r || actualBytes.g != expectedBytes.g
                                || actualBytes.b != expectedBytes.b || actualBytes.a != expectedBytes.a)
                                throw new InvalidOperationException("Native icon RGBA8 changed at pattern=" + pattern + " alpha=" + alpha
                                    + " tint=" + tint + " destination=" + destination + ": " + actualBytes + " != " + expectedBytes);
                            cases++;
                        }
            Type layers = cache.GetNestedType("Layer", BindingFlags.NonPublic);
            Type pixels = cache.GetNestedType("Pixels", BindingFlags.NonPublic);
            Type iterator = cache.GetNestedTypes(BindingFlags.NonPublic).Single(t => t.Name.StartsWith("<Bake>"));
            check(layers.GetField("tint").FieldType == typeof(NC) && pixels.GetField("colors").FieldType == typeof(NC32[]),
                "icon layers and cached source pixels own native color values");
            var fields = iterator.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            check(fields.Any(f => f.FieldType == typeof(NC[])) && !fields.Any(f => f.FieldType == typeof(GC[])),
                "multi-frame icon bake retains its accumulation buffer in native colors");
            check(true, cases + " source-over icon cases preserve all " + components + " float channels and exact RGBA8 output (maximum float error=" + maxError + ")");
            File.WriteAllText(Path.Combine(output, "native-icons.json"), JsonSerializer.Serialize(new {
                Cases = cases, FloatComponents = components, PackedComponents = cases * 4, MaximumFloatError = maxError,
                ExactRgba8 = true, Baseline = "7f5992db"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
