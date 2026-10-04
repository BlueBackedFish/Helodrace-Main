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
            typeof(Patch_BreachedDoor_OrdinaryRepair), typeof(Patch_DebugSettings_RaidTacticalOverlay),
            typeof(Patch_RaidGrenade_NoGunCast), typeof(Patch_RaidGrenade_NoGunAvailable), typeof(Patch_RaidGrenade_DrawHeld),
            typeof(Patch_RaidOpeningObservation_Lean)
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
            CheckSmokeGases();
            CheckBreachToolRecovery();
            CheckTacticalRoomOverlay();
            CheckAiGrenadePreparation();
            CheckOccupiedRoomPlan();
            CheckOpeningObservation();
            CheckOpeningDoorAndThrowTargets();
            CheckSupportMovementJobGap();
            RaidContactTests.Run();
            RaidObservationTests.Run();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckSupportMovementJobGap()
    {
        MethodInfo method = AccessTools.Method(typeof(MapComponent_RaidMovementAreas), "IsSupportExplosionFlee");
        var tracked = new Projectile_Explosive();
        var flee = new Job { def = new JobDef { defName = "Flee" }, jobGiver = new JobGiver_FleePotentialExplosion() };
        bool Flee(Job current, Projectile projectile, Thing known, bool waiting) =>
            (bool)method.Invoke(null, new object[] { current, projectile, known, waiting });
        if (Flee(null, tracked, tracked, true))
            throw new Exception("The no-current-job gap after grenade release must not throw or be classified as explosion fleeing.");
        if (!Flee(flee, tracked, tracked, true))
            throw new Exception("Actual fleeing from the tracked support grenade still needs its safe movement area.");
        if (Flee(flee, tracked, new Projectile_Explosive(), true))
            throw new Exception("An unrelated hostile grenade must not inherit the support grenade's movement exception.");
        if (Flee(flee, null, null, true) || Flee(flee, tracked, tracked, false))
            throw new Exception("Flee exceptions require a live tracked grenade and an active pre-entry support wait.");
        Console.WriteLine("PASS: support movement between-job null safety and explosion identity (4 checks)");
    }

    private static void CheckOpeningDoorAndThrowTargets()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        var breach = new IntVec3(10, 0, 10);
        MethodInfo membership = AccessTools.Method(typeof(RaidEntryObservation), "IncludesRoomCell");
        MethodInfo side = AccessTools.Method(typeof(RaidEntryObservation), "OnObservedSide");
        MethodInfo throwTarget = AccessTools.Method(typeof(RaidEntryObservation), "EntryThrowTarget");
        foreach (IntVec3 inward in new[] { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West })
        {
            IntVec3 lateral = new IntVec3(-inward.z, 0, inward.x);
            var rooms = new Dictionary<IntVec3, int> { [breach] = 99,
                [breach + inward] = 7, [breach - inward] = 8, [breach + lateral] = 99 };
            Func<IntVec3, int> roomAt = cell => rooms.TryGetValue(cell, out int room) ? room : -1;
            bool InRoom(IntVec3 cell, int room, bool door) => (bool)membership.Invoke(null,
                new object[] { cell, room, door, roomAt });
            bool OnSide(IntVec3 cell, bool door) => (bool)side.Invoke(null,
                new object[] { cell, breach, breach + inward, door });
            var plan = new RaidTacticalPlan { BreachCell = breach, BreachInside = breach + inward };
            bool CanThrow(IntVec3 cell) => (bool)throwTarget.Invoke(null, new object[] { plan, cell });
            Check(InRoom(breach, 7, true) && InRoom(breach, 8, true),
                "A door's separate cache room ID must not hide contact from either directly adjoining room.");
            Check(OnSide(breach, true) && !OnSide(breach, false),
                "The entry doorway is observable at zero forward depth, without including ordinary cells on that plane.");
            Check(InRoom(breach + inward, 7, false) && !InRoom(breach - inward, 7, false),
                "Ordinary floor cells retain their exact room membership.");
            Check(!InRoom(breach + lateral, 7, true),
                "A diagonal neighbor or another doorway must not propagate room membership through a door chain.");
            Check(!InRoom(breach, 42, true) && !OnSide(breach - inward, true),
                "Unrelated rooms and doors behind the observation direction must remain excluded.");
            Check(!CanThrow(breach) && !CanThrow(breach + inward)
                && !CanThrow(breach + inward + lateral),
                "Contact and blind targets inside two cells of the opening are never entry throw targets.");
            Check(CanThrow(breach + inward * 2) && CanThrow(breach + inward * 3),
                "The inclusive minimum throw distance is two cells in every entry orientation.");
            Check(!CanThrow(breach - inward * 2) && !CanThrow(breach + lateral * 2),
                "Distance alone cannot permit an entry grenade outside or along the opening's wall.");
            Check(CanThrow(breach + inward + lateral * 2),
                "Farther lateral targets inside the room use actual opening distance rather than an extra depth restriction.");
            var contact = new RaidEntryObservation { EnemyCell = breach + inward };
            var targets = (IEnumerable<IntVec3>)AccessTools.Method(typeof(RaidEntryObservation), "ThrowTargets")
                .Invoke(null, new object[] { contact, new[] { breach + inward * 3 }, (Func<IntVec3, bool>)CanThrow });
            Check(!targets.Any(), "A close enemy is still observed but its unsafe throw must not redirect to an unrelated target.");
        }
        Func<IntVec3, int> edgeRooms = cell => cell == breach + IntVec3.North ? 0 : -1;
        Check((bool)membership.Invoke(null, new object[] { breach, 0, true, edgeRooms }),
            "A doorway adjoining exterior room zero is included for outdoor observation.");
        edgeRooms = cell => -1;
        Check(!(bool)membership.Invoke(null, new object[] { breach, 0, true, edgeRooms }),
            "Out-of-bounds neighbors are not mistaken for exterior room zero.");
        var noOpening = new RaidTacticalPlan { BreachCell = IntVec3.Invalid };
        Check((bool)throwTarget.Invoke(null, new object[] { noOpening, breach })
            && !(bool)throwTarget.Invoke(null, new object[] { noOpening, IntVec3.Invalid }),
            "An entry without a physical opening has no clearance origin, while invalid targets are always rejected.");
        Console.WriteLine($"PASS: {checks} adjacent doorway observation and minimum entry throw distance checks");
    }

    private static void CheckOpeningObservation()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        Assembly assembly = typeof(RaidEntryObservation).Assembly;
        MethodInfo sideMethod = AccessTools.Method(typeof(RaidEntryObservation), "SidePositions");
        var breach = new IntVec3(10, 0, 10);
        foreach (IntVec3 inward in new[] { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West })
        {
            var positions = ((IEnumerable<IntVec3>)sideMethod.Invoke(null, new object[] { breach, breach + inward })).ToList();
            Check(positions.Count == 2 && positions.Distinct().Count() == 2 && positions.All(position =>
                (position.x - breach.x) * inward.x + (position.z - breach.z) * inward.z == -1
                && position.DistanceToSquared(breach) == 2),
                "For every wall orientation, observers stand diagonally outside, never on the opening's centerline.");
        }
        var enemy = new IntVec3(11, 0, 11);
        var visibleEmpty = new IntVec3(12, 0, 11);
        var blind = new IntVec3(11, 0, 12);
        var observation = new RaidEntryObservation { EnemyCell = enemy,
            VisibleCells = new List<IntVec3> { enemy, visibleEmpty } };
        MethodInfo targetMethod = AccessTools.Method(typeof(RaidEntryObservation), "ThrowTargets");
        List<IntVec3> Targets(Func<IntVec3, bool> valid) => ((IEnumerable<IntVec3>)targetMethod.Invoke(null,
            new object[] { observation, new[] { visibleEmpty, blind, enemy }, valid })).ToList();
        Check(Targets(cell => true).SequenceEqual(new[] { enemy }),
            "Contact observation provides only the first enemy position, with no additional blind-sector target.");
        Check(Targets(cell => cell != enemy).Count == 0,
            "An unsafe/unusable contact must not redirect the grenade to an unrelated blind sector.");
        observation.EnemyCell = IntVec3.Invalid;
        Check(Targets(cell => true).SequenceEqual(new[] { blind }), "No enemy sighting selects only unseen cells.");
        observation.VisibleCells.Add(blind);
        Check(Targets(cell => true).Count == 0, "A fully observed empty room has no grenade target.");
        MethodInfo record = AccessTools.Method(typeof(RaidEntryObservation), "RecordEnemy");
        Check((bool)record.Invoke(observation, new object[] { enemy }), "The first actual sighting is accepted.");
        Check(observation.Complete && observation.HasEnemyContact, "Enemy contact ends the peek immediately rather than waiting 90 ticks.");
        Check(observation.VisibleCells.Count == 0, "An interrupted contact peek keeps no extra visible-room intelligence.");
        Check(observation.EnemyCell == enemy, "Contact intelligence contains the first enemy's position.");
        Check(!(bool)record.Invoke(observation, new object[] { blind }) && observation.EnemyCell == enemy,
            "A second enemy cannot add or replace intelligence after the first-contact withdrawal.");
        var emptyObservation = new RaidEntryObservation();
        Check(!(bool)record.Invoke(emptyObservation, new object[] { IntVec3.Invalid }) && !emptyObservation.Complete,
            "An invalid position cannot manufacture contact or cancel a normal peek.");

        var pawn = new Pawn { Position = breach - IntVec3.North + IntVec3.East };
        pawn.jobs = new Pawn_JobTracker(pawn);
        var job = new Job { def = new JobDef { defName = RaidEntryObservation.JobDefName },
            targetA = pawn.Position, targetB = breach + IntVec3.North, targetC = breach - IntVec3.North };
        var driver = new JobDriver_RaidObserveOpening { pawn = pawn, job = job, Peeking = true };
        pawn.jobs.curJob = job; pawn.jobs.curDriver = driver;
        IntVec3 offset = IntVec3.Zero;
        bool result = false;
        Patch_RaidOpeningObservation_Lean.Postfix(pawn, ref offset, ref result);
        Check(result && offset == IntVec3.West, "The observation job uses the native adjacent-cell lean toward the opening.");
        driver.Peeking = false; offset = IntVec3.Zero; result = false;
        Patch_RaidOpeningObservation_Lean.Postfix(pawn, ref offset, ref result);
        Check(!result && offset == IntVec3.Zero, "Waiting for demolition must not peek through an intact wall.");
        driver.Peeking = true; driver.ended = true;
        Patch_RaidOpeningObservation_Lean.Postfix(pawn, ref offset, ref result);
        Check(!result, "Interrupted observation cannot retain an active forced lean.");
        driver.ended = false;
        var toils = ((IEnumerable<Toil>)AccessTools.Method(typeof(JobDriver_RaidObserveOpening), "MakeNewToils")
            .Invoke(driver, null)).ToList();
        Check(toils.Count == 6 && toils[1].defaultCompleteMode == ToilCompleteMode.PatherArrival
            && toils[2].defaultCompleteMode == ToilCompleteMode.Never,
            "Observation first reaches its side position and then counts actual peeking time, not walking time.");
        Check(toils[3].defaultCompleteMode == ToilCompleteMode.Instant
            && toils[4].defaultCompleteMode == ToilCompleteMode.PatherArrival,
            "Peek completion hands directly to a return-to-stack movement stage, without an extra fixed wait.");
        var weapon = new ThingWithComps();
        var owner = new CompEquippable { parent = weapon };
        var verb = new Verb_Shoot { caster = pawn, verbTracker = new VerbTracker(owner),
            verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot) } };
        result = true;
        Check(!Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result) && !result,
            "The observer must not start a gun burst while holding a fixed observation pose.");
        pawn.jobs.curJob = new Job { def = new JobDef { defName = "Flee" } };
        result = true;
        Check(Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result), "An emergency override releases the observation firing gate.");
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var definition = XDocument.Load(Path.Combine(root, "Defs/GreatWar/Jobs_RaidEntryObservation.xml")).Root.Element("JobDef");
        Check(definition.Element("driverClass")?.Value == typeof(JobDriver_RaidObserveOpening).FullName
            && definition.Element("suspendable")?.Value == "false"
            && definition.Element("checkOverrideOnDamage")?.Value == "Always",
            "The dedicated observer job binds correctly and can be cancelled for damage without stale resumption.");
        Check(XDocument.Load(Path.Combine(root, "Languages/Korean (한국어)/DefInjected/JobDef/RaidEntryObservation.xml"))
            .Root.Element(RaidEntryObservation.JobDefName + ".reportString") != null,
            "The new observation job has its Korean report binding.");
        Game previousGame = Current.Game;
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        var execution = new MapComponent_RaidTacticalExecution(map);
        AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent> { execution });
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var state = new MapComponent_RaidTacticalExecution.ExecutionState { OrganizationId = "ObserverTest",
            ActivePlan = new RaidTacticalPlan { PlannedTick = 42 }, Phase = RaidExecutionPhase.Breach,
            Observation = new RaidEntryObservation { Observer = pawn } };
        ((Dictionary<string, MapComponent_RaidTacticalExecution.ExecutionState>)AccessTools.Field(
            typeof(MapComponent_RaidTacticalExecution), "states").GetValue(execution))[state.OrganizationId] = state;
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
        AccessTools.Field(typeof(JobDriver_RaidObserveOpening), "organizationId").SetValue(driver, state.OrganizationId);
        job.count = 42;
        bool OwnerValid() => (bool)AccessTools.Method(typeof(JobDriver_RaidObserveOpening), "OwnerStillValid").Invoke(driver, null);
        Current.Game = game;
        try
        {
            Check(OwnerValid(), "An assigned observer can wait beside an active engineer.");
            state.Phase = RaidExecutionPhase.ObserveOpening;
            Check(OwnerValid(), "The same observer remains assigned when demolition hands over to observation.");
            toils[5].initAction();
            Check(state.Observation.ReturnComplete, "Only the post-arrival return completion action unlocks the support handoff.");
            state.Phase = RaidExecutionPhase.WithdrawFromCharge;
            Check(!OwnerValid(), "C4 withdrawal must cancel the observer instead of leaving them beside the charge.");
            state.Phase = RaidExecutionPhase.ObserveOpening;
            state.Observation.Observer = null;
            Check(!OwnerValid(), "Replacing the observer invalidates the previous actor's observation job.");
            state.Observation.Observer = pawn;
            state.ActivePlan.PlannedTick++;
            Check(!OwnerValid(), "A new plan must not inherit the old opening's observation job.");
        }
        finally { Current.Game = previousGame; }
        Console.WriteLine($"PASS: {checks} opening observation positions, target priority, native lean and interruption checks");
    }

    private static void CheckOccupiedRoomPlan()
    {
        // Exercise the actual assignment path rather than only the pure room policy.
        var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawnDef.defName = "OccupiedRoomTestPawn";
        var members = Enumerable.Range(1, 4).Select(id => new Pawn { def = pawnDef, thingIDNumber = id,
            Position = new IntVec3(id, 0, 1) }).ToList();
        var seed = new RaidTacticalPlan { Start = members[0].Position,
            Objective = new IntVec3(50, 0, 1), OccupiedRoom = 1 };
        MethodInfo method = AccessTools.Method(typeof(RaidTacticalPlanner), "MakeCurrentRoomPlan");
        var plan = (RaidTacticalPlan)method.Invoke(null, new object[] { new CombatOrganization(), members, seed });
        if (!plan.Success || plan.Selected.Maneuver != RaidTacticalManeuver.DirectAssault)
            throw new Exception("Occupied-room clearance must remain an executable direct assault even beyond the local window.");
        if (plan.Assignments.Count != members.Count || !plan.Assignments.Any(value => value.Task == RaidTacticalTask.Entry))
            throw new Exception("Occupied-room clearance must assign an entry team, including large rooms without stack positions.");
        if (plan.Assignments.Any(value => value.Position != value.Pawn.Position))
            throw new Exception("Occupied-room clearance must not relocate pawns to a new stack.");
        if (plan.PlannedBreach != null || plan.BreachCell.IsValid || plan.ReusePassage)
            throw new Exception("Occupied-room clearance must not carry demolition or passage-entry orders.");
        if (plan.CqbIntent != RaidCqbIntent.ClearCurrentRoom || plan.Entry != plan.Objective)
            throw new Exception("Occupied-room assignments must advance directly to their clearance objective.");
        Console.WriteLine("PASS: occupied-room execution assignments (5 checks)");
    }

    private static void CheckAiGrenadePreparation()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        var pawn = new Pawn();
        pawn.jobs = new Pawn_JobTracker(pawn);
        var definition = new JobDef { defName = RaidGrenadePreparation.JobDefName, neverShowWeapon = true };
        var job = new Job { def = definition, targetA = new IntVec3(5, 0, 0), targetC = new IntVec3(2, 0, 0), count = 1 };
        var grenade = new Thing();
        job.targetB = grenade;
        var driver = new JobDriver_RaidPrepareGrenade { pawn = pawn, job = job };
        pawn.jobs.curJob = job; pawn.jobs.curDriver = driver;
        var weapon = new ThingWithComps();
        var owner = new CompEquippable { parent = weapon };
        var verb = new Verb_Shoot { caster = pawn, verbTracker = new VerbTracker(owner),
            verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot) } };
        bool result = true;
        Check(!Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result) && !result,
            "An AI preparing a grenade must not start a gun cast even before reaching the throw cell.");
        result = true;
        Patch_RaidGrenade_NoGunAvailable.Postfix(verb, ref result);
        Check(!result, "Burst availability must be blocked while the grenade preparation job is active.");
        driver.Prepared = true;
        result = true;
        Check(!Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result), "A held ready grenade still prevents gunfire during movement.");
        driver.ended = true;
        result = true;
        Check(Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result), "Interrupting preparation immediately releases the gun restriction.");
        driver.ended = false;
        pawn.jobs.curJob = new Job { def = new JobDef { defName = "Flee" } };
        Check(Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result), "An unrelated emergency job cannot inherit the stale ready state.");
        pawn.jobs.curJob = job;
        driver.Released = true;
        Check(Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result), "A completed release does not retain a separate persistent firing lock.");
        driver.Released = false;
        verb.verbProps.verbClass = typeof(Verb_MeleeAttackDamage);
        Check(Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result), "Gun blocking does not globally disable melee defense.");
        verb.verbProps.verbClass = typeof(Verb_Shoot);
        foreach (string playerJob in new[] { "HD_ThrowInventoryGrenadeClose", "HD_ThrowInventoryGrenadeNormal" })
        {
            pawn.jobs.curJob = new Job { def = new JobDef { defName = playerJob } };
            Check(Patch_RaidGrenade_NoGunCast.Prefix(verb, ref result), "Player inventory grenade jobs retain their own behavior.");
        }
        pawn.jobs.curJob = job;
        var toils = ((IEnumerable<Toil>)AccessTools.Method(typeof(JobDriver_RaidPrepareGrenade), "MakeNewToils").Invoke(driver, null)).ToList();
        Check(toils.Count == 5 && toils[2].defaultCompleteMode == ToilCompleteMode.PatherArrival,
            "The AI job must carry readiness into an arrival-based movement stage before release.");
        Check(toils[3].defaultDuration == 18 && toils[3].defaultCompleteMode == ToilCompleteMode.Delay,
            "Release must take 0.3 seconds after arrival, not the player's 90/180-tick preparation.");
        Check(toils[4].defaultCompleteMode == ToilCompleteMode.Instant && toils[3].endConditions.Count > 0,
            "A short release still checks live throw validity/safety before the launch action.");
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var aiXml = XDocument.Load(Path.Combine(root, "Defs/GreatWar/Jobs_RaidGrenadePreparation.xml")).Root.Element("JobDef");
        Check(aiXml.Element("neverShowWeapon")?.Value == "true" && aiXml.Element("suspendable")?.Value == "false",
            "The AI job must hide the gun and must not resume an interrupted preparation from a suspended queue.");
        Check(aiXml.Element("casualInterruptible")?.Value == "true" && aiXml.Element("checkOverrideOnDamage")?.Value == "Always",
            "The AI preparation must allow surrounding emergencies and damage overrides to interrupt it.");
        Game previousGame = Current.Game;
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        var execution = new MapComponent_RaidTacticalExecution(map);
        AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent> { execution });
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var state = new MapComponent_RaidTacticalExecution.ExecutionState { OrganizationId = "A",
            ActivePlan = new RaidTacticalPlan { PlannedTick = 42 }, Phase = RaidExecutionPhase.Support,
            SupportIssued = true, Thrower = pawn };
        ((Dictionary<string, MapComponent_RaidTacticalExecution.ExecutionState>)AccessTools.Field(
            typeof(MapComponent_RaidTacticalExecution), "states").GetValue(execution))["A"] = state;
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
        AccessTools.Field(typeof(JobDriver_RaidPrepareGrenade), "organizationId").SetValue(driver, "A");
        AccessTools.Field(typeof(JobDriver_RaidPrepareGrenade), "planTick").SetValue(driver, 42);
        AccessTools.Field(typeof(JobDriver_RaidPrepareGrenade), "ownerCaptured").SetValue(driver, true);
        bool OwnerValid() => (bool)AccessTools.Method(typeof(JobDriver_RaidPrepareGrenade), "OwnerStillValid").Invoke(driver, null);
        Current.Game = game;
        try
        {
            Check(OwnerValid(), "Captured preparation remains owned by its current support actor and plan.");
            state.Thrower = new Pawn();
            Check(!OwnerValid(), "Replacing the support actor must cancel the previous actor's unlaunched grenade.");
            state.Thrower = pawn; state.Phase = RaidExecutionPhase.Assemble;
            Check(!OwnerValid(), "An unrelated regroup action cancels rather than carries grenade readiness into the new task.");
            state.ApproachSmokeActive = true; state.ApproachSmokeThrower = pawn;
            Check(OwnerValid(), "The designated field smoke actor retains preparation during the approach phase.");
            state.ActivePlan = new RaidTacticalPlan { PlannedTick = 43 };
            Check(!OwnerValid(), "A replaced tactical plan must not launch the grenade selected by the stale plan.");
        }
        finally { Current.Game = previousGame; }
        Console.WriteLine($"PASS: {checks} AI grenade preparation, interruption, gun gating and release lifecycle checks (real game classes)");
    }

    private static void CheckTacticalRoomOverlay()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        Assembly assembly = typeof(TacticalStructureVersion).Assembly;
        Type inputType = assembly.GetType("Helodrace.TacticalGeometryInput");
        Type rawType = assembly.GetType("Helodrace.TacticalRawCell");
        object input = Activator.CreateInstance(inputType, new object[] { 5, 1 });
        var cells = (Array)AccessTools.Field(inputType, "Cells").GetValue(input);
        for (int i = 0; i < cells.Length; i++)
        {
            object raw = Activator.CreateInstance(rawType);
            AccessTools.Field(rawType, "Room").SetValue(raw, i < 2 ? 7 : i > 2 ? 8 : 0);
            cells.SetValue(raw, i);
        }
        object geometry = AccessTools.Method(assembly.GetType("Helodrace.TacticalGeometry"), "Calculate")
            .Invoke(null, new object[] { input, System.Threading.CancellationToken.None });
        var version = (TacticalStructureVersion)Activator.CreateInstance(typeof(TacticalStructureVersion),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { (object)19, geometry }, null);
        Type dataType = assembly.GetType("Helodrace.RaidRoomDebugData");
        object Project(MapComponent_RaidTacticalExecution.ExecutionState state) => Activator.CreateInstance(dataType,
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { version, state }, null);
        string State(object data, int room) => AccessTools.Method(dataType, "State").Invoke(data, new object[] { room }).ToString();
        int RoomAt(object data, int x) => (int)AccessTools.Method(dataType, "RoomAt").Invoke(data, new object[] { new IntVec3(x, 0, 0) });
        var firstState = new MapComponent_RaidTacticalExecution.ExecutionState {
            ActivePlan = new RaidTacticalPlan { Objective = new IntVec3(4, 0, 0) },
            ClearedRoomCells = new List<IntVec3> { new IntVec3(0, 0, 0), new IntVec3(1, 0, 0),
                new IntVec3(-1, 0, 0), new IntVec3(99, 0, 0), new IntVec3(2, 0, 0) }
        };
        object first = Project(firstState);
        Check(State(first, 7) == "Cleared" && State(first, 8) == "CurrentTarget",
            "The overlay must project saved clearance and the current target onto pinned room IDs.");
        Check(((HashSet<int>)AccessTools.Field(dataType, "Cleared").GetValue(first)).Count == 1,
            "Repeated room coordinates, boundary cells and out-of-map records must not inflate clearance.");
        Check(RoomAt(first, 0) == 7 && RoomAt(first, 4) == 8 && RoomAt(first, -1) == 0 && RoomAt(first, 5) == 0,
            "Debug room lookup must remain bounded and use the captured layout.");
        var secondState = new MapComponent_RaidTacticalExecution.ExecutionState {
            ActivePlan = new RaidTacticalPlan { Objective = new IntVec3(0, 0, 0) },
            ClearedRoomCells = new List<IntVec3> { new IntVec3(4, 0, 0) }
        };
        object second = Project(secondState);
        Check(State(second, 7) == "CurrentTarget" && State(second, 8) == "Cleared" && State(first, 7) == "Cleared",
            "Organizations sharing a structure must retain independent clearance overlays.");
        Check(State(Project(null), 7) == "Unavailable" && State(Project(null), 0) == "Unavailable",
            "Missing execution progress must display unknown rather than imply an uncleared room.");
        firstState.ActivePlan.Objective = new IntVec3(0, 0, 0);
        Check(State(Project(firstState), 7) == "Cleared", "Cleared state must take priority over a repeated current target.");
        Check(State(Project(firstState), 8) == "Uncleared", "Other known rooms must remain uncleared.");
        firstState.ClearedRoomCells.Add(new IntVec3(4, 0, 0));
        Check(State(first, 8) == "CurrentTarget" && State(Project(firstState), 8) == "Cleared",
            "A refresh must capture new clearance without mutating the previously rendered snapshot.");
        Check(firstState.ClearedRoomCells.Count == 6 && RoomAt(first, 0) == 7 && RoomAt(first, 4) == 8,
            "Inspecting the map must not rewrite saved progress or static room IDs.");
        firstState.RoomSecurity.Observe(7, new IntVec3(0, 0, 0), 100);
        firstState.Contacts.Observe(123, "Rear contact", new IntVec3(0, 0, 0), 7, 1, 100, IntVec3.Invalid, true, 24f);
        Check(State(Project(firstState), 7) == "Threatened" && firstState.ClearedRoomCells.Count == 6,
            "A rear contact changes current security without deleting the room's clearing history.");
        firstState.Contacts.FinishScan(120, new HashSet<int>());
        Check(State(Project(firstState), 7) == "NeedsRecheck" && State(first, 7) == "Cleared",
            "Losing an enemy requires rechecking while old debug snapshots stay immutable.");
        firstState.RoomSecurity.Checked(7, 140);
        Check(State(Project(firstState), 7) == "Cleared", "A completed local sector check restores the room's historical clear display.");
        Console.WriteLine($"PASS: {checks} read-only tactical overlay room progress checks (real game classes)");
    }

    private static void CheckBreachToolRecovery()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        Game previousGame = Current.Game;
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        Current.Game = game;
        try
        {
            var toolDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            toolDef.defName = "HD_Apparel_GW_Sledgehammer";
            Pawn Donor(int id, PawnHealthState state)
            {
                var pawn = new Pawn { thingIDNumber = id, def = toolDef };
                AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
                pawn.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
                AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, state);
                pawn.apparel = new Pawn_ApparelTracker(pawn);
                return pawn;
            }
            Apparel Hammer(Pawn donor, int id)
            {
                var tool = new Apparel { def = toolDef, thingIDNumber = id };
                AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(tool,
                    new List<ThingComp> { new CompSledgehammerBreach { parent = tool } });
                var owner = (ThingOwner<Apparel>)AccessTools.Field(typeof(Pawn_ApparelTracker), "wornApparel").GetValue(donor.apparel);
                owner.InnerListForReading.Add(tool);
                tool.holdingOwner = owner;
                return tool;
            }
            var execution = new MapComponent_RaidTacticalExecution(map);
            var donor = Donor(100, PawnHealthState.Down);
            var tool = Hammer(donor, 101);
            var group = new CombatGroup { roleAssignments = new List<RoleAssignment> { new RoleAssignment { pawn = donor } } };
            var organization = new CombatOrganization { id = "A", rootGroups = new List<CombatGroup> { group } };
            execution.RequestCasualtyReevaluation("A", donor);
            execution.RequestCasualtyReevaluation("A", donor);
            var targets = (List<RaidBreachToolRecoveryTarget>)AccessTools.Field(
                typeof(MapComponent_RaidTacticalExecution), "recoveryTargets").GetValue(execution);
            var pending = (Dictionary<string, HashSet<Pawn>>)AccessTools.Field(
                typeof(MapComponent_RaidTacticalExecution), "pendingCasualties").GetValue(execution);
            Check(targets.Count == 1 && targets[0].Tool == tool && targets[0].OrganizationId == "A",
                "Casualty callbacks capture the hammer once before membership cleanup.");
            MethodInfo source = AccessTools.Method(typeof(RaidBreachToolRecovery), "SourceFor");
            Check(source.Invoke(null, new object[] { tool }) == donor, "A downed carrier remains the interaction source.");
            organization.RemoveMember(donor);
            pending.Clear();
            MethodInfo toolsFor = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "BreachToolsFor");
            Check(!organization.AllMembers.Any()
                && ((IEnumerable<Apparel>)toolsFor.Invoke(execution, new object[] { "A" })).Single() == tool,
                "Roster detachment and consuming casualty events must not lose the recovery equipment.");
            Check(!((IEnumerable<Apparel>)toolsFor.Invoke(execution, new object[] { "B" })).Any(),
                "Recovery equipment remains scoped to its original organization.");
            MethodInfo prune = AccessTools.Method(typeof(MapComponent_RaidTacticalExecution), "PruneBreachTools");
            for (int i = 0; i < 3; i++) prune.Invoke(execution, new object[] { new HashSet<string> { "A" } });
            Check(targets.Count == 1, "Repeated retry/prune passes retain an unresolved hammer.");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(donor.health, PawnHealthState.Dead);
            var corpse = new Corpse();
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(corpse, (sbyte)0);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(donor, (sbyte)-2);
            var corpseOwner = new ThingOwner<Pawn>(corpse);
            corpseOwner.InnerListForReading.Add(donor);
            donor.holdingOwner = corpseOwner;
            Check(source.Invoke(null, new object[] { tool }) == corpse, "A deceased carrier resolves through the actual corpse after detachment.");
            var carrier = Donor(107, PawnHealthState.Mobile);
            var carryOwner = new ThingOwner<Corpse>(new Pawn_CarryTracker(carrier));
            carryOwner.InnerListForReading.Add(corpse);
            corpse.holdingOwner = carryOwner;
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(corpse, (sbyte)-1);
            prune.Invoke(execution, new object[] { new HashSet<string> { "A" } });
            Check(!corpse.Spawned && targets.Count == 1,
                "A temporarily carried corpse retains its claim for another recovery attempt on the same map.");
            // Stripping the corpse preserves the exact tool even far from its donor.
            tool.holdingOwner = null;
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(tool, (sbyte)0);
            Check(source.Invoke(null, new object[] { tool }) == tool, "Stripped, loose equipment becomes its own interaction source.");
            prune.Invoke(execution, new object[] { new HashSet<string> { "A" } });
            Check(targets.Count == 1, "A stripped hammer remains tracked independently of the corpse location.");
            var receiver = Donor(102, PawnHealthState.Mobile);
            var receiverOwner = new ThingOwner<Apparel>(receiver.apparel);
            receiverOwner.InnerListForReading.Add(tool);
            tool.holdingOwner = receiverOwner;
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(tool, (sbyte)-1);
            prune.Invoke(execution, new object[] { new HashSet<string> { "A" } });
            Check(targets.Count == 0, "A hammer worn by a healthy replacement completes recovery.");
            var abandoned = Hammer(Donor(103, PawnHealthState.Down), 104);
            execution.RequestCasualtyReevaluation("A", ((Pawn_ApparelTracker)abandoned.ParentHolder).pawn);
            prune.Invoke(execution, new object[] { new HashSet<string>() });
            Check(targets.Count == 0, "Departed organizations must not leave stale recovery claims.");
            var destroyed = Hammer(Donor(105, PawnHealthState.Down), 106);
            execution.RequestCasualtyReevaluation("A", ((Pawn_ApparelTracker)destroyed.ParentHolder).pawn);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(destroyed, (sbyte)-2);
            prune.Invoke(execution, new object[] { new HashSet<string> { "A" } });
            Check(targets.Count == 0, "Destroyed equipment is removed instead of retried forever.");
            var departingDonor = Donor(108, PawnHealthState.Down);
            Hammer(departingDonor, 109);
            execution.RequestCasualtyReevaluation("A", departingDonor);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(departingDonor, (sbyte)-1);
            prune.Invoke(execution, new object[] { new HashSet<string> { "A" } });
            Check(targets.Count == 0, "Equipment removed from the map must not leave a permanent claim.");

            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
            XElement xml = XDocument.Load(Path.Combine(root, "Defs/GreatWar/Items/Sledgehammer_GreatWar.xml")).Root.Element("ThingDef");
            toolDef.useHitPoints = bool.Parse((string)xml.Element("useHitPoints"));
            toolDef.apparel = new ApparelProperties {
                careIfWornByCorpse = bool.Parse((string)xml.Element("apparel").Element("careIfWornByCorpse")) };
            var durable = new Apparel { def = toolDef, thingIDNumber = 110, HitPoints = 120 };
            var damage = new DamageWorker().Apply(new DamageInfo(new DamageDef { harmsHealth = true }, 50f), durable);
            Check(durable.HitPoints == 120 && damage.totalDamageDealt == 0f && !durable.Destroyed,
                "The XML setting must disable durability damage in the real vanilla damage worker.");
            durable.Notify_PawnKilled();
            Check(!durable.WornByCorpse, "The XML setting must suppress the real vanilla corpse-worn flag.");
            Console.WriteLine($"PASS: {checks} detached casualty equipment capture, retry, source and cleanup checks (real game classes)");
        }
        finally { Current.Game = previousGame; }
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
        ((IDisposable)analysis).Dispose();
        Check(analysis.BuildStatus == "Removed"
            && AccessTools.Property(typeof(MapComponent_TacticalMapAnalysis), "Completed").GetValue(analysis) == null,
            "Map removal and Map.Dispose must both cancel preparation idempotently.");
        Check(versionProperty.GetValue(first) == version && first.RoomAt(new IntVec3(1, 0, 1)) == 7,
            "Clearing the map cache owner cannot mutate an organization's pinned version.");
        Console.WriteLine("PASS: 8 shared structure ownership, restore and version lifetime checks (real game classes)");
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
