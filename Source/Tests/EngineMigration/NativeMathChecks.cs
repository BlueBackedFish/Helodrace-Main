using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Native = Waylith.Math.Mathf;
using Game = UnityEngine.Mathf;

namespace Helodrace.Tests
{
    internal static class NativeMathChecks
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            int cases = 0;
            void Near(float actual, float expected, string operation)
            {
                cases++;
                if (actual.Equals(expected)) return;
                if (!float.IsFinite(actual) || !float.IsFinite(expected)
                    || Math.Abs(actual - expected) > 1e-6f * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected))))
                    throw new InvalidOperationException(operation + ": " + actual + " != " + expected);
            }
            float[] values = { -1000000, -720, -360, -180, -1, -0f, 0, 1e-9f, .5f, 1, 180, 360, 720, 1000000 };
            float[] fractions = { -2, 0, .001f, .5f, .999f, 1, 2 };
            var angles = typeof(MapComponent_OilFields).Assembly.GetType("Helodrace.AngleMath", true);
            var interpolate = (Func<float, float, float, float>)angles.GetMethod("Interpolate", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(Func<float, float, float, float>));
            var move = (Func<float, float, float, float>)angles.GetMethod("MoveTowards", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(Func<float, float, float, float>));
            foreach (float value in values)
            {
                Near(Native.Abs(value), Game.Abs(value), "abs");
                Near(Native.Clamp(value, -360, 360), Game.Clamp(value, -360, 360), "clamp");
                Near(Native.Clamp01(value), Game.Clamp01(value), "clamp01");
                Near(Native.RoundToInt(value), Game.RoundToInt(value), "round");
                Near(Native.FloorToInt(value), Game.FloorToInt(value), "floor");
                Near(Native.CeilToInt(value), Game.CeilToInt(value), "ceil");
                foreach (float other in values)
                {
                    Near(Native.Min(value, other), Game.Min(value, other), "min");
                    Near(Native.Max(value, other), Game.Max(value, other), "max");
                    Near(Native.DeltaAngle(value, other), Game.DeltaAngle(value, other), "delta angle");
                    Near(Native.InverseLerp(value, other, .5f), Game.InverseLerp(value, other, .5f), "inverse lerp");
                    foreach (float fraction in fractions)
                    {
                        Near(Native.Lerp(value, other, fraction), Game.Lerp(value, other, fraction), "lerp");
                        Near(interpolate(value, other, fraction), Game.LerpAngle(value, other, fraction), "angle interpolation");
                        Near(move(value, other, fraction), Game.MoveTowardsAngle(value, other, fraction), "angle movement");
                    }
                }
            }
            check(true, cases + " scalar/angle cases match the actual game boundary, including wraps and half-turn ties");
            using var pe = new PEReader(File.OpenRead(typeof(MapComponent_OilFields).Assembly.Location));
            MetadataReader reader = pe.GetMetadataReader();
            string[] types = reader.TypeReferences.Select(h => reader.GetTypeReference(h)).Select(t => reader.GetString(t.Namespace) + "." + reader.GetString(t.Name)).ToArray();
            check(!types.Contains("UnityEngine.Mathf") && types.Contains("Waylith.Math.Mathf"), "compiled product has native scalar math references and no Unity Mathf type reference");
            File.WriteAllText(Path.Combine(output, "native-math.json"), JsonSerializer.Serialize(new { ScalarCases = cases, UnityMathfReferences = 0, NativeMathfReferences = types.Count(t => t == "Waylith.Math.Mathf") }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
