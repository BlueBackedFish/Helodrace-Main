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
        private readonly Dictionary<Pawn, IntVec3> openingQueuePositions = new Dictionary<Pawn, IntVec3>();
        private bool WaitForSharedOpening(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (tick % 120 == 0)
            {
                var liveQueued = new HashSet<Pawn>(states.Values.Where(value => value.SharedOpeningWait)
                    .SelectMany(value => value.ActivePlan?.Assignments.Select(assignment => assignment.Pawn) ?? Enumerable.Empty<Pawn>()));
                foreach (Pawn pawn in openingQueuePositions.Keys.Where(pawn => !liveQueued.Contains(pawn)
                    || !pawn.Spawned || pawn.Dead || pawn.Downed).ToList()) openingQueuePositions.Remove(pawn);
            }
            if (!plan.BreachCell.IsValid || plan.IsDefensive || state.Phase > RaidExecutionPhase.CrossBreach)
            { openingLeases.Release(state.UnitId); state.SharedOpeningWait = false; return false; }
            openingLeases.Prune(id => states.TryGetValue(id, out ExecutionState owner)
                && owner.ActivePlan?.BreachCell.IsValid == true && owner.Phase <= RaidExecutionPhase.CrossBreach
                && owner.ActivePlan.Assignments.Any(value => value.Pawn?.Spawned == true
                    && !value.Pawn.Dead && !value.Pawn.Downed));
            if (openingLeases.Acquire(state.UnitId, new SharedOpeningKey(members[0].Faction, plan.BreachCell),
                (a, b) => a.Faction == b.Faction && a.Cell.DistanceToSquared(b.Cell) <= 144))
            {
                if (state.SharedOpeningWait)
                {
                    foreach (Pawn pawn in members) openingQueuePositions.Remove(pawn);
                    state.ApproachProgressTick = state.PhaseStarted = tick;
                }
                state.SharedOpeningWait = false;
                return false;
            }
            if (members.All(pawn => pawn.Position.DistanceToSquared(plan.BreachCell) > 324))
            { state.SharedOpeningWait = false; return false; }
            state.SharedOpeningWait = true;
            state.ApproachProgressTick = state.PhaseStarted = tick;
            // Keep the queue in its current connected room, outside the leading
            // team's opening workspace. It never follows an unseen alternative door.
            RaidStructureSnapshot structure = StructureFor(map, plan);
            var occupied = new HashSet<IntVec3>(openingQueuePositions.Where(pair => !members.Contains(pair.Key))
                .Select(pair => pair.Value));
            foreach (Pawn pawn in members)
            {
                if (ContactGuardFor(state, pawn, tick) != null || MapComponent_RaidTacticalOrders.Protected(pawn)
                    || IsTaserOperation(pawn)) continue;
                if (!openingQueuePositions.TryGetValue(pawn, out IntVec3 cell) || !cell.Standable(map)
                    || occupied.Contains(cell) || cell.DistanceToSquared(plan.BreachCell) <= 144
                    || FormationOccupied(pawn, cell, stationaryOnly: true))
                {
                    int room = structure?.RoomAt(pawn.Position) ?? 0;
                    cell = GenRadial.RadialCellsAround(pawn.Position, 10f, true)
                        .Where(value => value.InBounds(map) && value.Standable(map)
                            && value.DistanceToSquared(plan.BreachCell) > 144 && !occupied.Contains(value)
                            && !plan.AvoidedTrapCells.Contains(value) && !FormationOccupied(pawn, value)
                            && (structure == null || structure.RoomAt(value) == room)
                            && RaidNodeRoute.WalkLine(map, pawn.Position, value)
                            && map.pawnDestinationReservationManager.CanReserve(value, pawn))
                        .DefaultIfEmpty(pawn.Position).First();
                    openingQueuePositions[pawn] = cell;
                }
                occupied.Add(cell);
                MapComponent_RaidTacticalOrders.Set(pawn, cell == pawn.Position ? RaidOrderKind.Hold : RaidOrderKind.Move,
                    cell, radius: 1f, reactive: true);
                MapComponent_RaidTacticalOrders.For(pawn)?.Movement.Block(RaidMoveBlockReason.OpeningQueue, tick);
            }
            return true;
        }
    }
}
