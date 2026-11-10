using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace.Ballistics;

// Phase 3: continuous torso/head yaw, sprite selection and stance data.
internal static class OrientationTests
{
    public static void Run(string root, Action<bool, string> check, Action<double, double, double, string> near)
    {
        // Stepping is rate limited and takes the short way round.
        near(10, BodyOrientationRules.Step(0f, 90f, 10f), 1e-4, "Step limited by rate");
        near(90, BodyOrientationRules.Step(85f, 90f, 10f), 1e-4, "Step lands on target");
        near(350, BodyOrientationRules.Step(0f, 270f, 10f), 1e-4, "Step turns the short way");
        near(5, BodyOrientationRules.Step(355f, 20f, 10f), 1e-4, "Step wraps through north");
        near(42, BodyOrientationRules.Step(42f, 200f, 0f), 1e-4, "Zero rate does not turn");

        near(70, BodyOrientationRules.ClampHead(0f, 120f), 1e-4, "Head limited to 70 degrees right");
        near(290, BodyOrientationRules.ClampHead(0f, 200f), 1e-4, "Head limited to 70 degrees left");
        near(30, BodyOrientationRules.ClampHead(0f, 30f), 1e-4, "Head within reach unchanged");

        // Sprite: nearest cardinal, held through the hysteresis band.
        near(0, BodyOrientationRules.SpriteYaw(30f, 0f), 1e-4, "Small turn keeps the north sprite");
        near(0, BodyOrientationRules.SpriteYaw(48f, 0f), 1e-4, "Hysteresis holds north past 45");
        near(90, BodyOrientationRules.SpriteYaw(52f, 0f), 1e-4, "Past the band switches to east");
        near(90, BodyOrientationRules.SpriteYaw(42f, 90f), 1e-4, "Hysteresis holds east below 45");
        near(180, BodyOrientationRules.SpriteYaw(200f, 0f), 1e-4, "Far turn picks nearest cardinal");
        near(0, BodyOrientationRules.SpriteYaw(352f, 270f), 1e-4, "Sprite wraps to north");
        for (float yaw = 0f; yaw < 360f; yaw += 7f)
        {
            float sprite = BodyOrientationRules.SpriteYaw(yaw, BodyOrientationRules.SpriteYaw(yaw, 0f));
            check(Math.Abs(BallisticGeometry.DeltaDegrees(sprite, yaw)) <= 45f + BodyOrientationRules.SpriteHysteresis,
                "Shown side is never more than 50 degrees from the torso: " + yaw);
        }

        // Targets: bladed stance offsets the torso from the aim; idle follows the intended facing.
        var rifle = new StanceParameters(20f, 0f, 7f, 14f, 0.75f, 3);
        near(110, BodyOrientationRules.TorsoTarget(true, 90f, 0f, rifle), 1e-4, "Aiming torso = aim + offset");
        near(180, BodyOrientationRules.TorsoTarget(false, 90f, 180f, rifle), 1e-4, "Idle torso follows facing");
        near(90, BodyOrientationRules.HeadTarget(true, 90f, 110f, rifle), 1e-4, "Aiming head looks at the target");

        // Analytic advance: ticking every tick and jumping straight to a later tick agree.
        var a = new BodyOrientationState { Stance = rifle };
        var b = new BodyOrientationState { Stance = rifle };
        a.Reset(0f, 100);
        b.Reset(0f, 100);
        a.SetTargets(110f, 90f);
        b.SetTargets(110f, 90f);
        for (int tick = 101; tick <= 110; tick++) a.Advance(tick);
        b.Advance(110);
        near(a.TorsoYaw, b.TorsoYaw, 1e-3, "Per-tick and jump advance agree (torso)");
        near(a.HeadYaw, b.HeadYaw, 1e-3, "Per-tick and jump advance agree (head)");
        near(70, a.TorsoYaw, 1e-3, "Rifle torso turns 7 degrees per tick");
        check(Math.Abs(BallisticGeometry.DeltaDegrees(a.TorsoYaw, a.HeadYaw)) <= BodyOrientationRules.MaxHeadTurn + 1e-3,
            "Head stays within reach while turning");
        a.Advance(130);
        near(110, a.TorsoYaw, 1e-3, "Torso settles on target");
        near(90, a.HeadYaw, 1e-3, "Head settles on target");
        a.Moving = true;
        a.SetTargets(200f, 200f);
        a.Advance(140);
        near(110 + 7 * 0.75 * 10, a.TorsoYaw, 1e-3, "Moving turns slower");
        a.Advance(139);
        near(162.5, a.TorsoYaw, 1e-3, "Time going backwards does nothing");
        var fresh = new BodyOrientationState();
        fresh.SetTargets(250f, 250f);
        fresh.Advance(5);
        check(fresh.Initialized && Math.Abs(fresh.TorsoYaw - 250f) < 1e-3, "First advance snaps to the target");

        // Family heuristic over the Helod arsenal's shapes.
        StanceFamily F(float mass, float range, int burst, bool shotgun = false, bool explosive = false, bool ranged = true) =>
            BodyOrientationRules.Classify(new WeaponStanceFacts(ranged, mass, range, burst, shotgun, explosive));
        check(F(0.79f, 23.9f, 1) == StanceFamily.Pistol, "Glock 19 is a pistol");
        check(F(1.22f, 22.9f, 1) == StanceFamily.Pistol, "Colt 1860 is a pistol");
        check(F(3.7f, 17.9f, 1, shotgun: true) == StanceFamily.Shotgun, "M1897 is a shotgun");
        check(F(7.5f, 31f, 7) == StanceFamily.MachineGun, "M249 is a machine gun");
        check(F(8.8f, 31.9f, 3) == StanceFamily.MachineGun, "BAR is a machine gun");
        check(F(4.2f, 43.9f, 1) == StanceFamily.Sniper, "M1903A4 is a precision rifle");
        check(F(4.31f, 34.9f, 1) == StanceFamily.Rifle, "M1 Garand is a rifle");
        check(F(3.2f, 27.9f, 6) == StanceFamily.Rifle, "M4A1 is a rifle");
        check(F(4.8f, 22.9f, 5) == StanceFamily.Rifle, "SMG uses the rifle stance");
        check(F(2.93f, 27.9f, 1, explosive: true) == StanceFamily.Launcher, "M79 is a launcher");
        check(F(0f, 0f, 0, ranged: false) == StanceFamily.Unarmed, "Melee and empty hands are unarmed");

        if (root == null) return;
        var profiles = XDocument.Load(Path.Combine(root, "Defs/Ballistics/StanceProfiles.xml")).Root.Elements()
            .ToDictionary(e => Enum.Parse<StanceFamily>((string)e.Element("family")), e => e);
        check(profiles.Count == Enum.GetValues(typeof(StanceFamily)).Length, "One stance profile per family");
        float Rate(StanceFamily family) => float.Parse((string)profiles[family].Element("torsoTurnRate"), CultureInfo.InvariantCulture);
        check(Rate(StanceFamily.Pistol) > Rate(StanceFamily.Rifle), "Pistols turn faster than rifles");
        check(Rate(StanceFamily.Rifle) > Rate(StanceFamily.Sniper), "Rifles turn faster than precision rifles");
        check(Rate(StanceFamily.Sniper) > Rate(StanceFamily.MachineGun), "Precision rifles turn faster than machine guns");
        foreach (var profile in profiles.Values)
        {
            float offset = profile.Element("torsoOffsetDegrees") == null ? 0f
                : float.Parse((string)profile.Element("torsoOffsetDegrees"), CultureInfo.InvariantCulture);
            check(Math.Abs(offset) <= 45f, "Stance offset leaves the target in front: " + (string)profile.Element("defName"));
        }
    }
}
