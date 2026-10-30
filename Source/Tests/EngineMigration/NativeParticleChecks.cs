using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Helodrace.ModernWar;
using Verse;
using GV = UnityEngine.Vector3;
using GC = UnityEngine.Color;

namespace Helodrace.Tests
{
    internal static class NativeParticleChecks
    {
        [ThreadStatic] private static bool captureEnabled;
        [ThreadStatic] private static FleckCreationData? captured;

        private static bool Capture(FleckCreationData __0)
        {
            if (!captureEnabled) return true;
            captured = __0;
            return false;
        }

        internal static void Run(string output, Action<bool, string> check)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var clone = typeof(object).GetMethod("MemberwiseClone", fields);
            var factories = new (Func<IFleck> Native, Func<IFleck> Frozen, FleckDef Definition)[] {
                (() => new FleckFallingSpark(), () => new Frozen.FleckFallingSpark(),
                    DefDatabase<FleckDef>.AllDefsListForReading.First(d => d.fleckSystemClass == typeof(FleckSystemFallingSpark))),
                (() => new FleckWhitePhosphorusParticle(), () => new Frozen.FleckWhitePhosphorusParticle(), DefDatabase<FleckDef>.GetNamed("HD_WhitePhosphorusSpark")),
                (() => new FleckWhitePhosphorusParticle(), () => new Frozen.FleckWhitePhosphorusParticle(), DefDatabase<FleckDef>.GetNamed("HD_WhitePhosphorusEmber")),
                (() => new FleckEaseOutSmoke(), () => new Frozen.FleckEaseOutSmoke(), DefDatabase<FleckDef>.GetNamed("HD_DirectionalVanillaSmoke"))
            };
            int cases = 0, intervals = 0, components = 0, expirations = 0, emissions = 0;
            float maximumError = 0;
            void Same(float actual, float expected)
            {
                components++;
                maximumError = Math.Max(maximumError, Math.Abs(actual - expected));
                if (!float.IsFinite(actual) || !float.IsFinite(expected)
                    || Math.Abs(actual - expected) > 2e-5f * Math.Max(1f, Math.Abs(expected)))
                    throw new InvalidOperationException("Particle component differs: " + actual + " != " + expected);
            }
            void Vector(GV actual, GV expected) { Same(actual.x, expected.x); Same(actual.y, expected.y); Same(actual.z, expected.z); }
            var velocities = new GV?[] { null, GV.zero, new GV(.000001f, 0, 0), new GV(.009999f, 0, 0), new GV(.01f, 0, 0),
                new GV(.010001f, 0, 0), new GV(-2, 3, 4), new GV(4, -2, -3), new GV(0, 10, 0) };
            (float Speed, float Orbit, float? Air)[] parameters = { (-.5f, -.1f, null), (.5f, .04f, 0), (2f, .2f, 1.3f) };
            foreach (var factory in factories)
                foreach (GV? velocity in velocities)
                    foreach (GV acceleration in new[] { GV.zero, new GV(0, 12, -12), new GV(.5f, -100, .25f) })
                        foreach (var settings in parameters)
                            foreach (GV position in new[] { new GV(50, .2f, 40), new GV(10, 0, 5) })
                                for (int profile = 0; profile < 3; profile++)
                                {
                                    IFleck actual = factory.Native(), expected = factory.Frozen();
                                    var definition = (FleckDef)clone.Invoke(factory.Definition, null);
                                    definition.acceleration = acceleration;
                                    FleckCreationData data = FleckMaker.GetDataStatic(position, Find.CurrentMap, definition, .7f);
                                    data.velocity = velocity; data.velocitySpeed = settings.Speed; data.orbitSpeed = settings.Orbit;
                                    data.airTimeLeft = settings.Air; data.rotation = 37f;
                                    if (profile == 2) data.ageTicksOverride = Find.TickManager.TicksGame - 10;
                                    int seed = 170703 + cases;
                                    float actualRandom, expectedRandom;
                                    Rand.PushState(seed);
                                    try { actual.Setup(data); actualRandom = Rand.Value; }
                                    finally { Rand.PopState(); }
                                    Rand.PushState(seed);
                                    try { expected.Setup(data); expectedRandom = Rand.Value; }
                                    finally { Rand.PopState(); }
                                    if (actualRandom != expectedRandom) throw new InvalidOperationException("Particle setup changed RNG consumption.");
                                    FieldInfo actualBase = actual.GetType().GetField("baseData", fields);
                                    FieldInfo expectedBase = expected.GetType().GetField("baseData", fields);
                                    void Compare()
                                    {
                                        Vector(actual.GetPosition(), expected.GetPosition());
                                        var a = (FleckStatic)actualBase.GetValue(actual);
                                        var e = (FleckStatic)expectedBase.GetValue(expected);
                                        Same(a.exactRotation, e.exactRotation); Same(a.ageSecs, e.ageSecs);
                                        if (a.ageTicks != e.ageTicks) throw new InvalidOperationException("Particle lifetime tick count changed.");
                                        Vector(a.ExactScale, e.ExactScale); Same(a.Alpha, e.Alpha);
                                    }
                                    Compare();
                                    for (int step = 0; step < 240; step++)
                                    {
                                        // FleckStatic.Draw writes its game-owned altitude. Exercise that
                                        // boundary independently of GPU submission while motion is active.
                                        if (step % 8 == 0)
                                            foreach (var pair in new[] { (actual, actualBase), (expected, expectedBase) })
                                            {
                                                var value = (FleckStatic)pair.Item2.GetValue(pair.Item1);
                                                value.position.worldPosition.y = 1.5f + step * .001f;
                                                pair.Item2.SetValue(pair.Item1, value);
                                            }
                                        float dt = profile == 0 ? 1f / 60f : profile == 1 ? .05f
                                            : new[] { 0f, 1f / 240f, 1f / 60f, .05f, .2f }[step % 5];
                                        bool done = actual.TimeInterval(dt, Find.CurrentMap);
                                        if (done != expected.TimeInterval(dt, Find.CurrentMap)) throw new InvalidOperationException("Particle expiry changed.");
                                        Compare(); intervals++;
                                        if (done) { expirations++; break; }
                                    }
                                    cases++;
                                }
            foreach (Type type in new[] { typeof(FleckFallingSpark), typeof(FleckWhitePhosphorusParticle), typeof(FleckEaseOutSmoke) })
                check(!type.GetFields(fields).Any(f => f.FieldType == typeof(GV)), type.Name + " owns native motion vectors; FleckStatic and IFleck remain game contracts");
            check(true, cases + " particle setups and " + intervals + " intervals match frozen motion, rotation, scale, alpha, expiry and setup RNG");

