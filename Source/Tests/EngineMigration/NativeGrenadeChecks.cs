using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using GV = UnityEngine.Vector3;
using NV = System.Numerics.Vector3;
using GM = UnityEngine.Matrix4x4;

namespace Helodrace.Tests
{
    internal static class NativeGrenadeChecks
    {
        private const BindingFlags Own = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        [ThreadStatic] private static List<GM> matrices;
        [ThreadStatic] private static List<FleckCreationData> flecks;
        private static int components, frames, trajectoryCases, partIntervals, launchCases, trailFlecks, contacts, expirations;
        private static float maximumError;
        private static bool CaptureMesh(GM __1)
        { if (matrices == null) return true; matrices.Add(__1); return false; }
        private static bool CaptureFleck(FleckCreationData __0)
        { if (flecks == null) return true; flecks.Add(__0); return false; }

        public sealed class SavedState
        {
            public int Id { get; set; }
            public string Kind { get; set; }
            public Dictionary<string, int[]> State { get; set; }
        }
        private static T Clone<T>(T item) => (T)CloneMethod.Invoke(item, null);
        private static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
                if (type.GetField(name, Own | BindingFlags.Public) is FieldInfo field) return field;
            throw new MissingFieldException(name);
        }
        private static object Get(object value, string field) => Field(value.GetType(), field).GetValue(value);
        private static void Set(object value, string field, object data) => Field(value.GetType(), field).SetValue(value, data);
        private static object Invoke(object value, string name, params object[] args)
            => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, args);
        private static NV Native(GV value) => new NV(value.x, value.y, value.z);
        private static void Same(float actual, float expected)
        {
            components++;
            maximumError = Math.Max(maximumError, Math.Abs(actual - expected));
            if (!float.IsFinite(actual) || !float.IsFinite(expected)
                || Math.Abs(actual - expected) > 2e-5f * Math.Max(1, Math.Abs(expected)))
                throw new InvalidOperationException("Grenade component differs: " + actual + " != " + expected);
        }
        private static float[] Floats(object value) => value switch
        {
            float f => new[] { f },
            GV v => new[] { v.x, v.y, v.z },
            NV v => new[] { v.X, v.Y, v.Z },
            UnityEngine.Vector2 v => new[] { v.x, v.y },
            System.Numerics.Vector2 v => new[] { v.X, v.Y },
            _ => null
        };
        private static void Compare(object actual, object expected)
        {
            foreach (var field in expected.GetType().GetFields(Own))
            {
                if (field.Name == "material") continue;
                object a = Get(actual, field.Name), e = field.GetValue(expected);
                float[] af = Floats(a), ef = Floats(e);
                if (af != null && ef != null)
                {
                    for (int i = 0; i < af.Length; i++) Same(af[i], ef[i]);
                }
                else if (!Equals(a, e)) throw new InvalidOperationException("Grenade state differs: " + field.Name + " " + a + " != " + e);
            }
        }
        private static void Draw(Thing actual, Thing expected)
        {
            matrices = new List<GM>();
            Invoke(actual, "DrawAt", new GV(999, 999, 999), false);
            GM[] a = matrices.ToArray(); matrices.Clear();
            Invoke(expected, "DrawAt", new GV(999, 999, 999), false);
            GM[] e = matrices.ToArray(); matrices = null;
            if (a.Length != e.Length) throw new InvalidOperationException("Grenade draw count differs.");
            for (int m = 0; m < a.Length; m++)
                for (int i = 0; i < 16; i++) Same(a[m][i], e[m][i]);
            frames++;
        }
        private static void CompareFlecks(FleckCreationData[] actual)
        {
            if (actual.Length != flecks.Count) throw new InvalidOperationException("Fleck count changed.");
            for (int i = 0; i < actual.Length; i++)
                foreach (var field in typeof(FleckCreationData).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object a = field.GetValue(actual[i]), e = field.GetValue(flecks[i]);
                    float[] af = Floats(a), ef = Floats(e);
                    if (af != null && ef != null)
                    { for (int axis = 0; axis < af.Length; axis++) Same(af[axis], ef[axis]); }
                    else if (!Equals(a, e)) throw new InvalidOperationException("Fleck data changed: " + field.Name);
                }
        }
        private static ThingDef FrozenDefinition(ThingDef source, Type type)
        {
            ThingDef result = Clone(source); result.thingClass = type;
            var extension = source.GetModExtension<ModernGrenadeProjectileExtension>();
            if (extension != null)
            {
                var copy = Clone(extension);
                if (copy.clipDef != null) copy.clipDef = FrozenDefinition(copy.clipDef, typeof(Frozen.ModernGrenadePart));
                if (copy.pinDef != null) copy.pinDef = FrozenDefinition(copy.pinDef, typeof(Frozen.ModernGrenadePart));
                result.modExtensions = source.modExtensions.Select(e => ReferenceEquals(e, extension) ? copy : e).ToList();
            }
            return result;
        }
        private static Dictionary<string, int[]> Snapshot(object value)
        {
            var state = new Dictionary<string, int[]>();
            foreach (var field in value.GetType().GetFields(Own))
            {
                object data = field.GetValue(value);
                if (field.Name == "material") continue;
                float[] floats = Floats(data);
                state[field.Name] = floats != null ? floats.Select(BitConverter.SingleToInt32Bits).ToArray()
                    : data is int i ? new[] { i } : data is bool b ? new[] { b ? 1 : 0 }
                    : throw new InvalidOperationException("Uncovered grenade state: " + field.Name);
            }
            return state;
        }

        internal static void Create(string output, Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.NativeGrenades");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(UnityEngine.Material), typeof(int) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeGrenadeChecks), nameof(CaptureMesh))));
            harmony.Patch(AccessTools.Method(typeof(FleckManager), nameof(FleckManager.CreateFleck)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeGrenadeChecks), nameof(CaptureFleck))));
            var definitions = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => typeof(Projectile_ModernGrenade).IsAssignableFrom(d.thingClass)).ToArray();
            ThingDef source = definitions.First(d => d.thingClass == typeof(Projectile_ModernGrenade) && d.GetModExtension<ModernGrenadeProjectileExtension>()?.clipDef != null);
            try
            {
                Trajectories(source);
                Parts(source.GetModExtension<ModernGrenadeProjectileExtension>().clipDef);
                Expiry(source.GetModExtension<ModernGrenadeProjectileExtension>().clipDef);
                Launches(definitions);
            }
            finally { matrices = null; flecks = null; harmony.UnpatchAll(harmony.Id); }
            check(true, trajectoryCases + " initialized grenade trajectories preserve flight/bounce positions, grid cells, draw transforms, clamping and RNG");
            check(true, partIntervals + " actual clip/pin tick intervals preserve delay, normalization, arc, spin, bounce, settling and draw transforms");
            check(true, launchCases + " actual grenade launches preserve fuse/trajectory/part state, trail creation data and RNG");
            check(trailFlecks > 0 && contacts > 0 && expirations == 2,
                trailFlecks + " observed trail flecks, " + contacts + " hard/soft terrain contacts and two actual part expirations match frozen behavior");
            check(typeof(Projectile_ModernGrenade).GetFields(Own).All(f => f.FieldType.Namespace != "UnityEngine" || f.FieldType == typeof(UnityEngine.Material))
                && typeof(ModernGrenadePart).GetFields(Own).All(f => f.FieldType.Namespace != "UnityEngine" || f.FieldType == typeof(UnityEngine.Material)),
                "grenade and part motion state are native; only game material handles remain");

            var saved = new List<SavedState>();
            Map map = Find.CurrentMap;
            IntVec3 cell = map.Center;
            for (int i = 0; i < 4; i++)
            {
                var grenade = (Projectile_ModernGrenade)ThingMaker.MakeThing(source);
                Set(grenade, "destination", cell.ToVector3Shifted() + new GV(6 + i, 0, .000001f));
                Set(grenade, "ticksToImpact", 60);
                Set(grenade, "closeUnderhandThrow", i % 2 == 0);
                Invoke(grenade, "InitializeTrajectory", new NV(cell.x + .125f, -0f, MathF.BitIncrement(cell.z + .25f)), source.GetModExtension<ModernGrenadeProjectileExtension>());
                Set(grenade, "age", new[] { 0, 30, 60, 68 }[i]);
                Set(grenade, "fuseDurationTicks", 500);
                if (i >= 2)
                {
                    Invoke(grenade, "SetBounceSurfaceFactor", 0, i == 2 ? 1.15f : MathF.BitIncrement(1f));
                    Invoke(grenade, "SetBounceSurfaceFactor", 1, .52f);
                }
                Invoke(grenade, "UpdateTrajectoryPosition");
                GenSpawn.Spawn(grenade, cell + new IntVec3(i, 0, 0), map);
                saved.Add(new SavedState { Id = grenade.thingIDNumber, Kind = "projectile", State = Snapshot(grenade) });

                var part = (ModernGrenadePart)ThingMaker.MakeThing(source.GetModExtension<ModernGrenadeProjectileExtension>().clipDef);
                part.Initialize(new NV(cell.x + .125f, -0f, MathF.BitIncrement(cell.z + .25f)), new NV(.3f, 0, -.7f), 2.1f, i == 0 ? 8 : i * 3, 30, .58f, -37, 18, .16f, 1, 240);
                for (int tick = 0; tick < new[] { 0, 18, 42, 80 }[i]; tick++) Invoke(part, "Tick");
                GenSpawn.Spawn(part, cell + new IntVec3(i, 0, 1), map);
                saved.Add(new SavedState { Id = part.thingIDNumber, Kind = "part", State = Snapshot(part) });
            }
            File.WriteAllText(Path.Combine(output, "grenade-states.json"), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "native-grenades.json"), JsonSerializer.Serialize(new {
                Trajectories = trajectoryCases, PartIntervals = partIntervals, Launches = launchCases,
                DrawCallbacks = frames, Components = components, MaximumFloatError = maximumError,
                TrailFlecks = trailFlecks, TerrainContacts = contacts, PartExpirations = expirations, Baseline = "fc204ee8"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void Trajectories(ThingDef source)
        {
            var definition = Clone(source); definition.thingClass = typeof(Projectile_ModernGrenade);
            var frozenDef = FrozenDefinition(source, typeof(Frozen.Projectile_ModernGrenade));
            foreach (GV origin in new[] { GV.zero, new GV(10.125f, .2f, 20.75f), new GV(-3, -0f, -.25f) })
                foreach (GV delta in new[] { GV.zero, new GV(.000999f, 0, 0), new GV(.001001f, 0, 0), new GV(2, 0, -3), new GV(-20, 0, 8) })
                    foreach (bool underhand in new[] { false, true })
                        foreach (int duration in new[] { 1, 9, 60 })
                            foreach (var ext in new[] { null, source.GetModExtension<ModernGrenadeProjectileExtension>(),
                                new ModernGrenadeProjectileExtension { projectileBounceTicks = -5, projectileBounceTravelDistance = -.2f, projectileBounceHeight = -.1f, projectileVisualArcHeight = -.1f } })
                            {
                                var a = (Projectile_ModernGrenade)ThingMaker.MakeThing(definition);
                                var e = (Frozen.Projectile_ModernGrenade)ThingMaker.MakeThing(frozenDef);
                                foreach (object value in new object[] { a, e })
                                {
                                    Set(value, "destination", origin + delta); Set(value, "ticksToImpact", duration); Set(value, "closeUnderhandThrow", underhand);
                                }
                                float ar, er;
                                Rand.PushState(trajectoryCases);
                                try { Invoke(a, "InitializeTrajectory", Native(origin), ext); ar = Rand.Value; } finally { Rand.PopState(); }
                                Rand.PushState(trajectoryCases);
                                try { Invoke(e, "InitializeTrajectory", origin, ext); er = Rand.Value; } finally { Rand.PopState(); }
                                if (ar != er) throw new InvalidOperationException("Trajectory RNG changed.");
                                int end = duration + (int)Get(a, "bounceFlightTicks");
                                foreach (int age in new[] { 0, 1, duration / 2, duration, duration + 1, (duration + end) / 2, end - 1, end, end + 1 })
                                {
                                    foreach (object value in new object[] { a, e })
                                    {
                                        Set(value, "age", age);
                                        Invoke(value, "SetBounceSurfaceFactor", 0, 1.15f); Invoke(value, "SetBounceSurfaceFactor", 1, .52f);
                                        Invoke(value, "UpdateTrajectoryPosition");
                                    }
                                    Compare(a, e);
                                    GV pa = a.ExactPosition, pe = e.ExactPosition;
                                    if (pa.ToIntVec3() != pe.ToIntVec3()) throw new InvalidOperationException("Grenade grid cell changed.");
                                    float[] trailA = Floats(Invoke(a, "CurrentVisualTrailPosition", ext)), trailE = Floats(Invoke(e, "CurrentVisualTrailPosition", ext));
                                    for (int axis = 0; axis < 3; axis++) Same(trailA[axis], trailE[axis]);
                                    Draw(a, e);
                                }
                                trajectoryCases++;
                            }
        }

        private static void Parts(ThingDef definition)
        {
            ThingDef frozenDef = FrozenDefinition(definition, typeof(Frozen.ModernGrenadePart));
            foreach (GV direction in new[] { GV.zero, new GV(.000009f, 0, 0), new GV(.000011f, 0, 0), new GV(.3f, -1f, .7f) })
                foreach (int delay in new[] { -1, 0, 8 })
                    foreach (int flight in new[] { -1, 1, 30 })
                        foreach (int bounce in new[] { 0, 18 })
                        {
                            var a = (ModernGrenadePart)ThingMaker.MakeThing(definition);
                            var e = (Frozen.ModernGrenadePart)ThingMaker.MakeThing(frozenDef);
                            GV start = new GV(10.125f, .2f, MathF.BitIncrement(12));
                            float ar, er;
                            Rand.PushState(partIntervals);
                            try { a.Initialize(Native(start), Native(direction), 2.1f, delay, flight, .58f, -37, bounce, .16f, 2, 240); ar = Rand.Value; } finally { Rand.PopState(); }
                            Rand.PushState(partIntervals);
                            try { e.Initialize(start, direction, 2.1f, delay, flight, .58f, -37, bounce, .16f, 2, 240); er = Rand.Value; } finally { Rand.PopState(); }
                            if (ar != er) throw new InvalidOperationException("Part RNG changed.");
                            for (int tick = 0; tick < Math.Max(0, delay) + Math.Max(1, flight) + bounce + 3; tick++)
                            {
                                Invoke(a, "Tick"); Invoke(e, "Tick"); Compare(a, e); Draw(a, e); partIntervals++;
                            }
                        }
        }

        private static void Launches(ThingDef[] definitions)
        {
            Map map = Find.CurrentMap; IntVec3 start = map.Center;
            Thing launcher = ThingMaker.MakeThing(ThingDefOf.WoodLog); GenSpawn.Spawn(launcher, start, map);
            try
            {
                foreach (ThingDef definition in definitions)
                {
                    ThingDef nativeDef = Clone(definition); nativeDef.thingClass = typeof(Projectile_ModernGrenade);
                    ThingDef oldDef = FrozenDefinition(definition, typeof(Frozen.Projectile_ModernGrenade));
                    var a = (Projectile_ModernGrenade)ThingMaker.MakeThing(nativeDef);
                    var e = (Frozen.Projectile_ModernGrenade)ThingMaker.MakeThing(oldDef);
                    GenSpawn.Spawn(a, start, map); GenSpawn.Spawn(e, start, map);
                    var before = new HashSet<Thing>(map.listerThings.AllThings);
                    try
                    {
                        GV origin = start.ToVector3Shifted() + new GV(.125f, 0, -.25f);
                        LocalTargetInfo target = start + new IntVec3(8, 0, 5);
                        float ar, er;
                        Rand.PushState(launchCases);
                        try { a.Launch(launcher, origin, target, target, ProjectileHitFlags.All); ar = Rand.Value; } finally { Rand.PopState(); }
                        var partsA = map.listerThings.AllThings.OfType<ModernGrenadePart>().Where(t => !before.Contains(t)).OrderBy(t => t.def.defName).ToArray();
                        Rand.PushState(launchCases);
                        try { e.Launch(launcher, origin, target, target, ProjectileHitFlags.All); er = Rand.Value; } finally { Rand.PopState(); }
                        var partsE = map.listerThings.AllThings.OfType<Frozen.ModernGrenadePart>().Where(t => !before.Contains(t)).OrderBy(t => t.def.defName).ToArray();
                        if (ar != er || partsA.Length != partsE.Length) throw new InvalidOperationException("Launch part count or RNG changed: " + definition.defName);
                        Compare(a, e);
                        for (int i = 0; i < partsA.Length; i++) Compare(partsA[i], partsE[i]);
                        var ext = definition.GetModExtension<ModernGrenadeProjectileExtension>();
                        if (ext != null)
                        {
                            foreach (int age in new[] { 4, 8, 60, 72 })
                            {
                                Set(a, "age", age); Set(e, "age", age); Invoke(a, "UpdateTrajectoryPosition"); Invoke(e, "UpdateTrajectoryPosition");
                                flecks = new List<FleckCreationData>();
                                Rand.PushState(age);
                                try { Invoke(a, "ThrowFlightTrail", ext); ar = Rand.Value; } finally { Rand.PopState(); }
                                var af = flecks.ToArray(); flecks.Clear();
                                Rand.PushState(age);
                                try { Invoke(e, "ThrowFlightTrail", ext); er = Rand.Value; } finally { Rand.PopState(); }
                                if (ar != er || af.Length != flecks.Count) throw new InvalidOperationException("Grenade trail count or RNG changed.");
                                CompareFlecks(af); trailFlecks += af.Length;
                                flecks = null;
                            }
                        }
                        Contacts(a, e);
                        launchCases++;
                    }
                    finally
                    {
                        flecks = null;
                        foreach (Thing t in map.listerThings.AllThings.Where(t => !before.Contains(t)).ToArray()) if (!t.Destroyed) t.Destroy();
                        if (!a.Destroyed) a.Destroy(); if (!e.Destroyed) e.Destroy();
                    }
                }
            }
            finally { launcher.Destroy(); }
        }

        private static void Contacts(Projectile_ModernGrenade a, Frozen.Projectile_ModernGrenade e)
        {
            Map map = a.Map;
            foreach (string terrainName in new[] { "Concrete", "Sand" })
            {
                foreach (object value in new object[] { a, e })
                { Set(value, "lastBounceContact", -1); Set(value, "bounceCount", 2); }
                int primary = (int)Get(a, "primaryFlightTicks"), bounce = (int)Get(a, "bounceFlightTicks");
                foreach (int age in new[] { primary, primary + (bounce + 1) / 2 })
                {
                    Set(a, "age", age); Set(e, "age", age); Invoke(a, "UpdateTrajectoryPosition"); Invoke(e, "UpdateTrajectoryPosition");
                    IntVec3 cell = a.ExactPosition.ToIntVec3();
                    if (cell != e.ExactPosition.ToIntVec3() || !cell.InBounds(map)) throw new InvalidOperationException("Contact cell differs or is outside map.");
                    TerrainDef original = cell.GetTerrain(map);
                    try
                    {
                        map.terrainGrid.SetTerrain(cell, DefDatabase<TerrainDef>.GetNamed(terrainName));
                        flecks = new List<FleckCreationData>();
                        float ar, er;
                        Rand.PushState(age);
                        try { Invoke(a, "ThrowBounceContactFleck"); ar = Rand.Value; } finally { Rand.PopState(); }
                        var af = flecks.ToArray(); flecks.Clear();
                        Rand.PushState(age);
                        try { Invoke(e, "ThrowBounceContactFleck"); er = Rand.Value; } finally { Rand.PopState(); }
                        if (ar != er) throw new InvalidOperationException("Contact RNG changed.");
                        Compare(a, e); CompareFlecks(af); contacts++;
                    }
                    finally { flecks = null; map.terrainGrid.SetTerrain(cell, original); }
                }
            }
        }

        private static void Expiry(ThingDef definition)
        {
            Map map = Find.CurrentMap;
            foreach (int settled in new[] { 1, 240 })
            {
                var a = (ModernGrenadePart)ThingMaker.MakeThing(definition);
                var e = (Frozen.ModernGrenadePart)ThingMaker.MakeThing(FrozenDefinition(definition, typeof(Frozen.ModernGrenadePart)));
                GV start = map.Center.ToVector3Shifted();
                Rand.PushState(settled);
                try { a.Initialize(Native(start), NV.UnitZ, 1, 2, 1, .1f, 37, 1, .1f, 1, settled); } finally { Rand.PopState(); }
                Rand.PushState(settled);
                try { e.Initialize(start, GV.forward, 1, 2, 1, .1f, 37, 1, .1f, 1, settled); } finally { Rand.PopState(); }
                GenSpawn.Spawn(a, map.Center, map); GenSpawn.Spawn(e, map.Center, map);
                try
                {
                    int lifetime = 2 + 1 + 1 + settled;
                    for (int tick = 1; tick <= lifetime; tick++)
                    {
                        Invoke(a, "Tick"); Invoke(e, "Tick"); Compare(a, e);
                        if (a.Destroyed != (tick == lifetime) || a.Destroyed != e.Destroyed)
                            throw new InvalidOperationException("Part expiration tick changed.");
                    }
                    expirations++;
                }
                finally { if (!a.Destroyed) a.Destroy(); if (!e.Destroyed) e.Destroy(); }
            }
        }

        internal static void Verify(string output, Action<bool, string> check)
        {
            var saved = JsonSerializer.Deserialize<SavedState[]>(File.ReadAllText(Path.Combine(output, "grenade-states.json")));
            foreach (var item in saved)
            {
                Thing thing = Find.CurrentMap.listerThings.AllThings.Single(t => t.thingIDNumber == item.Id);
                var actual = Snapshot(thing);
                if (actual.Count != item.State.Count || item.State.Any(p => !actual.TryGetValue(p.Key, out int[] a) || !a.SequenceEqual(p.Value)))
                    throw new InvalidOperationException("Native grenade state failed bit-exact save/reload: " + item.Id + " " + item.Kind);
            }
            check(saved.Length == 8, "all native grenade and part vectors/scalars round-trip exact bits before the first tick (eight in-flight/bouncing/settled objects)");
        }
    }
}
