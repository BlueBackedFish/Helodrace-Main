using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Helodrace.Squads;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Helodrace
{
    // Explicit command-line audit only. Never changes a normal player session.
    public sealed class MapComponent_RaidMovementRuntimeAudit : MapComponent
    {
        private readonly string output;
        private readonly List<Pawn> raiders = new List<Pawn>();
        private readonly Dictionary<Pawn, IntVec3> starts = new Dictionary<Pawn, IntVec3>();
        private readonly List<Building> buildings = new List<Building>();
        private Lord lord;
        private Faction faction;
        private Pawn owner;
        private int scenario = -1, caseTick, measuredTick, measuredFrame;
        private long measuredPreparationPasses;
        private int sequenceIndex = -1;
        private readonly int[] cases = Enumerable.Range(0, 20).ToArray();
        private long measuredTime;
        private bool initialized, finished;
        private readonly int warmupTicks = 300, sampleTicks = 600;
        private readonly bool functional;
        private readonly bool reactive;
        private int reactiveRequests;
        private readonly bool high;
        private Profiling.ProfileBenchmark benchmark;
        private string fingerprint;
        private readonly bool spawnCommands;
        private RaidSpawnRuntimeAudit spawnAudit;
        private RaidMovementFunctionalAudit functionalAudit;
        private readonly int[] populations = { 0, 50, 100, 200, 400 };
        public MapComponent_RaidMovementRuntimeAudit(Map map) : base(map)
        {
            GenCommandLine.TryGetCommandLineArg("hdRaidMovementAudit", out output);
            high = output != null && GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditHigh", out _);
            functional = output != null && GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditFunctional", out _);
            reactive = output != null && GenCommandLine.TryGetCommandLineArg("hdRaidReactiveAudit", out _);
            spawnCommands = output != null && GenCommandLine.TryGetCommandLineArg("hdRaidSpawnAudit", out _);
            if (output != null && GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditWarmup", out string warmup))
                warmupTicks = int.Parse(warmup);
            if (output != null && GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditSample", out string sample))
                sampleTicks = int.Parse(sample);
            if (warmupTicks < 0 || sampleTicks < 1) throw new ArgumentException("Invalid audit tick duration.");
            if (output != null && GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditCases", out string selected))
            {
                cases = selected.Split(',').Select(int.Parse).ToArray();
                if (cases.Length == 0 || cases.Any(value => value < 0 || value >= 20))
                    throw new ArgumentException("Audit cases must be in 0..19.");
            }
        }
        public override void MapComponentUpdate()
        {
            if (output == null || finished || Current.ProgramState != ProgramState.Playing
                || LongEventHandler.ShouldWaitForEvent) return;
            try
            {
                if (!initialized)
                {
                    initialized = true; Prefs.DevMode = true; RaidCpuProfiler.Enabled = true;
                    Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                    File.WriteAllText(output, "");
                    faction = Find.FactionManager.AllFactionsListForReading.First(value => value.def.defName == (high ? "HD_HelodCivilHighFaction" : "HD_HelodCivilLowFaction"));
                    faction.SetRelation(new FactionRelation(Faction.OfPlayer, FactionRelationKind.Hostile) { baseGoodwill = -100 });
                    faction.RelationWith(Faction.OfPlayer).baseGoodwill = -100;
                    Faction.OfPlayer.RelationWith(faction).baseGoodwill = -100;
                    if (!faction.HostileTo(Faction.OfPlayer)) throw new InvalidOperationException("Audit faction is not hostile.");
                    owner = map.mapPawns.FreeColonists.First(); TacticalAuditProtection.ProtectedOwner = owner;
                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList()) if (pawn != owner) pawn.Destroy(DestroyMode.Vanish);
                    foreach (IntVec3 cell in map.AllCells)
                    {
                        foreach (Thing thing in cell.GetThingList(map).ToList())
                            if (!(thing is Pawn))
                            {
                                // Steam geysers cannot be destroyed and otherwise leave
                                // random non-standable tiles in the isolated test corridor.
                                if (thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
                                else thing.DeSpawn();
                            }
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                        map.roofGrid.SetRoof(cell, null);
                    }
                    BeginCase();
                }
                Find.TickManager.CurTimeSpeed = functional || spawnCommands ? TimeSpeed.Superfast
                    : scenario < 10 ? TimeSpeed.Normal : TimeSpeed.Fast;
                if (spawnCommands)
                {
                    if (spawnAudit == null) spawnAudit = new RaidSpawnRuntimeAudit(map, owner, output, PrepareCaseBuildings);
                    if (spawnAudit.Update()) { finished = true; Application.Quit(); }
                    return;
                }
                if (functional)
                {
                    if (functionalAudit == null) functionalAudit = new RaidMovementFunctionalAudit(map, raiders, owner, output);
                    if (functionalAudit.Update()) { finished = true; Application.Quit(); }
                    return;
                }
                int age = GenTicks.TicksGame - caseTick;
                if (measuredTick < 0 && age >= warmupTicks)
                {
                    measuredTick = GenTicks.TicksGame; measuredFrame = Time.frameCount; measuredTime = Stopwatch.GetTimestamp();
                    RaidCpuProfiler.Reset(map);
                    GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditSeed", out string seed);
                    benchmark = new Profiling.ProfileBenchmark { fixtureVersion = reactive ? 3 : 2, seed = seed, mapFingerprint = fingerprint,
                        faction = faction.def.defName, requestedPopulation = populations[scenario % 5],
                        warmupTicks = warmupTicks, sampleTicks = sampleTicks, startPhases = Phases(),
                        unitCount = map.GetComponent<MapComponent_RaidTacticalPlans>().Plans.Count(),
                        radioOperators = raiders.Count(pawn => RaidTacticalRadioUtility.Radios(pawn).Any()) };
                    measuredPreparationPasses = map.GetComponent<MapComponent_RaidMovementAreas>().PreparationServicePasses;
                    if (!GenCommandLine.TryGetCommandLineArg("hdMethodProfileManual", out _))
                        Profiling.AgentMethodProfiler.Begin("raid-audit-" + scenario, scenario, raiders.Count, scenario < 10 ? 1 : 3,
                            cpu: !GenCommandLine.TryGetCommandLineArg("hdMethodProfileElapsedOnly", out _), seconds: 300, benchmark: benchmark);
                    if (reactive)
                    {
                        reactiveRequests = 0;
                        var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
                        foreach (RaidTacticalUnit unit in RaidTacticalUnit.All)
                            if (unit.Commander?.Spawned == true && unit.Commander.Map == map
                                && execution.StateFor(unit.Id)?.ActivePlan?.Success == true)
                            {
                                execution.NotifySupportRequested(unit.Commander, unit.Commander.Position + IntVec3.East * 20);
                                reactiveRequests++;
                            }
                        if (raiders.Count > 0 && reactiveRequests == 0) throw new InvalidOperationException("Reactive audit has no eligible unit.");
                    }
                }
                if (measuredTick >= 0 && GenTicks.TicksGame - measuredTick >= sampleTicks)
                { FinishCase(); BeginCase(); }
            }
            catch (Exception error)
            {
                finished = true;
                File.AppendAllText(output, "{\"error\":\"" + Escape(error.ToString()) + "\"}\n");
                Log.Error("Raid movement audit failed: " + error);
                Application.Quit();
            }
        }
        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n");
        private void BeginCase()
        {
            if (lord != null) { map.lordManager.RemoveLord(lord); lord = null; }
            foreach (Pawn pawn in raiders) { OrganizationAPI.Registry.Detach(pawn); if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish); }
            raiders.Clear(); starts.Clear();
            foreach (Building building in buildings) if (!building.Destroyed) building.Destroy(DestroyMode.Vanish);
            buildings.Clear();
            sequenceIndex++;
            if (sequenceIndex >= cases.Length) { finished = true; File.AppendAllText(output, "{\"complete\":true}\n"); Application.Quit(); return; }
            scenario = cases[sequenceIndex];
            int count = populations[scenario % 5]; bool tactical = scenario % 10 >= 5;
            if (high && count > 0) count = ((count + 12) / 13) * 13;
            if (functional) { count = 11; tactical = true; }
            if (spawnCommands) count = 0;
            PrepareCaseBuildings();
            if (reactive)
                for (int x = 64; x < 100; x += 7)
                    for (int z = 94; z < 134; z += 7)
                    {
                        var cover = (Building)ThingMaker.MakeThing(ThingDefOf.Sandbags,
                            ThingDefOf.Sandbags.MadeFromStuff ? ThingDefOf.Cloth : null);
                        cover.SetFaction(Faction.OfPlayer);
                        GenSpawn.Spawn(cover, new IntVec3(x, 0, z), map, WipeMode.Vanish); buildings.Add(cover);
                    }
            fingerprint = RaidAuditSeed.Fingerprint(map);
            var formation = DefDatabase<FormationDef>.GetNamed("HD_Formation_GW_RifleSquad");
            var doctrine = DefDatabase<DoctrineDef>.GetNamed("HD_Doctrine_GreatWar");
            var rifle = DefDatabase<PawnKindDef>.GetNamed("HD_GW_HelodRifleman");
            var soldierRole = DefDatabase<RoleDef>.GetNamed("HD_Role_Rifleman");
            var leaderRole = DefDatabase<RoleDef>.GetNamed("HD_Role_SquadLeader");
            var organization = new CombatOrganization { id = OrganizationAPI.Registry.AllocateId(), faction = faction, doctrine = doctrine };
            Rand.PushState(347001);
            try
            {
                List<Pawn> modern = high && count > 0 ? GenerateHigh(count, tactical) : null;
                for (int i = 0; i < count; i++)
                {
                    Pawn pawn = modern != null ? modern[i] : PawnGenerator.GeneratePawn(new PawnGenerationRequest(rifle, faction, PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true, allowDead: false, allowDowned: false, mustBeCapableOfViolence: true));
                    if (i % 11 == 1 || i == count - 1 && i % 11 == 0)
                        pawn.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Apparel_GW_Sledgehammer")));
                    pawn.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Grenade_MKII")));
                    if (i % 3 == 0)
                    {
                        Thing smoke = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Grenade_M8_Item")); smoke.stackCount = 2;
                        pawn.inventory.innerContainer.TryAdd(smoke);
                    }
                    IntVec3 cell = new IntVec3(66 + i % 16, 0, 90 + i / 16);
                    GenSpawn.Spawn(pawn, cell, map); raiders.Add(pawn); starts[pawn] = cell;
                    if (tactical && !high)
                    {
                        if (i % 11 == 0) organization.rootGroups.Add(new CombatGroup { id = organization.id + "/" + i / 11, formation = formation });
                        organization.rootGroups.Last().roleAssignments.Add(new RoleAssignment { pawn = pawn, combatRole = soldierRole,
                            commandRole = i % 11 == 0 ? leaderRole : null, explicitSuccessionOrder = i % 11 });
                    }
                }
                if (tactical && !high && count > 0)
                { organization.RestoreTreeLinks(); foreach (CombatGroup group in organization.rootGroups) group.InitializeCommand(); OrganizationAPI.Registry.Register(organization); }
            }
            finally { Rand.PopState(); }
            if (count > 0) lord = LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false,
                canTimeoutOrFlee: false, sappers: true, canSteal: false), map, raiders);
            caseTick = GenTicks.TicksGame; measuredTick = -1;
            RaidCpuProfiler.Reset(map);
            Log.Message($"Raid audit case {scenario}: count={count}, tactical={tactical}, speed={(scenario < 10 ? 1 : 3)}");
        }
        private List<Pawn> GenerateHigh(int count, bool tactical)
        {
            var result = new List<Pawn>();
            FormationDef squad = DefDatabase<FormationDef>.GetNamed("HD_Formation_MW_RifleSquad");
            for (int index = 0; index < count / 13; index++)
            {
                var generated = OrganizationGenerator.Generate(new PawnGroupMakerParms { faction = faction,
                    groupKind = PawnGroupKindDefOf.Combat, raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                    points = squad.FormationCost, tile = map.Tile, seed = 347001 + index }, out CombatOrganization org);
                if (generated.Count != 13 || org.rootGroups.Count != 1 || org.rootGroups[0].children.Count != 3)
                    throw new InvalidOperationException("HIGH audit must use a full 13-person squad with three fireteams.");
                if (!tactical) foreach (Pawn pawn in generated) OrganizationAPI.Registry.Detach(pawn);
                result.AddRange(generated);
            }
            return result;
        }
        private string Phases() => string.Join(",", map.GetComponent<MapComponent_RaidTacticalPlans>().Plans
            .GroupBy(plan => map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(plan.UnitId)?.Phase.ToString() ?? "NoState")
            .OrderBy(group => group.Key).Select(group => group.Key + ":" + group.Count()));
        private void PrepareCaseBuildings()
        {
            foreach (Building building in buildings) if (!building.Destroyed) building.Destroy(DestroyMode.Vanish);
            buildings.Clear();
            void Place(ThingDef def, IntVec3 position)
            {
                Building building = (Building)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
                building.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(building, position, map, WipeMode.Vanish);
                buildings.Add(building);
            }
            for (int x = 100; x <= 140; x++) { Place(ThingDefOf.Wall, new IntVec3(x, 0, 100)); Place(ThingDefOf.Wall, new IntVec3(x, 0, 136)); }
            for (int z = 101; z < 136; z++)
            {
                Place(ThingDefOf.Wall, new IntVec3(100, 0, z)); Place(ThingDefOf.Wall, new IntVec3(140, 0, z));
                Place(z == 116 ? ThingDefOf.Door : ThingDefOf.Wall, new IntVec3(120, 0, z));
            }
            for (int x = 101; x < 140; x++) for (int z = 101; z < 136; z++)
                map.roofGrid.SetRoof(new IntVec3(x, 0, z), RoofDefOf.RoofConstructed);
            if (functional)
            {
                new IntVec3(120, 0, 130).GetEdifice(map).Destroy(DestroyMode.Vanish);
                Place(ThingDefOf.Door, new IntVec3(120, 0, 130));
                for (int x = 101; x < 140; x++) if (x != 120) Place(ThingDefOf.Wall, new IntVec3(x, 0, 123));
            }
            Place(ThingDefOf.Bed, new IntVec3(134, 0, 117));
            ((Building_Bed)buildings.Last()).CompAssignableToPawn.TryAssignPawn(owner);
            owner.pather.StopDead(); owner.Position = new IntVec3(133, 0, 116);
        }
        private void FinishCase()
        {
            if (benchmark != null) benchmark.endPhases = Phases();
            if (!GenCommandLine.TryGetCommandLineArg("hdMethodProfileManual", out _)) Profiling.AgentMethodProfiler.End();
            double seconds = (Stopwatch.GetTimestamp() - measuredTime) / (double)Stopwatch.Frequency;
            var movement = map.GetComponent<MapComponent_RaidMovementAreas>();
            var plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            var scheduler = Current.Game.GetComponent<GameComponent_RaidPlanScheduler>();
            var executionScheduler = Current.Game.GetComponent<GameComponent_RaidExecutionScheduler>();
            var orders = map.GetComponent<MapComponent_RaidTacticalOrders>();
            var phases = plans.Plans.GroupBy(plan => execution.StateFor(plan.UnitId)?.Phase.ToString() ?? "NoState")
                .Select(group => group.Key + ":" + group.Count());
            var blocks = raiders.Where(pawn => pawn.Spawned).Select(MapComponent_RaidTacticalOrders.For)
                .Where(order => order != null).GroupBy(order => order.Movement.BlockReason)
                .Select(group => group.Key + ":" + group.Count());
            string json = "{\"faction\":\"" + faction.def.defName + "\",\"actualPopulation\":" + raiders.Count
                + ",\"mapFingerprint\":\"" + fingerprint + "\",\"startPhases\":\"" + Escape(benchmark?.startPhases ?? "")
                + "\",\"radioOperators\":" + (benchmark?.radioOperators ?? 0)
                + ",\"scenario\":" + scenario + ",\"population\":" + populations[scenario % 5]
                + ",\"tactical\":" + (scenario % 10 >= 5 ? "true" : "false") + ",\"speed\":" + (scenario < 10 ? 1 : 3)
                + ",\"TPS\":" + ((GenTicks.TicksGame - measuredTick) / seconds).ToString("0.000", CultureInfo.InvariantCulture)
                + ",\"FPS\":" + ((Time.frameCount - measuredFrame) / seconds).ToString("0.000", CultureInfo.InvariantCulture)
                + ",\"moved\":" + raiders.Count(pawn => pawn.Spawned && pawn.Position != starts[pawn])
                + ",\"alive\":" + raiders.Count(pawn => pawn.Spawned && !pawn.Dead && !pawn.Downed)
                + ",\"warmupTicks\":" + warmupTicks + ",\"sampleTicks\":" + sampleTicks
                + ",\"reactiveRequests\":" + reactiveRequests
                + ",\"phases\":\"" + Escape(string.Join(",", phases)) + "\""
                + ",\"successfulPlans\":" + plans.Plans.Count(plan => plan.Success)
                + ",\"planningService\":{\"pending\":" + scheduler.Pending + ",\"slices\":" + scheduler.Slices
                + ",\"completed\":" + scheduler.Completed + ",\"discarded\":" + scheduler.Discarded
                + ",\"budgetStops\":" + scheduler.BudgetStops + ",\"maxSliceMs\":"
                + scheduler.MaxSliceMilliseconds.ToString("0.000", CultureInfo.InvariantCulture) + "}"
                + ",\"executionService\":{\"pending\":" + executionScheduler.Pending
                + ",\"updates\":" + executionScheduler.Updates + ",\"urgent\":" + executionScheduler.UrgentUpdates
                + ",\"budgetStops\":" + executionScheduler.BudgetStops + ",\"maxDelayTicks\":" + executionScheduler.MaximumDelay
                + ",\"oldestDelayTicks\":" + executionScheduler.OldestDelay + ",\"maxUnitMs\":"
                + executionScheduler.MaximumUnitMilliseconds.ToString("0.000", CultureInfo.InvariantCulture) + "}"
                + ",\"orderReviews\":{\"count\":" + orders.ReviewCount + ",\"maxDelayTicks\":" + orders.MaximumReviewDelay + "}"
                + ",\"unstartedMoves\":\"" + Escape(orders.PendingMovesReport()) + "\""
                + ",\"unstartedMoveDetails\":\"" + Escape(orders.PendingMovesDetail()) + "\""
                + ",\"passageTraffic\":{\"waiting\":" + execution.PassageWaiting + "}"
                + ",\"queueMaintenance\":{\"cleanupPasses\":" + execution.OpeningCleanupPasses
                + ",\"leasePrunePasses\":" + execution.OpeningPrunePasses + "}"
                + ",\"planningWork\":{\"maxBreachChecks\":" + plans.Plans.Select(plan => plan.Work.BreachChecks).DefaultIfEmpty().Max()
                + ",\"maxRouteSteps\":" + plans.Plans.Select(plan => plan.Work.RouteSteps).DefaultIfEmpty().Max()
                + ",\"limitedPlans\":" + plans.Plans.Count(plan => plan.Work.Limited) + "}"
                + ",\"failedPlans\":\"" + Escape(string.Join(";", plans.Plans.Where(plan => !plan.Success)
                    .Select(plan => plan.Reason + " | " + plan.BreachSearch))) + "\""
                + ",\"nativeBytes\":" + movement.NativeMemoryBytes + ",\"peakNativeBytes\":" + movement.PeakNativeMemoryBytes
                + ",\"cacheCumulative\":{\"created\":" + movement.CreatedGrids + ",\"hits\":" + movement.CacheHits
                + ",\"evictions\":" + movement.CacheEvictions + ",\"memoryWaits\":" + movement.MemoryDeferrals
                + ",\"peakPending\":" + movement.PeakPendingGrids + ",\"notifications\":" + movement.PreparedNotifications + "}"
                + ",\"cancelledPreparations\":" + movement.CancelledPreparations + ",\"peakWaitFrames\":" + movement.PeakWaitFrames
                + ",\"pending\":" + movement.PendingGrids + ",\"blocked\":\"" + Escape(string.Join(",", blocks))
                + "\",\"preparationServicePasses\":" + (movement.PreparationServicePasses - measuredPreparationPasses)
                + ",\"cpuCumulative\":\"" + Escape(RaidCpuProfiler.Report(map)) + "\"}\n";
            File.AppendAllText(output, json); Log.Message("Raid audit sample: " + json);
            if (scenario % 10 >= 5 && populations[scenario % 5] > 0 && !plans.Plans.Any(plan => plan.Success))
                throw new InvalidOperationException("Invalid audit: tactical plans all failed; these values are not a tactical benchmark.");
        }
    }
}
