using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helodrace.Squads;
using Verse;

namespace Helodrace
{
    internal sealed class RaidExecutionTicket
    {
        internal readonly Map Map;
        internal readonly MapComponent_RaidTacticalExecution Owner;
        internal RaidTacticalUnit Unit;
        internal List<Pawn> Members;
        internal int ReviewAfter, AdmissionAfter, FallbackAfter;
        internal RaidExecutionTicket(Map map, MapComponent_RaidTacticalExecution owner, RaidTacticalUnit unit)
        { Map = map; Owner = owner; Unit = unit; }
    }

    // Shared across maps; a busy first map cannot consume a separate allowance
    // before all other maps. Only main-thread tick work is scheduled here.
    public sealed class GameComponent_RaidExecutionScheduler : GameComponent
    {
        private readonly TacticalDueQueue<RaidExecutionTicket> queue = new TacticalDueQueue<RaidExecutionTicket>();
        internal long Updates, BudgetStops, UrgentUpdates;
        internal int MaximumDelay;
        internal int Pending => queue.Count;
        internal double MaximumUnitMilliseconds;
        private int processedTick = -1;
        public GameComponent_RaidExecutionScheduler(Game game) { }
        internal void Schedule(RaidExecutionTicket ticket, int due) => queue.Schedule(ticket, due);
        internal void Cancel(RaidExecutionTicket ticket) => queue.Remove(ticket);
        internal int OldestDelay => queue.OldestDelay(GenTicks.TicksGame);
        public override void GameComponentTick()
        {
            int tick = GenTicks.TicksGame;
            if (processedTick == tick) return;
            processedTick = tick;
            long started = Stopwatch.GetTimestamp(), allowance = Stopwatch.Frequency * 2 / 1000;
            int processed = 0;
            while (processed < 8 && Stopwatch.GetTimestamp() - started < allowance
                && queue.TryTake(tick, out RaidExecutionTicket ticket, out int deadline))
            {
                if (!Find.Maps.Contains(ticket.Map) || !ticket.Owner.CurrentTicket(ticket)) continue;
                processed++; Updates++; MaximumDelay = Math.Max(MaximumDelay, tick - deadline);
                Run(ticket, tick, false);
            }
            if (queue.HasDue(tick)) BudgetStops++;
        }
        internal void Run(RaidExecutionTicket ticket, int tick, bool urgent)
        {
            long started = Stopwatch.GetTimestamp();
            if (urgent) { queue.Remove(ticket); UrgentUpdates++; }
            try
            {
                using (RaidCpuProfiler.Measure(ticket.Map, RaidCpuStage.Execution))
                    ticket.Owner.RunScheduledUnit(ticket, tick, urgent);
            }
            catch (Exception error)
            {
                ticket.ReviewAfter = tick + 60;
                Log.Error("Raid execution " + ticket.Unit.Id + ": " + error);
            }
            finally
            {
                MaximumUnitMilliseconds = Math.Max(MaximumUnitMilliseconds,
                    (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
                if (ticket.Owner.CurrentTicket(ticket)) queue.Schedule(ticket, tick + 10);
            }
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private readonly Dictionary<string, RaidExecutionTicket> executionTickets = new Dictionary<string, RaidExecutionTicket>();
        private int rosterAfter;
        private GameComponent_RaidExecutionScheduler ExecutionScheduler =>
            Verse.Current.Game.GetComponent<GameComponent_RaidExecutionScheduler>();
        internal bool CurrentTicket(RaidExecutionTicket ticket) =>
            executionTickets.TryGetValue(ticket.Unit.Id, out RaidExecutionTicket current) && current == ticket;

        private RaidExecutionTicket RegisterUnit(RaidTacticalUnit unit, int tick)
        {
            if (executionTickets.TryGetValue(unit.Id, out RaidExecutionTicket existing))
            { existing.Unit = unit; existing.Members = LiveMembers(unit); return existing; }
            var ticket = new RaidExecutionTicket(map, this, unit);
            ticket.Members = LiveMembers(unit);
            executionTickets[unit.Id] = ticket;
            ExecutionScheduler.Schedule(ticket, tick + RaidCommunicationPolicy.ScanOffset(unit.Id) % 10);
            return ticket;
        }

        private List<Pawn> LiveMembers(RaidTacticalUnit unit) => unit.Members.Where(pawn => pawn?.Spawned == true
            && pawn.Map == map && !pawn.Dead && !pawn.Downed && !pawn.Destroyed && IsTacticalRaider(pawn)).ToList();

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int tick = GenTicks.TicksGame;
            if (tick < rosterAfter && pendingCasualties.Count == 0) return;
            using (RaidCpuProfiler.Measure(map, RaidCpuStage.ExecutionMaintenance))
            {
                if (tick >= rosterAfter) RefreshExecutionRoster(tick);
                if (pendingCasualties.Count == 0) return;
                var losses = pendingCasualties.ToArray(); pendingCasualties.Clear();
                foreach (var loss in losses)
                {
                    RaidExecutionTicket ticket = executionTickets.TryGetValue(loss.Key, out var found) ? found : null;
                    if (ticket == null)
                    {
                        RaidTacticalUnit unit = RaidTacticalUnit.All.FirstOrDefault(value => value.Id == loss.Key);
                        if (unit != null) ticket = RegisterUnit(unit, tick);
                    }
                    if (ticket == null) continue;
                    OrganizationAPI.Registry?.ReevaluateCommand(ticket.Unit.Organization, tick);
                    map.GetComponent<MapComponent_RaidTacticalPlans>().InvalidateDecision(loss.Key);
                    foreach (Pawn lost in loss.Value) map.GetComponent<MapComponent_RaidTacticalOrders>()?.Forget(lost);
                    // Casualties bypass the ordinary deadline and allowance. No Job
                    // changes occur inside Pawn.Kill/Downed notification itself.
                    ExecutionScheduler.Run(ticket, tick, true);
                }
            }
        }

        private void RefreshExecutionRoster(int tick)
        {
            rosterAfter = tick + 60;
            var live = new HashSet<string>();
            var toolOwners = new HashSet<string>();
            foreach (RaidTacticalUnit unit in RaidTacticalUnit.All)
            {
                if (unit.Members.Any(pawn => pawn != null && !pawn.Dead && pawn.MapHeld == map)) toolOwners.Add(unit.Id);
                if (unit.Members.Any(pawn => pawn?.Spawned == true && pawn.Map == map
                    && !pawn.Dead && !pawn.Downed && IsTacticalRaider(pawn)))
                { live.Add(unit.Id); RegisterUnit(unit, tick); }
            }
            foreach (string id in executionTickets.Keys.Where(id => !live.Contains(id)
                && !pendingCasualties.ContainsKey(id)).ToList())
            {
                ExecutionScheduler.Cancel(executionTickets[id]); executionTickets.Remove(id);
                if (states.TryGetValue(id, out ExecutionState removed)) CancelPendingCharge(removed);
                states.Remove(id); waitingStructures.Remove(id);
            }
            // Loaded execution state can predate any runtime ticket registration.
            foreach (string id in states.Keys.Where(id => !live.Contains(id)
                && !pendingCasualties.ContainsKey(id)).ToList())
            { CancelPendingCharge(states[id]); states.Remove(id); }
            waitingStructures.RemoveWhere(id => !live.Contains(id));
            PruneBreachTools(toolOwners);
            PrunePassageTraffic(tick);
        }

        public override void MapRemoved()
        {
            foreach (RaidExecutionTicket ticket in executionTickets.Values) ExecutionScheduler.Cancel(ticket);
            executionTickets.Clear(); pendingCasualties.Clear();
            passageTraffic.Clear(); ingressGoals.Prune(_ => false); ingressYields.Prune(_ => false); transitMouths.Clear();
            base.MapRemoved();
        }

        internal void RunScheduledUnit(RaidExecutionTicket ticket, int tick, bool urgent)
        {
            RaidTacticalUnit unit = ticket.Unit;
            bool review = urgent || tick >= ticket.ReviewAfter;
            if (review) ticket.Members = LiveMembers(unit);
            else ticket.Members.RemoveAll(pawn => !pawn.Spawned || pawn.Map != map || pawn.Dead || pawn.Downed
                || RaidTacticalUnit.ForPawn(pawn)?.Id != unit.Id);
            List<Pawn> members = ticket.Members;
            states.TryGetValue(unit.Id, out ExecutionState state);
            if (!review && state?.ActivePlan?.Success == true && members.Count > 0)
            {
                RefreshContacts(members, state.ActivePlan, state, tick);
                bool reacting = EmergencyReactions(members, state.ActivePlan, state, tick)
                    || RespondToFire(members, state.ActivePlan, state, tick)
                    || RespondToCqbContacts(members, state.ActivePlan, state, tick);
                if (!reacting && state.Phase == RaidExecutionPhase.CrossBreach)
                    Update(unit, members, state.ActivePlan, state, tick);
                if (!reacting && state.SharedOpeningWait && tick >= ticket.AdmissionAfter)
                {
                    ticket.AdmissionAfter = tick + 30;
                    if (!FieldDefense(members, state.ActivePlan, state, tick)
                        && !WaitForSharedOpening(members, state.ActivePlan, state, tick)) review = true;
                }
                if (reacting) ticket.ReviewAfter = Math.Min(ticket.ReviewAfter, tick + 10);
            }
            if (!review) return;
            bool fallbackDue = tick >= ticket.FallbackAfter;
            if (fallbackDue) ticket.FallbackAfter = tick + 90;
            ExecuteUnit(unit, members, tick, fallbackDue);
            states.TryGetValue(unit.Id, out state);
            int interval = state?.SharedOpeningWait == true && !state.ContactPause
                && state.DefenseUntil <= tick && !state.ApproachSmokeActive && state.Reactions.Count == 0 ? 180
                : state != null && (state.ContactPause || state.DefenseUntil > tick
                    || state.ApproachSmokeActive || state.Reactions.Count > 0) ? 30
                : state == null || state.Phase == RaidExecutionPhase.Hold || state.Phase == RaidExecutionPhase.Complete ? 90
                : state.Phase == RaidExecutionPhase.Assemble && !state.ApproachComplete ? 60 : 30;
            ticket.ReviewAfter = tick + interval;
            ticket.AdmissionAfter = tick + 30;
        }
    }
}
