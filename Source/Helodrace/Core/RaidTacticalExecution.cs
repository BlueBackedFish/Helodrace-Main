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

    public sealed class MapComponent_RaidTacticalExecution : MapComponent
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

        public sealed class ExecutionState : IExposable
        {
            public ExecutionState() { }
            public string OrganizationId;
            public string PlanKey;
            public IntVec3 Objective;
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
            public int LastDoorResponseTick;
            // Planning cache is rebuilt after a save load; execution progress is Scribed above.
            public RaidTacticalPlan ActivePlan;

            public void ExposeData()
            {
                Scribe_Values.Look(ref OrganizationId, "organizationId");
                Scribe_Values.Look(ref PlanKey, "planKey");
                Scribe_Values.Look(ref Objective, "objective");
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
                Scribe_Values.Look(ref LastDoorResponseTick,
                    "lastDoorResponseTick");
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

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick % 30 != 0) return;
            GameComponent_CombatOrganizations registry = OrganizationAPI.Registry;
            MapComponent_RaidTacticalPlans plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            if (registry == null || plans == null) return;

            var activeIds = new HashSet<string>();
            foreach (CombatOrganization organization in registry.Organizations)
            {
                List<Pawn> members = organization.AllMembers
                    .Where(pawn => pawn.Spawned && pawn.Map == map && !pawn.Dead
                        && !pawn.Downed && !pawn.Destroyed && IsAssaultRaider(pawn))
                    .ToList();
                if (members.Count == 0)
                {
                    if (tick % 90 == 0) KeepSapperEscortTogether(organization);
                    continue;
                }
                activeIds.Add(organization.id);
                if (states.TryGetValue(organization.id, out ExecutionState completed)
                    && TryExitSecuredObjective(organization, members, completed, tick))
                {
                    states.Remove(organization.id);
                    continue;
                }
                RaidTacticalPlan plan = plans.GetPlan(organization);
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
                string key = PlanKey(organization, members);
                states.TryGetValue(organization.id, out ExecutionState state);
                bool idle = state != null && (state.Phase == RaidExecutionPhase.Hold
                    || state.Phase == RaidExecutionPhase.Complete
                    || state.Phase == RaidExecutionPhase.SecureRoom
                    || (state.Phase == RaidExecutionPhase.Assemble
                        && state.ApproachComplete));
                bool changedIdlePlan = idle && !ReferenceEquals(state.ActivePlan, plan)
                    && (state.Maneuver != plan.Selected.Maneuver
                        || state.Objective.DistanceTo(plan.Objective) > 3f);
                if (state == null || state.PlanKey != key
                    || state.Objective.DistanceTo(plan.Objective) > 8f
                    || changedIdlePlan)
                {
                    ExecutionState previous = state;
                    if (previous != null) CancelPendingCharge(previous);
                    state = new ExecutionState
                    {
                        OrganizationId = organization.id,
                        PlanKey = key,
                        Objective = plan.Objective,
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
                    plans.GetPlan(organization, true);
                    states.Remove(organization.id);
                }
                else if (state.Phase == RaidExecutionPhase.Assemble
                    && state.ApproachComplete && !AllReady(members, state.ActivePlan)
                    && tick - state.PhaseStarted >= AssembleTimeout)
                {
                    plans.GetPlan(organization, true);
                    states.Remove(organization.id);
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
                    || escort.CurJobDef?.defName == CompSledgehammerBreach.JobDefName)
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
                    || !pawn.CanReserve(target, 2, -1, null, false)) continue;
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
            Room room = objective.GetRoom(map);
            bool indoors = room != null && !room.PsychologicallyOutdoors;
            if (indoors != (state.Phase == RaidExecutionPhase.SecureRoom)) return false;

            List<Pawn> entry = state.ActivePlan.Assignments
                .Where(assignment => assignment.Task == RaidTacticalTask.Entry
                    && members.Contains(assignment.Pawn))
                .Select(assignment => assignment.Pawn).Distinct().ToList();
            if (entry.Count == 0) return false;
            int atObjective = entry.Count(pawn => indoors
                ? pawn.Position.GetRoom(map) == room
                : pawn.Position.DistanceTo(objective) <= 9f);
            if (atObjective * 2 < entry.Count) return false;

            float dangerRadius = indoors ? 12f : 18f;
            if (map.mapPawns.AllPawnsSpawned.Any(enemy => !enemy.Dead
                && !enemy.Downed && enemy.Faction != null
                && enemy.Faction.HostileTo(organization.faction)
                && (enemy.Position.DistanceTo(objective) <= dangerRadius
                    || (indoors && enemy.Position.GetRoom(map) == room)))) return false;

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
                        }
                        else if (tick - state.PhaseStarted >= SupportTimeout
                            || !TryStageEntryThrower(members, plan, state))
                            Advance(state, RaidExecutionPhase.EntryWait, tick);
                    }
                    else if ((state.Thrower?.CurJobDef?.defName != "HD_ThrowInventoryGrenadeClose"
                        && state.Thrower?.CurJobDef?.defName != "HD_ThrowInventoryGrenadeNormal")
                        || tick - state.PhaseStarted >= SupportTimeout)
                        Advance(state, RaidExecutionPhase.EntryWait, tick);
                    break;
                case RaidExecutionPhase.EntryWait:
                    MaintainStack(members, plan);
                    if (!UsingZaper(members)
                        && (!plan.BreachCell.IsValid || AllReady(members, plan)
                            || tick - state.PhaseStarted >= 600)
                        && tick - state.PhaseStarted >= plan.EntryDelayTicks)
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
                    List<Pawn> crossing = EntryPawns(members, plan);
                    foreach (Pawn pawn in crossing)
                        if (!PastBreach(pawn, plan))
                            TryGoto(pawn, pawn.Position == plan.BreachCell
                                ? plan.BreachInside : pawn.Position == plan.Entry
                                    ? plan.BreachCell : plan.Entry, true);
                    if (crossing.All(pawn => PastBreach(pawn, plan)))
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
                        IssueAssault(members, plan);
                        Advance(state, plan.Objective.GetRoom(map)?.PsychologicallyOutdoors == false
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
                    .Where(other => other != pawn && entry.Contains(other))
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
            return (pawn.Position.x - breach.x) * towardInside.x
                + (pawn.Position.z - breach.z) * towardInside.z >= 1;
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
                float distance = pawn.Position.DistanceTo(assignment.Position);
                remaining += distance;
                if (distance <= 9f || IsTaserOperation(pawn)) continue;
                IntVec3 target = plan.BreachCell.IsValid
                    ? assignment.Position
                    : CorridorReturn(pawn, plan.ApproachPath, assignment.Position);
                TryGoto(pawn, target);
            }
            if (remaining + 1f < state.ApproachBestRemaining)
            {
                state.ApproachBestRemaining = remaining;
                state.ApproachProgressTick = tick;
            }
            if (group.All(assignment => assignment.Pawn.Position
                    .DistanceTo(assignment.Position) <= 9f))
            {
                state.ApproachComplete = true;
                state.PhaseStarted = tick;
                state.ReadySince = -1;
                return true;
            }
            return false;
        }

        private static IntVec3 CorridorReturn(Pawn pawn, List<IntVec3> path,
            IntVec3 destination)
        {
            if (path == null || path.Count == 0) return destination;
            int nearest = 0;
            float deviation = float.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                float distance = pawn.Position.DistanceToSquared(path[i]);
                if (distance >= deviation) continue;
                nearest = i;
                deviation = distance;
            }
            // The route is a broad corridor, not a sequence of mandatory cells.
            // Only a substantial detour calls for a return toward its forward edge.
            if (deviation <= 64f) return destination;
            IntVec3 returnCell = path[Math.Min(nearest + 6, path.Count - 1)];
            return returnCell.DistanceTo(destination) > 9f
                && returnCell.DistanceTo(destination)
                    < pawn.Position.DistanceTo(destination)
                && GenSight.LineOfSight(pawn.Position, returnCell, pawn.Map, true)
                && pawn.CanReach(returnCell, PathEndMode.OnCell, Danger.Deadly)
                ? returnCell : destination;
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
                float readyRadius = ReadyRadius(plan, assignment);
                if (pawn.Position.DistanceTo(assignment.Position) > readyRadius)
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
                if (pawn.Position != assignment.Position)
                    TryGoto(pawn, assignment.Position);
                else if (pawn.CurJobDef != JobDefOf.Wait_Combat
                    || pawn.CurJob?.expiryInterval <= 0)
                    HoldPosition(pawn);
            }
        }

        private Pawn CurrentResponseTarget(List<Pawn> members, RaidTacticalPlan plan)
        {
            return map.mapPawns.AllPawnsSpawned
                .Where(enemy => !enemy.Dead && !enemy.Downed && enemy.Faction != null
                    && enemy.Faction.HostileTo(members[0].Faction)
                    && enemy.Position.DistanceTo(plan.Start) <= 25f)
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
            var occupied = new HashSet<IntVec3>();
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .Where(value => value.Task == RaidTacticalTask.Response))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || pawn.Map != map
                    || IsTaserOperation(pawn)) continue;
                float range = pawn.equipment?.Primary?.GetComp<CompEquippable>()
                    ?.PrimaryVerb?.verbProps?.range ?? 12f;
                if (pawn.Position.DistanceTo(enemy.Position) <= range * 0.8f
                    && GenSight.LineOfSight(pawn.Position, enemy.Position, map, true))
                {
                    if (pawn.CurJobDef == JobDefOf.Goto)
                        HoldPosition(pawn);
                    continue;
                }
                IntVec3 target = GenRadial.RadialCellsAround(enemy.Position, 11f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && cell.DistanceTo(enemy.Position) >= 5f
                        && cell.DistanceTo(enemy.Position) <= Math.Min(9f, range)
                        && !plan.AvoidedTrapCells.Contains(cell)
                        && !occupied.Contains(cell))
                    .OrderBy(cell => cell.DistanceTo(pawn.Position)
                        + cell.DistanceTo(plan.Start) * 0.25f)
                    .Where(cell => pawn.CanReach(cell,
                        PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                occupied.Add(target);
                TryGoto(pawn, target);
            }
        }

        private void TryRequestExternalSupport(CombatOrganization organization,
            List<Pawn> members, RaidTacticalPlan plan, ExecutionState state)
        {
            if (plan.Objective.GetRoom(map)?.PsychologicallyOutdoors == false
                || Find.WorldObjects == null) return;
            List<HelodForwardBase> bases = Find.WorldObjects.AllWorldObjects
                .OfType<HelodForwardBase>()
                .Where(value => value.Faction == organization.faction).ToList();
            if (bases.Count == 0) return;
            Pawn target = map.mapPawns.AllPawnsSpawned
                .Where(enemy => !enemy.Dead && !enemy.Downed
                    && enemy.Faction != null && enemy.Faction.HostileTo(organization.faction)
                    && enemy.Position.DistanceTo(plan.Objective) <= 12f
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
            return plan.Assignments.Where(assignment => members.Contains(assignment.Pawn))
                .All(assignment => assignment.Pawn.Position.DistanceTo(assignment.Position)
                    <= ReadyRadius(plan, assignment));
        }

        private static float ReadyRadius(RaidTacticalPlan plan,
            RaidTacticalAssignment assignment)
        {
            if (!plan.BreachCell.IsValid || assignment.Task == RaidTacticalTask.Withdraw)
                return 1.5f;
            return assignment.Task == RaidTacticalTask.Entry ? 0.5f : 8f;
        }

        private static void HoldPosition(Pawn pawn)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            job.expiryInterval = 120;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private static void TryGoto(Pawn pawn, IntVec3 cell, bool sprint = false,
            bool interruptTaser = false)
        {
            if ((!interruptTaser && IsTaserOperation(pawn))
                || !cell.IsValid || !cell.InBounds(pawn.Map)
                || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;
            if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == cell) return;
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
            bool smoke = maneuver == RaidTacticalManeuver.SmokeAdvance;
            bool entry = maneuver == RaidTacticalManeuver.CoordinatedEntry;
            bool fieldGrenade = maneuver == RaidTacticalManeuver.FieldGrenade;
            if (!smoke && !entry && !fieldGrenade) return null;
            Map currentMap = members[0].Map;
            Room objectiveRoom = plan.Objective.GetRoom(currentMap);
            if (entry && plan.Doctrine == RaidTacticalDoctrine.Low
                && currentMap.mapPawns.AllPawnsSpawned.Any(pawn => pawn.Faction == members[0].Faction
                    && (plan.BreachCell.IsValid
                        ? PastBreach(pawn, plan)
                        : pawn.Position.GetRoom(currentMap) == objectiveRoom))) return null;
            IEnumerable<IntVec3> targets = smoke
                ? (IEnumerable<IntVec3>)new[] { plan.Frontline }
                : fieldGrenade
                    ? (IEnumerable<IntVec3>)currentMap.mapPawns.AllPawnsSpawned
                        .Where(hostile => hostile.Faction != null
                            && hostile.Faction.HostileTo(members[0].Faction)
                            && !hostile.Dead && !hostile.Downed)
                        .Select(hostile => hostile.Position)
                        .Distinct()
                        .OrderBy(cell => cell.DistanceTo(plan.Frontline))
                        .ToList()
                : EntryGrenadeTargets(currentMap, plan, objectiveRoom);
            foreach (Pawn pawn in members.OrderBy(value => value == preferred ? -1
                : entry && plan.Doctrine == RaidTacticalDoctrine.Low
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
            RaidTacticalPlan plan, Room objectiveRoom)
        {
            if (!plan.BreachCell.IsValid)
                return GenRadial.RadialCellsAround(plan.Entry, 7f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && cell.GetRoom(map) == objectiveRoom
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

        private static bool IsSupportGrenade(Thing item, RaidTacticalPlan plan,
            bool smoke)
        {
            return smoke ? item.def.defName == "HD_Grenade_M8_Item"
                || item.def.weaponTags?.Contains("GrenadeSmoke") == true
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
            if (plan.Doctrine == RaidTacticalDoctrine.Low
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
            List<IntVec3> targets = EntryGrenadeTargets(map, plan,
                plan.Objective.GetRoom(map)).ToList();
            foreach (Pawn pawn in members)
            {
                if (IsTaserOperation(pawn)
                    || !InventoryGrenadeUtility.GrenadeStacks(pawn)
                        .Any(item => IsSupportGrenade(item, plan, false))
                    || !pawn.CanReach(plan.Entry, PathEndMode.OnCell, Danger.Deadly))
                    continue;
                if (!targets.Any(target => plan.Entry.DistanceTo(target)
                        <= InventoryGrenadeUtility.NormalThrowRange
                    && GenSight.LineOfSight(plan.Entry, target, map, true)
                    && GrenadeTargetSafe(map, pawn, target))) continue;
                state.Thrower = pawn;
                TryGoto(pawn, plan.Entry);
                return true;
            }
            return false;
        }

        private void IssueAssault(List<Pawn> members, RaidTacticalPlan plan)
        {
            Map currentMap = members[0].Map;
            Room objectiveRoom = plan.Objective.GetRoom(currentMap);
            bool highIndoor = plan.Doctrine == RaidTacticalDoctrine.High
                && objectiveRoom != null && !objectiveRoom.PsychologicallyOutdoors;
            var occupied = new HashSet<IntVec3>();
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .Where(value => value.Task == RaidTacticalTask.Entry)
                .OrderBy(value => value.EntryOrder))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || IsTaserOperation(pawn)) continue;
                IntVec3 target = highIndoor
                    ? FindHighEntryCell(pawn, plan, objectiveRoom, occupied)
                    : IntVec3.Invalid;
                if (!target.IsValid)
                    target = GenRadial.RadialCellsAround(plan.Objective, 4f, true)
                    .Where(cell => cell.InBounds(currentMap) && cell.Standable(currentMap)
                        && !occupied.Contains(cell) && !plan.AvoidedTrapCells.Contains(cell))
                    .OrderBy(cell => cell.DistanceToSquared(plan.Objective))
                    .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) target = plan.Entry;
                occupied.Add(target);
                TryGoto(pawn, target, highIndoor);
            }
        }

        private IntVec3 FindHighEntryCell(Pawn pawn, RaidTacticalPlan plan,
            Room objectiveRoom, HashSet<IntVec3> occupied)
        {
            float dx = plan.Entry.x - plan.Start.x;
            float dz = plan.Entry.z - plan.Start.z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.1f) return IntVec3.Invalid;
            float forwardX = dx / length;
            float forwardZ = dz / length;
            int order = plan.Assignments.FirstOrDefault(value => value.Pawn == pawn)
                ?.EntryOrder ?? 1;
            float side = order % 2 == 1 ? 1f : -1f;
            return GenRadial.RadialCellsAround(plan.Entry, 9f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && cell.GetRoom(map) == objectiveRoom
                    && !occupied.Contains(cell) && !plan.AvoidedTrapCells.Contains(cell))
                .Select(cell => new
                {
                    Cell = cell,
                    Forward = (cell.x - plan.Entry.x) * forwardX
                        + (cell.z - plan.Entry.z) * forwardZ,
                    Side = -(cell.x - plan.Entry.x) * forwardZ
                        + (cell.z - plan.Entry.z) * forwardX
                })
                .Where(value => value.Forward >= 1.5f && value.Forward <= 8f
                    && value.Side * side >= 0.5f)
                .OrderBy(value => Math.Abs(value.Forward - 5f)
                    + Math.Abs(value.Side - side * 2.5f) * 0.7f
                    + value.Cell.DistanceTo(plan.Objective) * 0.15f)
                .Select(value => value.Cell)
                .Where(cell => pawn.CanReach(cell,
                    PathEndMode.OnCell, Danger.Deadly))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private void IssueRoomSecurity(List<Pawn> members, RaidTacticalPlan plan)
        {
            Room room = plan.Objective.GetRoom(map);
            if (room == null || room.PsychologicallyOutdoors) return;
            MapComponent_TacticalMapAnalysis analysis = map.GetComponent<MapComponent_TacticalMapAnalysis>();
            if (analysis == null) return;
            var occupied = new List<IntVec3>();
            List<IntVec3> sectors = GenRadial.RadialCellsAround(plan.Objective, 11f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map)
                    && cell.GetRoom(map) == room && !plan.AvoidedTrapCells.Contains(cell)
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
                    && enemy.Position.GetRoom(map) == room
                    && enemy.Position.DistanceTo(pawn.Position) <= 6f)) continue;
                IntVec3 sector = sectors
                    .Where(cell => occupied.All(other => cell.DistanceTo(other) >= 3f))
                    .OrderByDescending(cell => analysis.At(cell).DoorThreat * 2f
                        + analysis.At(cell).WallThreat
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
            Room room = plan.Objective.GetRoom(map);
            if (room == null || room.PsychologicallyOutdoors) return false;
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
                    && enemy.Position.GetRoom(map) != room);
                if (!enemyOutside) continue;
                IntVec3 target = new[] { IntVec3.North, IntVec3.East,
                        IntVec3.South, IntVec3.West }
                    .Select(direction => door.Position + direction)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && cell.GetRoom(map) == room)
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
