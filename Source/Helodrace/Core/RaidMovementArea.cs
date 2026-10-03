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
            bool exteriorOnly, int initialRoom)
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
        }

        public NativeArray<ushort> GetOffsetGrid() => costs;
        public void Dispose() { if (costs.IsCreated) costs.Dispose(); }
    }

    public sealed class MapComponent_RaidMovementAreas : MapComponent
    {
        private readonly Dictionary<RaidTacticalPlan, Dictionary<int, RaidMovementArea>> areas =
            new Dictionary<RaidTacticalPlan, Dictionary<int, RaidMovementArea>>();
        public int Requests;
        public long BuildMilliseconds;
        public int CachedGrids => areas.Values.Sum(value => value.Count);
        public MapComponent_RaidMovementAreas(Map map) : base(map) { }

        internal RaidMovementArea For(Pawn pawn)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
            if (order?.Kind != RaidOrderKind.Move || !MapComponent_RaidTacticalOrders.Owned(pawn.CurJob))
                return null;
            var state = map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(order.OrganizationId);
            RaidTacticalPlan plan = state?.ActivePlan;
            if (plan == null) return null;
            bool outside = plan.BreachCell.IsValid && (state.Phase == RaidExecutionPhase.Assemble
                || state.Phase == RaidExecutionPhase.Breach || state.Phase == RaidExecutionPhase.Support
                || state.Phase == RaidExecutionPhase.EntryWait);
            RaidStructureSnapshot structure = map.GetComponent<MapComponent_RaidTacticalPlans>()
                .GetStructure(order.OrganizationId);
            // Subsequent interior room breaches are not exterior approaches.
            outside &= structure?.RoomAt(plan.Entry) == 0;
            int room = outside ? structure.RoomAt(pawn.Position) : -1;
            if (!areas.TryGetValue(plan, out Dictionary<int, RaidMovementArea> versions))
                areas[plan] = versions = new Dictionary<int, RaidMovementArea>();
            if (!versions.TryGetValue(room, out RaidMovementArea area))
            {
                Stopwatch watch = Stopwatch.StartNew();
                versions[room] = area = new RaidMovementArea(map, plan, structure, outside, room);
                BuildMilliseconds += watch.ElapsedMilliseconds;
            }
            Requests++;
            return area;
        }

        internal void DisposeAreas()
        {
            foreach (RaidMovementArea area in areas.Values.SelectMany(value => value.Values)) area.Dispose();
            areas.Clear();
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
