using System;
using Helodrace.Ballistics;

// Phase 9: blast pressure, vented compartments, warhead fragments, HESH.
internal static class BlastTests
{
    private static Action<bool, string> check;
    private static Action<double, double, double, string> near;

    public static void Run(Action<bool, string> checkFn, Action<double, double, double, string> nearFn)
    {
        check = checkFn;
        near = nearFn;
        Airblast();
        Compartments();
        Fragments();
    }

    private static void Airblast()
    {
        // Regime boundaries meet (natural-log reading of the Swisdak table): Z = 7.25 and 60 ft/lb^1/3.
        float toSi = 1.30060f / 3.28084f;
        float a = BlastModel.IncidentOverpressureKPa(7.2499f * toSi, 1f, out _), b = BlastModel.IncidentOverpressureKPa(7.2501f * toSi, 1f, out _);
        check(Math.Abs(a - b) / a < 0.03, "Kingery-Bulmash regimes meet at Z = 7.25 ft/lb^1/3: " + a + " vs " + b);
        float c = BlastModel.IncidentOverpressureKPa(59.99f * toSi, 1f, out _), d = BlastModel.IncidentOverpressureKPa(60.01f * toSi, 1f, out _);
        check(Math.Abs(c - d) / c < 0.1, "Kingery-Bulmash regimes meet at Z = 60 ft/lb^1/3: " + c + " vs " + d);
        // Reference points of the hemispherical surface-burst curve (published curves: ~1.1-1.4 MPa at Z = 1, ~14 kPa at Z = 10).
        float z1 = BlastModel.IncidentOverpressureKPa(1f, 1f, out _), z10 = BlastModel.IncidentOverpressureKPa(10f, 1f, out _);
        check(z1 > 1000f && z1 < 1500f, "Z = 1 m/kg^1/3 gives about 1.3 MPa: " + z1);
        check(z10 > 11f && z10 < 18f, "Z = 10 m/kg^1/3 gives about 15 kPa: " + z10);
        float last = float.MaxValue;
        for (float r = 0.5f; r < 60f; r *= 1.2f)
        {
            float p = BlastModel.IncidentOverpressureKPa(r, 2f, out _);
            check(p < last, "Overpressure falls with range");
            last = p;
        }
        // Cube-root scaling: 8x charge at 2x range gives the same pressure.
        near(BlastModel.IncidentOverpressureKPa(3f, 1f, out _), BlastModel.IncidentOverpressureKPa(6f, 8f, out _), 1e-2, "Hopkinson-Cranz scaling");
        BlastModel.IncidentOverpressureKPa(0.01f, 10f, out bool clamped);
        check(clamped, "Contact range is outside the fit and clamped");
    }

    private static void Compartments()
    {
        near(0.4 * 4.184e6 * 1 / 10 / 1000, BlastModel.QuasiStaticPressureKPa(1f, 10f), 1e-2, "Quasi-static pressure (gamma - 1) E W / V");
        check(BlastModel.QuasiStaticPressureKPa(1f, 5f) > BlastModel.QuasiStaticPressureKPa(1f, 10f), "A smaller compartment holds more pressure");
        float closedShare = BlastModel.VentedLoadShare(BlastModel.VentTimeConstantS(6f, 0f));
        float hatch = BlastModel.VentedLoadShare(BlastModel.VentTimeConstantS(6f, 0.4f));
        float ramp = BlastModel.VentedLoadShare(BlastModel.VentTimeConstantS(6f, 2.5f));
        check(closedShare == 1f && hatch < closedShare && ramp < hatch, "Openings vent the load: sealed " + closedShare + ", hatch " + hatch + ", ramp " + ramp);
        near(6.0 / (0.6 * 0.4 * 340), BlastModel.VentTimeConstantS(6f, 0.4f), 1e-5, "Blow-down time V / (Cd A c)");
        near(1.5, BlastModel.CrewBlastDamage(1f, 300f), 1e-5, "Crew blast damage scales with load");
        near(3, BlastModel.CrewBlastDamage(1f, 5000f), 1e-5, "Crew blast damage is capped");
    }

    private static void Fragments()
    {
        near(2440 / Math.Sqrt(2.0 + 0.5), BlastModel.GurneyCylinderSpeed(2f, 1f), 1e-2, "Gurney cylinder, M/C = 2");
        check(BlastModel.GurneyCylinderSpeed(1f, 1f) > BlastModel.GurneyCylinderSpeed(4f, 1f), "Heavier case, slower fragments");
        // Mott inversion: the mean of sampled masses is 2 mu.
        var rng = new BallisticRng(5);
        double sum = 0;
        for (int i = 0; i < 200000; i++) sum += BlastModel.MottSample(0.002f, rng.NextFloat());
        near(0.004, sum / 200000, 2e-4, "Mott sample mean = 2 mu");
        check(BlastModel.MottCount(2f, 0.002f) == 500, "Fragment count from case mass and mean mass");
        check(BlastModel.HeshScabs(0.1f, 0.105f, false) && !BlastModel.HeshScabs(0.2f, 0.105f, false), "HESH scabs plates under about 1.3 calibres");
        check(!BlastModel.HeshScabs(0.05f, 0.105f, true), "Spaced or lined armour defeats the scab");
    }
}

