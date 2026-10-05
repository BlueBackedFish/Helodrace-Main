using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helodrace.Squads;
using UnityEngine;
using Verse;

namespace Helodrace
{
    internal sealed class RaidPlanningJob : IDisposable
    {
        internal readonly Map Map;
        internal readonly RaidTacticalUnit Unit;
        internal readonly string Purpose, Signature;
        internal readonly IntVec3? Objective;
        internal readonly RaidTacticalPlan Context, Plan;
        internal readonly MapComponent_RaidPlanningService Owner;
        internal readonly int StructureVersion;
        internal IEnumerator<int> Steps;
        internal bool Complete, Cancelled;
        internal long CpuTicks;
        internal RaidPlanningJob(MapComponent_RaidPlanningService owner, Map map, RaidTacticalUnit unit,
            string purpose, IntVec3? objective, RaidTacticalPlan context, string signature, int version)
        {
            Owner = owner; Map = map; Unit = unit; Purpose = purpose; Objective = objective;
            Context = context; Signature = signature; StructureVersion = version;
            Plan = new RaidTacticalPlan { OrganizationId = unit.OrganizationId, UnitId = unit.Id,
                GroupId = unit.GroupId, PlannedTick = GenTicks.TicksGame };
            Steps = RaidTacticalPlanner.Build(this).GetEnumerator();
        }
        public void Dispose() { Steps?.Dispose(); Steps = null; }
    }

