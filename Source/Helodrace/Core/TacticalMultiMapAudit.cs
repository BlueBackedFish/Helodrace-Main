using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace.Tactics;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public bool r7TwoActualMaps, r7GlobalBudgetShared, r7CrossMapCommunicationBlocked;
        [DataMember] public bool r7RemovedMapClean, r7RemainingMapPreserved, r7RemainingMapCompleted;
        [DataMember] public int r7BudgetTicks, r7TwoMapTicks, r7MaxGlobalJobs, r7MaxGlobalReturns, r7MaxGlobalPaths;
        [DataMember] public int r7MaxGlobalPlans, r7MaxGlobalObservations, r7MaxGlobalCommunications;
        [DataMember] public string[] r7MultiMapEvents;
        [DataMember] public int r7SecondaryPopulation, r7PrimaryJobsBeforeRemoval, r7SecondaryJobsBeforeRemoval;
        [DataMember] public string r7SecondaryMapFingerprint, r7SecondaryPawnFingerprint;
    }

    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool MultiMapFixture => result.fixtureCase == "r7-multimap";
        private bool secondaryMapFixture;
        private Map secondaryMap;
        private MapComponent_TacticalCommands removedService;
        private int removedMapAt = -1;
        private readonly List<string> multiMapEvents = new List<string>();
        internal static MapComponent_TacticalEngineAudit MultiMapOwner;
        private void MultiMapEvent(string message)
        {
            string entry = (GenTicks.TicksGame - started) + ":" + message;
            multiMapEvents.Add(entry); result.r7MultiMapEvents = multiMapEvents.ToArray();
            Log.Message("R7 multi-map " + entry);
        }
        private void InitializeMultiMapDrill()
        {
            if (result.units != 1 || TacticalEngineSelection.Kind != TacticalEngineKind.New)
                throw new InvalidOperationException("Multi-map drill requires one complete squad per actual map and the new engine.");
            var neighbors = new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(map.Tile, neighbors);
            PlanetTile tile = neighbors.First(t => !Find.WorldGrid[t].WaterCovered
                && Find.WorldGrid[t].hilliness != Hilliness.Impassable && Find.WorldObjects.MapParentAt(t) == null);
            MapParent parent = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
            secondaryMap = MapGenerator.GenerateMap(map.Size, parent, parent.MapGeneratorDef);
            MapComponent_TacticalEngineAudit fixture = secondaryMap.GetComponent<MapComponent_TacticalEngineAudit>();
            // The CLI is global, so explicitly disable the second map's writer
            // and automatic setup before creating its real physical stimulus.
            fixture.secondaryMapFixture = true; fixture.finished = true;
            Pawn colonist = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(colonist, new IntVec3(155, 0, 155), secondaryMap);
            fixture.Initialize(); fixture.initialized = true;
            result.r7SecondaryPopulation = fixture.result.population;
            result.r7SecondaryMapFingerprint = fixture.result.mapFingerprint;
            result.r7SecondaryPawnFingerprint = fixture.result.pawnFingerprint;
            foreach (Pawn pawn in raiders) ProtectedRaiders.Add(pawn);
            owner.Position = fixture.owner.Position = new IntVec3(180, 0, 180);
            MapComponent_RaidMovementRuntimeAudit.ProtectedOwner = owner;
            Current.Game.CurrentMap = map;
            result.r7TwoActualMaps = Current.Game.Maps.Contains(map) && Current.Game.Maps.Contains(secondaryMap)
                && map != secondaryMap && map.uniqueID != secondaryMap.uniqueID
                && map.GetComponent<MapComponent_TacticalCommands>().Commands.Count() == 1
                && secondaryMap.GetComponent<MapComponent_TacticalCommands>().Commands.Count() == 1;
            result.r7CrossMapCommunicationBlocked = result.r7RemainingMapPreserved = result.r7GlobalBudgetShared = true;
            MultiMapOwner = this;
            MultiMapEvent("created actual maps " + map.uniqueID + "/" + secondaryMap.uniqueID + " squads=1/1 population="
                + result.population + "/" + result.r7SecondaryPopulation);
            if (!result.r7TwoActualMaps) throw new InvalidOperationException("Multi-map drill did not register both native maps.");
        }
        private void ApplyMultiMapDrill()
        {
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            TacticalSquadCommand survivor = service.Commands.Single();
            if (removedMapAt < 0)
            {
                MapComponent_TacticalCommands other = secondaryMap.GetComponent<MapComponent_TacticalCommands>();
                TacticalCommunications network = Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications;
                result.r7CrossMapCommunicationBlocked &= network.MessagesSent == 0
                    && service.Commands.Concat(other.Commands).All(c => c.Link.Cooperation.Agenda == null);
                if (service.JobsIssued < 10 || other.JobsIssued < 10 || result.r7TwoMapTicks < 120) return;
                string jobs = NativeJobs(survivor), history = History(survivor);
                TacticalLocalPlan plan = survivor.Plan;
                int claims = service.ClaimCount, leases = service.LeaseCount, owners = service.OwnedPawnCount;
                removedService = other; removedMapAt = GenTicks.TicksGame;
                result.r7PrimaryJobsBeforeRemoval = (int)service.JobsIssued;
                result.r7SecondaryJobsBeforeRemoval = (int)other.JobsIssued;
                // Exercise the real removal lifecycle, never call MapRemoved
                // directly or empty the command dictionaries from the fixture.
                Current.Game.DeinitAndRemoveMap(secondaryMap, false);
                result.r7RemovedMapClean = secondaryMap.Disposed && !Current.Game.Maps.Contains(secondaryMap)
                    && !other.Commands.Any() && other.OwnedPawnCount == 0 && other.ClaimCount == 0
                    && other.LeaseCount == 0 && other.KnownOpeningCount == 0
                    && Current.Game.GetComponent<GameComponent_TacticalCommands>().ScheduledCount == 1;
                result.r7RemainingMapPreserved &= service.Commands.Single() == survivor && survivor.Plan == plan
                    && NativeJobs(survivor) == jobs && History(survivor) == history
                    && service.ClaimCount == claims && service.LeaseCount == leases && service.OwnedPawnCount == owners;
                MultiMapEvent("removed second map; clean=" + result.r7RemovedMapClean + " survivor=" + result.r7RemainingMapPreserved);
                if (!result.r7RemovedMapClean || !result.r7RemainingMapPreserved)
                    throw new InvalidOperationException("Removing a native map leaked commands or changed the remaining map.");
            }
            result.r7RemainingMapPreserved &= removedService.OwnedPawnCount == 0 && !removedService.Commands.Any()
                && Current.Game.GetComponent<GameComponent_TacticalCommands>().ScheduledCount == 1;
            if (!result.r7RemainingMapCompleted && survivor.Phase == TacticalCommandPhase.Complete
                && survivor.Members.All(m => m.EntryAssignmentDone))
            {
                result.r7RemainingMapCompleted = true;
                MultiMapEvent("remaining map completed all native entry assignments");
            }
        }
        internal void AuditMultiMapTickBudget()
        {
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            int Value(string name) => (int)AccessTools.Field(typeof(TacticalWorkBudget), name).GetValue(budget);
            result.r7BudgetTicks++; if (removedMapAt < 0) result.r7TwoMapTicks++;
            result.r7MaxGlobalJobs = Math.Max(result.r7MaxGlobalJobs, Value("jobs"));
            result.r7MaxGlobalReturns = Math.Max(result.r7MaxGlobalReturns, Value("returns"));
            result.r7MaxGlobalPaths = Math.Max(result.r7MaxGlobalPaths, Value("paths"));
            result.r7MaxGlobalPlans = Math.Max(result.r7MaxGlobalPlans, Value("plans"));
            result.r7MaxGlobalObservations = Math.Max(result.r7MaxGlobalObservations, Value("observations"));
            result.r7MaxGlobalCommunications = Math.Max(result.r7MaxGlobalCommunications, Value("communications"));
            result.r7GlobalBudgetShared &= result.r7MaxGlobalJobs <= TacticalWorkBudget.JobLimit
                && result.r7MaxGlobalReturns <= TacticalWorkBudget.ReturnLimit && result.r7MaxGlobalPaths <= TacticalWorkBudget.PathLimit
                && result.r7MaxGlobalPlans <= TacticalWorkBudget.PlanLimit && result.r7MaxGlobalObservations <= TacticalWorkBudget.ObserveLimit
                && result.r7MaxGlobalCommunications <= 2;
        }
    }

    // Installed only for this functional audit. No production per-tick hook.
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class Patch_TacticalMultiMapBudgetAudit
    {
        private static bool Prepare() => GenCommandLine.TryGetCommandLineArg("hdTacticalAuditCase", out string value)
            && value == "r7-multimap";
        private static void Postfix() => MapComponent_TacticalEngineAudit.MultiMapOwner?.AuditMultiMapTickBudget();
    }
}