// Phase 10: ammunition reaction odds and isolation.
internal static class AmmoTests
{
    public static void Run(Action<bool, string> check, Action<double, double, double, string> near)
    {
        check(AmmoReactionModel.IgnitionChance(AmmoExposure.None, 100, false) == 0f, "An undamaged stowage never reacts");
        float jet = AmmoReactionModel.IgnitionChance(AmmoExposure.Jet, 0, false);
        float rod = AmmoReactionModel.IgnitionChance(AmmoExposure.Penetrator, 0, false);
        float few = AmmoReactionModel.IgnitionChance(AmmoExposure.Fragments, 3, false);
        float many = AmmoReactionModel.IgnitionChance(AmmoExposure.Fragments, 60, false);
        check(jet > rod && rod > few && many > few, "Jets ignite more readily than penetrators, more fragments more readily than few");
        check(AmmoReactionModel.IgnitionChance(AmmoExposure.Jet, 0, true) < jet * 0.5f, "Insensitive rounds resist ignition");
        var magazine = new AmmoStowage { PropellantKg = 100f, Isolated = true, BlowOutAreaM2 = 1f };
        var rack = new AmmoStowage { PropellantKg = 100f };
        check(AmmoReactionModel.CrewGasShare(magazine, false) < 0.1f && AmmoReactionModel.CrewGasShare(magazine, true) == 1f &&
              AmmoReactionModel.CrewGasShare(rack, false) == 1f, "An isolated magazine keeps the gas from the crew unless its door is open");
        near(BlastModel.QuasiStaticPressureKPa(40f, 10f), AmmoReactionModel.BurnPressureKPa(rack, 10f), 1e-3, "Burning propellant gas through the compartment model");
    }
}

// Phase 11: normalization, rod yaw in gaps, slats, ceramic crack grid, visor crazing.
internal static class AdvancedTests
{
    public static void Run(Action<bool, string> check, Action<double, double, double, string> near)
    {
        near(55, Normalization.Apply(60f * BallisticGeometry.DegToRad, 5f) * BallisticGeometry.RadToDeg, 1e-3, "Normalization turns the effective obliquity toward the normal");
        check(Normalization.Apply(2f * BallisticGeometry.DegToRad, 5f) == 0f, "Normalization never passes the normal");
        check(Normalization.Degrees(PenetrationMechanism.KineticLongRod, PenetratorClass.Tungsten) == 0f, "Long rods: the fit already holds obliquity");
        check(Normalization.Degrees(PenetrationMechanism.KineticHardCore, PenetratorClass.HardSteel) > Normalization.Degrees(PenetrationMechanism.KineticHardCore, PenetratorClass.Tungsten),
            "Blunt steel shot normalizes more than a tungsten core");
        // Spaced armour: the same two plates with a gap between them cost a rod more than bonded.
        var rha = VehicleTests.Rha();
        var round = new VehicleKineticRound { Mechanism = PenetrationMechanism.KineticLongRod, Core = VehicleTests.TungstenRod };
        var result = new VehicleKineticResult();
        float bonded = 0f, spaced = 0f;
        for (int i = 0; i < 400; i++)
        {
            var rod = PenetratorState.Projectile(PenetrationMechanism.KineticLongRod, 3f, -System.Numerics.Vector3.UnitZ * 1600f, 0.5f, 0.02f);
            VehicleKineticModel.Resolve(round, rod, new[] { new VehicleArmorLayer(rha, 0.08f), new VehicleArmorLayer(rha, 0.15f) }, System.Numerics.Vector3.UnitZ, (ulong)i, result);
            bonded += result.Out.LengthM;
            VehicleKineticModel.Resolve(round, rod, new[] { new VehicleArmorLayer(rha, 0.08f, 0.5f), new VehicleArmorLayer(rha, 0.15f) }, System.Numerics.Vector3.UnitZ, (ulong)i, result);
            spaced += result.Out.LengthM;
        }
        check(spaced < bonded, "A gap behind the first plate yaws the rod and costs it more length (" + spaced + " vs " + bonded + ")");
        check(RodYaw.Grow(0f, 100f, 0.5f) <= 0.35f && RodYaw.LimitFactor(1f, 0.6f, 0.02f) >= 0.5f, "Yaw growth and its cost are capped");
        // Slats: the three outcomes in their shares.
        int pass = 0, dud = 0, early = 0;
        for (int i = 0; i < 1000; i++)
        {
            switch (SlatArmor.Roll((i + 0.5f) / 1000f))
            {
                case SlatOutcome.PassThrough: pass++; break;
                case SlatOutcome.Dud: dud++; break;
                default: early++; break;
            }
        }
        check(pass == 350 && dud == 500 && early == 150, "Slat outcomes 35 / 50 / 15 %");
        // Crack grid: a repeat hit on the same cell meets weaker ceramic than a hit far away.
        var cracks = new float[CrackGrid.Cells];
        int centre = CrackGrid.CellOf(0f, 40f, 0.5f, 0f, 1f), corner = CrackGrid.CellOf(-39f, 40f, 0.01f, 0f, 1f);
        check(centre == 4 && corner == 0, "Hit cells: centre and lower-left corner");
        CrackGrid.Record(cracks, centre, false);
        check(CrackGrid.Effectiveness(cracks, centre) < CrackGrid.Effectiveness(cracks, corner) && CrackGrid.Effectiveness(cracks, corner) < 1f,
            "Same-spot hits meet cracked ceramic; neighbours are slightly weakened");
        for (int i = 0; i < 5; i++) CrackGrid.Record(cracks, centre, true);
        check(CrackGrid.Effectiveness(cracks, centre) >= 0.1f, "A shattered cell keeps a floor");
        near(0.25, VisorCrazing.Grow(0f, false), 1e-6, "A stopped hit crazes the visor");
        check(VisorCrazing.SightLoss(VisorCrazing.Grow(VisorCrazing.Grow(0f, true), true)) > 0.25f, "Repeated hits cloud the visor");
    }
}