    // One frame allowance for the game, shared fairly across every map.
    // No Pawn/Map access is moved off the main thread.
    public sealed class GameComponent_RaidPlanScheduler : GameComponent
    {
        private readonly TacticalSliceQueue<RaidPlanningJob> queue = new TacticalSliceQueue<RaidPlanningJob>();
        private int frame = -1;
        internal long Slices, Completed, Discarded, BudgetStops;
        internal double MaxSliceMilliseconds;
        internal int Pending => queue.Count;
        public GameComponent_RaidPlanScheduler(Game game) { }
        internal void Add(RaidPlanningJob job) => queue.Add(job);
        internal void Cancel(RaidPlanningJob job)
        { queue.Remove(job); job.Cancelled = true; job.Dispose(); }
        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            if (Find.TickManager == null || Find.TickManager.Paused || frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (queue.Count == 0) return;
            long started = Stopwatch.GetTimestamp(), allowance = Stopwatch.Frequency * 3 / 1000;
            Slices += queue.Run(64, () => Stopwatch.GetTimestamp() - started < allowance, Step);
            if (queue.Count > 0 && Stopwatch.GetTimestamp() - started >= allowance) BudgetStops++;
        }
        private bool Step(RaidPlanningJob job)
        {
            if (job.Cancelled || !Find.Maps.Contains(job.Map) || !job.Owner.Current(job))
            { job.Owner.Discard(job); Discarded++; return false; }
            long started = Stopwatch.GetTimestamp();
            try
            {
                bool more;
                using (RaidCpuProfiler.Measure(job.Map, RaidCpuStage.Planning))
                    more = job.Steps.MoveNext();
                if (more) return true;
                if (!job.Owner.Valid(job))
                { job.Owner.Discard(job); Discarded++; return false; }
                job.Complete = true;
                job.Plan.PlannedTick = GenTicks.TicksGame;
                Completed++;
                job.Dispose();
                return false;
            }
            catch (Exception error)
            {
                job.Plan.Selected = null;
                job.Plan.Work.MarkLimited();
                job.Plan.Reason = "Planning request failed: " + error.GetType().Name;
                job.Complete = true; job.Dispose();
                Log.Error("Raid planning request " + job.Unit.Id + "/" + job.Purpose + ": " + error);
                return false;
            }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - started;
                job.CpuTicks += elapsed;
                job.Plan.PlanningMilliseconds = job.CpuTicks * 1000 / Stopwatch.Frequency;
                MaxSliceMilliseconds = Math.Max(MaxSliceMilliseconds, elapsed * 1000.0 / Stopwatch.Frequency);
            }
        }
    }

    public sealed class MapComponent_RaidPlanningService : MapComponent
    {
        private readonly Dictionary<string, RaidPlanningJob> jobs = new Dictionary<string, RaidPlanningJob>();
        public MapComponent_RaidPlanningService(Map map) : base(map) { }
        private GameComponent_RaidPlanScheduler Scheduler => Verse.Current.Game.GetComponent<GameComponent_RaidPlanScheduler>();
        internal bool Pending(string id) => id != null && jobs.ContainsKey(id);
        internal bool InitialReady(string id) => jobs.TryGetValue(id, out RaidPlanningJob job)
            && job.Purpose == "initial" && job.Complete;
        internal bool Current(RaidPlanningJob job) => jobs.TryGetValue(job.Unit.Id, out RaidPlanningJob current) && current == job;
        internal void Discard(RaidPlanningJob job)
        {
            if (Current(job))
            {
                // Until the caller reissues a discarded first decision, keep
                // its safe waiting ownership instead of briefly enabling vanilla ingress.
                if (job.Context == null && Find.Maps.Contains(job.Map))
                { job.Complete = true; job.Plan.Selected = null; job.Plan.Work.MarkLimited(); }
                else jobs.Remove(job.Unit.Id);
            }
            job.Cancelled = true; job.Dispose();
        }
        internal void Cancel(string id)
        {
            if (id != null && jobs.TryGetValue(id, out RaidPlanningJob job))
            { jobs.Remove(id); Scheduler.Cancel(job); }
        }
        internal void Prune(HashSet<string> live)
        { foreach (string id in jobs.Keys.Where(id => !live.Contains(id)).ToList()) Cancel(id); }
        public override void MapRemoved()
        { foreach (string id in jobs.Keys.ToList()) Cancel(id); base.MapRemoved(); }
        internal RaidTacticalPlan Request(RaidTacticalUnit unit, string purpose,
            IntVec3? objective = null, RaidTacticalPlan context = null)
        {
            var plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            RaidStructureSnapshot structure = plans.GetStructure(unit.Organization);
            if (structure == null) return null;
            string signature = plans.Signature(unit);
            if (jobs.TryGetValue(unit.Id, out RaidPlanningJob job))
            {
                if (job.Purpose != purpose || job.Objective != objective || job.Context != context
                    || job.Signature != signature || job.StructureVersion != (structure.Version?.Id ?? 0)) Cancel(unit.Id);
                else if (!job.Complete) return null;
                else
                {
                    jobs.Remove(unit.Id);
                    if (Valid(job)) return job.Plan;
                }
            }
            job = new RaidPlanningJob(this, map, unit, purpose, objective, context, signature, structure.Version?.Id ?? 0);
            jobs[unit.Id] = job; Scheduler.Add(job);
            return null;
        }
        internal bool Valid(RaidPlanningJob job)
        {
            if (job.Cancelled || job.Signature != map.GetComponent<MapComponent_RaidTacticalPlans>().Signature(job.Unit)
                || (map.GetComponent<MapComponent_RaidTacticalPlans>().GetStructure(job.Unit.OrganizationId)?.Version?.Id ?? 0) != job.StructureVersion)
                return false;
            var state = map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(job.Unit.Id);
            if (job.Context != null && state?.ActivePlan != job.Context) return false;
            if (!job.Plan.Success) return true;
            List<Pawn> members = job.Plan.Assignments.Select(value => value.Pawn).ToList();
            if (members.Count == 0 || members.Any(pawn => !pawn.Spawned || pawn.Map != map || pawn.Dead || pawn.Downed)) return false;
            RaidStructureSnapshot structure = map.GetComponent<MapComponent_RaidTacticalPlans>().GetStructure(job.Unit.OrganizationId);
            int occupied = members.GroupBy(pawn => structure.RoomAt(pawn.Position))
                .OrderByDescending(group => group.Count()).First().Key;
            if (!job.Plan.IsDefensive && occupied != job.Plan.OccupiedRoom) return false;
            if (job.Plan.PlannedBreach != null && (!job.Plan.PlannedBreach.Spawned || job.Plan.PlannedBreach.Destroyed)) return false;
            if (!job.Plan.Entry.InBounds(map) || !job.Plan.Entry.Walkable(map)) return false;
            return job.Plan.Assignments.All(value => value.Position.InBounds(map) && value.Position.Standable(map));
        }
    }
}
