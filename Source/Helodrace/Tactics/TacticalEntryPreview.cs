using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    // Private, disposable speculation. Never serialized, claimed, reported as
    // secured or used to issue Jobs until the actual entry has completed.
    internal sealed class TacticalEntryPreview : IDisposable
    {
        private readonly TacticalLocalPlan source;
        private readonly IntVec3 goal;
        internal readonly TacticalRoomScan Scan;
        internal TacticalPlanPreparation Work;
        internal TacticalRoomFrontier Frontier;
        internal int Cursor, Score = int.MaxValue;
        internal bool Selected, Finished;
        internal TacticalEntryPreview(TacticalSquadCommand command, TacticalRoomScan scan)
        { source = command.Plan; goal = command.Goal; Scan = scan; }
        internal bool Matches(TacticalSquadCommand command) => source == command.Plan && goal == command.Goal;
        public void Dispose() { Work?.Dispose(); Work = null; }
    }
    public sealed partial class TacticalSquadCommand
    {
        internal TacticalEntryPreview EntryPreview;
        internal void CancelEntryPreview() { EntryPreview?.Dispose(); EntryPreview = null; }
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool AdoptEntryPreview(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalRoomFrontier frontier)
        {
            var preview = command.EntryPreview;
            bool valid = preview?.Matches(command) == true && preview.Frontier != null
                && preview.Frontier.Opening == frontier.Opening && preview.Frontier.Inward == frontier.Inward
                && preview.Frontier.Depth == frontier.Depth && preview.Work != null
                && preview.Work.AdoptEntryPreview(command, active, frontier.Inside);
            if (valid) { command.Preparation = preview.Work; preview.Work = null; }
            command.CancelEntryPreview();
            return valid;
        }
        private void AdvanceEntryPreview(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            if (command.Phase != TacticalCommandPhase.Enter || command.Plan == null || command.Plan.ReportedPassage
                || !active.Any(m => m.Entered)) return;
            if (command.EntryPreview?.Matches(command) == false) command.CancelEntryPreview();
            var preview = command.EntryPreview;
            // HIGH's next execution roster is chosen by its parent after this
            // room. Reuse its geometry, not a speculative 13-person footprint.
            if (preview != null && preview.Scan.Finished && (preview.Finished || TacticalHighCqbPolicy.High(command))) return;
            var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (preview == null)
                {
                    IntVec3 seed = TacticalRoomProgress.SurveySeed(command.Plan,
                        active.Where(m => m.Entered).Select(m => m.Pawn.Position),
                        c => c.InBounds(map) && c.Roofed(map) && c.Walkable(map) && !(c.GetEdifice(map) is Building_Door));
                    if (!seed.IsValid) return;
                    preview = command.EntryPreview = new TacticalEntryPreview(command, CreateRoomScan(command, seed));
                }
                if (!preview.Scan.Finished) { RoomScanSteps++; preview.Scan.Step(); return; }
                if (TacticalHighCqbPolicy.High(command)) return;
                if (!preview.Selected)
                {
                    bool securedGoal = command.GoalSecured || preview.Scan.Cells.Contains(command.Goal);
                    for (int i = 0; i < 12 && preview.Cursor < preview.Scan.Frontiers.Count; i++)
                    {
                        var frontier = preview.Scan.Frontiers[preview.Cursor++];
                        if (!TacticalMissionRouting.UnenteredRoom(command, frontier) || preview.Scan.Cells.Contains(frontier.Inside)) continue;
                        int score = FrontierScore(frontier, command, securedGoal);
                        if (score < preview.Score) { preview.Score = score; preview.Frontier = frontier; }
                    }
                    if (preview.Cursor < preview.Scan.Frontiers.Count) return;
                    preview.Selected = true;
                    if (preview.Frontier == null) { preview.Finished = true; return; }
                }
                PrepareStructureTargets(command, tick);
                if (preview.Work != null && !preview.Work.Matches(command, active, preview.Frontier.Inside))
                { preview.Work.Dispose(); preview.Work = null; }
                if (preview.Work == null)
                {
                    var search = new TacticalPlanSearch();
                    search.Start(RoomFrontierSteps(command, active, preview.Frontier, search, preview.Scan.Cells));
                    preview.Work = new TacticalPlanPreparation(command, active, preview.Frontier.Inside)
                    { Stage = TacticalPreparationStage.Search, Search = search };
                }
                bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out var owner) && owner != command;
                if (AdvancePreparation(command, active, Claimed, preview.Work, false, out _, out var failure))
                {
                    preview.Work.Dispose(); preview.Work = null;
                    // Classification queues retain their progress; do not keep
                    // retrying blocked footprints or start furniture work here.
                    if ((failure & TacticalPlanFailure.Classification) == 0) preview.Finished = true;
                }
                else if (preview.Work.Stage == TacticalPreparationStage.Commit) preview.Finished = true;
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }
    }
}