            var harmony = new Harmony("Helodrace.Tests.NativeParticleEmitter");
            harmony.Patch(AccessTools.Method(typeof(FleckManager), nameof(FleckManager.CreateFleck), new[] { typeof(FleckCreationData) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeParticleChecks), nameof(Capture))));
            var emit = typeof(HelodGasWorker_WhitePhosphorus).GetMethod("SpawnDriftingFleck", BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                foreach (FleckDef definition in new[] { HelodGasFleckDefOf.HD_WhitePhosphorusSpark, HelodGasFleckDefOf.HD_WhitePhosphorusEmber, null })
                    foreach (float scale in new[] { .34f, 1.05f })
                        foreach (GC color in new[] { new GC(1.2f, .42f, .08f, 1), new GC(2.05f, 1.18f, .42f, .3f) })
                            for (int seed = 0; seed < 32; seed++)
                            {
                                var cell = new IntVec3(50, 0, 40);
                                captured = null;
                                float nativeRandom, frozenRandom;
                                Rand.PushState(seed);
                                try
                                {
                                    captureEnabled = true;
                                    emit.Invoke(null, new object[] { cell, Find.CurrentMap, definition, scale,
                                        new Waylith.Math.Color(color.r, color.g, color.b, color.a) });
                                    nativeRandom = Rand.Value;
                                }
                                finally { captureEnabled = false; Rand.PopState(); }
                                FleckCreationData? original;
                                Rand.PushState(seed);
                                try { original = Frozen.WhitePhosphorusEmitter.SpawnDriftingFleck(cell, Find.CurrentMap, definition, scale, color); frozenRandom = Rand.Value; }
                                finally { Rand.PopState(); }
                                if (nativeRandom != frozenRandom || captured.HasValue != original.HasValue)
                                    throw new InvalidOperationException("White phosphorus emission changed RNG order or null-def handling.");
                                if (original.HasValue)
                                {
                                    var a = captured.Value; var e = original.Value;
                                    Vector(a.spawnPosition, e.spawnPosition); Vector(a.velocity.Value, e.velocity.Value);
                                    Same(a.scale, e.scale); Same(a.rotation, e.rotation); Same(a.rotationRate, e.rotationRate);
                                    Same(a.velocitySpeed, e.velocitySpeed); Same(a.orbitSpeed, e.orbitSpeed); Same(a.airTimeLeft.Value, e.airTimeLeft.Value);
                                    for (int channel = 0; channel < 4; channel++) Same(a.instanceColor.Value[channel], e.instanceColor.Value[channel]);
                                }
                                emissions++;
                            }
            }
            finally { captureEnabled = false; captured = null; harmony.UnpatchAll(harmony.Id); }
            check(true, emissions + " actual white phosphorus emission calls preserve creation data, HDR color and RNG order");
            File.WriteAllText(Path.Combine(output, "native-particles.json"), JsonSerializer.Serialize(new {
                Cases = cases, Intervals = intervals, Expirations = expirations, EmissionCases = emissions,
                Components = components, MaximumFloatError = maximumError, Baseline = "85bcbfe4"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
