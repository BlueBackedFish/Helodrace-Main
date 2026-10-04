using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private bool ClearObservationLine(IntVec3 source, IntVec3 target) =>
            RaidPhysicalMapCache.For(map).ClearLine(source, target, GenTicks.TicksGame,
                () => GenSight.LineOfSight(source, target, map, true) && !SmokeBetween(source, target));

        private bool CanObserveMapCell(List<Pawn> members, IntVec3 cell)
        {
            if (!cell.InBounds(map) || members.Count == 0) return false;
            RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(members[0]);
            ExecutionState state = StateFor(unit?.Id);
            // Other soldiers publish personal passage reports and relay them through contact.
            // Their hidden wall/door state cannot update the command map immediately.
            return members.Where(pawn => pawn == unit?.Commander || IsOpeningSensor(state, pawn))
                .Any(pawn => RaidObservationSight.CanSeeCell(map, pawn.Position, cell,
                    RaidContactMemory.Radius, ClearObservationLine));
        }

        private bool CanObserveContact(Pawn observer, IntVec3 source, Pawn target, int radius) =>
            RaidObservationSight.CanSeePawn(map, source, target, radius,
                source == observer.Position, ClearObservationLine);

        private bool CanObserveContact(Pawn observer, IntVec3 source, IntVec3 cell, int radius) =>
            source.DistanceToSquared(cell) <= radius * radius
            && ClearObservationLine(source, cell);

        private void RefreshContacts(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            using (RaidCpuProfiler.Measure(map, RaidCpuStage.Observation)) RefreshContactsCore(members, plan, state, tick);
        }

        private void RefreshContactsCore(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (!state.Contacts.ScanScheduled)
            {
                state.Contacts.ScanTick = tick - RaidContactMemory.ScanTicks + RaidCommunicationPolicy.ScanOffset(state.UnitId);
                state.Contacts.ScanScheduled = true;
            }
            if (tick - state.Contacts.ScanTick < RaidContactMemory.ScanTicks) return;
            ConfigureCommunicationKnowledge(state);
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure == null) return;
            RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(members[0]);
            int radius = unit?.Organization.doctrine?.fieldObservationRadius ?? 90;
            int budget = unit?.Organization.doctrine?.contactLosBudget ?? 96;
            budget = RaidPhysicalMapCache.For(map).ObservationBudget.Grant(state.UnitId, tick, budget);
            if (budget == 0) return;
            state.Contacts.ScanTick = tick;
            var personalSeen = new Dictionary<int, HashSet<int>>();
            var seen = new HashSet<int>();
            var positions = new HashSet<IntVec3>();
            // Cheap distance filtering first; LOS is restricted to the local squad surroundings.
            List<Pawn> candidates = RaidPhysicalMapCache.For(map).Nearby(members, radius, tick)
                .Where(value => value.HostileTo(members[0]))
                .Select(enemy => new { Enemy = enemy, Distance = members.Min(pawn => pawn.Position.DistanceToSquared(enemy.Position)) })
                .Where(value => value.Distance <= radius * radius)
                .OrderBy(value => value.Distance).ThenBy(value => value.Enemy.thingIDNumber)
                .Take(32).Select(value => value.Enemy).ToList();
            var linked = new HashSet<Pawn>(LinkedObservers(members, state, tick));
            List<Pawn> sources = members.OrderBy(pawn => pawn == unit?.Commander ? 0 : linked.Contains(pawn) ? 1 : 2).ToList();
            int start = state.ObservationCursor, processed = 0;
            for (int i = 0; i < candidates.Count && budget > 0; i++)
            {
                Pawn enemy = candidates[(start + i) % candidates.Count];
                processed++;
                Pawn observer = null;
                foreach (Pawn pawn in sources)
                {
                    int localRadius = structure.IsIndoor(pawn.Position) && structure.IsIndoor(enemy.Position)
                        ? RaidContactMemory.Radius : radius;
                    if (pawn.Position.DistanceToSquared(enemy.Position) > localRadius * localRadius) continue;
                    if (--budget < 0) break;
                    if (CanObserveContact(pawn, pawn.Position, enemy, localRadius)) { observer = pawn; break; }
                }
                if (observer == null) continue;
                positions.Add(enemy.Position);
                if (!personalSeen.TryGetValue(observer.thingIDNumber, out HashSet<int> ids))
                    personalSeen[observer.thingIDNumber] = ids = new HashSet<int>();
                ids.Add(enemy.thingIDNumber);
                if (RecordContact(observer, enemy, state, structure, tick)) seen.Add(enemy.thingIDNumber);
            }
            state.ObservationCursor = candidates.Count > 0 ? (start + processed) % candidates.Count : 0;
            foreach (RaidObserverMemory memory in state.Communication.Observers)
                memory.Contacts.FinishScan(tick, personalSeen.TryGetValue(memory.PawnId, out HashSet<int> ids) ? ids : new HashSet<int>());
            state.Contacts.FinishScan(tick, seen);
            foreach (RaidEnemyContact contact in state.Contacts.Entries.Where(value => !value.Visible
                && !value.PositionConfirmedEmpty && !positions.Contains(value.Position)))
            {
                foreach (Pawn pawn in sources)
                {
                    if (pawn != unit?.Commander && !IsOpeningSensor(state, pawn)) continue;
                    if (budget-- <= 0) break;
                    if (CanObserveContact(pawn, pawn.Position, contact.Position, RaidContactMemory.Radius))
                    { contact.PositionConfirmedEmpty = true; break; }
                }
                if (budget <= 0) break;
            }
            foreach (Pawn observer in sources) CaptureObserverPassages(observer, state, structure, tick, ref budget);
        }

        private bool RecordContact(Pawn observer, Pawn enemy, ExecutionState state, RaidStructureSnapshot structure, int tick)
        {
            RaidObserverMemory personal = state.Communication.For(observer.thingIDNumber);
            RaidEnemyContact previous = personal.Contacts.Entries.FirstOrDefault(value => value.EnemyId == enemy.thingIDNumber);
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
            RaidEnemyContact contact = personal.Contacts.Observe(enemy.thingIDNumber, enemy.LabelShort, enemy.Position, room,
                observer.thingIDNumber, tick, portal, enemy.equipment?.Primary != null, GunRange(enemy), state.UnitId, structure.Version.Id);
            var report = new RaidTacticalReport { Id = contact.ReportId, OriginUnit = state.UnitId, ObserverId = observer.thingIDNumber,
                Revision = tick, ObservedTick = tick, ReceivedTick = tick, StructureVersion = structure.Version.Id,
                Kind = RaidReportKind.Contact, Position = enemy.Position, Room = room, EnemyId = enemy.thingIDNumber,
                Label = contact.Label, Portal = contact.Portal, Direction = contact.Direction, Armed = contact.Armed,
                Range = contact.Range, ConfirmedEmpty = enemy.Dead || enemy.Downed, Route = new List<string> { state.UnitId } };
            personal.Reports.Publish(report);
            bool direct = observer == RaidTacticalUnit.ForPawn(observer)?.Commander || IsOpeningSensor(state, observer);
            if (direct)
            {
                state.Contacts.Observe(enemy.thingIDNumber, contact.Label, contact.Position, room, observer.thingIDNumber,
                    tick, portal, contact.Armed, contact.Range, state.UnitId, structure.Version.Id);
                state.Communication.Knowledge.Publish(report);
                if (report.ConfirmedEmpty) state.Contacts.Entries.RemoveAll(value => value.EnemyId == enemy.thingIDNumber);
                else foreach (int touchedRoom in ContactRooms(structure, enemy.Position))
                        state.RoomSecurity.Observe(touchedRoom, enemy.Position, tick);
            }
            return direct;
        }
    }
}
