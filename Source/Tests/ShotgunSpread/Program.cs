using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace;
using RimWorld;
using UnityEngine;
using Verse;

internal static class Program
{
    private static int checks;
    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    internal static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < 0.001f, message + $" ({actual} != {expected})");
    internal static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static BodyPartRecord Part(BodyDef body, string name, float area, BodyPartRecord parent = null, bool inside = false)
    {
        var part = new BodyPartRecord { body = body, def = new BodyPartDef { defName = name }, parent = parent,
            coverageAbs = area, depth = inside ? BodyPartDepth.Inside : BodyPartDepth.Outside, height = BodyPartHeight.Middle };
        parent?.parts.Add(part);
        body.AllParts.Add(part);
        if (parent == null) body.corePart = part;
        return part;
    }
    internal static Pawn Pawn(BodyDef body)
    {
        var pawn = Bare<Pawn>();
        pawn.def = Bare<ThingDef>();
        pawn.def.defName = "FixturePawn";
        pawn.def.race = new RaceProperties { body = body };
        pawn.health = Bare<Pawn_HealthTracker>();
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.needs = new Pawn_NeedsTracker();
        return pawn;
    }
    private static bool LogError(string text) => throw new Exception("Game error: " + text);
    private static bool ResolveFixtureType(string typeName, ref Type __result)
    {
        __result = typeof(Program).Assembly.GetType(typeName);
        return __result == null;
    }
    internal static bool NoEngineEffects() => false;
    private static bool PartMaxHealth(ref float __result) { __result = 100f; return false; }

    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error)
        {
            for (Exception nested = error; nested != null; nested = nested.InnerException)
            {
                Console.Error.WriteLine(nested.GetType().FullName);
                Console.Error.WriteLine(nested.Message);
            }
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }
    private static void Run()
    {
        DeepProfiler.enabled = false;
        var harmony = new Harmony("Helodrace.Tests.ShotgunSpread");
        harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(LogError)));
        harmony.Patch(AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly)), prefix: new HarmonyMethod(typeof(Program), nameof(ResolveFixtureType)));
        AccessTools.Field(typeof(GenTypes), "allTypesCached").SetValue(null, typeof(Program).Assembly.GetTypes().Concat(new[] { typeof(BodyDef) }).ToList());
        foreach (var method in new[] { AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache)),
            AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged)),
            AccessTools.Method(typeof(Hediff_Injury), nameof(Hediff_Injury.PostAdd)) })
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(NoEngineEffects)));
        harmony.Patch(AccessTools.Method(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth)), prefix: new HarmonyMethod(typeof(Program), nameof(PartMaxHealth)));
        Current.Game = Bare<Game>();
        Current.Game.components = new List<GameComponent> { new GameComponent_ShotgunWounds(Current.Game) };
        foreach (Type type in typeof(ShotgunShell).Assembly.GetTypes().Where(type => type.Namespace == "Helodrace" && type.IsDefined(typeof(HarmonyPatch), false)))
        {
            harmony.CreateClassProcessor(type).Patch();
            checks++;
        }
        Rand.PushState(83721);
        try
        {
            BallisticsTests();
            AccuracyTests();
            ShotgunMotionTests.Run();
            AnatomyTests();
            ShotgunLaunchTests.Run(harmony);
            ShotgunDamageTests.Run(harmony);
            ShotgunInjuryMergeTests.Run(harmony);
            RoundTrip();
        }
        finally { Rand.PopState(); harmony.UnpatchAll(harmony.Id); Current.Game = null; }
        Console.WriteLine($"Shotgun system: {checks} checks passed.");
    }

    private static void BallisticsTests()
    {
        Near(ShotgunBallistics.PatternDiameter(0.9f, 20f), 2f * ShotgunBallistics.PatternDiameter(0.9f, 10f), "Pattern must grow linearly with distance.");
        Near(ShotgunBallistics.PatternDiameter(0f, 50f), 0f, "Zero spread must stay tight.");
        Near(ShotgunBallistics.Energy(40f, 40f), 0.5f, "Half-distance must halve energy.");
        Check(ShotgunBallistics.Energy(5f, 40f) > ShotgunBallistics.Energy(25f, 40f), "Energy must decline with distance.");
        var normal = ShotgunBallistics.BodyRadii(1f, true);
        var large = ShotgunBallistics.BodyRadii(8f, true);
        var tiny = ShotgunBallistics.BodyRadii(0.125f, true);
        Near(large.x, normal.x * 2f, "Large races must scale by linear size.");
        int near = 0, far = 0, big = 0, small = 0, fringe = 0;
        for (int i = 0; i < 10000; i++)
        {
            Vector2 slope = ShotgunBallistics.Sample(0.9f);
            if (ShotgunBallistics.Hits(slope * 4f, normal)) near++;
            if (ShotgunBallistics.Hits(slope * 25f, normal)) far++;
            if (ShotgunBallistics.Hits(slope * 25f, large)) big++;
            if (ShotgunBallistics.Hits(slope * 25f, tiny)) small++;
            if (ShotgunBallistics.Hits(slope * 15f + new Vector2(0.6f, 0f), normal)) fringe++;
        }
        Check(near > 9900 && near > far * 2, "Near targets must catch tight groups; distant targets must lose pellets.");
        Check(big > far && far > small, "A fixed pattern must hit large races more often than small races.");
        Check(fringe > 0 && fringe < far, "The edge of a shared missed aim must produce partial hits.");
        var shell = new ShotgunShell { origin = Vector3.zero, targetPoint = new Vector3(0f, 0f, 10f), aimError = new Vector2(0.2f, 0.1f) };
        Vector2 offset = shell.OffsetAt(shell.targetPoint, new Vector2(0.01f, 0.02f));
        Near(offset.x, 0.3f, "Pellets must add their offset to the common horizontal aiming error.");
        Near(offset.y, 0.3f, "Pellets must share the vertical aiming error.");
    }

    private static void AccuracyTests()
    {
        const int shells = 10000;
        var radii = ShotgunBallistics.BodyRadii(1f, true);
        float noviceNear = 0f, noviceFar = 0f;
        foreach (float accuracy in new[] { 0f, 1f })
        foreach (float distance in new[] { 5f, 8f, 12f, 20f })
        {
            int total = 0, four = 0, five = 0;
            Rand.PushState(57419);
            try
            {
                for (int shot = 0; shot < shells; shot++)
                {
                    float deviation = ShotgunBallistics.AimDeviation(accuracy, distance);
                    var aim = new Vector2(Rand.Gaussian(0f, deviation), Rand.Gaussian(0f, deviation));
                    int hits = 0;
                    for (int pellet = 0; pellet < 9; pellet++)
                        if (ShotgunBallistics.Hits(aim + ShotgunBallistics.Sample(1f * ShotgunBallistics.HandlingSpread(accuracy)) * distance, radii)) hits++;
                    total += hits;
                    if (hits >= 4) four++;
                    if (hits >= 5) five++;
                }
            }
            finally { Rand.PopState(); }
            float mean = (float)total / shells;
            Console.WriteLine($"Accuracy={accuracy:0}, distance={distance:0}: {mean:0.00}/9 pellets, >=4 {100f * four / shells:0.0}%, >=5 {100f * five / shells:0.0}% (unchoked 1 degree).");
            if (accuracy == 0f)
            {
                if (distance == 5f) Check(five > shells * 0.99f, "Even the worst aim must usually land five pellets at five tiles.");
                if (distance == 8f)
                {
                    noviceNear = mean;
                    Check(four > shells * 0.94f && five > shells * 0.88f, "Close-range poor aim must usually land four to five pellets without a hit quota.");
                }
                if (distance == 20f) { noviceFar = mean; Check(mean < noviceNear * 0.5f, "Unchoked spread and aim error must still penalize distant shots."); }
            }
            else
            {
                if (distance == 8f) Check(mean > noviceNear + 1f, "Better aim must put a tighter group on target.");
                if (distance == 20f) Check(mean > noviceFar * 1.5f && mean < 6f, "Skilled distant shots must improve without eliminating physical spread.");
            }
        }
        Near(ShotgunBallistics.HandlingSpread(1f), 1f, "Perfect aim must retain the full unchoked base pattern.");
        Near(ShotgunBallistics.HandlingSpread(-1f), ShotgunBallistics.HandlingSpread(0f), "Accuracy inputs must be clamped.");
        Check(ShotgunBallistics.AimDeviation(0f, 8f) > ShotgunBallistics.AimDeviation(1f, 8f), "Accuracy must reduce shared muzzle error.");
    }

    private static void AnatomyTests()
    {
        var body = new BodyDef();
        var torso = Part(body, "AlienCore", 0.45f);
        var heart = Part(body, "ExtraPump", 0.10f, torso, true);
        var head = Part(body, "SensoryBulb", 0.15f, torso);
        var brain = Part(body, "ControlNode", 0.05f, head, true);
        var wing = Part(body, "Membrane", 0.15f, torso);
        var tail = Part(body, "BalanceFin", 0.10f, torso);
        var pawn = Pawn(body);
        var damage = new DamageInfo(new DamageDef { defName = "FixtureBullet" }, 6f, angle: 0);
        ShotgunBodyProfile profile = ShotgunBodyProfile.For(body);
        Check(profile.surfaces.Count == 4, "Only actual external body parts belong to the silhouette.");
        Near(profile.surfaces.Sum(surface => surface.area), 1f, "Surface areas must form one normalized silhouette.");
        Near(profile.Owner(torso).area, 0.55f, "Internal organ area must return to the outer surface.");
        Check(profile.Owner(brain).part == head && profile.Owner(heart).part == torso, "Custom organs must map to their nearest outside owner.");
        Check(profile.ChooseSurface(pawn, wing, damage, 0f) == wing, "A tight group must stay at its preselected surface.");
        var seen = new HashSet<BodyPartRecord>();
        for (int i = 0; i < 250; i++)
        {
            BodyPartRecord selected = profile.ChooseSurface(pawn, wing, damage, 2f);
            Check(selected.depth == BodyPartDepth.Outside, "Direct hits must not select internal organs.");
            seen.Add(selected);
        }
        Check(seen.Count > 1, "A broad pattern must reach neighbouring surfaces.");
        var internalSeen = new HashSet<BodyPartRecord>();
        for (int i = 0; i < 30; i++) internalSeen.Add(profile.ChooseInternal(pawn, torso, damage.Def));
        Check(internalSeen.SetEquals(new[] { heart }), "Torso penetration must not jump into the head or wings.");
        Check(profile.ChooseInternal(pawn, wing, damage.Def) == null, "Species without an organ in that surface must keep surface-only damage.");
        Near(ShotgunBodyProfile.Distance(profile.Owner(wing), profile.Owner(tail)), ShotgunBodyProfile.Distance(profile.Owner(tail), profile.Owner(wing)), "Anatomical proximity must be symmetric.");
        torso.coverageAbs -= 0.05f;
        Part(body, "SecondPump", 0.05f, torso, true);
        var withExtraOrgan = new ShotgunBodyProfile(body);
        Near(withExtraOrgan.Owner(torso).area, profile.Owner(torso).area, "Extra organs must not increase external hit area.");
        var wrapper = Part(body, "StructuralWrapper", 0f, torso);
        wrapper.def.conceptual = true;
        wing.parent = wrapper;
        wrapper.parts.Add(wing);
        var wrapped = new ShotgunBodyProfile(body);
        Near(ShotgunBodyProfile.Distance(wrapped.Owner(wing), wrapped.Owner(tail)), ShotgunBodyProfile.Distance(profile.Owner(wing), profile.Owner(tail)), "Zero-area structural wrappers must not alter spread distance.");
        pawn.health.hediffSet.hediffs.Add(new Hediff_MissingPart { pawn = pawn, Part = wing });
        Check(profile.ChooseSurface(pawn, wing, damage, 0f) == null, "Lost limbs must not redirect tightly grouped pellets.");
        Check(profile.ChooseSurface(pawn, wing, damage, 0.01f) == null, "Near-zero spread must not jump to a distant intact limb.");
        var malformed = new BodyDef();
        Part(malformed, "InternalOnly", 1f, inside: true);
        Check(new ShotgunBodyProfile(malformed).surfaces.Count == 0, "A body without usable surfaces must allow vanilla fallback.");
        Check(ShotgunBodyProfile.InternalPath(wing, brain).Count == 0, "Penetration must never cross into a different surface branch.");
        var shell = new ShotgunShell();
        ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = pawn, shell = shell, damageDef = damage.Def, relativeSpread = 2f };
        try
        {
            BodyPartRecord firstEntry = null;
            Check(!Patch_DamageWorker_ShotgunSurface.Prefix(damage, pawn, ref firstEntry) && firstEntry != null,
                "A pellet must choose an external entry once.");
            BodyPartRecord secondEntry = null;
            Patch_DamageWorker_ShotgunSurface.Prefix(damage, pawn, ref secondEntry);
            Check(secondEntry == firstEntry, "Fragments and same-type extra damage from one pellet must reuse its entry.");
            pawn.health.hediffSet.hediffs.Add(new Hediff_MissingPart { pawn = pawn, Part = firstEntry });
            Patch_DamageWorker_ShotgunSurface.Prefix(damage, pawn, ref secondEntry);
            Check(secondEntry == null, "A destroyed entry must not redirect later damage from the same pellet.");
        }
        finally { ShotgunSpreadContext.Impact = null; }
    }

    private static void RoundTrip()
    {
        var body = new BodyDef { defName = "ShotgunPersistenceBody" };
        var part = Part(body, "Surface", 1f);
        DefDatabase<BodyDef>.Add(body);
        var pawn = Pawn(body);
        pawn.thingIDNumber = 111;
        var first = Bare<Projectile_ShotgunPellet>();
        var second = Bare<Projectile_ShotgunPellet>();
        first.def = second.def = Bare<ThingDef>();
        first.def.defName = "FixturePellet";
        first.thingIDNumber = 112;
        second.thingIDNumber = 113;
        var shell = new ShotgunShell { id = 112, origin = Vector3.zero, targetPoint = new Vector3(0f, 0f, 15f), aimError = new Vector2(0.1f, 0.2f) };
        shell.CentreFor(pawn, new DamageDef());
        var tracker = new MapComponent_ShotgunShells(null);
        tracker.Register(first, shell);
        tracker.Register(second, shell);
        tracker.Register(first, shell);
        Check(shell.projectiles.Count == 2, "Pellet registration must be idempotent.");
        var injury = new Hediff_Injury { pawn = pawn, def = new HediffDef { defName = "FixtureWound", injuryProps = new InjuryProps() }, Part = part, Severity = 12f };
        injury.loadID = 115;
        pawn.health.hediffSet.hediffs.Add(injury);
        var ledger = new GameComponent_ShotgunWounds(null);
        ledger.Register(injury, shell.id, first.thingIDNumber);
        ledger.Register(injury, shell.id, second.thingIDNumber);
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shotgun-roundtrip.xml");
        try
        {
            Scribe.saver.InitSaving(path, "test");
            foreach (object reference in new object[] { pawn, first, second, injury }) Scribe.saver.loadIDsErrorsChecker.RegisterDeepSaved(reference, "fixture");
            Scribe_Deep.Look(ref tracker, "tracker", new object[] { null });
            Scribe_Deep.Look(ref ledger, "ledger", new object[] { null });
            Scribe.saver.FinalizeSaving();
            tracker = null;
            ledger = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref tracker, "tracker", new object[] { null });
            Scribe_Deep.Look(ref ledger, "ledger", new object[] { null });
            var directory = (LoadedObjectDirectory)AccessTools.Field(typeof(CrossRefHandler), "loadedObjectDirectory").GetValue(Scribe.loader.crossRefs);
            foreach (ILoadReferenceable reference in new ILoadReferenceable[] { pawn, first, second, injury }) directory.RegisterLoaded(reference);
            Scribe.loader.FinalizeLoading();
            ShotgunShell loaded = tracker.ShellFor(first);
            Check(loaded != shell && loaded == tracker.ShellFor(second), "Loaded pellets must share one restored shell.");
            Check(loaded.id == shell.id && loaded.aimError == shell.aimError && loaded.CentreFor(pawn, new DamageDef()) == part, "Save/load must retain common aim and the preselected body centre.");
            Check(ledger.For(injury).shellId == shell.id && ledger.For(injury).pelletCount == 2, "Wound provenance and pellet counts must survive save/load.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
    }
}
