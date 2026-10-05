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
        internal static Pawn ProtectedOwner;
        private int scenario = -1, caseTick, measuredTick, measuredFrame;
        private int sequenceIndex = -1;
        private readonly int[] cases = Enumerable.Range(0, 20).ToArray();
        private long measuredTime;
        private bool initialized, finished;
        private readonly int warmupTicks = 300, sampleTicks = 600;
        private readonly bool functional;
        private RaidMovementFunctionalAudit functionalAudit;
        private readonly int[] populations = { 0, 50, 100, 200, 400 };
        public MapComponent_RaidMovementRuntimeAudit(Map map) : base(map)
        {
            GenCommandLine.TryGetCommandLineArg("hdRaidMovementAudit", out output);
            functional = output != null && GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditFunctional", out _);
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
                    faction = Find.FactionManager.AllFactionsListForReading.First(value => value.def.defName == "HD_HelodCivilLowFaction");
                    faction.SetRelation(new FactionRelation(Faction.OfPlayer, FactionRelationKind.Hostile) { baseGoodwill = -100 });
                    faction.RelationWith(Faction.OfPlayer).baseGoodwill = -100;
                    Faction.OfPlayer.RelationWith(faction).baseGoodwill = -100;
                    if (!faction.HostileTo(Faction.OfPlayer)) throw new InvalidOperationException("Audit faction is not hostile.");
                    owner = map.mapPawns.FreeColonists.First(); ProtectedOwner = owner;
                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList()) if (pawn != owner) pawn.Destroy(DestroyMode.Vanish);
                    foreach (IntVec3 cell in CellRect.FromLimits(new IntVec3(45, 0, 65), new IntVec3(155, 0, 170)))
                    {
                        foreach (Thing thing in cell.GetThingList(map).ToList()) if (!(thing is Pawn)) thing.Destroy(DestroyMode.Vanish);
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                        map.roofGrid.SetRoof(cell, null);
                    }
                    BeginCase();
                }
                Find.TickManager.CurTimeSpeed = functional ? TimeSpeed.Superfast
                    : scenario < 10 ? TimeSpeed.Normal : TimeSpeed.Fast;
                if (functional)
                {
                    if (functionalAudit == null) functionalAudit = new RaidMovementFunctionalAudit(map, raiders, owner, output);
                    if (functionalAudit.Update()) { finished = true; Application.Quit(); }
                    return;
                }
                int age = GenTicks.TicksGame - caseTick;
                if (measuredTick < 0 && age >= warmupTicks)
                { measuredTick = GenTicks.TicksGame; measuredFrame = Time.frameCount; measuredTime = Stopwatch.GetTimestamp(); }
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
            if (functional) { count = 11; tactical = true; }
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
            var formation = DefDatabase<FormationDef>.GetNamed("HD_Formation_GW_RifleSquad");
            var doctrine = DefDatabase<DoctrineDef>.GetNamed("HD_Doctrine_GreatWar");
            var rifle = DefDatabase<PawnKindDef>.GetNamed("HD_GW_HelodRifleman");
            var soldierRole = DefDatabase<RoleDef>.GetNamed("HD_Role_Rifleman");
            var leaderRole = DefDatabase<RoleDef>.GetNamed("HD_Role_SquadLeader");
            var organization = new CombatOrganization { id = OrganizationAPI.Registry.AllocateId(), faction = faction, doctrine = doctrine };
            Rand.PushState(347001);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(rifle, faction, PawnGenerationContext.NonPlayer,
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
                    if (tactical)
                    {
                        if (i % 11 == 0) organization.rootGroups.Add(new CombatGroup { id = organization.id + "/" + i / 11, formation = formation });
                        organization.rootGroups.Last().roleAssignments.Add(new RoleAssignment { pawn = pawn, combatRole = soldierRole,
                            commandRole = i % 11 == 0 ? leaderRole : null, explicitSuccessionOrder = i % 11 });
                    }
                }
                if (tactical && count > 0)
                { organization.RestoreTreeLinks(); foreach (CombatGroup group in organization.rootGroups) group.InitializeCommand(); OrganizationAPI.Registry.Register(organization); }
            }
            finally { Rand.PopState(); }
            if (count > 0) lord = LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false,
                canTimeoutOrFlee: false, sappers: true, canSteal: false), map, raiders);
            caseTick = GenTicks.TicksGame; measuredTick = -1;
            RaidCpuProfiler.Reset(map);
            Log.Message($"Raid audit case {scenario}: count={count}, tactical={tactical}, speed={(scenario < 10 ? 1 : 3)}");
        }
        private void FinishCase()
        {
            double seconds = (Stopwatch.GetTimestamp() - measuredTime) / (double)Stopwatch.Frequency;
            var movement = map.GetComponent<MapComponent_RaidMovementAreas>();
            var plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            var scheduler = Current.Game.GetComponent<GameComponent_RaidPlanScheduler>();
            var phases = plans.Plans.GroupBy(plan => execution.StateFor(plan.UnitId)?.Phase.ToString() ?? "NoState")
                .Select(group => group.Key + ":" + group.Count());
            var blocks = raiders.Where(pawn => pawn.Spawned).Select(MapComponent_RaidTacticalOrders.For)
                .Where(order => order != null).GroupBy(order => order.Movement.BlockReason)
                .Select(group => group.Key + ":" + group.Count());
            string json = "{\"scenario\":" + scenario + ",\"population\":" + populations[scenario % 5]
                + ",\"tactical\":" + (scenario % 10 >= 5 ? "true" : "false") + ",\"speed\":" + (scenario < 10 ? 1 : 3)
                + ",\"TPS\":" + ((GenTicks.TicksGame - measuredTick) / seconds).ToString("0.000", CultureInfo.InvariantCulture)
                + ",\"FPS\":" + ((Time.frameCount - measuredFrame) / seconds).ToString("0.000", CultureInfo.InvariantCulture)
                + ",\"moved\":" + raiders.Count(pawn => pawn.Spawned && pawn.Position != starts[pawn])
                + ",\"alive\":" + raiders.Count(pawn => pawn.Spawned && !pawn.Dead && !pawn.Downed)
                + ",\"warmupTicks\":" + warmupTicks + ",\"sampleTicks\":" + sampleTicks
                + ",\"phases\":\"" + Escape(string.Join(",", phases)) + "\""
                + ",\"successfulPlans\":" + plans.Plans.Count(plan => plan.Success)
                + ",\"planningService\":{\"pending\":" + scheduler.Pending + ",\"slices\":" + scheduler.Slices
                + ",\"completed\":" + scheduler.Completed + ",\"discarded\":" + scheduler.Discarded
                + ",\"budgetStops\":" + scheduler.BudgetStops + ",\"maxSliceMs\":"
                + scheduler.MaxSliceMilliseconds.ToString("0.000", CultureInfo.InvariantCulture) + "}"
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
                + "\",\"cpuCumulative\":\"" + Escape(RaidCpuProfiler.Report(map)) + "\"}\n";
            File.AppendAllText(output, json); Log.Message("Raid audit sample: " + json);
            if (scenario % 10 >= 5 && populations[scenario % 5] > 0 && !plans.Plans.Any(plan => plan.Success))
                throw new InvalidOperationException("Invalid audit: tactical plans all failed; these values are not a tactical benchmark.");
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class Patch_RaidRuntimeAudit_Owner
    {
        private static bool Prefix(Thing __instance, ref DamageWorker.DamageResult __result)
        {
            if (__instance != MapComponent_RaidMovementRuntimeAudit.ProtectedOwner) return true;
            __result = new DamageWorker.DamageResult(); return false;
        }
    }
}
