using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private readonly struct SharedOpeningKey
        {
            internal readonly Faction Faction;
            internal readonly IntVec3 Cell;
            internal SharedOpeningKey(Faction faction, IntVec3 cell) { Faction = faction; Cell = cell; }
        }
        private readonly TacticalOpeningLeases<SharedOpeningKey> openingLeases = new TacticalOpeningLeases<SharedOpeningKey>();
        private readonly TacticalQueuePositions<Pawn, IntVec3> openingQueuePositions = new TacticalQueuePositions<Pawn, IntVec3>();
        private int openingCleanupTick = -1, openingPruneTick = -1;
        internal int OpeningCleanupPasses { get; private set; }
        internal int OpeningPrunePasses { get; private set; }
        private bool WaitForSharedOpening(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            using (RaidCpuProfiler.Measure(map, RaidCpuStage.OpeningQueue))
                return WaitForSharedOpeningCore(members, plan, state, tick);
        }
        private void ReleaseOpeningQueue(List<Pawn> members)
        {
            foreach (Pawn pawn in members) openingQueuePositions.Release(pawn);
        }
        private bool WaitForSharedOpeningCore(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (tick % 120 == 0 && openingCleanupTick != tick)
            {
                openingCleanupTick = tick; OpeningCleanupPasses++;
                var liveQueued = new HashSet<Pawn>(states.Values.Where(value => value.SharedOpeningWait)
                    .SelectMany(value => value.ActivePlan?.Assignments.Select(assignment => assignment.Pawn) ?? Enumerable.Empty<Pawn>()));
                openingQueuePositions.Prune(pawn => liveQueued.Contains(pawn)
                    && pawn.Spawned && pawn.Map == map && !pawn.Dead && !pawn.Downed);
            }
            if (!plan.BreachCell.IsValid || plan.IsDefensive || state.Phase > RaidExecutionPhase.CrossBreach)
            { openingLeases.Release(state.UnitId); ReleaseOpeningQueue(members); state.SharedOpeningWait = false; return false; }
            if (openingPruneTick != tick)
            {
                openingPruneTick = tick; OpeningPrunePasses++;
                openingLeases.Prune(id => states.TryGetValue(id, out ExecutionState owner)
                    && owner.ActivePlan?.BreachCell.IsValid == true && owner.Phase <= RaidExecutionPhase.CrossBreach
                    && owner.ActivePlan.Assignments.Any(value => value.Pawn?.Spawned == true
                        && !value.Pawn.Dead && !value.Pawn.Downed));
            }
            if (openingLeases.Acquire(state.UnitId, new SharedOpeningKey(members[0].Faction, plan.BreachCell),
                (a, b) => a.Faction == b.Faction && a.Cell.DistanceToSquared(b.Cell) <= 144))
            {
                if (state.SharedOpeningWait)
                {
                    ReleaseOpeningQueue(members);
                    state.ApproachProgressTick = state.PhaseStarted = tick;
                }
                state.SharedOpeningWait = false;
                return false;
            }
            if (members.All(pawn => pawn.Position.DistanceToSquared(plan.BreachCell) > 324))
            { ReleaseOpeningQueue(members); state.SharedOpeningWait = false; return false; }
            state.SharedOpeningWait = true;
            state.ApproachProgressTick = state.PhaseStarted = tick;
            // Keep the queue in its current connected room, outside the leading
            // team's opening workspace. It never follows an unseen alternative door.
            RaidStructureSnapshot structure = StructureFor(map, plan);
            foreach (Pawn pawn in members)
            {
                if (ContactGuardFor(state, pawn, tick) != null || MapComponent_RaidTacticalOrders.Protected(pawn)
                    || IsTaserOperation(pawn)) continue;
                if (!openingQueuePositions.TryGet(pawn, out IntVec3 cell) || !cell.Standable(map)
                    || !openingQueuePositions.Available(pawn, cell) || cell.DistanceToSquared(plan.BreachCell) <= 144
                    || FormationOccupied(pawn, cell, stationaryOnly: true))
                {
                    int room = structure?.RoomAt(pawn.Position) ?? 0;
                    cell = GenRadial.RadialCellsAround(pawn.Position, 10f, true)
                        .Where(value => value.InBounds(map) && value.Standable(map)
                            && value.DistanceToSquared(plan.BreachCell) > 144 && openingQueuePositions.Available(pawn, value)
                            && !plan.AvoidedTrapCells.Contains(value) && !FormationOccupied(pawn, value)
                            && (structure == null || structure.RoomAt(value) == room)
                            && RaidNodeRoute.WalkLine(map, pawn.Position, value)
                            && map.pawnDestinationReservationManager.CanReserve(value, pawn))
                        .DefaultIfEmpty(pawn.Position).First();
                    if (!openingQueuePositions.Assign(pawn, cell))
                    {
                        // No free waiting slot: keep the physical current position
                        // without claiming another pawn's assigned destination.
                        openingQueuePositions.Release(pawn);
                        cell = pawn.Position;
                    }
                }
                MapComponent_RaidTacticalOrders.Set(pawn, cell == pawn.Position ? RaidOrderKind.Hold : RaidOrderKind.Move,
                    cell, radius: 1f, reactive: true);
                MapComponent_RaidTacticalOrders.For(pawn)?.Movement.Block(RaidMoveBlockReason.OpeningQueue, tick);
            }
            return true;
        }
    }
}
