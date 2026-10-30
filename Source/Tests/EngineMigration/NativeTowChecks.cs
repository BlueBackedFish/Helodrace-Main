using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using GV = UnityEngine.Vector3;
using NV = System.Numerics.Vector3;
using GM = UnityEngine.Matrix4x4;

namespace Helodrace.Tests
{
    internal static class NativeTowChecks
    {
        internal static int PhaseIdentity;
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        [ThreadStatic] private static List<GM> matrices;
        [ThreadStatic] private static List<(GV, GV, float)> lines;
        [ThreadStatic] private static bool captureImpact;
        [ThreadStatic] private static Thing impacted;
        private static int components, angles, steering, frames, flightTicks, collisions, wireTests, retargets, hits, misses;
        private static float maximumError;

        public sealed class SavedMissile
        {
            public int Id { get; set; }
            public Dictionary<string, int[]> State { get; set; }
        }
        private static bool Mesh(GM __1) { if (matrices == null) return true; matrices.Add(__1); return false; }
        private static bool Line(GV __0, GV __1, float __3) { if (lines == null) return true; lines.Add((__0, __1, __3)); return false; }
        private static bool Impact(Projectile __instance, Thing __0)
        {
            if (!captureImpact || !(__instance is Projectile_Tow || __instance is Frozen.Projectile_Tow)) return true;
            impacted = __0; return false;
        }
        private static FieldInfo Field(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
                if (t.GetField(name, All | BindingFlags.DeclaredOnly) is FieldInfo f) return f;
            throw new MissingFieldException(name);
        }
        private static object Get(object v, string name) => Field(v.GetType(), name).GetValue(v);
        private static void Set(object v, string name, object data) => Field(v.GetType(), name).SetValue(v, data);
        private static object Call(object v, string name, params object[] args) => v.GetType().GetMethod(name, All).Invoke(v, args);
        private static T Clone<T>(T item) => (T)typeof(object).GetMethod("MemberwiseClone", All).Invoke(item, null);
        private static ThingDef Definition(ThingDef source, bool frozen)
        { var d = Clone(source); d.thingClass = frozen ? typeof(Frozen.Projectile_Tow) : typeof(Projectile_Tow); return d; }
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static GV Game(object v) => v is NV n ? new GV(n.X, n.Y, n.Z) : (GV)v;
        private static void SetVector(object v, string name, GV data) => Set(v, name, Field(v.GetType(), name).FieldType == typeof(NV) ? Native(data) : data);
        private static float[] Floats(object v) => v switch { float f => new[] { f }, NV n => new[] { n.X, n.Y, n.Z }, GV g => new[] { g.x, g.y, g.z }, _ => null };
        private static void Same(float a, float e)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(a - e));
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1, Math.Abs(e)))
                throw new InvalidOperationException("TOW component differs: " + a + " != " + e);
        }
        private static void Vector(object a, object e)
        { var x = Floats(a); var y = Floats(e); for (int i = 0; i < x.Length; i++) Same(x[i], y[i]); }
        private static void Compare(Projectile_Tow a, Frozen.Projectile_Tow e)
        {
            foreach (var f in e.GetType().GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (f.Name.EndsWith("Material") || f.Name == "ignitionSustainer") continue;
                object x = Get(a, f.Name), y = f.GetValue(e);
                if (Floats(x) != null && Floats(y) != null) Vector(x, y);
                else if (f.Name == "wirePath" && x != null && y != null)
                {
                    var ax = ((IEnumerable)x).Cast<object>().ToArray(); var ey = ((IEnumerable)y).Cast<object>().ToArray();
                    if (ax.Length != ey.Length) throw new InvalidOperationException("Wire point count changed.");
                    for (int i = 0; i < ax.Length; i++) Vector(ax[i], ey[i]);
                }
                else if (!Equals(x, y)) throw new InvalidOperationException("TOW state differs: " + f.Name + " " + x + " != " + y);
            }
            Vector(a.ExactPosition, e.ExactPosition);
            if (a.ExactPosition.ToIntVec3() != e.ExactPosition.ToIntVec3()) throw new InvalidOperationException("TOW map cell changed.");
        }
        private static void Draw(Projectile_Tow a, Frozen.Projectile_Tow e)
        {
            matrices = new List<GM>(); lines = new List<(GV, GV, float)>();
            Call(a, "DrawAt", GV.zero, false); var am = matrices.ToArray(); var al = lines.ToArray(); matrices.Clear(); lines.Clear();
            Call(e, "DrawAt", GV.zero, false);
            if (am.Length != matrices.Count || al.Length != lines.Count) throw new InvalidOperationException("TOW draw submission count changed.");
            for (int m = 0; m < am.Length; m++) for (int i = 0; i < 16; i++) Same(am[m][i], matrices[m][i]);
            for (int i = 0; i < al.Length; i++) { Vector(al[i].Item1, lines[i].Item1); Vector(al[i].Item2, lines[i].Item2); Same(al[i].Item3, lines[i].Item3); }
            matrices = null; lines = null; frames++;
        }

        internal static void Create(string output, Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.NativeTow");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(UnityEngine.Material), typeof(int) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeTowChecks), nameof(Mesh))));
            harmony.Patch(AccessTools.Method(typeof(GenDraw), "DrawLineBetween", new[] { typeof(GV), typeof(GV), typeof(UnityEngine.Material), typeof(float) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeTowChecks), nameof(Line))));
            harmony.Patch(AccessTools.Method(typeof(Projectile_Explosive), "Impact"), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeTowChecks), nameof(Impact))));
            Map map = Find.CurrentMap;
            ThingDef source = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.thingClass == typeof(Projectile_Tow));
            ThingDef launcherDef = DefDatabase<ThingDef>.GetNamed("HD_Building_M220TOW");
            var launcher = (Building_TurretGun)ThingMaker.MakeThing(launcherDef, GenStuff.DefaultStuffFor(launcherDef));
            IntVec3 start = map.Center;
            // This is the isolated quicktest map. Keep both comparison flights
            // on the same clear lane; obstruction branches are exercised below.
            foreach (IntVec3 cell in CellRect.CenteredOn(start + new IntVec3(35, 0, 0), 85, 19).ClipInsideMap(map))
                foreach (Thing t in cell.GetThingList(map).ToArray())
                    if (t is Building || t is Plant) t.Destroy();
            GenSpawn.Spawn(launcher, start, map);
            Thing target = ThingMaker.MakeThing(ThingDefOf.WoodLog); GenSpawn.Spawn(target, start + new IntVec3(75, 0, 0), map);
            try
            {
                Angles();
                PureSteering(source, target);
                ActualFlight(source, launcher, target);
                CollisionChecks(source, launcher, target);
                SaveProbes(source, launcher, output);
            }
            finally { captureImpact = false; matrices = null; lines = null; harmony.UnpatchAll(harmony.Id); if (!target.Destroyed) target.Destroy(); }
            check(true, angles + " native signed angles preserve actual game sign, zero/antiparallel and normalization behavior");
            check(true, steering + " TOW steering steps preserve launch cone, target vector smoothing, prediction, motor speed and draw transforms");
            check(true, flightTicks + " actual launched TOW ticks preserve motion, retargeting, wire points, map cells and draw callbacks");
            check(retargets == 3 && hits > 0 && misses > 0, "three actual retargets succeed; collision comparisons include both hits and misses");
            check(true, collisions + " tracked/aim-point/building collision samples and " + wireTests + " wire obstruction cases match frozen game geometry");
            check(typeof(Projectile_Tow).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic).All(f => f.FieldType != typeof(GV) && f.FieldType != typeof(List<GV>)),
                "TOW owns native position/direction/velocity and wire-point list");
            File.WriteAllText(Path.Combine(output, "native-tow.json"), JsonSerializer.Serialize(new {
                Angles = angles, SteeringSteps = steering, ActualFlightTicks = flightTicks, Retargets = retargets,
                CollisionCases = collisions, CollisionHits = hits, CollisionMisses = misses, WireCases = wireTests,
                DrawCallbacks = frames, Components = components, MaximumFloatError = maximumError, Baseline = "a70f624f"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        private static void Angles()
        {
            var signed = (Func<NV, NV, NV, float>)typeof(Projectile_Tow).Assembly.GetType("Helodrace.SpatialMath").GetMethod("SignedAngleBetween", All).CreateDelegate(typeof(Func<NV, NV, NV, float>));
            var values = new[] { GV.zero, GV.forward, -GV.forward, GV.right, -GV.right, GV.up, new GV(.000001f, 0, 0), new GV(.3f, -2, -.7f) };
            foreach (GV a in values) foreach (GV b in values) foreach (GV axis in new[] { GV.up, -GV.up, GV.zero, new GV(1, -2, 3) })
            { Same(signed(Native(a), Native(b), Native(axis)), GV.SignedAngle(a, b, axis)); angles++; }
        }
        private static void PureSteering(ThingDef source, Thing target)
        {
            foreach (TowProjectileExtension ext in new[] { null, source.GetModExtension<TowProjectileExtension>(), new TowProjectileExtension { maximumLaunchDeviationDegrees = -1, turnDegreesPerTick = 0, terminalOscillationFactor = 2, decelerationTicks = 0, coastSpeedFactor = -.2f } })
                foreach (GV direction in new[] { GV.zero, GV.forward, GV.right, new GV(.01f, 0, 0), new GV(-1, 0, .1f) })
                    foreach (int age in new[] { 0, 3, 98, 99, 220 })
                        foreach (int elapsed in new[] { 0, 1, 7 })
                        {
                            ThingDef d = Definition(source, false); d.modExtensions = ext == null ? null : new List<DefModExtension> { ext };
                            var a = (Projectile_Tow)ThingMaker.MakeThing(d); var e = (Frozen.Projectile_Tow)ThingMaker.MakeThing(Definition(d, true));
                            PhaseIdentity = a.thingIDNumber;
                            foreach (object v in new object[] { a, e })
                            { Set(v, "origin", new GV(10, .2f, 5)); Set(v, "destination", new GV(25, 0, 12)); Set(v, "ticksToImpact", 8); }
                            // Before InitializeFromBaseFlight, both game callbacks
                            // still use the base projectile's position contract.
                            Compare(a, e); Draw(a, e);
                            foreach (object v in new object[] { a, e })
                            {
                                Set(v, "flightInitialized", true); Set(v, "flightTicks", age); Set(v, "intendedTarget", new LocalTargetInfo(target));
                                SetVector(v, "exactPosition", target.DrawPos + new GV(-18, 0, 5)); SetVector(v, "flightDirection", direction); SetVector(v, "initialGuidanceDirection", direction);
                                SetVector(v, "trackedTargetVelocity", new GV(.1f, 0, -.2f)); SetVector(v, "lastTrackedTargetPosition", target.DrawPos + new GV(-.2f, 0, .4f));
                                Set(v, "targetVectorInitialized", true); Set(v, "lastTargetSampleTick", Find.TickManager.TicksGame - elapsed);
                            }
                            for (int step = 0; step < 6; step++)
                            {
                                Call(a, "ApplyContinuousSteering"); Call(e, "ApplyContinuousSteering"); Compare(a, e);
                                Same((float)a.GetType().GetProperty("CurrentSpeedPerTick", All).GetValue(a), (float)e.GetType().GetProperty("CurrentSpeedPerTick", All).GetValue(e));
                                Draw(a, e); steering++;
                            }
                        }
        }
        private static void ActualFlight(ThingDef source, Building_TurretGun launcher, Thing target)
        {
            for (int trial = 0; trial < 3; trial++)
            {
                var a = (Projectile_Tow)ThingMaker.MakeThing(source); var e = (Frozen.Projectile_Tow)ThingMaker.MakeThing(Definition(source, true));
                GenSpawn.Spawn(a, launcher.Position, launcher.Map); GenSpawn.Spawn(e, launcher.Position, launcher.Map);
                PhaseIdentity = a.thingIDNumber;
                try
                {
                    GV origin = launcher.DrawPos + new GV(.125f, 0, -.25f);
                    Rand.PushState(trial); try { a.Launch(launcher, origin, target, target, ProjectileHitFlags.None, false); } finally { Rand.PopState(); }
                    Rand.PushState(trial); try { e.Launch(launcher, origin, target, target, ProjectileHitFlags.None, false); } finally { Rand.PopState(); }
                    if (launcher.GetComp<CompTowLauncher>().ActiveMissile != a || a.LauncherPlatform != launcher || e.LauncherPlatform != launcher)
                        throw new InvalidOperationException("Current typed launcher registration or frozen launcher resolution failed.");
                    for (int tick = 0; tick < 20; tick++)
                    {
                        if (tick == 8) target.Position += new IntVec3(0, 0, trial - 1);
                        if (tick == 12)
                        {
                            LocalTargetInfo replacement = launcher.Position + new IntVec3(70, 0, 3 + trial);
                            bool av = a.TryChangeTarget(replacement), ev = e.TryChangeTarget(replacement);
                            if (!av || av != ev) throw new InvalidOperationException("TOW retarget did not succeed equally.");
                            retargets++;
                        }
                        Rand.PushState(tick); try { Call(a, "Tick"); } finally { Rand.PopState(); }
                        Rand.PushState(tick); try { Call(e, "Tick"); } finally { Rand.PopState(); }
                        if (a.Destroyed || e.Destroyed) throw new InvalidOperationException("Comparison flight ended before its planned observation.");
                        Compare(a, e); Draw(a, e); flightTicks++;
                    }
                }
                finally { if (!a.Destroyed) a.Destroy(); if (!e.Destroyed) e.Destroy(); }
            }
        }
        private static void CollisionChecks(ThingDef source, Building_TurretGun launcher, Thing target)
        {
            var a = (Projectile_Tow)ThingMaker.MakeThing(source); var e = (Frozen.Projectile_Tow)ThingMaker.MakeThing(Definition(source, true));
            GenSpawn.Spawn(a, launcher.Position, launcher.Map); GenSpawn.Spawn(e, launcher.Position, launcher.Map);
            Set(a, "launcherPlatform", launcher); Set(e, "launcherPlatform", launcher);
            Set(a, "flightInitialized", true); Set(e, "flightInitialized", true);
            Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
            GenSpawn.Spawn(wall, launcher.Position + new IntVec3(8, 0, 0), launcher.Map);
            target.Position = launcher.Position + new IntVec3(10, 0, 0);
            try
            {
                captureImpact = true;
                foreach (string method in new[] { "TryImpactTrackedTarget", "TryImpactAimPoint", "TryImpactBlockingBuilding" })
                    foreach (float offset in new[] { -.750001f, -.75f, -.05f, 0f, .05f, .75f, .750001f, 2f })
                        foreach (float length in new[] { 0f, .1f, .200001f, 3f })
                        {
                            var aim = method == "TryImpactAimPoint" ? new LocalTargetInfo(target.Position) : new LocalTargetInfo(target);
                            Set(a, "intendedTarget", aim); Set(e, "intendedTarget", aim);
                            GV center = (method == "TryImpactBlockingBuilding" ? wall.DrawPos : target.DrawPos);
                            GV start = center + new GV(-length / 2, 5, offset), end = center + new GV(length / 2, -2, offset);
                            impacted = null; bool av = (bool)Call(a, method, Native(start), Native(end)); Thing ah = impacted;
                            impacted = null; bool ev = (bool)Call(e, method, start, end);
                            if (av != ev || ah != impacted) throw new InvalidOperationException("TOW collision changed: " + method + " offset=" + offset);
                            if (av) hits++; else misses++;
                            collisions++;
                        }
                captureImpact = false;
                wall.DeSpawn();
                foreach (bool obstruction in new[] { false, true })
                {
                    SetVector(a, "exactPosition", launcher.DrawPos + new GV(15, 0, 0)); SetVector(e, "exactPosition", launcher.DrawPos + new GV(15, 0, 0));
                    Set(a, "wirePath", null); Set(e, "wirePath", null);
                    Call(a, "UpdateWirePath"); Call(e, "UpdateWirePath");
                    if (obstruction) GenSpawn.Spawn(wall, Game(Call(a, "OffsetWirePoint", 9, -1f)).ToIntVec3(), launcher.Map);
                    object[] na = { null }, oe = { null };
                    bool av = (bool)Call(a, "WireIntersectsTree", na), ev = (bool)Call(e, "WireIntersectsTree", oe);
                    if (av != ev || av != obstruction || na[0] != oe[0]) throw new InvalidOperationException("TOW wire obstruction changed: expected=" + obstruction + " native=" + av + " frozen=" + ev + " nativeThing=" + na[0] + " frozenThing=" + oe[0]);
                    Draw(a, e); wireTests++;
                }
            }
            finally { captureImpact = false; wall.Destroy(); a.Destroy(); e.Destroy(); }
        }
        private static Dictionary<string, int[]> Snapshot(Projectile_Tow missile)
        {
            var result = new Dictionary<string, int[]>();
            foreach (var f in typeof(Projectile_Tow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.Name.EndsWith("Material") || f.Name == "ignitionSustainer") continue;
                object value = f.GetValue(missile);
                float[] floats = Floats(value);
                result[f.Name] = floats != null ? floats.Select(BitConverter.SingleToInt32Bits).ToArray()
                    : value is int i ? new[] { i } : value is bool b ? new[] { b ? 1 : 0 }
                    : value is Thing t ? new[] { t.thingIDNumber }
                    : value is List<NV> list ? list.SelectMany(v => new[] { BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y), BitConverter.SingleToInt32Bits(v.Z) }).ToArray()
                    : value == null ? null : throw new InvalidOperationException("Uncovered TOW save field " + f.Name);
            }
            return result;
        }
        private static void SaveProbes(ThingDef source, Building_TurretGun launcher, string output)
        {
            var saved = new List<SavedMissile>();
            for (int i = 0; i < 4; i++)
            {
                var missile = (Projectile_Tow)ThingMaker.MakeThing(source); GenSpawn.Spawn(missile, launcher.Position, launcher.Map);
                missile.Launch(launcher, launcher.DrawPos, launcher.Position + new IntVec3(20, 0, 0), launcher.Position + new IntVec3(20, 0, 0), ProjectileHitFlags.None, false);
                Set(missile, "flightTicks", new[] { 0, 3, 99, 220 }[i]);
                SetVector(missile, "trackedTargetVelocity", new GV(-0f, 1e-7f, MathF.BitIncrement(.25f)));
                List<NV> path = i == 0 ? null : new List<NV>();
                for (int j = 0; j < (i == 2 ? 1 : i == 3 ? 19 : 0); j++)
                    path.Add(new NV(launcher.Position.x + j * .125f, -0f, MathF.BitIncrement(launcher.Position.z + j * .25f)));
                Set(missile, "wirePath", path);
                saved.Add(new SavedMissile { Id = missile.thingIDNumber, State = Snapshot(missile) });
            }
            File.WriteAllText(Path.Combine(output, "tow-states.json"), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        }
        internal static void Verify(string output, Action<bool, string> check)
        {
            var saved = JsonSerializer.Deserialize<SavedMissile[]>(File.ReadAllText(Path.Combine(output, "tow-states.json")));
            foreach (var item in saved)
            {
                var missile = Find.CurrentMap.listerThings.AllThings.OfType<Projectile_Tow>().Single(m => m.thingIDNumber == item.Id);
                var actual = Snapshot(missile);
                foreach (var p in item.State)
                    if (!actual.TryGetValue(p.Key, out int[] data) || ((data == null) != (p.Value == null)) || (data != null && !data.SequenceEqual(p.Value)))
                        throw new InvalidOperationException("TOW save state changed: " + item.Id + " " + p.Key);
            }
            check(saved.Length == 4, "TOW vectors, null/empty/single/19-point native wire lists, scalar state and launcher references round-trip exact bits before the first tick");
        }
    }
}
