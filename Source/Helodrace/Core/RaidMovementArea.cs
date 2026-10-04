using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // Immutable while published; request and native-reader leases protect
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
        internal readonly TacticalNativeLease<PathRequest> Lease = new TacticalNativeLease<PathRequest>();
        private readonly MapComponent_RaidMovementAreas owner;
        internal long NativeBytes => costs.IsCreated ? (long)costs.Length * sizeof(ushort) : 0;
        private int retryFrame;
        public bool Ready { get; private set; }
        public bool Canceled => canceled;
        public int LastRequestedFrame { get; private set; }
        public void MarkRequested() => LastRequestedFrame = UnityEngine.Time.frameCount;
        private readonly Stopwatch preparation = Stopwatch.StartNew();
        public long PreparationMilliseconds => preparation.ElapsedMilliseconds;

        public RaidMovementArea(Map map, TacticalMovementMaskInput captured)
        {
            owner = map.GetComponent<MapComponent_RaidMovementAreas>();
            MarkRequested();
            input = captured;
            if (input.Reactive)
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
            Lease.BeginRead();
            owner?.RegisterReader(this);
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
        private readonly Dictionary<TacticalMovementMaskKey, RaidMovementArea> areas =
            new Dictionary<TacticalMovementMaskKey, RaidMovementArea>();
        private sealed class PermissionVector { internal TacticalMaskVector Value; }
        private readonly ConditionalWeakTable<object, PermissionVector> permissionVectors =
            new ConditionalWeakTable<object, PermissionVector>();
        private TacticalMaskVector Vector(object source, IEnumerable<int> values)
        {
            if (source == null) return TacticalMaskVector.Empty;
            if (permissionVectors.TryGetValue(source, out PermissionVector captured)) return captured.Value;
            captured = new PermissionVector { Value = new TacticalMaskVector(values) };
            permissionVectors.Add(source, captured);
            return captured.Value;
        }
        private RaidMovementArea GetArea(RaidTacticalPlan plan, RaidStructureSnapshot structure,
            bool exteriorOnly, int initialRoom, RaidPawnOrder fight = null,
            int excludedRoom = 0, bool selectedOpeningOnly = false, bool reactive = false,
            RaidMovementNode connection = null, int openingOverride = -1, IEnumerable<IntVec3> ingressCells = null)
        {
            TacticalMaskVector roomVector = Vector(connection?.AllowedRooms, connection?.AllowedRooms);
            TacticalMaskVector portalVector = Vector(connection?.AllowedPortals,
                connection?.AllowedPortals.Where(cell => cell.InBounds(map)).Select(map.cellIndices.CellToIndex));
            IEnumerable<IntVec3> corridor = ingressCells ?? connection?.RestrictedCells;
            TacticalMaskVector cellVector = Vector(corridor, corridor?.Select(map.cellIndices.CellToIndex));
            var source = new TacticalMovementMaskInput {
                Width = map.Size.x, Height = map.Size.z, Structure = structure?.Version.Geometry,
                Reactive = reactive, ExteriorOnly = exteriorOnly, InitialRoom = initialRoom,
                ExcludedRoom = excludedRoom, SelectedOpeningOnly = selectedOpeningOnly,
                BreachIndex = openingOverride >= 0 ? openingOverride
                    : plan.BreachCell.InBounds(map) ? map.cellIndices.CellToIndex(plan.BreachCell) : -1,
                RestrictRooms = connection != null, RestrictPortals = connection != null,
                RestrictCells = corridor != null,
                Fight = fight != null, FightX = fight?.Destination.x ?? 0, FightZ = fight?.Destination.z ?? 0,
                FightRadius = fight?.Radius ?? 0, FightRoom = fight?.Room ?? 0,
                LeashX = fight?.LeashCenter.x ?? 0, LeashZ = fight?.LeashCenter.z ?? 0, LeashRadius = fight?.LeashRadius ?? 0
            };
            var key = new TacticalMovementMaskKey(source, roomVector, portalVector, cellVector);
            if (areas.TryGetValue(key, out RaidMovementArea area) && !area.Canceled)
            {
                CacheHits++;
                return area;
            }
            areas[key] = area = new RaidMovementArea(map, key.Input);
            CreatedGrids++;
            if (area.Ready) BuildMilliseconds += area.PreparationMilliseconds;
            else Queue(area);
            return area;
        }
        private readonly TacticalPreparationQueue<RaidMovementArea, Pawn> pending =
            new TacticalPreparationQueue<RaidMovementArea, Pawn>(RaidPawnReferenceComparer.Instance);
        private const int MaximumPumpsPerPass = 64;
        private bool removed;
        private readonly HashSet<RaidMovementArea> readingAreas = new HashSet<RaidMovementArea>();
        internal void RegisterReader(RaidMovementArea area) => readingAreas.Add(area);
        internal void CompleteReaders()
        {
            foreach (RaidMovementArea area in readingAreas) area.Lease.CompleteReads();
            readingAreas.Clear();
        }
        public int Requests;
        public int CacheHits, CreatedGrids;
        public long BuildMilliseconds;
        public int PreparedNotifications;
        public int CachedGrids => areas.Count;
        public int PendingGrids => pending.Count;
        public int WaitingPawns => pending.WaiterCount;
        public int PeakPendingGrids => pending.PeakCount;
        public int OldestWaitFrames => pending.OldestWaitAge(UnityEngine.Time.frameCount);
        public MapComponent_RaidMovementAreas(Map map) : base(map) { }
        public override void MapComponentTick() => Pump();
        public override void MapComponentUpdate() => Pump();
        private void Pump()
        {
            if (removed) return;
            foreach (RaidMovementArea area in pending.ServiceOrder(MaximumPumpsPerPass).ToArray())
            {
                area.Pump();
                if (!area.Ready) continue;
                BuildMilliseconds += area.PreparationMilliseconds;
                foreach (Pawn pawn in pending.Complete(area))
                    if (pawn?.Spawned == true && pawn.Map == map && !pawn.Dead && !pawn.Downed)
                    {
                        PreparedNotifications++;
                        MapComponent_RaidTacticalOrders.PreparationReady(pawn);
                    }
            }
        }
        private void Queue(RaidMovementArea area)
        {
            pending.Add(area, UnityEngine.Time.frameCount);
        }
        public override void MapRemoved() { Dispose(); base.MapRemoved(); }
        public void Dispose()
        {
            removed = true;
            foreach (RaidMovementArea area in pending.Keys) area.CancelPreparation();
            pending.Clear();
            // Native grids may still be read by Unity jobs. PathFinder.Dispose's
            // postfix remains the sole owner of their actual disposal.
        }
        internal bool ReadyFor(Pawn pawn)
        {
            if (MapComponent_RaidTacticalOrders.For(pawn)?.Reactive == true) { pending.Forget(pawn); return true; }
            RaidMovementArea area = Select(pawn, true);
            area?.MarkRequested();
            if (area != null && !area.Ready) pending.WaitFor(pawn, area);
            else pending.Forget(pawn);
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
            var state = map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(order.UnitId);
            RaidTacticalPlan plan = state?.ActivePlan;
            if (plan == null) return null;
            bool waitingForSupport = state.Phase == RaidExecutionPhase.ObserveOpening || state.Phase == RaidExecutionPhase.Support
                || state.Phase == RaidExecutionPhase.EntryWait;
            bool supportFlee = IsSupportExplosionFlee(pawn.CurJob, state.SupportProjectile,
                pawn.mindState?.knownExploder, waitingForSupport);
            bool emergencyFlee = order.Reactive && map.GetComponent<MapComponent_RaidTacticalExecution>()
                .TryEmergencyFleeDestination(pawn, out _);
            // CQB destinations are already selected from a small connected live
            // area. Avoid building a full-map reactive grid for every guarded room.
            if (order.Reactive && !supportFlee && !emergencyFlee
                && (state.ContactPause || MapComponent_RaidTacticalExecution.ContactGuardFor(pawn) != null)
                && !state.Reactions.Any(reaction => reaction.Pawn == pawn && reaction.Until > GenTicks.TicksGame
                    && (reaction.Kind == RaidReactionKind.Sniper || reaction.Kind == RaidReactionKind.Explosion))) return null;
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
                return GetArea(plan, structure, false, structure.RoomAt(pawn.Position), reactive: true);
            }
            RaidExteriorIngress ingress = map.GetComponent<MapComponent_RaidTacticalExecution>().ActiveExteriorIngress(pawn);
            if (ingress != null && structure != null)
            {
                bool interior = ingress.Entered && !ingress.Waiting;
                RaidMovementArea IngressArea(bool indoors)
                {
                    HashSet<IntVec3> connected = indoors ? map.GetComponent<MapComponent_RaidTacticalExecution>()
                        .InteriorIngressCells(pawn, structure, ingress, out _) : null;
                    return GetArea(plan, structure, !indoors, ingress.InsideRoom, selectedOpeningOnly: !indoors,
                        openingOverride: map.cellIndices.CellToIndex(ingress.Opening), ingressCells: connected);
                }
                RaidMovementArea currentIngressArea = IngressArea(interior);
                if (!interior && !ingress.Waiting)
                {
                    // Prepare the onward leg outside. A pawn must not stand in
                    // the shared mouth waiting for its first indoor cost grid.
                    RaidMovementArea onward = IngressArea(true);
                    if (preparing && !ingress.Yielding && !onward.Ready) return onward;
                }
                return currentIngressArea;
            }
            if (order.Kind == RaidOrderKind.Fight)
            {
                // A pawn outside the activity area must be able to return into it.
                if (!MapComponent_RaidTacticalOrders.Allowed(pawn, order, pawn.Position)) return null;
                return GetArea(plan, structure, false, -1, order);
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
            RaidMovementNode connection = map.GetComponent<MapComponent_RaidTacticalExecution>().ApproachConnection(pawn);
            return GetArea(plan, structure, outside, initialRoom, excludedRoom: excludedRoom,
                selectedOpeningOnly: selectedOpeningOnly, connection: connection);
        }

        internal static bool IsSupportExplosionFlee(Job current, Projectile supportProjectile, Thing knownExploder,
            bool waitingForSupport) => waitingForSupport
            && current?.jobGiver is RimWorld.JobGiver_FleePotentialExplosion
            && supportProjectile != null && knownExploder == supportProjectile;

        internal void DisposeAreas()
        {
            CompleteReaders();
            Dispose();
            foreach (RaidMovementArea area in areas.Values) area.Dispose();
            areas.Clear();
        }
    }

    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.CreateRequest), new[] {
        typeof(IntVec3), typeof(LocalTargetInfo), typeof(IntVec3?), typeof(Pawn),
        typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer) })]
    public static class Patch_RaidMovementArea_Request
    {
        public static void Postfix(PathRequest __result)
        {
            if (__result?.customizer is RaidMovementArea area) area.Lease.Acquire(__result);
        }
        public static void Prefix(Pawn pawn, ref PathRequest.IPathGridCustomizer customizer)
        {
            if (customizer != null || pawn?.Spawned != true) return;
            customizer = pawn.Map.GetComponent<MapComponent_RaidMovementAreas>()?.For(pawn);
        }
    }

    [HarmonyPatch(typeof(PathRequest), nameof(PathRequest.Resolve))]
    public static class Patch_RaidMovementArea_RequestResolved
    {
        public static void Postfix(PathRequest __instance)
        {
            if (__instance.customizer is RaidMovementArea area) area.Lease.Release(__instance);
        }
    }

    [HarmonyPatch(typeof(PathRequest), nameof(PathRequest.Dispose))]
    public static class Patch_RaidMovementArea_RequestCancelled
    {
        public static void Postfix(PathRequest __instance)
        {
            if (__instance.customizer is RaidMovementArea area) area.Lease.Release(__instance);
        }
    }

    [HarmonyPatch(typeof(PathFinder), "ForceCompleteScheduledJobs")]
    public static class Patch_RaidMovementArea_ReadersCompleted
    {
        public static void Postfix(Map ___map) => ___map.GetComponent<MapComponent_RaidMovementAreas>()?.CompleteReaders();
    }

    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.Dispose))]
    public static class Patch_RaidMovementArea_Dispose
    {
        // The original Dispose completes all outstanding jobs first.
        public static void Postfix(Map ___map) => ___map.GetComponent<MapComponent_RaidMovementAreas>()
            ?.DisposeAreas();
    }
}
