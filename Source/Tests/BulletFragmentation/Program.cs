using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml.Linq;
using System.IO;
using HarmonyLib;
using Helodrace;
using RimWorld;
using Verse;
using UnityEngine;

// The game assembly provides armor/injury methods; only weapon rendering is a fixture.
namespace Helodrace.ModernWar
{
    public sealed class CompModularWeaponNode : ThingComp
    {
        public WeaponProps Props = new WeaponProps();
        public ModularWeaponConvertedStats ConvertedStats = new ModularWeaponConvertedStats();
    }
    public sealed class WeaponProps { public bool isAssemblyRoot = true; }
    public sealed class ModularWeaponConvertedStats
    {
        public float BarrelLengthInches = 16f, MuzzleVelocityOffsetMetersPerSecond;
        public float DamageTransferFactor = 1f, PenetrationEfficiencyFactor = 1f;
        public float RangeFactor = 1f;
        public ModularWeaponBallisticProfile Ballistics;
    }
    public sealed class Verb_ShootModularUnderbarrel : Verb_Shoot { }
}

internal static class Program
{
    private static int checks;
    private static readonly List<DamageInfo> wounds = new List<DamageInfo>();
    private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static bool FakeFinalize(Pawn pawn, float totalDamage, DamageInfo dinfo, DamageWorker.DamageResult result, ref float __result)
    {
        wounds.Add(dinfo);
        result.AddPart(pawn, dinfo.HitPart);
        result.totalDamageDealt += totalDamage;
        __result = totalDamage;
        return false;
    }
    private static bool NoEffects() => false;
    private static bool WeaponStatFixture(Thing thing, StatDef stat, ref float __result)
    {
        __result = stat != null && stat == StatDefOf.RangedWeapon_RangeMultiplier
            ? thing?.TryGetComp<Helodrace.ModernWar.CompModularWeaponNode>()?.ConvertedStats.RangeFactor ?? 1f : 1f;
        return false;
    }
    // Headless type lookup only: native Scribe_Deep still constructs and roundtrips
    // the real snapshot, without scanning unrelated Unity-dependent game types.
    private static bool SnapshotTypeFixture(string typeName, ref Type __result)
    {
        if (typeName != typeof(BulletBallisticsSnapshot).FullName) return true;
        __result = typeof(BulletBallisticsSnapshot); return false;
    }
    private static bool failImpact;
    private static BulletFragmentationImpact observedImpact;
    private static bool FakeImpact()
    {
        observedImpact = BulletFragmentationImpact.Current;
        if (failImpact) throw new InvalidOperationException("fixture impact failure");
        return false;
    }
    private static bool PositionFixture(ref Vector3 __result) { __result = new Vector3(0, 0, 15); return false; }
    private static bool LogError(string text) => throw new Exception("Game error: " + text);
    private static BodyPartRecord Part(BodyDef body, string name, BodyPartRecord parent = null, bool inside = false)
    {
        var part = new BodyPartRecord { body = body, def = new BodyPartDef { defName = name }, parent = parent,
            coverageAbs = 0.1f, depth = inside ? BodyPartDepth.Inside : BodyPartDepth.Outside };
        parent?.parts.Add(part);
        body.AllParts.Add(part);
        if (parent == null) body.corePart = part;
        return part;
    }

