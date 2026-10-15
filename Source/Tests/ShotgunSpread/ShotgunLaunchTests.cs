using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace;
using UnityEngine;
using Verse;

internal static class ShotgunLaunchTests
{
    private static readonly List<Projectile_ShotgunPellet> pellets = new List<Projectile_ShotgunPellet>();
    private static Map map;
    private static ThingDef projectile;
    private static int shots;
    private static bool fail;
    private static bool throwShot;
    private static ShotgunLaunch observed;
    private static bool missLong;
    private static readonly AccessTools.FieldRef<Projectile, Vector3> projectileOrigin = AccessTools.FieldRefAccess<Projectile, Vector3>("origin");
    private static readonly AccessTools.FieldRef<Projectile, Vector3> projectileDestination = AccessTools.FieldRefAccess<Projectile, Vector3>("destination");
    private static readonly AccessTools.FieldRef<Projectile, Thing> projectileLauncher = AccessTools.FieldRefAccess<Projectile, Thing>("launcher");
    private sealed class TestShotgun : Verb_ShootShotgun
    {
        internal int count;
        protected override int PelletCount => count;
        internal bool Fire() => base.TryCastShot();
    }
    internal static ThingDef ProjectileDef()
    {
        var def = Program.Bare<ThingDef>();
        def.defName = "FixturePellet";
        def.thingClass = typeof(Projectile_ShotgunPellet);
        def.projectile = new ProjectileProperties { speed = 45f, damageDef = new DamageDef { defName = "FixtureBullet" } };
        AccessTools.Field(typeof(ProjectileProperties), "damageAmountBase").SetValue(def.projectile, 6);
        AccessTools.Field(typeof(ProjectileProperties), "armorPenetrationBase").SetValue(def.projectile, 0.1f);
        def.modExtensions = new List<DefModExtension> { new ShotgunProjectileExtension { internalPenetrationChance = 1f } };
        return def;
    }
    private static Projectile_ShotgunPellet NewPellet(ThingDef def)
    {
        var pellet = Program.Bare<Projectile_ShotgunPellet>();
        pellet.def = def;
        pellet.thingIDNumber = 1000 + pellets.Count;
        pellet.extraDamages = new List<ExtraDamage>();
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pellet, (sbyte)0);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(pellet, new IntVec3(3, 0, 3));
        pellets.Add(pellet);
        return pellet;
    }
    private static bool FakeSpawn(ThingDef def, ref Thing __result) { __result = NewPellet(def); return false; }
    private static bool FakeBaseLaunch(Projectile __instance, Thing launcher, Vector3 origin,
        LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags)
    {
        projectileOrigin(__instance) = origin;
        projectileLauncher(__instance) = launcher;
        __instance.usedTarget = usedTarget;
        __instance.intendedTarget = intendedTarget;
        __instance.HitFlags = hitFlags;
        return false;
    }
    private static bool FakeShot(ref bool __result)
    {
        shots++;
        observed = ShotgunSpreadContext.Launching;
        if (throwShot) throw new InvalidOperationException("fixture failure");
        __result = !fail;
        if (__result)
        {
            var pellet = NewPellet(projectile);
            pellet.damageDefOverride = projectile.projectile.damageDef;
            pellet.extraDamages.Add(new ExtraDamage());
            pellet.Launch(null, new Vector3(3.5f, 0f, 3.5f), new IntVec3(3, 0, missLong ? 21 : 18), new IntVec3(3, 0, 18), ProjectileHitFlags.All);
        }
        return false;
    }
    private static ShotReport Report(float accuracy, float size)
    {
        object report = default(ShotReport);
        foreach (string field in new[] { "factorFromShooterAndDist", "factorFromEquipment", "factorFromWeather", "factorFromCoveringGas" })
            AccessTools.Field(typeof(ShotReport), field).SetValue(report, field == "factorFromShooterAndDist" ? accuracy : 1f);
        AccessTools.Field(typeof(ShotReport), "factorFromTargetSize").SetValue(report, size);
        return (ShotReport)report;
    }

    private static void CheckPawnAim(Verb verb)
    {
        var body = new BodyDef();
        Program.Part(body, "AimSurface", 1f);
        Pawn target = Program.Pawn(body);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(target, new IntVec3(3, 0, 8));
        Vector2 first = default(Vector2);
        foreach (float size in new[] { 0.5f, 2f })
        {
            var launch = new ShotgunLaunch(verb, 9, 1f) { report = Report(0.1f, size) };
            Rand.PushState(7891);
            try
            {
                launch.Capture(NewPellet(projectile), null, new Vector3(3.5f, 0f, 3.5f),
                    new IntVec3(3, 0, 13), target, ProjectileHitFlags.NonTargetWorld, false, null, null);
            }
            finally { Rand.PopState(); }
            Program.Check(launch.shell.aimError.magnitude < 0.3f, "Pawn aiming must replace the coarse vanilla miss tile with a close continuous error.");
            Program.Check(launch.handlingSpread > 1f && launch.handlingSpread <= 1.25f, "Poor aim must add only a modest handling penalty to base spread.");
            if (size == 0.5f) first = launch.shell.aimError;
            else Program.Check(first == launch.shell.aimError, "Target size must affect the silhouette without also worsening shared aim.");
        }
    }
    internal static void Run(Harmony harmony)
    {
        map = Program.Bare<Map>();
        map.components = new List<MapComponent> { new MapComponent_ShotgunShells(map) };
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map> { map });
        projectile = ProjectileDef();
        var baseLaunch = AccessTools.Method(typeof(Projectile), nameof(Projectile.Launch), new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) });
        var spawn = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(ThingDef), typeof(IntVec3), typeof(Map), typeof(WipeMode) });
        var shot = AccessTools.Method(typeof(Verb_Shoot), "TryCastShot");
        harmony.Patch(baseLaunch, prefix: new HarmonyMethod(typeof(ShotgunLaunchTests), nameof(FakeBaseLaunch)));
        harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(ShotgunLaunchTests), nameof(FakeSpawn)));
        harmony.Patch(shot, prefix: new HarmonyMethod(typeof(ShotgunLaunchTests), nameof(FakeShot)));
        var verb = new TestShotgun { count = 9, verbProps = new VerbProperties_Shotgun { pelletCount = 9, spreadAngle = 0.9f } };
        var outer = new ShotgunLaunch(verb, 3, 1f);
        ShotgunSpreadContext.Launching = outer;
        Program.Check(verb.Fire() && shots == 1 && pellets.Count == 9, "Nine pellets must use exactly one vanilla shot/accuracy/ammo path.");
        ShotgunShell shell = pellets[0].Shell;
        Program.Check(pellets.All(pellet => pellet.Shell == shell) && shell.projectiles.Count == 9, "Every pellet must share one shell.");
        Program.Check(pellets.All(pellet => pellet.damageDefOverride == projectile.projectile.damageDef && pellet.extraDamages.Count == 1), "Clones must retain projectile damage overrides and weapon traits.");
        Program.Check(pellets.Select(pellet => projectileDestination(pellet)).Distinct().Count() > 1, "Pellets must have distinct continuous trajectories.");
        Program.Check(ShotgunSpreadContext.Launching == outer, "Launch context must be restored.");
        pellets.Clear(); shots = 0; missLong = true;
        Program.Check(verb.Fire() && pellets.Average(pellet => projectileDestination(pellet).z) > 20f,
            "World-target pellets must preserve the forward component of the common aiming miss.");
        missLong = false;
        pellets.Clear(); shots = 0;
        verb.count = 20;
        Program.Check(verb.Fire() && shots == 1 && pellets.Count == 20, "Runtime launcher buckshot must use one shot for twenty pellets.");
        pellets.Clear(); shots = 0;
        verb.count = 1;
        Program.Check(verb.Fire() && shots == 1 && pellets.Count == 1 && observed == null && pellets[0].Shell == null, "Single-projectile modes must not inherit an outer shell.");
        fail = true; shots = 0; pellets.Clear(); verb.count = 9;
        Program.Check(!verb.Fire() && shots == 1 && pellets.Count == 0, "A failed vanilla shot must not produce pellets.");
        fail = false; throwShot = true;
        try { verb.Fire(); throw new Exception("Expected a launch failure."); }
        catch (InvalidOperationException) { }
        Program.Check(ShotgunSpreadContext.Launching == outer, "Launch exceptions must restore the context.");
        throwShot = false;
        ShotgunSpreadContext.Launching = null;
        // Leave other fixtures free to exercise the actual base methods.
        harmony.Unpatch(baseLaunch, HarmonyPatchType.Prefix, harmony.Id);
        harmony.Unpatch(spawn, HarmonyPatchType.Prefix, harmony.Id);
        harmony.Unpatch(shot, HarmonyPatchType.Prefix, harmony.Id);
        CheckPawnAim(verb);
        var weapon = Program.Bare<ThingDef>();
        AccessTools.Field(typeof(ThingDef), "verbs").SetValue(weapon, new List<VerbProperties>
        {
            new VerbProperties_Shotgun { verbClass = typeof(TestShotgun), spreadAngle = 0.9f },
            new VerbProperties_Shotgun { verbClass = typeof(TestShotgun), spreadAngle = 0.9f },
            new VerbProperties { verbClass = typeof(Verb_Shoot) }
        });
        Program.Check(ShotgunDisplayStats.SpreadAngles(weapon).SequenceEqual(new[] { 0.9f }), "Info stats must merge duplicate firing-mode angles.");
    }
}
