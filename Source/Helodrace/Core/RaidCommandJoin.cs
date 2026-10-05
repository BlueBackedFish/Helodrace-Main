using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        internal bool ResolvePersonalJoin(RaidPawnOrder order)
        {
            RaidPawnCommand command = order.Command;
            Pawn pawn = order.Pawn;
            ExecutionState state = StateFor(order.UnitId);
            RaidStructureSnapshot structure = state?.ActivePlan == null ? null : StructureFor(map, state.ActivePlan);
            if (structure == null || !command.Destination.InBounds(map)) return false;
            if (pawn.Position == command.Destination) return true;
            int sourceRoom = structure.RoomAt(pawn.Position), targetRoom = structure.RoomAt(command.Destination);
            if (sourceRoom == 0 && targetRoom == 0) return true;
            RaidMovementNode connection = command.Connection;
            if (connection?.StructureVersion == structure.Version.Id
                && connection.RestrictedCells?.Contains(pawn.Position) == true
                && connection.RestrictedCells.Contains(command.Destination)) return true;
            int tick = GenTicks.TicksGame;
            if (tick < command.JoinSearchAfter) return false;
            command.JoinSearchAfter = tick + 90;
            var rooms = new HashSet<int> { sourceRoom, targetRoom };
            foreach (IntVec3 cleared in state.ClearedRoomCells) rooms.Add(structure.RoomAt(cleared));
            var known = new HashSet<IntVec3>(state.CqbKnowledge.KnownPassablePortals(structure.Version.Id));
            foreach (RaidTacticalReport report in state.Communication.For(pawn.thingIDNumber).Reports.Reports)
                if (report.Kind == RaidReportKind.Passage && report.StructureVersion == structure.Version.Id
                    && report.IsPortal && report.Usable) known.Add(report.Position);
            connection = KnownIndoorJoin(map, structure, pawn.Position, command.Destination,
                rooms, known.Contains, state.ActivePlan.AvoidedTrapCells);
            if (connection == null || !connection.RestrictedCells.Contains(command.Destination)) return false;
            command.Connection = connection; command.Revision++; order.RefreshPending = true;
            MapComponent_RaidTacticalTrace.Record(pawn, $"Independent role join via known rooms to {command.Destination}");
            return true;
        }
    }
}
