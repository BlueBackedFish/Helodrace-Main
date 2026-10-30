using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using NativeVector = System.Numerics.Vector2;
using NativeRect = Waylith.Math.Rect;
using GameVector = UnityEngine.Vector2;
using GameRect = UnityEngine.Rect;

namespace Helodrace.Tests
{
    internal static class NativeGeometryChecks
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            Assembly product = typeof(MapComponent_OilFields).Assembly;
            Type boundary = product.GetType("Helodrace.GameGeometry", true);
            const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo Method(string name, Type parameter) => boundary.GetMethod(name, methods, null, new[] { parameter }, null);
            var toNativeVector = (Func<GameVector, NativeVector>)Method("ToNative", typeof(GameVector)).CreateDelegate(typeof(Func<GameVector, NativeVector>));
            var toGameVector = (Func<NativeVector, GameVector>)Method("ToGame", typeof(NativeVector)).CreateDelegate(typeof(Func<NativeVector, GameVector>));
            var toNativeRect = (Func<GameRect, NativeRect>)Method("ToNative", typeof(GameRect)).CreateDelegate(typeof(Func<GameRect, NativeRect>));
            var toGameRect = (Func<NativeRect, GameRect>)Method("ToGame", typeof(NativeRect)).CreateDelegate(typeof(Func<NativeRect, GameRect>));
            int cases = 0;
            void Same(float actual, float expected)
            {
                cases++;
                if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected))
                    throw new InvalidOperationException("Geometry boundary changed a component: " + actual + " != " + expected);
            }
            float[] values = { -1000000f, -720.25f, -1f, -0f, 0f, 1e-9f, .5f, 1f, 1000000f };
            foreach (float x in values)
                foreach (float y in values)
                {
                    var original = new GameVector(x, y);
                    NativeVector native = toNativeVector(original);
                    GameVector roundtrip = toGameVector(native);
                    Same(native.X, x); Same(native.Y, y);
                    Same(roundtrip.x, x); Same(roundtrip.y, y);
                    foreach (float width in new[] { -10f, 0f, 1f, 300f })
                        foreach (float height in new[] { -10f, 0f, 1f, 300f })
                        {
                            var rect = new GameRect(x, y, width, height);
                            NativeRect nativeRect = toNativeRect(rect);
                            GameRect back = toGameRect(nativeRect);
                            Same(nativeRect.x, x); Same(nativeRect.y, y);
                            Same(nativeRect.width, width); Same(nativeRect.height, height);
                            Same(back.x, x); Same(back.y, y);
                            Same(back.width, width); Same(back.height, height);
                            Same(nativeRect.center.X, rect.center.x);
                            Same(nativeRect.center.Y, rect.center.y);
                        }
                }
            check(true, cases + " actual game/native vector and rectangle components match, including signed zero and negative sizes");
            Type camera = product.GetType("Helodrace.ModernWar.ModularWeaponPreviewCamera", true);
            Type drag = product.GetType("Helodrace.ModernWar.ModularWeaponPreviewDrag", true);
            check(camera.GetProperty("Pan").PropertyType == typeof(NativeVector)
                && camera.GetMethod("OriginFor").ReturnType == typeof(NativeVector)
                && camera.GetMethod("BoundsAtFit").ReturnType == typeof(NativeRect)
                && drag.GetMethod("Move").ReturnType == typeof(NativeVector), "compiled preview camera and drag expose native geometry internally");
            File.WriteAllText(Path.Combine(output, "native-geometry.json"), JsonSerializer.Serialize(new { BoundaryComponents = cases, NativePreviewGeometry = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
