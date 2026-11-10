using System;
using System.Collections.Generic;
using System.Numerics;
using Helodrace.Ballistics;

// Regression checks for the game-independent ballistics core (design doc phase 1).
internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static void Near(double expected, double actual, double tolerance, string message) =>
        Check(Math.Abs(expected - actual) <= tolerance, message + " expected " + expected + " got " + actual);

    private static void NearVector(Vector3 expected, Vector3 actual, string message) =>
        Check(Vector3.Distance(expected, actual) < 1e-4f, message + " expected " + expected + " got " + actual);

    private static int Main(string[] args)
    {
        Frames();
        Angles();
        Surfaces();
        SphereImpactDistribution();
        EllipsoidImpacts();
        RandomStreams();
        Model();
        Pipeline();
        Trace();
        KineticTests.Run(args.Length > 0 ? args[0] : null, Check, Near);
        HelmetTests.Run(Check, Near);
        VehicleTests.Run(Check, Near);
        ShapedChargeTests.Run(Check, Near);
        InteriorTests.Run(Check, Near);
        BlastTests.Run(Check, Near);
        AmmoTests.Run(Check, Near);
        OrientationTests.Run(args.Length > 0 ? args[0] : null, Check, Near);
        Console.WriteLine("BallisticsCore: " + checks + " checks passed");
        return 0;
    }

    private static void Frames()
    {
        var north = new BallisticFrame(Vector3.Zero, 0f);
        NearVector(Vector3.UnitZ, north.Forward, "Yaw 0 faces north");
        NearVector(Vector3.UnitX, north.Right, "Yaw 0 right is east");
        var east = new BallisticFrame(new Vector3(10, 0, 5), 90f);
        NearVector(Vector3.UnitX, east.Forward, "Yaw 90 faces east (clockwise)");
        NearVector(-Vector3.UnitZ, east.Right, "Yaw 90 right is south");
        NearVector(new Vector3(12, 1, 5), east.ToWorldPoint(new Vector3(0, 1, 2)), "Local forward maps to east");
        var hull = new BallisticFrame(new Vector3(3, 0, -4), 37f);
        var point = new Vector3(-1.5f, 0.7f, 2.25f);
        NearVector(point, hull.ToLocalPoint(hull.ToWorldPoint(point)), "Point round trip");
        NearVector(point, hull.ToLocalDirection(hull.ToWorldDirection(point)), "Direction round trip");
        Near(330f, new BallisticFrame(Vector3.Zero, -30f).YawDegrees, 1e-4, "Yaw normalized");

        // Turret 90 deg right on a north hull faces east; its pivot offset rotates with the hull.
        var tank = new BallisticFrame(Vector3.Zero, 90f);
        var turret = tank.Child(new Vector3(0, 1, 1), 90f);
        NearVector(new Vector3(1, 1, 0), turret.Origin, "Turret pivot follows hull yaw");
        NearVector(-Vector3.UnitZ, turret.Forward, "Turret yaw composes with hull yaw");
        // A crew seat in turret coordinates moves when the hull turns.
        Vector3 seat = new Vector3(0.5f, 0, -0.5f);
        Vector3 before = turret.ToWorldPoint(seat);
        Vector3 after = new BallisticFrame(Vector3.Zero, 180f).Child(new Vector3(0, 1, 1), 90f).ToWorldPoint(seat);
        Check(Vector3.Distance(before, after) > 0.5f, "Interior points rotate with the vehicle");
    }

    private static void Angles()
    {
        Near(-20f, BallisticGeometry.DeltaDegrees(10f, 350f), 1e-4, "Signed shortest delta");
        Check(BallisticGeometry.TryObliquity(-Vector3.UnitZ, Vector3.UnitZ, out float head), "Head-on hit");
        Near(0, head, 1e-6, "Head-on obliquity is zero");
        Vector3 sixty = BallisticGeometry.PlateNormal(0f, 60f);
        Check(BallisticGeometry.TryObliquity(-Vector3.UnitZ, sixty, out float sloped), "Hit on 60 deg plate");
        Near(60f, sloped * BallisticGeometry.RadToDeg, 1e-3, "Flat shot on 60 deg slope");
        Near(0.2, BallisticGeometry.LineOfSightThickness(0.1f, sloped), 1e-5, "LOS doubles at 60 deg");
        Check(!BallisticGeometry.TryObliquity(Vector3.UnitZ, Vector3.UnitZ, out _), "Receding shot cannot hit");
        Check(!BallisticGeometry.TryObliquity(Vector3.UnitX, Vector3.UnitZ, out _), "Tangent shot cannot hit");
        float grazing = BallisticGeometry.LineOfSightThickness(0.1f, MathF.PI / 2f);
        Check(float.IsFinite(grazing) && grazing <= 0.1f / BallisticGeometry.MinimumCosine + 1e-3f, "Grazing LOS is capped");

        NearVector(Vector3.UnitZ, BallisticGeometry.TravelDirection(0f, 0f), "Flat shot north");
        NearVector(-Vector3.UnitY, BallisticGeometry.TravelDirection(123f, 90f), "Vertical drop");
        Vector3 dive = BallisticGeometry.TravelDirection(90f, 30f);
        Near(-0.5, dive.Y, 1e-5, "30 deg descent");
        Near(1, dive.Length(), 1e-5, "Travel is unit length");
        Near(45f, BallisticGeometry.DescentDegrees(2f, 1f, 1f), 1e-4, "Descent from heights");
        Near(0f, BallisticGeometry.DescentDegrees(1.4f, 1.4f, 30f), 1e-4, "Level fire");

        // Top attack is not a separate damage type: the same geometry hits the roof.
        Vector3 roof = BallisticGeometry.PlateNormal(0f, 90f);
        NearVector(Vector3.UnitY, roof, "Roof normal faces up");
        Check(!BallisticGeometry.TryObliquity(BallisticGeometry.TravelDirection(180f, 0f), roof, out _), "Flat fire misses roof");
        Check(BallisticGeometry.TryObliquity(BallisticGeometry.TravelDirection(180f, 45f), roof, out float top), "Diving fire hits roof");
        Near(45f, top * BallisticGeometry.RadToDeg, 1e-3, "Dive angle sets roof obliquity");
    }

    private static void Surfaces()
    {
        Vector3 incoming = Vector3.Normalize(new Vector3(1, 0, -1));
        Vector3 reflected = BallisticGeometry.Reflect(incoming, Vector3.UnitZ);
        NearVector(Vector3.Normalize(new Vector3(1, 0, 1)), reflected, "Mirror reflection");
        Near(0, BallisticGeometry.Reflect(incoming, new Vector3(0, 0, 3)).Length() - 1, 1e-5, "Reflection keeps length");

        // A 2 x 2 front plate at z = 1, counter-clockwise seen from the front (+Z side).
        var plate = new List<Vector3> { new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1) };
        NearVector(Vector3.UnitZ, BallisticGeometry.PolygonNormal(plate), "Newell normal points outward");
        Check(BallisticGeometry.RayConvexPolygon(new Vector3(0.5f, 0.5f, 5), -Vector3.UnitZ, plate, out float d, out Vector3 n),
            "Ray hits plate");
        Near(4, d, 1e-5, "Hit distance");
        Check(BallisticGeometry.TryObliquity(-Vector3.UnitZ, n, out _), "Hit faces the shot");
        Check(!BallisticGeometry.RayConvexPolygon(new Vector3(1.5f, 0, 5), -Vector3.UnitZ, plate, out _, out _), "Ray misses beside plate");
        Check(!BallisticGeometry.RayConvexPolygon(new Vector3(0, 0, 5), Vector3.UnitZ, plate, out _, out _), "Ray pointing away misses");
        Check(!BallisticGeometry.RayConvexPolygon(new Vector3(0, 0, 5), -Vector3.UnitZ, new List<Vector3> { Vector3.Zero, Vector3.UnitX }, out _, out _),
            "Degenerate polygon refused");
    }

    private static void SphereImpactDistribution()
    {
        // Uniform parallel beam on a sphere: P(theta < x) = sin^2 x, E[theta] = pi / 4. [physical]
        const int samples = 200000;
        var rng = new BallisticRng(12345);
        Vector3 travel = Vector3.Normalize(new Vector3(0.3f, -0.2f, -1f));
        double sum = 0;
        var thresholds = new[] { 15.0, 30.0, 45.0, 60.0, 75.0 };
        var below = new int[thresholds.Length];
        for (int i = 0; i < samples; i++)
        {
            SurfaceImpact hit = BallisticGeometry.SampleEllipsoidImpact(new Vector3(2, 3, 4), new Vector3(0.11f), travel,
                rng.NextFloat(), rng.NextFloat());
            if (i < 2000)
            {
                Near(0.11, Vector3.Distance(hit.Point, new Vector3(2, 3, 4)), 1e-4, "Sphere hit lies on surface");
                Check(Vector3.Dot(hit.OutwardNormal, travel) <= 1e-5f, "Sphere hit faces the shot");
            }
            double degrees = hit.Obliquity * BallisticGeometry.RadToDeg;
            sum += hit.Obliquity;
            for (int t = 0; t < thresholds.Length; t++)
            {
                if (degrees < thresholds[t]) below[t]++;
            }
        }
        Near(Math.PI / 4, sum / samples, 0.004, "Mean sphere obliquity is pi/4");
        for (int t = 0; t < thresholds.Length; t++)
        {
            double expected = Math.Pow(Math.Sin(thresholds[t] * Math.PI / 180), 2);
            Near(expected, below[t] / (double)samples, 0.005, "Sphere obliquity CDF at " + thresholds[t]);
        }
    }

    private static void EllipsoidImpacts()
    {
        var rng = new BallisticRng(99);
        Vector3 radii = new Vector3(0.12f, 0.1f, 0.14f);
        Vector3 travel = Vector3.Normalize(new Vector3(-1f, -0.4f, 0.2f));
        double sumX = 0;
        const int samples = 50000;
        for (int i = 0; i < samples; i++)
        {
            SurfaceImpact hit = BallisticGeometry.SampleEllipsoidImpact(Vector3.Zero, radii, travel, rng.NextFloat(), rng.NextFloat());
            Vector3 q = hit.Point / radii;
            Near(1, q.LengthSquared(), 1e-4, "Ellipsoid hit lies on surface");
            Check(Vector3.Dot(hit.OutwardNormal, travel) <= 1e-5f, "Ellipsoid hit faces the shot");
            Near(1, hit.OutwardNormal.Length(), 1e-4, "Normal is unit");
            sumX += hit.Point.X;
        }
        // Shot travels toward -X, so every hit is on the +X-facing half; the mean lies on that side.
        Check(sumX / samples > 0.05, "Hits land on the side facing the shooter");

        // Helmet crown: a steep dive meets the top of a helmet head-on, a flat shot meets it at grazing angles.
        SurfaceImpact crownDive = BallisticGeometry.SampleEllipsoidImpact(Vector3.Zero, radii, -Vector3.UnitY, 0f, 0f);
        Near(0, crownDive.Obliquity, 1e-4, "Beam centre on the crown is head-on");
        SurfaceImpact rim = BallisticGeometry.SampleEllipsoidImpact(Vector3.Zero, radii, -Vector3.UnitY, 0.999f, 0.3f);
        Check(rim.Obliquity * BallisticGeometry.RadToDeg > 80f, "Beam edge is grazing");
    }

    private static void RandomStreams()
    {
        var a = new BallisticRng(42);
        var b = new BallisticRng(42);
        for (int i = 0; i < 100; i++) Check(a.NextULong() == b.NextULong(), "Same seed, same sequence");
        var c = new BallisticRng(43);
        Check(new BallisticRng(42).NextULong() != c.NextULong(), "Different seed, different sequence");

        var rng = new BallisticRng(7);
        double total = 0;
        for (int i = 0; i < 100000; i++)
        {
            float value = rng.NextFloat();
            Check(value >= 0f && value < 1f, "NextFloat in [0, 1)");
            total += value;
        }
        Near(0.5, total / 100000, 0.005, "NextFloat mean");
        int hits = new BallisticRng(8).Binomial(10000, 0.3f);
        Near(3000, hits, 200, "Binomial approximation mean");
        Check(new BallisticRng(1).Binomial(0, 0.5f) == 0 && new BallisticRng(1).Binomial(5, 1f) == 5, "Binomial bounds");

        ulong seed = BallisticSeed.ForImpact(1001, 60000, 77);
        Check(seed == BallisticSeed.ForImpact(1001, 60000, 77), "Impact seed reproducible");
        Check(seed != BallisticSeed.ForImpact(1002, 60000, 77), "Projectile changes seed");
        Check(seed != BallisticSeed.ForImpact(1001, 60001, 77), "Tick changes seed");
        Check(seed != BallisticSeed.ForImpact(1001, 60000, 78), "Target changes seed");
        Check(seed != BallisticSeed.ForImpact(77, 60000, 1001), "Projectile and target are not interchangeable");
        Check(BallisticSeed.ForChild(seed, 0, 5) != BallisticSeed.ForChild(seed, 1, 5), "Child index changes seed");
        Check(BallisticSeed.ForChild(seed, 0, 5) != BallisticSeed.ForChild(seed, 0, 6), "Child target changes seed");
        Check(BallisticSeed.StableHash("abc") == 0xE71FA2190541574BUL, "FNV-1a reference value");
        Check(BallisticSeed.Stream(seed, "A").NextULong() != BallisticSeed.Stream(seed, "B").NextULong(), "Streams differ by id");
    }

    private static void Model()
    {
        var round = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.004f, new Vector3(0, 0, -900), 0.023f, 0.0057f);
        Near(1620, round.KineticEnergyJ, 0.5, "0.5 m v^2");
        Near(900, round.Speed, 1e-3, "Speed");
        NearVector(-Vector3.UnitZ, round.Direction, "Direction");
        Check(round.Kind == PenetratorKind.Kinetic && round.Condition == PenetratorCondition.Intact, "Bullet starts intact kinetic");
        Check(PenetrationMechanisms.InitialKind(PenetrationMechanism.Tandem) == PenetratorKind.Jet, "Tandem is a jet");
        Check(PenetrationMechanisms.InitialKind(PenetrationMechanism.HighExplosiveSquashHead) == PenetratorKind.Blast, "HESH is blast");
        Check(PenetrationMechanisms.InitialKind(PenetrationMechanism.KineticLongRod) == PenetratorKind.Kinetic, "APFSDS is kinetic");
        Check(PenetratorState.Projectile(PenetrationMechanism.Shot, -1, Vector3.UnitX, -1, -1).MassKg == 0, "Negative mass clamped");
        Check(default(PenetratorState).IsSpent, "Empty penetrator is spent");

        var frame = new BallisticFrame(Vector3.Zero, 90f);
        PenetratorState local = round.InFrame(frame);
        NearVector(round.Velocity, local.ToWorld(frame).Velocity, "Penetrator frame round trip");
        Near(round.KineticEnergyJ, local.KineticEnergyJ, 0.01, "Frame change keeps energy");

        var impact = new ImpactEvent(5, 10, 1, 2, "Bullet", PenetrationMechanism.KineticBullet, Vector3.Zero, round);
        ImpactEvent child = impact.Child(0, 3, PenetrationMechanism.Fragment, Vector3.One, round);
        Check(child.Depth == 1 && child.TargetId == 3 && child.Seed != impact.Seed && child.Tick == impact.Tick, "Child impact derivation");
        Check(new ImpactEvent(1, 1, 1, 1, null, PenetrationMechanism.Shot, Vector3.Zero, round).ProjectileDefName == string.Empty,
            "Null def name normalized");
    }

    private sealed class Stage : IImpactStage
    {
        public string Id { get; }
        private readonly Func<ImpactContext, bool> applies;
        private readonly Action<ImpactContext> run;
        public readonly List<string> Log;

        public Stage(string id, List<string> log, Action<ImpactContext> run = null, Func<ImpactContext, bool> applies = null)
        {
            Id = id;
            Log = log;
            this.run = run;
            this.applies = applies;
        }

        public bool Applies(ImpactContext context) => applies?.Invoke(context) ?? true;

        public void Run(ImpactContext context)
        {
            Log.Add(Id);
            run?.Invoke(context);
        }
    }

    private static ImpactEvent SampleEvent(int depth = 0)
    {
        var round = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.004f, new Vector3(0, 0, -900), 0.023f, 0.0057f);
        var impact = new ImpactEvent(BallisticSeed.ForImpact(1, 2, 3), 2, 9, 3, "HD_Bullet_556M855A1",
            PenetrationMechanism.KineticBullet, new Vector3(1, 1.2f, 2), round);
        for (int i = 0; i < depth; i++) impact = impact.Child(i, 3, PenetrationMechanism.Fragment, impact.WorldPoint, round);
        return impact;
    }

    private static void Pipeline()
    {
        var log = new List<string>();
        float rolledInB = -1;
        var pipeline = new ImpactPipelineBuilder()
            .Add(new Stage("A", log, c => c.Trace.Value("a", 1, EvidenceGrade.Physical)))
            .Add(new Stage("C", log, c => c.Outcome.Result = ImpactResult.Stopped))
            .InsertAfter("A", new Stage("B", log, c => { var r = c.StageRandom(); rolledInB = r.NextFloat(); c.Trace.Value("roll", rolledInB, EvidenceGrade.Game); }))
            .InsertBefore("A", new Stage("Skip", log, applies: _ => false))
            .Build();
        var context = new ImpactContext();
        context.Reset(SampleEvent(), BallisticFrame.World, null);
        pipeline.Run(context);
        Check(string.Join(",", log) == "A,B,C", "Stage order and skipping: " + string.Join(",", log));
        Check(context.Trace.Stages.Count == 4 && context.Trace.Stages[0].Status == TraceStageStatus.Skipped, "Skipped stage traced");
        Check(context.Trace.Result == ImpactResult.Stopped, "Trace carries result");
        Check(context.CurrentStageId == null, "Stage id cleared after run");

        // A stage's random stream depends only on the event seed and its id: removing another stage
        // or running again must not change it.
        float first = rolledInB;
        var rerun = new ImpactContext();
        rerun.Reset(SampleEvent(), BallisticFrame.World, null);
        pipeline.Run(rerun);
        Check(rolledInB == first, "Same seed reproduces the roll");
        Check(rerun.Trace.ToJson() == context.Trace.ToJson(), "Same seed reproduces the whole trace");
        var reduced = new ImpactPipelineBuilder()
            .Add(new Stage("B", new List<string>(), c => { var r = c.StageRandom(); rolledInB = r.NextFloat(); }))
            .Build();
        var alone = new ImpactContext();
        alone.Reset(SampleEvent(), BallisticFrame.World, null);
        reduced.Run(alone);
        Check(rolledInB == first, "Other stages do not shift a stage's roll");

        // Halt marks remaining stages as not reached.
        var haltLog = new List<string>();
        var halting = new ImpactPipeline(new IImpactStage[]
        {
            new Stage("X", haltLog, c => c.Halt("certain stop")),
            new Stage("Y", haltLog)
        });
        var halted = new ImpactContext();
        halted.Reset(SampleEvent(), BallisticFrame.World, null);
        halting.Run(halted);
        Check(string.Join(",", haltLog) == "X", "Halt stops later stages");
        Check(halted.Trace.Find("Y").Status == TraceStageStatus.NotReached, "Not reached is traced");
        Check(halted.Trace.Find("X").Note.Contains("certain stop"), "Halt reason traced");

        // Depth limits for secondary impacts.
        var depthContext = new ImpactContext();
        depthContext.Reset(SampleEvent(), BallisticFrame.World, null);
        ImpactEvent child = depthContext.Event.Child(0, 4, PenetrationMechanism.Fragment, Vector3.Zero, depthContext.Penetrator);
        Check(depthContext.TryQueueChild(child, 2) && depthContext.ChildImpacts.Count == 1, "Child queued within depth");
        Check(!depthContext.TryQueueChild(SampleEvent(3), 2), "Child beyond depth refused");
        var deepLog = new List<string>();
        var deep = new ImpactContext();
        deep.Reset(SampleEvent(3), BallisticFrame.World, null);
        new ImpactPipeline(new IImpactStage[] { new Stage("Z", deepLog) }, 2).Run(deep);
        Check(deepLog.Count == 0 && deep.Trace.Warnings.Count == 1, "Pipeline refuses events beyond depth");

        // Reset clears pooled state.
        var pool = new ImpactContextPool();
        ImpactContext pooled = pool.Rent();
        pooled.Reset(SampleEvent(), BallisticFrame.World, null);
        pooled.Stack.Layers.Add(new ArmorLayer { MaterialId = "HD_Armor_RHA", ThicknessM = 0.01f });
        pooled.Outcome.Secondaries.Add(new SecondaryEvent { Kind = SecondaryEventKind.Spall });
        pooled.Halt("x");
        pool.Return(pooled);
        ImpactContext reused = pool.Rent();
        Check(ReferenceEquals(reused, pooled), "Pool reuses contexts");
        reused.Reset(SampleEvent(), BallisticFrame.World, null);
        Check(reused.Stack.IsEmpty && reused.Outcome.Secondaries.Count == 0 && !reused.Halted && reused.Trace.Stages.Count == 0,
            "Reset clears pooled state");

        // Exceptions are traced and rethrown.
        var failing = new ImpactPipeline(new IImpactStage[] { new Stage("F", new List<string>(), _ => throw new InvalidOperationException("bad layer")) });
        var failed = new ImpactContext();
        failed.Reset(SampleEvent(), BallisticFrame.World, null);
        bool threw = false;
        try { failing.Run(failed); } catch (InvalidOperationException) { threw = true; }
        Check(threw && failed.Trace.Find("F").Status == TraceStageStatus.Failed && failed.Trace.Find("F").Note.Contains("bad layer"),
            "Failure traced and rethrown");

        bool duplicate = false;
        try { new ImpactPipeline(new IImpactStage[] { new Stage("D", log), new Stage("D", log) }); } catch (ArgumentException) { duplicate = true; }
        Check(duplicate, "Duplicate stage ids refused");
        bool unknown = false;
        try { new ImpactPipelineBuilder().InsertAfter("Nope", new Stage("Q", log)); } catch (ArgumentException) { unknown = true; }
        Check(unknown, "Unknown anchor refused");
        Check(ImpactStageIds.Ordered.Length == 14 && new HashSet<string>(ImpactStageIds.Ordered).Count == 14, "Fourteen standard stages");
    }

    private static void Trace()
    {
        var context = new ImpactContext();
        context.Reset(SampleEvent(), BallisticFrame.World, null);
        new ImpactPipeline(new IImpactStage[]
        {
            new Stage(ImpactStageIds.ImpactGeometry, new List<string>(), c =>
            {
                c.Trace.Value("obliquityDeg", 61.5, EvidenceGrade.Physical);
                c.Trace.Value("rawLOS", double.PositiveInfinity, EvidenceGrade.Physical);
                c.Trace.Warn("coefficient \"source\" missing\n");
                c.Trace.Note("first");
                c.Trace.Note("second");
            })
        }).Run(context);
        string text = context.Trace.ToText();
        Check(text.Contains("HD_Bullet_556M855A1") && text.Contains("obliquityDeg=61.5[physical]") && text.Contains("first; second"),
            "Text trace: " + text);
        string json = context.Trace.ToJson();
        Check(json.Contains("\"grade\":\"physical\"") && json.Contains("\\\"source\\\" missing\\n"),
            "JSON trace escapes: " + json);
        Check(json.Contains("\"value\":\"Infinity\""), "Non-finite values quoted");
        Check(json.StartsWith("{\"seed\":\"") && json.EndsWith("]}"), "JSON shape");

        var buffer = new ImpactTraceBuffer(3);
        var traces = new ImpactTrace[5];
        for (int i = 0; i < 5; i++)
        {
            traces[i] = new ImpactTrace { Tick = i };
            buffer.Add(traces[i]);
        }
        Check(buffer.Count == 3 && buffer[0].Tick == 4 && buffer[1].Tick == 3 && buffer[2].Tick == 2, "Ring buffer newest first");
        bool outOfRange = false;
        try { _ = buffer[3]; } catch (ArgumentOutOfRangeException) { outOfRange = true; }
        Check(outOfRange, "Ring buffer bounds");
        buffer.Clear();
        Check(buffer.Count == 0, "Ring buffer clear");
    }
}
