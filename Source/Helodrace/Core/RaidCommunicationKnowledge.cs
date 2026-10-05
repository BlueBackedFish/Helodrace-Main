using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private void ConfigureCommunicationKnowledge(ExecutionState state, Pawn commander)
        {
            state.CqbKnowledge.Origin = state.UnitId;
            int observerId = commander?.thingIDNumber ?? 0;
            state.CqbKnowledge.OnObserved = (cell, known) => {
                int tick = GenTicks.TicksGame;
                PublishPassage(state, observerId, cell, known, tick,
                    state.Communication.Knowledge);
            };
        }

        private void PublishPassage(ExecutionState state, int observer, IntVec3 position,
            RaidKnownCqbCell cell, int tick, RaidReportLedger ledger)
        {
            RaidStructureSnapshot structure = StructureFor(map, state.ActivePlan);
            if (structure == null) return;
            ledger.Publish(new RaidTacticalReport { Id = state.UnitId + ":" + observer + ":passage:" + position,
                OriginUnit = state.UnitId, ObserverId = observer, Revision = tick, ObservedTick = tick, ReceivedTick = tick,
                Kind = RaidReportKind.Passage, Position = position, Room = structure.RoomAt(position), StructureVersion = structure.Version.Id,
                Usable = cell.Usable, Building = cell.Building, IsPortal = cell.Portal, Route = new List<string> { state.UnitId } });
        }

        private void PublishRoomCheck(ExecutionState state, IntVec3 cell, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, state.ActivePlan);
            int room = structure?.RoomAt(cell) ?? 0;
            if (room <= 0) return;
            Pawn commander = executionTickets.TryGetValue(state.UnitId, out RaidExecutionTicket ticket)
                ? ticket.Unit.Commander : null;
            state.Communication.Knowledge.Publish(new RaidTacticalReport { Id = state.UnitId + ":room:" + room,
                OriginUnit = state.UnitId, ObserverId = commander?.thingIDNumber ?? 0, Revision = tick,
                ObservedTick = tick, ReceivedTick = tick, Kind = RaidReportKind.RoomChecked, Position = cell,
                Room = room, StructureVersion = structure.Version.Id, Route = new List<string> { state.UnitId } });
        }

        internal void AcceptReport(ExecutionState state, RaidTacticalReport report, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, state.ActivePlan);
            if (structure == null || !report.Position.InBounds(map)) return;
            // Reported room IDs are never trusted across static layout versions.
            int room = report.StructureVersion == structure.Version.Id ? report.Room : structure.RoomAt(report.Position);
            if (report.Kind == RaidReportKind.Contact && state.Contacts.Receive(report, room, tick) && !report.ConfirmedEmpty)
                foreach (int touched in ContactRooms(structure, report.Position))
                    state.RoomSecurity.Observe(touched, report.Position, report.ObservedTick);
            else if (report.Kind == RaidReportKind.Passage) state.CqbKnowledge.Receive(report);
            else if (report.Kind == RaidReportKind.RoomChecked && room > 0)
                state.RoomSecurity.Checked(room, report.ObservedTick);
            // A peer's room-check report is knowledge, not this unit's own completed traversal.
            // It does not add to ClearedRoomCells or change the committed route/objective.
        }

        private void CaptureObserverPassages(Pawn observer, ExecutionState state, RaidStructureSnapshot structure,
            int tick, ref int budget)
        {
            RaidReportLedger ledger = state.Communication.For(observer.thingIDNumber).Reports;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(observer.Position, 4f, true))
            {
                if (budget <= 0) return;
                if (!cell.InBounds(map)) continue;
                Building building = cell.GetEdifice(map) as Building;
                if (!(building is Building_Door) && (!structure.CachedAt(cell).WallLine || building != null)) continue;
                string id = state.UnitId + ":" + observer.thingIDNumber + ":passage:" + cell;
                RaidTacticalReport old = ledger.Reports.FirstOrDefault(report => report.Id == id);
                var live = new RaidKnownCqbCell { Building = building?.thingIDNumber ?? 0, Portal = true,
                    Usable = cell.Walkable(map) && (!(building is Building_Door door) || door.Open || door.PawnCanOpen(observer)) };
                if (old != null && tick - old.ObservedTick < 120 && old.Building == live.Building
                    && old.Usable == live.Usable) continue;
                budget--;
                if (RaidObservationSight.CanSeeCell(map, observer.Position, cell, 4, ClearObservationLine))
                {
                    PublishPassage(state, observer.thingIDNumber, cell, live, tick, ledger);
                    if (observer == RaidTacticalUnit.ForPawn(observer)?.Commander)
                    {
                        PublishPassage(state, observer.thingIDNumber, cell, live, tick, state.Communication.Knowledge);
                        state.CqbKnowledge.RememberDirect(cell, live, tick, structure.Version.Id, state.UnitId);
                    }
                }
            }
        }

        private IEnumerable<Pawn> LinkedObservers(List<Pawn> members, ExecutionState state, int tick)
        {
            RaidCommunicationFrame frame = map.GetComponent<MapComponent_RaidTacticalCommunications>()?.Frame(state.UnitId, tick);
            return members.Where(pawn => frame?.CommandDelays.ContainsKey(pawn.thingIDNumber) == true
                || IsOpeningSensor(state, pawn));
        }

        private static bool IsOpeningSensor(ExecutionState state, Pawn pawn) => state?.Phase == RaidExecutionPhase.ObserveOpening
            && state.Observation?.Observer == pawn && RaidEntryObservation.Active(pawn) != null;
    }
}
