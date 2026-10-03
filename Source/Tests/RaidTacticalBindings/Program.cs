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

internal static class Program
{
    private static int Main()
    {
        // Check target signatures and injected private fields against the real
        // installed game. This is not a Unity/Mono runtime patch simulation.
        string managed = @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed";
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Type[] patches = {
            typeof(Patch_RaidTacticalTrace_StartJob), typeof(Patch_RaidTacticalTrace_EndJob),
            typeof(Patch_RaidTacticalDuty), typeof(Patch_RaidTacticalDutyConstant),
            typeof(Patch_RaidTacticalContinuation), typeof(Patch_RaidTacticalHoldFacing),
            typeof(Patch_RaidTacticalPendingAttack),
            typeof(Patch_RaidTacticalSupportFlee),
            typeof(Patch_RaidMovementArea_Request), typeof(Patch_RaidMovementArea_Dispose),
            typeof(Patch_BreachedDoor_NoRandomBreakdown), typeof(Patch_BreachedDoor_AlwaysOpen),
            typeof(Patch_BreachedDoor_FreePassage), typeof(Patch_BreachedDoor_BreakdownRepaired),
            typeof(Patch_BreachedDoor_OrdinaryRepair)
        };
        try
        {
            foreach (Type patch in patches)
            {
                HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
                MethodInfo target = AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                if (target == null) throw new Exception("Missing target: " + patch.Name);
                foreach (MethodInfo hook in patch.GetMethods(BindingFlags.Static | BindingFlags.Public)
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
                        else expected = target.GetParameters().FirstOrDefault(value => value.Name == parameter.Name)?.ParameterType;
                        if (expected == null || !actual.IsAssignableFrom(expected))
                            throw new Exception(patch.Name + ": invalid binding " + parameter.Name);
                    }
                Console.WriteLine("PASS: " + patch.Name);
            }
            CheckDoorFaultHooks();
            CheckCasualtyReevaluation();
            CheckGrenadePrediction();
            CheckSharedStructureVersions();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckSharedStructureVersions()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        Assembly assembly = typeof(RaidStructureSnapshot).Assembly;
        Type inputType = assembly.GetType("Helodrace.TacticalGeometryInput", true);
        Type rawType = assembly.GetType("Helodrace.TacticalRawCell", true);
        Type flagsType = assembly.GetType("Helodrace.TacticalRawFlags", true);
        var input = Activator.CreateInstance(inputType, new object[] { 3, 3 });
        var rawCells = (Array)AccessTools.Field(inputType, "Cells").GetValue(input);
        for (int i = 0; i < rawCells.Length; i++)
        {
            object raw = Activator.CreateInstance(rawType);
            AccessTools.Field(rawType, "Flags").SetValue(raw, Enum.ToObject(flagsType, 1));
            AccessTools.Field(rawType, "Room").SetValue(raw, 7);
            rawCells.SetValue(raw, i);
        }
        object geometry = AccessTools.Method(assembly.GetType("Helodrace.TacticalGeometry"), "Calculate")
            .Invoke(null, new object[] { input, System.Threading.CancellationToken.None });
        var version = (TacticalStructureVersion)Activator.CreateInstance(typeof(TacticalStructureVersion),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { (object)12, geometry }, null);
        // Map's field initializers include Unity state. These methods only need
        // value dimensions and indexing, so no engine initialization is involved.
        var map = (Map)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(3, 1, 3) };
        map.cellIndices = new CellIndices(3, 3);
        RaidStructureSnapshot Snapshot(string id) => (RaidStructureSnapshot)Activator.CreateInstance(
            typeof(RaidStructureSnapshot), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { map, version, id }, null);
        var first = Snapshot("A");
        var second = Snapshot("B");
        PropertyInfo versionProperty = AccessTools.Property(typeof(RaidStructureSnapshot), "Version");
        Check(ReferenceEquals(versionProperty.GetValue(first), versionProperty.GetValue(second)),
            "Two organizations must share one immutable version rather than clone the map.");
        Check(first.RoomAt(new IntVec3(1, 0, 1)) == 7 && second.RoomAt(new IntVec3(1, 0, 1)) == 7,
            "Shared organization room lookup keeps the captured room ID.");
        var analysis = new MapComponent_TacticalMapAnalysis(map);
        AccessTools.Property(typeof(MapComponent_TacticalMapAnalysis), "Completed").SetValue(analysis, version);
        Check(first.RoomAt(new IntVec3(-1, 0, 0)) == 0, "Out-of-map room lookup remains safe.");
        var restored = new RaidStructureSnapshot { OrganizationId = "A" };
        AccessTools.Field(typeof(RaidStructureSnapshot), "versionId").SetValue(restored, 12);
        var versions = new Dictionary<int, TacticalStructureVersion> { [12] = version };
        MethodInfo restore = AccessTools.Method(typeof(RaidStructureSnapshot), "Restore");
        Check((bool)restore.Invoke(restored, new object[] { map, versions })
            && ReferenceEquals(versionProperty.GetValue(restored), version),
            "Restoring organization references must resolve the shared saved version.");
        Check(!(bool)restore.Invoke(new RaidStructureSnapshot(), new object[] { map, versions }),
            "A missing saved version cannot silently become a live-map snapshot.");
        AccessTools.Method(typeof(MapComponent_TacticalMapAnalysis), "ReserveVersion").Invoke(analysis, new object[] { 12 });
        AccessTools.Method(typeof(MapComponent_TacticalMapAnalysis), "ReserveVersion").Invoke(analysis, new object[] { 4 });
        Check((int)AccessTools.Field(typeof(MapComponent_TacticalMapAnalysis), "nextVersion").GetValue(analysis) == 12,
            "New version IDs must not collide with retained versions after loading.");
        analysis.MapRemoved();
        Check(versionProperty.GetValue(first) == version && first.RoomAt(new IntVec3(1, 0, 1)) == 7,
            "Clearing the map cache owner cannot mutate an organization's pinned version.");
        Console.WriteLine("PASS: 7 shared structure ownership, restore and version lifetime checks (real game classes)");
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

    private static void CheckGrenadePrediction()
    {
        MethodInfo aim = AccessTools.DeclaredMethod(typeof(MapComponent_RaidTacticalExecution), "ExplosionAim");
        MethodInfo radius = AccessTools.DeclaredMethod(typeof(MapComponent_RaidTacticalExecution), "ExplosionRadius");
        var grenade = new Projectile_Explosive { Position = new IntVec3(2, 0, 2),
            usedTarget = new LocalTargetInfo(new IntVec3(8, 0, 8)) };
        AccessTools.FieldRefAccess<Projectile, UnityEngine.Vector3>("destination")(grenade) = new UnityEngine.Vector3(10, 0, 11);
        if ((IntVec3)aim.Invoke(null, new object[] { grenade }) != new IntVec3(10, 0, 11))
            throw new Exception("Airborne grenade evasion must use actual scattered landing position.");
        AccessTools.FieldRefAccess<Projectile, bool>("landed")(grenade) = true;
        if ((IntVec3)aim.Invoke(null, new object[] { grenade }) != grenade.Position)
            throw new Exception("Landed grenade evasion must use actual impact position after a collision.");
        var def = (ThingDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.projectile = new ProjectileProperties { explosionRadius = 3f };
        if ((float)radius.Invoke(null, new object[] { def }) != 9f)
            throw new Exception("Ordinary grenades must retain the vanilla minimum flee distance.");
        def.modExtensions = new List<DefModExtension> { new Helodrace.ModernWar.FragmentationGrenadeExtension {
            radius = 8f, longRangeRadius = 12f } };
        if ((float)radius.Invoke(null, new object[] { def }) != 14f)
            throw new Exception("Grenade evasion must cover the mod's long-range fragments plus margin.");
        Console.WriteLine("PASS: 4 grenade landing and fragmentation safety checks (real game classes)");
    }

    private static void CheckCasualtyReevaluation()
    {
        int checks = 0;
        void Check(bool condition, string detail)
        {
            if (!condition) throw new Exception(detail);
            checks++;
        }
        foreach (string notification in new[] { "Notify_Killed", "Notify_Downed", "PostDeSpawn" })
        {
            MethodInfo method = AccessTools.DeclaredMethod(typeof(PawnOrganizationComponent), notification);
            Check(method != null && method.GetBaseDefinition().DeclaringType == typeof(ThingComp),
                "The organization must receive the real vanilla " + notification + " callback.");
        }
        var execution = new MapComponent_RaidTacticalExecution(null);
        // ThingDef's constructor loads Unity shaders; identity checks only need
        // its managed defName field in this non-Unity boundary test.
        var pawnDef = (ThingDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawnDef.defName = "TestCasualtyPawn";
        Pawn TestPawn(int id) => new Pawn { def = pawnDef, thingIDNumber = id };
        var lost = TestPawn(1);
        execution.RequestCasualtyReevaluation("A", lost);
        execution.RequestCasualtyReevaluation("A", lost);
        execution.RequestCasualtyReevaluation("A", TestPawn(2));
        execution.RequestCasualtyReevaluation("B", TestPawn(3));
        execution.RequestCasualtyReevaluation(null, TestPawn(4));
        var pending = (Dictionary<string, HashSet<Pawn>>)AccessTools.Field(
            typeof(MapComponent_RaidTacticalExecution), "pendingCasualties").GetValue(execution);
        Check(pending.Count == 2 && pending["A"].Count == 2 && pending["B"].Count == 1,
            "Repeated death/down events must coalesce without losing separate organizations or casualties.");
        MethodInfo resetBreach = AccessTools.DeclaredMethod(typeof(MapComponent_RaidTacticalExecution), "ResetLostBreacher");
        MethodInfo resetSupport = AccessTools.DeclaredMethod(typeof(MapComponent_RaidTacticalExecution), "ResetLostSupport");
        var target = new Building();
        var plan = new RaidTacticalPlan { PlannedBreach = target };
        foreach (RaidBreachKind kind in new[] { RaidBreachKind.Sledgehammer, RaidBreachKind.PowerCutter, RaidBreachKind.C4 })
        {
            var state = new MapComponent_RaidTacticalExecution.ExecutionState {
                ActivePlan = plan, Breacher = lost, BreachTarget = target, BreachKind = kind,
                Phase = kind == RaidBreachKind.C4 ? RaidExecutionPhase.WithdrawFromCharge : RaidExecutionPhase.Breach,
                WithdrawalIssued = true, BreachAttempts = 2 };
            resetBreach.Invoke(null, new object[] { state, 77, false });
            Check(state.Breacher == null && state.BreachTarget == null && state.BreachKind == RaidBreachKind.None
                && state.Phase == RaidExecutionPhase.Breach && state.PhaseStarted == 77
                && !state.WithdrawalIssued && state.BreachAttempts == 0
                && state.ActivePlan == plan && plan.PlannedBreach == target,
                "A lost " + kind + " operator must permit replacement at the committed opening without failure accumulation.");
        }
        var detonating = new MapComponent_RaidTacticalExecution.ExecutionState {
            Breacher = lost, BreachTarget = target, BreachKind = RaidBreachKind.C4,
            Phase = RaidExecutionPhase.Detonation, PhaseStarted = 42 };
        resetBreach.Invoke(null, new object[] { detonating, 77, true });
        Check(detonating.Breacher == null && detonating.BreachTarget == target
            && detonating.BreachKind == RaidBreachKind.C4 && detonating.Phase == RaidExecutionPhase.Detonation
            && detonating.PhaseStarted == 42,
            "An already triggered charge must keep its effect wait and original timeout after the operator dies.");
        var grenade = new Projectile_Explosive();
        var support = new MapComponent_RaidTacticalExecution.ExecutionState {
            Thrower = lost, SupportLaunched = true, SupportIssued = true, SupportReturnRequired = true,
            SupportProjectile = grenade, Phase = RaidExecutionPhase.EntryWait };
        resetSupport.Invoke(null, new object[] { support, 77 });
        Check(support.Thrower == lost && support.SupportProjectile == grenade && support.SupportIssued
            && support.SupportLaunched && !support.SupportReturnRequired && support.Phase == RaidExecutionPhase.EntryWait,
            "Thrower death after launch must retain the live grenade and instigator while releasing only the return requirement.");
        var preparation = new MapComponent_RaidTacticalExecution.ExecutionState {
            ActivePlan = plan, Thrower = lost, SupportIssued = true, SupportReturnRequired = true,
            Phase = RaidExecutionPhase.EntryWait };
        resetSupport.Invoke(null, new object[] { preparation, 77 });
        Check(preparation.Thrower == null && !preparation.SupportIssued && !preparation.SupportReturnRequired
            && preparation.Phase == RaidExecutionPhase.Support && preparation.PhaseStarted == 77
            && preparation.ActivePlan == plan,
            "Thrower death before launch must select a new thrower without restarting the approach.");
        Console.WriteLine($"PASS: {checks} casualty notification, batching and actor replacement checks (real game classes)");
    }
}
