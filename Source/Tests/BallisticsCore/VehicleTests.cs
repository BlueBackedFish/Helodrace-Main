using System;
using System.Collections.Generic;
using System.Numerics;
using Helodrace.Ballistics;

// Phase 5: vehicle armour shell (ray-plate, hull/turret frames) and the kinetic vehicle models.
internal static class VehicleTests
{
    private static Action<bool, string> check;
    private static Action<double, double, double, string> near;

    public static readonly LongRodCoefficients Tungsten = new LongRodCoefficients(0.994f, 134.5f, -0.148f, 0.033f, "test");
    public static readonly LongRodCoefficients Uranium = new LongRodCoefficients(0.825f, 90.0f, -0.0849f, 0.035f, "test");
    public static readonly PenetratorMaterial TungstenRod = new PenetratorMaterial("W", 17600f, 300f, 1.5e9f, Tungsten);
    public static readonly PenetratorMaterial UraniumRod = new PenetratorMaterial("DU", 18600f, 300f, 1.2e9f, Uranium);

    public static VehicleArmorMaterial Rha(float k = 1245f) => new VehicleArmorMaterial(
        new KineticMaterial("RHA", ArmorMaterialFamily.Metal, 7850f, new[] { k * 1.35f, k * 1.12f, k, k * 0.85f }, null, null,
            0.05f, 0.1f, 0f, 60f, 4f), 270f, 1f, true);

    public static VehicleArmorMaterial Aluminium() => new VehicleArmorMaterial(
        new KineticMaterial("Al", ArmorMaterialFamily.Metal, 2660f, new[] { 1600f, 1400f, 1300f, 1100f }, null, null,
            0.05f, 0.1f, 0f), 100f, 0.9f, false);

    public static void Run(Action<bool, string> checkFn, Action<double, double, double, string> nearFn)
    {
        check = checkFn;
        near = nearFn;
        ShellRaycast();
        TurretFrames();
        ShellValidation();
        ShotLine();
        LongRod();
        Tate();
        DeMarre();
        SmallArms();
        Stacks();
    }

    /// <summary>Box hull 3 m wide, 6 m long, 0.5-1.8 m high, with a 60 deg glacis and an optional rear ramp.</summary>
    public static VehicleArmorShell BoxHull(bool turret = false)
    {
        var shell = new VehicleArmorShell();
        float x = 1.5f, front = 3f, rear = -3f, low = 0.5f, high = 1.8f;
        // Glacis: from (front, low) up and back to (front - 0.75, high): 60 deg from vertical.
        float glacisTop = front - (high - low) * MathF.Tan(60f * BallisticGeometry.DegToRad);
        void Add(string id, int layup, string aperture, ArmorFrame frame, params Vector3[] v) =>
            shell.Plates.Add(new ArmorPlate(id, frame, v, layup, aperture));
        Add("glacis", 0, null, ArmorFrame.Hull, new Vector3(-x, low, front), new Vector3(x, low, front),
            new Vector3(x, high, glacisTop), new Vector3(-x, high, glacisTop));
        Add("roof", 1, null, ArmorFrame.Hull, new Vector3(-x, high, glacisTop), new Vector3(x, high, glacisTop),
            new Vector3(x, high, rear), new Vector3(-x, high, rear));
        Add("left", 1, null, ArmorFrame.Hull, new Vector3(-x, low, rear), new Vector3(-x, low, front),
            new Vector3(-x, high, glacisTop), new Vector3(-x, high, rear));
        Add("right", 1, null, ArmorFrame.Hull, new Vector3(x, low, front), new Vector3(x, low, rear),
            new Vector3(x, high, rear), new Vector3(x, high, glacisTop));
        Add("rear", 1, "ramp", ArmorFrame.Hull, new Vector3(x, low, rear), new Vector3(-x, low, rear),
            new Vector3(-x, high, rear), new Vector3(x, high, rear));
        Add("belly", 1, null, ArmorFrame.Hull, new Vector3(-x, low, rear), new Vector3(x, low, rear),
            new Vector3(x, low, front), new Vector3(-x, low, front));
        if (turret)
        {
            shell.TurretPivot = new Vector3(0f, 1.8f, 0f);
            float t = 1f, h = 0.8f;
            Add("turretFront", 2, null, ArmorFrame.Turret, new Vector3(-t, 0, t), new Vector3(t, 0, t), new Vector3(t, h, t), new Vector3(-t, h, t));
            Add("turretLeft", 3, null, ArmorFrame.Turret, new Vector3(-t, 0, -t), new Vector3(-t, 0, t), new Vector3(-t, h, t), new Vector3(-t, h, -t));
            Add("turretRight", 3, null, ArmorFrame.Turret, new Vector3(t, 0, t), new Vector3(t, 0, -t), new Vector3(t, h, -t), new Vector3(t, h, t));
            Add("turretRear", 3, null, ArmorFrame.Turret, new Vector3(t, 0, -t), new Vector3(-t, 0, -t), new Vector3(-t, h, -t), new Vector3(t, h, -t));
            Add("turretRoof", 3, null, ArmorFrame.Turret, new Vector3(-t, h, t), new Vector3(t, h, t), new Vector3(t, h, -t), new Vector3(-t, h, -t));
        }
        return shell;
    }

