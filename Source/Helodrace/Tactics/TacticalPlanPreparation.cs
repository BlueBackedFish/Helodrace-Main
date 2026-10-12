using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    // Main-thread cooperative work: one bounded iterator slice per allowance.
    // No background access to live Map/Pawn objects and no partial plan claims.
    public sealed class TacticalPlanSearch : IDisposable
    {
        private IEnumerator<TacticalLocalPlan> steps;
        public TacticalPlanFailure Failure;
        public TacticalLocalPlan Plan;
        public bool Complete;
        public void Start(IEnumerable<TacticalLocalPlan> source) { steps = source.GetEnumerator(); }
        public bool Step()
        {
            if (Complete) return true;
            if (!steps.MoveNext()) { Complete = true; Dispose(); }
            else if (steps.Current != null) { Plan = steps.Current; Complete = true; Dispose(); }
            return Complete;
        }
        public void Dispose() { steps?.Dispose(); steps = null; Complete = true; }
    }
    internal enum TacticalPreparationStage { Start, Search, Allocate, Commit }
    public sealed class TacticalPlanPreparation : IDisposable
    {
        internal TacticalPreparationStage Stage;
        internal TacticalPlanSearch Search;
        internal TacticalEntryAllocator Allocation;
        internal TacticalLocalPlan Candidate;
        internal TacticalPlanFailure Failure;
        private readonly IntVec3 goal;
        private readonly TacticalLocalPlan sourcePlan;
        private readonly Pawn[] members;
        private readonly TacticalCommandPhase phase;
        private readonly string agreement;
        public TacticalPlanPreparation(TacticalSquadCommand command, IList<TacticalMemberCommand> active, IntVec3 goal)
        { this.goal = goal; sourcePlan = command.Plan; phase = command.Phase; members = active.Select(m => m.Pawn).ToArray();
            agreement = command.Link.Cooperation.Active ? command.Link.Cooperation.Agenda.Id : null; }
        public bool Matches(TacticalSquadCommand command, IList<TacticalMemberCommand> active, IntVec3 destination)
        {
            if (goal != destination || sourcePlan != command.Plan || phase != command.Phase || members.Length != active.Count) return false;
            if (agreement != (command.Link.Cooperation.Active ? command.Link.Cooperation.Agenda.Id : null)) return false;
            for (int i = 0; i < members.Length; i++) if (members[i] != active[i].Pawn) return false;
            return true;
        }
        public void Dispose() { Search?.Dispose(); Search = null; }
    }
    public sealed partial class TacticalSquadCommand
    {
        // Recreated after load from the persisted phase/frontier; never save
        // closures, unfinished footprints or fabricated physical entry.
        public TacticalPlanPreparation Preparation;
        internal void CancelPreparation() { Preparation?.Dispose(); Preparation = null; }
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool PreparePendingPlan(TacticalSquadCommand command, List<TacticalMemberCommand> active, Pawn leader,
            IntVec3 goal, Func<IntVec3, bool> claimed, out TacticalLocalPlan plan, out TacticalPlanFailure failure)
        {
            plan = null; failure = TacticalPlanFailure.None;
            if (command.Preparation != null && !command.Preparation.Matches(command, active, goal)) command.CancelPreparation();
            if (command.Preparation == null) command.Preparation = new TacticalPlanPreparation(command, active, goal);
            TacticalPlanPreparation work = command.Preparation;
            if (work.Stage != TacticalPreparationStage.Start) return FinishPreparation(command, active, claimed, out plan, out failure);
            PlansAttempted++;
            int frontage = !command.Mission.NextGoal.IsValid && command.Link.Cooperation.Active && command.Failures < 2
                ? command.Link.Cooperation.Agenda.Side(command.Id) * 12 : 0;
            bool triedKnown = false;
            bool RecoverFurniture(TacticalLocalPlan candidate) => TryPrepareFurniture(command, active, candidate,
                claimed, claimed, cell => cell.InBounds(map) && cell.Roofed(map) == candidate.Inside.Roofed(map)
                    && !command.SecuredCells.Contains(cell), requireFull: true);
            work.Candidate = ResumeFurniturePlan(command, active, leader, claimed);
            if (work.Candidate == null && command.FurnitureAction == null)
                work.Candidate = frontage == 0 ? ReuseOpening(command, leader, goal, command.Members.Count, claimed,
                    out triedKnown, out failure) : ReuseReportedOpening(command, leader, goal, command.Members.Count,
                        claimed, out triedKnown, out failure, onlyCleared: true);
            work.Failure = failure;
            if (work.Candidate != null && !work.Candidate.Direct && work.Candidate.Stack.Count == 0)
            {
                work.Search = TacticalLocalPlanner.ReusedFootprint(map, leader, work.Candidate, command.Members.Count, claimed);
                work.Candidate = null; work.Stage = TacticalPreparationStage.Search; return false;
            }
            if (work.Candidate == null && command.FurnitureAction == null
                && TacticalLocalPlanner.SearchFreshOpening(triedKnown, failure, command.Failures))
            {
                if (triedKnown) BusyOpeningFallbacks++;
                work.Search = TacticalLocalPlanner.Find(map, leader, goal, command.Members.Count,
                    claimed, cell => leases.ContainsKey(cell), barrier => active.Any(member => TacticalBreachTools.CanUse(member.Pawn, barrier)),
                    out failure, command.Failures, frontage, command.SecuredCells.Count == 0 ? null : (Func<IntVec3, bool>)command.SecuredCells.Contains,
                    (opening, inward) => ClassifyStructure(command, opening, inward, leader), RecoverFurniture,
                    candidate => TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, candidate));
                work.Stage = TacticalPreparationStage.Search;
            }
            else work.Stage = TacticalPreparationStage.Allocate;
            return false;
        }
        private bool FinishPreparation(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            Func<IntVec3, bool> claimed, out TacticalLocalPlan plan, out TacticalPlanFailure failure)
        {
            TacticalPlanPreparation work = command.Preparation;
            plan = null; failure = work.Failure;
            if (work.Stage == TacticalPreparationStage.Search)
            {
                if (!work.Search.Step()) return false;
                work.Candidate = work.Search.Plan; work.Failure |= work.Search.Failure;
                work.Search.Dispose(); work.Search = null;
                work.Stage = TacticalPreparationStage.Allocate;
                if (work.Candidate != null) return false;
            }
            if (work.Candidate == null)
            { failure = work.Failure; command.CancelPreparation(); return true; }
            if (work.Stage == TacticalPreparationStage.Allocate)
            {
                if (!work.Candidate.Direct && work.Candidate.EntryOrder.Count != command.Members.Count)
                {
                    if (work.Allocation == null)
                    {
                        work.Allocation = new TacticalEntryAllocator(work.Candidate, TacticalEntryOrder.For(command, active, work.Candidate),
                            work.Candidate.ReportedPassage ? (Func<IntVec3,bool>)null : cell => !GenSight.LineOfSight(work.Candidate.Opening, cell, map, true));
                        return false;
                    }
                    if (!work.Allocation.Step()) return false;
                    if (!work.Allocation.Valid)
                    { failure = work.Failure | TacticalPlanFailure.Inside; command.CancelPreparation(); return true; }
                }
                work.Stage = TacticalPreparationStage.Commit; return false;
            }
            TacticalLocalPlan candidate = work.Candidate;
            bool Free(IntVec3 cell) => TacticalFurniturePolicy.FreeIgnoring(map, cell, claimed, candidate.PendingFurniture);
            // Other squads can reserve space while this work is suspended.
            if (candidate.Positions.Count != command.Members.Count || candidate.Positions.Any(c => !Free(c))
                || !candidate.Direct && (!candidate.ReportedPassage && (candidate.Stack.Count != command.Members.Count
                    || candidate.Stack.Any(c => !Free(c)) || !Free(candidate.Outside) || !Free(candidate.Inside))
                    || Enumerable.Range(0, candidate.Depth).Any(i => !candidate.ReportedPassage
                        && leases.TryGetValue(candidate.Opening + candidate.Inward * i, out TacticalSquadCommand owner) && owner != command)
                    || !candidate.ReportedPassage && candidate.EntryLane.IsValid && !Free(candidate.EntryLane)))
            { failure = work.Failure | TacticalPlanFailure.Busy; command.CancelPreparation(); return true; }
            if (!candidate.Direct)
            {
                if (candidate.ReportedPassage && !TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, candidate))
                { failure = TacticalPlanFailure.Unsecured; command.CancelPreparation(); return true; }
                candidate.Barrier = TacticalLocalPlanner.NextBarrier(map, candidate);
                candidate.ExistingOpening = OpeningUsable(candidate);
                if (candidate.ReportedPassage && !candidate.ExistingOpening
                    || !candidate.ExistingOpening && !active.Any(m => CanBreachPlan(m.Pawn, candidate)))
                { failure = work.Failure | TacticalPlanFailure.Obstructed; command.CancelPreparation(); return true; }
            }
            plan = candidate; failure = TacticalPlanFailure.None; command.CancelPreparation(); return true;
        }
    }
}
