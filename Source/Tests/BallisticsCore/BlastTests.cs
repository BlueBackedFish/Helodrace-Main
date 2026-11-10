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