    private static bool Hit(VehicleArmorShell shell, float hullYaw, float turretYaw, Vector3 origin, Vector3 direction,
        out ShellHit hit, Func<string, bool> open = null)
    {
        var hull = new BallisticFrame(Vector3.Zero, hullYaw);
        return shell.Raycast(origin, direction, hull, shell.TurretFrame(hull, turretYaw), open, out hit);
    }

    private static void ShellRaycast()
    {
        VehicleArmorShell shell = BoxHull();
        Vector3 south = BallisticGeometry.TravelDirection(180f, 0f);
        check(Hit(shell, 0f, 0f, new Vector3(0, 1.0f, 20), south, out ShellHit front), "Flat shot from the north hits the hull");
        check(front.Plate.Id == "glacis", "North shot on a north hull hits the glacis, got " + front.Plate?.Id);
        near(60, front.Obliquity * BallisticGeometry.RadToDeg, 1e-2, "Glacis obliquity is its slope");
        near(60, front.Plate.SlopeFromVerticalDegrees, 1e-2, "Glacis slope from the vertices");
        Vector3 west = BallisticGeometry.TravelDirection(270f, 0f);
        check(Hit(shell, 0f, 0f, new Vector3(20, 1.0f, 0), west, out ShellHit side) && side.Plate.Id == "right", "East shot hits the right side");
        near(0, side.Obliquity, 1e-4, "Side shot is normal");
        near(1.5, side.LocalPoint.X, 1e-4, "Strike point on the side plate");
        // 30 deg off the side: obliquity 30.
        Vector3 skew = BallisticGeometry.TravelDirection(240f, 0f);
        check(Hit(shell, 0f, 0f, new Vector3(0, 1.0f, 0) - skew * 20f, skew, out ShellHit angled) && angled.Plate.Id == "right", "Angled side shot");
        near(30, angled.Obliquity * BallisticGeometry.RadToDeg, 1e-2, "Angled side obliquity");
        // Hull rotated 90 deg (facing east): the same east shot now strikes the glacis.
        check(Hit(shell, 90f, 0f, new Vector3(20, 1.0f, 0), west, out ShellHit turned) && turned.Plate.Id == "glacis",
            "Hull turned east: east shot hits the glacis, got " + turned.Plate?.Id);
        // Hull at 45 deg: a north shot meets the glacis and the left side about equally obliquely.
        check(Hit(shell, 135f, 0f, new Vector3(0, 1.0f, 20), south, out ShellHit quarter) &&
            (quarter.Plate.Id == "left" || quarter.Plate.Id == "rear"), "Hull at 135 deg: north shot hits left or rear, got " + quarter.Plate?.Id);
        // Diving shot hits the roof, flat shot at roof height misses it.
        Vector3 dive = BallisticGeometry.TravelDirection(180f, 70f);
        check(Hit(shell, 0f, 0f, new Vector3(0, 1.8f, -1) - dive * 20f, dive, out ShellHit roof) && roof.Plate.Id == "roof", "Diving shot hits the roof");
        near(20, roof.Obliquity * BallisticGeometry.RadToDeg, 1e-2, "Roof obliquity = 90 - descent");
        // Rear ramp: open flag is reported only while the aperture is open.
        Vector3 north = BallisticGeometry.TravelDirection(0f, 0f);
        check(Hit(shell, 0f, 0f, new Vector3(0, 1.0f, -20), north, out ShellHit closed) && closed.Plate.Id == "rear" && !closed.Open, "Closed ramp");
        check(Hit(shell, 0f, 0f, new Vector3(0, 1.0f, -20), north, out ShellHit opened, key => key == "ramp") && opened.Open, "Open ramp is a hole");
        check(!Hit(shell, 0f, 0f, new Vector3(5, 1.0f, 20), south, out _), "Shot beside the hull misses");
        check(!Hit(shell, 0f, 0f, new Vector3(0, 1.0f, -20), south, out _), "Shot moving away misses");
    }

