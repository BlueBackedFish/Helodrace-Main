using System;
using System.Collections.Generic;
using System.Numerics;
using Helodrace.Ballistics;

// Phase 7: interior boxes, crew seats, spall bundles.
internal static class InteriorTests
{
    private static Action<bool, string> check;
    private static Action<double, double, double, string> near;

    public static void Run(Action<bool, string> checkFn, Action<double, double, double, string> nearFn)
    {
        check = checkFn;
        near = nearFn;
        Geometry();
        Trace();
        Spall();
        DebugCapture();
    }

    private static void DebugCapture()
    {
        var interior = new VehicleInterior();
        interior.Boxes.Add(new InteriorBox { Min = new Vector3(-10, -10, 2), Max = new Vector3(10, 10, 3) });
        var profile = new SpallProfile { Rays = 8 };
        var frame = new BallisticFrame(new Vector3(20, 0, 30), 37f);
        Vector3 origin = frame.ToWorldPoint(new Vector3(0, 0.5f, 0));
        var layers = new[] { new VehicleArmorLayer(VehicleTests.Rha(), 1f) };
        var seats = new List<SpallSeatHit>();
        var boxes = new List<SpallBoxHit>();
        var rays = new List<SpallDebugRay>();
        SpallTracer.Trace(interior, profile, origin, frame.Forward, 64, 200f, frame, frame,
            _ => layers, 17, seats, boxes);
        var baseline = boxes.ToArray();
        SpallTracer.Trace(interior, profile, origin, frame.Forward, 64, 200f, frame, frame,
            _ => layers, 17, seats, boxes, debugRays: rays);
        check(rays.Count == 8 && boxes.Count == baseline.Length, "Debug capture retains actual bundle rays without changing intersections");
        for (int i = 0; i < rays.Count; i++)
        {
            check(boxes[i].Box == baseline[i].Box && boxes[i].Fragments == baseline[i].Fragments && boxes[i].Speed == baseline[i].Speed,
                "Debug capture does not alter fragment damage input");
            check(rays[i].Start == origin && rays[i].Stopped && !rays[i].CrewHit && rays[i].Fragments == 8,
                "Debug ray retains its origin, bundle count and equipment stop");
            near(2f, frame.ToLocalPoint(rays[i].End).Z, 1e-4, "Debug ray ends on the stopping equipment face");
        }
        interior.Boxes.Clear();
        interior.Seats.Add(new InteriorSeat { Base = new Vector3(0, 0, 1), Radius = 0.25f, Height = 1 });
        profile.Rays = 1; profile.ConeHalfAngleDegrees = 0f;
        SpallTracer.Trace(interior, profile, origin, frame.Forward, 1, 200f, frame, frame,
            _ => layers, 17, seats, boxes, debugRays: rays);
        check(rays.Count == 1 && rays[0].CrewHit && rays[0].Stopped && seats.Count == 1, "Crew hit is captured on the actual ray");
        near(0.75f, frame.ToLocalPoint(rays[0].CrewPoint).Z, 1e-4, "Crew marker is at the seat entry rather than the maximum ray range");
        SpallTracer.Trace(interior, profile, origin, frame.Forward, 0, 200f, frame, frame,
            _ => layers, 17, seats, boxes, debugRays: rays);
        check(rays.Count == 0, "No-fragment impacts clear debug rays");
    }

    private static void Geometry()
    {
        check(InteriorGeometry.RayAabb(new Vector3(-5, 0.5f, 0.5f), Vector3.UnitX, Vector3.Zero, Vector3.One, out float enter, out float exit, out Vector3 n),
            "Ray hits the box");
        near(5, enter, 1e-5, "Box entry distance");
        near(6, exit, 1e-5, "Box exit distance");
        check(n == -Vector3.UnitX, "Entry face normal points back at the ray");
        check(!InteriorGeometry.RayAabb(new Vector3(-5, 2f, 0.5f), Vector3.UnitX, Vector3.Zero, Vector3.One, out _, out _, out _), "Ray above the box misses");
        check(InteriorGeometry.RayAabb(new Vector3(0.5f, 0.5f, 0.5f), Vector3.UnitZ, Vector3.Zero, Vector3.One, out enter, out exit, out _) && enter < 0 && exit > 0,
            "Ray starting inside a box reports it");
        check(InteriorGeometry.RayCylinder(new Vector3(-3, 0.5f, 0), Vector3.UnitX, Vector3.Zero, 0.25f, 0.9f, out enter, out exit), "Ray hits the seat");
        near(2.75, enter, 1e-4, "Cylinder entry");
        near(3.25, exit, 1e-4, "Cylinder exit");
        check(!InteriorGeometry.RayCylinder(new Vector3(-3, 1.2f, 0), Vector3.UnitX, Vector3.Zero, 0.25f, 0.9f, out _, out _), "Ray over a seated head misses");
        check(InteriorGeometry.RayCylinder(new Vector3(0.1f, 3f, 0), -Vector3.UnitY, Vector3.Zero, 0.25f, 0.9f, out enter, out _), "Ray from above hits the top");
        near(2.1, enter, 1e-4, "Cylinder top entry");
        var seat = new InteriorSeat { Radius = 0.25f, Height = 0.9f };
        near(0.45, InteriorGeometry.ProjectedArea(seat, Vector3.UnitX), 1e-5, "Side-on projected area 2 R H");
    }

