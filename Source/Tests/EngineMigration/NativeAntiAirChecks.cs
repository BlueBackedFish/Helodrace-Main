using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using GV = UnityEngine.Vector3;
using NV = System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeAntiAirChecks
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Type Registry = typeof(CompMannedAntiAir).Assembly.GetType("Helodrace.AntiAirRoundRegistry", true);
        private static readonly Action<Projectile, CompMannedAntiAir> Mark = (Action<Projectile, CompMannedAntiAir>)Registry.GetMethod("Mark", All).CreateDelegate(typeof(Action<Projectile, CompMannedAntiAir>));
        private static readonly Action<Projectile> Tick = (Action<Projectile>)Registry.GetMethod("Tick", All).CreateDelegate(typeof(Action<Projectile>));
        private static readonly Action<Projectile> Remove = (Action<Projectile>)Registry.GetMethod("Remove", All).CreateDelegate(typeof(Action<Projectile>));
        private static readonly IDictionary Rounds = (IDictionary)Registry.GetField("Rounds", All).GetValue(null);
        private static readonly IDictionary OldRounds = (IDictionary)typeof(Frozen.AntiAirRoundRegistry).GetField("Rounds", All).GetValue(null);
        private static int components, decisions, distances, leads, trajectories, registryCases;
        private static float maximumError;
        private static bool captureResolve, resolveResult;
        private static int nativeCalls, frozenCalls;

        public sealed class PositionProbe : Projectile
        {
            public GV Current;
            public override GV ExactPosition => Current;
        }
        private static bool ResolveProbe(ThingComp __instance, ref bool __result)
        {
            if (!captureResolve) return true;
            if (__instance is CompMannedAntiAir) nativeCalls++; else frozenCalls++;
            __result = resolveResult;
            return false;
        }
        private static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
                if (type.GetField(name, BindingFlags.DeclaredOnly | All) is FieldInfo result) return result;
            throw new MissingFieldException(name);
        }
        private static object Get(object target, string name) => Field(target.GetType(), name).GetValue(target);
        private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static GV Game(object v) => v is NV n ? new GV(n.X, n.Y, n.Z) : (GV)v;
        private static void Same(float actual, float expected)
        {
            components++;
            maximumError = Math.Max(maximumError, Math.Abs(actual - expected));
            if (!float.IsFinite(actual) || !float.IsFinite(expected)
                || Math.Abs(actual - expected) > 2e-5f * Math.Max(1, Math.Abs(expected)))
                throw new InvalidOperationException("Anti-air component differs: " + actual + " != " + expected);
        }
        private static void Vector(GV actual, GV expected)
        { Same(actual.x, expected.x); Same(actual.y, expected.y); Same(actual.z, expected.z); }
        private static ThingDef CloneDefinition(ThingDef original, Type type)
        {
            var copy = (ThingDef)typeof(object).GetMethod("MemberwiseClone", All).Invoke(original, null);
            copy.thingClass = type; return copy;
        }

        internal static void Run(string output, Action<bool, string> check)
        {
            DistanceChecks();
            check(true, distances + " segment-to-origin distances and " + decisions + " collision boundary decisions match frozen game-vector calculations");
            Map map = Find.CurrentMap;
            ThingDef turretDef = DefDatabase<ThingDef>.AllDefsListForReading.Single(d => d.comps?.OfType<CompProperties_MannedAntiAir>().Any() == true);
            var turret = (Building_TurretGun)ThingMaker.MakeThing(turretDef, GenStuff.DefaultStuffFor(turretDef));
            IntVec3 location = CellFinder.RandomClosewalkCellNear(map.Center, map, 20);
            GenSpawn.Spawn(turret, location, map);
            var native = turret.GetComp<CompMannedAntiAir>();
            var props = native.PropsForPatches;
            var oldProps = new Frozen.CompProperties_MannedAntiAir();
            foreach (var field in typeof(CompProperties_MannedAntiAir).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
                oldProps.GetType().GetField(field.Name).SetValue(oldProps, field.GetValue(props));
            oldProps.interceptDirectProjectiles = props.interceptDirectProjectiles = true;
            var old = new Frozen.CompMannedAntiAir { parent = turret, props = oldProps };
            ThingDef projectileDef = DefDatabase<ThingDef>.GetNamed("Bullet_BoltActionRifle");
            var probeDef = CloneDefinition(projectileDef, typeof(PositionProbe));
            var threat = (PositionProbe)ThingMaker.MakeThing(probeDef);
            GenSpawn.Spawn(threat, location + new IntVec3(6, 0, 0), map);
            Set(native, "selectedThreat", threat); Set(old, "selectedThreat", threat);
            try
            {
                LeadChecks(native, old, threat);
                TrajectoryChecks(native, old, projectileDef);
                RegistryChecks(native, old, threat, probeDef);
            }
            finally
            {
                if (!threat.Destroyed) threat.Destroy();
                turret.Destroy();
            }
            check(true, leads + " actual turret/projectile lead positions and clamped target cells match frozen calculation");
            check(true, trajectories + " actual projectile miss trajectories preserve destination, flight ticks and lifetime");
            check(true, registryCases + " actual moving-projectile registry traces preserve collision attempts, single-shot resolution and cleanup");
            Type state = Registry.GetNestedType("RoundState", BindingFlags.NonPublic);
            check(state.GetField("previousRoundPosition").FieldType == typeof(NV) && state.GetField("previousThreatPosition").FieldType == typeof(NV),
                "anti-air registry owns native previous positions");
            MethodInfo launch = AccessTools.Method(typeof(Projectile), nameof(Projectile.Launch), new[] {
                typeof(Thing), typeof(GV), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) });
            check(Harmony.GetPatchInfo(launch)?.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(Patch_Projectile_Launch_MarkAntiAirRound)) == true,
                "anti-air launch patch still binds the current game-vector callback");
            File.WriteAllText(Path.Combine(output, "native-anti-air.json"), JsonSerializer.Serialize(new {
                Distances = distances, CollisionDecisions = decisions, Leads = leads, MissTrajectories = trajectories,
                RegistryTraces = registryCases, Components = components, MaximumFloatError = maximumError, Baseline = "852f53f9"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void DistanceChecks()
        {
            var native = (Func<NV, NV, float>)Registry.GetMethod("DistanceToOriginSquared", All).CreateDelegate(typeof(Func<NV, NV, float>));
            var old = (Func<GV, GV, float>)typeof(Frozen.AntiAirRoundRegistry).GetMethod("DistanceToOriginSquared", All).CreateDelegate(typeof(Func<GV, GV, float>));
            void Compare(GV start, GV end)
            {
                float a = native(Native(start), Native(end)), e = old(start, end);
                Same(a, e); distances++;
                float radius = MathF.Sqrt(e);
                foreach (float r in new[] { .1f, 1.25f, 3.5f, radius, MathF.BitDecrement(radius), MathF.BitIncrement(radius) })
                {
                    if ((a <= r * r) != (e <= r * r)) throw new InvalidOperationException("Anti-air collision threshold changed: " + start + " -> " + end + " radius=" + r);
                    decisions++;
                }
            }
            float[] edge = { -100f, -1.25f, -.010001f, -.01f, -.009999f, -0f, .000001f, .009999f, .01f, .010001f, 1.25f, 100f };
            foreach (float x in edge)
                foreach (float z in edge)
                    foreach (float dx in new[] { 0f, .009999f, .01f, .010001f, -3f, 7f })
                        foreach (float dz in new[] { 0f, .009999f, .01f, .010001f, -2f, 5f })
                            Compare(new GV(x, 0, z), new GV(x + dx, 0, z + dz));
            var random = new Random(170852);
            for (int i = 0; i < 20000; i++)
            {
                GV start = new GV((float)(random.NextDouble() * 200 - 100), 0, (float)(random.NextDouble() * 200 - 100));
                GV end = new GV((float)(random.NextDouble() * 200 - 100), 0, (float)(random.NextDouble() * 200 - 100));
                Compare(start, end);
            }
        }

        private static void LeadChecks(CompMannedAntiAir native, Frozen.CompMannedAntiAir old, PositionProbe target)
        {
            MethodInfo current = typeof(CompMannedAntiAir).GetMethod("ProjectileLeadPosition", All), frozen = old.GetType().GetMethod("ProjectileLeadPosition", All);
            GV center = native.parent.DrawPos;
            foreach (GV offset in new[] { GV.zero, new GV(.000001f, 0, 0), new GV(2, 0, 3), new GV(-8, 0, 11), new GV(30, 4, -20) })
                foreach (GV travel in new[] { GV.zero, new GV(50, 0, 0), new GV(-50, -4, 80), new GV(2, 3, -.5f) })
                    foreach (int ticks in new[] { -1, 0, 1, 12, 60, 600 })
                    {
                        target.Current = center + offset;
                        Set(target, "destination", target.Current + travel); Set(target, "ticksToImpact", ticks);
                        Vector(Game(current.Invoke(native, new object[] { target })), (GV)frozen.Invoke(old, new object[] { target }));
                        LocalTargetInfo a = native.CurrentLeadTarget(), e = old.CurrentLeadTarget();
                        if (a.IsValid != e.IsValid || a.Cell != e.Cell) throw new InvalidOperationException("Anti-air lead target cell changed.");
                        leads++;
                    }
        }

        private static void TrajectoryChecks(CompMannedAntiAir native, Frozen.CompMannedAntiAir old, ThingDef definition)
        {
            var a = (Projectile)ThingMaker.MakeThing(definition); var e = (Projectile)ThingMaker.MakeThing(definition);
            foreach (GV start in new[] { GV.zero, new GV(10.125f, .3f, -2.5f) })
                foreach (GV delta in new[] { GV.zero, new GV(.009999f, 0, 0), new GV(.01f, 0, 0), new GV(.010001f, 0, 0), new GV(2, 3, -.5f) })
                    foreach (float range in new[] { -1f, .1f, 1f, 2f, 3.25f })
                    {
                        native.PropsForPatches.missTrajectoryRangeFactor = old.PropsForPatches.missTrajectoryRangeFactor = range;
                        foreach (var p in new[] { a, e })
                        { Set(p, "origin", start); Set(p, "destination", start + delta); Set(p, "ticksToImpact", 17); Set(p, "lifetime", 19); }
                        native.ConfigureMissTrajectory(a); old.ConfigureMissTrajectory(e);
                        Vector((GV)Get(a, "destination"), (GV)Get(e, "destination"));
                        if (!Equals(Get(a, "ticksToImpact"), Get(e, "ticksToImpact")) || !Equals(Get(a, "lifetime"), Get(e, "lifetime")))
                            throw new InvalidOperationException("Miss trajectory timing changed.");
                        trajectories++;
                    }
        }

        private static void RegistryChecks(CompMannedAntiAir native, Frozen.CompMannedAntiAir old, PositionProbe threat, ThingDef definition)
        {
            Map map = native.parent.Map;
            var harmony = new Harmony("Helodrace.Tests.NativeAntiAirRegistry");
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(NativeAntiAirChecks), nameof(ResolveProbe)));
            harmony.Patch(AccessTools.Method(typeof(CompMannedAntiAir), nameof(CompMannedAntiAir.TryResolveAntiAirImpact)), prefix: prefix);
            harmony.Patch(AccessTools.Method(typeof(Frozen.CompMannedAntiAir), nameof(Frozen.CompMannedAntiAir.TryResolveAntiAirImpact)), prefix: prefix);
            try
            {
                captureResolve = true;
                foreach (float radius in new[] { -.1f, .1f, 1.25f, 3.5f })
                    foreach (float height in new[] { 0f, 25f })
                        foreach (bool outcome in new[] { false, true })
                            foreach (GV end in new[] { new GV(3, 0, 0), new GV(3, 0, 1.25f), new GV(3, 0, MathF.BitIncrement(1.25f)), new GV(-2, 0, 5) })
                            {
                                native.PropsForPatches.interceptCollisionRadius = old.PropsForPatches.interceptCollisionRadius = radius;
                                var a = (PositionProbe)ThingMaker.MakeThing(definition); var e = (PositionProbe)ThingMaker.MakeThing(definition);
                                GenSpawn.Spawn(a, native.parent.Position, map); GenSpawn.Spawn(e, native.parent.Position, map);
                                try
                                {
                                    nativeCalls = frozenCalls = 0; resolveResult = outcome;
                                    GV t0 = native.parent.DrawPos + new GV(8, 0, 0);
                                    threat.Current = t0; a.Current = e.Current = t0 + new GV(-3, height, 0);
                                    Set(native, "selectedThreat", threat); Set(old, "selectedThreat", threat);
                                    Mark(a, native); Frozen.AntiAirRoundRegistry.Mark(e, old);
                                    threat.Current = t0 + new GV(2, 0, -1); a.Current = e.Current = threat.Current + end + new GV(0, height, 0);
                                    Tick(a); Frozen.AntiAirRoundRegistry.Tick(e); CompareState(a, e);
                                    Tick(a); Frozen.AntiAirRoundRegistry.Tick(e); CompareState(a, e);
                                    a.Current = e.Current = threat.Current;
                                    Tick(a); Frozen.AntiAirRoundRegistry.Tick(e); CompareState(a, e);
                                    if (nativeCalls != frozenCalls || nativeCalls > 1) throw new InvalidOperationException("Collision resolution count changed.");
                                    registryCases++;
                                }
                                finally { Remove(a); Frozen.AntiAirRoundRegistry.Remove(e); a.Destroy(); e.Destroy(); }
                            }
                // A registered but despawned projectile must be removed, and
                // invalid threats must consume the same single attempt.
                foreach (bool despawnRound in new[] { false, true })
                {
                    var a = (PositionProbe)ThingMaker.MakeThing(definition); var e = (PositionProbe)ThingMaker.MakeThing(definition);
                    GenSpawn.Spawn(a, native.parent.Position, map); GenSpawn.Spawn(e, native.parent.Position, map);
                    Set(native, "selectedThreat", threat); Set(old, "selectedThreat", threat);
                    Mark(a, native); Frozen.AntiAirRoundRegistry.Mark(e, old);
                    if (despawnRound) { a.DeSpawn(); e.DeSpawn(); } else threat.DeSpawn();
                    Tick(a); Frozen.AntiAirRoundRegistry.Tick(e); CompareState(a, e);
                    Remove(a); Frozen.AntiAirRoundRegistry.Remove(e); a.Destroy(); e.Destroy();
                    if (!threat.Spawned) GenSpawn.Spawn(threat, native.parent.Position + new IntVec3(6, 0, 0), map);
                    registryCases++;
                }
            }
            finally { captureResolve = false; harmony.UnpatchAll(harmony.Id); }
        }
        private static void CompareState(Projectile a, Projectile e)
        {
            if (Rounds.Contains(a.thingIDNumber) != OldRounds.Contains(e.thingIDNumber)) throw new InvalidOperationException("Registry removal changed.");
            if (!Rounds.Contains(a.thingIDNumber)) return;
            object x = Rounds[a.thingIDNumber], y = OldRounds[e.thingIDNumber];
            Vector(Game(Get(x, "previousRoundPosition")), (GV)Get(y, "previousRoundPosition"));
            Vector(Game(Get(x, "previousThreatPosition")), (GV)Get(y, "previousThreatPosition"));
            if (!Equals(Get(x, "attemptedIntercept"), Get(y, "attemptedIntercept"))) throw new InvalidOperationException("Intercept attempt changed.");
        }
    }
}
