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
            int matrices = CompareMatrices(product, boundary);
            check(true, matrices + " matrix/point/direction components match actual game TRS and row/column boundaries, including composition and negative scales");
            NativeAimingChecks.Run(output, check);
            File.WriteAllText(Path.Combine(output, "native-geometry.json"), JsonSerializer.Serialize(new { BoundaryComponents = cases, TransformComponents = transforms, MatrixComponents = matrices, NativePreviewGeometry = true }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static int CompareMatrices(Assembly product, Type boundary)
        {
            var create = (Func<System.Numerics.Vector3, System.Numerics.Quaternion, System.Numerics.Vector3, System.Numerics.Matrix4x4>)
                product.GetType("Helodrace.SpatialMath", true).GetMethod("CreateTransform", BindingFlags.Static | BindingFlags.NonPublic)
                    .CreateDelegate(typeof(Func<System.Numerics.Vector3, System.Numerics.Quaternion, System.Numerics.Vector3, System.Numerics.Matrix4x4>));
            var toGame = (Func<System.Numerics.Matrix4x4, UnityEngine.Matrix4x4>)boundary
                .GetMethod("ToGame", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(System.Numerics.Matrix4x4) }, null)
                .CreateDelegate(typeof(Func<System.Numerics.Matrix4x4, UnityEngine.Matrix4x4>));
            int count = 0;
            void Near(float actual, float expected)
            {
                count++;
                if (!float.IsFinite(actual) || !float.IsFinite(expected)
                    || Math.Abs(actual - expected) > 2e-5f * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected))))
                    throw new InvalidOperationException("Spatial transform: " + actual + " != " + expected);
            }
            void Vector(System.Numerics.Vector3 native, UnityEngine.Vector3 game)
            { Near(native.X, game.x); Near(native.Y, game.y); Near(native.Z, game.z); }
            var points = new[] { new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(1, 0, 0), new System.Numerics.Vector3(0, 1, 0), new System.Numerics.Vector3(0, 0, 1), new System.Numerics.Vector3(-2, .7f, 3) };
            var scales = new[] { System.Numerics.Vector3.One, new System.Numerics.Vector3(-1, 2, .25f), new System.Numerics.Vector3(-1, -2, -3), new System.Numerics.Vector3(2, 0, .5f), System.Numerics.Vector3.Zero };
            var rotations = new[] { System.Numerics.Quaternion.Identity, System.Numerics.Quaternion.Zero,
                new System.Numerics.Quaternion(1e-8f, 0, 0, 0), new System.Numerics.Quaternion(.4f, -.7f, .2f, 2f),
                System.Numerics.Quaternion.CreateFromYawPitchRoll(.9f, -.4f, .7f) };
            foreach (var position in new[] { System.Numerics.Vector3.Zero, new System.Numerics.Vector3(-10, .003f, 7) })
                foreach (var scale in scales)
                    foreach (var rotation in rotations)
                    {
                        var native = create(position, rotation, scale);
                        var game = UnityEngine.Matrix4x4.TRS(new UnityEngine.Vector3(position.X, position.Y, position.Z),
                            new UnityEngine.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), new UnityEngine.Vector3(scale.X, scale.Y, scale.Z));
                        var converted = toGame(native);
                        // Every element is checked through basis points and homogeneous multiplication.
                        var homogeneous = new System.Numerics.Vector4(.2f, -.5f, .7f, .3f);
                        var native4 = System.Numerics.Vector4.Transform(homogeneous, native);
                        var game4 = converted * new UnityEngine.Vector4(homogeneous.X, homogeneous.Y, homogeneous.Z, homogeneous.W);
                        Near(native4.X, game4.x); Near(native4.Y, game4.y); Near(native4.Z, game4.z); Near(native4.W, game4.w);
                        foreach (var point in points)
                        {
                            var input = new UnityEngine.Vector3(point.X, point.Y, point.Z);
                            var transformed = System.Numerics.Vector3.Transform(point, native);
                            Vector(transformed, game.MultiplyPoint3x4(input));
                            Vector(transformed, converted.MultiplyPoint3x4(input));
                            Vector(System.Numerics.Vector3.TransformNormal(point, native), game.MultiplyVector(input));
                        }
                        var childPosition = new System.Numerics.Vector3(.3f, 2, -.8f);
                        var childRotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, .7f);
                        var child = create(childPosition, childRotation, new System.Numerics.Vector3(.4f, -.7f, 1.3f));
                        var gameChild = UnityEngine.Matrix4x4.TRS(new UnityEngine.Vector3(.3f, 2, -.8f),
                            new UnityEngine.Quaternion(childRotation.X, childRotation.Y, childRotation.Z, childRotation.W), new UnityEngine.Vector3(.4f, -.7f, 1.3f));
                        foreach (var point in points)
                            Vector(System.Numerics.Vector3.Transform(point, child * native),
                                (game * gameChild).MultiplyPoint3x4(new UnityEngine.Vector3(point.X, point.Y, point.Z)));
                    }
            return count;
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
