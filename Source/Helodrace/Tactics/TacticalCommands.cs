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
    public enum TacticalCommandPhase { Pending, Stack, Breach, Enter, Complete, Released }
    public sealed class TacticalMemberCommand
    {
        public Pawn Pawn;
        public Job Job;
        public int RetryTick;
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
        public int Due, PhaseStarted, Failures;
        public bool HadConnectedStack;
        public Pawn Breacher;
        public bool Terminal => Phase == TacticalCommandPhase.Released;
    }

    // One fair bounded scheduler for ALL maps. No per-map tactical tick/update.
    [NewTactical]
    public sealed class GameComponent_TacticalCommands : GameComponent
    {
        private readonly List<TacticalSquadCommand> commands = new List<TacticalSquadCommand>();
        private int cursor, discoverAt;
        public long Advances, BudgetStops;
        public GameComponent_TacticalCommands(Game game) { }
        public void RegisterLord(Lord lord)
        {
            if (!(lord?.LordJob is LordJob_AssaultColony)) return;
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
            if (tick >= discoverAt)
            {
                discoverAt = tick + 300;
                foreach (RaidTacticalUnit unit in RaidTacticalUnit.All)
                {
                    Pawn pawn = unit.Members.FirstOrDefault(member => member.Spawned && !member.Dead);
                    if (pawn == null || !(pawn.GetLord()?.LordJob is LordJob_AssaultColony)) continue;
                    MapComponent_TacticalCommands service = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
                    if (service != null && (unit.Faction.HostileTo(Faction.OfPlayer) || service.HasExplicitGoal))
                    {
                        TacticalSquadCommand command = service.Register(unit, tick);
                        if (command != null) commands.Add(command);
                    }
                }
                commands.RemoveAll(command => command.Terminal || command.Owner.map.Disposed);
                if (cursor >= commands.Count) cursor = 0;
            }
            if (commands.Count == 0) return;
            long started = Stopwatch.GetTimestamp();
            int visited = 0, processed = 0;
            // Four cheap due checks, at most two real squad decisions in a tick.
            while (visited++ < Math.Min(4, commands.Count) && processed < 2)
            {
                if (cursor >= commands.Count) cursor = 0;
                TacticalSquadCommand command = commands[cursor++];
                if (command.Terminal || command.Due > tick) continue;
                command.Owner.Advance(command, tick); Advances++; processed++;
                if (Stopwatch.GetTimestamp() - started > Stopwatch.Frequency * .0015)
                { BudgetStops++; break; }
            }
        }
    }

    [NewTactical]
    public sealed class MapComponent_TacticalCommands : MapComponent
    {
        private readonly Dictionary<string, TacticalSquadCommand> squads = new Dictionary<string, TacticalSquadCommand>();
        private readonly Dictionary<Pawn, TacticalSquadCommand> byPawn = new Dictionary<Pawn, TacticalSquadCommand>();
        private readonly Dictionary<IntVec3, TacticalSquadCommand> claims = new Dictionary<IntVec3, TacticalSquadCommand>();
        private readonly Dictionary<IntVec3, TacticalSquadCommand> leases = new Dictionary<IntVec3, TacticalSquadCommand>();
        private IntVec3 explicitGoal = IntVec3.Invalid, automaticGoal = IntVec3.Invalid;
        private int goalRetry;
        public long JobsIssued, JobFailures, PlansAttempted, PlansBuilt;
        public IEnumerable<TacticalSquadCommand> Commands => squads.Values;
        public bool HasExplicitGoal => explicitGoal.IsValid;
        public MapComponent_TacticalCommands(Map map) : base(map) { }

        // Public/debug mission input. This also permits an AI-v-AI target.
        public void SetObjective(IntVec3 goal)
        {
            if (!goal.InBounds(map)) throw new ArgumentException("Objective is outside this map.");
            explicitGoal = goal;
            foreach (TacticalSquadCommand command in squads.Values)
            {
                ReleaseClaims(command); command.Plan = null; command.Goal = goal;
                command.Phase = TacticalCommandPhase.Pending; command.Due = GenTicks.TicksGame;
                foreach (TacticalMemberCommand member in command.Members) { member.Passed = member.Crossed = member.Entered = false; member.RetryTick = 0; }
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
            { JobFailures++; member.RetryTick = GenTicks.TicksGame + 180; }
            Wake(pawn);
        }
        internal void PassedOpening(Pawn pawn, Job job, IntVec3 at)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.Plan == null) return;
            TacticalMemberCommand member = command.Members.Find(item => item.Pawn == pawn);
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
            && member.Pawn.GetLord()?.LordJob is LordJob_AssaultColony;

        public void Advance(TacticalSquadCommand command, int tick)
        {
            command.Due = tick + (command.Phase == TacticalCommandPhase.Enter ? 30 : 120);
            var active = command.Members.Where(member => Available(member, map)).ToList();
            if (active.Count == 0) { Release(command); return; }
            if (command.Phase == TacticalCommandPhase.Complete) { command.Due = tick + 600; return; }
            if (command.Phase == TacticalCommandPhase.Pending)
            {
                command.Goal = Goal(tick);
                if (!command.Goal.IsValid) { command.Due = tick + 300; return; }
                Pawn leader = active[0].Pawn;
                Pawn hammer = active.Select(member => member.Pawn).FirstOrDefault(pawn => CompSledgehammerBreach.WornBy(pawn) != null
                    && CompSledgehammerBreach.CanOperate(pawn));
                PlansAttempted++;
                TacticalLocalPlan plan = TacticalLocalPlanner.Find(map, leader, hammer, command.Goal, command.Members.Count,
                    cell => claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command,
                    cell => leases.ContainsKey(cell));
                if (plan == null)
                {
                    // Waiting for another squad's physical footprint does not let
                    // vanilla wander through an unrelated entrance in the meantime.
                    foreach (TacticalMemberCommand member in active)
                        EnsurePost(member, member.Pawn.Position, command.Goal, tick);
                    command.Failures++; command.Due = tick + 240; return;
                }
                ReleaseClaims(command); command.Plan = plan; PlansBuilt++; command.PhaseStarted = tick;
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
                    command.Phase = TacticalCommandPhase.Stack;
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
                { command.Phase = TacticalCommandPhase.Enter; command.PhaseStarted = tick; }
                else if (command.Phase == TacticalCommandPhase.Stack)
                {
                    // A lost straggler must not keep the entire squad frozen.
                    if (ready < active.Count && tick - command.PhaseStarted < 1200) return;
                    command.Phase = TacticalCommandPhase.Breach; command.PhaseStarted = tick;
                }
                if (command.Phase == TacticalCommandPhase.Breach)
                {
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
                        Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalBreach"), current.Barrier,
                            current.Outside, CompSledgehammerBreach.WornBy(worker.Pawn).parent);
                        Issue(worker, job);
                    }
                    command.Due = Math.Min(command.Due, tick + 30);
                    return;
                }
            }
            if (command.Phase != TacticalCommandPhase.Enter) return;
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
                    member.Entered |= member.Pawn.Position == current.Positions[i] && (current.Direct || driver.Crossed && member.Passed);
                if (member.Entered) continue;
                if (current.Direct) EnsurePost(member, current.Positions[i], command.Goal, tick);
                else if (member.Job?.def.defName != "HD_NewTacticalIngress" || member.Pawn.CurJob != member.Job
                    || member.Pawn.jobs.curDriver is TacticalJobDriver stopped && stopped.AtPost)
                {
                    if (tick < member.RetryTick) continue;
                    Job ingress = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalIngress"),
                        current.Outside, current.Opening, current.Positions[i]);
                    ingress.targetQueueA = new List<LocalTargetInfo> { current.Inside };
                    ingress.count = member.Crossed ? 2 : member.Passed ? 1 : 0;
                    Issue(member, ingress);
                }
            }
            if (active.All(member => member.Entered))
            {
                command.Phase = TacticalCommandPhase.Complete; command.Due = tick + 600;
                ReleaseClaims(command);
                // Completed local mission hands combat back to the vanilla AI.
                foreach (TacticalMemberCommand member in active) EndOwned(member);
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
        private static bool AtPost(TacticalMemberCommand member) => member.Pawn.CurJob == member.Job
            && member.Pawn.jobs.curDriver is TacticalJobDriver driver && driver.AtPost
            && member.Pawn.Position == member.Job.targetA.Cell;
        private void EnsurePost(TacticalMemberCommand member, IntVec3 position, IntVec3 face, int tick)
        {
            if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalPost"
                && member.Job.targetA.Cell == position
                && (!(member.Pawn.jobs.curDriver is TacticalJobDriver driver) || !driver.AtPost || member.Pawn.Position == position)) return;
            if (member.Pawn.CurJob?.playerForced == true || tick < member.RetryTick) return;
            Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"), position, face));
        }
        private void Issue(TacticalMemberCommand member, Job job)
        {
            // Set ownership before StartJob; an old job's finish notification
            // cannot clear the replacement. One owner, one persistent job.
            member.Job = job; JobsIssued++;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            member.Pawn.jobs.StartJob(job, JobCondition.InterruptForced, resumeCurJobAfterwards: false,
                cancelBusyStances: true, keepCarryingThingOverride: true);
        }
        private static void EndOwned(TacticalMemberCommand member)
        {
            Job owned = member.Job; member.Job = null;
            if (owned != null && member.Pawn.CurJob == owned) member.Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
        private void ReleaseClaims(TacticalSquadCommand command)
        {
            foreach (IntVec3 cell in claims.Where(pair => pair.Value == command).Select(pair => pair.Key).ToArray()) claims.Remove(cell);
            foreach (IntVec3 cell in leases.Where(pair => pair.Value == command).Select(pair => pair.Key).ToArray()) leases.Remove(cell);
        }
        private void Release(TacticalSquadCommand command)
        {
            command.Phase = TacticalCommandPhase.Released; ReleaseClaims(command);
            foreach (TacticalMemberCommand member in command.Members) { byPawn.Remove(member.Pawn); EndOwned(member); }
        }
        public override void MapRemoved()
        {
            foreach (TacticalSquadCommand command in squads.Values) Release(command);
            squads.Clear(); byPawn.Clear(); claims.Clear(); leases.Clear();
        }
    }

    [NewTactical, HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    public static class Patch_NewTactical_RaidCreated
    {
        private static void Postfix(Lord __result) => Current.Game.GetComponent<GameComponent_TacticalCommands>()?.RegisterLord(__result);
    }
}
