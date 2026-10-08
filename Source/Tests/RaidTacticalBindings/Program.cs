using System;
using System.IO;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Helodrace;
using RimWorld;
using Verse;
using System.Collections.Generic;
using Helodrace.Squads;
using System.Xml.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using Verse.AI;

internal static class Program
{
    private sealed class GameDependentStock : StockGenerator_SingleDef
    {
        public override bool HandlesThingDef(ThingDef def) => throw new Exception("No storyteller at startup.");
    }
    private static void CheckStartupStockGenerators()
    {
        Type trading = typeof(TacticalEngineSelection).Assembly.GetType("Helodrace.Economy.HelodMoneyTrading", true);
        var check = AccessTools.Method(trading, "ExistingMoneyStock");
        var money = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        bool Existing(StockGenerator generator) => (bool)check.Invoke(null, new object[] { generator, money });
        if (Existing(new GameDependentStock()) || Existing(new StockGenerator_Tomes()))
            throw new Exception("Game-dependent subclass should not be queried during static initialization.");
        var simple = new StockGenerator_SingleDef();
        AccessTools.Field(typeof(StockGenerator_SingleDef), "thingDef").SetValue(simple, money);
        if (!Existing(simple) || !Existing((StockGenerator)Activator.CreateInstance(typeof(TacticalEngineSelection).Assembly
            .GetType("Helodrace.Economy.StockGenerator_HelodMoney", true), true)))
            throw new Exception("Existing money generators must prevent duplicate registration.");
        Console.WriteLine("PASS: 4 startup stock-generator isolation and money registration checks.");
    }
    private static int Main(string[] commandLineArgs)
    {
        // Check target signatures and injected private fields against the real
        // installed game. This is not a Unity/Mono runtime patch simulation.
        string managed = @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed";
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return RunReferencedTests(commandLineArgs);
    }
    // Resolve installed Unity/game assemblies before JIT examines game types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunReferencedTests(string[] args)
    {
        try
        {
            if (args.Contains("--r7")) { TacticalEngineSelectionTests.Run(); TacticalPersistenceTests.Run(); TacticalRetirementTests.Run(); TacticalAuditProtectionTests.Run(); TacticalRemovalTests.Run(); TacticalWorkBudgetTests.Run(); return 0; }
            if (args.Contains("--defense")) { TacticalDefenseTests.Run(); TacticalWorkBudgetTests.Run(); return 0; }
            if (args.Contains("--r6")) { TacticalFieldTests.Run(); TacticalSupportTests.Run(); TacticalContactTests.Run(); TacticalWorkBudgetTests.Run(); return 0; }
            if (args.Contains("--smoke")) { CheckSmokeGases(); return 0; }
            if (args.Contains("--hemostasis")) { PartHemostasisTests.Run(); return 0; }
            if (args.Contains("--r5")) { TacticalCooperationTests.Run(); TacticalWorkBudgetTests.Run(); TacticalContactTests.Run(); return 0; }
            if (args.Contains("--r4")) { TacticalContactTests.Run(); TacticalWorkBudgetTests.Run(); TacticalOpeningTests.Run(); return 0; }
            var patches = new[] {
                typeof(Patch_EdgeWalkInGroups_Organization), typeof(Patch_RaidStrategy_OrganizationMinimum),
                typeof(Patch_BreachedDoor_NoRandomBreakdown), typeof(Patch_BreachedDoor_AlwaysOpen),
                typeof(Patch_BreachedDoor_FreePassage), typeof(Patch_BreachedDoor_BreakdownRepaired), typeof(Patch_BreachedDoor_OrdinaryRepair)
            }.Concat(typeof(TacticalEngineSelection).Assembly.GetTypes().Where(type => type.Namespace == "Helodrace.Tactics"
                && type.Name.StartsWith("Patch_NewTactical_") && type.IsDefined(typeof(HarmonyPatch), false))).ToArray();
            foreach (Type patch in patches)
            {
                HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
                MethodInfo target = AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                if (target == null) throw new Exception("Missing target: " + patch.Name);
                foreach (MethodInfo hook in patch.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.Name == "Prefix" || method.Name == "Postfix"))
                    foreach (ParameterInfo parameter in hook.GetParameters())
                    {
                        Type actual = parameter.ParameterType.IsByRef
                            ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                        Type expected;
                        if (parameter.Name.StartsWith("___"))
                            expected = AccessTools.Field(info.declaringType, parameter.Name.Substring(3))?.FieldType;
                        else if (parameter.Name == "__instance") expected = info.declaringType;
                        else if (parameter.Name == "__result") expected = target.ReturnType;
                        else if (parameter.Name == "__state") expected = AccessTools.Method(patch, "Prefix")
                            ?.GetParameters().FirstOrDefault(value => value.Name == "__state")?.ParameterType.GetElementType();
                        else expected = target.GetParameters().FirstOrDefault(value => value.Name == parameter.Name)?.ParameterType;
                        if (expected == null || !actual.IsAssignableFrom(expected))
                            throw new Exception(patch.Name + ": invalid binding " + parameter.Name);
                    }
                Console.WriteLine("PASS: " + patch.Name);
            }
            CheckDoorFaultHooks(); CheckStartupStockGenerators(); CheckSmokeGases();
            RaidSpawnGenerationTests.Run(); RaidTacticalUnitTests.Run(); OrganizationEdgeArrivalTests.Run();
            TacticalEngineSelectionTests.Run(); TacticalWorkBudgetTests.Run(); TacticalOpeningTests.Run();
            PawnProfilerBindingTests.Run(); TacticalBreachRecoveryTests.Run(); TacticalChargeTests.Run();
            TacticalRoomScanTests.Run(); TacticalEntryAllocationTests.Run(); TacticalContactTests.Run();
            TacticalCooperationTests.Run(); TacticalFieldTests.Run(); TacticalSupportTests.Run(); TacticalDefenseTests.Run();
            TacticalPersistenceTests.Run(); TacticalRetirementTests.Run(); TacticalAuditProtectionTests.Run(); TacticalRemovalTests.Run();
            PartHemostasisTests.Run(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void CheckSmokeGases()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        XElement gasDefs = XDocument.Load(Path.Combine(root, "Defs/GasDefs_Helod.xml")).Root;
        HelodGasDef Gas(string name, ushort index)
        {
            XElement xml = gasDefs.Elements().Single(def => (string)def.Element("defName") == name);
            return new HelodGasDef { defName = name, index = index,
                accuracyFactor = float.Parse((string)xml.Element("accuracyFactor"), CultureInfo.InvariantCulture) };
        }
        var hc = Gas("HD_HCSmokeGrid", 0);
        var wp = Gas("HD_WhitePhosphorusSmokeGrid", 1);
        XElement grenadeDefs = XDocument.Load(Path.Combine(root, "Defs/GreatWar/Items/Grenades_GreatWar.xml")).Root;
        foreach (string name in new[] { "HD_Grenade_M8_Item", "HD_Projectile_M8_Round" })
        {
            XElement xml = grenadeDefs.Elements().Single(def => (string)def.Element("defName") == name);
            Check(xml.Descendants("gasDef").Any(gas => gas.Value == hc.defName)
                && !xml.Descendants("postExplosionGasType").Any(gas => gas.Value == "BlindSmoke"),
                name + " must release dedicated HC rather than vanilla smoke.");
        }
        ThingDef Projectile(HelodGasDef gas, GasType? vanilla = null)
        {
            var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            def.projectile = new ProjectileProperties { damageDef = new DamageDef { defName = "Smoke" },
                postExplosionGasType = vanilla };
            def.modExtensions = new List<DefModExtension>();
            if (gas != null) def.modExtensions.Add(new HelodGasOnExplosionExtension { gasDef = gas });
            return def;
        }
        Check(RaidSmokeUtility.IsScreeningProjectile(Projectile(hc)), "HC must remain available to raid smoke planning.");
        Check(RaidSmokeUtility.IsScreeningProjectile(Projectile(null, GasType.BlindSmoke)), "Vanilla smoke remains a screening option.");
        Check(!RaidSmokeUtility.IsScreeningProjectile(Projectile(wp)), "Incendiary WP is not a safe screening grenade.");
        Check(!RaidSmokeUtility.IsScreeningProjectile(Projectile(new HelodGasDef { defName = "HD_CSGasGrid" }, GasType.BlindSmoke)),
            "Vanilla smoke on a tear-gas grenade must not make it a safe screening option.");
        Check(!RaidSmokeUtility.IsScreeningProjectile(Projectile(null)), "Smoke damage without a gas source cannot screen movement.");
        var fragmented = Projectile(hc);
        fragmented.modExtensions.Add(new Helodrace.ModernWar.FragmentationGrenadeExtension());
        Check(!RaidSmokeUtility.IsScreeningProjectile(fragmented), "Fragmentation cannot become a movement screen.");
        var flashbang = Projectile(hc);
        flashbang.modExtensions.Add(new Helodrace.ModernWar.FlashbangProjectileExtension());
        Check(!RaidSmokeUtility.IsScreeningProjectile(flashbang), "Flashbang cannot become a movement screen.");

        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(3, 1, 3) };
        map.cellIndices = new CellIndices(3, 3);
        Type stateType = typeof(HelodGasDef).Assembly.GetType("Helodrace.HelodGasState", true);
        object state = RuntimeHelpers.GetUninitializedObject(stateType);
        var grids = new[] { new byte[9], new byte[9] };
        AccessTools.Field(stateType, "map").SetValue(state, map);
        AccessTools.Field(stateType, "defs").SetValue(state, new List<HelodGasDef> { hc, wp });
        AccessTools.Field(stateType, "grids").SetValue(state, grids);
        AccessTools.Field(stateType, "activeCellCounts").SetValue(state, new[] { 1, 1 });
        AccessTools.Field(stateType, "totalActiveCells").SetValue(state, 2);
        var line = new ShootLine(new IntVec3(0, 0, 1), new IntVec3(2, 0, 1));
        float Factor() => (float)AccessTools.Method(stateType, "AccuracyFactorAlong").Invoke(state, new object[] { line });
        grids[0][4] = 255;
        Check(Factor() == 0.25f, "Shots crossing HC must use the XML accuracy coefficient 0.25.");
        grids[0][4] = 0;
        grids[1][4] = 255;
        Check(Factor() == 0.35f, "Shots crossing WP must use the XML accuracy coefficient 0.35.");
        grids[0][4] = 255;
        Check(Factor() == 0.25f, "Overlapping smoke uses the strongest screen without multiplying gas penalties.");
        grids[0][4] = grids[1][4] = 0;
        grids[0][0] = 255;
        Check(Factor() == 1f, "A cloud outside the shot line must not reduce accuracy.");
        AccessTools.Field(stateType, "totalActiveCells").SetValue(state, 0);
        Check(Factor() == 1f, "An empty gas state must leave accuracy unchanged.");
        Console.WriteLine($"PASS: {checks} HC/WP grenade selection and shot-line checks (XML and real game classes)");
    }
    private static void CheckDoorFaultHooks()
    {
        var door = new Building_Door();
        var fault = new CompDoorBreachFault { parent = door };
        var breakdown = new CompBreachableDoorBreakdown { parent = door };
        AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(door,
            new List<ThingComp> { breakdown, fault });
        bool alwaysOpen = false;
        Patch_BreachedDoor_AlwaysOpen.Postfix(door, ref alwaysOpen);
        if (alwaysOpen) throw new Exception("An intact door must keep its normal closing behavior.");
        AccessTools.Field(typeof(CompDoorBreachFault), "jammed").SetValue(fault, true);
        Patch_BreachedDoor_AlwaysOpen.Postfix(door, ref alwaysOpen);
        if (!alwaysOpen) throw new Exception("A breached door must stay open.");
        bool closesSoon = true;
        Patch_BreachedDoor_FreePassage.Postfix(door, ref closesSoon);
        if (closesSoon) throw new Exception("Pathfinding must see a breached door as freely passable.");
        if (Patch_BreachedDoor_NoRandomBreakdown.Prefix(breakdown)
            || !Patch_BreachedDoor_NoRandomBreakdown.Prefix(new CompBreakdownable()))
            throw new Exception("Added door breakdowns must be deliberate while normal machinery still breaks down.");
        Patch_BreachedDoor_BreakdownRepaired.Postfix(breakdown);
        alwaysOpen = false;
        closesSoon = true;
        Patch_BreachedDoor_AlwaysOpen.Postfix(door, ref alwaysOpen);
        Patch_BreachedDoor_FreePassage.Postfix(door, ref closesSoon);
        if (fault.Jammed || alwaysOpen || !closesSoon)
            throw new Exception("Vanilla breakdown repair must restore normal closing and passage checks.");
        alwaysOpen = true;
        Patch_BreachedDoor_AlwaysOpen.Postfix(door, ref alwaysOpen);
        if (!alwaysOpen) throw new Exception("A naturally always-open door must retain its original behavior.");
        Console.WriteLine("PASS: 6 door-fault lifecycle hook checks (unspawned real game objects)");
    }
}