    private static int Main(string[] args)
    {
        var harmony = new Harmony("Helodrace.Tests.BulletFragmentation");
        try
        {
            DeepProfiler.enabled = false;
            harmony.Patch(AccessTools.Method(typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor)),
                prefix: new HarmonyMethod(typeof(Program), nameof(NoEffects)));
            harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(LogError)));
            harmony.Patch(AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue)),
                prefix: new HarmonyMethod(typeof(Program), nameof(WeaponStatFixture)));
            foreach (Type type in typeof(BulletFragmentationExtension).Assembly.GetTypes()
                .Where(type => type.IsDefined(typeof(HarmonyPatch), false)))
            {
                harmony.CreateClassProcessor(type).Patch();
                checks++;
            }
            var finalize = AccessTools.Method(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury", new[]
                { typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult) });
            harmony.Patch(finalize, prefix: new HarmonyMethod(typeof(Program), nameof(FakeFinalize)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache)),
                prefix: new HarmonyMethod(typeof(Program), nameof(NoEffects)));
            var settings = new BulletFragmentationExtension();
            float Strength(float distance, float barrel, float armor) => settings.Strength(
                BulletBallisticsRules.RetainedVelocity(BulletBallisticsRules.BarrelVelocityFactor(barrel, 16, .35f), distance, 55), armor);
            float close = Strength(5, 16, 0);
            Check(close > Strength(30, 16, 0), "Distance must reduce fragmentation.");
            Check(close > Strength(5, 10.3f, 0), "Short barrels must reduce fragmentation.");
            Check(close < Strength(5, 20, 0), "Long barrels must increase fragmentation.");
            Check(close > Strength(5, 16, 0.5f), "Penetrated armor must reduce fragmentation even after a full penetration.");
            Check(Strength(5, 16, 0.5f) > Strength(5, 16, 1f), "Multiple layers must accumulate resistance.");
            Check(Strength(500, 20, 0) == 0, "Below-threshold impacts must not fragment.");
            Check(Strength(0, 0, 0) == Strength(0, 16, 0), "Fixed weapons must use the ammo reference barrel.");
            Check(Strength(-1, 16, -1) == Strength(0, 16, 0), "Invalid negative inputs must not boost strength.");

            var body = new BodyDef();
            var torso = Part(body, "Torso");
            var arm = Part(body, "Arm", torso);
            var hand = Part(body, "Hand", arm);
            var finger = Part(body, "Finger", hand);
            var head = Part(body, "Head", torso);
            var brain = Part(body, "Brain", head, true);
            var heart = Part(body, "Heart", torso, true);
            var lung = Part(body, "Lung", torso, true);
            var pawn = Bare<Pawn>();
            pawn.def = Bare<ThingDef>();
            pawn.def.race = new RaceProperties { body = body };
            AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.def.race, new FleshTypeDef { isOrganic = true });
            pawn.health = Bare<Pawn_HealthTracker>();
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
            pawn.health.hediffSet = new HediffSet(pawn);
            var nearArm = BulletFragmentationImpact.Neighbors(pawn, arm);
            Check(nearArm.Contains(torso) && nearArm.Contains(hand) && !nearArm.Contains(head) && !nearArm.Contains(finger),
                "An arm hit must stay on immediate neighbors, never skip to the head or fingers.");
            Check(BulletFragmentationImpact.Neighbors(pawn, heart).Contains(lung), "Organs in the same cavity may receive fragments.");
            Check(!BulletFragmentationImpact.Neighbors(pawn, torso).Contains(brain), "A torso hit cannot skip the head to reach the brain.");
            var missing = new Hediff_MissingPart { pawn = pawn, Part = arm };
            pawn.health.hediffSet.hediffs.Add(missing);
            Check(!BulletFragmentationImpact.Neighbors(pawn, torso).Contains(arm), "Missing body parts must be excluded.");
            Check(BulletFragmentationImpact.Neighbors(pawn, hand).Count == 0, "Children of missing parts must also be excluded.");
            pawn.health.hediffSet.hediffs.Remove(missing);

            var sharp = new DamageArmorCategoryDef { defName = "Sharp" };
            DamageArmorCategoryDefOf.Sharp = sharp;
            var bullet = new DamageDef { defName = "FixtureBullet", armorCategory = sharp };
            var fragment = new DamageDef { defName = "HD_BulletFragment", workerClass = typeof(DamageWorker_AddInjury), armorCategory = sharp };
            DefDatabase<DamageDef>.Add(fragment);
            var worker = new DamageWorker_AddInjury { def = bullet };
            var damage = new DamageInfo(bullet, 16, hitPart: torso);
            var result = new DamageWorker.DamageResult();
            BulletFragmentationImpact NewImpact() => new BulletFragmentationImpact
            {
                pawn = pawn, damageDef = bullet, armorPart = torso,
                settings = new BulletFragmentationExtension { chance = 1, damageFraction = 0.7f }, velocityFactor = 1f
            };
            void Apply(float amount = 16) => finalize.Invoke(worker, new object[] { pawn, amount, damage, result });

            Rand.PushState(83721);
            try
            {
                BulletFragmentationImpact.Current = NewImpact();
                Apply();
                var fragments = wounds.Where(wound => wound.Def == fragment).ToList();
                Check(fragments.Count >= 2 && fragments.Count <= 5, "A qualifying hit must produce multiple bounded fragments.");
                Check(fragments.All(wound => wound.Amount <= 2.5f && !wound.AllowDamagePropagation), "Each fragment must be small and cannot propagate.");
                Check(fragments.Sum(wound => wound.Amount) <= 16 * 0.7f + 0.001f, "Bonus damage must stay within the post-armor budget.");
                Check(fragments.All(wound => BulletFragmentationImpact.Neighbors(pawn, torso).Contains(wound.HitPart)), "Every wound must be local to the impact.");
                Check(result.LastHitPart == torso, "Fragments must not redirect native small-pawn propagation to a different body part.");
                Check(fragments.Select(wound => wound.HitPart).Distinct().Count() == Math.Min(fragments.Count, 5), "Fragments must spread across distinct available neighbors first.");
                int before = wounds.Count;
                Apply();
                Check(wounds.Count == before + 1, "One bullet can fragment only once, including propagation and recursive finalization.");
                wounds.Clear(); BulletFragmentationImpact.Current = NewImpact();
                Apply(2);
                Check(wounds.Where(wound => wound.Def == fragment).Sum(wound => wound.Amount) <= 2 * 0.7f + 0.001f,
                    "Armor-reduced primary damage must reduce the fragment budget, never restore full projectile damage.");
                wounds.Clear(); BulletFragmentationImpact.Current = null; Apply();
                Check(wounds.Count == 1, "Ordinary bullets must not fragment.");
                wounds.Clear(); BulletFragmentationImpact.Current = NewImpact(); Apply(0);
                Check(wounds.Count == 1, "A stopped bullet must produce no fragments.");
                wounds.Clear(); BulletFragmentationImpact.Current = NewImpact();
                damage.Def = new DamageDef { defName = "Blunt" }; Apply();
                Check(wounds.Count == 1, "Armor-converted blunt damage must not fragment.");
                damage.Def = bullet;
                wounds.Clear(); BulletFragmentationImpact.Current = NewImpact();
                BulletFragmentationImpact.Current.velocityFactor = BulletBallisticsRules.RetainedVelocity(1, 500, 55); Apply();
                Check(wounds.Count == 1, "Long-range impacts below the velocity threshold must not fragment.");
                wounds.Clear(); BulletFragmentationImpact.Current = NewImpact();
                BulletFragmentationImpact.RecordArmor(pawn, 0.5f, 16, bullet);
                BulletFragmentationImpact.RecordArmor(pawn, 0.7f, 16, bullet);
                Check(Math.Abs(BulletFragmentationImpact.Current.penetratedArmor - 1.2f) < 0.001f, "Traversed clothing and plates must accumulate.");
                BulletFragmentationImpact.RecordArmor(pawn, 5, 0, bullet);
                Check(BulletFragmentationImpact.Current.penetratedArmor < 1.21f, "Blocked layers are not penetrated layers.");
                var armorMethod = AccessTools.Method(typeof(ArmorUtility), "ApplyArmor");
                harmony.Patch(armorMethod, prefix: new HarmonyMethod(typeof(Program), nameof(NoEffects)));
                BulletFragmentationImpact.Current.penetratedArmor = 0;
                armorMethod.Invoke(null, new object[] { 16f, 0.2f, 0.4f, Bare<Apparel>(), bullet, pawn, false });
                Check(Math.Abs(BulletFragmentationImpact.Current.penetratedArmor - 0.4f) < 0.001f,
                    "The patched native armor signature must capture a penetrated apparel layer.");
                armorMethod.Invoke(null, new object[] { 16f, 0.2f, 0.8f, null, bullet, pawn, false });
                Check(Math.Abs(BulletFragmentationImpact.Current.penetratedArmor - 0.4f) < 0.001f,
                    "The native natural-armor pass must not add clothing resistance.");
                Patch_ArmorUtility_FragmentationEntry.Prefix(pawn, arm);
                Check(BulletFragmentationImpact.Current.penetratedArmor == 0 && BulletFragmentationImpact.Current.armorPart == arm,
                    "Each native armor pass must reset the entry and traversed layers.");
                wounds.Clear(); BulletFragmentationImpact.Current = NewImpact();
                AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.def.race, new FleshTypeDef { isOrganic = false }); Apply();
                Check(wounds.Count == 1, "Mechanical pawns must not receive flesh fragmentation wounds.");
            }
            finally { Rand.PopState(); BulletFragmentationImpact.Current = null; }
            LifecycleTests(harmony, pawn, bullet);
            ModularRangeTests.Run(Check, harmony);
            DefinitionTests(args.Length > 0 ? args[0] : ".");
            Console.WriteLine($"Bullet fragmentation: {checks} checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void LifecycleTests(Harmony harmony, Pawn pawn, DamageDef bullet)
    {
        harmony.Patch(AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly)),
            prefix: new HarmonyMethod(typeof(Program), nameof(SnapshotTypeFixture)));
        harmony.Patch(AccessTools.Method(typeof(Projectile), nameof(Projectile.Launch), new[]
            { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoEffects)));
        harmony.Patch(AccessTools.Method(typeof(Projectile), nameof(Projectile.ExposeData)), prefix: new HarmonyMethod(typeof(Program), nameof(NoEffects)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Projectile), nameof(Projectile.ExactPosition)), prefix: new HarmonyMethod(typeof(Program), nameof(PositionFixture)));
        harmony.Patch(AccessTools.Method(typeof(Bullet), "Impact"), prefix: new HarmonyMethod(typeof(Program), nameof(FakeImpact)));
        var pellet = Bare<Projectile_FragmentingBullet>();
        pellet.def = Bare<ThingDef>();
        pellet.def.projectile = new ProjectileProperties { damageDef = bullet };
        AccessTools.Field(typeof(ProjectileProperties), "damageAmountBase").SetValue(pellet.def.projectile, 13);
        AccessTools.Field(typeof(ProjectileProperties), "armorPenetrationBase").SetValue(pellet.def.projectile, .28f);
        pellet.def.modExtensions = new List<DefModExtension> { new BulletFragmentationExtension(), new BulletBallisticsExtension() };
        var weapon = Bare<ThingWithComps>();
        var comp = new Helodrace.ModernWar.CompModularWeaponNode { parent = weapon };
        comp.ConvertedStats.BarrelLengthInches = 10.3f;
        comp.ConvertedStats.MuzzleVelocityOffsetMetersPerSecond = 20;
        comp.ConvertedStats.DamageTransferFactor = 1.1f;
        comp.ConvertedStats.PenetrationEfficiencyFactor = .95f;
        AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(weapon, new List<ThingComp> { comp });
        pellet.Launch(null, Vector3.zero, pawn, pawn, ProjectileHitFlags.All, equipment: weapon);
        var snapshotField = AccessTools.Field(typeof(Projectile_BallisticBullet), "ballistics");
        var captured = (BulletBallisticsSnapshot)snapshotField.GetValue(pellet);
        float savedVelocity = captured.muzzleVelocityFactor;
        int savedDamage = pellet.DamageAmount;
        float savedPenetration = pellet.ArmorPenetration;
        comp.ConvertedStats.BarrelLengthInches = 20;
        comp.ConvertedStats.DamageTransferFactor = 2;
        Check(Math.Abs(captured.muzzleVelocityFactor - (BulletBallisticsRules.BarrelVelocityFactor(10.3f, 16, .35f) + 20f/900f)) < .001f,
            "Changing equipment after firing cannot change the captured muzzle velocity.");
        Check(pellet.DamageAmount == savedDamage && Math.Abs(pellet.ArmorPenetration - savedPenetration) < .001f,
            "Damage and penetration must not re-read changed equipment in flight.");
        Check(savedDamage == captured.DamageAt(15) && Math.Abs(savedPenetration - captured.PenetrationAt(15)) < .001f,
            "Live projectile properties must consume the common snapshot at actual travel distance.");
        var alternate = new BulletBallisticsSnapshot { referenceDamage=captured.referenceDamage,
            referencePenetration=captured.referencePenetration, velocityHalfDistance=captured.velocityHalfDistance,
            damageTransfer=captured.damageTransfer, penetrationEfficiency=captured.penetrationEfficiency,
            muzzleVelocityFactor=1.2f };
        Check(alternate.DamageAt(15)==captured.DamageAt(15)
            && Math.Abs(alternate.PenetrationAt(15)-captured.PenetrationAt(15))<.001f,
            "Captured barrel velocity cannot scale direct projectile damage or penetration.");
        Check(new BulletFragmentationExtension().Strength(alternate.VelocityAt(15),0)
            > new BulletFragmentationExtension().Strength(captured.VelocityAt(15),0),
            "Captured barrel velocity still changes fragmentation at the same distance.");
        var fixedBullet = Bare<Projectile_BallisticBullet>(); fixedBullet.def = pellet.def;
        fixedBullet.Launch(null, Vector3.zero, pawn, pawn, ProjectileHitFlags.All);
        Check(fixedBullet.DamageAmount == 13 && Math.Abs(fixedBullet.ArmorPenetration-.28f)<.001f,
            "Non-modular weapons sharing these projectile defs must retain native primary damage/AP.");
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fragmentation-roundtrip.xml");
        try
        {
            Scribe.saver.InitSaving(path, "test");
            pellet.ExposeData();
            Scribe.saver.FinalizeSaving();
            pellet = Bare<Projectile_FragmentingBullet>();
            Scribe.loader.InitLoading(path);
            pellet.ExposeData();
            Scribe.loader.FinalizeLoading();
            captured = (BulletBallisticsSnapshot)snapshotField.GetValue(pellet);
            Check(Math.Abs(captured.muzzleVelocityFactor - savedVelocity) < .001f
                && captured.referenceDamage == 13 && Math.Abs(captured.referencePenetration - .28f) < .001f,
                "An airborne bullet must retain velocity, damage and penetration after save/load.");
            Check(Math.Abs(captured.damageTransfer-1.1f)<.001f && Math.Abs(captured.penetrationEfficiency-.95f)<.001f,
                "Save/load must retain non-default transfer/penetration contributions.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        pellet.def = Bare<ThingDef>();
        pellet.def.projectile = new ProjectileProperties { damageDef = bullet };
        pellet.def.modExtensions = new List<DefModExtension> { new BulletFragmentationExtension(), new BulletBallisticsExtension() };
        var outer = new BulletFragmentationImpact();
        BulletFragmentationImpact.Current = outer;
        var impact = AccessTools.Method(typeof(Projectile_FragmentingBullet), "Impact");
        try
        {
            impact.Invoke(pellet, new object[] { pawn, false });
            Check(observedImpact?.pawn == pawn && Math.Abs(observedImpact.velocityFactor - captured.VelocityAt(15)) < .001f,
                "Fragmentation must use the same captured velocity and travel distance as primary damage/AP.");
            Check(BulletFragmentationImpact.Current == outer, "Impact must restore the surrounding context.");
            impact.Invoke(pellet, new object[] { pawn, true });
            Check(observedImpact == null && BulletFragmentationImpact.Current == outer, "Shield interception must never enable fragmentation.");
            impact.Invoke(pellet, new object[] { null, false });
            Check(observedImpact == null, "Ground impacts must not enable wounds.");
            failImpact = true;
            try { impact.Invoke(pellet, new object[] { pawn, false }); }
            catch (System.Reflection.TargetInvocationException) { }
            Check(BulletFragmentationImpact.Current == outer, "A failing impact must not leak fragmentation state into later damage.");
        }
        finally { failImpact = false; BulletFragmentationImpact.Current = null; }
    }

    private static void DefinitionTests(string root)
    {
        var defs = System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "Defs"), "*.xml", System.IO.SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Root.Elements()).ToList();
        var optedIn = defs.Where(node => node.Element("modExtensions")?.Elements("li")
            .Any(li => (string)li.Attribute("Class") == "Helodrace.BulletFragmentationExtension") == true).ToList();
        Check(optedIn.Count == 6, "Only the six selected projectile defs may opt in.");
        Check(optedIn.All(node => (string)node.Element("thingClass") == "Helodrace.Projectile_FragmentingBullet"), "Each opt-in must use the fragmentation projectile.");
        Check(optedIn.All(node => node.Element("modExtensions").Elements().Any(e => (string)e.Attribute("Class") == "Helodrace.BulletBallisticsExtension")),
            "Every fragmenting projectile must use the shared ballistic profile.");
        Check(optedIn.All(node => !node.Element("modExtensions").Elements().Where(e => (string)e.Attribute("Class") == "Helodrace.BulletFragmentationExtension")
            .Any(e => e.Element("referenceBarrelInches") != null || e.Element("velocityHalfDistance") != null)),
            "Fragmentation must not retain a second, divergent barrel/velocity model.");
        Check(!optedIn.Any(node => ((string)node.Element("defName")).Contains("M995") || ((string)node.Element("defName")).Contains("TACTX")),
            "AP and expanding-copper selections must retain their existing behavior.");
        var injury = defs.Single(node => (string)node.Element("defName") == "HD_FragmentationWound");
        Check((string)injury.Element("injuryProps").Element("canMerge") == "false", "Small wounds must remain individually visible.");
        var damage = defs.Single(node => (string)node.Element("defName") == "HD_BulletFragment");
        Check((string)damage.Element("hediff") == "HD_FragmentationWound" && (string)damage.Element("harmAllLayersUntilOutside") == "false",
            "Fragment damage must select its wound without duplicating outer-layer injuries.");
    }
}
