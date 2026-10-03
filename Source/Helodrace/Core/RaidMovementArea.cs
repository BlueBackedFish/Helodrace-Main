using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public RaidMovementArea(Map map, RaidTacticalPlan plan, RaidStructureSnapshot structure,
            bool exteriorOnly, int initialRoom, RaidPawnOrder fight = null,
            int excludedRoom = 0, bool selectedOpeningOnly = false)
        {
            costs = new NativeArray<ushort>(map.cellIndices.NumGridCells, Allocator.Persistent);
            for (int i = 0; i < costs.Length; i++) costs[i] = 100;
            // A broad corridor steers individual paths without pulling every
            // displaced pawn back to an exact checkpoint.
            foreach (IntVec3 root in plan.ApproachPath.Concat(new[] { plan.Start, plan.Entry }))
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(root, 6f, true))
                    if (cell.InBounds(map)) costs[map.cellIndices.CellToIndex(cell)] = 0;
            foreach (IntVec3 root in plan.Assignments.Select(assignment => assignment.Position))
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(root, 3f, true))
                    if (cell.InBounds(map)) costs[map.cellIndices.CellToIndex(cell)] = 0;
            if (exteriorOnly && structure != null)
                foreach (IntVec3 cell in map.AllCells)
                    if (structure.RoomAt(cell) > 0 && structure.RoomAt(cell) != initialRoom)
                        costs[map.cellIndices.CellToIndex(cell)] = ushort.MaxValue;
            if (structure != null && (excludedRoom > 0 || selectedOpeningOnly))
                foreach (IntVec3 cell in map.AllCells)
                    if (excludedRoom > 0 && structure.RoomAt(cell) == excludedRoom
                        || selectedOpeningOnly && !RaidBreachTraversal.CanUsePortal(
                            cell == plan.BreachCell,
                            structure.CachedAt(cell).WallLine, structure.CachedAt(cell).ExteriorAccess))
                        costs[map.cellIndices.CellToIndex(cell)] = ushort.MaxValue;
            if (fight != null)
                foreach (IntVec3 cell in map.AllCells)
                    if (cell.DistanceToSquared(fight.Destination) > fight.Radius * fight.Radius
                        || fight.LeashRadius > 0f && cell.DistanceToSquared(fight.LeashCenter)
                            > fight.LeashRadius * fight.LeashRadius
                        || fight.Room > 0 && structure?.RoomAt(cell) != fight.Room)
                        costs[map.cellIndices.CellToIndex(cell)] = ushort.MaxValue;
        }

        public NativeArray<ushort> GetOffsetGrid() => costs;
        public void Dispose() { if (costs.IsCreated) costs.Dispose(); }
    }

    public sealed class MapComponent_RaidMovementAreas : MapComponent
    {
        private readonly Dictionary<RaidTacticalPlan, Dictionary<int, RaidMovementArea>> areas =
            new Dictionary<RaidTacticalPlan, Dictionary<int, RaidMovementArea>>();
        private readonly Dictionary<string, RaidMovementArea> fightingAreas = new Dictionary<string, RaidMovementArea>();
        public int Requests;
        public long BuildMilliseconds;
        public int CachedGrids => areas.Values.Sum(value => value.Count) + fightingAreas.Count;
        public MapComponent_RaidMovementAreas(Map map) : base(map) { }

        internal RaidMovementArea For(Pawn pawn)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
            if (order == null || pawn.CurJobDef != RimWorld.JobDefOf.Goto)
                return null;
            var state = map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(order.OrganizationId);
            RaidTacticalPlan plan = state?.ActivePlan;
            if (plan == null) return null;
            bool waitingForSupport = state.Phase == RaidExecutionPhase.Support
                || state.Phase == RaidExecutionPhase.EntryWait;
            bool supportFlee = waitingForSupport
                && pawn.CurJob.jobGiver is RimWorld.JobGiver_FleePotentialExplosion
                && state.SupportProjectile != null && pawn.mindState.knownExploder == state.SupportProjectile;
            if (!MapComponent_RaidTacticalOrders.Owned(pawn.CurJob) && !supportFlee
                || order.Kind == RaidOrderKind.Hold && !supportFlee) return null;
            bool outside = plan.BreachCell.IsValid && (state.Phase == RaidExecutionPhase.Assemble
                || state.Phase == RaidExecutionPhase.Breach || state.Phase == RaidExecutionPhase.Support
                || state.Phase == RaidExecutionPhase.EntryWait);
            RaidStructureSnapshot structure = map.GetComponent<MapComponent_RaidTacticalPlans>()
                .GetStructure(order.OrganizationId);
            if (order.Kind == RaidOrderKind.Fight)
            {
                // A pawn outside the activity area must be able to return into it.
                if (!MapComponent_RaidTacticalOrders.Allowed(pawn, order, pawn.Position)) return null;
                string key = $"{order.OrganizationId}:{order.Destination}:{order.Radius}:{order.Room}:"
                    + $"{order.LeashCenter}:{order.LeashRadius}";
                if (!fightingAreas.TryGetValue(key, out RaidMovementArea fightArea))
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    fightingAreas[key] = fightArea = new RaidMovementArea(map, plan, structure, false, -1, order);
                    BuildMilliseconds += watch.ElapsedMilliseconds;
                }
                Requests++;
                return fightArea;
            }
            // Subsequent interior room breaches are not exterior approaches.
            outside &= structure?.RoomAt(plan.Entry) == 0;
            int insideRoom = plan.BreachCell.IsValid ? structure?.RoomAt(plan.BreachInside) ?? 0 : 0;
            int excludedRoom = waitingForSupport && insideRoom > 0
                && structure.RoomAt(pawn.Position) != insideRoom ? insideRoom : 0;
            bool selectedOpeningOnly = state.Phase == RaidExecutionPhase.CrossBreach
                && plan.Assignments.Any(assignment => assignment.Pawn == pawn
                    && assignment.Task == RaidTacticalTask.Entry);
            int initialRoom = structure?.RoomAt(pawn.Position) ?? 0;
            int room = selectedOpeningOnly ? -2 : excludedRoom > 0 ? -3 - (outside ? initialRoom : 0)
                : outside ? initialRoom : -1;
            if (!areas.TryGetValue(plan, out Dictionary<int, RaidMovementArea> versions))
                areas[plan] = versions = new Dictionary<int, RaidMovementArea>();
            if (!versions.TryGetValue(room, out RaidMovementArea area))
            {
                Stopwatch watch = Stopwatch.StartNew();
                versions[room] = area = new RaidMovementArea(map, plan, structure,
                    outside, initialRoom,
                    excludedRoom: excludedRoom, selectedOpeningOnly: selectedOpeningOnly);
                BuildMilliseconds += watch.ElapsedMilliseconds;
            }
            Requests++;
            return area;
        }

        internal void DisposeAreas()
        {
            foreach (RaidMovementArea area in areas.Values.SelectMany(value => value.Values)) area.Dispose();
            foreach (RaidMovementArea area in fightingAreas.Values) area.Dispose();
            areas.Clear();
            fightingAreas.Clear();
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
