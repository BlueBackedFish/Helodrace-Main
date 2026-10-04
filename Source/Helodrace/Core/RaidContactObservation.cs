using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private bool ClearObservationLine(IntVec3 source, IntVec3 target) =>
            GenSight.LineOfSight(source, target, map, true) && !SmokeBetween(source, target);

        private bool CanObserveContact(Pawn observer, IntVec3 source, Pawn target, int radius) =>
            RaidObservationSight.CanSeePawn(map, source, target, radius,
                source == observer.Position, ClearObservationLine);

        private bool CanObserveContact(Pawn observer, IntVec3 source, IntVec3 cell, int radius) =>
            source.DistanceToSquared(cell) <= radius * radius
            && ClearObservationLine(source, cell);

        private void RefreshContacts(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (tick - state.Contacts.ScanTick < RaidContactMemory.ScanTicks) return;
            state.Contacts.ScanTick = tick;
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure == null) return;
            var seen = new HashSet<int>();
            var positions = new HashSet<IntVec3>();
            // Cheap distance filtering first; LOS is restricted to the local squad surroundings.
            foreach (Pawn enemy in map.mapPawns.AllPawnsSpawned.Where(value => value.HostileTo(members[0])))
            {
                Pawn observer = members.FirstOrDefault(pawn => CanObserveContact(pawn, pawn.Position,
                    enemy, RaidContactMemory.Radius));
                if (observer == null) continue;
                seen.Add(enemy.thingIDNumber); positions.Add(enemy.Position);
                if (enemy.Dead || enemy.Downed)
                {
                    state.Contacts.Entries.RemoveAll(contact => contact.EnemyId == enemy.thingIDNumber);
                    continue;
                }
                RecordContact(observer, enemy, state, structure, tick);
            }
            state.Contacts.FinishScan(tick, seen);
            foreach (RaidEnemyContact contact in state.Contacts.Entries.Where(value => !value.Visible
                && !value.PositionConfirmedEmpty && !positions.Contains(value.Position)))
                if (members.Any(pawn => CanObserveContact(pawn, pawn.Position, contact.Position, RaidContactMemory.Radius)))
                    contact.PositionConfirmedEmpty = true;
        }

        private void RecordContact(Pawn observer, Pawn enemy, ExecutionState state, RaidStructureSnapshot structure, int tick)
        {
            RaidEnemyContact previous = state.Contacts.Entries.FirstOrDefault(value => value.EnemyId == enemy.thingIDNumber);
            IntVec3 portal = IntVec3.Invalid;
            bool PortalAt(IntVec3 cell) => cell.InBounds(map) && (IsOpeningDoorCell(map, structure, cell)
                || structure.CachedAt(cell).WallLine && cell.Walkable(map));
            if (PortalAt(enemy.Position)) portal = enemy.Position;
            else if (previous?.Visible == true && tick - previous.SeenTick <= 40
                && previous.Position.DistanceToSquared(enemy.Position) <= 64
                && GenSight.LineOfSight(previous.Position, enemy.Position, map, true))
                portal = GenSight.PointsOnLineOfSight(previous.Position, enemy.Position)
                    .Where(PortalAt).DefaultIfEmpty(IntVec3.Invalid).Last();
            int room = structure.RoomAt(enemy.Position);
            if (PortalAt(enemy.Position))
                room = GenAdj.CardinalDirections.Select(direction => enemy.Position + direction)
                    .Where(cell => cell.InBounds(map) && !structure.CachedAt(cell).WallLine)
                    .OrderByDescending(cell => cell.DistanceToSquared(observer.Position))
                    .Select(structure.RoomAt).DefaultIfEmpty(room).First();
            state.Contacts.Observe(enemy.thingIDNumber, enemy.LabelShort, enemy.Position, room,
                observer.thingIDNumber, tick, portal, enemy.equipment?.Primary != null, GunRange(enemy));
            foreach (int touchedRoom in ContactRooms(structure, enemy.Position))
                state.RoomSecurity.Observe(touchedRoom, enemy.Position, tick);
        }
    }
}
