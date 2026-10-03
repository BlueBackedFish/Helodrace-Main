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
            public int ApproachProgressTick;
            public float ApproachBestRemaining = float.MaxValue;
            public int BreachAttempts;
            public Pawn Breacher;
            public Building BreachTarget;
            public RaidBreachKind BreachKind;
            public bool WithdrawalIssued;
            public Pawn Thrower;
            public bool SupportIssued;
            public bool SupportLaunched;
            public bool SupportReturnRequired;
            public Projectile SupportProjectile;
            public ThingDef SupportProjectileDef;
            public int SupportEffectsClearedTick = -1;
            public string SupportStatus = "Not requested";
            public bool ApproachSmokeActive;
            public bool ApproachSmokeLaunched;
            public Pawn ApproachSmokeThrower;
            public Projectile ApproachSmokeProjectile;
            public IntVec3 ApproachSmokeTarget = IntVec3.Invalid;
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
            public int LastRoomPlanTick = -600;
            public int LastDoorResponseTick;
            public IntVec3 CrossingBreach = IntVec3.Invalid;
            public List<BreachCrossing> Crossings = new List<BreachCrossing>();
            // Persist committed positions together with the execution progress.
            public RaidTacticalPlan ActivePlan;

            public void ExposeData()
            {
                Scribe_Values.Look(ref OrganizationId, "organizationId");
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
                Scribe_Values.Look(ref ApproachProgressTick, "approachProgressTick");
                Scribe_Values.Look(ref ApproachBestRemaining, "approachBestRemaining",
                    float.MaxValue);
                Scribe_Values.Look(ref BreachAttempts, "breachAttempts");
                Scribe_References.Look(ref Breacher, "breacher");
                Scribe_References.Look(ref BreachTarget, "breachTarget");
                Scribe_Values.Look(ref BreachKind, "breachKind");
                Scribe_Values.Look(ref WithdrawalIssued, "withdrawalIssued");
                Scribe_References.Look(ref Thrower, "thrower");
                Scribe_Values.Look(ref SupportIssued, "supportIssued");
                Scribe_Values.Look(ref SupportLaunched, "supportLaunched");
                Scribe_Values.Look(ref SupportReturnRequired, "supportReturnRequired");
                Scribe_References.Look(ref SupportProjectile, "supportProjectile");
                Scribe_Defs.Look(ref SupportProjectileDef, "supportProjectileDef");
                Scribe_Values.Look(ref SupportEffectsClearedTick, "supportEffectsClearedTick", -1);
                Scribe_Values.Look(ref SupportStatus, "supportStatus", "Not requested");
                Scribe_Values.Look(ref ApproachSmokeActive, "approachSmokeActive");
                Scribe_Values.Look(ref ApproachSmokeLaunched, "approachSmokeLaunched");
                Scribe_References.Look(ref ApproachSmokeThrower, "approachSmokeThrower");
                Scribe_References.Look(ref ApproachSmokeProjectile, "approachSmokeProjectile");
                Scribe_Values.Look(ref ApproachSmokeTarget, "approachSmokeTarget", IntVec3.Invalid);
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
                Scribe_Values.Look(ref LastRoomPlanTick, "lastRoomPlanTick", -600);
                Scribe_Values.Look(ref LastDoorResponseTick,
                    "lastDoorResponseTick");
                Scribe_Values.Look(ref CrossingBreach, "crossingBreach", IntVec3.Invalid);
                Scribe_Collections.Look(ref Crossings, "crossings", LookMode.Deep);
                Scribe_Deep.Look(ref ActivePlan, "activePlan");
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
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                states.Clear();
                foreach (ExecutionState state in savedStates ?? new List<ExecutionState>())
                    if (state?.OrganizationId != null) states[state.OrganizationId] = state;
                savedStates = null;
            }
        }

        public string Status(string organizationId)
        {
            if (organizationId == null || !states.TryGetValue(organizationId,
                out ExecutionState state)) return "Inactive";
            if (state.Phase == RaidExecutionPhase.Assemble && !state.ApproachComplete)
                return "Approach";
            return state.Phase.ToString();
        }

        public RaidTacticalPlan ActivePlanFor(string organizationId)
        {
            if (organizationId == null || !states.TryGetValue(organizationId, out ExecutionState state)
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
        {
            string id = OrganizationAPI.GetOrganization(pawn)?.id;
            return id != null && IsTacticalRaider(pawn)
                && states.TryGetValue(id, out ExecutionState state)
                && state.ActivePlan?.Success == true
                && state.ActivePlan.Assignments.Any(assignment => assignment.Pawn == pawn);
        }

        internal string SupportStatusFor(string id) => states.TryGetValue(id, out ExecutionState state)
            ? state.SupportStatus : "Inactive";

        internal ExecutionState StateFor(string id) => id != null && states.TryGetValue(id,
            out ExecutionState state) ? state : null;

        internal void NotifySupportLaunched(Pawn pawn, Projectile projectile)
        {
            string id = OrganizationAPI.GetOrganization(pawn)?.id;
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
            string id = OrganizationAPI.GetOrganization(pawn)?.id;
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
            bool preparing = state.Thrower?.CurJobDef?.defName == "HD_ThrowInventoryGrenadeClose"
                || state.Thrower?.CurJobDef?.defName == "HD_ThrowInventoryGrenadeNormal";
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
        {
            ThingDef projectile = grenade?.def?.projectileWhenLoaded;
            if (projectile?.projectile == null) return false;
            if (RaidSmokeUtility.IsSmoke(grenade)) return true;
            float scatter = InventoryGrenadeUtility.ThrowMissRadius(pawn, close,
                pawn.Position.DistanceTo(target));
            FragmentationGrenadeExtension fragments = projectile.GetModExtension<FragmentationGrenadeExtension>();
            float fragmentRadius = fragments == null ? 0f : Math.Max(fragments.radius,
                fragments.longRangeFragmentFraction > 0f ? fragments.longRangeRadius : 0f);
            float radius = Math.Max(projectile.projectile.explosionRadius, fragmentRadius)
                + scatter + 0.75f;
            return !pawn.Map.mapPawns.AllPawnsSpawned.Any(ally => !ally.Dead
                && !ally.HostileTo(pawn) && ally.Position.DistanceTo(target) <= radius
                && (ally.Position.DistanceTo(target) <= scatter + 0.75f
                    || GenSight.LineOfSight(target, ally.Position, pawn.Map, true)));
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick % 10 != 0) return;
            GameComponent_CombatOrganizations registry = OrganizationAPI.Registry;
            MapComponent_RaidTacticalPlans plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            if (registry == null || plans == null) return;

            // Only the lightweight committed crossing runs more frequently.
            // Planning, room scanning and general organization updates stay at
            // their original cadence.
            if (tick % 30 != 0)
            {
                foreach (ExecutionState crossing in states.Values.ToList())
                {
                    if (crossing.Phase != RaidExecutionPhase.CrossBreach
                        || crossing.ActivePlan?.Success != true) continue;
                    CombatOrganization organization = registry.Organizations
                        .FirstOrDefault(value => value.id == crossing.OrganizationId);
                    if (organization == null) continue;
                    List<Pawn> members = organization.AllMembers.Where(pawn => pawn.Spawned
                        && pawn.Map == map && !pawn.Dead && !pawn.Downed && !pawn.Destroyed
                        && IsTacticalRaider(pawn)).ToList();
                    if (members.Count > 0)
                        Update(organization, members, crossing.ActivePlan, crossing, tick);
                }
                return;
            }

            var activeIds = new HashSet<string>();
            foreach (CombatOrganization organization in registry.Organizations)
            {
                List<Pawn> members = organization.AllMembers
                    .Where(pawn => pawn.Spawned && pawn.Map == map && !pawn.Dead
                        && !pawn.Downed && !pawn.Destroyed && IsTacticalRaider(pawn))
                    .ToList();
                if (members.Count == 0)
                {
                    if (tick % 90 == 0) KeepSapperEscortTogether(organization);
                    continue;
                }
                activeIds.Add(organization.id);
                if (!IsDefendingRaider(members[0]))
                    RaidBreachToolRecovery.TryStart(members, organization.AllMembers, map);
                if (states.TryGetValue(organization.id, out ExecutionState completed)
                    && TryExitSecuredObjective(organization, members, completed, tick))
                {
                    states.Remove(organization.id);
                    continue;
                }
                states.TryGetValue(organization.id, out ExecutionState state);
                string key = PlanKey(organization, members);
                if (state?.ActivePlan?.Success == true && state.Phase != RaidExecutionPhase.Hold
                    && state.Phase != RaidExecutionPhase.Complete)
                    AssignLateMembers(members, state.ActivePlan);
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
                        : RaidTacticalPlanner.MakePlan(map, organization, state.Objective)
                    : state?.ActivePlan?.Success == true && state.PlanKey == key
                        && state.Phase != RaidExecutionPhase.Hold
                        && state.Phase != RaidExecutionPhase.Complete
                        ? state.ActivePlan : plans.GetPlan(organization);
                if (plan?.Success != true)
                {
                    if (tick % 90 == 0) KeepSapperEscortTogether(organization);
                    if (states.TryGetValue(organization.id, out ExecutionState abandoned))
                    {
                        CancelPendingCharge(abandoned);
                        states.Remove(organization.id);
                    }
                    continue;
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
                        OrganizationId = organization.id,
                        PlanKey = key,
                        Objective = plan.Objective,
                        FinalObjective = previous?.FinalObjective.IsValid == true
                            ? previous.FinalObjective : plan.FinalObjective,
                        ClearingRooms = previous?.ClearingRooms == true,
                        BedSecured = previous?.BedSecured == true,
                        ClearedRoomCells = previous?.ClearedRoomCells
                            ?? new List<IntVec3>(),
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
                    states[organization.id] = state;
                    RaidTacticalSpeech.Say(Commander(organization, members),
                        "HD_RaidTactical_Assemble");
                }
                if (state.ActivePlan == null) state.ActivePlan = plan;
                else if (idle && !ReferenceEquals(state.ActivePlan, plan))
                {
                    state.ActivePlan = plan;
                    state.Objective = plan.Objective;
                    state.ReadySince = -1;
                }
                Update(organization, members, state.ActivePlan, state, tick);
                if (state.Phase == RaidExecutionPhase.Assemble && !state.ApproachComplete
                    && tick - Math.Max(state.ApproachProgressTick, state.PhaseStarted)
                        >= ApproachStallTimeout)
                {
                    if (state.ActivePlan.SafeStackCells.Count > 0)
                    {
                        RetargetBlockedStackMembers(members, state.ActivePlan);
                        state.ApproachProgressTick = tick;
                        state.ApproachBestRemaining = float.MaxValue;
                    }
                    else
                    {
                        plans.GetPlan(organization, true);
                        states.Remove(organization.id);
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
                        plans.GetPlan(organization, true);
                        states.Remove(organization.id);
                    }
                }
            }
            foreach (string id in states.Keys.Where(id => !activeIds.Contains(id)).ToList())
            {
                CancelPendingCharge(states[id]);
                states.Remove(id);
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
                    Task = RaidTacticalTask.Security, Position = cell });
                MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Move, cell);
                MapComponent_RaidTacticalTrace.Record(pawn, $"Late member joins security at {cell}");
            }
        }

        private void KeepSapperEscortTogether(CombatOrganization organization)
        {
            if (organization.faction == Faction.OfPlayer) return;
            List<Pawn> members = organization.AllMembers.Where(pawn => pawn.Spawned
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

            CombatGroup group = organization.AllGroups
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

        private static string PlanKey(CombatOrganization organization, List<Pawn> members)
        {
            return string.Join(",", members.Select(pawn => pawn.thingIDNumber).OrderBy(id => id))
                + ":" + string.Join(",", organization.AllGroups.Select(group =>
                    (group.EffectiveCommander?.thingIDNumber ?? -1) + ":"
                    + (int)(group.CommandEfficiency * 100f)));
        }

        private static Pawn Commander(CombatOrganization organization, List<Pawn> members)
        {
            return organization.rootGroups.Select(root => root.EffectiveCommander)
                .FirstOrDefault(members.Contains) ?? members[0];
        }

        private bool TryExitSecuredObjective(CombatOrganization organization,
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
            if (map.mapPawns.AllPawnsSpawned.Any(enemy => !enemy.Dead
                && !enemy.Downed && enemy.Faction != null
                && enemy.Faction.HostileTo(organization.faction)
                && (enemy.Position.DistanceTo(objective) <= dangerRadius
                    || (indoors && structure.RoomAt(enemy.Position) == room)))) return false;

            Lord lord = members[0].GetLord();
            if (lord == null || members.Any(pawn => pawn.GetLord() != lord)) return false;
            RaidTacticalSpeech.Say(Commander(organization, members),
                "HD_RaidTactical_Withdraw");
            lord.SetJob(new LordJob_ExitMapBest(LocomotionUrgency.Sprint, true, true));
            return true;
        }

        private void Update(CombatOrganization organization, List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
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
                        TryRequestExternalSupport(organization, members, plan, state);
                    }
                    if (WaitingForExternalSupport(state, members, tick))
                    {
                        foreach (Pawn pawn in members)
                            if ((pawn.CurJobDef != JobDefOf.Wait_Combat
                                    || pawn.CurJob?.expiryInterval <= 0)
                                && !IsTaserOperation(pawn)
                                && map.GetComponent<MapComponent_HelodCasSupport>()
                                    ?.RequiresStationaryGuidance(pawn) != true)
                                HoldPosition(pawn);
                        break;
                    }
                    if (ApproachScreen(members, plan, state, tick)) break;
                    if (!FollowApproach(members, plan, state, tick)) break;
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
                    if (RaidBreachToolRecovery.Pending(members)
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
                        && BreachOpened(plan.PlannedBreach ?? state.BreachTarget))
                    {
                        FinishBreachAttempt(plan, state, tick);
                        break;
                    }
                    if (state.Breacher == null && state.BreachTarget == null)
                    {
                        if (plan.PlannedBreach != null && BreachOpened(plan.PlannedBreach))
                            Advance(state, RaidExecutionPhase.Support, tick);
                        else if (!TryStartBreach(members, plan, state))
                        {
                            if (plan.PlannedBreach == null || BreachOpened(plan.PlannedBreach))
                                Advance(state, RaidExecutionPhase.Support, tick);
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
                        Advance(state, RaidExecutionPhase.WithdrawFromCharge, tick);
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
                case RaidExecutionPhase.Support:
                    MaintainStack(members, plan, state.Thrower);
                    if (state.SupportLaunched) ReturnSupportThrower(state);
                    if (plan.BreachCell.IsValid && state.Thrower == null
                        && !AllReady(members, plan)
                        && tick - state.PhaseStarted < 600) break;
                    if (!state.SupportIssued)
                    {
                        Pawn thrower = TryStartSupport(members, plan,
                            state.Maneuver, state.Thrower);
                        if (thrower != null)
                        {
                            state.Thrower = thrower;
                            state.SupportIssued = true;
                            state.SupportStatus = "Throw preparation";
                        }
                        else if (tick - state.PhaseStarted >= SupportTimeout
                            || !TryStageEntryThrower(members, plan, state))
                        {
                            state.SupportStatus = "Skipped: no usable safe throw, equipment, or support timeout";
                            state.SupportReturnRequired = plan.BreachCell.IsValid && state.Thrower != null;
                            Advance(state, RaidExecutionPhase.EntryWait, tick);
                        }
                    }
                    else if ((state.Thrower?.CurJobDef?.defName != "HD_ThrowInventoryGrenadeClose"
                        && state.Thrower?.CurJobDef?.defName != "HD_ThrowInventoryGrenadeNormal")
                        || tick - state.PhaseStarted >= SupportTimeout)
                    {
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
                    if (!BreachOpened(plan.PlannedBreach))
                    {
                        state.Breacher = null;
                        state.BreachTarget = null;
                        Advance(state, RaidExecutionPhase.Breach, tick);
                        break;
                    }
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
                        RaidTacticalSpeech.Say(Commander(organization, members),
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
                        IssueRoomSecurity(members, plan);
                        state.DoorStateSignature = NearbyDoorState(plan.Objective);
                        state.LastRoomSecurityTick = tick;
                        Advance(state, RaidExecutionPhase.SecureRoom, tick);
                    }
                    break;
                case RaidExecutionPhase.SecureRoom:
                    if (tick - state.LastRoomSecurityTick >= 30)
                    {
                        string doors = NearbyDoorState(plan.Objective);
                        if (doors != state.DoorStateSignature)
                        {
                            IssueRoomSecurity(members, plan);
                            if (tick - state.LastDoorResponseTick >= 360
                                && TryCounterClosingDoor(members, plan,
                                    state.DoorStateSignature))
                                state.LastDoorResponseTick = tick;
                            state.DoorStateSignature = doors;
                            state.LastRoomSecurityTick = tick;
                        }
                    }
                    if (tick - state.PhaseStarted >= 180
                        && tick - state.LastRoomPlanTick >= 600
                        && RoomSecured(organization, members, plan)
                        && (state.ClearingRooms || plan.ObjectiveIsNamedBed
                            || plan.ObjectiveIsIntermediate))
                    {
                        state.LastRoomPlanTick = tick;
                        if (!TryPlanNextRoom(organization, members, plan, state, tick))
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
                        MaintainEntryCohesion(organization, members, plan);
                    break;
            }
        }

        private void MaintainEntryCohesion(CombatOrganization organization,
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
                CombatGroup group = organization.AllGroups
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

        private static void FinishBreachAttempt(RaidTacticalPlan plan,
            ExecutionState state, int tick)
        {
            if (plan.PlannedBreach == null || BreachOpened(plan.PlannedBreach))
            {
                state.Breacher = null;
                state.BreachTarget = null;
                state.BreachKind = RaidBreachKind.None;
                Advance(state, RaidExecutionPhase.Support, tick);
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
            return plan.Assignments.Where(assignment => assignment.Task == RaidTacticalTask.Entry
                && members.Contains(assignment.Pawn))
                .OrderBy(assignment => assignment.EntryOrder)
                .Select(assignment => assignment.Pawn).ToList();
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
                    plan.SafeSupportCells.Add(assignment.Pawn.Position);
                    HoldPosition(assignment.Pawn);
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
                    || !crossing.Destination.Standable(map))
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

        private static bool FollowApproach(List<Pawn> members, RaidTacticalPlan plan,
            ExecutionState state, int tick)
        {
            if (state.ApproachComplete) return true;
            List<RaidTacticalAssignment> group = plan.Assignments
                .Where(assignment => assignment.Task != RaidTacticalTask.Withdraw
                    && members.Contains(assignment.Pawn))
                .ToList();
            if (group.Count == 0)
            {
                state.ApproachComplete = true;
                return true;
            }
            float remaining = 0f;
            foreach (RaidTacticalAssignment assignment in group)
            {
                Pawn pawn = assignment.Pawn;
                float distance = AtStagingPosition(assignment, plan) ? 0f
                    : pawn.Position.DistanceTo(assignment.Position);
                remaining += distance;
                if (distance <= 9f || IsTaserOperation(pawn)) continue;
                TryGoto(pawn, assignment.Position);
            }
            if (remaining + 1f < state.ApproachBestRemaining)
            {
                state.ApproachBestRemaining = remaining;
                state.ApproachProgressTick = tick;
            }
            if (group.All(assignment => AtStagingPosition(assignment, plan)
                    || assignment.Pawn.Position.DistanceTo(assignment.Position) <= 9f))
            {
                state.ApproachComplete = true;
                state.PhaseStarted = tick;
                state.ReadySince = -1;
                return true;
            }
            return false;
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
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || !assignment.Position.IsValid
                    || (skipResponse && assignment.Task == RaidTacticalTask.Response)) continue;
                if (IsTaserOperation(pawn)) continue;
                ExecutionState state = StateFor(plan.OrganizationId);
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
                        TcccUtility.Start(pawn, pawn, TcccTreatment.SelfHemostasis);
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
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
            {
                Pawn pawn = assignment.Pawn;
                if (pawn == exempt || !members.Contains(pawn)
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

        private void TryRequestExternalSupport(CombatOrganization organization,
            List<Pawn> members, RaidTacticalPlan plan, ExecutionState state)
        {
            if (StructureFor(map, plan)?.IsIndoor(plan.Objective) == true
                || Find.WorldObjects == null) return;
            List<HelodForwardBase> bases = Find.WorldObjects.AllWorldObjects
                .OfType<HelodForwardBase>()
                .Where(value => value.Faction == organization.faction).ToList();
            if (bases.Count == 0) return;
            Pawn target = map.mapPawns.AllPawnsSpawned
                .Where(enemy => !enemy.Dead && !enemy.Downed
                    && enemy.Faction != null && enemy.Faction.HostileTo(organization.faction)
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
            return plan.Assignments.Where(assignment => members.Contains(assignment.Pawn)
                    && assignment.Task != RaidTacticalTask.Withdraw)
                .All(assignment => AtStagingPosition(assignment, plan));
        }

        private static bool AtStagingPosition(RaidTacticalAssignment assignment,
            RaidTacticalPlan plan)
        {
            Pawn pawn = assignment.Pawn;
            if (!plan.BreachCell.IsValid
                || assignment.Task == RaidTacticalTask.Withdraw)
                return pawn.Position.DistanceTo(assignment.Position) <= 1.5f;
            return assignment.Task == RaidTacticalTask.Entry
                ? plan.SafeStackCells.Count > 0
                    ? plan.SafeStackCells.Contains(pawn.Position)
                    : pawn.Position.DistanceTo(assignment.Position) <= 1.5f
                : plan.SafeSupportCells.Count > 0
                    ? plan.SafeSupportCells.Contains(pawn.Position)
                    : pawn.Position.DistanceTo(assignment.Position) <= 3f;
        }

        private static void RetargetBlockedStackMembers(List<Pawn> members,
            RaidTacticalPlan plan, bool onlyBlocked = false)
        {
            var occupied = new HashSet<IntVec3>(members.Select(pawn => pawn.Position));
            var reserved = new HashSet<IntVec3>(plan.Assignments
                .Where(assignment => members.Contains(assignment.Pawn)
                    && assignment.Position.IsValid)
                .Select(assignment => assignment.Position));
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn)
                    || assignment.Task == RaidTacticalTask.Withdraw
                    || AtStagingPosition(assignment, plan)) continue;
                if (onlyBlocked && assignment.Position.IsValid
                    && assignment.Position.InBounds(pawn.Map) && assignment.Position.Standable(pawn.Map)
                    && (MapComponent_RaidTacticalOrders.For(pawn)?.RetryAfter ?? 0) <= GenTicks.TicksGame) continue;
                reserved.Remove(assignment.Position);
                IEnumerable<IntVec3> safeCells = assignment.Task == RaidTacticalTask.Entry
                    ? (IEnumerable<IntVec3>)plan.SafeStackCells
                    : plan.SafeSupportCells;
                foreach (IntVec3 cell in safeCells
                    .Where(cell => cell != assignment.Position
                        && cell.InBounds(pawn.Map) && cell.Standable(pawn.Map)
                        && !occupied.Contains(cell) && !reserved.Contains(cell))
                    .OrderBy(cell => cell.DistanceToSquared(pawn.Position))
                    .Take(32))
                {
                    if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                        continue;
                    assignment.Position = cell;
                    occupied.Add(cell);
                    break;
                }
                reserved.Add(assignment.Position);
            }
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
            RaidTacticalManeuver maneuver, Pawn preferred = null)
        {
            bool entry = maneuver == RaidTacticalManeuver.CoordinatedEntry;
            Map currentMap = members[0].Map;
            bool entrySmoke = RaidSmokePolicy.EntrySmoke(entry, RaidSmokeUtility.ExteriorEntry(currentMap, plan));
            bool smoke = maneuver == RaidTacticalManeuver.SmokeAdvance || entrySmoke;
            bool fieldGrenade = maneuver == RaidTacticalManeuver.FieldGrenade;
            if (!smoke && !entry && !fieldGrenade) return null;
            RaidStructureSnapshot structure = StructureFor(currentMap, plan);
            int objectiveRoom = structure?.RoomAt(plan.Objective) ?? 0;
            if (entry && !smoke && plan.Doctrine == RaidTacticalDoctrine.Low
                && currentMap.mapPawns.AllPawnsSpawned.Any(pawn => pawn.Faction == members[0].Faction
                    && (plan.BreachCell.IsValid
                        ? PastBreach(pawn, plan)
                        : objectiveRoom > 0
                            && structure.RoomAt(pawn.Position) == objectiveRoom))) return null;
            IEnumerable<IntVec3> targets = entrySmoke ? EntrySmokeTargets(currentMap, plan, structure) : smoke
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
                : EntryGrenadeTargets(currentMap, plan, structure, objectiveRoom);
            foreach (Pawn pawn in members.OrderBy(value => value == preferred ? -1
                : entry && !smoke && plan.Doctrine == RaidTacticalDoctrine.Low
                    && InventoryGrenadeUtility.GrenadeStacks(value)
                        .Any(item => item.def.defName == "HD_Grenade_MKIII") ? 0 : 1))
            {
                if (IsTaserOperation(pawn)) continue;
                Thing grenade = InventoryGrenadeUtility.GrenadeStacks(pawn)
                    .FirstOrDefault(item => IsSupportGrenade(item, plan, smoke));
                if (grenade == null) continue;
                foreach (IntVec3 target in targets)
                {
                    if (!target.IsValid || !target.InBounds(currentMap)) continue;
                    if (!smoke && !GrenadeTargetSafe(currentMap, pawn, target))
                        continue;
                    bool close = InventoryGrenadeUtility.CanThrowAt(pawn, target,
                        InventoryGrenadeUtility.CloseThrowRange);
                    if (!close && !InventoryGrenadeUtility.CanThrowAt(pawn, target,
                        InventoryGrenadeUtility.NormalThrowRange)) continue;
                    if (!SafeSupportThrow(pawn, grenade, target, close)) continue;
                    JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(close
                        ? "HD_ThrowInventoryGrenadeClose" : "HD_ThrowInventoryGrenadeNormal");
                    if (jobDef == null) return null;
                    RaidTacticalSpeech.Say(pawn, smoke
                        ? "HD_RaidTactical_Smoke" : "HD_RaidTactical_Grenade");
                    pawn.jobs.StartJob(JobMaker.MakeJob(jobDef, target, grenade),
                        JobCondition.InterruptForced);
                    return pawn;
                }
            }
            return null;
        }

        private static IEnumerable<IntVec3> EntryGrenadeTargets(Map map,
            RaidTacticalPlan plan, RaidStructureSnapshot structure, int objectiveRoom)
        {
            if (!plan.BreachCell.IsValid)
                return GenRadial.RadialCellsAround(plan.Entry, 7f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && structure != null && objectiveRoom > 0
                        && structure.RoomAt(cell) == objectiveRoom
                        && cell.DistanceTo(plan.Entry) >= 2f)
                    .OrderBy(cell => cell.DistanceTo(plan.Entry));
            IntVec3 inward = plan.BreachInside - plan.BreachCell;
            return GenRadial.RadialCellsAround(plan.BreachInside, 7f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && GenSight.LineOfSight(plan.BreachInside, cell, map, true)
                    && (cell.x - plan.BreachCell.x) * inward.x
                        + (cell.z - plan.BreachCell.z) * inward.z >= 3)
                .OrderBy(cell => cell.DistanceTo(plan.BreachInside));
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
                    && (room == 0 || structure.RoomAt(cell) == room)
                    && GenSight.LineOfSight(plan.BreachInside, cell, map, true)
                    && (cell.x - plan.BreachCell.x) * inward.x
                        + (cell.z - plan.BreachCell.z) * inward.z >= 1
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

        private static bool TryStageEntryThrower(List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state)
        {
            if (!plan.BreachCell.IsValid
                || state.Maneuver != RaidTacticalManeuver.CoordinatedEntry)
                return false;
            Map map = members[0].Map;
            bool smoke = RaidSmokePolicy.EntrySmoke(true, RaidSmokeUtility.ExteriorEntry(map, plan));
            if (!smoke && plan.Doctrine == RaidTacticalDoctrine.Low
                && map.mapPawns.AllPawnsSpawned.Any(pawn =>
                    pawn.Faction == members[0].Faction && PastBreach(pawn, plan)))
                return false;
            if (state.Thrower != null)
            {
                if (!members.Contains(state.Thrower)
                    || state.Thrower.Position == plan.Entry) return false;
                TryGoto(state.Thrower, plan.Entry);
                return true;
            }
            RaidStructureSnapshot structure = StructureFor(map, plan);
            List<IntVec3> targets = (smoke ? EntrySmokeTargets(map, plan, structure)
                : EntryGrenadeTargets(map, plan,
                    structure, structure?.RoomAt(plan.Objective) ?? 0)).ToList();
            foreach (Pawn pawn in members)
            {
                if (IsTaserOperation(pawn)
                    || !InventoryGrenadeUtility.GrenadeStacks(pawn)
                        .Any(item => IsSupportGrenade(item, plan, smoke))
                    || !pawn.CanReach(plan.Entry, PathEndMode.OnCell, Danger.Deadly))
                    continue;
                if (!targets.Any(target => plan.Entry.DistanceTo(target)
                        <= InventoryGrenadeUtility.NormalThrowRange
                    && GenSight.LineOfSight(plan.Entry, target, map, true)
                    && (smoke || GrenadeTargetSafe(map, pawn, target)))) continue;
                state.Thrower = pawn;
                TryGoto(pawn, plan.Entry);
                return true;
            }
            return false;
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
            if (plan.BreachCell.IsValid && structure.RoomAt(plan.BreachInside) == room)
            {
                ExecutionState state = StateFor(plan.OrganizationId);
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
                if (map.mapPawns.AllPawnsSpawned.Any(enemy => !enemy.Dead
                    && enemy.Faction != null && enemy.Faction.HostileTo(pawn.Faction)
                    && structure.RoomAt(enemy.Position) == room
                    && enemy.Position.DistanceTo(pawn.Position) <= 6f)) continue;
                IntVec3 sector = sectors
                    .Where(cell => occupied.All(other => cell.DistanceTo(other) >= 3f))
                    .OrderByDescending(cell => structure.CachedAt(cell).DoorThreat * 2f
                        + structure.CachedAt(cell).WallThreat
                        + DoorStateScore(cell)
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
                    Building_Door focusDoor = map.listerThings.AllThings
                        .OfType<Building_Door>()
                        .Where(door => !door.Destroyed && door.Position != sector
                            && door.Position.DistanceTo(sector) <= 8f)
                        .OrderByDescending(door => door.Open)
                        .ThenBy(door => door.Position.DistanceToSquared(sector))
                        .FirstOrDefault();
                    if (focusDoor != null)
                        TacticalAimUtility.SetFocusForAI(pawn, focusDoor.Position);
                }
            }
        }

        private bool RoomSecured(CombatOrganization organization, List<Pawn> members,
            RaidTacticalPlan plan)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            int room = structure?.RoomAt(plan.Objective) ?? 0;
            if (room == 0) return false;
            List<Pawn> entry = EntryPawns(members, plan);
            if (entry.Count == 0 || entry.Count(pawn =>
                structure.RoomAt(pawn.Position) == room) * 2 < entry.Count) return false;
            return !map.mapPawns.AllPawnsSpawned.Any(pawn => !pawn.Dead
                && !pawn.Downed && pawn.Faction != null
                && pawn.Faction.HostileTo(organization.faction)
                && structure.RoomAt(pawn.Position) == room);
        }

        private bool TryPlanNextRoom(CombatOrganization organization,
            List<Pawn> members, RaidTacticalPlan current,
            ExecutionState state, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, current);
            if (structure == null) return false;
            if (!state.ClearedRoomCells.Contains(current.Objective))
                state.ClearedRoomCells.Add(current.Objective);
            state.ClearingRooms = true;
            if (structure.RoomAt(current.Objective)
                == structure.RoomAt(state.FinalObjective))
                state.BedSecured = true;
            if (!state.BedSecured && state.FinalObjective.IsValid)
            {
                RaidTacticalPlan bedPlan = RaidTacticalPlanner.MakePlan(map,
                    organization, state.FinalObjective);
                if (bedPlan?.Success == true
                    && structure.RoomAt(bedPlan.Objective)
                        != structure.RoomAt(current.Objective))
                {
                    ActivateNextRoomPlan(organization, members, state,
                        bedPlan, tick);
                    return true;
                }
            }
            IntVec3 start = members[0].Position;
            IEnumerable<IntVec3> anchors = structure.ObjectiveAnchors
                .Concat(map.mapPawns.AllPawnsSpawned
                    .Where(pawn => !pawn.Dead && pawn.Faction != null
                        && pawn.Faction.HostileTo(organization.faction))
                    .Select(pawn => pawn.Position));
            HashSet<int> cleared = new HashSet<int>(state.ClearedRoomCells
                .Where(cell => cell.InBounds(map))
                .Select(structure.RoomAt).Where(room => room > 0));
            foreach (IGrouping<int, IntVec3> group in anchors
                .Where(cell => cell.InBounds(map))
                .GroupBy(structure.RoomAt)
                .Where(group => group.Key > 0
                    && !cleared.Contains(group.Key))
                .OrderBy(group => group.Min(cell => cell.DistanceTo(start)
                    + cell.DistanceTo(state.FinalObjective) * 0.15f)))
            {
                IntVec3 target = group.SelectMany(anchor =>
                        GenRadial.RadialCellsAround(anchor, 6f, true))
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && structure.RoomAt(cell) == group.Key)
                    .OrderBy(cell => cell.DistanceTo(start))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                RaidTacticalPlan next = RaidTacticalPlanner.MakePlan(map,
                    organization, target);
                if (next?.Success != true) continue;
                ActivateNextRoomPlan(organization, members, state, next, tick);
                return true;
            }
            return false;
        }

        private static void ActivateNextRoomPlan(CombatOrganization organization,
            List<Pawn> members, ExecutionState state, RaidTacticalPlan next, int tick)
        {
            state.ActivePlan = next;
            state.Objective = next.Objective;
            state.Maneuver = next.Selected.Maneuver;
            state.PlanKey = PlanKey(organization, members);
            state.ReadySince = -1;
            state.ApproachComplete = false;
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
            RaidTacticalSpeech.Say(Commander(organization, members),
                "HD_RaidTactical_Assemble");
        }

        private string NearbyDoorState(IntVec3 objective)
        {
            return string.Join(",", map.listerThings.AllThings.OfType<Building_Door>()
                .Where(door => !door.Destroyed
                    && door.Position.DistanceTo(objective) <= 12f)
                .OrderBy(door => door.thingIDNumber)
                .Select(door => door.thingIDNumber + ":" + (door.Open ? "1" : "0")));
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
            foreach (Building_Door door in map.listerThings.AllThings
                .OfType<Building_Door>()
                .Where(value => !value.Destroyed && !value.Open
                    && value.Position.DistanceTo(plan.Objective) <= 12f
                    && openIds.Contains(value.thingIDNumber.ToString())))
            {
                bool enemyOutside = map.mapPawns.AllPawnsSpawned.Any(enemy =>
                    !enemy.Dead && !enemy.Downed && enemy.Faction != null
                    && enemy.Faction.HostileTo(members[0].Faction)
                    && enemy.Position.DistanceTo(door.Position) <= 4f
                    && structure.RoomAt(enemy.Position) != room);
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
                    JobDef job = DefDatabase<JobDef>.GetNamedSilentFail(close
                        ? "HD_ThrowInventoryGrenadeClose"
                        : "HD_ThrowInventoryGrenadeNormal");
                    if (job == null) return false;
                    pawn.jobs.StartJob(JobMaker.MakeJob(job, target, smoke),
                        JobCondition.InterruptForced);
                    RaidTacticalSpeech.Say(pawn, "HD_RaidTactical_Smoke");
                    return true;
                }
            }
            return false;
        }

        private float DoorStateScore(IntVec3 cell)
        {
            IntVec3[] directions = { IntVec3.North, IntVec3.East,
                IntVec3.South, IntVec3.West };
            float score = 0f;
            foreach (IntVec3 direction in directions)
            {
                IntVec3 adjacent = cell + direction;
                if (!adjacent.InBounds(map)
                    || !(adjacent.GetEdifice(map) is Building_Door door)) continue;
                score = Math.Max(score, door.Open ? 8f : 4f);
            }
            return score;
        }

    }

    public static class RaidTacticalSpeech
    {
        public static void Say(Pawn pawn, string key)
        {
            if (pawn?.Spawned == true && pawn.Map == Find.CurrentMap)
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, key.Translate(), 1.7f);
        }
    }
}
