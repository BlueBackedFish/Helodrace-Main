using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed class TacticalSecuredReport : IExposable
    {
        public const int CellLimit = 16, HistoryLimit = 64;
        public string Origin;
        public int Sequence, SeenTick, ThreatAt = -1;
        public IntVec3 Goal, Opening, Inward;
        public bool PortalUsable, Recheck;
        public List<IntVec3> Cells = new List<IntVec3>();
        public List<TacticalRoomFrontier> Frontiers = new List<TacticalRoomFrontier>();
        public TacticalSecuredReport Copy() => new TacticalSecuredReport { Origin = Origin, Sequence = Sequence,
            SeenTick = SeenTick, ThreatAt = ThreatAt, Goal = Goal, Opening = Opening, Inward = Inward,
            PortalUsable = PortalUsable, Recheck = Recheck, Cells = Cells.ToList(),
            Frontiers = Frontiers.Select(f => new TacticalRoomFrontier { Opening = f.Opening, Inward = f.Inward,
                Preference = f.Preference, Outdoors = f.Outdoors }).ToList() };
        public void ExposeData()
        {
            Scribe_Values.Look(ref Origin, "origin"); Scribe_Values.Look(ref Sequence, "sequence");
            Scribe_Values.Look(ref SeenTick, "seenTick"); Scribe_Values.Look(ref ThreatAt, "threatAt", -1);
            Scribe_Values.Look(ref Goal, "goal"); Scribe_Values.Look(ref Opening, "opening"); Scribe_Values.Look(ref Inward, "inward");
            Scribe_Values.Look(ref PortalUsable, "portalUsable"); Scribe_Values.Look(ref Recheck, "recheck");
            Scribe_Collections.Look(ref Cells, "cells", LookMode.Value);
            Scribe_Collections.Look(ref Frontiers, "frontiers", LookMode.Deep);
        }
    }

    public sealed class TacticalSecuredKnowledge : IExposable
    {
        public List<TacticalSecuredReport> Own = new List<TacticalSecuredReport>();
        public List<TacticalSecuredReport> Received = new List<TacticalSecuredReport>();
        public int Sequence;
        private readonly Dictionary<IntVec3, int> receivedCells = new Dictionary<IntVec3, int>();
        private bool indexed;
        private void Index(TacticalSecuredReport report, int delta)
        {
            foreach (IntVec3 cell in report.Cells)
            {
                receivedCells.TryGetValue(cell, out int count);
                if (count + delta <= 0) receivedCells.Remove(cell); else receivedCells[cell] = count + delta;
            }
        }
        private void EnsureIndex()
        {
            if (indexed) return;
            receivedCells.Clear(); foreach (TacticalSecuredReport report in Received) if (!report.Recheck) Index(report, 1);
            indexed = true;
        }
        public bool Cleared(IntVec3 cell) { EnsureIndex(); return receivedCells.ContainsKey(cell); }
        public void Record(TacticalSecuredReport report)
        {
            report.Sequence = ++Sequence;
            if (Own.Count >= TacticalSecuredReport.HistoryLimit) Own.RemoveAt(0);
            Own.Add(report);
        }
        public bool Receive(TacticalSecuredReport report, string source, IntVec3 goal, int tick)
        {
            if (report == null || report.Origin != source || report.Goal != goal || report.SeenTick > tick
                || report.Sequence <= 0 || report.Cells.Count > TacticalSecuredReport.CellLimit || report.Frontiers.Count > 2
                || Received.Exists(r => r.Origin == source && r.Sequence == report.Sequence)) return false;
            EnsureIndex();
            TacticalSecuredReport snapshot = report.Copy();
            foreach (TacticalSecuredReport old in Received)
                if (old.Opening == snapshot.Opening && old.ThreatAt >= snapshot.SeenTick)
                { snapshot.Recheck = true; snapshot.ThreatAt = Math.Max(snapshot.ThreatAt, old.ThreatAt); }
            if (Received.Count >= TacticalSecuredReport.HistoryLimit)
            {
                TacticalSecuredReport oldest = Received[0]; Received.RemoveAt(0);
                if (!oldest.Recheck) Index(oldest, -1);
            }
            Received.Add(snapshot); if (!snapshot.Recheck) Index(snapshot, 1); return true;
        }
        public void ObserveThreat(IntVec3 position, IntVec3 area, int tick)
        {
            EnsureIndex();
            var affected = new HashSet<IntVec3>();
            foreach (TacticalSecuredReport report in Received)
                if (report.Cells.Contains(position) || report.Cells.Contains(area) || report.Opening == position) affected.Add(report.Opening);
            foreach (TacticalSecuredReport report in Own)
                if (report.Cells.Contains(position) || report.Cells.Contains(area) || report.Opening == position) affected.Add(report.Opening);
            foreach (TacticalSecuredReport report in Received)
                if (affected.Contains(report.Opening))
                {
                    report.ThreatAt = Math.Max(report.ThreatAt, tick);
                    if (!report.Recheck) { Index(report, -1); report.Recheck = true; }
                }
            // Old local clear reports must not be sent as current safe progress
            // after this squad actually observed reinfiltration there.
            foreach (TacticalSecuredReport report in Own)
                if (affected.Contains(report.Opening))
                { report.ThreatAt = Math.Max(report.ThreatAt, tick); report.Recheck = true; }
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Sequence, "sequence");
            Scribe_Collections.Look(ref Own, "own", LookMode.Deep);
            Scribe_Collections.Look(ref Received, "received", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) indexed = false;
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        public TacticalSecuredKnowledge SecuredKnowledge = new TacticalSecuredKnowledge();
        internal readonly List<IntVec3> ReportBatch = new List<IntVec3>(TacticalSecuredReport.CellLimit);
        internal int ReportFrontierCursor;
    }
    public sealed partial class TacticalSquadLink
    {
        public Dictionary<string, int> SecuredAcks = new Dictionary<string, int>();
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private void ReportSecuredCell(TacticalSquadCommand command, IntVec3 cell, int tick)
        {
            command.ReportBatch.Add(cell);
            if (command.ReportBatch.Count == TacticalSecuredReport.CellLimit) FlushSecuredReport(command, tick);
        }
        private void FlushSecuredReport(TacticalSquadCommand command, int tick, bool portalOnly = false)
        {
            if (command.Plan == null || !portalOnly && command.ReportBatch.Count == 0) return;
            TacticalLocalPlan plan = command.Plan;
            var report = new TacticalSecuredReport { Origin = command.Id, Goal = command.Goal, SeenTick = tick,
                Opening = plan.Opening, Inward = plan.Inward, PortalUsable = !plan.Direct && OpeningUsable(plan),
                Cells = command.ReportBatch.ToList() };
            if (command.RoomScan != null)
                for (int i = 0; i < 2 && command.ReportFrontierCursor < command.RoomScan.Frontiers.Count; i++)
                {
                    TacticalRoomFrontier frontier = command.RoomScan.Frontiers[command.ReportFrontierCursor++];
                    report.Frontiers.Add(new TacticalRoomFrontier { Opening = frontier.Opening, Inward = frontier.Inward,
                        Preference = frontier.Preference, Outdoors = frontier.Outdoors });
                }
            command.ReportBatch.Clear(); command.SecuredKnowledge.Record(report);
        }
        internal void ReceiveSecuredReport(TacticalSquadCommand command, TacticalSecuredReport report)
        {
            bool added = false;
            if (report.PortalUsable)
            {
                if (command.Link.KnownPortals.Count >= 8) command.Link.KnownPortals.Remove(command.Link.KnownPortals.First());
                command.Link.KnownPortals.Add(report.Opening);
            }
            foreach (TacticalRoomFrontier frontier in report.Recheck ? Enumerable.Empty<TacticalRoomFrontier>() : report.Frontiers)
                if (command.FrontierKeys.Add(frontier.Opening))
                {
                    command.Frontiers.Add(new TacticalRoomFrontier { Opening = frontier.Opening, Inward = frontier.Inward,
                        Preference = frontier.Preference, Outdoors = frontier.Outdoors, Approach = true });
                    added = true;
                }
            if (added)
            {
                command.FrontierCursor = 0;
                if (command.Phase == TacticalCommandPhase.Complete)
                {
                    command.Phase = TacticalCommandPhase.Clear; command.Mission.Stage = TacticalMissionStage.Searching;
                    command.Mission.EmptySurveys = 0; command.Mission.SearchAt = 0;
                }
            }
            // No copy to SecuredCells or own reports: a report is knowledge,
            // neither physical entry nor permission to relay a peer's report.
        }
        private TacticalLocalPlan ReuseReportedOpening(TacticalSquadCommand command, Pawn leader, IntVec3 goal, int count,
            Func<IntVec3, bool> claimed, out bool tried, out TacticalPlanFailure failure)
        {
            tried = false; failure = TacticalPlanFailure.None;
            int candidates = 0; var seen = new HashSet<IntVec3>();
            List<TacticalSecuredReport> reports = command.SecuredKnowledge.Received;
            for (int i = reports.Count - 1; i >= 0 && candidates < 2; i--)
            {
                TacticalSecuredReport report = reports[i];
                if (!report.PortalUsable || !command.Link.KnownPortals.Contains(report.Opening) || !seen.Add(report.Opening)
                    || command.SecuredCells.Contains(report.Opening + report.Inward) || report.Goal != command.Goal) continue;
                var plan = new TacticalLocalPlan { Opening = report.Opening, Inward = report.Inward, ExistingOpening = true };
                IntVec3 travel = goal - leader.Position, relative = leader.Position - plan.Opening;
                if (travel.x * plan.Inward.x + travel.z * plan.Inward.z <= 0
                    || relative.x * plan.Inward.x + relative.z * plan.Inward.z >= 1
                    || plan.Outside.DistanceToSquared(leader.Position) > goal.DistanceToSquared(leader.Position)) continue;
                candidates++; tried = true;
                Building live = plan.Opening.GetEdifice(map);
                if (live?.Faction != null && live.Faction != command.TargetFaction)
                { failure |= TacticalPlanFailure.NotBoundary; continue; }
                if (leases.ContainsKey(plan.Opening)) { failure |= TacticalPlanFailure.Busy; continue; }
                if (!OpeningUsable(plan)) { failure |= TacticalPlanFailure.Obstructed; continue; }
                if (!TacticalLocalPlanner.Free(map, plan.Outside, claimed) || !TacticalLocalPlanner.Free(map, plan.Inside, claimed)
                    || !TacticalLocalPlanner.BuildStack(map, plan, count, claimed)
                    || !TacticalLocalPlanner.BuildPositions(map, plan, count, claimed, c => c.Roofed(map) == plan.Inside.Roofed(map)))
                { failure |= TacticalPlanFailure.Obstructed; continue; }
                if (!leader.CanReach(plan.Outside, PathEndMode.OnCell, Danger.Deadly))
                { failure |= TacticalPlanFailure.Unreachable; continue; }
                plan.Barrier = plan.Opening.GetEdifice(map); failure = TacticalPlanFailure.None; return plan;
            }
            return null;
        }
    }
}
