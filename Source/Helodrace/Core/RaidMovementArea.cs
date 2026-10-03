using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // Immutable for the lifetime of a pathfinder: queued Unity jobs can retain
    // the custom cost array after an order changes or a room is cleared.
    internal sealed class RaidMovementArea : PathRequest.IPathGridCustomizer
    {
        private NativeArray<ushort> costs;
        private TacticalMovementMaskInput input;
        private Task<ushort[]> calculation;
        private CancellationTokenSource cancellation;
        private ushort[] values;
        private int copied;
        private bool canceled;
        private int retryFrame;
        public bool Ready { get; private set; }
        public bool Canceled => canceled;
        public int LastRequestedFrame { get; private set; }
        public RaidTacticalPlan OwnerPlan;
        public void MarkRequested() => LastRequestedFrame = UnityEngine.Time.frameCount;
        private readonly Stopwatch preparation = Stopwatch.StartNew();
        public long PreparationMilliseconds => preparation.ElapsedMilliseconds;

        public RaidMovementArea(Map map, RaidTacticalPlan plan, RaidStructureSnapshot structure,
            bool exteriorOnly, int initialRoom, RaidPawnOrder fight = null,
            int excludedRoom = 0, bool selectedOpeningOnly = false, bool reactive = false)
        {
            OwnerPlan = plan;
            MarkRequested();
            input = new TacticalMovementMaskInput {
                Width = map.Size.x, Height = map.Size.z, Structure = structure?.Version.Geometry,
                Roots = plan.ApproachPath.Concat(new[] { plan.Start, plan.Entry })
                    .Where(cell => cell.IsValid).Distinct()
                    .Select(cell => new TacticalMaskRoot { X = cell.x, Z = cell.z, Radius = 6 })
                    .Concat(plan.Assignments.Select(assignment => new TacticalMaskRoot {
                        X = assignment.Position.x, Z = assignment.Position.z, Radius = 3 })).ToArray(),
                Reactive = reactive, ExteriorOnly = exteriorOnly, InitialRoom = initialRoom,
                ExcludedRoom = excludedRoom, SelectedOpeningOnly = selectedOpeningOnly,
                BreachIndex = plan.BreachCell.InBounds(map) ? map.cellIndices.CellToIndex(plan.BreachCell) : -1,
                Fight = fight != null, FightX = fight?.Destination.x ?? 0, FightZ = fight?.Destination.z ?? 0,
                FightRadius = fight?.Radius ?? 0, FightRoom = fight?.Room ?? 0,
                LeashX = fight?.LeashCenter.x ?? 0, LeashZ = fight?.LeashCenter.z ?? 0,
                LeashRadius = fight?.LeashRadius ?? 0
            };
            if (reactive)
            {
                // Explosion/sniper evasion cannot wait for deferred preparation.
                // This rare first-room mask is shared across organizations.
                costs = new NativeArray<ushort>(TacticalMovementMask.Calculate(input, CancellationToken.None), Allocator.Persistent);
                Ready = true;
                input = null;
                preparation.Stop();
            }
        }

        public void Pump()
        {
            if (Ready || canceled || UnityEngine.Time.frameCount < retryFrame) return;
            if (values == null)
            {
                if (calculation == null)
                {
                    if (cancellation == null) cancellation = new CancellationTokenSource();
                    TacticalGeometryWorker.TryStart(input, cancellation.Token, out calculation);
                }
                if (calculation == null || !calculation.IsCompleted) return;
                TacticalGeometryWorker.Release(calculation);
                if (calculation.IsFaulted || calculation.IsCanceled)
                {
                    if (calculation.IsFaulted) Log.Error("[Helodrace] Movement mask failed: " + calculation.Exception);
                    calculation = null;
                    cancellation.Dispose();
                    cancellation = null;
                    retryFrame = UnityEngine.Time.frameCount + 300;
                    return;
                }
                values = calculation.GetAwaiter().GetResult();
                calculation = null;
            }
            if (!costs.IsCreated)
            {
                if (!TacticalCacheBudget.TakeCell()) return;
                costs = new NativeArray<ushort>(values.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            while (copied < values.Length)
            {
                int count = Math.Min(256, values.Length - copied);
                // A bulk copy is one cheap operation, not 256 live map queries.
                // It still checks the shared elapsed-time budget between chunks.
                if (!TacticalCacheBudget.TakeCell()) return;
                NativeArray<ushort>.Copy(values, copied, costs, copied, count);
                copied += count;
            }
            Ready = true;
            values = null;
            input = null;
            cancellation.Dispose();
            cancellation = null;
            preparation.Stop();
        }

        public NativeArray<ushort> GetOffsetGrid()
        {
            if (!Ready) throw new InvalidOperationException("Movement mask is not ready.");
            return costs;
        }
        public void CancelPreparation()
        {
            canceled = true;
            CancellationTokenSource source = cancellation;
            Task<ushort[]> task = calculation;
            cancellation = null;
            calculation = null;
            values = null;
            input = null;
            // Unpublished partial buffers were never returned to PathFinder.
            if (!Ready && costs.IsCreated) costs.Dispose();
            if (source == null) return;
            source.Cancel();
            if (task == null) source.Dispose();
            else task.ContinueWith(finished => {
                if (finished.IsFaulted) _ = finished.Exception;
                TacticalGeometryWorker.Release(finished);
                source.Dispose();
            }, TaskScheduler.Default);
        }
        public void Dispose() { CancelPreparation(); if (costs.IsCreated) costs.Dispose(); }
    }

    public sealed class MapComponent_RaidMovementAreas : MapComponent, IDisposable
    {
        private readonly Dictionary<RaidTacticalPlan, Dictionary<int, RaidMovementArea>> areas =
            new Dictionary<RaidTacticalPlan, Dictionary<int, RaidMovementArea>>();
        private readonly Dictionary<string, RaidMovementArea> fightingAreas = new Dictionary<string, RaidMovementArea>();
        private readonly Dictionary<TacticalStructureVersion, Dictionary<int, RaidMovementArea>> reactiveAreas =
            new Dictionary<TacticalStructureVersion, Dictionary<int, RaidMovementArea>>();
        private readonly HashSet<RaidMovementArea> pending = new HashSet<RaidMovementArea>();
        private const int MaximumPendingGrids = 64;
        private bool removed;
        public int Requests;
        public long BuildMilliseconds;
        public int CachedGrids => areas.Values.Sum(value => value.Count) + fightingAreas.Count
            + reactiveAreas.Values.Sum(value => value.Count);
        public int PendingGrids => pending.Count;
        public MapComponent_RaidMovementAreas(Map map) : base(map) { }
        public override void MapComponentTick() => Pump();
        public override void MapComponentUpdate() => Pump();
        private void Pump()
        {
            if (removed) return;
            foreach (RaidMovementArea area in pending.OrderByDescending(value => value.LastRequestedFrame).ToArray())
            {
                area.Pump();
                if (!area.Ready) continue;
                BuildMilliseconds += area.PreparationMilliseconds;
                pending.Remove(area);
                foreach (Pawn pawn in area.OwnerPlan.Assignments.Select(assignment => assignment.Pawn))
                    if (pawn?.Spawned == true && pawn.Map == map && !pawn.Dead && !pawn.Downed)
                        MapComponent_RaidTacticalOrders.PreparationReady(pawn);
            }
        }
        private void Queue(RaidMovementArea area)
        {
            if (pending.Count >= MaximumPendingGrids)
            {
                RaidMovementArea oldest = pending.OrderBy(value => value.LastRequestedFrame).First();
                oldest.CancelPreparation();
                pending.Remove(oldest);
            }
            pending.Add(area);
        }
        public override void MapRemoved() { Dispose(); base.MapRemoved(); }
        public void Dispose()
        {
            removed = true;
            foreach (RaidMovementArea area in pending) area.CancelPreparation();
            pending.Clear();
            // Native grids may still be read by Unity jobs. PathFinder.Dispose's
            // postfix remains the sole owner of their actual disposal.
        }
        internal bool ReadyFor(Pawn pawn)
        {
            if (MapComponent_RaidTacticalOrders.For(pawn)?.Reactive == true) return true;
            RaidMovementArea area = Select(pawn, true);
            area?.MarkRequested();
            return area?.Ready ?? true;
        }

        internal RaidMovementArea For(Pawn pawn)
        {
            RaidMovementArea area = Select(pawn, false);
            area?.MarkRequested();
            if (area?.Ready != true) return null;
            Requests++;
            return area;
        }

        private RaidMovementArea Select(Pawn pawn, bool preparing)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
            bool equipmentMove = RaidEntryObservation.Active(pawn) != null || RaidGrenadePreparation.Active(pawn) != null;
            if (removed || order == null || !preparing && pawn.CurJobDef != RimWorld.JobDefOf.Goto && !equipmentMove)
                return null;
            var state = map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(order.OrganizationId);
            RaidTacticalPlan plan = state?.ActivePlan;
            if (plan == null) return null;
            bool waitingForSupport = state.Phase == RaidExecutionPhase.ObserveOpening || state.Phase == RaidExecutionPhase.Support
                || state.Phase == RaidExecutionPhase.EntryWait;
            bool supportFlee = waitingForSupport
                && pawn.CurJob.jobGiver is RimWorld.JobGiver_FleePotentialExplosion
                && state.SupportProjectile != null && pawn.mindState.knownExploder == state.SupportProjectile;
            bool emergencyFlee = order.Reactive && map.GetComponent<MapComponent_RaidTacticalExecution>()
                .TryEmergencyFleeDestination(pawn, out _);
            if (!preparing && (!MapComponent_RaidTacticalOrders.Owned(pawn.CurJob) && !supportFlee && !emergencyFlee && !equipmentMove
                || order.Kind == RaidOrderKind.Hold && !supportFlee && !emergencyFlee && !equipmentMove)) return null;
            bool outside = plan.BreachCell.IsValid && (state.Phase == RaidExecutionPhase.Assemble
                || state.Phase == RaidExecutionPhase.Breach || state.Phase == RaidExecutionPhase.ObserveOpening || state.Phase == RaidExecutionPhase.Support
                || state.Phase == RaidExecutionPhase.EntryWait);
            RaidStructureSnapshot structure = map.GetComponent<MapComponent_RaidTacticalPlans>()
                .GetStructure(order.OrganizationId);
            if (order.Reactive)
            {
                if (structure == null) return null;
                int reactionRoom = structure.RoomAt(pawn.Position);
                if (!reactiveAreas.TryGetValue(structure.Version, out Dictionary<int, RaidMovementArea> rooms))
                    reactiveAreas[structure.Version] = rooms = new Dictionary<int, RaidMovementArea>();
                if (!rooms.TryGetValue(reactionRoom, out RaidMovementArea reactionArea))
                {
                    rooms[reactionRoom] = reactionArea = new RaidMovementArea(map, plan, structure, false, reactionRoom, reactive: true);
                    BuildMilliseconds += reactionArea.PreparationMilliseconds;
                }
                return reactionArea;
            }
            if (order.Kind == RaidOrderKind.Fight)
            {
                // A pawn outside the activity area must be able to return into it.
                if (!MapComponent_RaidTacticalOrders.Allowed(pawn, order, pawn.Position)) return null;
                string key = $"{order.OrganizationId}:{order.Destination}:{order.Radius}:{order.Room}:"
                    + $"{order.LeashCenter}:{order.LeashRadius}";
                if (!fightingAreas.TryGetValue(key, out RaidMovementArea fightArea) || fightArea.Canceled)
                {
                    fightingAreas[key] = fightArea = new RaidMovementArea(map, plan, structure, false, -1, order);
                    Queue(fightArea);
                }
                fightArea.OwnerPlan = plan;
                return fightArea;
            }
            // Subsequent interior room breaches are not exterior approaches.
            outside &= structure?.RoomAt(plan.Entry) == 0;
            int insideRoom = plan.BreachCell.IsValid ? structure?.RoomAt(plan.BreachInside) ?? 0 : 0;
            int excludedRoom = (waitingForSupport || equipmentMove) && insideRoom > 0
                && structure.RoomAt(pawn.Position) != insideRoom ? insideRoom : 0;
            bool selectedOpeningOnly = !plan.ReusePassage && state.Phase == RaidExecutionPhase.CrossBreach
                && plan.Assignments.Any(assignment => assignment.Pawn == pawn
                    && assignment.Task == RaidTacticalTask.Entry);
            int initialRoom = structure?.RoomAt(pawn.Position) ?? 0;
            int room = selectedOpeningOnly ? -2 : excludedRoom > 0 ? -3 - (outside ? initialRoom : 0)
                : outside ? initialRoom : -1;
            if (!areas.TryGetValue(plan, out Dictionary<int, RaidMovementArea> versions))
                areas[plan] = versions = new Dictionary<int, RaidMovementArea>();
            if (!versions.TryGetValue(room, out RaidMovementArea area) || area.Canceled)
            {
                versions[room] = area = new RaidMovementArea(map, plan, structure,
                    outside, initialRoom,
                    excludedRoom: excludedRoom, selectedOpeningOnly: selectedOpeningOnly);
                Queue(area);
            }
            return area;
        }

        internal void DisposeAreas()
        {
            Dispose();
            foreach (RaidMovementArea area in areas.Values.SelectMany(value => value.Values)) area.Dispose();
            foreach (RaidMovementArea area in fightingAreas.Values) area.Dispose();
            foreach (RaidMovementArea area in reactiveAreas.Values.SelectMany(value => value.Values)) area.Dispose();
            areas.Clear();
            fightingAreas.Clear();
            reactiveAreas.Clear();
        }
    }

    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.CreateRequest), new[] {
        typeof(IntVec3), typeof(LocalTargetInfo), typeof(IntVec3?), typeof(Pawn),
        typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer) })]
    public static class Patch_RaidMovementArea_Request
    {
        public static void Prefix(Pawn pawn, ref PathRequest.IPathGridCustomizer customizer)
        {
            if (customizer != null || pawn?.Spawned != true) return;
            customizer = pawn.Map.GetComponent<MapComponent_RaidMovementAreas>()?.For(pawn);
        }
    }

    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.Dispose))]
    public static class Patch_RaidMovementArea_Dispose
    {
        // The original Dispose completes all outstanding jobs first.
        public static void Postfix(Map ___map) => ___map.GetComponent<MapComponent_RaidMovementAreas>()
            ?.DisposeAreas();
    }
}
