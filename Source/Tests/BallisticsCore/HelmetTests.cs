using System;
using System.Numerics;
using Helodrace.Ballistics;

// Phase 4: strike-face ricochet and helmet shell coverage.
internal static class HelmetTests
{
    public static void Run(Action<bool, string> check, Action<double, double, double, string> near)
    {
        var steel = new KineticMaterial("steel", ArmorMaterialFamily.Metal, 7850f, new[] { 1230f, 1100f, 1000f, 900f },
            null, null, 0.06f, 0.35f, 0.003f, 55f, 5f, 0.85f, 0.1f);
        var noGlance = new KineticMaterial("fibre", ArmorMaterialFamily.Fiber, 1440f, new[] { 2600f, 1585f, 1385f, 1075f },
            null, null, 0.06f, 0.3f, 0.003f);
        float Rad(float degrees) => degrees * BallisticGeometry.DegToRad;

        // Logistic around the critical angle.
        near(0.5, KineticBulletModel.RicochetProbability(steel, Rad(55f)), 1e-4, "Half the rounds glance at the critical angle");
        check(KineticBulletModel.RicochetProbability(steel, Rad(20f)) < 0.001f, "Square hits do not glance");
        check(KineticBulletModel.RicochetProbability(steel, Rad(80f)) > 0.99f, "Grazing hits glance");
        check(KineticBulletModel.RicochetProbability(steel, Rad(60f)) > KineticBulletModel.RicochetProbability(steel, Rad(50f)),
            "Glancing grows with obliquity");
        check(KineticBulletModel.RicochetProbability(noGlance, Rad(85f)) == 0f, "A material without a critical angle never glances");

        // Velocity after a glance: tangential kept (friction), normal reversed at the restitution share.
        Vector3 normal = Vector3.UnitY;
        Vector3 incoming = new Vector3(MathF.Sin(Rad(75f)), -MathF.Cos(Rad(75f)), 0f) * 400f;
        var round = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.0149f, incoming, 0f, 0.0115f);
        int glances = 0;
        for (ulong seed = 1; seed < 200; seed++)
        {
            var rng = new BallisticRng(seed);
            if (!KineticBulletModel.TryRicochet(round, normal, steel, ref rng, out PenetratorState after, out float absorbed, out float p))
                continue;
            glances++;
            near(400 * MathF.Sin(Rad(75f)) * 0.85, after.Velocity.X, 0.01, "Tangential velocity kept at the friction share");
            near(400 * MathF.Cos(Rad(75f)) * 0.1, after.Velocity.Y, 0.01, "Normal velocity reversed at the restitution share");
            check(after.Velocity.Y > 0f, "Glancing round leaves the surface");
            near(round.KineticEnergyJ, absorbed + after.KineticEnergyJ, 0.01, "Glance conserves energy (absorbed + kept)");
            check(after.Condition == PenetratorCondition.Deformed, "Glancing round is deformed");
        }
        check(glances > 150, "A 75 degree hit on steel mostly glances: " + glances);
        var receding = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.01f, Vector3.UnitY * 300f, 0f, 0.01f);
        var r = new BallisticRng(3);
        check(!KineticBulletModel.TryRicochet(receding, normal, steel, ref r, out _, out _, out _), "A receding round cannot glance");

        // Shell coverage: brim and open face window.
        var fast = new ShellCoverage(0.0f, 50f, 0.35f);
        check(fast.Contains(0f, 0.8f), "Forehead above the face window is covered");
        check(!fast.Contains(0f, 0.1f), "Face window is open");
        check(fast.Contains(90f, 0.1f), "Side of the head above the brim is covered");
        check(!fast.Contains(90f, -0.2f), "High-cut shell leaves the ears below its brim");
        check(fast.Contains(180f, 0.2f), "Back of the head is covered");
        var m1 = new ShellCoverage(-0.25f, 55f, 0.30f);
        check(m1.Contains(90f, -0.2f), "Deep brim covers the ears");

        // Geometry: for level shots at a head, the glance share follows the obliquity the shell sees. The crown
        // and the edges meet the round at grazing angles, the centre of the forehead square on.
        var geometry = new BallisticRng(11);
        double expected = 0, observed = 0, crownGlance = 0, crownCount = 0, browGlance = 0, browCount = 0;
        int covered = 0;
        Vector3 level = -Vector3.UnitZ;
        for (int i = 0; i < 60000; i++)
        {
            BodyHit hit = BodyHitGeometry.Sample(BodyHitGeometry.Head, level, ref geometry);
            if (!fast.Contains(hit.AzimuthDegrees, hit.HeightFraction)) continue;
            covered++;
            float p = KineticBulletModel.RicochetProbability(steel, hit.SurfaceObliquity);
            expected += p;
            var glance = new BallisticRng(BallisticSeed.ForImpact(i, 0, 1));
            var shot = PenetratorState.Projectile(PenetrationMechanism.KineticBullet, 0.008f, level * 350f, 0f, 0.009f);
            bool g = KineticBulletModel.TryRicochet(shot, hit.LocalNormal, steel, ref glance, out _, out _, out _);
            if (g) observed++;
            if (hit.HeightFraction > 0.75f) { crownCount++; if (g) crownGlance++; }
            if (hit.HeightFraction > 0.35f && hit.HeightFraction < 0.55f && Math.Abs(hit.AzimuthDegrees) < 20f)
            {
                browCount++;
                if (g) browGlance++;
            }
        }
        check(covered > 10000, "Level shots reach the shell: " + covered);
        near(expected / covered, observed / covered, 0.01, "Observed glance share matches the obliquity distribution");
        check(crownCount > 200 && browCount > 200, "Crown and brow samples present");
        check(crownGlance / crownCount > browGlance / browCount + 0.3,
            "Crown glances far more than the brow: " + (crownGlance / crownCount).ToString("0.00") + " vs " + (browGlance / browCount).ToString("0.00"));
    }
}
