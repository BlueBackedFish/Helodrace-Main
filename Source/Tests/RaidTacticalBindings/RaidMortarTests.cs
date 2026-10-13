using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;

internal static class RaidMortarTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        Check(RaidMortarPolicy.Withdraw(false, 12, 9, 0, 0) && !RaidMortarPolicy.Withdraw(false, 12, 10, 0, 0), "Ordinary mortar losses have a defined early withdrawal threshold.");
        Check(RaidMortarPolicy.Withdraw(true, 12, 10, 0, 0) && !RaidMortarPolicy.Withdraw(true, 12, 11, 0, 0), "Chemical crews withdraw earlier under losses.");
        Check(RaidMortarPolicy.Withdraw(true, 12, 12, 6, 0) && RaidMortarPolicy.Withdraw(false, 12, 12, 12, 0)
            && !RaidMortarPolicy.Withdraw(false, 12, 12, 11, 0), "Only a complete ammunition allocation ends the fire mission.");
        Check(RaidMortarPolicy.AssemblyExpired(2400, 1) && RaidMortarPolicy.AssemblyExpired(100, 3)
            && !RaidMortarPolicy.AssemblyExpired(2399, 2), "Assembly cannot retry indefinitely.");
        Check(RaidMortarPolicy.RecoveryExpired(900) && !RaidMortarPolicy.RecoveryExpired(899), "Unreachable equipment has a finite abandonment deadline.");
        Check(RaidMortarPolicy.Withdraw(false, 12, 12, 0, 18000), "A siege must not outlive its bounded mission indefinitely.");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Defs"))) directory = directory.Parent;
        string root = directory.FullName;
        var strategies = XDocument.Load(Path.Combine(root, "Defs/Organization/RaidStrategies.xml")).Root.Elements().ToArray();
        Check(strategies.Length == 3 && strategies.Any(n => n.Element("defName")?.Value == "HD_ChemicalMortarAssault"), "Only the authorized sweep and mortar mission types are defined.");
        foreach (var job in XDocument.Load(Path.Combine(root, "Defs/Organization/RaidMortarJobs.xml")).Root.Elements("JobDef"))
        {
            Type driver = typeof(RaidMortarPolicy).Assembly.GetType(job.Element("driverClass").Value, true);
            Check(driver.IsSubclassOf(typeof(JobDriver)), "Every raid job must have a real native driver.");
            foreach (var field in job.Elements()) Check(AccessTools.Field(typeof(JobDef), field.Name.LocalName) != null, "Unsupported job XML field: " + field.Name);
        }
        Check(AccessTools.Method(typeof(CompChangeableProjectile), "LoadShell", new[] { typeof(ThingDef), typeof(int) }) != null
            && AccessTools.Method(typeof(CompMannable), "ManForATick", new[] { typeof(Pawn) }) != null, "Ammo loading and turret crew integration must bind the installed game.");
        Check(!Helodrace.Tactics.GameComponent_TacticalCommands.CanOwnLordJob(new LordJob_HelodMortar())
            && Helodrace.Tactics.GameComponent_TacticalCommands.CanOwnLordJob(new LordJob_HelodSweep()),
            "The tactical defense scheduler must never hijack mortar assembly/man/pack work just because guards have Defend duties.");
        CheckPlan(Check); CheckSave(Check);
        Console.WriteLine("PASS: " + checks + " mortar budget, native work, bounded retreat/recovery and save-state checks.");
    }
    private static void CheckPlan(Action<bool,string> check)
    {
        var role = new RoleDef { isCommandRole = true, canCommandUnitLevels = new List<string> { "Squad" } };
        var formation = new FormationDef { defName = "test", unitLevel = "Squad", commanderRole = role, minimumPersonnel = 6, idealPersonnel = 6, maximumPersonnel = 6,
            requiredRoles = new List<FormationRoleSlot> { new FormationRoleSlot { pawnKind = new PawnKindDef { combatPower = 100 }, combatRole = new RoleDef(), commandRole = role, count = 6 } } };
        var doctrine = new DoctrineDef { availableFormations = new List<FormationDef> { formation }, formationPointTolerance = 0, maximumRaidPersonnel = 40 };
        var faction = new Faction { def = new FactionDef { modExtensions = new List<DefModExtension> { new FactionOrganizationExtension { doctrine = doctrine } } } };
        var strategy = new RaidStrategyDef { workerClass = typeof(RaidStrategyWorker_HelodMortar) };
        var parms = new PawnGroupMakerParms { points = 950, faction = faction, raidStrategy = strategy };
        var plan = OrganizationGenerator.PlanForRaid(parms);
        check(plan.Personnel == 6 && plan.initialRaidPoints == 950 && plan.SupportPointsRemaining == 350,
            "Physical mortar kit funds must be set aside before choosing complete infantry formations.");
        var organization = new CombatOrganization(); organization.SetBudget(plan);
        int kit = organization.ReserveSupport(350);
        check(kit != 0 && organization.SupportPointsRemaining == 0 && organization.FinishSupport(kit, true), "A mortar kit cannot mint free equipment outside its support budget.");
        strategy.modExtensions = new List<DefModExtension> { new MortarRaidExtension { chemical = true } };
        check(OrganizationGenerator.PlanForRaid(parms).Personnel == 0, "More expensive chemical kits must not produce a partial squad below their full budget.");
    }
    private static void CheckSave(Action<bool,string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-mortar-" + Guid.NewGuid() + ".xml");
        var state = new LordToilData_HelodMortar { Phase = RaidMortarPhase.Recover, Chemical = true, Initial = 12,
            Shots = 4, PhaseAt = 9000, Started = 600, Attempts = 2, RetryAt = 9120, Spot = new IntVec3(50, 0, 70), HadLoaded = true };
        var cache = (IDictionary)AccessTools.Field(typeof(GenTypes), "typeCache").GetValue(null);
        Type key = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
        cache[Activator.CreateInstance(key, new object[] { state.GetType().FullName, null })] = state.GetType();
        bool previous = ScribeMetaHeaderUtility.modListChanged; ScribeMetaHeaderUtility.modListChanged = true;
        try
        {
            Scribe.saver.InitSaving(path, "mortar"); Scribe_Deep.Look(ref state, "state"); Scribe.saver.FinalizeSaving();
            LordToilData_HelodMortar loaded = null;
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "state");
            Scribe.ExitNode();
            foreach (string field in new[] { "curXmlParent", "curParent", "curPathRelToParent" }) AccessTools.Field(typeof(ScribeLoader), field).SetValue(Scribe.loader, null);
            Scribe.mode = LoadSaveMode.Inactive;
            var crossRefs = AccessTools.Field(typeof(ScribeLoader), "crossRefs").GetValue(Scribe.loader);
            AccessTools.Method(crossRefs.GetType(), "ResolveAllCrossReferences").Invoke(crossRefs, null);
            var initer = AccessTools.Field(typeof(ScribeLoader), "initer").GetValue(Scribe.loader);
            AccessTools.Method(initer.GetType(), "DoAllPostLoadInits").Invoke(initer, null);
            check(loaded.Phase == RaidMortarPhase.Recover && loaded.Chemical && loaded.Shots == 4
                && loaded.PhaseAt == 9000 && loaded.Spot == state.Spot && loaded.RetryAt == 9120 && loaded.HadLoaded,
                "Saved withdrawal must retain its finite recovery deadline and shot/loading state.");
        }
        finally { Scribe.mode = LoadSaveMode.Inactive; ScribeMetaHeaderUtility.modListChanged = previous; if (File.Exists(path)) File.Delete(path); }
    }
}