    private static void TurretFrames()
    {
        VehicleArmorShell shell = BoxHull(turret: true);
        Vector3 south = BallisticGeometry.TravelDirection(180f, 0f);
        Vector3 origin = new Vector3(0, 2.2f, 20);
        check(Hit(shell, 0f, 0f, origin, south, out ShellHit a) && a.Plate.Id == "turretFront", "Forward turret shows its front");
        near(0, a.Obliquity, 1e-4, "Turret front is normal to the shot");
        check(Hit(shell, 0f, 90f, origin, south, out ShellHit b) && b.Plate.Id == "turretLeft", "Turret 90 right shows its left side");
        // Hull east (90) + turret -90 relative = turret north again.
        check(Hit(shell, 90f, -90f, origin, south, out ShellHit c) && c.Plate.Id == "turretFront", "Hull and turret yaw compose");
        near(1.0, c.LocalPoint.Z, 1e-4, "Strike point in turret coordinates");
        check(Hit(shell, 90f, 90f, origin, south, out ShellHit d) && d.Plate.Id == "turretRear", "Hull east + turret right faces south");
        // At hull height the turret is not in the way.
        check(Hit(shell, 0f, 0f, new Vector3(0, 1.0f, 20), south, out ShellHit e) && e.Plate.Frame == ArmorFrame.Hull, "Low shot hits the hull");
        // The turret shadows the roof behind it for a shallow dive.
        Vector3 dive = BallisticGeometry.TravelDirection(180f, 20f);
        check(Hit(shell, 0f, 0f, new Vector3(0, 2.0f, 0) - dive * 30f, dive, out ShellHit f) && f.Plate.Frame == ArmorFrame.Turret,
            "Turret shadows the roof behind it");
    }

