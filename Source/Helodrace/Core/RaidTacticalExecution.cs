using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using Helodrace.Tactical;
using RimWorld;
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
        Flank,
        Assault,
        Hold,
        Complete
    }

    public enum RaidBreachKind
    {
        None,
        PowerCutter,
        C4
    }

    public sealed class MapComponent_RaidTacticalExecution : MapComponent
    {
        private const int AssembleTimeout = 720;
        private const int BreachTimeout = 900;
        private const int WithdrawalTimeout = 600;
        private const int DetonationTimeout = 240;
        private const int SupportTimeout = 360;
        private const int FlankTimeout = 600;
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
            public Pawn Breacher;
            public Building BreachTarget;
            public RaidBreachKind BreachKind;
            public bool WithdrawalIssued;
            public Pawn Thrower;
            public bool SupportIssued;
            public bool FlankIssued;
            public bool AssaultIssued;
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
                Scribe_References.Look(ref Breacher, "breacher");
                Scribe_References.Look(ref BreachTarget, "breachTarget");
                Scribe_Values.Look(ref BreachKind, "breachKind");
                Scribe_Values.Look(ref WithdrawalIssued, "withdrawalIssued");
                Scribe_References.Look(ref Thrower, "thrower");
                Scribe_Values.Look(ref SupportIssued, "supportIssued");
                Scribe_Values.Look(ref FlankIssued, "flankIssued");
                Scribe_Values.Look(ref AssaultIssued, "assaultIssued");
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
            return organizationId != null && states.TryGetValue(organizationId, out ExecutionState state)
                ? state.Phase.ToString() : "Inactive";
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
                if (members.Count == 0) continue;
                activeIds.Add(organization.id);
                RaidTacticalPlan plan = plans.GetPlan(organization);
                if (plan?.Success != true) continue;
                string key = PlanKey(organization, members);
                if (!states.TryGetValue(organization.id, out ExecutionState state)
                    || state.PlanKey != key
                    || state.Objective.DistanceTo(plan.Objective) > 8f)
                {
                    state = new ExecutionState
                    {
                        OrganizationId = organization.id,
                        PlanKey = key,
                        Objective = plan.Objective,
                        Maneuver = plan.Selected.Maneuver,
                        ActivePlan = plan,
                        Phase = RaidExecutionPhase.Assemble,
                        PhaseStarted = tick
                    };
                    states[organization.id] = state;
                    RaidTacticalSpeech.Say(Commander(organization, members),
                        "HD_RaidTactical_Assemble");
                }
                if (state.ActivePlan == null) state.ActivePlan = plan;
                Update(organization, members, state.ActivePlan, state, tick);
            }
            foreach (string id in states.Keys.Where(id => !activeIds.Contains(id)).ToList())
                states.Remove(id);
        }

        private static bool IsAssaultRaider(Pawn pawn)
        {
            Lord lord = pawn.GetLord();
            return pawn.Faction != Faction.OfPlayer
                && (lord?.LordJob is LordJob_AssaultColony
                    || lord?.LordJob?.GetType().Name.StartsWith("LordJob_AssaultColony",
                        StringComparison.Ordinal) == true);
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

        private void Update(CombatOrganization organization, List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            switch (state.Phase)
            {
                case RaidExecutionPhase.Assemble:
                    Assemble(members, plan);
                    if (AllReady(members, plan) || tick - state.PhaseStarted >= AssembleTimeout)
                    {
                        RaidExecutionPhase next = state.Maneuver == RaidTacticalManeuver.HoldAndCounterattack
                            || state.Maneuver == RaidTacticalManeuver.Regroup
                            ? RaidExecutionPhase.Hold
                            : RaidExecutionPhase.Breach;
                        Advance(state, next, tick);
                    }
                    break;
                case RaidExecutionPhase.Breach:
                    if (state.Breacher == null && state.BreachTarget == null)
                    {
                        if (!TryStartBreach(members, plan, state))
                            Advance(state, RaidExecutionPhase.Support, tick);
                    }
                    else if (state.BreachKind == RaidBreachKind.C4
                        && BreachExplosiveUtility.ChargeOnWall(state.BreachTarget)
                            ?.OperatorPawn == state.Breacher)
                        Advance(state, RaidExecutionPhase.WithdrawFromCharge, tick);
                    else if (state.BreachKind == RaidBreachKind.C4
                        && (state.Breacher?.CurJobDef?.defName
                            != BreachExplosiveUtility.ShockTubeJobDefName
                            || tick - state.PhaseStarted >= BreachTimeout))
                        Advance(state, RaidExecutionPhase.Support, tick);
                    else if (state.BreachKind == RaidBreachKind.PowerCutter
                        && (state.BreachTarget == null || state.BreachTarget.Destroyed
                            || !state.BreachTarget.Spawned
                            || state.Breacher?.CurJobDef?.defName != "HD_PowerCutterBreach"
                            || tick - state.PhaseStarted >= BreachTimeout))
                        Advance(state, RaidExecutionPhase.Support, tick);
                    break;
                case RaidExecutionPhase.WithdrawFromCharge:
                    CompInstalledBreachCharge charge = BreachExplosiveUtility
                        .ChargeOnWall(state.BreachTarget);
                    if (charge == null || charge.OperatorPawn != state.Breacher)
                    {
                        Advance(state, RaidExecutionPhase.Support, tick);
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
                        Advance(state, RaidExecutionPhase.Support, tick);
                    }
                    break;
                case RaidExecutionPhase.Detonation:
                    if (state.BreachTarget == null || state.BreachTarget.Destroyed
                        || !state.BreachTarget.Spawned
                        || tick - state.PhaseStarted >= DetonationTimeout)
                        Advance(state, RaidExecutionPhase.Support, tick);
                    break;
                case RaidExecutionPhase.Support:
                    if (!state.SupportIssued)
                    {
                        state.SupportIssued = true;
                        state.Thrower = TryStartSupport(members, plan, state.Maneuver);
                        if (state.Thrower == null)
                            Advance(state, AfterSupport(plan, state.Maneuver), tick);
                    }
                    else if ((state.Thrower?.CurJobDef?.defName != "HD_ThrowInventoryGrenadeClose"
                        && state.Thrower?.CurJobDef?.defName != "HD_ThrowInventoryGrenadeNormal")
                        || tick - state.PhaseStarted >= SupportTimeout)
                        Advance(state, AfterSupport(plan, state.Maneuver), tick);
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
                        Advance(state, RaidExecutionPhase.Complete, tick);
                    }
                    break;
                case RaidExecutionPhase.Hold:
                    Assemble(members, plan);
                    break;
            }
        }

        private static void Advance(ExecutionState state, RaidExecutionPhase next, int tick)
        {
            state.Phase = next;
            state.PhaseStarted = tick;
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
                if (!members.Contains(pawn)) continue;
                IntVec3 target = GenRadial.RadialCellsAround(plan.Flank, 3f, true)
                    .Where(cell => cell.InBounds(map) && cell.Standable(map)
                        && !plan.AvoidedTrapCells.Contains(cell) && !occupied.Contains(cell))
                    .OrderBy(cell => cell.DistanceToSquared(plan.Flank))
                    .FirstOrDefault(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly));
                if (!target.IsValid) target = plan.Flank;
                occupied.Add(target);
                TryGoto(pawn, target);
            }
        }

        private void Assemble(List<Pawn> members, RaidTacticalPlan plan)
        {
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || !assignment.Position.IsValid) continue;
                if (pawn.Position.DistanceTo(assignment.Position) > 1.5f)
                    TryGoto(pawn, assignment.Position);
                else if (assignment.Task == RaidTacticalTask.Withdraw
                    && TcccUtility.HasTraining(pawn)
                    && pawn.health?.hediffSet?.BleedRateTotal > 0f)
                {
                    if (pawn.CurJobDef?.defName != "HD_TCCC_Treat")
                        TcccUtility.Start(pawn, pawn, TcccTreatment.SelfHemostasis);
                }
                else if (pawn.CurJobDef != JobDefOf.Wait_Combat)
                    pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Combat),
                        JobCondition.InterruptForced);
            }
        }

        private static bool AllReady(List<Pawn> members, RaidTacticalPlan plan)
        {
            return plan.Assignments.Where(assignment => members.Contains(assignment.Pawn))
                .All(assignment => assignment.Pawn.Position.DistanceTo(assignment.Position) <= 1.5f);
        }

        private static void TryGoto(Pawn pawn, IntVec3 cell)
        {
            if (!cell.IsValid || !cell.InBounds(pawn.Map)
                || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;
            if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == cell) return;
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, cell),
                JobCondition.InterruptForced);
        }

        private static bool TryStartBreach(List<Pawn> members, RaidTacticalPlan plan,
            ExecutionState state)
        {
            if (state.Maneuver != RaidTacticalManeuver.CoordinatedEntry) return false;
            Map currentMap = members[0].Map;
            Building target = GenRadial.RadialCellsAround(plan.Entry, 2.9f, true)
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
                if (cutterJob == null
                    || pawn.equipment?.Primary?.TryGetComp<CompPowerCutterBreach>() == null
                    || !CompPowerCutterBreach.TryFindInteractionCell(pawn, target, out IntVec3 cell)
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
                CompBreachIgniter igniter = BreachExplosiveUtility.FindIgniter(pawn,
                    BreachInitiationMode.ShockTube, false);
                int required = BreachExplosiveUtility.RequiredC4For(target);
                if (!BreachExplosiveUtility.CanOperate(pawn) || igniter == null
                    || BreachExplosiveUtility.CountInInventory(pawn,
                        BreachExplosiveUtility.C4Def) < required
                    || BreachExplosiveUtility.ActiveChargeFor(pawn) != null
                    || BreachExplosiveUtility.ChargeOnWall(target) != null
                    || !BreachExplosiveUtility.TryFindInteractionCell(pawn, target,
                        out IntVec3 cell)
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
                    .FirstOrDefault(cell => pawn.CanReach(cell,
                        PathEndMode.OnCell, Danger.Deadly));
                if (!target.IsValid) continue;
                occupied.Add(target);
                TryGoto(pawn, target);
            }
        }

        private static Pawn TryStartSupport(List<Pawn> members, RaidTacticalPlan plan,
            RaidTacticalManeuver maneuver)
        {
            bool smoke = maneuver == RaidTacticalManeuver.SmokeAdvance;
            bool entry = maneuver == RaidTacticalManeuver.CoordinatedEntry;
            bool fieldGrenade = maneuver == RaidTacticalManeuver.FieldGrenade;
            if (!smoke && !entry && !fieldGrenade) return null;
            Map currentMap = members[0].Map;
            Room objectiveRoom = plan.Objective.GetRoom(currentMap);
            if (entry && plan.Doctrine == RaidTacticalDoctrine.Low
                && currentMap.mapPawns.AllPawnsSpawned.Any(pawn => pawn.Faction == members[0].Faction
                    && pawn.Position.GetRoom(currentMap) == objectiveRoom)) return null;
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
                : GenRadial.RadialCellsAround(plan.Entry, 7f, true)
                    .Where(cell => cell.InBounds(currentMap) && cell.Standable(currentMap)
                        && cell.GetRoom(currentMap) == objectiveRoom
                        && cell.DistanceTo(plan.Entry) >= 2f)
                    .OrderBy(cell => cell.DistanceTo(plan.Entry));
            foreach (Pawn pawn in members)
            {
                Thing grenade = InventoryGrenadeUtility.GrenadeStacks(pawn).FirstOrDefault(item =>
                    smoke ? item.def.defName == "HD_Grenade_M8_Item"
                        || item.def.weaponTags?.Contains("GrenadeSmoke") == true
                    : plan.Doctrine == RaidTacticalDoctrine.High
                        ? item.def.defName == "HD_Grenade_M84_Item"
                            || item.def.defName == "HD_Grenade_M7A2_Item"
                        : item.def.defName == "HD_Grenade_MKII"
                            || item.def.defName == "HD_Grenade_MKIII");
                if (grenade == null) continue;
                foreach (IntVec3 target in targets)
                {
                    if (!target.IsValid || !target.InBounds(currentMap)) continue;
                    if (!smoke && currentMap.mapPawns.AllPawnsSpawned.Any(ally =>
                        !ally.Dead && !ally.HostileTo(pawn)
                        && ally.Position.DistanceTo(target) <= 3.5f))
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

        private void IssueAssault(List<Pawn> members, RaidTacticalPlan plan)
        {
            Map currentMap = members[0].Map;
            var occupied = new HashSet<IntVec3>();
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .Where(value => value.Task == RaidTacticalTask.Entry)
                .OrderBy(value => value.EntryOrder))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn)) continue;
                IntVec3 target = GenRadial.RadialCellsAround(plan.Objective, 4f, true)
                    .Where(cell => cell.InBounds(currentMap) && cell.Standable(currentMap)
                        && !occupied.Contains(cell) && !plan.AvoidedTrapCells.Contains(cell))
                    .OrderBy(cell => cell.DistanceToSquared(plan.Objective))
                    .FirstOrDefault(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly));
                if (!target.IsValid) target = plan.Entry;
                occupied.Add(target);
                TryGoto(pawn, target);
            }
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
