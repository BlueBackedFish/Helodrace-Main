using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Helodrace.ModernWar;
using Verse;
using NV = System.Numerics.Vector3;
using NV2 = System.Numerics.Vector2;
using NM = System.Numerics.Matrix4x4;
using GV = UnityEngine.Vector3;
using GM = UnityEngine.Matrix4x4;
using GQ = UnityEngine.Quaternion;
using M = UnityEngine.Mathf;

namespace Helodrace.Tests
{
    internal static class NativeLightChecks
    {
        private delegate bool Laser(NV origin, NV target, NV weaponDirection, bool align, float maximum, out NV end, out bool clipped);

        internal static void Run(string output, Action<bool, string> check)
        {
            Assembly product = typeof(ModularWeaponLaserRenderer).Assembly;
            const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
            Type geometry = product.GetType("Helodrace.ModernWar.ModularLightGeometry", true);
            var laser = (Laser)geometry.GetMethod("TryLaserBeam", methods).CreateDelegate(typeof(Laser));
            var origin = (Func<ModularTransform2D, NV, NV, float, bool, float, NV>)geometry.GetMethod("EmitterOrigin", methods)
                .CreateDelegate(typeof(Func<ModularTransform2D, NV, NV, float, bool, float, NV>));
            MethodInfo flashlight = geometry.GetMethod("TryFlashlightBeam", methods);
            var cone = (Func<int, int, (NV[], NV2[], int[])>)typeof(ModularWeaponFlashlightRenderer).GetMethod("BuildConeGeometry", methods)
                .CreateDelegate(typeof(Func<int, int, (NV[], NV2[], int[])>));
            var toGame = (Func<NM, GM>)product.GetType("Helodrace.GameGeometry", true).GetMethod("ToGame", methods, null, new[] { typeof(NM) }, null)
                .CreateDelegate(typeof(Func<NM, GM>));
            int scalarComponents = 0, lasers = 0, flashlights = 0, emitters = 0, coneComponents = 0;
            void Near(float actual, float expected)
            {
                scalarComponents++;
                if (!float.IsFinite(actual) || !float.IsFinite(expected)
                    || Math.Abs(actual - expected) > 2e-5f * Math.Max(1f, Math.Abs(expected)))
                    throw new InvalidOperationException("Native light differs: " + actual + " != " + expected);
            }
            GV Game(NV value) => new GV(value.X, value.Y, value.Z);
            void Vector(NV actual, GV expected) { Near(actual.X, expected.x); Near(actual.Y, expected.y); Near(actual.Z, expected.z); }
            void Matrix(NM actual, GM expected)
            {
                GM game = toGame(actual);
                for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) Near(game[row, col], expected[row, col]);
            }
            var positions = new[] { NV.Zero, new NV(20, 3, 30), new NV(-4, -.5f, 2) };
            var deltas = new[] { NV.Zero, new NV(.000001f, 0, 0), new NV(.01f, 0, 0), new NV(0, 0, .05f),
                new NV(3, 2, 4), new NV(-2, -.5f, 7) };
            var directions = new[] { NV.Zero, NV.UnitX, NV.UnitZ, new NV(.6f, 0, .8f), new NV(2, 0, 0), new NV(.5f, 0, .5f) };
            foreach (NV start in positions)
                foreach (NV delta in deltas)
                    foreach (NV direction in directions)
                        foreach (bool align in new[] { false, true })
                            foreach (float max in new[] { -1f, 0f, .009999f, .01f, .010001f, .05f, .5f, 5f, 10f })
                            {
                                GV expected = Game(start + delta);
                                expected.y = start.Y;
                                GV d = expected - Game(start); d.y = 0;
                                bool valid = d.sqrMagnitude >= .0001f, clip = false;
                                if (valid)
                                {
                                    if (align && Game(direction).sqrMagnitude > .5f)
                                    { d = Game(direction).normalized * d.magnitude; expected = Game(start) + d; expected.y = start.Y; }
                                    clip = d.magnitude > max;
                                    if (clip) expected = Game(start) + d.normalized * max;
                                }
                                bool actual = laser(start, start + delta, direction, align, max, out NV end, out bool clipped);
                                if (actual != valid || (valid && clipped != clip)) throw new InvalidOperationException("Laser validity or clipping changed.");
                                if (actual) Vector(end, expected);
                                lasers++;
                            }
            foreach (var props in new[] {
                new ModularWeaponFlashlightProperties(),
                new ModularWeaponFlashlightProperties { maxRange = .05f, nearWidth = 2, farWidth = .01f, closeRange = 0, lengthOvershoot = -1 },
                new ModularWeaponFlashlightProperties { maxRange = 10, nearWidth = .2f, farWidth = 2, closeRange = 4, closeRangeMinScale = .3f, circleRadius = .8f, lengthOvershoot = 2 }
            })
                foreach (NV start in positions)
                    foreach (NV delta in deltas.Concat(new[] { new NV(0, 0, props.maxRange), new NV(0, 0, MathF.BitIncrement(props.maxRange)) }))
                        foreach (NV direction in directions)
                            foreach (bool align in new[] { false, true })
                            {
                                GV d = Game(start + delta) - Game(start); d.y = 0;
                                float length = d.magnitude;
                                bool valid = length >= .05f && length <= props.maxRange;
                                object[] args = { start, start + delta, direction, align, props, null };
                                bool actual = (bool)flashlight.Invoke(null, args);
                                if (actual != valid) throw new InvalidOperationException("Flashlight distance validity changed.");
                                if (actual)
                                {
                                    // Frozen 6d92def9 geometry before native extraction.
                                    GV target = Game(start + delta);
                                    if (align && Game(direction).sqrMagnitude > .5f)
                                    { d = Game(direction) * length; target = Game(start) + d; target.y = start.Y; }
                                    float far = props.farWidth, radius = props.circleRadius;
                                    if (props.closeRange > .01f && length < props.closeRange)
                                    { float scale = M.Max(props.closeRangeMinScale, length / props.closeRange); far *= scale; radius *= scale; }
                                    object beam = args[5];
                                    object Field(string name) => beam.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(beam);
                                    Near((float)Field("NearWidth"), M.Min(props.nearWidth, far));
                                    Near((float)Field("FarWidth"), far); Near((float)Field("PoolRadius"), radius);
                                    Matrix((NM)Field("ConeTransform"), GM.TRS(Game(start), GQ.AngleAxis(d.AngleFlat(), GV.up),
                                        new GV(1, 1, M.Max(.05f, length + props.lengthOvershoot))));
                                    target.y = start.Y + .001f;
                                    Matrix((NM)Field("PoolTransform"), GM.TRS(target, GQ.identity, new GV(radius * 2, 1, radius * 2)));
                                }
                                flashlights++;
                            }
            foreach (float angle in new[] { -90f, 0f, 35f, 200f })
                foreach (bool flip in new[] { false, true })
                    foreach (NV start in positions)
                    {
                        var transform = new ModularTransform2D { position = new NV2(.5f, -.2f), angle = 17, scale = new NV2(-2, .5f) };
                        var point = new NV(.3f, 2, -.1f);
                        NV2 local = transform.TransformPoint(point); if (flip) local.X = -local.X;
                        NV2 world = ModularTransform2D.RotateForWorldYaw(local, angle);
                        GV expected = Game(start) + new GV(world.X, 0, world.Y); expected.y = 2.5f;
                        Vector(origin(transform, point, start, angle, flip, 2.5f), expected);
                        emitters++;
                    }
            foreach (int near in new[] { 1, 15, 100 })
                foreach (int far in new[] { 1, 110, 250 })
                {
                    var (vertices, uvs, triangles) = cone(near, far);
                    if (vertices.Length != 33 || uvs.Length != 33 || triangles.Length != 120) throw new InvalidOperationException("Cone topology size changed.");
                    for (int row = 0; row < 11; row++)
                    {
                        float progress = row / 10f, half = M.Lerp(near / 100f * .5f, far / 100f * .5f, progress);
                        for (int column = 0; column < 3; column++)
                        {
                            Vector(vertices[row * 3 + column], new GV((column - 1) * half, 0, progress));
                            Near(uvs[row * 3 + column].X, column * .5f); Near(uvs[row * 3 + column].Y, progress);
                            coneComponents += 5;
                        }
                        if (row == 10) continue;
                        int a = row * 3, b = a + 3;
                        int[] quadIndices = { a, b, b + 1, a, b + 1, a + 1, a + 1, b + 1, b + 2, a + 1, b + 2, a + 2 };
                        if (!triangles.Skip(row * 12).Take(12).SequenceEqual(quadIndices)) throw new InvalidOperationException("Cone winding or UV layout changed.");
                    }
                }
            Type beamType = product.GetType("Helodrace.ModernWar.ModularFlashlightBeam", true);
            check(beamType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).All(f => f.FieldType.Namespace != "UnityEngine"),
                "derived flashlight beam data contains only native geometry");
            check(typeof(ModularWeaponLaserRenderer).GetField("materials", methods).FieldType.GetGenericArguments()[0] == typeof(Waylith.Math.Color),
                "laser material cache uses native color keys");
            // Exercise the still-existing game upload boundaries with native CPU data.
            var meshFactory = typeof(ModularWeaponFlashlightRenderer).GetMethod("ConeMesh", methods);
            var uploaded = (UnityEngine.Mesh)meshFactory.Invoke(null, new object[] { .15f, 1.1f });
            var expectedUpload = cone(15, 110);
            var uploadedVertices = uploaded.vertices;
            var uploadedUvs = uploaded.uv;
            check(uploaded.EngineMesh != null && uploadedVertices.Length == 33 && uploadedUvs.Length == 33,
                "native cone CPU buffers cross the current game mesh upload boundary");
            for (int i = 0; i < uploadedVertices.Length; i++)
            {
                Vector(expectedUpload.Item1[i], uploadedVertices[i]);
                Near(uploadedUvs[i].x, expectedUpload.Item2[i].X); Near(uploadedUvs[i].y, expectedUpload.Item2[i].Y);
            }
            var materialFactory = typeof(ModularWeaponFlashlightRenderer).GetMethod("LightMaterial", methods);
            foreach (bool pool in new[] { false, true })
            {
                object[] args = { null, new Waylith.Math.Color(1, .95f, .75f, .16f), pool };
                var material = (UnityEngine.Material)materialFactory.Invoke(null, args);
                var texture = material.mainTexture as UnityEngine.Texture2D;
                check(texture?.CoreTexture != null && texture.width == (pool ? 64 : 32) && texture.height == 64,
                    "native Color32 " + (pool ? "pool" : "cone") + " buffer crosses the current game texture upload boundary");
                check(ReferenceEquals(material, materialFactory.Invoke(null, args)), "native color preserves the procedural material cache hit");
            }
            check(true, lasers + " laser and " + flashlights + " flashlight cases preserve range, clipping, alignment, close scaling and matrices");
            check(true, emitters + " emitter transforms and " + coneComponents + " cone vertex/UV components preserve mirrored layout and winding");
            File.WriteAllText(Path.Combine(output, "native-lights.json"), JsonSerializer.Serialize(new {
                LaserCases = lasers, FlashlightCases = flashlights, EmitterCases = emitters, ScalarComponents = scalarComponents,
                ConeComponents = coneComponents, Baseline = "6d92def9"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
