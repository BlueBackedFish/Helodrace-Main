using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace;
using RimWorld;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    private static BodyPartRecord Part(string name, BodyPartRecord parent = null)
    {
        var part = new BodyPartRecord
        {
            def = new BodyPartDef { defName = name }, parent = parent,
            coverageAbs = 0.1f, height = BodyPartHeight.Middle, depth = BodyPartDepth.Inside
        };
        parent?.parts.Add(part);
        return part;
    }

    private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    private static Pawn Pawn()
    {
        var pawn = Bare<Pawn>();
        pawn.health = Bare<Pawn_HealthTracker>();
        pawn.health.hediffSet = new HediffSet(pawn);
        return pawn;
    }

    private static void SameRegion(BodyPartRecord hit, int spread, params BodyPartRecord[] expected)
    {
        var actual = ShotgunSpreadRules.Candidates(hit, spread).ToArray();
        Check(actual.Length == actual.Distinct().Count(), "Candidates must not be duplicated.");
        Check(actual.Length == expected.Length && actual.All(expected.Contains), "Unexpected spread region.");
    }

    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().FullName);
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    private static void Run()
    {
        DeepProfiler.enabled = false;
        var harmony = new Harmony("Helodrace.Tests.ShotgunSpread");
        harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(LogError)));
        harmony.Patch(AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly)),
            prefix: new HarmonyMethod(typeof(Program), nameof(ResolveFixtureType)));
        var torso = Part("Torso");
        var chest = Part("Chest", torso);
        var lung = Part("LeftLung", chest);
        var otherLung = Part("RightLung", chest);
        var heart = Part("Heart", chest);
        var head = Part("Head", torso);
        var brain = Part("Brain", head);
        SameRegion(lung, 0, lung);
        SameRegion(lung, 1, lung, otherLung, heart);
        SameRegion(lung, 2, lung, otherLung, heart, chest, head);
        SameRegion(lung, int.MaxValue, lung, otherLung, heart, chest, head);
        SameRegion(torso, 0, torso);
        SameRegion(torso, 1, torso, chest, head);
        Check(!ShotgunSpreadRules.Candidates(lung, 2).Contains(brain), "Do not include grandchildren of another parent.");
        Check(!ShotgunSpreadRules.Candidates(null, 1).Any(), "Null anchor must have no candidates.");

        var pawn = Pawn();
        var otherPawn = Pawn();
        var shell = new ShotgunShell(1);
        shell.Remember(pawn, lung);
        shell.Remember(pawn, head);
        shell.Remember(otherPawn, brain);
        Check(shell.AnchorFor(pawn).part == lung, "The first hit must remain the anchor.");
        Check(shell.AnchorFor(otherPawn).part == brain, "Each pawn needs its own anchor.");
        Check(new ShotgunShell(1).AnchorFor(pawn) == null, "Separate shells must not share anchors.");
        Check(new ShotgunShell(-5).spreadFactor == 0, "Negative spread must clamp to zero.");

        var bulletDamage = new DamageDef { defName = "TestBullet" };
        var damage = new DamageInfo(bulletDamage, 6f, angle: 0);
        Rand.PushState(12345);
        try
        {
            for (int i = 0; i < 100; i++)
                Check(chest.parts.Contains(ShotgunSpreadRules.Choose(pawn, lung, 1, damage)), "Spread 1 must stay within the chest's children.");
            pawn.health.hediffSet.hediffs.Add(new Hediff_MissingPart { pawn = pawn, Part = lung });
            Check(ShotgunSpreadRules.Choose(pawn, lung, 0, damage) == null, "A missing focused part must not redirect damage.");
            for (int i = 0; i < 25; i++)
                Check(ShotgunSpreadRules.Choose(pawn, lung, 1, damage) != lung, "Missing parts must be excluded.");
            pawn.health.hediffSet.hediffs.Add(new Hediff_MissingPart { pawn = pawn, Part = chest });
            Check(ShotgunSpreadRules.Choose(pawn, lung, 1, damage) == null, "A missing parent must exclude all its children.");
        }
        finally { Rand.PopState(); }

        // Validate Harmony binding against the installed game's actual methods.
        foreach (Type type in new[] { typeof(Patch_Projectile_Launch_ShotgunShell), typeof(Patch_Bullet_Impact_ShotgunShell), typeof(Patch_DamageWorker_ShotgunHitPart) })
        {
            harmony.CreateClassProcessor(type).Patch();
            checks++;
        }

        ShotgunSpreadContext.Impact = new ShotgunImpact { shell = shell, pawn = pawn, damageDef = bulletDamage };
        var lookup = AccessTools.Method(typeof(DamageWorker_AddInjury), "GetExactPartFromDamageInfo");
        var worker = new DamageWorker_AddInjury();
        Check(lookup.Invoke(worker, new object[] { damage, pawn }) == null, "Patched lookup must suppress an exhausted region.");
        BodyPartRecord result = null;
        Check(Patch_DamageWorker_ShotgunHitPart.Prefix(damage, otherPawn, ref result), "Unrelated pawns must use vanilla selection.");
        var explicitDamage = damage;
        explicitDamage.SetHitPart(head);
        Check(Patch_DamageWorker_ShotgunHitPart.Prefix(explicitDamage, pawn, ref result), "Explicit propagation parts must be preserved.");
        var previous = ShotgunSpreadContext.Impact;
        Patch_Bullet_Impact_ShotgunShell.Prefix(Bare<Bullet>(), null, false, out ShotgunImpact state);
        Check(ShotgunSpreadContext.Impact == null, "Unrelated impacts must suspend the context.");
        Patch_Bullet_Impact_ShotgunShell.Finalizer(state);
        Check(ShotgunSpreadContext.Impact == previous, "Impact finalizer must restore the previous context.");
        ShotgunSpreadContext.Impact = null;
        LaunchTests(harmony);
        RoundTrip(torso, pawn, lung);
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine($"Shotgun spread: {checks} checks passed.");
    }

    private sealed class TestShotgun : Verb_ShootShotgun
    {
        internal int count;
        protected override int PelletCount => count > 0 ? count : base.PelletCount;
        internal bool Fire() => base.TryCastShot();
    }

    private static readonly System.Collections.Generic.List<ShotgunShell> launched = new System.Collections.Generic.List<ShotgunShell>();
    private static bool failFirst;
    private static bool failLater;
    private static bool throwShot;

    private static bool FakeShot(ref bool __result)
    {
        launched.Add(ShotgunSpreadContext.Launching);
        if (throwShot) throw new InvalidOperationException("test launch failure");
        __result = !(failFirst || (failLater && launched.Count > 1));
        return false;
    }

    private static void LaunchTests(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Verb_Shoot), "TryCastShot"),
            prefix: new HarmonyMethod(typeof(Program), nameof(FakeShot)));
        var verb = new TestShotgun { verbProps = new VerbProperties_Shotgun { pelletCount = 9, spreadFactor = 2 } };
        var outerShell = new ShotgunShell(0);
        ShotgunSpreadContext.Launching = outerShell;
        Check(verb.Fire() && launched.Count == 9, "One shotgun shell must launch all nine pellets.");
        Check(launched.All(shell => shell == launched[0]) && launched[0].spreadFactor == 2,
            "Every pellet must share the configured shell.");
        Check(ShotgunSpreadContext.Launching == outerShell, "Launch context must be restored.");
        launched.Clear();
        verb.count = 20;
        Check(verb.Fire() && launched.Count == 20, "Runtime buckshot pellet counts must share the same behavior.");
        launched.Clear();
        verb.count = 1;
        Check(verb.Fire() && launched.Count == 1 && launched[0] == null, "Single projectiles must not inherit an outer shell.");
        launched.Clear();
        verb.count = 9;
        failFirst = true;
        Check(!verb.Fire() && launched.Count == 1, "A failed initial shot must stop the shell.");
        failFirst = false;
        failLater = true;
        launched.Clear();
        Check(verb.Fire() && launched.Count == 9, "Later pellet failures must not refund a fired shell.");
        failLater = false;
        throwShot = true;
        try { verb.Fire(); throw new Exception("Expected a launch exception."); }
        catch (InvalidOperationException) { }
        Check(ShotgunSpreadContext.Launching == outerShell, "Launch exceptions must restore the context.");
        throwShot = false;
        ShotgunSpreadContext.Launching = null;
    }

    private static void RoundTrip(BodyPartRecord torso, Pawn pawn, BodyPartRecord lung)
    {
        var body = new BodyDef { defName = "ShotgunTestBody", corePart = torso };
        foreach (var part in new[] { torso }.Concat(Descendants(torso)))
        {
            part.body = body;
            body.AllParts.Add(part);
        }
        // Only the fixture's types are needed; a headless process does not load
        // the game's optional Unity modules and mod assemblies.
        AccessTools.Field(typeof(GenTypes), "allTypesCached").SetValue(null,
            typeof(Program).Assembly.GetTypes().Concat(new[] { typeof(BodyDef) }).ToList());
        DefDatabase<BodyDef>.Add(body);
        pawn.def = Bare<ThingDef>();
        pawn.def.defName = "TestPawn";
        pawn.thingIDNumber = 1;
        var pellet = Bare<Bullet>();
        pellet.def = Bare<ThingDef>();
        pellet.def.defName = "TestPellet";
        pellet.thingIDNumber = 2;
        var secondPellet = Bare<Bullet>();
        secondPellet.def = pellet.def;
        secondPellet.thingIDNumber = 3;
        var shell = new ShotgunShell(2);
        shell.Remember(pawn, lung);
        var tracker = new MapComponent_ShotgunShells(null);
        tracker.Register(pellet, shell);
        tracker.Register(secondPellet, shell);
        tracker.Register(pellet, shell);
        Check(shell.projectiles.Count == 2, "Registering a pellet twice must be idempotent.");

        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shotgun-roundtrip.xml");
        try
        {
            Scribe.saver.InitSaving(path, "test");
            // The fixture supplies map-owned reference targets separately.
            Scribe.saver.loadIDsErrorsChecker.RegisterDeepSaved(pawn, "fixturePawn");
            Scribe.saver.loadIDsErrorsChecker.RegisterDeepSaved(pellet, "fixturePellet");
            Scribe.saver.loadIDsErrorsChecker.RegisterDeepSaved(secondPellet, "fixtureSecondPellet");
            Scribe_Deep.Look(ref tracker, "tracker", new object[] { null });
            Scribe.saver.FinalizeSaving();
            tracker = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref tracker, "tracker", new object[] { null });
            var directory = (LoadedObjectDirectory)AccessTools.Field(typeof(CrossRefHandler), "loadedObjectDirectory")
                .GetValue(Scribe.loader.crossRefs);
            directory.RegisterLoaded(pawn);
            directory.RegisterLoaded(pellet);
            directory.RegisterLoaded(secondPellet);
            Scribe.loader.FinalizeLoading();
            ShotgunShell loaded = tracker.ShellFor(pellet);
            Check(loaded != null && loaded != shell, "Save/load must restore the shell and runtime index.");
            Check(loaded == tracker.ShellFor(secondPellet), "Loaded pellets must share one shell instance.");
            Check(loaded.spreadFactor == 2 && loaded.AnchorFor(pawn).part == lung, "Save/load must preserve spread and the anatomical anchor.");
        }
        finally
        {
            Scribe.ForceStop();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static System.Collections.Generic.IEnumerable<BodyPartRecord> Descendants(BodyPartRecord part)
    {
        foreach (var child in part.parts)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static bool LogError(string text)
    {
        throw new Exception("Game error: " + text);
    }

    private static bool ResolveFixtureType(string typeName, ref Type __result)
    {
        __result = typeof(Program).Assembly.GetType(typeName);
        return __result == null;
    }
}
