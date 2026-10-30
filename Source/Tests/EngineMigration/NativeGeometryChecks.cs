using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NativeVector = System.Numerics.Vector2;
using NativeRect = Waylith.Math.Rect;
using GameVector = UnityEngine.Vector2;
using GameRect = UnityEngine.Rect;
using Helodrace.ModernWar;

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
            int transforms = CompareTransforms();
            check(true, transforms + " transform components match the frozen game-vector calculation, including mirrored/nonuniform/near-zero scales and nested attachments");
            check(typeof(ModularTransform2D).GetFields().All(f => f.FieldType.Namespace != "UnityEngine")
                && typeof(ModularRenderNode).GetField("railStart").FieldType == typeof(NativeVector)
                && typeof(ModularRenderNode).GetProperty("GraphicCenter").PropertyType == typeof(NativeVector), "owned attachment transforms and render node coordinates use native vectors");
            File.WriteAllText(Path.Combine(output, "native-geometry.json"), JsonSerializer.Serialize(new { BoundaryComponents = cases, TransformComponents = transforms, NativePreviewGeometry = true }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static int CompareTransforms()
        {
            int count = 0;
            void Near(float actual, float expected)
            {
                count++;
                if (!float.IsFinite(actual) || !float.IsFinite(expected)
                    || Math.Abs(actual - expected) > 2e-5f * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected))))
                    throw new InvalidOperationException("Attachment geometry: " + actual + " != " + expected);
            }
            void Vector(NativeVector native, GameVector game) { Near(native.X, game.x); Near(native.Y, game.y); }
            void Transform(ModularTransform2D native, LegacyTransform2D game)
            {
                Vector(native.position, game.position); Vector(native.scale, game.scale);
                Near(native.angle, game.angle); Near(native.layer, game.layer);
            }
            float[] angles = { -720f, -180f, -90f, -.01f, 0f, 45f, 90f, 180f, 720f };
            GameVector[] scales = { new GameVector(1, 1), new GameVector(2, .25f), new GameVector(-2, .5f), new GameVector(-1, -1), new GameVector(0, 0), new GameVector(1e-6f, -1e-6f) };
            foreach (float angle in angles)
                foreach (GameVector scale in scales)
                    foreach (GameVector mountScale in scales)
                    {
                        var native = new ModularTransform2D { position = new NativeVector(.3f, -.7f), angle = angle, scale = new NativeVector(scale.x, scale.y), layer = -.4f };
                        var game = new LegacyTransform2D { position = new GameVector(.3f, -.7f), angle = angle, scale = scale, layer = -.4f };
                        var socket = new ModularAttachmentTransform { position = new UnityEngine.Vector3(.2f, 3, -.15f), angle = 37f, scale = scale, layer = .2f };
                        var mount = new ModularAttachmentTransform { position = new UnityEngine.Vector3(-.1f, -7, .08f), angle = -19f, scale = mountScale, layer = -.1f };
                        foreach (float ratio in new[] { .25f, 1f, 2f })
                        {
                            var childNative = native.Attach(socket.ToNativePose(), mount.ToNativePose(), 12f, ratio);
                            var childGame = game.Attach(socket, mount, 12f, ratio);
                            Transform(childNative, childGame);
                            var nestedNative = childNative.Attach(mount.ToNativePose(), socket.ToNativePose(), -5f, .5f);
                            var nestedGame = childGame.Attach(mount, socket, -5f, .5f);
                            Transform(nestedNative, nestedGame);
                            foreach (var point in new[] { UnityEngine.Vector3.zero, new UnityEngine.Vector3(.7f, 8f, -.2f), new UnityEngine.Vector3(-3f, 0, 2f) })
                            {
                                var result = nestedNative.TransformPoint(new System.Numerics.Vector3(point.x, point.y, point.z));
                                var expected = nestedGame.TransformPoint(point);
                                Vector(result, expected);
                                Vector(nestedNative.InverseTransformPoint(result), nestedGame.InverseTransformPoint(expected));
                            }
                        }
                        foreach (var value in new[] { new GameVector(0, 0), new GameVector(.5f, -.8f), new GameVector(-2, 3) })
                        {
                            var yaw = UnityEngine.Quaternion.AngleAxis(angle, UnityEngine.Vector3.up) * new UnityEngine.Vector3(value.x, 0f, value.y);
                            Vector(ModularTransform2D.RotateForWorldYaw(new NativeVector(value.x, value.y), angle), new GameVector(yaw.x, yaw.z));
                        }
                    }
            return count;
        }
    }
}
