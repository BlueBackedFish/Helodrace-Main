using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private IEnumerable<int> ContactRooms(RaidStructureSnapshot structure, IntVec3 cell)
        {
            if (IsOpeningDoorCell(map, structure, cell) || structure.CachedAt(cell).WallLine && cell.Walkable(map))
                return GenAdj.CardinalDirections.Select(direction => cell + direction)
                    .Where(value => value.InBounds(map) && !structure.CachedAt(value).WallLine)
                    .Select(structure.RoomAt).Where(room => room > 0).Distinct();
            return new[] { structure.RoomAt(cell) }.Where(room => room > 0);
        }

        private bool RecentRoomContact(ExecutionState state, RaidStructureSnapshot structure, int room, int tick) =>
            state.Contacts.Entries.Any(contact => contact.Confidence(tick) <= RaidContactConfidence.Recent
                && ContactRooms(structure, contact.Position).Contains(room));

        private bool CheckRoomConcern(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int room, int tick)
        {
            RaidRoomSecurityRecord record = state.RoomSecurity.For(room);
            if (record?.RecentConcern(tick) != true) return true;
            RaidStructureSnapshot structure = StructureFor(map, plan);
            List<Pawn> inside = members.Where(pawn => structure.RoomAt(pawn.Position) == room).ToList();
            if (inside.Any(pawn => pawn.Position.DistanceToSquared(record.Concern) <= 64
                && CanObserveContact(pawn, pawn.Position, record.Concern, RaidContactMemory.Radius)))
            {
                // This verifies the last contact sector, not every hidden tile of the room.
                state.RoomSecurity.Checked(room, tick);
                return true;
            }
            if (tick < record.NextAttemptTick) return false;
            record.NextAttemptTick = tick + 120;
            foreach (Pawn pawn in inside.Where(value => ContactGuardFor(value) == null && !MapComponent_RaidTacticalOrders.Protected(value))
                .OrderBy(value => value.Position.DistanceToSquared(record.Concern)).Take(2))
            {
                IntVec3 position = GenRadial.RadialCellsAround(record.Concern, 3f, true)
                    .Where(cell => ValidReactiveCell(cell) && structure.RoomAt(cell) == room
                        && cell.DistanceToSquared(pawn.Position) <= 100 && !plan.AvoidedTrapCells.Contains(cell)
                        && CanObserveContact(pawn, cell, record.Concern, RaidContactMemory.Radius))
                    .OrderBy(cell => cell.DistanceToSquared(pawn.Position))
                    .Take(12).Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (position.IsValid) MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Fight, position, radius: 2f);
            }
            return false;
        }

        private bool TryPlanContactRecheck(CombatOrganization organization, List<Pawn> members, RaidTacticalPlan current,
            ExecutionState state, RaidStructureSnapshot structure, HashSet<int> cleared, Pawn observer, int tick)
        {
            int occupied = structure.RoomAt(observer.Position);
            List<RaidRoomSecurityRecord> pending = state.RoomSecurity.Rooms
                .Where(value => value.Room != occupied && cleared.Contains(value.Room) && value.RecentConcern(tick)
                    && tick >= value.NextAttemptTick).OrderByDescending(value => value.LastThreatTick).ToList();
            if (pending.Count == 0) return false;
            if (state.LocalCqb == null) state.LocalCqb = new RaidCqbLocalMap();
            state.LocalCqb.Refresh(map, structure, observer, observer.Position, tick, current.AvoidedTrapCells,
                observed: cell => CanObserveMapCell(members, cell));
            var allowed = new HashSet<int>(cleared) { occupied };
            HashSet<IntVec3> reachable = state.LocalCqb.Reachable(observer.Position, allowed);
            foreach (RaidRoomSecurityRecord record in pending)
            {
                record.NextAttemptTick = tick + 180;
                IntVec3 target = GenRadial.RadialCellsAround(record.Concern, 3f, true)
                    .Where(cell => ValidReactiveCell(cell) && structure.RoomAt(cell) == record.Room
                        && reachable.Contains(cell) && !current.AvoidedTrapCells.Contains(cell))
                    .OrderBy(cell => cell.DistanceToSquared(record.Concern)).Take(16)
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!target.IsValid) continue;
                RaidTacticalPlan next = RaidTacticalPlanner.MakePlan(map, organization, target);
                if (next?.Success != true || next.PlannedBreach != null) continue;
                next.ObjectiveIsIntermediate = true; next.ObjectiveIsRecheck = true;
                next.BreachCell = next.BreachInside = IntVec3.Invalid;
                next.ReusePassage = false;
                next.EntryDelayTicks = next.CoordinationDelayTicks = 0;
                next.Selected = new RaidTacticalOption { Maneuver = RaidTacticalManeuver.CoordinatedEntry,
                    Score = 100f, Reason = "Recheck an observed contact using an already open passage" };
                next.Options.Add(next.Selected);
                ActivateNextRoomPlan(organization, members, state, next, tick);
                MapComponent_RaidTacticalTrace.Record(observer, $"CQB recheck R{record.Room}: known open route to {target}; no repeated demolition or blind grenade");
                return true;
            }
            return false;
        }
    }
}
