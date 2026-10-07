using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.Tactics
{
    public enum TacticalCommandPhase { Pending, Stack, Breach, Observe, Support, BlastWait, Enter, Returning, Complete, Released }
    public sealed class TacticalMemberCommand
    {
        public Pawn Pawn;
        public Job Job;
        public int RetryTick;
        public int LastProgressTick;
        public IntVec3 LastPosition, Parking = IntVec3.Invalid;
        public bool Passed, Crossed, Entered, Rear;
    }
    public sealed class TacticalSquadCommand
    {
        public string Id;
        public readonly List<TacticalMemberCommand> Members = new List<TacticalMemberCommand>();
        public MapComponent_TacticalCommands Owner;
        public TacticalCommandPhase Phase;
        public TacticalLocalPlan Plan;
        public IntVec3 Goal;
        public int Due, PhaseStarted, Failures, PlanRetryAt;
        public bool HadConnectedStack;
        public Pawn Breacher;
        public int BarrierHitPoints = -1;
        public int ReturnCursor;
        public bool ReleaseAfterReturn;
        public bool DeferredWork;
        public TacticalPlanFailure LastPlanFailure;
        public TacticalOpeningAction OpeningAction;
        public bool ReplanAfterSupport;
        public bool Terminal => Phase == TacticalCommandPhase.Released;
    }

    // One fair bounded scheduler for ALL maps. No per-map tactical tick/update.
    [NewTactical]
    public sealed class GameComponent_TacticalCommands : GameComponent
    {
        private readonly List<TacticalSquadCommand> commands = new List<TacticalSquadCommand>();
        private int cursor, discoverAt, discoverOrganization;
        private IEnumerator<RaidTacticalUnit> discovery;
        private CombatOrganization discovering;
        private int discoveryRevision;
        public readonly TacticalWorkBudget WorkBudget = new TacticalWorkBudget();
        public long Advances, BudgetStops;
        public GameComponent_TacticalCommands(Game game) { }
        public static bool IsAssaultPhase(LordJob job, LordToil toil) => job is LordJob_AssaultColony
            && (toil is LordToil_AssaultColony || toil is LordToil_AssaultColonySappers || toil is LordToil_AssaultColonyBreaching);
        public static bool IsAssaultLord(Lord lord) => lord != null && IsAssaultPhase(lord.LordJob, lord.CurLordToil);
        public void RegisterLord(Lord lord)
        {
            if (!IsAssaultLord(lord)) return;
            var seen = new HashSet<string>();
            foreach (Pawn pawn in lord.ownedPawns)
            {
                RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(pawn);
                if (unit == null || !seen.Add(unit.Id)) continue;
                MapComponent_TacticalCommands service = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
                if (service == null || !unit.Faction.HostileTo(Faction.OfPlayer) && !service.HasExplicitGoal) continue;
                TacticalSquadCommand command = service.Register(unit, GenTicks.TicksGame);
                if (command != null) commands.Add(command);
            }
        }
        public override void GameComponentTick()
        {
            int tick = GenTicks.TicksGame;
            DiscoverOne(tick);
            if (commands.Count == 0) return;
            long started = Stopwatch.GetTimestamp();
            int visited = 0, processed = 0;
            // Four cheap due checks, at most two real squad decisions in a tick.
            while (visited++ < Math.Min(4, commands.Count) && processed < 2)
            {
                if (cursor >= commands.Count) cursor = 0;
                TacticalSquadCommand command = commands[cursor];
                if (command.Terminal || command.Owner.map.Disposed)
                { commands.RemoveAt(cursor); continue; }
                cursor++;
                if (command.Due > tick) continue;
                command.Owner.Advance(command, tick); Advances++; processed++;
                if (Stopwatch.GetTimestamp() - started > Stopwatch.Frequency * .0015)
                { BudgetStops++; break; }
            }
        }
        private void DiscoverOne(int tick)
        {
            if (tick < discoverAt) return;
            GameComponent_CombatOrganizations registry = OrganizationAPI.Registry;
            if (registry == null) { discoverAt = tick + 300; return; }
            if (discovering != null && (registry.GetOrganization(discovering.id) != discovering
                || discovering.StructureRevision != discoveryRevision))
            { discovery?.Dispose(); discovery = null; discovering = null; }
            if (discovery == null)
            {
                if (discoverOrganization >= registry.Organizations.Count)
                { discoverOrganization = 0; discoverAt = tick + 300; return; }
                discovering = registry.Organizations[discoverOrganization++];
                discoveryRevision = discovering.StructureRevision;
                discovery = RaidTacticalUnit.ForOrganization(discovering).GetEnumerator();
            }
            // A registration sweep is spread over ticks, including after load.
            if (!discovery.MoveNext())
            { discovery.Dispose(); discovery = null; discovering = null; return; }
            RaidTacticalUnit unit = discovery.Current;
            Pawn pawn = unit.Members.FirstOrDefault(member => member.Spawned && !member.Dead);
            if (pawn == null || !IsAssaultLord(pawn.GetLord())) return;
            MapComponent_TacticalCommands service = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            if (service == null || !unit.Faction.HostileTo(Faction.OfPlayer) && !service.HasExplicitGoal) return;
            TacticalSquadCommand command = service.Register(unit, tick);
            if (command != null) commands.Add(command);
        }
    }

    [NewTactical]
    public sealed partial class MapComponent_TacticalCommands : MapComponent
    {
        private readonly Dictionary<string, TacticalSquadCommand> squads = new Dictionary<string, TacticalSquadCommand>();
        private readonly Dictionary<Pawn, TacticalSquadCommand> byPawn = new Dictionary<Pawn, TacticalSquadCommand>();
        private readonly Dictionary<IntVec3, TacticalSquadCommand> claims = new Dictionary<IntVec3, TacticalSquadCommand>();
        private readonly Dictionary<IntVec3, TacticalSquadCommand> leases = new Dictionary<IntVec3, TacticalSquadCommand>();
        private readonly List<TacticalLocalPlan> knownOpenings = new List<TacticalLocalPlan>(8);
        private IntVec3 explicitGoal = IntVec3.Invalid, automaticGoal = IntVec3.Invalid;
        private int goalRetry;
        public long JobsIssued, JobFailures, PlansAttempted, PlansBuilt;
        public string LastJobFailure;
        public IEnumerable<TacticalSquadCommand> Commands => squads.Values;
        public bool HasExplicitGoal => explicitGoal.IsValid;
        public MapComponent_TacticalCommands(Map map) : base(map) { }

        // Profiler-only read: never register or rebuild an organization.
        internal bool TryProfileContext(Pawn pawn, out string id, out TacticalCommandPhase phase)
        {
            if (byPawn.TryGetValue(pawn, out TacticalSquadCommand command))
            { id = command.Id; phase = command.Phase; return true; }
            id = null; phase = default; return false;
        }

        // Public/debug mission input. This also permits an AI-v-AI target.
        public void SetObjective(IntVec3 goal)
        {
            if (!goal.InBounds(map)) throw new ArgumentException("Objective is outside this map.");
            explicitGoal = goal;
            knownOpenings.Clear();
            foreach (TacticalSquadCommand command in squads.Values.ToArray())
            {
                if (command.Terminal) { squads.Remove(command.Id); continue; }
                // Never erase an already launched grenade's safety state.
                if (command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared)
                { command.ReplanAfterSupport = true; command.Goal = goal; command.Due = GenTicks.TicksGame + 1; continue; }
                ReleaseClaims(command); command.Plan = null; command.Goal = goal;
                command.OpeningAction = null;
                command.Phase = TacticalCommandPhase.Pending; command.Due = GenTicks.TicksGame;
                command.ReturnCursor = 0; command.ReleaseAfterReturn = false;
                command.PlanRetryAt = 0;
                foreach (TacticalMemberCommand member in command.Members)
                {
                    member.Passed = member.Crossed = member.Entered = false;
                    // Preserve ownership until a replacement job is admitted.
                    member.RetryTick = 0;
                }
            }
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref explicitGoal, "newTacticalExplicitGoal", IntVec3.Invalid);
            // Execution is rebuilt after load; persisted vanilla JobDrivers keep
            // their own progress. No legacy save compatibility is needed.
        }
        internal TacticalSquadCommand Register(RaidTacticalUnit unit, int tick)
        {
            if (squads.ContainsKey(unit.Id)) return null;
            var command = new TacticalSquadCommand { Id = unit.Id, Owner = this, Due = tick };
            foreach (Pawn pawn in unit.Members)
                if (pawn.Spawned && pawn.Map == map && !pawn.Dead && !byPawn.ContainsKey(pawn))
                {
                    command.Members.Add(new TacticalMemberCommand { Pawn = pawn });
                    byPawn[pawn] = command;
                }
            if (command.Members.Count == 0) return null;
            for (int i = command.Members.Count * 2 / 3; i < command.Members.Count; i++) command.Members[i].Rear = true;
            squads.Add(command.Id, command); return command;
        }
        public void Wake(Pawn pawn)
        {
            if (pawn != null && byPawn.TryGetValue(pawn, out TacticalSquadCommand command) && !command.Terminal)
                command.Due = Math.Min(command.Due, GenTicks.TicksGame + 1);
        }
        internal void JobFinished(Pawn pawn, Job job, JobCondition condition)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)) return;
            TacticalMemberCommand member = command.Members.Find(item => item.Pawn == pawn);
            if (member == null || member.Job != job) return;
            member.Job = null;
            if (condition != JobCondition.Succeeded)
            {
                JobFailures++; member.RetryTick = GenTicks.TicksGame + 180;
                LastJobFailure = job.def.defName + ":" + condition + " pawn=" + pawn.thingIDNumber;
            }
            Wake(pawn);
        }
        internal void PassedOpening(Pawn pawn, Job job, IntVec3 at)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.Plan == null) return;
            TacticalMemberCommand member = command.Members.Find(item => item.Pawn == pawn);
            if (member?.Job == job && command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared) UnsafeEntries++;
            if (member?.Job == job && at == command.Plan.Opening) member.Passed = true;
        }
        internal void CrossedInside(Pawn pawn, Job job)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)) return;
            TacticalMemberCommand member = command.Members.Find(item => item.Pawn == pawn);
            if (member?.Job == job) member.Crossed = true;
        }
        private static bool Available(TacticalMemberCommand member, Map map) => member.Pawn.Spawned
            && member.Pawn.Map == map && !member.Pawn.Dead && !member.Pawn.Downed && !member.Pawn.InMentalState
            && GameComponent_TacticalCommands.IsAssaultLord(member.Pawn.GetLord());

        public void Advance(TacticalSquadCommand command, int tick)
        {
            command.DeferredWork = false;
            try { AdvanceCore(command, tick); }
            finally
            {
                if (command.DeferredWork && !command.Terminal)
                    command.Due = Math.Min(command.Due, tick + 1);
            }
        }
        private void AdvanceCore(TacticalSquadCommand command, int tick)
        {
            if (command.Phase == TacticalCommandPhase.Returning)
            {
                if (SupportEffectsPending(command, tick)) { command.Due = tick + 15; return; }
                ReturnMembers(command, tick); return;
            }
            command.Due = tick + (command.Phase == TacticalCommandPhase.Enter ? 30 : 120);
            var active = command.Members.Where(member => Available(member, map)).ToList();
            if (active.Count == 0) { Release(command); return; }
            if (command.Phase == TacticalCommandPhase.Complete) { command.Due = tick + 600; return; }
            foreach (TacticalMemberCommand member in active)
            {
                if (member.Pawn.Position != member.LastPosition)
                { member.LastPosition = member.Pawn.Position; member.LastProgressTick = tick; }
                if (member.Pawn.CurJob == member.Job && member.Pawn.jobs.curDriver is TacticalJobDriver driver
                    && !driver.AtPost && !(driver is JobDriver_TacticalBreach) && tick - member.LastProgressTick > 1200)
                {
                    // A live but stalled path must also have a bounded retry.
                    // Replace a stalled job directly, without a transient vanilla
                    // think-tree search between cancelling it and issuing a post.
                    if (!CanIssue(member)) continue;
                    IntVec3 post = RetryPost(command, member);
                    if (Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"), post, command.Goal)))
                    { JobFailures++; member.RetryTick = tick + 180; member.LastProgressTick = tick; }
                    continue;
                }
                if (member.Job == null && tick < member.RetryTick && member.Pawn.CurJob?.playerForced != true)
                {
                    // Do not let a failed tactical job briefly acquire vanilla
                    // escort/wander orders while its bounded retry is pending.
                    // Reuse the existing assigned post; no plan is recalculated.
                    if (!CanIssue(member)) continue;
                    IntVec3 post = member.Pawn.Position;
                    if (command.Plan != null)
                    {
                        int index = command.Members.IndexOf(member);
                        post = member.Crossed || command.Plan.Direct ? command.Plan.Positions[index] : command.Plan.Stack[index];
                    }
                    Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"), post, command.Goal));
                }
            }
            if (command.Phase == TacticalCommandPhase.Pending)
            {
                if (tick < command.PlanRetryAt)
                {
                    // Deferred parking jobs must not turn a 240-tick plan retry
                    // into a new geometric search every tick.
                    foreach (TacticalMemberCommand member in active) EnsureParking(command, member, tick);
                    command.Due = command.PlanRetryAt; return;
                }
                command.Goal = Goal(tick);
                if (!command.Goal.IsValid) { command.Due = tick + 300; return; }
                TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) { command.Due = tick + 1; return; }
                Pawn leader = active[0].Pawn;
                Pawn hammer = active.Select(member => member.Pawn).FirstOrDefault(pawn => CompSledgehammerBreach.WornBy(pawn) != null
                    && CompSledgehammerBreach.CanOperate(pawn));
                PlansAttempted++;
                bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command;
                long planStarted = Stopwatch.GetTimestamp();
                TacticalLocalPlan plan;
                TacticalPlanFailure failure;
                try
                {
                    plan = ReuseOpening(leader, command.Goal, command.Members.Count, Claimed,
                        out bool triedKnown, out failure);
                    if (!triedKnown) plan = TacticalLocalPlanner.Find(map, leader, hammer, command.Goal, command.Members.Count,
                        Claimed, cell => leases.ContainsKey(cell), out failure);
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - planStarted); }
                command.LastPlanFailure = failure;
                if (plan == null)
                {
                    if ((hammer == null || command.Failures >= 4) && leases.Count == 0)
                    { Release(command); return; }
                    // Waiting for another squad's physical footprint does not let
                    // vanilla wander through an unrelated entrance in the meantime.
                    foreach (TacticalMemberCommand member in active)
                        EnsureParking(command, member, tick);
                    command.Failures++; command.PlanRetryAt = tick + 240;
                    command.Due = command.PlanRetryAt; return;
                }
                ReleaseClaims(command); command.Plan = plan; PlansBuilt++; command.PhaseStarted = tick;
                command.OpeningAction = null;
                if (plan.Direct)
                {
                    foreach (IntVec3 cell in plan.Positions) claims[cell] = command;
                    command.Phase = TacticalCommandPhase.Enter;
                }
                else
                {
                    leases.Add(plan.Opening, command);
                    foreach (IntVec3 cell in plan.Stack.Concat(plan.Positions)) claims[cell] = command;
                    claims[plan.Outside] = command;
                    claims[plan.Inside] = command;
                    claims[plan.Opening] = command;
                    command.HadConnectedStack = TacticalLocalPlanner.Connected(plan.Stack);
                    command.Breacher = hammer;
                    command.Phase = plan.ExistingOpening ? TacticalCommandPhase.Observe : TacticalCommandPhase.Stack;
                }
            }
            TacticalLocalPlan current = command.Plan;
            if (command.Phase == TacticalCommandPhase.Stack || command.Phase == TacticalCommandPhase.Breach)
            {
                int ready = 0;
                for (int i = 0; i < command.Members.Count; i++)
                {
                    TacticalMemberCommand member = command.Members[i]; if (!Available(member, map)) continue;
                    if (command.Phase == TacticalCommandPhase.Breach && member.Pawn == command.Breacher) continue;
                    EnsurePost(member, current.Stack[i], current.Opening, tick);
                    if (AtPost(member)) ready++;
                }
                if (OpeningUsable(current))
                { RememberOpening(current); command.Phase = TacticalCommandPhase.Observe; command.PhaseStarted = tick; }
                else if (command.Phase == TacticalCommandPhase.Stack)
                {
                    // A lost straggler must not keep the entire squad frozen.
                    if (ready < active.Count && tick - command.PhaseStarted < 1200) return;
                    command.Phase = TacticalCommandPhase.Breach; command.PhaseStarted = tick;
                }
                if (command.Phase == TacticalCommandPhase.Breach)
                {
                    current.Barrier = current.Opening.GetEdifice(map);
                    int hitPoints = current.Barrier?.HitPoints ?? 0;
                    if (hitPoints != command.BarrierHitPoints)
                    { command.BarrierHitPoints = hitPoints; command.PhaseStarted = tick; }
                    if (tick - command.PhaseStarted > 1200)
                    { Release(command); return; }
                    TacticalMemberCommand worker = active.FirstOrDefault(member => member.Pawn == command.Breacher
                        && CompSledgehammerBreach.WornBy(member.Pawn) != null && CompSledgehammerBreach.CanOperate(member.Pawn));
                    worker = worker ?? active.FirstOrDefault(member => CompSledgehammerBreach.WornBy(member.Pawn) != null
                        && CompSledgehammerBreach.CanOperate(member.Pawn));
                    if (worker == null || !CompSledgehammerBreach.CanOperate(worker.Pawn))
                    {
                        // R3 adds dropped-tool recovery. R2 relinquishes the
                        // assignment instead of trapping every survivor forever.
                        Release(command); return;
                    }
                    command.Breacher = worker.Pawn;
                    if (worker.Job?.def.defName != "HD_NewTacticalBreach" || worker.Pawn.CurJob != worker.Job)
                    {
                        if (tick < worker.RetryTick) return;
                        if (!CanIssue(worker)) return;
                        Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalBreach"), current.Barrier,
                            current.Outside, CompSledgehammerBreach.WornBy(worker.Pawn).parent);
                        Issue(worker, job);
                    }
                    command.Due = Math.Min(command.Due, tick + 30);
                    return;
                }
            }
            if (command.Phase == TacticalCommandPhase.Observe || command.Phase == TacticalCommandPhase.Support
                || command.Phase == TacticalCommandPhase.BlastWait)
            {
                AdvanceOpeningAction(command, active, tick);
                if (command.Phase != TacticalCommandPhase.Enter) return;
            }
            if (command.Phase != TacticalCommandPhase.Enter) return;
            AvoidObservedOccupiedPost(command);
            if (!current.Direct && !OpeningUsable(current))
            {
                // Local obstruction, not global topology refresh. Keep the
                // selected entrance and retry the breach, with bounded latency.
                current.Barrier = current.Opening.GetEdifice(map);
                command.Phase = TacticalCommandPhase.Breach; command.Due = tick + 180; return;
            }
            for (int i = 0; i < command.Members.Count; i++)
            {
                TacticalMemberCommand member = command.Members[i]; if (!Available(member, map)) continue;
                if (member.Pawn.jobs.curDriver is TacticalJobDriver driver && member.Pawn.CurJob == member.Job && driver.AtPost)
                    member.Entered |= member.Pawn.Position == current.Positions[i] && (current.Direct || member.Crossed && member.Passed);
                if (member.Entered) continue;
                if (current.Direct) EnsurePost(member, current.Positions[i], command.Goal, tick);
                else if (member.Job?.def.defName != "HD_NewTacticalIngress" || member.Pawn.CurJob != member.Job
                    || member.Pawn.jobs.curDriver is TacticalJobDriver stopped && stopped.AtPost)
                {
                    if (tick < member.RetryTick) continue;
                    if (!CanIssue(member)) continue;
                    Job ingress = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalIngress"),
                        current.Outside, current.Opening, current.Positions[i]);
                    ingress.targetQueueA = new List<LocalTargetInfo> { current.Inside };
                    ingress.count = member.Crossed ? 2 : member.Passed ? 1 : 0;
                    Issue(member, ingress);
                }
            }
            if (active.All(member => member.Entered))
            {
                // Keep the footprint until the final owned post is released.
                // Vanilla next-job selection must not run for a whole squad here.
                BeginReturn(command, tick, false);
            }
        }
        private IntVec3 Goal(int tick)
        {
            if (explicitGoal.IsValid) return explicitGoal;
            if (automaticGoal.IsValid) return automaticGoal;
            if (tick < goalRetry) return IntVec3.Invalid;
            goalRetry = tick + 600;
            foreach (Building_Bed bed in map.listerThings.AllThings.OfType<Building_Bed>())
                if (bed.Spawned && bed.Faction == Faction.OfPlayer && bed.OwnersForReading.Any(pawn => pawn.Name != null))
                    return automaticGoal = bed.Position;
            return IntVec3.Invalid;
        }
        private bool OpeningUsable(TacticalLocalPlan plan)
        {
            Building barrier = plan.Opening.GetEdifice(map);
            return barrier == null ? plan.Opening.Standable(map)
                : barrier is Building_Door door && (door.Open || DoorBreachFaultUtility.Jammed(door));
        }
        private void RememberOpening(TacticalLocalPlan plan)
        {
            if (knownOpenings.Count < 8 && !knownOpenings.Any(known => known.Opening == plan.Opening)) knownOpenings.Add(plan);
        }
        private TacticalLocalPlan ReuseOpening(Pawn leader, IntVec3 goal, int count, Func<IntVec3,bool> claimed,
            out bool tried, out TacticalPlanFailure failure)
        {
            tried = false; failure = TacticalPlanFailure.None; int probes = 0;
            foreach (TacticalLocalPlan known in knownOpenings)
            {
                IntVec3 travel = goal - leader.Position, relative = leader.Position - known.Opening;
                // Reuse only the same approach face, never route an already
                // indoor squad back out or an opposite-edge squad around a base.
                if (travel.x * known.Inward.x + travel.z * known.Inward.z <= 0
                    || relative.x * known.Inward.x + relative.z * known.Inward.z >= 1
                    || known.Outside.DistanceToSquared(leader.Position) > goal.DistanceToSquared(leader.Position)
                    || known.Stack.Count < count || known.Positions.Count < count || !OpeningUsable(known)) continue;
                bool physical = known.Outside.Standable(map) && known.Inside.Standable(map);
                for (int i = 0; physical && i < count; i++)
                    physical = known.Stack[i].Standable(map) && known.Positions[i].Standable(map);
                if (!physical) continue; // Changed obstruction: allow a fresh bounded plan.
                tried = true;
                bool busy = leases.ContainsKey(known.Opening) || claimed(known.Outside) || claimed(known.Inside);
                for (int i = 0; !busy && i < count; i++) busy = claimed(known.Stack[i]) || claimed(known.Positions[i]);
                if (busy) { failure |= TacticalPlanFailure.Busy; continue; }
                if (probes++ >= 2) break;
                if (!leader.CanReach(known.Outside, PathEndMode.OnCell, Danger.Deadly))
                { failure |= TacticalPlanFailure.Unreachable; continue; }
                var plan = new TacticalLocalPlan { Opening = known.Opening, Inward = known.Inward, ExistingOpening = true,
                    Barrier = known.Opening.GetEdifice(map) };
                for (int i = 0; i < count; i++) { plan.Stack.Add(known.Stack[i]); plan.Positions.Add(known.Positions[i]); }
                plan.Interior.UnionWith(known.Interior);
                failure = TacticalPlanFailure.None; return plan;
            }
            return null;
        }
        private static bool AtPost(TacticalMemberCommand member) => member.Pawn.CurJob == member.Job
            && member.Pawn.jobs.curDriver is TacticalJobDriver driver && driver.AtPost
            && member.Pawn.Position == member.Job.targetA.Cell;
        private void EnsurePost(TacticalMemberCommand member, IntVec3 position, IntVec3 face, int tick)
        {
            if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalPost"
                && member.Job.targetA.Cell == position
                && (!(member.Pawn.jobs.curDriver is TacticalJobDriver driver) || !driver.AtPost || member.Pawn.Position == position)) return;
            if (member.Pawn.CurJob?.playerForced == true || tick < member.RetryTick) return;
            if (!CanIssue(member)) return;
            Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"), position, face));
        }
        private void EnsureParking(TacticalSquadCommand command, TacticalMemberCommand member, int tick)
        {
            if (!member.Parking.IsValid || !claims.TryGetValue(member.Parking, out TacticalSquadCommand owner) || owner != command)
            {
                member.Parking = IntVec3.Invalid;
                int candidates = 0;
                for (int radius = 0; radius <= 2 && !member.Parking.IsValid; radius++)
                    for (int x = -radius; x <= radius && !member.Parking.IsValid; x++)
                        for (int z = -radius; z <= radius && !member.Parking.IsValid; z++)
                        {
                            if (Math.Max(Math.Abs(x), Math.Abs(z)) != radius || candidates++ >= 25) continue;
                            IntVec3 cell = member.Pawn.Position + new IntVec3(x,0,z);
                            if (!TacticalLocalPlanner.Free(map, cell, claims.ContainsKey)) continue;
                            Pawn occupant = cell.GetFirstPawn(map);
                            if (occupant != null && occupant != member.Pawn) continue;
                            member.Parking = cell; claims[cell] = command;
                        }
            }
            if (member.Parking.IsValid) EnsurePost(member, member.Parking, command.Goal, tick);
        }
        private static IntVec3 RetryPost(TacticalSquadCommand command, TacticalMemberCommand member)
        {
            if (command.Plan == null) return member.Pawn.Position;
            int index = command.Members.IndexOf(member);
            return member.Crossed || command.Plan.Direct ? command.Plan.Positions[index] : command.Plan.Stack[index];
        }
        private bool Issue(TacticalMemberCommand member, Job job)
        {
            int tick = GenTicks.TicksGame;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryJob(tick))
            {
                Defer(member);
                return false;
            }
            // Set ownership before StartJob; an old job's finish notification
            // cannot clear the replacement. One owner, one persistent job.
            member.Job = job; JobsIssued++;
            member.LastPosition = member.Pawn.Position; member.LastProgressTick = GenTicks.TicksGame;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            long started = Stopwatch.GetTimestamp();
            try
            {
                member.Pawn.jobs.StartJob(job, JobCondition.InterruptForced, resumeCurJobAfterwards: false,
                    cancelBusyStances: true, keepCarryingThingOverride: true);
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            return true;
        }
        private bool CanIssue(TacticalMemberCommand member)
        {
            if (Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.CanJob(GenTicks.TicksGame)) return true;
            Defer(member); return false;
        }
        private void Defer(TacticalMemberCommand member)
        {
            if (byPawn.TryGetValue(member.Pawn, out TacticalSquadCommand command)) command.DeferredWork = true;
        }
        private static void EndOwned(TacticalMemberCommand member, bool startNewJob = true)
        {
            Job owned = member.Job; member.Job = null;
            if (owned != null && member.Pawn.CurJob == owned)
                member.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: startNewJob);
        }
        private void ReleaseClaims(TacticalSquadCommand command)
        {
            foreach (IntVec3 cell in claims.Where(pair => pair.Value == command).Select(pair => pair.Key).ToArray()) claims.Remove(cell);
            foreach (IntVec3 cell in leases.Where(pair => pair.Value == command).Select(pair => pair.Key).ToArray()) leases.Remove(cell);
        }
        private void Release(TacticalSquadCommand command)
        {
            BeginReturn(command, GenTicks.TicksGame, true);
        }
        private static void BeginReturn(TacticalSquadCommand command, int tick, bool release)
        {
            command.Phase = TacticalCommandPhase.Returning; command.ReleaseAfterReturn = release;
            command.ReturnCursor = 0; command.Due = tick + 1;
        }
        private void ReturnMembers(TacticalSquadCommand command, int tick)
        {
            command.Due = tick + 1;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            while (command.ReturnCursor < command.Members.Count)
            {
                TacticalMemberCommand member = command.Members[command.ReturnCursor];
                if (member.Job != null && member.Pawn.Spawned && member.Pawn.Map == map && member.Pawn.CurJob == member.Job)
                {
                    if (!budget.TryJob(tick, returning: true)) return;
                    long started = Stopwatch.GetTimestamp();
                    try { EndOwned(member); }
                    finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                }
                else member.Job = null;
                if (command.ReleaseAfterReturn) byPawn.Remove(member.Pawn);
                command.ReturnCursor++;
            }
            ReleaseClaims(command);
            command.Phase = command.ReleaseAfterReturn ? TacticalCommandPhase.Released : TacticalCommandPhase.Complete;
            command.Due = tick + 600;
        }
        public override void MapRemoved()
        {
            // A removed map cannot keep a deferred queue alive. Clean ownership
            // without doing a new vanilla job search on a map being disposed.
            foreach (TacticalSquadCommand command in squads.Values)
                foreach (TacticalMemberCommand member in command.Members) EndOwned(member, false);
            squads.Clear(); byPawn.Clear(); claims.Clear(); leases.Clear(); knownOpenings.Clear();
        }
    }

    [NewTactical, HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    public static class Patch_NewTactical_RaidCreated
    {
        private static void Postfix(Lord __result) => Current.Game.GetComponent<GameComponent_TacticalCommands>()?.RegisterLord(__result);
    }
}
