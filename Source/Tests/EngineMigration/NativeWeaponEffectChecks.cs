using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Helodrace.ModernWar;
using Verse;
using NVector = System.Numerics.Vector3;
using NMatrix = System.Numerics.Matrix4x4;
using NColor = Waylith.Math.Color;
using NColor32 = Waylith.Math.Color32;
using GVector = UnityEngine.Vector3;
using GColor = UnityEngine.Color;
using GColor32 = UnityEngine.Color32;

namespace Helodrace.Tests
{
    internal static class NativeWeaponEffectChecks
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            Assembly product = typeof(ModularWeaponMuzzleEffectDrawer).Assembly;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type spatial = product.GetType("Helodrace.SpatialMath", true);
            var flatAngle = (Func<NVector, float>)spatial.GetMethod("FlatAngle", flags).CreateDelegate(typeof(Func<NVector, float>));
            var pinned = (Func<NVector, NVector, float, float, float, float, NMatrix>)typeof(ModularWeaponMuzzleEffectDrawer)
                .GetMethod("PinnedTransform", flags).CreateDelegate(typeof(Func<NVector, NVector, float, float, float, float, NMatrix>));
            Type boundary = product.GetType("Helodrace.GameGeometry", true);
            T ConvertMethod<T>(string name, Type input) where T : Delegate => (T)boundary.GetMethod(name, flags, null, new[] { input }, null).CreateDelegate(typeof(T));
            var matrixToGame = ConvertMethod<Func<NMatrix, UnityEngine.Matrix4x4>>("ToGame", typeof(NMatrix));
            var colorToNative = ConvertMethod<Func<GColor, NColor>>("ToNative", typeof(GColor));
            var colorToGame = ConvertMethod<Func<NColor, GColor>>("ToGame", typeof(NColor));
            var bytesToNative = ConvertMethod<Func<GColor32, NColor32>>("ToNative", typeof(GColor32));
            var bytesToGame = ConvertMethod<Func<NColor32, GColor32>>("ToGame", typeof(NColor32));
            int angles = 0, matrices = 0, colors = 0;
            void Near(float actual, float expected)
            {
                if (!float.IsFinite(actual) || !float.IsFinite(expected)
                    || Math.Abs(actual - expected) > 2e-5f * Math.Max(1f, Math.Abs(expected)))
                    throw new InvalidOperationException("Weapon effect geometry differs: " + actual + " != " + expected);
            }
            var values = new[] { -10000f, -1f, -.2f, -1e-5f, -1e-6f, -0f, 0f, 1e-6f, 1e-5f, .1f, 2f, 10000f };
            foreach (float x in values)
                foreach (float z in values)
                    foreach (float y in new[] { -1f, 0f, .5f })
                    {
                        Near(flatAngle(new NVector(x, y, z)), new GVector(x, y, z).AngleFlat());
                        angles++;
                    }
            foreach (float yaw in new[] { 0f, 20f, 160f, 200f, 340f, 360f })
                foreach (float scale in new[] { .000001f, .001f, 1f, 1000f })
                {
                    var game = UnityEngine.Quaternion.AngleAxis(yaw, GVector.up) * GVector.forward * scale;
                    float actual = flatAngle(new NVector(game.x, game.y, game.z));
                    float expected = game.AngleFlat();
                    Near(actual, expected);
                    if ((actual > 200f && actual < 340f) != (expected > 200f && expected < 340f))
                        throw new InvalidOperationException("Direction conversion changed a west-facing threshold.");
                    angles++;
                }
            foreach (var muzzle in new[] { NVector.Zero, new NVector(30, .2f, 40), new NVector(-2, -1, 3) })
                foreach (var forward in new[] { NVector.UnitX, -NVector.UnitX, new NVector(.6f, 0, .8f) })
                    foreach (float size in new[] { 0f, .1f, .5f, 2f, -1f })
                        foreach (float drift in new[] { -.2f, 0f, .5f })
                            foreach (float yaw in new[] { -720f, 0f, 20f, 200f, 340f, 360f })
                                foreach (float altitude in new[] { -.1f, 3.2f })
                                {
                                    // Frozen pre-conversion pinned muzzle formula from 59bb4564.
                                    GVector center = new GVector(muzzle.X, muzzle.Y, muzzle.Z)
                                        + new GVector(forward.X, forward.Y, forward.Z) * (size * .3125f + drift);
                                    center.y = altitude;
                                    var expected = UnityEngine.Matrix4x4.TRS(center,
                                        UnityEngine.Quaternion.AngleAxis(yaw, GVector.up), new GVector(size, 1f, size));
                                    var actual = matrixToGame(pinned(muzzle, forward, yaw, altitude, size, drift));
                                    for (int row = 0; row < 4; row++)
                                        for (int col = 0; col < 4; col++) { Near(actual[row, col], expected[row, col]); matrices++; }
                                }
            var colorValues = new[] { -10f, -1f, -0f, 0f, .00001f, .5f, 1f, 100f, float.NaN, float.PositiveInfinity };
            var gameColors = colorValues.Select(v => new GColor(v, .25f, .5f, v)).ToArray();
            var nativeColors = gameColors.Select(colorToNative).ToArray();
            void SameBits(float actual, float expected)
            {
                if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected))
                    throw new InvalidOperationException("Color boundary changed component bits.");
                colors++;
            }
            for (int i = 0; i < gameColors.Length; i++)
            {
                var back = colorToGame(nativeColors[i]);
                SameBits(back.r, gameColors[i].r); SameBits(back.g, gameColors[i].g);
                SameBits(back.b, gameColors[i].b); SameBits(back.a, gameColors[i].a);
                for (int j = 0; j < gameColors.Length; j++)
                    if (nativeColors[i].Equals(nativeColors[j]) != gameColors[i].Equals(gameColors[j]))
                        throw new InvalidOperationException("Native/game color dictionary equality differs.");
            }
            for (int alpha = 0; alpha < 256; alpha++)
            {
                var original = new GColor32((byte)alpha, (byte)(255 - alpha), 128, (byte)alpha);
                var back = bytesToGame(bytesToNative(original));
                if (back.r != original.r || back.g != original.g || back.b != original.b || back.a != original.a)
                    throw new InvalidOperationException("Color32 boundary changed a byte.");
                colors += 4;
            }
            Type effect = typeof(ModularWeaponMuzzleEffectDrawer).GetNestedType("ActiveEffect", BindingFlags.NonPublic);
            check(effect.GetField("muzzle").FieldType == typeof(NVector) && effect.GetField("forward").FieldType == typeof(NVector),
                "active muzzle effects retain only native positions and directions");
            check(true, angles + " actual native/game flat angles match, including tiny vectors and facing boundaries");
            check(true, matrices + " pinned muzzle matrix components match the frozen game calculation");
            check(true, colors + " color boundary components and dictionary equality cases preserve game behavior");
            File.WriteAllText(Path.Combine(output, "native-weapon-effects.json"), JsonSerializer.Serialize(new {
                FlatAngles = angles, MuzzleMatrixComponents = matrices, ColorComponents = colors, Baseline = "59bb4564"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
