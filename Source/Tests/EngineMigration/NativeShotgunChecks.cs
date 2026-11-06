using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using RimWorld;
using Verse;
using GV = UnityEngine.Vector3;
using G2 = UnityEngine.Vector2;
using NV = System.Numerics.Vector3;
using N2 = System.Numerics.Vector2;

namespace Helodrace.Tests
{
    internal static class NativeShotgunChecks
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Product = typeof(ShotgunShell).Assembly;
        private static readonly Type Ballistics = Product.GetType("Helodrace.ShotgunBallistics", true);
        private static int components, silhouettes, intercepts, samples, offsets, poses;
        private static float maximumError;
        public sealed class SavedShell
        {
            public int Id { get; set; }
            public int[] Origin { get; set; }
            public int[] Target { get; set; }
            public int[] Aim { get; set; }
            public int[] Pellets { get; set; }
            public int[][] Slopes { get; set; }
            public int[][] BaseOrigins { get; set; }
            public int[][] BaseDestinations { get; set; }
            public int[] Remaining { get; set; }
        }
        private static T Method<T>(string name) where T : Delegate => (T)Ballistics.GetMethod(name, All).CreateDelegate(typeof(T));
        private static object Get(object v, string name)
        {
            for (Type t = v.GetType(); t != null; t = t.BaseType)
                if (t.GetField(name, All | BindingFlags.DeclaredOnly) is FieldInfo f) return f.GetValue(v);
            throw new MissingFieldException(name);
        }
        private static void Set(object v, string name, object value)
        {
            for (Type t = v.GetType(); t != null; t = t.BaseType)
                if (t.GetField(name, All | BindingFlags.DeclaredOnly) is FieldInfo f) { f.SetValue(v, value); return; }
            throw new MissingFieldException(name);
        }
        private static object Call(object v, string name, params object[] args) => v.GetType().GetMethod(name, All).Invoke(v, args);
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static N2 Native(G2 v) => new N2(v.x, v.y);
        private static void Same(float a, float e)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(a - e));
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1, Math.Abs(e)))
                throw new InvalidOperationException("Shotgun component differs: " + a + " != " + e);
        }
        private static void Vector(NV a, GV e) { Same(a.X, e.x); Same(a.Y, e.y); Same(a.Z, e.z); }
        private static void Vector(N2 a, G2 e) { Same(a.X, e.x); Same(a.Y, e.y); }
        private static int[] Bits(NV v) => new[] { BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y), BitConverter.SingleToInt32Bits(v.Z) };
        private static int[] Bits(N2 v) => new[] { BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y) };

        internal static void Create(string output, Action<bool, string> check)
        {
            BallisticChecks(); ShellChecks(); MotionChecks();
            check(true, silhouettes + " body/ellipse edge decisions and " + intercepts + " quadratic/linear/unreachable intercept times match frozen game-vector math");
            check(true, samples + " Gaussian spread samples preserve vector components and exact Verse RNG order");
            check(true, offsets + " shell forward/right/range/aim offsets match frozen geometry, including near-zero launch and negative/three-dimensional offsets");
            check(true, poses + " actual pawn motion/lead/pose checks preserve path progress, stopping clamp and same-tick pose reuse");
            check(typeof(ShotgunShell).GetField("origin").FieldType == typeof(NV) && typeof(ShotgunShell).GetField("aimError").FieldType == typeof(N2)
                && typeof(Projectile_ShotgunPellet).GetField("slope", All).FieldType == typeof(N2), "shell, cached pawn pose and pellet slope use native vectors internally");
            SaveFlights(output, check);
            File.WriteAllText(Path.Combine(output, "native-shotgun.json"), JsonSerializer.Serialize(new {
                Silhouettes = silhouettes, Intercepts = intercepts, Samples = samples, ShellOffsets = offsets, PoseChecks = poses,
                Components = components, MaximumFloatError = maximumError, Baseline = "e94e51db"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        private static void BallisticChecks()
        {
            var radii = Method<Func<float, bool, N2>>("BodyRadii"); var hits = Method<Func<N2, N2, bool>>("Hits");
            foreach (float size in new[] { -.1f, 0f, .001f, .02f, .125f, 1f, 8f, 100f })
                foreach (bool standing in new[] { false, true })
                {
                    N2 a = radii(size, standing); G2 e = Frozen.ShotgunBallistics.BodyRadii(size, standing); Vector(a, e);
                    foreach (float x in new[] { -1.00001f, -1f, -.5f, -0f, .5f, 1f, 1.00001f })
                        foreach (float y in new[] { -1.00001f, -1f, -.5f, -0f, .5f, 1f, 1.00001f })
                        {
                            var point = new G2(e.x * x, e.y * y);
                            if (hits(Native(point), a) != Frozen.ShotgunBallistics.Hits(point, e)) throw new InvalidOperationException("Shotgun ellipse boundary changed.");
                            silhouettes++;
                        }
                }
            var intercept = Method<Func<NV, NV, float, float>>("InterceptTicks");
            foreach (GV offset in new[] { GV.zero, new GV(.000001f, 4, 0), new GV(.01f, 4, 0), new GV(0, 0, 10), new GV(-3, -2, 7), new GV(200, 5, -100) })
                foreach (GV velocity in new[] { GV.zero, new GV(.1f, 0, 0), new GV(0, 1, -.1f), new GV(0, -4, 1), new GV(0, 0, 1.000001f), new GV(0, 0, 2), new GV(-3, 1, 7) })
                    foreach (float speed in new[] { -1f, 0f, .0001f, .1f, .999999f, 1f, 1.000001f, 10f })
                    { Same(intercept(Native(offset), Native(velocity), speed), Frozen.ShotgunBallistics.InterceptTicks(offset, velocity, speed)); intercepts++; }
            var sample = Method<Func<float, N2>>("Sample");
            foreach (float angle in new[] { -1f, 0f, .15f, .9f, 1f, 15f, 30f })
                for (int seed = 0; seed < 256; seed++)
                {
                    N2 a; G2 e; float ar, er;
                    Rand.PushState(seed); try { a = sample(angle); ar = Rand.Value; } finally { Rand.PopState(); }
                    Rand.PushState(seed); try { e = Frozen.ShotgunBallistics.Sample(angle); er = Rand.Value; } finally { Rand.PopState(); }
                    Vector(a, e); if (ar != er) throw new InvalidOperationException("Shotgun spread changed RNG order."); samples++;
                }
        }
        private static void ShellChecks()
        {
            var forward = (Func<ShotgunShell, NV>)typeof(ShotgunShell).GetProperty("Forward", All).GetGetMethod(true).CreateDelegate(typeof(Func<ShotgunShell, NV>));
            var right = (Func<ShotgunShell, NV>)typeof(ShotgunShell).GetProperty("Right", All).GetGetMethod(true).CreateDelegate(typeof(Func<ShotgunShell, NV>));
            var distance = (Func<ShotgunShell, NV, float>)typeof(ShotgunShell).GetMethod("DistanceTo", All).CreateDelegate(typeof(Func<ShotgunShell, NV, float>));
            var offset = (Func<ShotgunShell, NV, N2, N2>)typeof(ShotgunShell).GetMethod("OffsetAt", All).CreateDelegate(typeof(Func<ShotgunShell, NV, N2, N2>));
            foreach (GV origin in new[] { GV.zero, new GV(10.125f, .25f, -2.5f) })
                foreach (GV delta in new[] { GV.zero, new GV(.001f, 0, 0), new GV(.03162f, 0, 0), new GV(.03163f, 0, 0), new GV(1, 5, 10), new GV(-25, -2, 8) })
                    foreach (G2 aim in new[] { G2.zero, new G2(.2f, -.1f), new G2(1e-7f, -0f) })
                    {
                        var a = new ShotgunShell { origin = Native(origin), targetPoint = Native(origin + delta), aimError = Native(aim) };
                        var e = new Frozen.ShotgunShell { origin = origin, targetPoint = origin + delta, aimError = aim };
                        Vector(forward(a), e.Forward); Vector(right(a), e.Right); Same((float)typeof(ShotgunShell).GetProperty("AimDistance", All).GetValue(a), e.AimDistance);
                        foreach (float progress in new[] { -1f, 0f, 1e-6f, .25f, .5f, 1f, 1.00001f, 2f })
                            foreach (G2 slope in new[] { G2.zero, new G2(.01f, -.02f), new G2(.1f, .2f), new G2(-1e-7f, -0f) })
                            {
                                GV point = origin + delta * progress + new GV(.125f, 3, -.25f);
                                Same(distance(a, Native(point)), e.DistanceTo(point)); Vector(offset(a, Native(point), Native(slope)), e.OffsetAt(point, slope)); offsets++;
                            }
                    }
        }
        private static void MotionChecks()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            Map map = Find.CurrentMap; IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 12); GenSpawn.Spawn(pawn, cell, map);
            var path = pawn.pather;
            object moving = Get(path, "moving"), percentage = Get(path, "cachedMovePercentage"), destination = Get(path, "destination");
            var next = path.nextCell; float cost = path.nextCellCostTotal;
            var position = Method<Func<Pawn, NV>>("Position"); var velocity = Method<Func<Pawn, NV>>("Velocity"); var aim = Method<Func<Pawn, NV, float, NV>>("AimPoint");
            var a = new ShotgunShell(); var e = new Frozen.ShotgunShell();
            try
            {
                foreach (bool isMoving in new[] { false, true })
                    foreach (float progress in new[] { 0f, .25f, .5f, 1f })
                        foreach (float stepCost in new[] { 1f, 20f, 450f, 900f })
                        {
                            Set(path, "moving", isMoving); Set(path, "cachedMovePercentage", progress); Set(path, "destination", new LocalTargetInfo(cell + new IntVec3(1, 0, 0)));
                            path.nextCell = cell + new IntVec3(1, 0, 0); path.nextCellCostTotal = stepCost;
                            Vector(position(pawn), Frozen.ShotgunBallistics.Position(pawn)); Vector(velocity(pawn), Frozen.ShotgunBallistics.Velocity(pawn));
                            foreach (float speed in new[] { .01f, .1f, 1f, 10f })
                            { GV origin = cell.ToVector3Shifted() + new GV(0, 2, -10); Vector(aim(pawn, Native(origin), speed), Frozen.ShotgunBallistics.AimPoint(pawn, origin, speed)); }
                            object ap = Call(a, "PoseFor", pawn); var ep = e.PoseFor(pawn);
                            Vector((NV)Get(ap, "position"), ep.position); Vector((N2)Get(ap, "radii"), ep.radii);
                            // Repeated same-tick reads must keep the first physical
                            // pose even as the path-progress input changes.
                            Set(path, "cachedMovePercentage", 1f - progress);
                            object cached = Call(a, "PoseFor", pawn); var oldCached = e.PoseFor(pawn);
                            Vector((NV)Get(cached, "position"), oldCached.position);
                            ((IDictionary)Get(a, "poses")).Clear(); ((IDictionary)Get(e, "poses")).Clear(); poses++;
                        }
            }
            finally
            {
                Set(path, "moving", moving); Set(path, "cachedMovePercentage", percentage); Set(path, "destination", destination); path.nextCell = next; path.nextCellCostTotal = cost; pawn.Destroy();
            }
        }
        private static void SaveFlights(string output, Action<bool, string> check)
        {
            Map map = Find.CurrentMap; Thing launcher = ThingMaker.MakeThing(ThingDefOf.WoodLog); GenSpawn.Spawn(launcher, map.Center, map);
            Type launchType = Product.GetType("Helodrace.ShotgunLaunch", true), context = Product.GetType("Helodrace.ShotgunSpreadContext", true);
            FieldInfo launching = context.GetField("Launching", All); object previous = launching.GetValue(null);
            ThingDef def = DefDatabase<ThingDef>.GetNamed("HD_Bullet_12Gauge00Buck_Proj");
            var saved = new List<SavedShell>();
            try
            {
                for (int shellIndex = 0; shellIndex < 4; shellIndex++)
                {
                    object launch = Activator.CreateInstance(launchType, All, null, new object[] { null, 9, .9f }, null); launching.SetValue(null, launch);
                    var pellet = (Projectile_ShotgunPellet)ThingMaker.MakeThing(def); GenSpawn.Spawn(pellet, launcher.Position, map);
                    IntVec3 target = launcher.Position + new IntVec3(0, 0, 20 + shellIndex);
                    GV origin = launcher.DrawPos + new GV(.125f, shellIndex * .125f, -.25f);
                    Rand.PushState(170100 + shellIndex);
                    try { pellet.Launch(launcher, origin, target, target, ProjectileHitFlags.All); Call(launch, "LaunchRemaining"); } finally { Rand.PopState(); }
                    var shell = (ShotgunShell)Get(launch, "shell");
                    if (shell.projectiles.Count != 9 || shell.projectiles.Any(p => typeof(Projectile_ShotgunPellet).GetProperty("Shell", All).GetValue(p) != shell))
                        throw new InvalidOperationException("Nine actual pellets did not share their native shell.");
                    saved.Add(new SavedShell { Id = shell.id, Origin = Bits(shell.origin), Target = Bits(shell.targetPoint), Aim = Bits(shell.aimError),
                        Pellets = shell.projectiles.Select(p => p.thingIDNumber).ToArray(), Slopes = shell.projectiles.Select(p => Bits((N2)Get(p, "slope"))).ToArray(),
                        BaseOrigins = shell.projectiles.Select(p => Bits(Native((GV)Get(p, "origin")))).ToArray(),
                        BaseDestinations = shell.projectiles.Select(p => Bits(Native((GV)Get(p, "destination")))).ToArray(), Remaining = shell.projectiles.Select(p => (int)Get(p, "ticksToImpact")).ToArray() });
                }
            }
            finally { launching.SetValue(null, previous); }
            File.WriteAllText(Path.Combine(output, "shotgun-states.json"), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            check(saved.Count == 4, "four actual nine-pellet launches share native shell state and preserve continuous horizontal flight endpoints");
        }
        internal static void Verify(string output, Action<bool, string> check)
        {
            var saved = JsonSerializer.Deserialize<SavedShell[]>(File.ReadAllText(Path.Combine(output, "shotgun-states.json")));
            var component = Find.CurrentMap.GetComponent<MapComponent_ShotgunShells>();
            int pellets = 0;
            foreach (var item in saved)
            {
                var shell = ((IEnumerable)Get(component, "shells")).Cast<ShotgunShell>().Single(s => s.id == item.Id);
                if (!Bits(shell.origin).SequenceEqual(item.Origin) || !Bits(shell.targetPoint).SequenceEqual(item.Target) || !Bits(shell.aimError).SequenceEqual(item.Aim)
                    || !shell.projectiles.Select(p => p.thingIDNumber).SequenceEqual(item.Pellets)) throw new InvalidOperationException("Native shotgun shell did not round-trip exact bits/references.");
                for (int i = 0; i < item.Pellets.Length; i++)
                {
                    Projectile p = shell.projectiles[i];
                    if (!Bits((N2)Get(p, "slope")).SequenceEqual(item.Slopes[i]) || !Bits(Native((GV)Get(p, "origin"))).SequenceEqual(item.BaseOrigins[i])
                        || !Bits(Native((GV)Get(p, "destination"))).SequenceEqual(item.BaseDestinations[i]) || (int)Get(p, "ticksToImpact") != item.Remaining[i]
                        || Call(component, "ShellFor", p) != shell) throw new InvalidOperationException("Native pellet slope/trajectory/lookup changed after reload.");
                    pellets++;
                }
            }
            var xml = XDocument.Load(GenFilePaths.FilePathForSavedGame("ShotgunNativeState17"));
            var shellNodes = xml.Descendants("shotgunShells").Elements("li").ToArray();
            var pelletNodes = xml.Descendants().Where(n => (string)n.Attribute("Class") == typeof(Projectile_ShotgunPellet).FullName).ToArray();
            if (shellNodes.Length != 4 || pelletNodes.Length != 36 || shellNodes.Any(n => n.Element("origin")?.Element("x") == null || n.Element("originX") != null)
                || pelletNodes.Any(n => n.Element("pelletSlope")?.Element("x") == null || n.Element("pelletSlopeX") != null))
                throw new InvalidOperationException("Expected explicit native shotgun scalar-node geometry in the new save.");
            check(saved.Length == 4 && pellets == 36, "four native shells and 36 real pellets preserve exact vector/slope/trajectory bits, reference sharing and rebuilt map lookup before the first tick");
            check(true, "shotgun vectors use explicit native scalar nodes without an older Main vector/schema read path");
        }
    }
}
