using System;
using System.Numerics;
using Helodrace.Ballistics;

// Phase 6: shaped-charge jet and reactive armour.
internal static class ShapedChargeTests
{
    private static Action<bool, string> check;
    private static Action<double, double, double, string> near;

    public static void Run(Action<bool, string> checkFn, Action<double, double, double, string> nearFn)
    {
        check = checkFn;
        near = nearFn;
        Jet();
        Reactive();
    }

    private static VehicleArmorMaterial Al() => new VehicleArmorMaterial(
        new KineticMaterial("Al", ArmorMaterialFamily.Metal, 2660f, new[] { 1600f, 1400f, 1300f, 1100f }, null, null, 0.05f, 0.1f, 0f),
        80f, 0.95f, false, 1.1f);

    private static void Jet()
    {
        var warhead = new ShapedChargeWarhead { PenetrationRhaM = 0.43f, Scatter = 0f };
        near(0.43 / Math.Sqrt(8960.0 / 7850.0), ShapedChargeModel.JetLength(0.43f, 8960f), 1e-5, "Jet length from P = L sqrt(rho_j / rho_t)");
        var result = new JetResult();
        VehicleArmorMaterial rha = VehicleTests.Rha();
        ShapedChargeModel.Resolve(warhead, 0.43f, new[] { new VehicleArmorLayer(rha, 0.3f) }, 0f, 1, result);
        check(result.Perforated, "430 mm jet perforates 300 mm RHA");
        near(0.13, result.PotentialOutRhaM, 1e-4, "Jet spends its RHA potential layer by layer");
        ShapedChargeModel.Resolve(warhead, 0.43f, new[] { new VehicleArmorLayer(rha, 0.25f) }, MathF.PI / 3f, 1, result);
        check(result.Result == ImpactResult.Stopped, "60 deg slope doubles LOS: 250 mm stops a 430 mm jet");
        // Additivity: a split stack spends the same potential.
        ShapedChargeModel.Resolve(warhead, 0.43f, new[] { new VehicleArmorLayer(rha, 0.1f), new VehicleArmorLayer(rha, 0.1f) }, 0f, 1, result);
        near(0.23, result.PotentialOutRhaM, 1e-4, "Split plates spend the same jet");
        // Non-steels: density ratio x jet efficiency.
        near(0.1 * 2660 / 7850.0 * 1.1, Al().RhaEquivalentVsJet(0.1f), 1e-5, "Aluminium jet equivalence");
        ShapedChargeModel.Resolve(warhead, 0.43f, new[] { new VehicleArmorLayer(rha, 0.42f) }, 0f, 1, result);
        check(result.Perforated && result.Result == ImpactResult.Perforation, "Thin margin is a perforation, not an overmatch");
        check(ShapedChargeModel.DamageFactor(result) >= 0.5f && ShapedChargeModel.DamageFactor(result) < 0.52f, "Thin-margin damage share near 0.5");
        // Scatter is sampled and reproducible.
        var scattered = new ShapedChargeWarhead { PenetrationRhaM = 0.43f, Scatter = 0.05f };
        int perforated = 0;
        for (int i = 0; i < 4000; i++)
        {
            ShapedChargeModel.Resolve(scattered, 0.43f, new[] { new VehicleArmorLayer(rha, 0.43f) }, 0f, (ulong)i * 13UL + 1, result);
            if (result.Perforated) perforated++;
        }
        near(0.5, perforated / 4000.0, 0.04, "Jet at its nominal figure perforates about half the time");
        // Grazing fuze failure.
        check(ShapedChargeModel.DudProbability(warhead, 60f * BallisticGeometry.DegToRad) < 1e-4f, "No duds at 60 deg");
        check(ShapedChargeModel.DudProbability(warhead, 86f * BallisticGeometry.DegToRad) > 0.9f, "Grazing strikes dud");
    }

    private static void Reactive()
    {
        var spec = new ReactiveModuleSpec();
        var heat = new ShapedChargeWarhead { PenetrationRhaM = 0.43f };
        float jet = ShapedChargeModel.JetLength(heat.PenetrationRhaM, heat.JetDensityKgM3);
        // A side module (face normal +X) with a 60 deg cassette: a perpendicular shot meets the sandwich at 60 deg.
        Vector3 face = Vector3.UnitX;
        Vector3 sandwich = ReactiveArmorModel.SandwichNormal(face, 60f);
        check(BallisticGeometry.TryObliquity(-Vector3.UnitX, sandwich, out float theta), "Shot meets the sandwich");
        near(60, theta * BallisticGeometry.RadToDeg, 1e-3, "Sandwich obliquity = cassette tilt for a face-on shot");
        float single = ReactiveArmorModel.JetResidual(spec, heat, jet, theta);
        near(0.2, single, 0.03, "Single HEAT keeps about 0.2 of its jet (previous Bradley value)");
        // Tandem: precursor initiates, the main jet arrives 120 us later and meets only the remaining overlap.
        var tow = new ShapedChargeWarhead { PenetrationRhaM = 0.9f, PrecursorPenetrationRhaM = 0.05f };
        float main = ReactiveArmorModel.JetResidual(spec, tow, ShapedChargeModel.JetLength(0.9f, tow.JetDensityKgM3), theta, tow.TandemDelayS);
        near(0.7, main, 0.05, "Tandem main jet keeps about 0.7 (previous Bradley value)");
        check(main > single, "Tandem beats a single charge");
        check(ReactiveArmorModel.JetResidual(spec, tow, jet, theta, 1e-3f) == 1f, "Late main jet meets no plates");
        // Normal incidence on the sandwich: no lateral sweep, no disruption.
        near(1, ReactiveArmorModel.JetResidual(spec, heat, jet, 0f), 1e-5, "Plates moving along the jet do not disrupt it");
        // Disruption rises with obliquity over the practical range.
        float last = 1f;
        for (int degrees = 10; degrees <= 70; degrees += 10)
        {
            float r = ReactiveArmorModel.JetResidual(spec, heat, jet, degrees * BallisticGeometry.DegToRad);
            check(r <= last + 1e-5f, "More oblique sandwich disrupts more (" + degrees + " deg)");
            last = r;
        }
        // Activation delay lets the tip through.
        var slow = new ReactiveModuleSpec { ActivationDelayS = 20e-6f };
        check(ReactiveArmorModel.JetResidual(slow, heat, jet, theta) > single + 0.2f, "A slow module lets the jet tip through");
        // NERA: lower plate speed and efficiency, degradation reduces it further.
        var nera = new ReactiveModuleSpec { Kind = ReactiveKind.NonExplosive, FlyerVelocityMs = 120f, Efficiency = 0.45f };
        float fresh = ReactiveArmorModel.JetResidual(nera, heat, jet, theta);
        float worn = ReactiveArmorModel.JetResidual(nera, heat, jet, theta, 0f, 1f - 2 * nera.DegradationPerHit);
        check(fresh > single && fresh < 1f, "NERA disrupts less than ERA: " + fresh);
        check(worn > fresh, "Degraded NERA disrupts less");
        check(new ReactiveModuleSpec().Validate().GetEnumerator().MoveNext() == false, "Default module validates");
    }
}