    private static void ShellValidation()
    {
        var errors = new List<string>(BoxHull(true).Validate("box"));
        check(errors.Count == 0, "Box hull validates: " + string.Join("; ", errors));
        var bad = new VehicleArmorShell();
        bad.Plates.Add(new ArmorPlate("in", ArmorFrame.Hull, new[] { new Vector3(-1, 0, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 0, 1) }, 0));
        bad.Plates.Add(new ArmorPlate("out", ArmorFrame.Hull, new[] { new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1) }, 0));
        var badErrors = new List<string>(bad.Validate("bad"));
        check(badErrors.Exists(e => e.Contains("inward")), "Inward winding is reported");
        var bent = new VehicleArmorShell();
        bent.Plates.Add(new ArmorPlate("bent", ArmorFrame.Hull, new[] { new Vector3(0, 0, 1), new Vector3(2, 0, 1), new Vector3(1, 0.2f, 1), new Vector3(2, 2, 1), new Vector3(0, 2, 1) }, 0));
        check(new List<string>(bent.Validate("bent")).Exists(e => e.Contains("convex")), "Concave polygon is reported");
    }

    private static void ShotLine()
    {
        VehicleArmorShell shell = BoxHull(turret: true);
        var hull = new BallisticFrame(new Vector3(50, 0, 50), 0f);
        int glacis = 0, turret = 0, roof = 0, total = 0;
        for (int i = 0; i < 4000; i++)
        {
            // North shot (travelling south) against a north-facing hull.
            if (!VehicleShotLine.TryStrike(shell, hull, 0f, hull.Origin, 180f, 0f, (ulong)i * 977UL + 3, null, out ShellHit hit, out _)) continue;
            total++;
            if (hit.Plate.Id == "glacis") glacis++;
            if (hit.Plate.Frame == ArmorFrame.Turret) turret++;
            if (hit.Plate.Id == "roof") roof++;
        }
        check(total == 4000, "Every sampled shot line finds the shell");
        check(glacis > 1000 && turret > 600, "Frontal shots spread over glacis and turret: " + glacis + " / " + turret);
        check(roof < total / 10, "Flat fire rarely reaches the roof: " + roof);
        int diveRoof = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (VehicleShotLine.TryStrike(shell, hull, 0f, hull.Origin, 180f, 75f, (ulong)i + 11, null, out ShellHit hit, out _)
                && (hit.Plate.Id == "roof" || hit.Plate.Id == "turretRoof")) diveRoof++;
        }
        check(diveRoof > 1700, "A 75 deg dive mostly strikes roofs: " + diveRoof);
        // The game point sets where a dive lands along the line of fire.
        Vector3 aft = hull.ToWorldPoint(new Vector3(0, 0, -2.2f));
        int aftRoof = 0;
        for (int i = 0; i < 1000; i++)
            if (VehicleShotLine.TryStrike(shell, hull, 0f, aft, 180f, 75f, (ulong)i + 5, null, out ShellHit hit, out _)
                && hit.Plate.Id == "roof" && hit.LocalPoint.Z < -1.2f) aftRoof++;
        check(aftRoof > 800, "A dive aimed at the rear deck lands on the rear deck: " + aftRoof);
        VehicleShotLine.TryStrike(shell, hull, 0f, hull.Origin, 180f, 0f, 42, null, out ShellHit first, out _);
        VehicleShotLine.TryStrike(shell, hull, 0f, hull.Origin, 180f, 0f, 42, null, out ShellHit again, out _);
        check(first.PlateIndex == again.PlateIndex && first.WorldPoint == again.WorldPoint, "Shot line is deterministic");
        // A shot whose game point is off to the right side lands on the right half.
        var right = hull.ToWorldPoint(new Vector3(1.2f, 0, 0));
        int rightHalf = 0, n = 0;
        for (int i = 0; i < 1000; i++)
            if (VehicleShotLine.TryStrike(shell, hull, 0f, right, 180f, 0f, (ulong)i + 7, null, out ShellHit hit, out _))
            { n++; if (hit.LocalPoint.X > 0f) rightHalf++; }
        check(rightHalf > n * 0.8, "Game impact offset carries across the line of fire: " + rightHalf + "/" + n);
    }

    private static void LongRod()
    {
        // Hand calculation, tungsten rod L = 0.6 m, D = 0.02 m (L/D 30), 1.6 km/s, normal, RHA BHN 270.
        float p = VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.6f, 0.02f, 1600f, 0f, 7850f, 270f);
        double s2 = (134.5 - 0.148 * 270) * 270 / 17600.0;
        double expected = 0.6 * 0.994 / Math.Tanh(0.283 + 0.0656 * 30) * Math.Sqrt(17600.0 / 7850) * Math.Exp(-s2 / (1.6 * 1.6));
        near(expected, p, 1e-4, "Lanz-Odermatt hand calculation");
        // Oblique perforation: LOS limit grows with cos^-0.224.
        float p60 = VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.6f, 0.02f, 1600f, MathF.PI / 3f, 7850f, 270f);
        near(Math.Pow(0.5, -0.224), p60 / p, 1e-4, "cos^m obliquity term");
        // Monotonic in speed and length, lower against harder steel.
        check(VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.6f, 0.02f, 1800f, 0f, 7850f, 270f) > p, "Faster rod perforates more");
        check(VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.7f, 0.02f, 1600f, 0f, 7850f, 270f) > p, "Longer rod perforates more");
        check(VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.6f, 0.02f, 1600f, 0f, 7850f, 500f) < p, "Harder target resists more");
        // High-speed limit: P -> a / tanh(...) L sqrt(rho_p / rho_t), which is L sqrt(rho_p / rho_t) within the
        // fit constant (a / tanh = 1.000 at the L/D 40 clamp).
        float hydro = 0.8f * MathF.Sqrt(17600f / 7850f);
        float fast = VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.8f, 0.02f, 60000f, 0f, 7850f, 270f);
        near(1.0, fast / hydro, 0.01, "High-speed limit converges to L sqrt(rho_p/rho_t)");
        float last = 0f;
        for (float v = 500f; v <= 60000f; v *= 1.5f)
        {
            float value = VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.8f, 0.02f, v, 0f, 7850f, 270f);
            check(value >= last && value <= hydro * 1.001f, "Perforation rises monotonically to the hydrodynamic limit");
            last = value;
        }
        // DU rod with its own fit is below tungsten at the same geometry at 1.6 km/s (lower a).
        check(VehicleKineticModel.RodPerforationLimit(UraniumRod, 0.6f, 0.02f, 1600f, 0f, 7850f, 270f) < p * 1.05f, "DU fit evaluated");
        // Out-of-range inputs warn and clamp.
        var trace = new ImpactTrace();
        trace.Enter("t", TraceStageStatus.Ran);
        float stubby = VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.04f, 0.02f, 1600f, 80f * BallisticGeometry.DegToRad, 7850f, 270f, trace, "rod");
        check(trace.Warnings.Count == 2, "L/D and obliquity outside the fit warn: " + string.Join("; ", trace.Warnings));
        check(stubby > 0f, "Clamped evaluation stays finite");
    }

    private static void Tate()
    {
        // Published worked example (Liu et al., LAJSS 2019, eq. 2): W rod D 11 mm, L 90 mm, Yp 1506 MPa,
        // rho_p 17800, rho_t 7830, evaluated at tan(30 deg) gives 33.9989 m/s.
        var paper = new PenetratorMaterial("W", 17800f, 300f, 1.506e9f, Tungsten);
        float example = VehicleKineticModel.TateCriticalVelocity(paper, 0.09f, 0.011f, 30f * BallisticGeometry.DegToRad, 7830f);
        near(34.0, example, 0.5, "Tate expression matches the published worked example");
        float last = 0f;
        for (int degrees = 10; degrees <= 88; degrees += 6)
        {
            float v = VehicleKineticModel.TateCriticalVelocity(TungstenRod, 0.6f, 0.02f, degrees * BallisticGeometry.DegToRad, 7850f);
            check(v > last, "Critical ricochet speed grows with obliquity");
            last = v;
        }
        // Practical slopes (60-70 deg) never ricochet a 1.5 km/s rod; grazing (87 deg) does.
        var result = new VehicleKineticResult();
        var layers = new[] { new VehicleArmorLayer(Rha(), 0.08f) };
        foreach (float degrees in new[] { 60f, 65f, 70f })
        {
            Vector3 normal = BallisticGeometry.PlateNormal(0f, 0f);
            Vector3 travel = -RotateAboutX(normal, degrees);
            var rod = PenetratorState.Projectile(PenetrationMechanism.KineticLongRod, 4f, travel * 1500f, 0.6f, 0.02f);
            VehicleKineticModel.Resolve(new VehicleKineticRound { Mechanism = PenetrationMechanism.KineticLongRod, Core = TungstenRod },
                rod, layers, normal, 1, result);
            check(result.Result != ImpactResult.Ricochet, "Rod does not ricochet at " + degrees + " deg");
        }
        {
            Vector3 normal = BallisticGeometry.PlateNormal(0f, 0f);
            Vector3 travel = -RotateAboutX(normal, 87f);
            var rod = PenetratorState.Projectile(PenetrationMechanism.KineticLongRod, 4f, travel * 1500f, 0.6f, 0.02f);
            VehicleKineticModel.Resolve(new VehicleKineticRound { Mechanism = PenetrationMechanism.KineticLongRod, Core = TungstenRod },
                rod, layers, normal, 1, result);
            check(result.Result == ImpactResult.Ricochet, "Rod ricochets at 87 deg");
            check(result.Out.KineticEnergyJ <= rod.KineticEnergyJ, "Ricochet does not add energy");
        }
    }

    /// <summary>Direction at the given angle from a +Z normal, tilted toward +Y.</summary>
    private static Vector3 RotateAboutX(Vector3 normal, float degrees)
    {
        float r = degrees * BallisticGeometry.DegToRad;
        return Vector3.Normalize(normal * MathF.Cos(r) + Vector3.UnitY * MathF.Sin(r));
    }

    private static void DeMarre()
    {
        // 75 mm, 6.8 kg AP vs 100 mm RHA: 2400 * 0.75^0.75 * 1^0.7 / sqrt(6.8).
        float v = VehicleKineticModel.DeMarreLimit(2400f, 0.075f, 0.1f, 6.8f);
        near(2400 * Math.Pow(0.75, 0.75) / Math.Sqrt(6.8), v, 0.01, "De Marre hand calculation");
        check(VehicleKineticModel.DeMarreLimit(2400f, 0.075f, 0.12f, 6.8f) > v, "Thicker plate needs more speed");
        check(VehicleKineticModel.DeMarreLimit(2400f, 0.075f, 0.1f, 8f) < v, "Heavier shot needs less speed");
        near(Math.Pow(2, 0.7), VehicleKineticModel.DeMarreLimit(2400f, 0.075f, 0.2f, 6.8f) / v, 1e-4, "t^0.7");

        var round = new VehicleKineticRound { Mechanism = PenetrationMechanism.KineticHardCore, RicochetCriticalDegrees = 72f };
        var result = new VehicleKineticResult();
        Vector3 normal = Vector3.UnitZ;
        var shot = PenetratorState.Projectile(PenetrationMechanism.KineticHardCore, 6.8f, -normal * (v * 1.3f), 0f, 0.075f);
        VehicleKineticModel.Resolve(round, shot, new[] { new VehicleArmorLayer(Rha(), 0.1f) }, normal, 5, result);
        check(result.Perforated, "AP shot 30 % above the limit perforates");
        check(result.Out.Speed >= v * 0.65f && result.Out.Speed <= v * 0.962f, "Lambert-Jonas p = 2 residual within the scatter band");
        float single = result.Out.Speed;
        VehicleKineticModel.Resolve(round, shot, new[] { new VehicleArmorLayer(Rha(), 0.05f), new VehicleArmorLayer(Rha(), 0.05f) }, normal, 5, result);
        near(single, result.Out.Speed, 1e-2, "De Marre resolves the summed stack: a split plate equals one plate");
        var slow = PenetratorState.Projectile(PenetrationMechanism.KineticHardCore, 6.8f, -normal * (v * 0.8f), 0f, 0.075f);
        VehicleKineticModel.Resolve(round, slow, new[] { new VehicleArmorLayer(Rha(), 0.1f) }, normal, 5, result);
        check(result.Result == ImpactResult.Stopped, "AP shot below the limit stops");
        int glances = 0;
        Vector3 steep = -RotateAboutX(normal, 80f);
        for (int i = 0; i < 2000; i++)
        {
            var oblique = PenetratorState.Projectile(PenetrationMechanism.KineticHardCore, 6.8f, steep * 900f, 0f, 0.075f);
            VehicleKineticModel.Resolve(round, oblique, new[] { new VehicleArmorLayer(Rha(), 0.05f) }, normal, (ulong)i, result);
            if (result.Result == ImpactResult.Ricochet) glances++;
        }
        near(VehicleKineticModel.RicochetProbability(80f * BallisticGeometry.DegToRad, 72f, 3f), glances / 2000.0, 0.03, "AP ricochet rate matches its curve");
    }

    private static void SmallArms()
    {
        // 12.7 mm M8 API (40.4 g, 12.9 mm, hard steel, 887 m/s) against 20 mm RHA is calibrated to V50.
        var round = new VehicleKineticRound { Mechanism = PenetrationMechanism.KineticBullet, SmallArmsClass = PenetratorClass.HardSteel };
        var result = new VehicleKineticResult();
        Vector3 normal = Vector3.UnitZ;
        int perforated = 0;
        for (int i = 0; i < 4000; i++)
        {
            var bullet = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.0404f, -normal * 887f, 0f, 0.0129f);
            VehicleKineticModel.Resolve(round, bullet, new[] { new VehicleArmorLayer(Rha(), 0.02f) }, normal, (ulong)i * 31UL + 1, result);
            if (result.Perforated) perforated++;
        }
        near(0.5, perforated / 4000.0, 0.04, "12.7 mm API is near V50 on 20 mm RHA");
        // 5.56 mm against 50 mm RHA: certain stop, early exit, no layer rolls.
        var rifle = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.004f, -normal * 950f, 0f, 0.0057f);
        VehicleKineticModel.Resolve(new VehicleKineticRound { SmallArmsClass = PenetratorClass.MildSteel }, rifle,
            new[] { new VehicleArmorLayer(Rha(), 0.05f) }, normal, 1, result);
        check(result.EarlyStop && result.Result == ImpactResult.Stopped, "Rifle round on a tank hull stops early (T2)");
        check(result.EnergyOut == 0f, "Stopped round carries no energy");
    }

    private static void Stacks()
    {
        var round = new VehicleKineticRound { Mechanism = PenetrationMechanism.KineticLongRod, Core = TungstenRod };
        var result = new VehicleKineticResult();
        Vector3 normal = Vector3.UnitZ;
        PenetratorState Rod() => PenetratorState.Projectile(PenetrationMechanism.KineticLongRod, 3.3f, -normal * 1600f, 0.6f, 0.02f);
        float limit = VehicleKineticModel.RodPerforationLimit(TungstenRod, 0.6f, 0.02f, 1600f, 0f, 7850f, 270f);
        VehicleKineticModel.Resolve(round, Rod(), new[] { new VehicleArmorLayer(Rha(), limit * 0.5f) }, normal, 9, result);
        check(result.Perforated, "Rod perforates half its limit");
        check(result.Out.LengthM < 0.6f && result.Out.MassKg < 3.3f, "Rod erodes");
        check(result.EnergyOut <= result.EnergyIn, "Energy does not grow through armour");
        VehicleKineticModel.Resolve(round, Rod(), new[] { new VehicleArmorLayer(Rha(), limit * 1.2f) }, normal, 9, result);
        check(result.Result == ImpactResult.Stopped, "Rod stops on 120 % of its limit");
        // Splitting a plate in two changes the residual rod by only a few percent (erosion additivity).
        float single = 0f, split = 0f;
        for (int i = 0; i < 400; i++)
        {
            VehicleKineticModel.Resolve(round, Rod(), new[] { new VehicleArmorLayer(Rha(), limit * 0.6f) }, normal, (ulong)i, result);
            single += result.Out.LengthM;
            VehicleKineticModel.Resolve(round, Rod(), new[] { new VehicleArmorLayer(Rha(), limit * 0.3f), new VehicleArmorLayer(Rha(), limit * 0.3f) }, normal, (ulong)i, result);
            split += result.Out.LengthM;
        }
        near(1.0, split / single, 0.06, "Layered erosion is close to the single plate");
        // Aluminium is RHA-equivalent by density x efficiency.
        VehicleArmorMaterial al = Aluminium();
        near(0.1 * 2660 / 7850.0 * 0.9, al.RhaEquivalent(0.1f), 1e-5, "Aluminium RHA equivalent");
        VehicleKineticModel.Resolve(round, Rod(), new[] { new VehicleArmorLayer(al, 0.2f) }, normal, 3, result);
        check(result.Result == ImpactResult.Overmatch, "Rod overmatches 200 mm aluminium");
        // Determinism: same seed, same result.
        VehicleKineticModel.Resolve(round, Rod(), new[] { new VehicleArmorLayer(Rha(), limit * 0.98f) }, normal, 77, result);
        ImpactResult r1 = result.Result;
        float l1 = result.Out.LengthM;
        VehicleKineticModel.Resolve(round, Rod(), new[] { new VehicleArmorLayer(Rha(), limit * 0.98f) }, normal, 77, result);
        check(r1 == result.Result && l1 == result.Out.LengthM, "Same seed, same rod result");
        // Open aperture / empty stack: perforation without loss.
        VehicleKineticModel.Resolve(round, Rod(), Array.Empty<VehicleArmorLayer>(), normal, 1, result);
        check(result.Perforated && result.EnergyOut == result.EnergyIn, "No layers: unprotected");
    }
}
