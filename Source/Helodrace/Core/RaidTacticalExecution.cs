using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using Helodrace.Tactical;
using Helodrace.ModernWar;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    public enum RaidExecutionPhase
    {
        Assemble,
        Breach,
        WithdrawFromCharge,
        Detonation,
        ObserveOpening,
        Support,
        EntryWait,
        CrossBreach,
        Flank,
        Assault,
        ClearRoom,
        SecureRoom,
        Hold,
        Complete
    }

    public enum RaidBreachKind
    {
        None,
        PowerCutter,
        Sledgehammer,
        C4
    }

    public enum RaidExternalSupportKind
    {
        None,
        Air,
        Artillery
    }

    [LegacyTactical]
    public sealed partial class MapComponent_RaidTacticalExecution : MapComponent
    {
        private const int AssembleTimeout = 720;
        private const int BreachTimeout = 900;
        private const int WithdrawalTimeout = 600;
        private const int DetonationTimeout = 240;
        private const int SupportTimeout = 900;
        private const int FlankTimeout = 600;
        private const int ApproachStallTimeout = 1200;
        private readonly Dictionary<string, ExecutionState> states = new Dictionary<string, ExecutionState>();
        private List<ExecutionState> savedStates;
        private readonly HashSet<string> waitingStructures = new HashSet<string>();

        public sealed class BreachCrossing : IExposable
        {
            public Pawn Pawn;
            public RaidBreachProgress Progress;
            public IntVec3 Destination = IntVec3.Invalid;
            public int SearchAfter;

            public void ExposeData()
            {
                Scribe_References.Look(ref Pawn, "pawn");
                Scribe_Values.Look(ref Progress, "progress");
                Scribe_Values.Look(ref Destination, "destination", IntVec3.Invalid);
                Scribe_Values.Look(ref SearchAfter, "searchAfter");
            }
        }

        public sealed class ExecutionState : IExposable
        {
            public ExecutionState() { }
            public string OrganizationId;
            public string UnitId;
            public string GroupId;
            public string PlanKey;
            public IntVec3 Objective;
            public IntVec3 FinalObjective;
            public bool ClearingRooms;
            public bool BedSecured;
            public List<IntVec3> ClearedRoomCells = new List<IntVec3>();
            public RaidTacticalManeuver Maneuver;
            public RaidExecutionPhase Phase;
            public int PhaseStarted;
            public int ReadySince = -1;
            public bool ApproachComplete;
            public int CurrentNode;
            public List<RaidNodeMemberProgress> NodeMembers = new List<RaidNodeMemberProgress>();
            internal readonly RaidTacticalIndices Indices = new RaidTacticalIndices();
            internal int ObserverCursor;
            public int ApproachProgressTick;
            public float ApproachBestRemaining = float.MaxValue;
            public int BreachAttempts;
            public Pawn Breacher;
            public Building BreachTarget;
            public RaidBreachKind BreachKind;
            public bool WithdrawalIssued;
            public Pawn Thrower;
            public RaidEntryObservation Observation;
            public bool SupportIssued;
            public bool SupportLaunched;
            public bool SupportReturnRequired;
            public Projectile SupportProjectile;
            public ThingDef SupportProjectileDef;
            public int SupportEffectsClearedTick = -1;
            public string SupportStatus = "Not requested";
            public bool ApproachSmokeActive;
            public RaidSmokeFormation SmokeFormation;
            public bool ApproachSmokeLaunched;
            public Pawn ApproachSmokeThrower;
            public Projectile ApproachSmokeProjectile;
            public IntVec3 ApproachSmokeTarget = IntVec3.Invalid;
            public IntVec3 ApproachSmokeThreat = IntVec3.Invalid;
            public int ApproachSmokeStarted;
            public int ApproachSmokeClearedTick = -1;
            public int NextApproachSmokeTick;
            public bool FlankIssued;
            public bool AssaultIssued;
            public bool ExternalSupportAttempted;
            public RaidExternalSupportKind ExternalSupportKind;
            public Pawn ExternalSupportCaller;
            public Pawn ExternalSupportTarget;
            public IntVec3 ExternalSupportTargetCell;
            public int ExternalSupportClearedTick;
            public string DoorStateSignature;
            public int LastRoomSecurityTick;
            public int LastRoomPlanTick = -30;
            internal readonly RaidRoomPlanAttempts<IntVec3> RoomPlanAttempts = new RaidRoomPlanAttempts<IntVec3>();
            internal RaidTacticalPlan RoomSearchPlan;
            internal int RoomSearchRevision = -1;
            internal List<IntVec3> RoomSearchTargets;
            internal IntVec3 PendingRoomGoal = IntVec3.Invalid;
            internal IntVec3 PendingRecheckGoal = IntVec3.Invalid;
            internal RaidCqbLocalMap LocalCqb;
            internal int LastLocalReplanTick = -60;
            internal int LastCqbValidationTick = -30;
            public int LastDoorResponseTick;
            public IntVec3 CrossingBreach = IntVec3.Invalid;
            public List<BreachCrossing> Crossings = new List<BreachCrossing>();
            public List<RaidExteriorIngress> ExteriorIngress = new List<RaidExteriorIngress>();
            public List<RaidReactivePosition> Reactions = new List<RaidReactivePosition>();
            public int DefenseUntil;
            public Pawn DefenseCaller;
            public IntVec3 DefenseAim = IntVec3.Invalid;
            public int ScreenAdvanceUntil;
            public RaidContactMemory Contacts = new RaidContactMemory();
            internal readonly TacticalReactionCadence RoutineReactions = new TacticalReactionCadence();
            internal int ObservationCursor;
            internal int VisibleEnemiesTick = -1000, VisibleEnemiesRevision = -1;
            internal List<Pawn> VisibleEnemies = new List<Pawn>();
            public RaidCommunicationState Communication = new RaidCommunicationState();
            internal RaidCqbKnowledge CqbKnowledge = new RaidCqbKnowledge();
            public List<RaidContactGuard> ContactGuards = new List<RaidContactGuard>();
            public bool ContactPause;
            [System.NonSerialized] internal bool SharedOpeningWait;
            public RaidRoomSecurity RoomSecurity = new RaidRoomSecurity();
            // Persist committed positions together with the execution progress.
            public RaidTacticalPlan ActivePlan;

            public void ExposeData()
            {
                Scribe_Values.Look(ref OrganizationId, "organizationId");
                Scribe_Values.Look(ref UnitId, "unitId");
                Scribe_Values.Look(ref GroupId, "groupId");
                Scribe_Values.Look(ref PlanKey, "planKey");
                Scribe_Values.Look(ref Objective, "objective");
                Scribe_Values.Look(ref FinalObjective, "finalObjective");
                Scribe_Values.Look(ref ClearingRooms, "clearingRooms");
                Scribe_Values.Look(ref BedSecured, "bedSecured");
                Scribe_Collections.Look(ref ClearedRoomCells, "clearedRoomCells",
                    LookMode.Value);
                if (Scribe.mode == LoadSaveMode.PostLoadInit && ClearedRoomCells == null)
                    ClearedRoomCells = new List<IntVec3>();
                Scribe_Values.Look(ref Maneuver, "maneuver");
                Scribe_Values.Look(ref Phase, "phase");
                Scribe_Values.Look(ref PhaseStarted, "phaseStarted");
                Scribe_Values.Look(ref ReadySince, "readySince", -1);
                Scribe_Values.Look(ref ApproachComplete, "approachComplete");
                Scribe_Values.Look(ref CurrentNode, "currentNode");
                Scribe_Collections.Look(ref NodeMembers, "nodeMembers", LookMode.Deep);
                Scribe_Values.Look(ref ApproachProgressTick, "approachProgressTick");
                Scribe_Values.Look(ref ApproachBestRemaining, "approachBestRemaining",
                    float.MaxValue);
                Scribe_Values.Look(ref BreachAttempts, "breachAttempts");
                Scribe_References.Look(ref Breacher, "breacher");
                Scribe_References.Look(ref BreachTarget, "breachTarget");
                Scribe_Values.Look(ref BreachKind, "breachKind");
                Scribe_Values.Look(ref WithdrawalIssued, "withdrawalIssued");
                Scribe_References.Look(ref Thrower, "thrower");
                Scribe_Deep.Look(ref Observation, "openingObservation");
                Scribe_Values.Look(ref SupportIssued, "supportIssued");
                Scribe_Values.Look(ref SupportLaunched, "supportLaunched");
                Scribe_Values.Look(ref SupportReturnRequired, "supportReturnRequired");
                Scribe_References.Look(ref SupportProjectile, "supportProjectile");
                Scribe_Defs.Look(ref SupportProjectileDef, "supportProjectileDef");
                Scribe_Values.Look(ref SupportEffectsClearedTick, "supportEffectsClearedTick", -1);
                Scribe_Values.Look(ref SupportStatus, "supportStatus", "Not requested");
                Scribe_Values.Look(ref ApproachSmokeActive, "approachSmokeActive");
                Scribe_Deep.Look(ref SmokeFormation, "smokeFormation");
                Scribe_Values.Look(ref ApproachSmokeLaunched, "approachSmokeLaunched");
                Scribe_References.Look(ref ApproachSmokeThrower, "approachSmokeThrower");
                Scribe_References.Look(ref ApproachSmokeProjectile, "approachSmokeProjectile");
                Scribe_Values.Look(ref ApproachSmokeTarget, "approachSmokeTarget", IntVec3.Invalid);
                Scribe_Values.Look(ref ApproachSmokeThreat, "approachSmokeThreat", IntVec3.Invalid);
                Scribe_Values.Look(ref ApproachSmokeStarted, "approachSmokeStarted");
                Scribe_Values.Look(ref ApproachSmokeClearedTick, "approachSmokeClearedTick", -1);
                Scribe_Values.Look(ref NextApproachSmokeTick, "nextApproachSmokeTick");
                Scribe_Values.Look(ref FlankIssued, "flankIssued");
                Scribe_Values.Look(ref AssaultIssued, "assaultIssued");
                Scribe_Values.Look(ref ExternalSupportAttempted, "externalSupportAttempted");
                Scribe_Values.Look(ref ExternalSupportKind, "externalSupportKind");
                Scribe_References.Look(ref ExternalSupportCaller, "externalSupportCaller");
                Scribe_References.Look(ref ExternalSupportTarget, "externalSupportTarget");
                Scribe_Values.Look(ref ExternalSupportTargetCell, "externalSupportTargetCell");
                Scribe_Values.Look(ref ExternalSupportClearedTick,
                    "externalSupportClearedTick");
                Scribe_Values.Look(ref DoorStateSignature, "doorStateSignature");
                Scribe_Values.Look(ref LastRoomSecurityTick,
                    "lastRoomSecurityTick");
                Scribe_Values.Look(ref LastRoomPlanTick, "lastRoomPlanTick", -30);
                Scribe_Values.Look(ref LastDoorResponseTick,
                    "lastDoorResponseTick");
                Scribe_Values.Look(ref CrossingBreach, "crossingBreach", IntVec3.Invalid);
                Scribe_Collections.Look(ref Crossings, "crossings", LookMode.Deep);
                Scribe_Collections.Look(ref ExteriorIngress, "exteriorIngress", LookMode.Deep);
                if (Scribe.mode == LoadSaveMode.PostLoadInit && ExteriorIngress == null)
                    ExteriorIngress = new List<RaidExteriorIngress>();
                Scribe_Collections.Look(ref Reactions, "reactions", LookMode.Deep);
                Scribe_Values.Look(ref DefenseUntil, "defenseUntil");
                Scribe_References.Look(ref DefenseCaller, "defenseCaller");
                Scribe_Values.Look(ref DefenseAim, "defenseAim", IntVec3.Invalid);
                Scribe_Values.Look(ref ScreenAdvanceUntil, "screenAdvanceUntil");
                if (Scribe.mode == LoadSaveMode.PostLoadInit && Reactions == null)
                    Reactions = new List<RaidReactivePosition>();
                Scribe_Deep.Look(ref ActivePlan, "activePlan");
                Scribe_Deep.Look(ref Contacts, "contacts");
                Scribe_Deep.Look(ref Communication, "communication");
                Scribe_Deep.Look(ref CqbKnowledge, "cqbKnowledge");
                Scribe_Collections.Look(ref ContactGuards, "contactGuards", LookMode.Deep);
                Scribe_Values.Look(ref ContactPause, "contactPause");
                Scribe_Deep.Look(ref RoomSecurity, "roomSecurity");
                if (Scribe.mode == LoadSaveMode.PostLoadInit && RoomSecurity == null) RoomSecurity = new RaidRoomSecurity();
                if (Scribe.mode == LoadSaveMode.PostLoadInit && ContactGuards == null) ContactGuards = new List<RaidContactGuard>();
                if (Scribe.mode == LoadSaveMode.PostLoadInit && Contacts == null) Contacts = new RaidContactMemory();
                if (Scribe.mode == LoadSaveMode.PostLoadInit && Crossings == null)
                    Crossings = new List<BreachCrossing>();
            }
        }

        public MapComponent_RaidTacticalExecution(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) savedStates = states.Values.ToList();
            Scribe_Collections.Look(ref savedStates, "raidTacticalExecution", LookMode.Deep);
            Scribe_Collections.Look(ref recoveryTargets, "raidBreachToolRecovery", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (recoveryTargets == null) recoveryTargets = new List<RaidBreachToolRecoveryTarget>();
                states.Clear();
                foreach (ExecutionState state in savedStates ?? new List<ExecutionState>())
                    if (state?.UnitId != null) states[state.UnitId] = state;
                savedStates = null;
            }
        }

        public string Status(string unitId)
        {
            if (unitId == null || !states.TryGetValue(unitId,
                out ExecutionState state)) return "Inactive";
            if (state.Reactions.Any(value => value.Kind == RaidReactionKind.Explosion && value.Until > GenTicks.TicksGame))
                return "Grenade evasion";
            if (state.DefenseUntil > GenTicks.TicksGame) return "Support defense";
            if (state.ScreenAdvanceUntil > GenTicks.TicksGame) return "Advance inside smoke";
            if (state.ApproachSmokeActive) return "Screening smoke";
            if (state.SmokeFormation != null) return "Smoke rally";
            if (state.Reactions.Any(value => value.Kind == RaidReactionKind.Sniper && value.Until > GenTicks.TicksGame))
                return "Sniper cover";
            if (state.Phase == RaidExecutionPhase.Assemble && !state.ApproachComplete)
                return "Approach";
            return state.Phase.ToString();
        }

        public RaidTacticalPlan ActivePlanFor(string unitId)
        {
            if (unitId == null || !states.TryGetValue(unitId, out ExecutionState state)
                || state.UnitId != unitId || state.ActivePlan?.UnitId != unitId
                || state.ActivePlan?.Success != true || state.Phase == RaidExecutionPhase.Complete
                || state.Phase == RaidExecutionPhase.Hold && !state.ActivePlan.IsDefensive) return null;
            if (state.ActivePlan.IsDefensive && state.ActivePlan.Assignments.Any(assignment =>
                assignment.Pawn?.Spawned == true && !assignment.Pawn.Dead && !assignment.Pawn.Downed
                    && !IsDefendingRaider(assignment.Pawn))) return null;
            return state.ActivePlan;
        }

        private static RaidStructureSnapshot StructureFor(Map map, RaidTacticalPlan plan) =>
            map.GetComponent<MapComponent_RaidTacticalPlans>()
                ?.GetStructure(plan.OrganizationId);

        internal bool ControlsPawn(Pawn pawn)
            => ControlsPawn(pawn, RaidTacticalUnit.ForPawn(pawn));

        internal bool ControlsPawn(Pawn pawn, RaidTacticalUnit unit)
        {
            string id = unit?.Id;
            return id != null && IsTacticalRaider(pawn)
                && (waitingStructures.Contains(id)
                || map.GetComponent<MapComponent_RaidTacticalPlans>()?.DecisionQueuedFor(id) == true
                || states.TryGetValue(id, out ExecutionState state)
                && OwnsAssignment(id, state, pawn));
        }

        internal static bool OwnsAssignment(string unitId, ExecutionState state, Pawn pawn) =>
            state?.UnitId == unitId && state?.ActivePlan?.UnitId == unitId
            && state?.ActivePlan?.Success == true
            && state.Indices.Assignment(state.ActivePlan, pawn) != null;

        internal string SupportStatusFor(string id) => states.TryGetValue(id, out ExecutionState state)
            ? state.SupportStatus : "Inactive";

        internal ExecutionState StateFor(string id) => id != null && states.TryGetValue(id,
            out ExecutionState state) ? state : null;

        internal void NotifySupportLaunched(Pawn pawn, Projectile projectile)
        {
            string id = RaidTacticalUnit.ForPawn(pawn)?.Id;
            if (id == null || !states.TryGetValue(id, out ExecutionState state)) return;
            if (state.ApproachSmokeActive && state.ApproachSmokeThrower == pawn)
            {
                state.ApproachSmokeLaunched = true;
                state.ApproachSmokeProjectile = projectile;
                MapComponent_RaidTacticalTrace.Record(pawn, "Approach smoke launched");
                return;
            }
            if ((state.Phase != RaidExecutionPhase.Support
                    && state.Phase != RaidExecutionPhase.EntryWait)
                || state.Thrower != pawn) return;
            state.SupportLaunched = true;
            state.SupportProjectile = projectile;
            state.SupportProjectileDef = projectile.def;
            state.SupportEffectsClearedTick = -1;
            state.SupportReturnRequired = state.ActivePlan.BreachCell.IsValid;
            ReturnSupportThrower(state);
            state.SupportStatus = "Projectile launched; waiting for effect";
            MapComponent_RaidTacticalTrace.Record(pawn, state.SupportStatus);
        }

        private bool ReturnSupportThrower(ExecutionState state)
        {
            if (!state.SupportReturnRequired) return false;
            Pawn pawn = state.Thrower;
            RaidTacticalAssignment assignment = state.ActivePlan.Assignments
                .FirstOrDefault(value => value.Pawn == pawn);
            if (pawn?.Spawned != true || pawn.Map != map || pawn.Dead || pawn.Downed
                || assignment == null)
            {
                state.SupportReturnRequired = false;
                return false;
            }
            if (!AtStagingPosition(assignment, state.ActivePlan))
            {
                TryGoto(pawn, assignment.Position, true);
                return true;
            }
            HoldPosition(pawn);
            state.SupportReturnRequired = false;
            return false;
        }

        internal bool TrySupportFleeDestination(Pawn pawn, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            string id = RaidTacticalUnit.ForPawn(pawn)?.Id;
            if (id == null || !states.TryGetValue(id, out ExecutionState state)
                || state.Phase != RaidExecutionPhase.Support && state.Phase != RaidExecutionPhase.EntryWait
                || state.SupportProjectile == null || pawn.mindState.knownExploder != state.SupportProjectile
                || !state.ActivePlan.BreachCell.IsValid) return false;
            RaidTacticalPlan plan = state.ActivePlan;
            RaidTacticalAssignment assignment = plan.Assignments.FirstOrDefault(value => value.Pawn == pawn);
            if (assignment == null) return false;
            Thing danger = pawn.mindState.knownExploder;
            IEnumerable<IntVec3> candidates = new[] { assignment.Position }
                .Concat(plan.SafeStackCells).Concat(plan.SafeSupportCells).Distinct();
            cell = candidates.Where(candidate => candidate.InBounds(map) && candidate.Standable(map)
                    && candidate != plan.BreachCell && candidate != plan.BreachInside
                    && (candidate.DistanceTo(danger.Position) > JobGiver_FleePotentialExplosion.FleeDist
                        || !GenSight.LineOfSight(danger.Position, candidate, map, true))
                    && map.pawnDestinationReservationManager.CanReserve(candidate, pawn))
                .OrderBy(candidate => candidate == assignment.Position ? 0 : 1)
                .ThenBy(candidate => candidate.DistanceToSquared(pawn.Position))
                .Where(candidate => pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly))
                .DefaultIfEmpty(IntVec3.Invalid).First();
            return cell.IsValid;
        }

        private bool SupportEffectsPending(ExecutionState state, int tick)
        {
            bool preparing = RaidGrenadePreparation.IsThrowJob(state.Thrower);
            bool live = state.SupportProjectile?.Spawned == true;
            // Projectile_Explosive destroys the projectile before starting an
            // Explosion. Its damage reaches cells on later ticks, so despawn
            // alone cannot release the squad.
            bool exploding = state.SupportLaunched && !live && !preparing
                && map.listerThings.ThingsOfDef(ThingDefOf.Explosion)
                .OfType<Explosion>().Any(explosion => explosion.Spawned
                    && explosion.instigator == state.Thrower
                    && explosion.projectile == state.SupportProjectileDef);
            if (live || preparing || exploding) state.SupportEffectsClearedTick = -1;
            else if (state.SupportLaunched && state.SupportEffectsClearedTick < 0)
                state.SupportEffectsClearedTick = tick;
            bool settled = !state.SupportLaunched
                || state.SupportEffectsClearedTick >= 0 && tick - state.SupportEffectsClearedTick >= 30;
            bool pending = RaidOrderPolicy.SupportPending(live, preparing, exploding, settled);
            if (pending) state.SupportStatus = preparing ? "Throw preparation; entry blocked"
                : live ? "Waiting for grenade detonation"
                : exploding ? "Waiting for explosion damage to finish" : "Effect settling";
            else if (state.SupportLaunched) state.SupportStatus = "Support effect complete";
            return pending;
        }

        internal static bool SafeSupportThrow(Pawn pawn, Thing grenade, IntVec3 target, bool close)
            => SafeSupportThrowFrom(pawn, grenade, pawn.Position, target, close);

        internal static bool SafeSupportThrowFrom(Pawn pawn, Thing grenade, IntVec3 position, IntVec3 target, bool close)
        {
            ThingDef projectile = grenade?.def?.projectileWhenLoaded;
            if (projectile?.projectile == null) return false;
            if (RaidSmokeUtility.IsSmoke(grenade)) return true;
            float scatter = InventoryGrenadeUtility.ThrowMissRadius(pawn, close,
                position.DistanceTo(target));
            FragmentationGrenadeExtension fragments = projectile.GetModExtension<FragmentationGrenadeExtension>();
            float fragmentRadius = fragments == null ? 0f : Math.Max(fragments.radius,
                fragments.longRangeFragmentFraction > 0f ? fragments.longRangeRadius : 0f);
            float radius = Math.Max(projectile.projectile.explosionRadius, fragmentRadius)
                + scatter + 0.75f;
            return !pawn.Map.mapPawns.AllPawnsSpawned.Any(ally => {
                IntVec3 cell = ally == pawn ? position : ally.Position;
                return !ally.Dead && !ally.HostileTo(pawn) && cell.DistanceTo(target) <= radius
                    && (cell.DistanceTo(target) <= scatter + 0.75f || GenSight.LineOfSight(target, cell, pawn.Map, true));
            });
        }

        private void ExecuteUnit(RaidTacticalUnit unit, List<Pawn> members, int tick, bool fallbackDue)
        {
            MapComponent_RaidTacticalPlans plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            if (members.Count == 0)
            {
                if (states.TryGetValue(unit.Id, out ExecutionState empty))
                {
                    CancelPendingCharge(empty);
                    states.Remove(unit.Id);
                }
                if (fallbackDue) KeepSapperEscortTogether(unit);
                return;
            }
            if (!plans.StructureReadyFor(unit.OrganizationId))
            {
                waitingStructures.Add(unit.Id);
                foreach (Pawn member in members)
                    MapComponent_RaidTacticalOrders.Set(member, RaidOrderKind.Hold, member.Position);
                return;
            }
            waitingStructures.Remove(unit.Id);
            foreach (Pawn fallen in unit.Members.Where(pawn => pawn != null
                && (pawn.Downed || pawn.Dead) && pawn.MapHeld == map))
                RememberBreachTools(unit.Id, fallen);
            if (!IsDefendingRaider(members[0]))
                RaidBreachToolRecovery.TryStart(members, BreachToolsFor(unit.Id), map);
            if (states.TryGetValue(unit.Id, out ExecutionState completed)
                && TryExitSecuredObjective(unit, members, completed, tick))
            {
                states.Remove(unit.Id);
                return;
            }
            states.TryGetValue(unit.Id, out ExecutionState state);
            if (state != null)
                ReconcileCasualties(unit, members, state, tick);
            string key = PlanKey(unit, members);
            if (state?.ActivePlan?.Success == true && state.Phase != RaidExecutionPhase.Hold
                && state.Phase != RaidExecutionPhase.Complete)
            {
                AssignLateMembers(members, state.ActivePlan);
                ReplaceLostEntryTeam(state.ActivePlan, state);
            }
            // Losing a member or changing commander must not recall pawns
            // already breaching or crossing to a newly assigned stack.
            if (state?.ActivePlan?.Success == true
                && state.Phase != RaidExecutionPhase.Hold
                && state.Phase != RaidExecutionPhase.Complete
                && members.All(pawn => state.ActivePlan.Assignments
                    .Any(assignment => assignment.Pawn == pawn)))
                state.PlanKey = key;
            RaidTacticalPlan plan = state?.ClearingRooms == true
                ? state.ActivePlan != null && state.PlanKey == key
                    ? state.ActivePlan
                    : map.GetComponent<MapComponent_RaidPlanningService>().Request(unit, "roster", state.Objective, state.ActivePlan)
                : state?.ActivePlan?.Success == true && state.PlanKey == key
                    && state.Phase != RaidExecutionPhase.Hold
                    && state.Phase != RaidExecutionPhase.Complete
                    ? state.ActivePlan : plans.GetPlan(unit);
            if (plan?.Success != true)
            {
                if (plans.DecisionQueuedFor(unit.Id))
                {
                    if (state?.ActivePlan?.Success != true)
                        foreach (Pawn member in members)
                            MapComponent_RaidTacticalOrders.Set(member, RaidOrderKind.Hold, member.Position);
                    return;
                }
                if (fallbackDue) KeepSapperEscortTogether(unit);
                if (states.TryGetValue(unit.Id, out ExecutionState abandoned))
                {
                    CancelPendingCharge(abandoned);
                    states.Remove(unit.Id);
                }
                return;
            }
            bool idle = state != null && (state.Phase == RaidExecutionPhase.Hold
                || state.Phase == RaidExecutionPhase.Complete);
            bool changedIdlePlan = idle && !ReferenceEquals(state.ActivePlan, plan)
                && (state.ActivePlan.IsDefensive != plan.IsDefensive
                    || state.Maneuver != plan.Selected.Maneuver
                    || state.Objective.DistanceTo(plan.Objective) > 3f);
            if (state == null || state.PlanKey != key
                || idle && state.Objective.DistanceTo(plan.Objective) > 8f
                || changedIdlePlan)
            {
                ExecutionState previous = state;
                if (previous != null) CancelPendingCharge(previous);
                state = new ExecutionState
                {
                    OrganizationId = unit.OrganizationId,
                    UnitId = unit.Id,
                    GroupId = unit.GroupId,
                    PlanKey = key,
                    Objective = plan.Objective,
                    FinalObjective = previous?.FinalObjective.IsValid == true
                        ? previous.FinalObjective : plan.FinalObjective,
                    ClearingRooms = previous?.ClearingRooms == true,
                    BedSecured = previous?.BedSecured == true,
                    ClearedRoomCells = previous?.ClearedRoomCells
                        ?? new List<IntVec3>(),
                    Contacts = previous?.Contacts ?? new RaidContactMemory(),
                    Communication = previous?.Communication ?? new RaidCommunicationState(),
                    CqbKnowledge = previous?.CqbKnowledge ?? new RaidCqbKnowledge(),
                    ContactGuards = previous?.ContactGuards ?? new List<RaidContactGuard>(),
                    RoomSecurity = previous?.RoomSecurity ?? new RaidRoomSecurity(),
                    LocalCqb = previous?.LocalCqb,
                    Maneuver = plan.Selected.Maneuver,
                    ActivePlan = plan,
                    Phase = RaidExecutionPhase.Assemble,
                    PhaseStarted = tick,
                    ApproachProgressTick = tick
                };
                if (previous != null
                    && previous.Objective.DistanceTo(plan.Objective) <= 8f)
                    state.ExternalSupportAttempted = previous.ExternalSupportAttempted;
                if (previous != null
                    && previous.ExternalSupportKind != RaidExternalSupportKind.None)
                {
                    state.ExternalSupportAttempted = true;
                    state.ExternalSupportKind = previous.ExternalSupportKind;
                    state.ExternalSupportCaller = previous.ExternalSupportCaller;
                    state.ExternalSupportTarget = previous.ExternalSupportTarget;
                    state.ExternalSupportTargetCell = previous.ExternalSupportTargetCell;
                    state.ExternalSupportClearedTick = previous.ExternalSupportClearedTick;
                }
                if (previous != null)
                {
                    state.ExteriorIngress = previous.ExteriorIngress;
                    if (ReferenceEquals(previous.ActivePlan, plan))
                    {
                        state.CurrentNode = previous.CurrentNode;
                        state.NodeMembers = previous.NodeMembers;
                        state.ApproachComplete = previous.ApproachComplete;
                    }
                    state.Reactions = previous.Reactions;
                    state.DefenseUntil = previous.DefenseUntil;
                    state.DefenseCaller = previous.DefenseCaller;
                    state.DefenseAim = previous.DefenseAim;
                    state.ApproachSmokeActive = previous.ApproachSmokeActive;
                    state.SmokeFormation = previous.SmokeFormation;
                    state.ApproachSmokeLaunched = previous.ApproachSmokeLaunched;
                    state.ApproachSmokeThrower = previous.ApproachSmokeThrower;
                    state.ApproachSmokeProjectile = previous.ApproachSmokeProjectile;
                    state.ApproachSmokeTarget = previous.ApproachSmokeTarget;
                    state.ApproachSmokeThreat = previous.ApproachSmokeThreat;
                    state.ApproachSmokeStarted = previous.ApproachSmokeStarted;
                    state.ApproachSmokeClearedTick = previous.ApproachSmokeClearedTick;
                    state.NextApproachSmokeTick = previous.NextApproachSmokeTick;
                    state.ScreenAdvanceUntil = previous.ScreenAdvanceUntil;
                }
                states[unit.Id] = state;
                RaidTacticalSpeech.Say(Commander(unit, members),
                    "HD_RaidTactical_Assemble");
            }
            if (state.ActivePlan == null) state.ActivePlan = plan;
            else if (idle && !ReferenceEquals(state.ActivePlan, plan))
            {
                state.ActivePlan = plan;
                state.Objective = plan.Objective;
                state.PendingRoomGoal = state.PendingRecheckGoal = IntVec3.Invalid;
                state.ReadySince = -1;
            }
            Update(unit, members, state.ActivePlan, state, tick);
            if (state.Phase == RaidExecutionPhase.Assemble && !state.ApproachComplete
                && !state.ApproachSmokeActive && state.DefenseUntil <= tick && !state.ContactPause
                && tick - Math.Max(state.ApproachProgressTick, state.PhaseStarted)
                    >= ApproachStallTimeout)
            {
                if (state.ActivePlan.MovementNodes.Count > 0)
                {
                    RaidTacticalPlan repaired = map.GetComponent<MapComponent_RaidPlanningService>()
                        .Request(unit, "repair", state.Objective, state.ActivePlan);
                    if (repaired == null) return;
                    if (repaired?.Success == true)
                    {
                        MapComponent_RaidTacticalTrace.Record(Commander(unit, members),
                            $"Node {state.CurrentNode} stalled: repair approach from current positions");
                        ActivateNextRoomPlan(unit, members, state, repaired, tick);
                    }
                    state.ApproachProgressTick = tick;
                }
                else if (state.ActivePlan.SafeStackCells.Count > 0)
                {
                    RetargetBlockedStackMembers(members, state.ActivePlan);
                    state.ApproachProgressTick = tick;
                    state.ApproachBestRemaining = float.MaxValue;
                }
                else
                {
                    plans.GetPlan(unit, true);
                    states.Remove(unit.Id);
                }
            }
            else if (state.Phase == RaidExecutionPhase.Assemble
                && state.ApproachComplete && !AllReady(members, state.ActivePlan)
                && tick - state.PhaseStarted >= AssembleTimeout)
            {
                if (state.ActivePlan.SafeStackCells.Count > 0)
                {
                    RetargetBlockedStackMembers(members, state.ActivePlan);
                    state.PhaseStarted = tick;
                }
                else
                {
                    plans.GetPlan(unit, true);
                    states.Remove(unit.Id);
                }
            }
        }

        private static void CancelPendingCharge(ExecutionState state)
        {
            if (state?.BreachKind != RaidBreachKind.C4
                || state.Phase == RaidExecutionPhase.Detonation) return;
            Pawn breacher = state.Breacher;
            if (breacher?.CurJobDef?.defName
                == BreachExplosiveUtility.ShockTubeJobDefName)
                breacher.jobs.EndCurrentJob(JobCondition.InterruptForced);
            CompInstalledBreachCharge charge = BreachExplosiveUtility
                .ChargeOnWall(state.BreachTarget);
            if (charge?.OperatorPawn == breacher)
                charge.parent.Destroy(DestroyMode.Vanish);
        }

        internal static bool IsAssaultRaider(Pawn pawn)
        {
            Lord lord = pawn.GetLord();
            return pawn.Faction != Faction.OfPlayer
                && (lord?.LordJob is LordJob_AssaultColony
                    || lord?.LordJob is LordJob_StageThenAttack
                    || lord?.LordJob is LordJob_SleepThenAssaultColony
                    || lord?.LordJob?.GetType().Name.StartsWith("LordJob_AssaultColony",
                        StringComparison.Ordinal) == true);
        }

        internal static bool IsDefendingRaider(Pawn pawn) => pawn.Faction != Faction.OfPlayer
            && pawn.GetLord() != null && (pawn.mindState?.duty?.def == DutyDefOf.Defend
                || pawn.mindState?.duty?.def == DutyDefOf.DefendBase);

        internal static bool IsTacticalRaider(Pawn pawn) => IsAssaultRaider(pawn) || IsDefendingRaider(pawn);

        private static void AssignLateMembers(List<Pawn> members, RaidTacticalPlan plan)
        {
            foreach (Pawn pawn in members.Where(member => !plan.Assignments.Any(assignment => assignment.Pawn == member)))
            {
                var occupied = new HashSet<IntVec3>(plan.Assignments.Select(assignment => assignment.Position));
                IntVec3 cell = plan.SafeStackCells.Concat(plan.SafeSupportCells)
                    .Where(candidate => candidate.InBounds(pawn.Map) && candidate.Standable(pawn.Map)
                        && !occupied.Contains(candidate) && candidate != plan.Entry
                        && candidate != plan.BreachInside)
                    .OrderBy(candidate => candidate.DistanceToSquared(pawn.Position))
                    .Take(32).Where(candidate => pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(pawn.Position).First();
                // Join as security until the next room/mission plan. Never recall
                // existing entrants or renumber a crossing already in progress.
                plan.Assignments.Add(new RaidTacticalAssignment { Pawn = pawn,
                    GroupId = OrganizationAPI.GetGroup(pawn)?.id,
                    Task = RaidTacticalTask.Security, Position = cell });
                MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Move, cell);
                MapComponent_RaidTacticalTrace.Record(pawn, $"Late member joins security at {cell}");
            }
        }

        private void KeepSapperEscortTogether(RaidTacticalUnit unit)
        {
            if (unit.Faction == Faction.OfPlayer) return;
            List<Pawn> members = unit.Members.Where(pawn => pawn.Spawned
                && pawn.Map == map && !pawn.Dead && !pawn.Downed).ToList();
            if (members.Count < 4 || !(members[0].GetLord()?.CurLordToil
                is LordToil_AssaultColonySappers toil)) return;
            Pawn sapper = members.FirstOrDefault(pawn =>
                pawn.mindState?.duty?.def?.defName == "Sapper");
            if (sapper == null) return;
            TryAssistSapperWithSledgehammer(members, sapper);
            IntVec3 destination = (toil.data as LordToilData_AssaultColonySappers)
                ?.sapperDest ?? IntVec3.Invalid;
            if (!destination.IsValid || destination == sapper.Position) return;

            CombatGroup group = unit.Groups
                .Where(value => value.Members.Contains(sapper))
                .OrderBy(value => value.Members.Count()).FirstOrDefault();
            List<Pawn> escorts = (group?.Members ?? members)
                .Where(pawn => pawn != sapper && members.Contains(pawn))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(sapper.Position))
                .Take(3).ToList();
            escorts.AddRange(members.Where(pawn => pawn != sapper
                    && !escorts.Contains(pawn))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(sapper.Position))
                .Take(3 - escorts.Count));

            int dx = destination.x - sapper.Position.x;
            int dz = destination.z - sapper.Position.z;
            IntVec3 forward = Math.Abs(dx) >= Math.Abs(dz)
                ? (dx >= 0 ? IntVec3.East : IntVec3.West)
                : (dz >= 0 ? IntVec3.North : IntVec3.South);
            IntVec3 side = new IntVec3(-forward.z, 0, forward.x);
            IntVec3[] offsets = { forward * 2 + side * 2,
                forward * 2 - side * 2, -forward * 2 };
            var occupied = new HashSet<IntVec3> { sapper.Position };
            for (int i = 0; i < escorts.Count; i++)
            {
                Pawn escort = escorts[i];
                if (IsTaserOperation(escort)
                    || escort.CurJobDef?.defName == CompSledgehammerBreach.JobDefName
                    || escort.CurJobDef?.defName == RaidBreachToolRecovery.JobName)
                    continue;
                if (map.mapPawns.AllPawnsSpawned.Any(enemy => !enemy.Dead
                    && enemy.Faction != null && enemy.Faction.HostileTo(escort.Faction)
                    && enemy.Position.DistanceTo(escort.Position) <= 12f
                    && GenSight.LineOfSight(escort.Position, enemy.Position, map, true)))
                    continue;
                IntVec3 ideal = sapper.Position + offsets[i];
                IntVec3 cell = GenRadial.RadialCellsAround(ideal, 2.9f, true)
                    .Where(value => value.InBounds(map) && value.Standable(map)
                        && !occupied.Contains(value)
                        && value.DistanceTo(sapper.Position) <= 5f
                        && value.GetRoom(map) == sapper.Position.GetRoom(map))
                    .OrderBy(value => value.DistanceToSquared(ideal))
                    .Where(value => escort.CanReach(value,
                        PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!cell.IsValid) continue;
                occupied.Add(cell);
                if (escort.Position.DistanceTo(cell) > 1.5f)
                    TryGoto(escort, cell);
            }
        }

        private static void TryAssistSapperWithSledgehammer(List<Pawn> members,
            Pawn sapper)
        {
            Building target = sapper.CurJob?.targetA.Thing as Building;
            if (target == null && sapper.CurJob?.targetA.Cell.IsValid == true)
                target = sapper.CurJob.targetA.Cell.GetEdifice(sapper.Map) as Building;
            if (target == null || !target.def.IsWall
                || !target.Spawned || target.Destroyed) return;
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                CompSledgehammerBreach.JobDefName);
            if (jobDef == null) return;
            foreach (Pawn pawn in members
                .Where(pawn => pawn.Position.DistanceTo(sapper.Position) <= 12f)
                .OrderBy(pawn => pawn == sapper ? 1 : 0)
                .ThenBy(pawn => pawn.Position.DistanceToSquared(target.Position)))
            {
                CompSledgehammerBreach tool = CompSledgehammerBreach.WornBy(pawn);
                if (tool == null || IsTaserOperation(pawn)
                    || pawn.CurJobDef?.defName == CompSledgehammerBreach.JobDefName
                    || !CompSledgehammerBreach.IsValidTarget(pawn, target)
                    || !CompSledgehammerBreach.TryFindInteractionCell(pawn,
                        target, out IntVec3 cell)
                    || !pawn.CanReserve(target, 1, -1, null, false)) continue;
                pawn.jobs.StartJob(JobMaker.MakeJob(jobDef, target, cell,
                    tool.parent), JobCondition.InterruptForced);
                return;
            }
        }

        private static string PlanKey(RaidTacticalUnit unit, List<Pawn> members)
        {
            return string.Join(",", members.Select(pawn => pawn.thingIDNumber).OrderBy(id => id))
                + ":" + string.Join(",", unit.Groups.Select(group =>
                    (group.EffectiveCommander?.thingIDNumber ?? -1) + ":"
                    + (int)(group.CommandEfficiency * 100f)));
        }

        private static Pawn Commander(RaidTacticalUnit unit, List<Pawn> members)
        {
            Pawn commander = unit.Commander;
            return commander != null && members.Contains(commander) ? commander : members[0];
        }

        private bool TryExitSecuredObjective(RaidTacticalUnit unit,
            List<Pawn> members, ExecutionState state, int tick)
        {
            if (state.ActivePlan == null || !state.ActivePlan.ObjectiveIsObservedEnemy
                || tick - state.PhaseStarted < 600
                || (state.Phase != RaidExecutionPhase.Complete
                    && state.Phase != RaidExecutionPhase.SecureRoom)) return false;

            IntVec3 objective = state.Objective;
            if (!objective.IsValid || !objective.InBounds(map)) return false;
            RaidStructureSnapshot structure = StructureFor(map, state.ActivePlan);
            if (structure == null) return false;
            int room = structure.RoomAt(objective);
            bool indoors = room > 0;
            if (indoors != (state.Phase == RaidExecutionPhase.SecureRoom)) return false;

            List<Pawn> entry = state.ActivePlan.Assignments
                .Where(assignment => assignment.Task == RaidTacticalTask.Entry
                    && members.Contains(assignment.Pawn))
                .Select(assignment => assignment.Pawn).Distinct().ToList();
            if (entry.Count == 0) return false;
            int atObjective = entry.Count(pawn => indoors
                ? structure.RoomAt(pawn.Position) == room
                : pawn.Position.DistanceTo(objective) <= 9f);
            if (atObjective * 2 < entry.Count) return false;

            float dangerRadius = indoors ? 12f : 18f;
            RefreshContacts(members, state.ActivePlan, state, tick);
            if (state.Contacts.Entries.Any(contact => contact.Confidence(tick) <= RaidContactConfidence.Area
                && (contact.Position.DistanceTo(objective) <= dangerRadius || indoors && ContactRooms(structure, contact.Position).Contains(room)))) return false;
            if (indoors && state.RoomSecurity.For(room)?.RecentConcern(tick) == true) return false;

            Lord lord = members[0].GetLord();
            if (lord == null || members.Any(pawn => pawn.GetLord() != lord)) return false;
            RaidTacticalSpeech.Say(Commander(unit, members),
                "HD_RaidTactical_Withdraw");
            var exit = new LordJob_ExitMapBest(LocomotionUrgency.Sprint, true, true);
            if (lord.ownedPawns.Any(pawn => !members.Contains(pawn)))
            {
                // Squads can share the vanilla raid Lord. Completing one unit's
                // objective must not change the mission of all its siblings.
                lord.RemovePawns(members);
                LordMaker.MakeNewLord(unit.Faction, exit, map, members);
            }
            else lord.SetJob(exit);
            return true;
        }

        private void Update(RaidTacticalUnit unit, List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (plan.UnitId != unit.Id || state.UnitId != unit.Id) return;
            plan.Assignments.RemoveAll(assignment => !members.Contains(assignment.Pawn));
            RefreshContacts(members, plan, state, tick);
            if (EmergencyReactions(members, plan, state, tick)
                || RoutineContactReactions(members, plan, state, tick)
                || FieldDefense(members, plan, state, tick))
            {
                state.ApproachProgressTick = tick;
                return;
            }
            if (WaitForSharedOpening(members, plan, state, tick)) return;
            if (state.Phase == RaidExecutionPhase.Complete && state.ClearingRooms
                && StructureFor(map, plan)?.IsIndoor(plan.Objective) == true
                && state.RoomSecurity.Rooms.Any(room => room.RecentConcern(tick)))
                Advance(state, RaidExecutionPhase.SecureRoom, tick);
            if (RecoverCqbIntent(unit, members, plan, state, tick)) return;
            if (RefreshLocalCqb(unit, members, plan, state, tick)) return;
            // Completed phases may hand over immediately; movement, gathering,
            // and live explosive waits still block on their actual conditions.
            for (int transitions = 0; transitions < 4; transitions++)
            {
                RaidExecutionPhase before = state.Phase;
                UpdateStep(unit, members, plan, state, tick);
                if (state.Phase == before || state.ActivePlan != plan) break;
            }
        }

        private void UpdateStep(RaidTacticalUnit unit, List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (plan.CqbIntent == RaidCqbIntent.ClearCurrentRoom && state.Phase == RaidExecutionPhase.Assemble)
            {
                Advance(state, RaidExecutionPhase.Assault, tick);
                return;
            }
            if (plan.Doctrine == RaidTacticalDoctrine.High
                && (state.Phase == RaidExecutionPhase.Assemble
                    || state.Phase == RaidExecutionPhase.EntryWait
                    || state.Phase == RaidExecutionPhase.ClearRoom
                    || state.Phase == RaidExecutionPhase.SecureRoom
                    || state.Phase == RaidExecutionPhase.Hold
                    || state.Phase == RaidExecutionPhase.Complete))
                TryCounterPsychicLance(members);
            switch (state.Phase)
            {
                case RaidExecutionPhase.Assemble:
                    if (!state.ExternalSupportAttempted)
                    {
                        state.ExternalSupportAttempted = true;
                        TryRequestExternalSupport(unit, members, plan, state);
                    }
                    if (FieldDefense(members, plan, state, tick)
                        || ApproachScreen(members, plan, state, tick))
                    {
                        state.ApproachProgressTick = tick;
                        break;
                    }
                    if (!FollowNodes(members, plan, state, tick)) break;
                    Assemble(members, plan);
                    bool ready = AllReady(members, plan);
                    if (ready && state.ReadySince < 0) state.ReadySince = tick;
                    if (!ready) state.ReadySince = -1;
                    if (!UsingZaper(members)
                        && ready && state.ReadySince >= 0
                        && tick - state.ReadySince >= plan.CoordinationDelayTicks)
                    {
                        RaidExecutionPhase next = state.Maneuver == RaidTacticalManeuver.HoldAndCounterattack
                            || state.Maneuver == RaidTacticalManeuver.Regroup
                            ? RaidExecutionPhase.Hold
                            : RaidExecutionPhase.Breach;
                        Advance(state, next, tick);
                    }
                    break;
                case RaidExecutionPhase.Breach:
                    MaintainStack(members, plan, state.Breacher);
                    if (RaidBreachToolRecovery.Pending(members) && !BreachOpened(plan)
                        && (state.Breacher == null || state.Breacher.Dead || state.Breacher.Downed))
                    {
                        state.Breacher = null;
                        state.BreachTarget = null;
                        state.BreachKind = RaidBreachKind.None;
                        state.PhaseStarted = tick;
                        break;
                    }
                    // Demolition can complete independently of the tool job.
                    // Do not leave an already open wall waiting on its old driver.
                    if (state.BreachKind != RaidBreachKind.C4
                        && (plan.BreachCell.IsValid || plan.PlannedBreach != null
                            || state.BreachTarget != null)
                        && BreachOpened(plan))
                    {
                        FinishBreachAttempt(plan, state, tick);
                        break;
                    }
                    if (state.Breacher == null && state.BreachTarget == null)
                    {
                        if (plan.PlannedBreach != null && BreachOpened(plan))
                            Advance(state, RaidExecutionPhase.ObserveOpening, tick);
                        else if (!TryStartBreach(members, plan, state))
                        {
                            if (plan.PlannedBreach == null || BreachOpened(plan))
                                Advance(state, RaidExecutionPhase.ObserveOpening, tick);
                            else if (tick - state.PhaseStarted >= BreachTimeout)
                            {
                                state.PhaseStarted = tick;
                                if (++state.BreachAttempts >= 3)
                                    Advance(state, RaidExecutionPhase.Hold, tick);
                            }
                        }
                    }
                    else if (state.BreachKind == RaidBreachKind.C4
                        && BreachExplosiveUtility.ChargeOnWall(state.BreachTarget)
                            ?.OperatorPawn == state.Breacher)
                    {
                        CancelOpeningObservation(state);
                        state.Observation = null;
                        Advance(state, RaidExecutionPhase.WithdrawFromCharge, tick);
                    }
                    else if (state.BreachKind == RaidBreachKind.C4
                        && (state.Breacher?.CurJobDef?.defName
                            != BreachExplosiveUtility.ShockTubeJobDefName
                            || tick - state.PhaseStarted >= BreachTimeout))
                        FinishBreachAttempt(plan, state, tick);
                    else if (state.BreachKind == RaidBreachKind.PowerCutter
                        && (BreachOpened(state.BreachTarget)
                            || state.Breacher?.CurJobDef?.defName != "HD_PowerCutterBreach"))
                        FinishBreachAttempt(plan, state, tick);
                    else if (state.BreachKind == RaidBreachKind.Sledgehammer
                        && (state.BreachTarget == null || state.BreachTarget.Destroyed
                            || !state.BreachTarget.Spawned
                            || state.BreachTarget is Building_Door openedDoor && openedDoor.Open
                            || state.Breacher?.CurJobDef?.defName
                                != CompSledgehammerBreach.JobDefName))
                        FinishBreachAttempt(plan, state, tick);
                    if (state.Phase == RaidExecutionPhase.Breach)
                        EnsureOpeningObserver(members, plan, state, tick);
                    break;
                case RaidExecutionPhase.WithdrawFromCharge:
                    CompInstalledBreachCharge charge = BreachExplosiveUtility
                        .ChargeOnWall(state.BreachTarget);
                    if (charge == null || charge.OperatorPawn != state.Breacher)
                    {
                        FinishBreachAttempt(plan, state, tick);
                        break;
                    }
                    float safeRadius = Math.Max(9f,
                        charge.Props.beyondFragmentRadius + 2f);
                    if (!state.WithdrawalIssued)
                    {
                        state.WithdrawalIssued = true;
                        IssueChargeWithdrawal(members, plan, state.BreachTarget.Position, safeRadius);
                    }
                    if (map.mapPawns.AllPawnsSpawned
                            .Where(pawn => !pawn.Dead && !pawn.HostileTo(state.Breacher))
                            .All(pawn => pawn.Position.DistanceTo(state.BreachTarget.Position)
                                >= safeRadius)
                        && charge.CanTrigger(out _))
                    {
                        RaidTacticalSpeech.Say(state.Breacher,
                            "HD_RaidTactical_Detonate");
                        charge.Trigger();
                        Advance(state, RaidExecutionPhase.Detonation, tick);
                    }
                    else if (tick - state.PhaseStarted >= WithdrawalTimeout)
                    {
                        // A stranded squad must never inherit a live automated charge.
                        charge.parent.Destroy(DestroyMode.Vanish);
                        FinishBreachAttempt(plan, state, tick);
                    }
                    break;
                case RaidExecutionPhase.Detonation:
                    if (state.BreachTarget == null || state.BreachTarget.Destroyed
                        || !state.BreachTarget.Spawned
                        || tick - state.PhaseStarted >= DetonationTimeout)
                        FinishBreachAttempt(plan, state, tick);
                    break;
                case RaidExecutionPhase.ObserveOpening:
                    UpdateOpeningObservation(members, plan, state, tick);
                    break;
                case RaidExecutionPhase.Support:
                    MaintainStack(members, plan, state.Thrower);
                    if (state.SupportLaunched) ReturnSupportThrower(state);
                    if (plan.BreachCell.IsValid && state.Thrower == null
                        && !AllReady(members, plan)
                        && tick - state.PhaseStarted < 600) break;
                    if (!state.SupportIssued)
                    {
                        Pawn thrower = TryStartSupport(members, plan,
                            state.Maneuver, state.Thrower, state.Observation);
                        if (thrower != null)
                        {
                            state.Thrower = thrower;
                            state.SupportIssued = true;
                            state.SupportStatus = "Throw preparation";
                        }
                        else
                        {
                            RaidStructureSnapshot supportStructure = StructureFor(map, plan);
                            int supportRoom = supportStructure?.RoomAt(plan.BreachCell.IsValid ? plan.BreachInside : plan.Objective) ?? 0;
                            state.SupportStatus = state.Maneuver == RaidTacticalManeuver.CoordinatedEntry
                                && state.Observation?.HasEnemyContact != true
                                && supportRoom > 0 && supportStructure.RoomArea(supportRoom) <= RaidEntryObservationPolicy.SmallRoomCells
                                ? "Skipped: target room has at most 16 floor cells; save grenade"
                                : "Skipped: no safe observed enemy/blind-sector throw or usable equipment";
                            state.SupportReturnRequired = plan.BreachCell.IsValid && state.Thrower != null;
                            Advance(state, RaidExecutionPhase.EntryWait, tick);
                        }
                    }
                    else if (!RaidGrenadePreparation.IsThrowJob(state.Thrower)
                        || tick - state.PhaseStarted >= SupportTimeout)
                    {
                        if (RaidGrenadePreparation.Active(state.Thrower) != null)
                            state.Thrower.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                        state.SupportReturnRequired = plan.BreachCell.IsValid && state.Thrower != null;
                        state.SupportStatus = state.SupportLaunched
                            ? "Waiting for projectile effect" : "Throw failed before launch";
                        Advance(state, RaidExecutionPhase.EntryWait, tick);
                    }
                    break;
                case RaidExecutionPhase.EntryWait:
                    bool returningThrower = ReturnSupportThrower(state);
                    if (tick % 120 == 0)
                        RetargetBlockedStackMembers(members, plan, onlyBlocked: true);
                    MaintainStack(members, plan);
                    if (!UsingZaper(members)
                        && RaidOrderPolicy.ReadyToEnter(!returningThrower
                                && (!plan.BreachCell.IsValid || AllReady(members, plan)),
                            SupportEffectsPending(state, tick),
                            tick - state.PhaseStarted >= plan.EntryDelayTicks))
                        Advance(state, plan.BreachCell.IsValid
                            ? RaidExecutionPhase.CrossBreach
                            : AfterSupport(plan, state.Maneuver), tick);
                    break;
                case RaidExecutionPhase.CrossBreach:
                    MaintainStack(members, plan, holdEntry: false);
                    if (!BreachOpened(plan))
                    {
                        state.Breacher = null;
                        state.BreachTarget = null;
                        Advance(state, RaidExecutionPhase.Breach, tick);
                        break;
                    }
                    // This unit opened the passage itself; later room plans
                    // must retain that connection for its rear security members.
                    RaidStructureSnapshot crossedStructure = StructureFor(map, plan);
                    if (crossedStructure != null && plan.BreachCell.InBounds(map))
                        state.CqbKnowledge.RememberDirect(plan.BreachCell, new RaidKnownCqbCell {
                            Building = plan.BreachCell.GetEdifice(map)?.thingIDNumber ?? 0, Portal = true, Usable = true
                        }, tick, crossedStructure.Version.Id, state.UnitId);
                    if (FollowBreachCrossing(members, plan, state))
                        Advance(state, RaidExecutionPhase.Assault, tick);
                    break;
                case RaidExecutionPhase.Flank:
                    if (!state.FlankIssued)
                    {
                        state.FlankIssued = true;
                        IssueFlank(members, plan);
                    }
                    if (EntryMembersNear(members, plan, plan.Flank, 4f)
                        || tick - state.PhaseStarted >= FlankTimeout)
                        Advance(state, RaidExecutionPhase.Assault, tick);
                    break;
                case RaidExecutionPhase.Assault:
                    if (!state.AssaultIssued)
                    {
                        state.AssaultIssued = true;
                        RaidTacticalSpeech.Say(Commander(unit, members),
                            "HD_RaidTactical_MoveIn");
                        IssueAssault(members, plan, state);
                        Advance(state, StructureFor(map, plan)?.IsIndoor(plan.Objective) == true
                            ? RaidExecutionPhase.ClearRoom : RaidExecutionPhase.Complete, tick);
                    }
                    break;
                case RaidExecutionPhase.ClearRoom:
                    if (!UsingZaper(members)
                        && (EntryMembersNear(members, plan, plan.Objective, 6f)
                            || tick - state.PhaseStarted >= 360))
                    {
                        state.DoorStateSignature = NearbyDoorState(members, plan.Objective, state.DoorStateSignature);
                        IssueRoomSecurity(members, plan);
                        state.LastRoomSecurityTick = tick;
                        Advance(state, RaidExecutionPhase.SecureRoom, tick);
                    }
                    break;
                case RaidExecutionPhase.SecureRoom:
                    if (tick - state.LastRoomSecurityTick >= 30)
                    {
                        string doors = NearbyDoorState(members, plan.Objective, state.DoorStateSignature);
                        if (doors != state.DoorStateSignature)
                        {
                            string previousDoors = state.DoorStateSignature;
                            state.DoorStateSignature = doors;
                            IssueRoomSecurity(members, plan);
                            if (tick - state.LastDoorResponseTick >= 360
                                && TryCounterClosingDoor(members, plan,
                                    previousDoors))
                                state.LastDoorResponseTick = tick;
                        }
                        state.LastRoomSecurityTick = tick;
                    }
                    if (tick - state.PhaseStarted >= 30
                        && tick - state.LastRoomPlanTick >= 30
                        && RoomSecured(unit, members, plan)
                        && (state.ClearingRooms || plan.ObjectiveIsNamedBed
                            || plan.ObjectiveIsIntermediate))
                    {
                        state.LastRoomPlanTick = tick;
                        if (!TryPlanNextRoom(unit, members, plan, state, tick))
                        {
                            if (state.BedSecured)
                                Advance(state, RaidExecutionPhase.Complete, tick);
                        }
                    }
                    break;
                case RaidExecutionPhase.Hold:
                    bool responding = CurrentResponseTarget(members, plan) != null;
                    Assemble(members, plan, responding);
                    if (responding && tick % 180 == 0)
                        IssueResponse(members, plan);
                    break;
                case RaidExecutionPhase.Complete:
                    if (tick - state.PhaseStarted >= 600 && tick % 180 == 0)
                        MaintainEntryCohesion(unit, members, plan);
                    break;
            }
        }

        private void MaintainEntryCohesion(RaidTacticalUnit unit,
            List<Pawn> members, RaidTacticalPlan plan)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            List<Pawn> entry = plan.Assignments
                .Where(assignment => assignment.Task == RaidTacticalTask.Entry
                    && members.Contains(assignment.Pawn))
                .Select(assignment => assignment.Pawn).ToList();
            foreach (Pawn pawn in entry)
            {
                if (IsTaserOperation(pawn)) continue;
                CombatGroup group = unit.Groups
                    .Where(value => value.Members.Contains(pawn)
                        && value.Members.Count(entry.Contains) > 1)
                    .OrderBy(value => value.Members.Count()).FirstOrDefault();
                Pawn buddy = (group?.Members ?? entry)
                    .Where(other => other != pawn && entry.Contains(other)
                        && (structure == null || structure.RoomAt(other.Position) == structure.RoomAt(pawn.Position))
                        && GenSight.LineOfSight(pawn.Position, other.Position, map, true))
                    .OrderBy(other => other.Position.DistanceToSquared(pawn.Position))
                    .FirstOrDefault();
                if (buddy == null || pawn.Position.DistanceTo(buddy.Position) <= 12f
                    || (pawn.CurJobDef == JobDefOf.Goto
                        && pawn.Position.DistanceTo(plan.Objective) > 8f)
                    || map.mapPawns.AllPawnsSpawned.Any(enemy => !enemy.Dead
                        && enemy.Faction != null && enemy.Faction.HostileTo(pawn.Faction)
                        && enemy.Position.DistanceTo(pawn.Position) <= 12f
                        && GenSight.LineOfSight(pawn.Position, enemy.Position, map, true)))
                    continue;
                IntVec3 cell = GenRadial.RadialCellsAround(buddy.Position, 4f, true)
                    .Where(value => value.InBounds(map) && value.Standable(map)
                        && value != buddy.Position
                        && (structure == null || structure.RoomAt(value) == structure.RoomAt(pawn.Position))
                        && !plan.AvoidedTrapCells.Contains(value))
                    .OrderBy(value => value.DistanceToSquared(pawn.Position))
                    .Where(value => pawn.CanReach(value,
                        PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (cell.IsValid) TryGoto(pawn, cell);
            }
        }

        private static void Advance(ExecutionState state, RaidExecutionPhase next, int tick)
        {
            state.Phase = next;
            state.PhaseStarted = tick;
        }

        private static bool BreachOpened(Building target)
        {
            return target == null || target.Destroyed || !target.Spawned
                || target is Building_Door door && door.Open;
        }

        private bool BreachOpened(RaidTacticalPlan plan)
        {
            if (!plan.BreachCell.IsValid) return BreachOpened(plan.PlannedBreach);
            if (!plan.BreachCell.InBounds(map)) return false;
            Building current = plan.BreachCell.GetEdifice(map) as Building;
            bool canOpen = plan.ReusePassage && current is Building_Door door
                && plan.Assignments.Any(assignment => assignment.Pawn?.Spawned == true
                    && !assignment.Pawn.Dead && !assignment.Pawn.Downed
                    && door.PawnCanOpen(assignment.Pawn));
            return (BreachOpened(current) || canOpen) && plan.BreachCell.Walkable(map);
        }

        private void FinishBreachAttempt(RaidTacticalPlan plan,
            ExecutionState state, int tick)
        {
            if (BreachOpened(plan))
            {
                state.Breacher = null;
                state.BreachTarget = null;
                state.BreachKind = RaidBreachKind.None;
                Advance(state, RaidExecutionPhase.ObserveOpening, tick);
                return;
            }
            state.Breacher = null;
            state.BreachTarget = null;
            state.BreachKind = RaidBreachKind.None;
            if (++state.BreachAttempts >= 3)
                Advance(state, RaidExecutionPhase.Hold, tick);
        }

        private static List<Pawn> EntryPawns(List<Pawn> members, RaidTacticalPlan plan)
        {
            List<Pawn> entry = plan.Assignments.Where(assignment => assignment.Task == RaidTacticalTask.Entry
                && members.Contains(assignment.Pawn))
                .OrderBy(assignment => assignment.EntryOrder)
                .Select(assignment => assignment.Pawn).ToList();
            List<Pawn> moving = entry.Where(pawn => ContactGuardFor(pawn) == null).ToList();
            return moving.Count > 0 ? moving : entry;
        }

        private static bool PastBreach(Pawn pawn, RaidTacticalPlan plan)
        {
            IntVec3 breach = plan.BreachCell;
            IntVec3 towardInside = plan.BreachInside - breach;
            if ((pawn.Position.x - breach.x) * towardInside.x
                + (pawn.Position.z - breach.z) * towardInside.z < 1) return false;
            RaidStructureSnapshot structure = StructureFor(pawn.Map, plan);
            int insideRoom = structure?.RoomAt(plan.BreachInside) ?? 0;
            return insideRoom == 0 || structure.RoomAt(pawn.Position) == insideRoom;
        }

        private bool FollowBreachCrossing(List<Pawn> members, RaidTacticalPlan plan,
            ExecutionState state)
        {
            RememberTransitMouth(plan);
            RememberExteriorIngress(members, plan, state);
            List<Pawn> entry = EntryPawns(members, plan);
            HashSet<IntVec3> clearanceCells = null;
            if (state.CrossingBreach != plan.BreachCell)
            {
                int capacity = RaidBreachTraversal.AdmissionLimit(entry.Count,
                    (clearanceCells = BreachClearanceCells(plan)).Count);
                if (capacity == 0)
                {
                    foreach (Pawn pawn in entry)
                        MapComponent_RaidTacticalTrace.Record(pawn,
                            "Entry waiting: opening has no connected interior standing cell");
                    return false;
                }
                // A small room cannot physically hold the entire entry team.
                // Keep the excess members as outside security for this room;
                // the next room's plan assigns its own entry team again.
                List<Pawn> admitted = entry.Take(capacity).ToList();
                foreach (RaidTacticalAssignment assignment in plan.Assignments
                    .Where(value => value.Task == RaidTacticalTask.Entry && members.Contains(value.Pawn)
                        && !admitted.Contains(value.Pawn)))
                {
                    assignment.Task = RaidTacticalTask.Security;
                    plan.SafeSupportCells.Add(assignment.Position);
                    if (!AtStagingPosition(assignment, plan)) TryGoto(assignment.Pawn, assignment.Position);
                    else HoldPosition(assignment.Pawn);
                    MapComponent_RaidTacticalTrace.Record(assignment.Pawn,
                        "Small room: holding outside as reserve security");
                }
                entry = EntryPawns(members, plan);
                state.CrossingBreach = plan.BreachCell;
                state.Crossings.Clear();
            }
            state.Crossings.RemoveAll(crossing => crossing.Pawn == null
                || !entry.Contains(crossing.Pawn));
            var reserved = new HashSet<IntVec3>(state.Crossings
                .Where(crossing => crossing.Destination.IsValid)
                .Select(crossing => crossing.Destination));
            for (int i = 0; i < entry.Count; i++)
            {
                Pawn pawn = entry[i];
                BreachCrossing crossing = state.Crossings
                    .FirstOrDefault(value => value.Pawn == pawn);
                if (crossing == null)
                {
                    crossing = new BreachCrossing { Pawn = pawn };
                    state.Crossings.Add(crossing);
                }
                if ((!crossing.Destination.IsValid || !crossing.Destination.InBounds(map)
                    || !crossing.Destination.Standable(map)
                    || crossing.Progress != RaidBreachProgress.Complete
                        && (!map.pawnDestinationReservationManager.CanReserve(crossing.Destination, pawn)
                            || FormationOccupied(pawn, crossing.Destination, stationaryOnly: true)))
                    && GenTicks.TicksGame >= crossing.SearchAfter)
                {
                    reserved.Remove(crossing.Destination);
                    crossing.Destination = FindBreachClearanceCell(pawn, plan, i, reserved,
                        clearanceCells ?? (clearanceCells = BreachClearanceCells(plan)));
                    crossing.SearchAfter = GenTicks.TicksGame + 120;
                    if (crossing.Destination.IsValid) reserved.Add(crossing.Destination);
                    else MapComponent_RaidTacticalTrace.Record(pawn,
                        "Entry waiting: no unoccupied interior clearance cell");
                }
                crossing.Progress = RaidBreachTraversal.Advance(crossing.Progress,
                    pawn.Position.DistanceTo(plan.Entry) <= 1.5f, PastBreach(pawn, plan),
                    pawn.Position == crossing.Destination);
                if (IsTaserOperation(pawn)) continue;
                if (crossing.Progress != RaidBreachProgress.Complete)
                {
                    // The entire ready cohort starts together. Each pawn has a
                    // unique interior destination; vanilla movement resolves
                    // local passage and collisions instead of a nearest-first
                    // scheduler stopping everyone else at their stack cells.
                    if (crossing.Destination.IsValid) TryGoto(pawn, crossing.Destination, true);
                    else HoldPosition(pawn);
                }
                else if (crossing.Progress == RaidBreachProgress.Complete)
                {
                    if (pawn.Position != crossing.Destination && crossing.Destination.IsValid)
                        TryGoto(pawn, crossing.Destination, true);
                    else if (pawn.CurJobDef != JobDefOf.Wait_Combat)
                        HoldPosition(pawn);
                }
            }

            return state.Crossings.All(crossing => crossing.Progress == RaidBreachProgress.Complete);
        }

        private IntVec3 FindBreachClearanceCell(Pawn pawn, RaidTacticalPlan plan,
            int order, HashSet<IntVec3> reserved, IReadOnlyCollection<IntVec3> clearanceCells = null)
        {
            IntVec3 inward = plan.BreachInside - plan.BreachCell;
            IntVec3 along = new IntVec3(-inward.z, 0, inward.x);
            int side = order % 2 == 0 ? -1 : 1;
            int preferredLateral = side * (1 + order / 2);
            IEnumerable<IntVec3> cells = (clearanceCells ?? BreachClearanceCells(plan))
                .Where(cell => !reserved.Contains(cell)
                    && map.pawnDestinationReservationManager.CanReserve(cell, pawn)
                    && !cell.GetThingList(map).OfType<Pawn>().Any(other => other != pawn))
                .OrderBy(cell => RaidBreachTraversal.PlacementScore(
                    (cell.x - plan.BreachCell.x) * inward.x
                        + (cell.z - plan.BreachCell.z) * inward.z,
                    BackWallDistance(cell, inward),
                    (cell.x - plan.BreachCell.x) * along.x
                        + (cell.z - plan.BreachCell.z) * along.z,
                    preferredLateral, EntryCover(cell, plan)));
            foreach (IntVec3 cell in cells.Take(32))
                if (pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return cell;
            return IntVec3.Invalid;
        }

        private HashSet<IntVec3> BreachClearanceCells(RaidTacticalPlan plan)
        {
            IntVec3 inward = plan.BreachInside - plan.BreachCell;
            RaidStructureSnapshot structure = StructureFor(map, plan);
            int room = structure?.RoomAt(plan.BreachInside) ?? 0;
            HashSet<IntVec3> connected = RaidFormationTopology.Connected(
                GenRadial.RadialCellsAround(plan.BreachInside, 12f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && !plan.AvoidedTrapCells.Contains(cell)
                        && (cell.x - plan.BreachCell.x) * inward.x
                            + (cell.z - plan.BreachCell.z) * inward.z >= 1
                        && (cell.x - plan.BreachCell.x) * inward.x
                            + (cell.z - plan.BreachCell.z) * inward.z <= 6
                        && (room == 0 || structure.RoomAt(cell) == room)),
                plan.BreachInside, cell => GenAdj.CardinalDirections.Select(direction => cell + direction),
                cell => true);
            bool singleCellRoom = connected.Count == 1;
            connected.RemoveWhere(cell => !RaidBreachTraversal.IsClearance(
                (cell.x - plan.BreachCell.x) * inward.x
                    + (cell.z - plan.BreachCell.z) * inward.z, cell == plan.BreachInside, singleCellRoom)
                || !RaidBreachTraversal.PlacementDepthAllowed(
                    (cell.x - plan.BreachCell.x) * inward.x
                        + (cell.z - plan.BreachCell.z) * inward.z,
                    (cell.x - plan.BreachCell.x) * inward.x
                        + (cell.z - plan.BreachCell.z) * inward.z > 3 && EntryCover(cell, plan) >= 0.1f));
            return connected;
        }

        private int BackWallDistance(IntVec3 cell, IntVec3 inward)
        {
            for (int distance = 1; distance <= 3; distance++)
            {
                IntVec3 behind = cell - inward * distance;
                if (!behind.InBounds(map)) break;
                Building wall = behind.GetEdifice(map) as Building;
                if (wall?.def.IsWall == true || wall is Building_Door door && !door.Open)
                    return distance;
            }
            return 4;
        }

        private float EntryCover(IntVec3 cell, RaidTacticalPlan plan)
        {
            // Rear walls are the fallback, not forward cover. Only actual
            // non-wall cover facing into the room can justify a deeper slot.
            IntVec3 inward = plan.BreachInside - plan.BreachCell;
            IntVec3 front = plan.BreachInside + inward * 8;
            float best = 0f;
            foreach (CoverInfo cover in CoverUtility.CalculateCoverGiverSet(cell, front, map))
                if (cover.Thing?.def.IsWall != true && !(cover.Thing is Building_Door))
                    best = Math.Max(best, cover.BlockChance);
            return best;
        }

        private static RaidExecutionPhase AfterSupport(RaidTacticalPlan plan,
            RaidTacticalManeuver maneuver)
        {
            return maneuver == RaidTacticalManeuver.FlankAttack
                && plan.Flank.IsValid ? RaidExecutionPhase.Flank : RaidExecutionPhase.Assault;
        }

        private static bool EntryMembersNear(List<Pawn> members, RaidTacticalPlan plan,
            IntVec3 cell, float radius)
        {
            return plan.Assignments.Where(assignment => assignment.Task == RaidTacticalTask.Entry
                && members.Contains(assignment.Pawn))
                .All(assignment => assignment.Pawn.Position.DistanceTo(cell) <= radius);
        }

        private void IssueFlank(List<Pawn> members, RaidTacticalPlan plan)
        {
            var occupied = new HashSet<IntVec3>();
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .Where(value => value.Task == RaidTacticalTask.Entry))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || IsTaserOperation(pawn)) continue;
                IntVec3 target = GenRadial.RadialCellsAround(plan.Flank, 3f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && !plan.AvoidedTrapCells.Contains(cell) && !occupied.Contains(cell))
                    .OrderBy(cell => cell.DistanceToSquared(plan.Flank))
                    .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) target = plan.Flank;
                occupied.Add(target);
                TryGoto(pawn, target);
            }
        }

        private void Assemble(List<Pawn> members, RaidTacticalPlan plan,
            bool skipResponse = false)
        {
            if (StateFor(plan.UnitId)?.Phase != RaidExecutionPhase.Hold)
                RetargetBlockedStackMembers(members, plan, onlyBlocked: true);
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || !assignment.Position.IsValid
                    || (skipResponse && assignment.Task == RaidTacticalTask.Response)) continue;
                if (IsTaserOperation(pawn)) continue;
                ExecutionState state = StateFor(plan.UnitId);
                if (state?.Phase == RaidExecutionPhase.Hold
                    && assignment.Task != RaidTacticalTask.Withdraw)
                {
                    MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Fight,
                        assignment.Position, radius: assignment.Task == RaidTacticalTask.FireSupport ? 6f : 4f);
                    continue;
                }
                bool staged = AtStagingPosition(assignment, plan);
                if (!staged)
                    TryGoto(pawn, assignment.Position);
                else if (assignment.Task == RaidTacticalTask.Withdraw
                    && TcccUtility.HasTraining(pawn)
                    && pawn.health?.hediffSet?.BleedRateTotal > 0f)
                {
                    if (pawn.CurJobDef?.defName != "HD_TCCC_Treat")
                        TcccUtility.Start(pawn, pawn, TcccTreatment.Hemostasis);
                }
                else if (pawn.CurJobDef != JobDefOf.Wait_Combat
                    || pawn.CurJob?.expiryInterval <= 0)
                    HoldPosition(pawn);
                if (plan.BreachCell.IsValid
                    && assignment.Task != RaidTacticalTask.Withdraw
                    && staged)
                    FaceStackSector(pawn, plan, assignment);
            }
        }

        private static void MaintainStack(List<Pawn> members, RaidTacticalPlan plan,
            Pawn exempt = null, bool holdEntry = true)
        {
            if (!plan.BreachCell.IsValid) return;
            RetargetBlockedStackMembers(members, plan, onlyBlocked: true, exempt: exempt, holdEntry: holdEntry);
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
            {
                Pawn pawn = assignment.Pawn;
                if (pawn == exempt || RaidEntryObservation.Active(pawn) != null || !members.Contains(pawn)
                    || !holdEntry && assignment.Task == RaidTacticalTask.Entry
                    || assignment.Task == RaidTacticalTask.Withdraw
                    || !assignment.Position.IsValid || IsTaserOperation(pawn)) continue;
                if (!AtStagingPosition(assignment, plan))
                    TryGoto(pawn, assignment.Position);
                else
                {
                    if (pawn.CurJobDef != JobDefOf.Wait_Combat
                        || pawn.CurJob?.expiryInterval <= 0)
                        HoldPosition(pawn);
                    FaceStackSector(pawn, plan, assignment);
                }
            }
        }

        private static void FaceStackSector(Pawn pawn, RaidTacticalPlan plan,
            RaidTacticalAssignment assignment)
        {
            IntVec3 outward = plan.Entry - plan.BreachCell;
            IntVec3 along = new IntVec3(-outward.z, 0, outward.x);
            IntVec3 offset = pawn.Position - plan.BreachCell;
            int side = offset.x * along.x + offset.z * along.z >= 0 ? 1 : -1;
            IntVec3 focus = assignment.Task == RaidTacticalTask.Entry
                && assignment.EntryOrder <= 2
                ? plan.BreachCell + outward * 2
                : plan.BreachCell + along * (side * 9) + outward * 3;
            if (!focus.IsValid || pawn.Position == focus) return;
            MapComponent_RaidTacticalOrders.Face(pawn, Rot4.FromAngleFlat(
                (focus - pawn.Position).ToVector3().AngleFlat()));
        }

        private Pawn CurrentResponseTarget(List<Pawn> members, RaidTacticalPlan plan)
        {
            return map.mapPawns.AllPawnsSpawned
                .Where(enemy => !enemy.Dead && !enemy.Downed && enemy.Faction != null
                    && enemy.Faction.HostileTo(members[0].Faction)
                    && enemy.Position.DistanceTo(plan.Start) <= 25f
                    && members.Any(member => GenSight.LineOfSight(member.Position, enemy.Position, map, true)))
                .OrderBy(enemy => enemy.Position.DistanceToSquared(plan.Start))
                .FirstOrDefault();
        }

        private void TryCounterPsychicLance(List<Pawn> members)
        {
            JobDef fireJob = DefDatabase<JobDef>.GetNamedSilentFail("HD_ZaperX26Fire");
            if (fireJob == null) return;
            JobDef contactJob = DefDatabase<JobDef>.GetNamedSilentFail("HD_ZaperX26Contact");
            foreach (Pawn leader in members)
            {
                CompZaperX26 linked = leader.apparel?.WornApparel
                    .Select(apparel => apparel.TryGetComp<CompZaperX26>())
                    .FirstOrDefault(comp => comp?.TetheredTarget != null);
                Pawn target = linked?.TetheredTarget;
                if (target?.Spawned != true || target.Map != map || target.Dead
                    || target.Faction == null || !target.Faction.HostileTo(leader.Faction))
                    continue;
                if (leader.CurJobDef == JobDefOf.Kidnap
                    || leader.CurJobDef == contactJob) continue;
                if (map.mapPawns.AllPawnsSpawned.Any(enemy => enemy != target
                    && !enemy.Dead && !enemy.Downed && enemy.Faction != null
                    && enemy.Faction.HostileTo(leader.Faction)
                    && enemy.Position.DistanceTo(target.Position) <= 8f
                    && GenSight.LineOfSight(leader.Position, enemy.Position, map, true)))
                    continue;
                if (target.Downed && leader.CanReserve(target, 1, -1, null, false)
                    && leader.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                {
                    leader.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Kidnap, target),
                        JobCondition.InterruptForced);
                    RaidTacticalSpeech.Say(leader, "HD_RaidTactical_Neutralize");
                    return;
                }
                if (!target.Downed && contactJob != null && linked.CanContactNow
                    && leader.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                {
                    leader.jobs.StartJob(JobMaker.MakeJob(contactJob, target,
                        linked.parent), JobCondition.InterruptForced);
                    return;
                }
            }
            List<Pawn> lanceUsers = map.mapPawns.AllPawnsSpawned
                .Where(pawn => !pawn.Dead && !pawn.Downed && pawn.Faction != null
                    && pawn.Faction.HostileTo(members[0].Faction)
                    && pawn.apparel?.WornApparel.Any(apparel =>
                        apparel.def.defName.Contains("PsychicShockLance")
                        || apparel.def.defName.Contains("PsychicInsanityLance")) == true)
                .ToList();
            if (lanceUsers.Count == 0) return;
            foreach (Pawn leader in members)
            {
                CompZaperX26 zaper = leader.apparel?.WornApparel
                    .Select(apparel => apparel.TryGetComp<CompZaperX26>())
                    .FirstOrDefault(comp => comp?.CanFireNow == true);
                if (zaper == null || IsTaserOperation(leader)) continue;
                Pawn target = lanceUsers
                    .Where(enemy => leader.Position.DistanceTo(enemy.Position) <= 7.9f
                        && GenSight.LineOfSight(leader.Position, enemy.Position, map))
                    .OrderBy(enemy => enemy.Position.DistanceToSquared(leader.Position))
                    .FirstOrDefault();
                if (target == null) continue;
                leader.jobs.StartJob(JobMaker.MakeJob(fireJob, target, zaper.parent),
                    JobCondition.InterruptForced);
                RaidTacticalSpeech.Say(leader, "HD_RaidTactical_Neutralize");
                return;
            }
        }

        private static bool UsingZaper(List<Pawn> members)
        {
            return members.Any(pawn => pawn.CurJobDef?.defName == "HD_ZaperX26Fire"
                || pawn.CurJobDef?.defName == "HD_ZaperX26Contact");
        }

        private static bool IsTaserOperation(Pawn pawn)
        {
            return pawn.CurJobDef?.defName == "HD_ZaperX26Fire"
                || pawn.CurJobDef?.defName == "HD_ZaperX26Contact"
                || pawn.CurJobDef == JobDefOf.Kidnap;
        }

        private void IssueResponse(List<Pawn> members, RaidTacticalPlan plan)
        {
            Pawn enemy = CurrentResponseTarget(members, plan);
            if (enemy == null) return;
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .Where(value => value.Task == RaidTacticalTask.Response))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || pawn.Map != map
                    || IsTaserOperation(pawn)) continue;
                MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Fight,
                    plan.Start, radius: 25f);
            }
        }

        private void TryRequestExternalSupport(RaidTacticalUnit unit,
            List<Pawn> members, RaidTacticalPlan plan, ExecutionState state)
        {
            if (StructureFor(map, plan)?.IsIndoor(plan.Objective) == true
                || Find.WorldObjects == null) return;
            List<HelodForwardBase> bases = Find.WorldObjects.AllWorldObjects
                .OfType<HelodForwardBase>()
                .Where(value => value.Faction == unit.Faction).ToList();
            if (bases.Count == 0) return;
            Pawn target = map.mapPawns.AllPawnsSpawned
                .Where(enemy => !enemy.Dead && !enemy.Downed
                    && enemy.Faction != null && enemy.Faction.HostileTo(unit.Faction)
                    && enemy.Position.DistanceTo(plan.Objective) <= 12f
                    && members.Any(member => member.Position.DistanceToSquared(enemy.Position) <= 1600
                        && GenSight.LineOfSight(member.Position, enemy.Position, map, true))
                    && map.mapPawns.AllPawnsSpawned.All(other => other.Dead
                        || other.HostileTo(members[0])
                        || other.Position.DistanceTo(enemy.Position) >= 20f))
                .OrderBy(enemy => enemy.Position.DistanceToSquared(plan.Objective))
                .FirstOrDefault();
            if (target == null) return;
            Pawn caller = members.FirstOrDefault(pawn =>
                HelodCasSupportUtility.CanUseTalkOnTarget(pawn, map, target.Position));
            if (caller == null) return;

            if (!SCR300RadioUtility.IsBlackout(map))
                foreach (HelodForwardBase forwardBase in bases
                    .Where(value => HelodCasSupportUtility.CanUseBase(map, value)))
                {
                    HelodCasAircraftKind aircraft = HelodCasSupportUtility
                        .AircraftForForwardBase(forwardBase);
                    HelodCasAttackKind attack = aircraft == HelodCasAircraftKind.P47
                        ? HelodCasAttackKind.Bombing : HelodCasAttackKind.GBU54;
                    IntVec3 entry = new IntVec3(plan.Start.x <= target.Position.x
                        ? 0 : map.Size.x - 1, 0, target.Position.z);
                    HelodCasSupportUtility.ScatterFor(aircraft, attack,
                        out float major, out float minor);
                    var strike = new HelodCasAttackPlan(entry, target.Position,
                        HelodCasGuidanceMode.TalkOn, map, major, minor, attack, aircraft,
                        new[] { target.Position }, new Thing[] { target });
                    if (HelodCasSupportUtility.TryCall(map, strike, forwardBase,
                        caller, null))
                    {
                        state.ExternalSupportKind = RaidExternalSupportKind.Air;
                        state.ExternalSupportCaller = caller;
                        state.ExternalSupportTarget = target;
                        state.ExternalSupportTargetCell = target.Position;
                        RaidTacticalSpeech.Say(caller, "HD_RaidTactical_ExternalSupport");
                        return;
                    }
                }

            ThingDef shell = DefDatabase<ThingDef>.GetNamedSilentFail(
                plan.Doctrine == RaidTacticalDoctrine.High
                    ? "HD_105mmShell_M1HE" : "HD_81mmMortarShell_M43HE");
            if (shell?.projectileWhenLoaded == null) return;
            HelodForwardBaseService service = plan.Doctrine == RaidTacticalDoctrine.High
                ? HelodForwardBaseService.Artillery105mmSupport
                : HelodForwardBaseService.InfantryMortarSupport;
            foreach (HelodForwardBase forwardBase in bases
                .Where(value => HelodMortarSupportUtility.CanUseBase(map, value, service)))
                if (HelodMortarSupportUtility.TryCall(map, target.Position,
                    forwardBase, shell, default(IntVec3), null, caller, service))
                {
                    state.ExternalSupportKind = RaidExternalSupportKind.Artillery;
                    state.ExternalSupportCaller = caller;
                    state.ExternalSupportTargetCell = target.Position;
                    RaidTacticalSpeech.Say(caller, "HD_RaidTactical_ExternalSupport");
                    return;
                }
        }

        private bool WaitingForExternalSupport(ExecutionState state,
            List<Pawn> members, int tick)
        {
            if (state.ExternalSupportKind == RaidExternalSupportKind.None) return false;
            IntVec3 aim = state.ExternalSupportKind == RaidExternalSupportKind.Air
                && state.ExternalSupportTarget?.Spawned == true
                ? state.ExternalSupportTarget.Position : state.ExternalSupportTargetCell;
            bool unsafeToFire = aim.IsValid && map.mapPawns.AllPawnsSpawned.Any(pawn =>
                !pawn.Dead && !pawn.HostileTo(members[0])
                && pawn.Position.DistanceTo(aim) < 20f);
            if (unsafeToFire)
            {
                if (state.ExternalSupportKind == RaidExternalSupportKind.Air)
                {
                    MapComponent_HelodCasSupport support = map
                        .GetComponent<MapComponent_HelodCasSupport>();
                    if (support?.CanCancelStrike(state.ExternalSupportCaller,
                            out _) == true)
                        support.TryCancelStrike(state.ExternalSupportCaller);
                }
                else map.GetComponent<MapComponent_HelodMortarSupport>()
                    ?.CancelStrike(state.ExternalSupportCaller);
            }
            bool active = state.ExternalSupportKind == RaidExternalSupportKind.Air
                ? map.GetComponent<MapComponent_HelodCasSupport>()
                    ?.HasActiveStrike(state.ExternalSupportCaller) == true
                : map.GetComponent<MapComponent_HelodMortarSupport>()
                    ?.HasActiveStrike(state.ExternalSupportCaller) == true;
            if (active)
            {
                state.ExternalSupportClearedTick = 0;
                return true;
            }
            if (state.ExternalSupportClearedTick == 0)
                state.ExternalSupportClearedTick = tick;
            if (tick - state.ExternalSupportClearedTick < 180) return true;
            state.ExternalSupportKind = RaidExternalSupportKind.None;
            state.ExternalSupportCaller = null;
            state.ExternalSupportTarget = null;
            state.PhaseStarted = tick;
            state.ReadySince = -1;
            return false;
        }

        private static bool AllReady(List<Pawn> members, RaidTacticalPlan plan)
        {
            ExecutionState state = members.Count == 0 ? null : members[0].Map
                .GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(plan.UnitId);
            return (state == null ? plan.Assignments.Where(assignment => members.Contains(assignment.Pawn)
                    && assignment.Task != RaidTacticalTask.Withdraw)
                : ApproachAssignments(members, plan, state, GenTicks.TicksGame))
                .All(assignment => AtStagingPosition(assignment, plan));
        }

        private static bool AtStagingPosition(RaidTacticalAssignment assignment,
            RaidTacticalPlan plan)
        {
            Pawn pawn = assignment.Pawn;
            if (assignment.Task == RaidTacticalTask.Withdraw)
                return pawn.Position.DistanceTo(assignment.Position) <= 1.5f;
            return assignment.Position.IsValid
                && RaidFormationSlots<IntVec3>.Ready(pawn.Position, assignment.Position,
                    pawn.Position == assignment.Position && FormationOccupied(pawn, pawn.Position));
        }

        private static void HoldPosition(Pawn pawn)
        {
            if (MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Hold, pawn.Position)) return;
            Job job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            job.expiryInterval = 60000;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private static void TryGoto(Pawn pawn, IntVec3 cell, bool sprint = false,
            bool interruptTaser = false, bool fightOnArrival = false, float activityRadius = 10f)
        {
            if ((!interruptTaser && IsTaserOperation(pawn))
                || !cell.IsValid || !cell.InBounds(pawn.Map)) return;
            if (MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Move,
                cell, sprint, fightOnArrival, activityRadius)) return;
            if (pawn.Position == cell)
            {
                if (pawn.CurJobDef == JobDefOf.Goto) HoldPosition(pawn);
                return;
            }
            if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == cell) return;
            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            if (sprint) job.locomotionUrgency = LocomotionUrgency.Sprint;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private static bool PlannedBreachCell(Pawn pawn, RaidTacticalPlan plan,
            Building target, out IntVec3 cell)
        {
            cell = plan.Entry;
            return target == plan.PlannedBreach && cell.IsValid
                && GenAdj.CellsAdjacentCardinal(target).Contains(cell)
                && cell.Standable(pawn.Map)
                && pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly);
        }

        private static bool TryStartBreach(List<Pawn> members, RaidTacticalPlan plan,
            ExecutionState state)
        {
            if (state.Maneuver != RaidTacticalManeuver.CoordinatedEntry) return false;
            if (plan.BreachCell.IsValid && plan.PlannedBreach == null) return false;
            if (plan.Doctrine == RaidTacticalDoctrine.Low
                && TryStartSledgehammerBreach(members, plan, state)) return true;
            Map currentMap = members[0].Map;
            Building target = plan.PlannedBreach ?? GenRadial.RadialCellsAround(plan.Entry, 2.9f, true)
                .Where(cell => cell.InBounds(currentMap))
                .Select(cell => cell.GetEdifice(currentMap) as Building)
                .Where(building => CompPowerCutterBreach.IsValidBreachTarget(building)
                    && building.Faction != null
                    && building.Faction.HostileTo(members[0].Faction))
                .OrderBy(building => building.Position.DistanceToSquared(plan.Entry))
                .FirstOrDefault();
            if (target == null) return false;
            JobDef cutterJob = DefDatabase<JobDef>.GetNamedSilentFail("HD_PowerCutterBreach");
            foreach (Pawn pawn in members)
            {
                if (IsTaserOperation(pawn) || cutterJob == null
                    || pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>() == null
                    || !CompPowerCutterBreach.IsValidBreachTarget(target)
                    || !(plan.PlannedBreach != null
                        ? PlannedBreachCell(pawn, plan, target, out IntVec3 cell)
                        : CompPowerCutterBreach.TryFindInteractionCell(pawn, target,
                            out cell))
                    || !pawn.CanReserve(target, 1, -1, null, false)) continue;
                state.Breacher = pawn;
                state.BreachTarget = target;
                state.BreachKind = RaidBreachKind.PowerCutter;
                RaidTacticalSpeech.Say(pawn, "HD_RaidTactical_Breaching");
                pawn.jobs.StartJob(JobMaker.MakeJob(cutterJob, target, cell),
                    JobCondition.InterruptForced);
                return true;
            }
            JobDef c4Job = DefDatabase<JobDef>.GetNamedSilentFail(
                BreachExplosiveUtility.ShockTubeJobDefName);
            if (c4Job == null) return false;
            foreach (Pawn pawn in members)
            {
                if (IsTaserOperation(pawn)) continue;
                CompBreachIgniter igniter = BreachExplosiveUtility.FindIgniter(pawn,
                    BreachInitiationMode.ShockTube, false);
                int required = BreachExplosiveUtility.RequiredC4For(target);
                if (!target.def.IsWall || !BreachExplosiveUtility.CanOperate(pawn)
                    || igniter == null
                    || BreachExplosiveUtility.CountInInventory(pawn,
                        BreachExplosiveUtility.C4Def) < required
                    || BreachExplosiveUtility.ActiveChargeFor(pawn) != null
                    || BreachExplosiveUtility.ChargeOnWall(target) != null
                    || !(plan.PlannedBreach != null
                        ? PlannedBreachCell(pawn, plan, target, out IntVec3 cell)
                        : BreachExplosiveUtility.TryFindInteractionCell(pawn, target,
                            out cell))
                    || !pawn.CanReserve(target, 1, -1, null, false)) continue;
                Job job = JobMaker.MakeJob(c4Job, target, cell, igniter.parent);
                job.count = required;
                state.Breacher = pawn;
                state.BreachTarget = target;
                state.BreachKind = RaidBreachKind.C4;
                RaidTacticalSpeech.Say(pawn, "HD_RaidTactical_Breaching");
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                return true;
            }
            return false;
        }

        private static bool TryStartSledgehammerBreach(List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state)
        {
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                CompSledgehammerBreach.JobDefName);
            if (jobDef == null) return false;
            Map currentMap = members[0].Map;
            IEnumerable<Building> targets = plan.PlannedBreach != null
                ? (IEnumerable<Building>)new[] { plan.PlannedBreach }
                : GenRadial.RadialCellsAround(plan.Entry, 2.9f, true)
                .Where(cell => cell.InBounds(currentMap))
                .Select(cell => cell.GetEdifice(currentMap) as Building)
                .Where(building => building != null && building.Faction != null
                    && building.Faction.HostileTo(members[0].Faction))
                .Distinct()
                .OrderBy(building => building is Building_Door ? 0 : 1)
                .ThenBy(building => building.Position.DistanceToSquared(plan.Entry));
            foreach (Building target in targets)
            {
                foreach (Pawn pawn in members)
                {
                    CompSledgehammerBreach tool = CompSledgehammerBreach.WornBy(pawn);
                    if (tool == null || IsTaserOperation(pawn)
                        || !CompSledgehammerBreach.IsValidTarget(pawn, target)
                        || !(plan.PlannedBreach != null
                            ? PlannedBreachCell(pawn, plan, target, out IntVec3 cell)
                            : CompSledgehammerBreach.TryFindInteractionCell(pawn,
                                target, out cell))
                        || !pawn.CanReserve(target, 1, -1, null, false)) continue;
                    state.Breacher = pawn;
                    state.BreachTarget = target;
                    state.BreachKind = RaidBreachKind.Sledgehammer;
                    RaidTacticalSpeech.Say(pawn, "HD_RaidTactical_Breaching");
                    pawn.jobs.StartJob(JobMaker.MakeJob(jobDef, target, cell,
                        tool.parent), JobCondition.InterruptForced);
                    return true;
                }
            }
            return false;
        }

        private void IssueChargeWithdrawal(List<Pawn> members, RaidTacticalPlan plan,
            IntVec3 chargeCell, float safeRadius)
        {
            var occupied = new HashSet<IntVec3>();
            foreach (Pawn pawn in members.OrderBy(pawn => pawn.Position.DistanceTo(chargeCell)))
            {
                if (pawn.Position.DistanceTo(chargeCell) >= safeRadius) continue;
                IntVec3 target = GenRadial.RadialCellsAround(chargeCell,
                        safeRadius + 7f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && cell.DistanceTo(chargeCell) >= safeRadius
                        && !plan.AvoidedTrapCells.Contains(cell)
                        && !occupied.Contains(cell))
                    .OrderBy(cell => cell.DistanceTo(plan.Start) * 0.6f
                        + cell.DistanceTo(chargeCell) * 0.4f)
                    .Where(cell => pawn.CanReach(cell,
                        PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                occupied.Add(target);
                TryGoto(pawn, target, interruptTaser: true);
            }
        }

        private static Pawn TryStartSupport(List<Pawn> members, RaidTacticalPlan plan,
            RaidTacticalManeuver maneuver, Pawn preferred = null, RaidEntryObservation observation = null)
        {
            bool entry = maneuver == RaidTacticalManeuver.CoordinatedEntry;
            if (entry && plan.ObjectiveIsRecheck) return null;
            Map currentMap = members[0].Map;
            RaidStructureSnapshot structure = StructureFor(currentMap, plan);
            int objectiveRoom = structure?.RoomAt(plan.BreachCell.IsValid ? plan.BreachInside : plan.Objective) ?? 0;
            RaidEntrySupportKind entrySupport = RaidEntryObservationPolicy.Support(objectiveRoom == 0,
                structure?.RoomArea(objectiveRoom) ?? 0, observation?.HasEnemyContact == true);
            if (entry && entrySupport == RaidEntrySupportKind.None) return null;
            bool entrySmoke = entry && entrySupport == RaidEntrySupportKind.Smoke;
            bool smoke = maneuver == RaidTacticalManeuver.SmokeAdvance || entrySmoke;
            bool fieldGrenade = maneuver == RaidTacticalManeuver.FieldGrenade;
            if (!smoke && !entry && !fieldGrenade) return null;
            RaidContactMemory contacts = currentMap.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(plan.UnitId)?.Contacts;
            RaidEnemyContact contactTarget = entry && observation?.HasEnemyContact == true
                ? contacts?.Entries.FirstOrDefault(contact => contact.EnemyId == observation.EnemyId) : null;
            if (entry && observation?.HasEnemyContact == true
                && (contactTarget == null || !contacts.CanTarget(contactTarget.EnemyId, contactTarget.Position, GenTicks.TicksGame))) return null;
            if (entry && !smoke && plan.Doctrine == RaidTacticalDoctrine.Low
                && currentMap.mapPawns.AllPawnsSpawned.Any(pawn => pawn.Faction == members[0].Faction
                    && (plan.BreachCell.IsValid
                        ? PastBreach(pawn, plan)
                        : objectiveRoom > 0
                            && structure.RoomAt(pawn.Position) == objectiveRoom))) return null;
            IEnumerable<IntVec3> targets = entry && observation?.HasEnemyContact == true
                ? new[] { contactTarget.Position } : entrySmoke ? EntrySmokeTargets(currentMap, plan, structure) : smoke
                ? (IEnumerable<IntVec3>)new[] { plan.Frontline }
                : fieldGrenade
                    ? (IEnumerable<IntVec3>)currentMap.mapPawns.AllPawnsSpawned
                        .Where(hostile => hostile.Faction != null
                            && hostile.Faction.HostileTo(members[0].Faction)
                            && !hostile.Dead && !hostile.Downed
                            && members.Any(member => member.Position.DistanceToSquared(hostile.Position) <= 1600
                                && GenSight.LineOfSight(member.Position, hostile.Position, currentMap, true)))
                        .Select(hostile => hostile.Position)
                        .Distinct()
                        .OrderBy(cell => cell.DistanceTo(plan.Frontline))
                        .ToList()
                : EntryGrenadeTargets(currentMap, plan, structure, objectiveRoom, observation);
            preferred = preferred ?? observation?.Observer;
            List<Pawn> throwers = members.OrderBy(value => value == preferred ? -1
                : entry && !smoke && plan.Doctrine == RaidTacticalDoctrine.Low
                    && InventoryGrenadeUtility.GrenadeStacks(value)
                        .Any(item => item.def.defName == "HD_Grenade_MKIII") ? 0 : 1)
                .ThenBy(value => value.Position.DistanceToSquared(plan.Entry)).ToList();
            // Contact throws keep their exact target; an unsafe/near-opening contact is skipped.
            foreach (IntVec3 target in targets.Take(16))
            {
                if (!target.IsValid || !target.InBounds(currentMap)
                    || entry && !RaidEntryObservation.EntryThrowTarget(plan, target)) continue;
                foreach (Pawn pawn in throwers)
                {
                    if (IsTaserOperation(pawn)) continue;
                    Thing grenade = InventoryGrenadeUtility.GrenadeStacks(pawn)
                        .FirstOrDefault(item => IsSupportGrenade(item, plan, smoke));
                    if (grenade == null) continue;
                    if (!smoke && !GrenadeTargetSafe(currentMap, pawn, target))
                        continue;
                    IEnumerable<IntVec3> positions = entry && plan.BreachCell.IsValid
                        ? new[] { pawn.Position, observation?.Position ?? IntVec3.Invalid, plan.Entry }
                            .Concat(plan.SafeStackCells.Take(8)).Distinct()
                        : new[] { pawn.Position };
                    foreach (IntVec3 position in positions)
                    {
                        if (!position.InBounds(currentMap) || !position.Standable(currentMap)
                            || entry && plan.BreachCell.IsValid && (position == plan.BreachCell
                                || structure?.RoomAt(position) != structure?.RoomAt(plan.Entry))) continue;
                        bool close = InventoryGrenadeUtility.TryFindThrowSourceFrom(pawn, position, target,
                            InventoryGrenadeUtility.CloseThrowRange, out _);
                        if (!close && !InventoryGrenadeUtility.TryFindThrowSourceFrom(pawn, position, target,
                            InventoryGrenadeUtility.NormalThrowRange, out _)) continue;
                        if (!SafeSupportThrowFrom(pawn, grenade, position, target, close)
                            || !RaidGrenadePreparation.Start(pawn, grenade, target, position, close)) continue;
                        RaidTacticalSpeech.Say(pawn, smoke ? "HD_RaidTactical_Smoke" : "HD_RaidTactical_Grenade");
                        return pawn;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<IntVec3> EntryGrenadeTargets(Map map,
            RaidTacticalPlan plan, RaidStructureSnapshot structure, int objectiveRoom, RaidEntryObservation observation)
        {
            IntVec3 center = plan.BreachCell.IsValid ? plan.BreachInside : plan.Entry;
            bool InTarget(IntVec3 cell) => cell.InBounds(map) && cell.Standable(map)
                && objectiveRoom > 0 && OpeningRoomContains(map, structure, cell, objectiveRoom)
                && !plan.AvoidedTrapCells.Contains(cell)
                && RaidEntryObservation.EntryThrowTarget(plan, cell);
            return RaidEntryObservation.ThrowTargets(observation,
                GenRadial.RadialCellsAround(center, 13.9f, true).OrderBy(cell => cell.DistanceToSquared(center)), InTarget);
        }

        private static IEnumerable<IntVec3> EntrySmokeTargets(Map map,
            RaidTacticalPlan plan, RaidStructureSnapshot structure)
        {
            IntVec3 inward = plan.BreachInside - plan.BreachCell;
            IntVec3 center = plan.BreachInside + inward;
            int room = structure?.RoomAt(plan.BreachInside) ?? 0;
            return GenRadial.RadialCellsAround(center, 3f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && !plan.AvoidedTrapCells.Contains(cell)
                    && OpeningRoomContains(map, structure, cell, room)
                    && GenSight.LineOfSight(plan.BreachInside, cell, map, true)
                    && RaidEntryObservation.EntryThrowTarget(plan, cell)
                    && (cell.x - plan.BreachCell.x) * inward.x
                        + (cell.z - plan.BreachCell.z) * inward.z <= 3)
                .OrderBy(cell => cell.DistanceToSquared(center));
        }

        private static bool IsSupportGrenade(Thing item, RaidTacticalPlan plan,
            bool smoke)
        {
            return smoke ? RaidSmokeUtility.IsSmoke(item)
                : plan.Doctrine == RaidTacticalDoctrine.High
                    ? item.def.defName == "HD_Grenade_M84_Item"
                        || item.def.defName == "HD_Grenade_M7A2_Item"
                    : item.def.defName == "HD_Grenade_MKII"
                        || item.def.defName == "HD_Grenade_MKIII";
        }

        private static bool GrenadeTargetSafe(Map map, Pawn thrower, IntVec3 target)
        {
            return !map.mapPawns.AllPawnsSpawned.Any(ally => !ally.Dead
                && !ally.HostileTo(thrower)
                && ally.Position.DistanceTo(target) <= 3.5f);
        }

        private void IssueAssault(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state)
        {
            Map currentMap = members[0].Map;
            RaidStructureSnapshot structure = StructureFor(currentMap, plan);
            int objectiveRoom = structure?.RoomAt(plan.Objective) ?? 0;
            IntVec3 inward = plan.BreachCell.IsValid
                ? plan.BreachInside - plan.BreachCell : IntVec3.Zero;
            bool highIndoor = plan.Doctrine == RaidTacticalDoctrine.High
                && objectiveRoom > 0;
            var occupied = new HashSet<IntVec3>();
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .Where(value => value.Task == RaidTacticalTask.Entry)
                .OrderBy(value => value.EntryOrder))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || IsTaserOperation(pawn)) continue;
                IntVec3 target = plan.BreachCell.IsValid
                    ? state.Crossings.FirstOrDefault(value => value.Pawn == pawn)?.Destination
                        ?? IntVec3.Invalid : IntVec3.Invalid;
                if (target.IsValid && (!target.InBounds(map) || !target.Standable(map)))
                    target = IntVec3.Invalid;
                if (!target.IsValid && !plan.BreachCell.IsValid)
                    target = GenRadial.RadialCellsAround(plan.Objective, 4f, true)
                    .Where(cell => cell.InBounds(currentMap) && cell.Standable(currentMap)
                        && !occupied.Contains(cell) && !plan.AvoidedTrapCells.Contains(cell)
                        && (objectiveRoom == 0 || structure.RoomAt(cell) == objectiveRoom)
                        && (!plan.BreachCell.IsValid
                            || (cell.x - plan.BreachCell.x) * inward.x
                                + (cell.z - plan.BreachCell.z) * inward.z >= 2))
                    .OrderBy(cell => cell.DistanceToSquared(plan.Objective))
                    .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid && plan.BreachCell.IsValid)
                    target = FindBreachClearanceCell(pawn, plan,
                        assignment.EntryOrder - 1, occupied);
                if (!target.IsValid) target = pawn.Position;
                occupied.Add(target);
                TryGoto(pawn, target, highIndoor, fightOnArrival: true,
                    activityRadius: plan.BreachCell.IsValid ? 3f : 10f);
            }
        }

        private void IssueRoomSecurity(List<Pawn> members, RaidTacticalPlan plan)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            int room = structure?.RoomAt(plan.Objective) ?? 0;
            if (room == 0) return;
            Dictionary<int, bool> knownDoors = RaidLocalMapState.KnownDoors(StateFor(plan.UnitId)?.DoorStateSignature);
            if (plan.BreachCell.IsValid && structure.RoomAt(plan.BreachInside) == room)
            {
                ExecutionState state = StateFor(plan.UnitId);
                foreach (RaidTacticalAssignment assignment in plan.Assignments.Where(value =>
                    value.Task == RaidTacticalTask.Entry && members.Contains(value.Pawn)))
                {
                    Pawn pawn = assignment.Pawn;
                    if (IsTaserOperation(pawn)) continue;
                    IntVec3 center = state?.Crossings.FirstOrDefault(value => value.Pawn == pawn)
                        ?.Destination ?? pawn.Position;
                    if (!center.IsValid || !center.InBounds(map) || !center.Standable(map))
                        center = pawn.Position;
                    MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Fight, center, radius: 3f);
                }
                return;
            }
            var occupied = new List<IntVec3>();
            List<IntVec3> sectors = GenRadial.RadialCellsAround(plan.Objective, 11f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && structure.RoomAt(cell) == room
                    && !plan.AvoidedTrapCells.Contains(cell)
                    && cell != plan.BreachInside && cell != plan.BreachCell
                    && cell.DistanceTo(plan.Entry) >= 2f)
                .ToList();
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .Where(value => value.Task == RaidTacticalTask.Entry)
                .OrderBy(value => value.EntryOrder))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || !pawn.Spawned || pawn.Map != map
                    || IsTaserOperation(pawn)) continue;
                ExecutionState securityState = StateFor(plan.UnitId);
                if (securityState?.Contacts.Entries.Any(contact => contact.Confidence(GenTicks.TicksGame) == RaidContactConfidence.Visible
                    && ContactRooms(structure, contact.Position).Contains(room)
                    && contact.Position.DistanceToSquared(pawn.Position) <= 36) == true) continue;
                IntVec3 sector = sectors
                    .Where(cell => occupied.All(other => cell.DistanceTo(other) >= 3f))
                    .OrderByDescending(cell => structure.CachedAt(cell).DoorThreat * 2f
                        + structure.CachedAt(cell).WallThreat
                        + DoorStateScore(cell, knownDoors)
                        + (occupied.Count == 0 ? 0f
                            : occupied.Min(other => cell.DistanceTo(other)) * 0.5f)
                        - cell.DistanceTo(plan.Objective) * 0.2f)
                    .Where(cell => pawn.CanReach(cell,
                        PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!sector.IsValid) continue;
                occupied.Add(sector);
                TryGoto(pawn, sector);
                if (plan.Doctrine == RaidTacticalDoctrine.High)
                {
                    Building_Door focusDoor = RaidLocalMapState.Doors(map, sector, 8f)
                        .Where(door => !door.Destroyed && door.Position != sector
                            && knownDoors.ContainsKey(door.thingIDNumber))
                        .OrderByDescending(door => knownDoors[door.thingIDNumber])
                        .ThenBy(door => door.Position.DistanceToSquared(sector))
                        .FirstOrDefault();
                    if (focusDoor != null)
                        TacticalAimUtility.SetFocusForAI(pawn, focusDoor.Position);
                }
            }
        }

        private bool RoomSecured(RaidTacticalUnit unit, List<Pawn> members,
            RaidTacticalPlan plan)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            int room = structure?.RoomAt(plan.Objective) ?? 0;
            if (room == 0) return false;
            List<Pawn> entry = EntryPawns(members, plan);
            if (entry.Count == 0 || entry.Count(pawn =>
                structure.RoomAt(pawn.Position) == room) * 2 < entry.Count) return false;
            ExecutionState state = StateFor(plan.UnitId);
            int tick = GenTicks.TicksGame;
            return state != null && !RecentRoomContact(state, structure, room, tick)
                && CheckRoomConcern(members, plan, state, room, tick);
        }

        private bool TryPlanNextRoom(RaidTacticalUnit unit,
            List<Pawn> members, RaidTacticalPlan current,
            ExecutionState state, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, current);
            if (structure == null) return false;
            // Deferred room planning keeps this phase active; it is not proof
            // that every remaining room has already been cleared.
            if (!state.ClearedRoomCells.Contains(current.Objective))
            {
                state.ClearedRoomCells.Add(current.Objective);
                PublishRoomCheck(state, current.Objective, tick);
            }
            state.ClearingRooms = true;
            if (structure.RoomAt(current.Objective)
                == structure.RoomAt(state.FinalObjective))
                state.BedSecured = true;
            HashSet<int> cleared = new HashSet<int>(state.ClearedRoomCells
                .Where(cell => cell.InBounds(map)).Select(structure.RoomAt).Where(room => room > 0));
            Pawn observer = members.Where(pawn => structure.RoomAt(pawn.Position) == structure.RoomAt(current.Objective))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(current.Objective)).FirstOrDefault() ?? members[0];
            if (state.LocalCqb == null) state.LocalCqb = new RaidCqbLocalMap(state.CqbKnowledge);
            if (!state.PendingRoomGoal.IsValid)
            {
                if (TryPlanContactRecheck(unit, members, current, state, structure, cleared, observer, tick)) return true;
                state.LocalCqb.Refresh(map, structure, observer, current.Objective, tick, current.AvoidedTrapCells,
                    observed: cell => CanObserveMapCell(members, cell));
                if (state.RoomSearchPlan != current || state.RoomSearchRevision != state.LocalCqb.Revision)
                {
                    state.RoomSearchPlan = current;
                    state.RoomSearchRevision = state.LocalCqb.Revision;
                    state.RoomPlanAttempts.Clear();
                    var rooms = new HashSet<int>(cleared) { structure.RoomAt(observer.Position) };
                    state.RoomSearchTargets = NextRoomTargets(state, structure, observer, cleared, tick)
                        .Where(cell => cell.InBounds(map) && rooms.Add(structure.RoomAt(cell)))
                        .Take(12).ToList();
                }
                if (!state.RoomPlanAttempts.TrySelect(state.RoomSearchTargets, tick,
                        out IntVec3 target, out bool pending)) return pending;
                state.PendingRoomGoal = target;
            }
            IntVec3 requested = state.PendingRoomGoal;
            RaidTacticalPlan next = map.GetComponent<MapComponent_RaidPlanningService>()
                .Request(unit, "next-room", requested, current);
            if (next == null) return true;
            state.PendingRoomGoal = IntVec3.Invalid;
            if (next?.Success != true || cleared.Contains(structure.RoomAt(next.Objective)))
            {
                state.RoomPlanAttempts.Failed(requested, tick);
                return true;
            }
            next.ObjectiveIsIntermediate = true;
            ActivateNextRoomPlan(unit, members, state, next, tick);
            return true;
        }

        private IEnumerable<IntVec3> NextRoomTargets(ExecutionState state,
            RaidStructureSnapshot structure, Pawn observer, HashSet<int> cleared, int tick)
        {
            foreach (IntVec3 target in state.LocalCqb.NeighborTargets(observer.Position, cleared)
                .OrderBy(cell => structure.RoomAt(cell) == structure.RoomAt(state.FinalObjective) ? 0 : 1)
                .ThenBy(cell => cell.DistanceToSquared(observer.Position)).Take(8)) yield return target;
            if (!state.BedSecured && state.FinalObjective.InBounds(map)) yield return state.FinalObjective;
            IEnumerable<IntVec3> anchors = structure.ObjectiveAnchors
                .Concat(state.Contacts.Entries.Where(contact => contact.Confidence(tick) <= RaidContactConfidence.Area)
                    .Select(contact => contact.Position))
                .Where(cell => cell.DistanceTo(observer.Position) <= RaidCqbLocalMap.Radius);
            foreach (IGrouping<int, IntVec3> group in anchors
                .Where(cell => cell.InBounds(map))
                .GroupBy(structure.RoomAt)
                .Where(group => group.Key > 0
                    && !cleared.Contains(group.Key))
                .Take(12))
            {
                IntVec3 target = group.SelectMany(anchor =>
                        GenRadial.RadialCellsAround(anchor, 6f, true))
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && structure.RoomAt(cell) == group.Key)
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                yield return target;
            }
        }

        private static void ActivateNextRoomPlan(RaidTacticalUnit unit,
            List<Pawn> members, ExecutionState state, RaidTacticalPlan next, int tick)
        {
            CancelOpeningObservation(state);
            state.Observation = null;
            state.ContactGuards.Clear(); state.ContactPause = false;
            state.ActivePlan = next;
            state.Objective = next.Objective;
            state.PendingRoomGoal = state.PendingRecheckGoal = IntVec3.Invalid;
            state.Maneuver = next.Selected.Maneuver;
            state.PlanKey = PlanKey(unit, members);
            state.ReadySince = -1;
            state.ApproachComplete = false;
            state.CurrentNode = 0;
            state.NodeMembers.Clear();
            state.ApproachProgressTick = tick;
            state.ApproachBestRemaining = float.MaxValue;
            state.BreachAttempts = 0;
            state.Breacher = null;
            state.BreachTarget = null;
            state.BreachKind = RaidBreachKind.None;
            state.WithdrawalIssued = false;
            state.Thrower = null;
            state.CrossingBreach = IntVec3.Invalid;
            state.Crossings.Clear();
            state.SupportIssued = false;
            state.SupportLaunched = false;
            state.SupportReturnRequired = false;
            state.SupportProjectile = null;
            state.SupportProjectileDef = null;
            state.SupportEffectsClearedTick = -1;
            state.SupportStatus = "Not requested";
            state.FlankIssued = false;
            state.AssaultIssued = false;
            state.ExternalSupportAttempted = true;
            Advance(state, RaidExecutionPhase.Assemble, tick);
            RaidTacticalSpeech.Say(Commander(unit, members),
                "HD_RaidTactical_Assemble");
        }

        private string NearbyDoorState(List<Pawn> members, IntVec3 objective, string previous)
        {
            return RaidLocalMapState.DoorSignature(map, objective, previous, cell => CanObserveMapCell(members, cell));
        }

        private bool TryCounterClosingDoor(List<Pawn> members,
            RaidTacticalPlan plan, string previousState)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            int room = structure?.RoomAt(plan.Objective) ?? 0;
            if (room == 0) return false;
            HashSet<string> openIds = new HashSet<string>((previousState ?? "")
                .Split(',').Where(value => value.EndsWith(":1"))
                .Select(value => value.Substring(0, value.Length - 2)));
            foreach (Building_Door door in RaidLocalMapState.Doors(map, plan.Objective, 12f)
                .Where(value => !value.Open
                    && openIds.Contains(value.thingIDNumber.ToString())
                    && CanObserveMapCell(members, value.Position)))
            {
                ExecutionState state = StateFor(plan.UnitId);
                bool enemyOutside = state?.Contacts.Entries.Any(contact =>
                    contact.Confidence(GenTicks.TicksGame) <= RaidContactConfidence.Recent
                    && contact.Position.DistanceToSquared(door.Position) <= 16
                    && contact.Room != room) == true;
                if (!enemyOutside) continue;
                IntVec3 target = new[] { IntVec3.North, IntVec3.East,
                        IntVec3.South, IntVec3.West }
                    .Select(direction => door.Position + direction)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && structure.RoomAt(cell) == room)
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                foreach (Pawn pawn in members.OrderBy(value =>
                    value.Position.DistanceToSquared(target)))
                {
                    if (IsTaserOperation(pawn)) continue;
                    Thing smoke = InventoryGrenadeUtility.GrenadeStacks(pawn)
                        .FirstOrDefault(item => item.def.defName == "HD_Grenade_M8_Item"
                            || item.def.weaponTags?.Contains("GrenadeSmoke") == true);
                    if (smoke == null) continue;
                    bool close = InventoryGrenadeUtility.CanThrowAt(pawn, target,
                        InventoryGrenadeUtility.CloseThrowRange);
                    if (!close && !InventoryGrenadeUtility.CanThrowAt(pawn, target,
                        InventoryGrenadeUtility.NormalThrowRange)) continue;
                    if (!RaidGrenadePreparation.Start(pawn, smoke, target, pawn.Position, close)) continue;
                    RaidTacticalSpeech.Say(pawn, "HD_RaidTactical_Smoke");
                    return true;
                }
            }
            return false;
        }

        private float DoorStateScore(IntVec3 cell, Dictionary<int, bool> knownDoors)
        {
            IntVec3[] directions = { IntVec3.North, IntVec3.East,
                IntVec3.South, IntVec3.West };
            float score = 0f;
            foreach (IntVec3 direction in directions)
            {
                IntVec3 adjacent = cell + direction;
                if (!adjacent.InBounds(map)
                    || !(adjacent.GetEdifice(map) is Building_Door door)) continue;
                score = Math.Max(score, knownDoors.TryGetValue(door.thingIDNumber, out bool open) && open ? 8f : 4f);
            }
            return score;
        }

    }

}
