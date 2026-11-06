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
    internal static class NativeM47Checks
    {
        internal static int PhaseIdentity;
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        [ThreadStatic] private static List<GM> matrices;
        [ThreadStatic] private static List<(GV, GV, float)> lines;
        [ThreadStatic] private static bool captureImpact;
        [ThreadStatic] private static Thing impacted;
        private static int components, pulses, coasts, burns, frames, flightTicks, retargets, collisions, hits, misses, wires, edges;
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
            if (!captureImpact || !(__instance is Projectile_M47Dragon || __instance is Frozen.Projectile_M47Dragon)) return true;
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
        { var d = Clone(source); d.thingClass = frozen ? typeof(Frozen.Projectile_M47Dragon) : typeof(Projectile_M47Dragon); return d; }
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static GV Game(object v) => v is NV n ? new GV(n.X, n.Y, n.Z) : (GV)v;
        private static void SetVector(object v, string name, GV data) => Set(v, name, Field(v.GetType(), name).FieldType == typeof(NV) ? Native(data) : data);
        private static float[] Floats(object v) => v switch { float f => new[] { f }, NV n => new[] { n.X, n.Y, n.Z }, GV g => new[] { g.x, g.y, g.z }, _ => null };
        private static void Same(float a, float e)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(a - e));
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1, Math.Abs(e)))
                throw new InvalidOperationException("M47 component differs: " + a + " != " + e);
        }
        private static void Vector(object a, object e)
        { var x = Floats(a); var y = Floats(e); for (int i = 0; i < x.Length; i++) Same(x[i], y[i]); }
        private static void Compare(Projectile_M47Dragon a, Frozen.Projectile_M47Dragon e)
        {
            foreach (var f in e.GetType().GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                object x = Get(a, f.Name), y = f.GetValue(e);
                if (Floats(x) != null && Floats(y) != null) Vector(x, y);
                else if (f.Name == "wirePath" && x != null && y != null)
                {
                    var ax = ((IEnumerable)x).Cast<object>().ToArray(); var ey = ((IEnumerable)y).Cast<object>().ToArray();
                    if (ax.Length != ey.Length) throw new InvalidOperationException("M47 wire point count changed.");
                    for (int i = 0; i < ax.Length; i++) Vector(ax[i], ey[i]);
                }
                else if (!Equals(x, y)) throw new InvalidOperationException("M47 state differs: " + f.Name + " " + x + " != " + y);
            }
            foreach (string name in new[] { "origin", "destination" }) Vector(Get(a, name), Get(e, name));
            foreach (string name in new[] { "ticksToImpact", "lifetime" })
                if (!Equals(Get(a, name), Get(e, name))) throw new InvalidOperationException("M47 base timing differs: " + name);
            Vector(a.ExactPosition, e.ExactPosition);
            if (a.ExactPosition.ToIntVec3() != e.ExactPosition.ToIntVec3()) throw new InvalidOperationException("M47 grid cell changed.");
        }
        private static void Draw(Projectile_M47Dragon a, Frozen.Projectile_M47Dragon e)
        {
            bool active = (bool)a.GetType().GetProperty("HasActiveSteeringFrame", All).GetValue(a);
            if (active != (bool)e.GetType().GetProperty("HasActiveSteeringFrame", All).GetValue(e)) throw new InvalidOperationException("M47 steering frame activation changed.");
            matrices = new List<GM>(); lines = new List<(GV, GV, float)>();
            void Submit(object value)
            {
                // The base projectile's ordinary renderer is an unchanged game
                // callback. Inspect the custom steering frame and both wires.
                if (active) Call(value, "DrawAt", GV.zero, false);
                else { Call(value, "DrawWire", -1f); Call(value, "DrawWire", 1f); }
            }
            Submit(a); var am = matrices.ToArray(); var al = lines.ToArray(); matrices.Clear(); lines.Clear(); Submit(e);
            if (am.Length != matrices.Count || al.Length != lines.Count) throw new InvalidOperationException("M47 draw count changed.");
            for (int m = 0; m < am.Length; m++) for (int i = 0; i < 16; i++) Same(am[m][i], matrices[m][i]);
            for (int i = 0; i < al.Length; i++) { Vector(al[i].Item1, lines[i].Item1); Vector(al[i].Item2, lines[i].Item2); Same(al[i].Item3, lines[i].Item3); }
            matrices = null; lines = null; frames++;
        }

        internal static void Create(string output, Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.NativeM47");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(UnityEngine.Material), typeof(int) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeM47Checks), nameof(Mesh))));
            harmony.Patch(AccessTools.Method(typeof(GenDraw), "DrawLineBetween", new[] { typeof(GV), typeof(GV), typeof(UnityEngine.Material), typeof(float) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeM47Checks), nameof(Line))));
            harmony.Patch(AccessTools.Method(typeof(Projectile_Explosive), "Impact"), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeM47Checks), nameof(Impact))));
            Map map = Find.CurrentMap; IntVec3 start = map.Center;
            foreach (IntVec3 cell in CellRect.CenteredOn(start + new IntVec3(35, 0, 0), 85, 35).ClipInsideMap(map))
                foreach (Thing t in cell.GetThingList(map).ToArray()) if (t is Building || t is Plant) t.Destroy();
            Thing launcher = ThingMaker.MakeThing(ThingDefOf.WoodLog); GenSpawn.Spawn(launcher, start, map);
            var equipment = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M47_Dragon")); GenSpawn.Spawn(equipment, start + new IntVec3(0, 0, 1), map);
            Thing target = ThingMaker.MakeThing(ThingDefOf.WoodLog); GenSpawn.Spawn(target, start + new IntVec3(75, 0, 0), map);
            ThingDef source = DefDatabase<ThingDef>.GetNamed("HD_M222_Proj");
            try
            {
                PurePulses(source, target);
                ActualFlight(source, launcher, equipment, target);
                CollisionChecks(source, launcher, target);
                SaveProbes(source, launcher, equipment, output);
            }
            finally { captureImpact = false; matrices = null; lines = null; harmony.UnpatchAll(harmony.Id); target.Destroy(); }
            check(coasts > 0 && burns > 0, pulses + " M47 pulse steps preserve coasting and active corrections (coasts=" + coasts + ", burns=" + burns + "), target vectors, textures and base trajectory timing");
            check(retargets == 3, flightTicks + " actual flight ticks and three successful retargets preserve base movement, launcher registration, steering cadence and wires");
            check(hits > 0 && misses > 0, collisions + " tracked/building collision samples preserve hits=" + hits + " and misses=" + misses);
            check(wires == 2 && edges > 0, wires + " wire obstruction cases and " + edges + " map-edge distances match frozen calculation");
            check(typeof(Projectile_M47Dragon).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic).All(f => f.FieldType != typeof(GV) && f.FieldType != typeof(List<GV>)), "M47 owns native guidance vectors and wire-point list");
            File.WriteAllText(Path.Combine(output, "native-m47.json"), JsonSerializer.Serialize(new {
                Pulses = pulses, Coasts = coasts, Burns = burns, FlightTicks = flightTicks, Retargets = retargets,
                CollisionCases = collisions, Hits = hits, Misses = misses, WireCases = wires, MapEdges = edges, DrawCallbacks = frames,
                Components = components, MaximumFloatError = maximumError, Baseline = "4f2080ef"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void SeedBase(object value, GV start, float speed)
        { Set(value, "origin", start); Set(value, "destination", start + GV.forward * (speed * 100)); Set(value, "ticksToImpact", 100); Set(value, "lifetime", 100); }
        private static void PurePulses(ThingDef source, Thing target)
        {
            foreach (M47DragonProjectileExtension ext in new[] { null, source.GetModExtension<M47DragonProjectileExtension>(), new M47DragonProjectileExtension {
                maximumLaunchDeviationDegrees = -1, alignedCoastAngleDegrees = 0, targetVectorSmoothing = 2,
                vectorLeadStartDistance = 0, vectorLeadFullDistance = 2, maximumLeadTicks = -1, terminalOscillationFactor = 2 } })
                foreach (float angle in new[] { -150f, -12.0001f, -2.0001f, -.35f, 0f, .35f, 1.9999f, 2f, 2.0001f, 11.9999f, 12f, 12.0001f, 35f, 90f, 180f })
                    foreach (int elapsed in new[] { 0, 1, 7 })
                    {
                        ThingDef d = Definition(source, false); d.modExtensions = ext == null ? null : new List<DefModExtension> { ext };
                        var a = (Projectile_M47Dragon)ThingMaker.MakeThing(d); var e = (Frozen.Projectile_M47Dragon)ThingMaker.MakeThing(Definition(d, true)); PhaseIdentity = a.thingIDNumber;
                        GV start = target.DrawPos + new GV(0, 0, -30);
                        SeedBase(a, start, source.projectile.SpeedTilesPerTick); SeedBase(e, start, source.projectile.SpeedTilesPerTick);
                        GV desired = target.DrawPos - a.ExactPosition; desired.y = 0; desired.Normalize();
                        GV direction = UnityEngine.Quaternion.AngleAxis(angle, GV.up) * desired;
                        foreach (object value in new object[] { a, e })
                        {
                            Set(value, "intendedTarget", new LocalTargetInfo(target)); SetVector(value, "flightDirection", direction); SetVector(value, "initialGuidanceDirection", desired);
                            SetVector(value, "trackedTargetVelocity", new GV(.1f, 0, -.2f)); SetVector(value, "lastTrackedTargetPosition", target.DrawPos + new GV(-.2f, 0, .4f));
                            Set(value, "targetVectorInitialized", true); Set(value, "lastTargetVectorSampleTick", Find.TickManager.TicksGame - elapsed);
                        }
                        for (int step = 0; step < 6; step++)
                        {
                            Call(a, "ApplySteeringPulse"); Call(e, "ApplySteeringPulse"); Compare(a, e); Draw(a, e);
                            if ((int)Get(a, "steeringFlameTicksLeft") > 0) burns++; else coasts++;
                            pulses++;
                        }
                        foreach (string suffix in new[] { "up", "midleft", "midright", "upleft", "upright", "left", "right", "downleft", "downright", "down" })
                        {
                            Set(a, "steeringTextureSuffix", suffix); Set(e, "steeringTextureSuffix", suffix); Set(a, "steeringFlameTicksLeft", 1); Set(e, "steeringFlameTicksLeft", 1); Draw(a, e);
                        }
                    }
        }

        private static void ActualFlight(ThingDef source, Thing launcher, ThingWithComps equipment, Thing target)
        {
            for (int trial = 0; trial < 3; trial++)
            {
                var a = (Projectile_M47Dragon)ThingMaker.MakeThing(source); var e = (Frozen.Projectile_M47Dragon)ThingMaker.MakeThing(Definition(source, true));
                GenSpawn.Spawn(a, launcher.Position, launcher.Map); GenSpawn.Spawn(e, launcher.Position, launcher.Map); PhaseIdentity = a.thingIDNumber;
                try
                {
                    target.Position = launcher.Position + new IntVec3(75, 0, trial * 3);
                    GV origin = launcher.DrawPos + new GV(.125f, 0, -.25f);
                    Rand.PushState(trial); try { a.Launch(launcher, origin, target, target, ProjectileHitFlags.None, false, equipment); } finally { Rand.PopState(); }
                    Rand.PushState(trial); try { e.Launch(launcher, origin, target, target, ProjectileHitFlags.None, false, equipment); } finally { Rand.PopState(); }
                    for (int tick = 0; tick < 24; tick++)
                    {
                        if (tick == 8) target.Position += new IntVec3(0, 0, 12 * (trial - 1));
                        if (tick == 12)
                        {
                            LocalTargetInfo replacement = launcher.Position + new IntVec3(70, 0, 3 + trial);
                            bool av = a.TryChangeTarget(replacement), ev = e.TryChangeTarget(replacement);
                            if (!av || av != ev) throw new InvalidOperationException("M47 retarget did not succeed equally."); retargets++;
                        }
                        Rand.PushState(tick); try { Call(a, "Tick"); } finally { Rand.PopState(); }
                        Rand.PushState(tick); try { Call(e, "Tick"); } finally { Rand.PopState(); }
                        if (a.Destroyed || e.Destroyed) throw new InvalidOperationException("M47 flight ended before planned observation.");
                        if (tick == 0 && equipment.GetComp<CompM47DragonLauncher>().ActiveMissile != a) throw new InvalidOperationException("M47 typed launcher registration failed.");
                        Compare(a, e); Draw(a, e); flightTicks++;
                    }
                }
                finally { if (!a.Destroyed) a.Destroy(); if (!e.Destroyed) e.Destroy(); }
            }
        }

        private static void CollisionChecks(ThingDef source, Thing launcher, Thing target)
        {
            var a = (Projectile_M47Dragon)ThingMaker.MakeThing(source); var e = (Frozen.Projectile_M47Dragon)ThingMaker.MakeThing(Definition(source, true));
            GenSpawn.Spawn(a, launcher.Position, launcher.Map); GenSpawn.Spawn(e, launcher.Position, launcher.Map);
            a.Launch(launcher, launcher.DrawPos, target, target, ProjectileHitFlags.None, false);
            e.Launch(launcher, launcher.DrawPos, target, target, ProjectileHitFlags.None, false);
            Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog); GenSpawn.Spawn(wall, launcher.Position + new IntVec3(8, 0, 0), launcher.Map);
            target.Position = launcher.Position + new IntVec3(10, 0, 0);
            try
            {
                captureImpact = true;
                foreach (string method in new[] { "TryImpactTrackedTarget", "TryImpactBlockingBuilding" })
                    foreach (float offset in new[] { -.420001f, -.42f, -.05f, 0f, .05f, .42f, .420001f, 2f })
                        foreach (float length in new[] { 0f, .1f, .200001f, 3f })
                        {
                            GV center = method == "TryImpactBlockingBuilding" ? wall.DrawPos : target.DrawPos;
                            GV start = center + new GV(-length / 2, 0, offset), end = center + new GV(length / 2, 0, offset);
                            Set(a, "intendedTarget", new LocalTargetInfo(target)); Set(e, "intendedTarget", new LocalTargetInfo(target));
                            impacted = null; bool av = (bool)Call(a, method, Native(start), Native(end)); Thing ah = impacted;
                            impacted = null; bool ev = (bool)Call(e, method, start, end);
                            if (av != ev || ah != impacted) throw new InvalidOperationException("M47 collision changed: " + method);
                            if (av) hits++; else misses++; collisions++;
                        }
                captureImpact = false; wall.DeSpawn();
                foreach (bool obstruction in new[] { false, true })
                {
                    GV position = launcher.DrawPos + new GV(15, 0, 0);
                    foreach (object value in new object[] { a, e })
                    { Set(value, "origin", position); Set(value, "destination", position + GV.right * (source.projectile.SpeedTilesPerTick * 100)); Set(value, "ticksToImpact", 100); Set(value, "wirePath", null); Call(value, "UpdateWirePath"); }
                    if (obstruction) GenSpawn.Spawn(wall, Game(Call(a, "OffsetWirePoint", 9, -1f)).ToIntVec3(), launcher.Map);
                    object[] na = { null }, oe = { null };
                    bool av = (bool)Call(a, "WireIntersectsTree", na), ev = (bool)Call(e, "WireIntersectsTree", oe);
                    if (av != ev || av != obstruction || na[0] != oe[0]) throw new InvalidOperationException("M47 wire obstruction changed.");
                    Draw(a, e); wires++;
                }
                foreach (GV position in new[] { GV.zero, new GV(.01f, 0, .01f), launcher.DrawPos, new GV(launcher.Map.Size.x - .01f, 0, launcher.Map.Size.z - .01f) })
                    foreach (GV direction in new[] { GV.zero, GV.forward, GV.right, -GV.forward, -GV.right, new GV(.000099f, 0, .000101f), new GV(.3f, 0, -.7f) })
                    { Same((float)Call(a, "DistanceToMapEdge", Native(position), Native(direction)), (float)Call(e, "DistanceToMapEdge", position, direction)); edges++; }
            }
            finally { captureImpact = false; wall.Destroy(); a.Destroy(); e.Destroy(); }
        }

        private static Dictionary<string, int[]> Snapshot(Projectile_M47Dragon missile)
        {
            var result = new Dictionary<string, int[]>();
            foreach (var f in typeof(Projectile_M47Dragon).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object value = f.GetValue(missile); float[] floats = Floats(value);
                result[f.Name] = floats != null ? floats.Select(BitConverter.SingleToInt32Bits).ToArray()
                    : value is int i ? new[] { i } : value is bool b ? new[] { b ? 1 : 0 }
                    : value is string s ? s.Select(c => (int)c).ToArray()
                    : value is List<NV> list ? list.SelectMany(v => new[] { BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y), BitConverter.SingleToInt32Bits(v.Z) }).ToArray()
                    : value == null ? null : throw new InvalidOperationException("Uncovered M47 save field " + f.Name);
            }
            foreach (string name in new[] { "origin", "destination" }) result["base-" + name] = Floats(Get(missile, name)).Select(BitConverter.SingleToInt32Bits).ToArray();
            foreach (string name in new[] { "ticksToImpact", "lifetime" }) result["base-" + name] = new[] { (int)Get(missile, name) };
            result["base-launcher"] = new[] { missile.Launcher.thingIDNumber }; result["base-equipment"] = new[] { ((Thing)Get(missile, "equipment")).thingIDNumber };
            return result;
        }
        private static void SaveProbes(ThingDef source, Thing launcher, ThingWithComps equipment, string output)
        {
            var saved = new List<SavedMissile>();
            for (int i = 0; i < 4; i++)
            {
                var missile = (Projectile_M47Dragon)ThingMaker.MakeThing(source); GenSpawn.Spawn(missile, launcher.Position, launcher.Map);
                missile.Launch(launcher, launcher.DrawPos, launcher.Position + new IntVec3(30, 0, 0), launcher.Position + new IntVec3(30, 0, 0), ProjectileHitFlags.None, false, equipment);
                Call(missile, "InitializeFlightDirection"); Call(missile, "RegisterWithLauncher");
                Set(missile, "wireAgeTicks", i * 6); Set(missile, "steeringPulseCount", i); Set(missile, "guidanceActive", i != 3);
                Set(missile, "steeringTextureSuffix", new[] { null, "midleft", "", "right" }[i]); Set(missile, "steeringFlameTicksLeft", i);
                SetVector(missile, "trackedTargetVelocity", new GV(-0f, 1e-7f, MathF.BitIncrement(.25f)));
                List<NV> path = i == 0 ? null : new List<NV>();
                for (int j = 0; j < (i == 2 ? 1 : i == 3 ? 19 : 0); j++) path.Add(new NV(launcher.Position.x + j * .125f, -0f, MathF.BitIncrement(launcher.Position.z + j * .25f)));
                Set(missile, "wirePath", path);
                saved.Add(new SavedMissile { Id = missile.thingIDNumber, State = Snapshot(missile) });
            }
            File.WriteAllText(Path.Combine(output, "m47-states.json"), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
        }
        internal static void Verify(string output, Action<bool, string> check)
        {
            var saved = JsonSerializer.Deserialize<SavedMissile[]>(File.ReadAllText(Path.Combine(output, "m47-states.json")));
            foreach (var item in saved)
            {
                var missile = Find.CurrentMap.listerThings.AllThings.OfType<Projectile_M47Dragon>().Single(m => m.thingIDNumber == item.Id);
                var actual = Snapshot(missile);
                foreach (var p in item.State)
                    if (!actual.TryGetValue(p.Key, out int[] data) || ((data == null) != (p.Value == null)) || (data != null && !data.SequenceEqual(p.Value)))
                        throw new InvalidOperationException("M47 save state changed: " + item.Id + " " + p.Key);
            }
            check(saved.Length == 4, "M47 native vectors and null/empty/single/19-point wires, pulse texture/state, base trajectory and launcher/equipment references round-trip exact bits before the first tick");
        }
    }
}
