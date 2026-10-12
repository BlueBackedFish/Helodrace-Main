using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalSharedPassagePolicy
    {
        public static bool Cleared(TacticalSecuredKnowledge knowledge, TacticalLocalPlan plan) =>
            knowledge.Cleared(plan.Direct ? plan.Opening : plan.Inside)
            || !plan.Direct && knowledge.ClearedPassage(plan.Opening, plan.Inward, plan.Depth);
        public static int IngressLeg(bool crossed, bool passed, bool cleared) => crossed ? 2 : passed || cleared ? 1 : 0;
        public static bool Reconsider(TacticalCommandPhase phase, bool cleared, bool direct, bool charge, bool launched) =>
            cleared && !direct && !charge && !launched && (phase == TacticalCommandPhase.Stack
                || phase == TacticalCommandPhase.Breach || phase == TacticalCommandPhase.Observe || phase == TacticalCommandPhase.Support);
    }
    public sealed class TacticalSecuredReport : IExposable
    {
        public const int CellLimit = 16, HistoryLimit = 256;
        public string Origin;
        public int Sequence, SeenTick, ThreatAt = -1;
        public int Depth = 1;
        public IntVec3 Goal, Opening, Inward;
        public bool PortalUsable, Recheck;
        public List<IntVec3> Cells = new List<IntVec3>();
        public List<TacticalRoomFrontier> Frontiers = new List<TacticalRoomFrontier>();
        public TacticalSecuredReport Copy() => new TacticalSecuredReport { Origin = Origin, Sequence = Sequence,
            SeenTick = SeenTick, ThreatAt = ThreatAt, Goal = Goal, Opening = Opening, Inward = Inward, Depth = Depth,
            PortalUsable = PortalUsable, Recheck = Recheck, Cells = Cells.ToList(),
            Frontiers = Frontiers.Select(f => new TacticalRoomFrontier { Opening = f.Opening, Inward = f.Inward,
                Depth = f.Depth, Preference = f.Preference, Outdoors = f.Outdoors }).ToList() };
        public void ExposeData()
        {
            Scribe_Values.Look(ref Origin, "origin"); Scribe_Values.Look(ref Sequence, "sequence");
            Scribe_Values.Look(ref SeenTick, "seenTick"); Scribe_Values.Look(ref ThreatAt, "threatAt", -1);
            Scribe_Values.Look(ref Goal, "goal"); Scribe_Values.Look(ref Opening, "opening"); Scribe_Values.Look(ref Inward, "inward");
            Scribe_Values.Look(ref Depth, "depth", 1);
            Scribe_Values.Look(ref PortalUsable, "portalUsable"); Scribe_Values.Look(ref Recheck, "recheck");
            Scribe_Collections.Look(ref Cells, "cells", LookMode.Value);
            Scribe_Collections.Look(ref Frontiers, "frontiers", LookMode.Deep);
        }
    }

    public sealed class TacticalSecuredKnowledge : IExposable
    {
        public List<TacticalSecuredReport> Records = new List<TacticalSecuredReport>();
        public int Sequence;
        private readonly Dictionary<IntVec3, int> clearedCells = new Dictionary<IntVec3, int>();
        private readonly Dictionary<IntVec3, HashSet<TacticalSecuredReport>> byCell = new Dictionary<IntVec3, HashSet<TacticalSecuredReport>>();
        private readonly Dictionary<IntVec3, List<TacticalSecuredReport>> byOpening = new Dictionary<IntVec3, List<TacticalSecuredReport>>();
        private bool indexed;
        private void Index(TacticalSecuredReport report, int delta)
        {
            foreach (IntVec3 cell in report.Cells)
            {
                clearedCells.TryGetValue(cell, out int count);
                if (count + delta <= 0) clearedCells.Remove(cell); else clearedCells[cell] = count + delta;
            }
        }
        private void EnsureIndex()
        {
            if (indexed) return;
            clearedCells.Clear(); byCell.Clear(); byOpening.Clear();
            foreach (TacticalSecuredReport report in Records) AddIndex(report);
            indexed = true;
        }
        private void AddIndex(TacticalSecuredReport report)
        {
            if (!byOpening.TryGetValue(report.Opening, out List<TacticalSecuredReport> group))
                byOpening[report.Opening] = group = new List<TacticalSecuredReport>();
            group.Add(report);
            foreach (IntVec3 cell in report.Cells)
            {
                if (!byCell.TryGetValue(cell, out HashSet<TacticalSecuredReport> entries)) byCell[cell] = entries = new HashSet<TacticalSecuredReport>();
                entries.Add(report);
            }
            if (!report.Recheck) Index(report, 1);
        }
        private void RemoveIndex(TacticalSecuredReport report)
        {
            if (!report.Recheck) Index(report, -1);
            List<TacticalSecuredReport> group = byOpening[report.Opening]; group.Remove(report);
            if (group.Count == 0) byOpening.Remove(report.Opening);
            foreach (IntVec3 cell in report.Cells)
                if (byCell.TryGetValue(cell, out HashSet<TacticalSecuredReport> entries))
                { entries.Remove(report); if (entries.Count == 0) byCell.Remove(cell); }
        }
        public bool Cleared(IntVec3 cell) { EnsureIndex(); return clearedCells.ContainsKey(cell); }
        public bool KnownOpening(IntVec3 cell) { EnsureIndex(); return byOpening.ContainsKey(cell); }
        public bool KnownPassage(IntVec3 opening, IntVec3 inward, int depth)
        {
            EnsureIndex();
            return byOpening.TryGetValue(opening, out List<TacticalSecuredReport> group)
                && group.Exists(r => r.PortalUsable && r.Inward == inward && r.Depth == depth);
        }
        public bool ClearedPassage(IntVec3 opening, IntVec3 inward, int depth)
        {
            EnsureIndex();
            // The mouth may be unroofed by the blast and absent from the floor
            // survey. Require actual floor-clear evidence, not a portal-only
            // report. Destroyed adjoining wall layers can shorten the channel.
            return byOpening.TryGetValue(opening, out List<TacticalSecuredReport> group)
                && group.Exists(r => r.PortalUsable && !r.Recheck && r.Cells.Count > 0
                    && r.Inward == inward && r.Depth >= depth);
        }
        public bool Record(TacticalSecuredReport report)
        {
            if (report == null || report.Depth < 1 || report.Depth > TacticalPortalGeometry.LayerLimit
                || report.Cells.Count > TacticalSecuredReport.CellLimit || report.Frontiers.Count > 2
                || report.Frontiers.Any(f => f.Depth < 1 || f.Depth > TacticalPortalGeometry.LayerLimit)) return false;
            EnsureIndex();
            TacticalSecuredReport snapshot = report.Copy();
            snapshot.Cells = snapshot.Cells.Distinct().ToList();
            if (byOpening.TryGetValue(snapshot.Opening, out List<TacticalSecuredReport> group))
                foreach (TacticalSecuredReport old in group)
                    if (old.ThreatAt >= snapshot.SeenTick)
                    { snapshot.Recheck = true; snapshot.ThreatAt = Math.Max(snapshot.ThreatAt, old.ThreatAt); }
            snapshot.Sequence = ++Sequence;
            if (Records.Count >= TacticalSecuredReport.HistoryLimit)
            {
                TacticalSecuredReport oldest = Records[0]; Records.RemoveAt(0); RemoveIndex(oldest);
            }
            Records.Add(snapshot); AddIndex(snapshot); return true;
        }
        public void ObserveThreat(IntVec3 position, IntVec3 area, int tick)
        {
            EnsureIndex();
            var affected = new HashSet<IntVec3>();
            if (byCell.TryGetValue(position, out HashSet<TacticalSecuredReport> atPosition))
                foreach (TacticalSecuredReport report in atPosition) affected.Add(report.Opening);
            if (byCell.TryGetValue(area, out HashSet<TacticalSecuredReport> atArea))
                foreach (TacticalSecuredReport report in atArea) affected.Add(report.Opening);
            if (byOpening.ContainsKey(position)) affected.Add(position);
            bool changed = false;
            foreach (IntVec3 opening in affected)
                foreach (TacticalSecuredReport report in byOpening[opening])
                {
                    // A delayed enemy report must not revoke a newer actual clear.
                    if (report.SeenTick > tick || report.ThreatAt >= tick) continue;
                    report.ThreatAt = Math.Max(report.ThreatAt, tick);
                    if (!report.Recheck) { Index(report, -1); report.Recheck = true; changed = true; }
                }
            if (changed) Sequence++;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Sequence, "sequence");
            Scribe_Collections.Look(ref Records, "records", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit) indexed = false;
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        private TacticalSecuredKnowledge unattachedKnowledge;
        public TacticalSecuredKnowledge SecuredKnowledge => Owner?.SharedSecured
            ?? (unattachedKnowledge ?? (unattachedKnowledge = new TacticalSecuredKnowledge()));
        public int SharedSecuredCursor;
        internal int SharedStructureRevision = -1;
        internal readonly List<IntVec3> ReportBatch = new List<IntVec3>(TacticalSecuredReport.CellLimit);
        internal int ReportFrontierCursor;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        public TacticalSecuredKnowledge SharedSecured = new TacticalSecuredKnowledge();
        private bool RefreshSharedPassage(TacticalSquadCommand command, int tick)
        {
            TacticalLocalPlan plan = command.Plan;
            if (plan == null || command.FurnitureAction != null || command.ContactResponse != null
                || TacticalOpeningPolicy.OwnsContact(command, command.OpeningAction?.Enemy ?? IntVec3.Invalid)
                || !TacticalSharedPassagePolicy.Reconsider(command.Phase, TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, plan),
                    plan.Direct, command.ChargeAction != null, command.OpeningAction?.Launched == true)) return false;
            // Stop preparatory work before releasing its controller. A launched
            // grenade or a C4 action always retains its own safety/return flow.
            foreach (TacticalMemberCommand member in command.Members)
            {
                string job = member.Job?.def.defName;
                if (!Available(member, map) || member.Pawn.CurJob != member.Job
                    || job != "HD_NewTacticalBreach" && job != "HD_NewTacticalCut"
                        && job != "HD_NewTacticalObserve" && job != "HD_NewTacticalThrow") continue;
                if (!CanIssue(member)) { command.Due = tick + 1; return true; }
                if (!Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), member.Pawn.Position, plan.Inside)))
                { command.Due = tick + 1; return true; }
            }
            command.OpeningAction = null;
            if (!OpeningUsable(plan))
            {
                // A different team already cleared this room through another
                // mouth. Release this obsolete wall job and reuse the shared gap.
                BeginReturn(command, tick, false, replan: true); return true;
            }
            plan.ReportedPassage = true;
            // Free the obsolete staging footprint and the exclusive breach
            // lease. Subsequent squads only reserve their destination posts.
            ReleaseClaims(command);
            foreach (IntVec3 cell in plan.Positions) claims[cell] = command;
            command.Phase = TacticalCommandPhase.Enter; command.PhaseStarted = tick;
            return false;
        }
        private void SyncSharedSecured(TacticalSquadCommand command, int tick)
        {
            if (command.SharedSecuredCursor >= SharedSecured.Sequence) return;
            int consumed = 0;
            foreach (TacticalSecuredReport report in SharedSecured.Records)
            {
                if (report.Sequence <= command.SharedSecuredCursor) continue;
                if (consumed++ >= 2) { command.Due = Math.Min(command.Due, tick + 1); return; }
                command.SharedSecuredCursor = report.Sequence;
                if (report.Origin != command.Id) ApplySharedSecured(command, report);
            }
            command.SharedSecuredCursor = SharedSecured.Sequence;
        }
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
                Opening = plan.Opening, Inward = plan.Inward, Depth = plan.Depth, PortalUsable = !plan.Direct && OpeningUsable(plan),
                Cells = command.ReportBatch.ToList() };
            if (command.RoomScan != null)
                for (int i = 0; i < 2 && command.ReportFrontierCursor < command.RoomScan.Frontiers.Count; i++)
                {
                    TacticalRoomFrontier frontier = command.RoomScan.Frontiers[command.ReportFrontierCursor++];
                    report.Frontiers.Add(new TacticalRoomFrontier { Opening = frontier.Opening, Inward = frontier.Inward,
                        Depth = frontier.Depth, Preference = frontier.Preference, Outdoors = frontier.Outdoors });
                }
            command.ReportBatch.Clear(); command.SecuredKnowledge.Record(report);
        }
        internal void ApplySharedSecured(TacticalSquadCommand command, TacticalSecuredReport report)
        {
            bool added = false;
            foreach (TacticalRoomFrontier frontier in report.Recheck ? Enumerable.Empty<TacticalRoomFrontier>() : report.Frontiers)
                if (command.FrontierKeys.Add(frontier.Opening))
                {
                    command.Frontiers.Add(new TacticalRoomFrontier { Opening = frontier.Opening, Inward = frontier.Inward,
                        Depth = frontier.Depth, Preference = frontier.Preference, Outdoors = frontier.Outdoors, Approach = true });
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
            // Shared geometry never fakes physical entry by this squad.
        }
        private TacticalLocalPlan ReuseReportedOpening(TacticalSquadCommand command, Pawn leader, IntVec3 goal, int count,
            Func<IntVec3, bool> claimed, out bool tried, out TacticalPlanFailure failure, bool onlyCleared = false)
        {
            tried = false; failure = TacticalPlanFailure.None;
            int candidates = 0; var seen = new HashSet<IntVec3>();
            List<TacticalSecuredReport> reports = command.SecuredKnowledge.Records;
            for (int i = reports.Count - 1; i >= 0 && candidates < 2; i--)
            {
                TacticalSecuredReport report = reports[i];
                if (!report.PortalUsable || onlyCleared && !command.SecuredKnowledge.ClearedPassage(report.Opening, report.Inward, report.Depth)
                    || !seen.Add(report.Opening)
                    || command.SecuredCells.Contains(report.Opening + report.Inward * report.Depth)) continue;
                var plan = new TacticalLocalPlan { Opening = report.Opening, Inward = report.Inward, Depth = report.Depth, ExistingOpening = true };
                IntVec3 travel = goal - leader.Position, relative = leader.Position - plan.Opening;
                if (travel.x * plan.Inward.x + travel.z * plan.Inward.z <= 0
                    || relative.x * plan.Inward.x + relative.z * plan.Inward.z >= 1
                    || plan.Outside.DistanceToSquared(leader.Position) > goal.DistanceToSquared(leader.Position)) continue;
                candidates++; tried = true;
                Building live = plan.Opening.GetEdifice(map);
                if (live?.Faction != null && live.Faction != command.TargetFaction)
                { failure |= TacticalPlanFailure.NotBoundary; continue; }
                if (!OpeningUsable(plan)) { failure |= TacticalPlanFailure.Obstructed; continue; }
                bool cleared = TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, plan);
                if (!cleared && leases.ContainsKey(plan.Opening)) { failure |= TacticalPlanFailure.Busy; continue; }
                if (cleared ? !TacticalLocalPlanner.BuildClearedTransit(map, plan, count, claimed)
                    : !TacticalLocalPlanner.Free(map, plan.Outside, claimed) || !TacticalLocalPlanner.Free(map, plan.Inside, claimed))
                { failure |= TacticalPlanFailure.Obstructed; continue; }
                // An uncleared reported opening returns geometry only; its
                // stack, inside seats and reach check use separate slices.
                if (cleared && !leader.CanReach(plan.Inside, PathEndMode.OnCell, Danger.Deadly))
                { failure |= TacticalPlanFailure.Unreachable; continue; }
                plan.Barrier = plan.Opening.GetEdifice(map); failure = TacticalPlanFailure.None; return plan;
            }
            return null;
        }
    }
}