    private static void Trace()
    {
        var interior = new VehicleInterior();
        interior.Boxes.Add(new InteriorBox { Id = "engine", Frame = ArmorFrame.Hull, Min = new Vector3(-1, 0.4f, -2.5f), Max = new Vector3(1, 1.2f, -1.5f), Component = "engine" });
        interior.Seats.Add(new InteriorSeat { Role = "driver", Frame = ArmorFrame.Hull, Base = new Vector3(-0.7f, 0.5f, 1.5f) });
        interior.Seats.Add(new InteriorSeat { Role = "gunner", Frame = ArmorFrame.Turret, Base = new Vector3(0.5f, -0.6f, 0f) });
        var hull = new BallisticFrame(Vector3.Zero, 0f);
        var turret = hull.Child(new Vector3(0, 1.6f, 0), 0f);
        var hits = new List<InteriorHit>();
        // A rod entering the front travelling aft at driver height meets the driver, then the engine.
        interior.Trace(new Vector3(-0.7f, 0.8f, 2.4f), -Vector3.UnitZ, hull, turret, 10f, hits);
        check(hits.Count == 2 && hits[0].Kind == InteriorHitKind.Seat && hits[1].Kind == InteriorHitKind.Equipment, "Driver then engine, nearest first");
        near(0.65, hits[0].Distance, 1e-3, "Driver at the expected range");
        // The gunner sits in the turret frame: turning the turret 180 deg moves the seat across.
        interior.Trace(new Vector3(-3, 1.4f, 0), Vector3.UnitX, hull, turret, 10f, hits);
        check(hits.Exists(h => h.Kind == InteriorHitKind.Seat && interior.Seats[h.Index].Role == "gunner"), "Gunner in the line before the turret turns");
        float before = hits.Find(h => interior.Seats[h.Index].Role == "gunner").Distance;
        var turned = hull.Child(new Vector3(0, 1.6f, 0), 180f);
        interior.Trace(new Vector3(-3, 1.4f, 0), Vector3.UnitX, hull, turned, 10f, hits);
        float after = hits.Find(h => h.Kind == InteriorHitKind.Seat && interior.Seats[h.Index].Role == "gunner").Distance;
        near(before - 1.0, after, 1e-3, "Turret seat moved 1 m toward the shot after a half turn");
        // Hull rotation moves everything: the same world ray misses the driver after a quarter turn.
        var quarter = new BallisticFrame(Vector3.Zero, 90f);
        interior.Trace(new Vector3(-0.7f, 0.8f, 2.4f), -Vector3.UnitZ, quarter, quarter.Child(new Vector3(0, 1.6f, 0), 0f), 10f, hits);
        check(!hits.Exists(h => h.Kind == InteriorHitKind.Seat && interior.Seats[h.Index].Role == "driver"), "Driver turns away with the hull");
        interior.Compartments.Add(new InteriorCompartment { Id = "crew", Frame = ArmorFrame.Hull, Min = new Vector3(-1.5f, 0.4f, -1.5f), Max = new Vector3(1.5f, 1.6f, 2.5f) });
        check(interior.CompartmentAt(new Vector3(0, 1, 0), hull, turret) == 0 && interior.CompartmentAt(new Vector3(0, 1, -2), hull, turret) == -1, "Compartment lookup");
        near(3 * 1.2 * 4, interior.Compartments[0].VolumeM3, 1e-4, "Compartment volume");
    }

    private static void Spall()
    {
        SpallProfile kinetic = SpallProfile.Kinetic;
        check(kinetic.Count(0) == kinetic.MinFragments && kinetic.Count(1e6f) == kinetic.MaxFragments, "Fragment count is clamped");
        near(30, kinetic.Count(50), 0.5, "0.6 fragments per perforated RHA mm");
        var dirs = new List<Vector3>();
        kinetic.Directions(Vector3.UnitZ, 7, dirs);
        check(dirs.Count == kinetic.Rays, "One direction per ray");
        float cosMax = MathF.Cos(kinetic.ConeHalfAngleDegrees * BallisticGeometry.DegToRad);
        check(dirs.TrueForAll(d => Vector3.Dot(d, Vector3.UnitZ) >= cosMax - 1e-4f), "Rays stay inside the cone");
        var again = new List<Vector3>();
        kinetic.Directions(Vector3.UnitZ, 7, again);
        check(dirs[3] == again[3], "Spall rays are deterministic");
        // Expected hits = n * A / (omega r^2) when below one.
        double sum = 0;
        const int samples = 20000;
        float area = 0.45f, range = 3f;
        var rng = new BallisticRng(99);
        for (int i = 0; i < samples; i++) sum += kinetic.Hits(10, area, range, ref rng);
        double expected = 10 * Math.Min(1.0, area / (kinetic.RaySolidAngle * range * range));
        near(expected, sum / samples, 0.05, "Bundle hits match the area / footprint ratio");
        var far = new BallisticRng(99);
        double farSum = 0;
        for (int i = 0; i < samples; i++) farSum += kinetic.Hits(10, area, range * 2, ref far);
        check(farSum < sum * 0.5, "Hits fall off with range (1/r^2)");
        near(11, SpallProfile.FragmentDamage(0.003f, MathF.Sqrt(2 * 1700f / 0.003f)), 1e-3, "Fragment wound anchored to a rifle round");
        check(kinetic.FragmentDiameterM > 0.008f && kinetic.FragmentDiameterM < 0.01f, "3 g steel fragment is about 9 mm");
    }
}
