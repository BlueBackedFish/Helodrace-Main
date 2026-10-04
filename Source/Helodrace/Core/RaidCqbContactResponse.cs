using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class RaidContactGuard : IExposable
    {
        public Pawn Pawn;
        public int EnemyId, Until, SearchAfter;
        public IntVec3 Position = IntVec3.Invalid, Focus = IntVec3.Invalid;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref EnemyId, "enemyId");
            Scribe_Values.Look(ref Until, "until");
            Scribe_Values.Look(ref SearchAfter, "searchAfter");
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
            Scribe_Values.Look(ref Focus, "focus", IntVec3.Invalid);
        }
    }

    internal static class RaidCqbContactPolicy
    {
        internal static bool Rear(IntVec3 center, IntVec3 forward, IntVec3 target) =>
            (target.x - center.x) * forward.x + (target.z - center.z) * forward.z < 0;
        internal static bool Opposed(IntVec3 center, IntVec3 a, IntVec3 b)
        {
            IntVec3 x = a - center, y = b - center;
            int dot = x.x * y.x + x.z * y.z;
            return dot < 0 && (long)dot * dot * 4 >= (long)x.LengthHorizontalSquared * y.LengthHorizontalSquared;
        }
        internal static bool Pause(bool visibleRear, bool visibleOpposed, bool visibleClose) => visibleRear || visibleOpposed || visibleClose;
        internal static IEnumerable<RaidEnemyContact> Watch(RaidContactMemory memory, IntVec3 center, int tick) =>
            memory.Entries.Where(contact => contact.Confidence(tick) <= RaidContactConfidence.Area
                && contact.Position.DistanceToSquared(center) <= RaidContactMemory.Radius * RaidContactMemory.Radius)
                .OrderBy(contact => contact.Confidence(tick)).ThenByDescending(contact => contact.SeenTick)
                .GroupBy(contact => contact.WatchPoint).Select(group => group.First());
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        internal static RaidContactGuard ContactGuardFor(Pawn pawn)
        {
            string id = pawn?.Spawned == true ? RaidTacticalUnit.ForPawn(pawn)?.Id : null;
            return id == null ? null : ContactGuardFor(pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                ?.StateFor(id), pawn, GenTicks.TicksGame);
        }

        internal static RaidContactGuard ContactGuardFor(ExecutionState state, Pawn pawn, int tick) =>
            state?.ContactGuards.FirstOrDefault(guard => guard.Pawn == pawn && guard.Until > tick);

        // One cohort policy for node completion and staging readiness. A guard
        // holds its assigned contact until released; it is not a missing mover.
        internal static List<RaidTacticalAssignment> ApproachAssignments(List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            var present = new HashSet<Pawn>(members);
            var guarding = new HashSet<Pawn>(state.ContactGuards.Where(guard => guard.Until > tick)
                .Select(guard => guard.Pawn));
            return plan.Assignments.Where(assignment => assignment.Task != RaidTacticalTask.Withdraw
                && present.Contains(assignment.Pawn) && !guarding.Contains(assignment.Pawn)).ToList();
        }

        private bool RespondToCqbContacts(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure == null || plan.IsDefensive || !members.Any(pawn => structure.IsIndoor(pawn.Position)))
            {
                ReleaseContactGuards(state, plan, members, guard => true);
                state.ContactPause = false;
                return false;
            }
            // A real member anchors the group; a geometric average could lie across a wall.
            Pawn anchor = members.GroupBy(pawn => structure.RoomAt(pawn.Position)).OrderByDescending(group => group.Count())
                .First().OrderBy(pawn => members.Sum(other => pawn.Position.DistanceToSquared(other.Position))).First();
            IntVec3 center = anchor.Position;
            IntVec3 forward = plan.BreachCell.IsValid ? plan.BreachInside - plan.BreachCell : plan.Objective - plan.Start;
            List<RaidEnemyContact> contacts = RaidCqbContactPolicy.Watch(state.Contacts, center, tick).ToList();
            List<RaidEnemyContact> visible = contacts.Where(contact => contact.Confidence(tick) == RaidContactConfidence.Visible).ToList();
            bool opposed = visible.Any(a => visible.Any(b => a != b && RaidCqbContactPolicy.Opposed(center, a.Position, b.Position)));
            bool rear = visible.Any(contact => RaidCqbContactPolicy.Rear(center, forward, contact.Position)
                && members.Any(pawn => pawn.Position.DistanceToSquared(contact.Position) <= 144));
            bool close = visible.Any(contact => OpeningRoomContains(map, structure, contact.Position, structure.RoomAt(center))
                && members.Any(pawn => pawn.Position.DistanceToSquared(contact.Position) <= 36));
            bool pause = RaidCqbContactPolicy.Pause(rear, opposed, close);
            int targetRoom = structure.RoomAt(plan.Objective);
            var watchers = contacts.Where(contact => pause || contact.Room != targetRoom
                || RaidCqbContactPolicy.Rear(center, forward, contact.Position)
                || state.ContactGuards.Any(guard => guard.EnemyId == contact.EnemyId)).Take(2).ToList();
            ReleaseContactGuards(state, plan, members, guard => !members.Contains(guard.Pawn)
                || !watchers.Any(contact => contact.EnemyId == guard.EnemyId)
                || guard.Pawn.Position.DistanceToSquared(center) > 144);
            var occupied = new HashSet<IntVec3>(state.ContactGuards.Select(guard => guard.Position));
            int guardLimit = opposed ? Math.Min(2, members.Count) : Math.Min(2, Math.Max(1, members.Count / 3));
            foreach (RaidEnemyContact contact in watchers)
            {
                RaidContactGuard guard = state.ContactGuards.FirstOrDefault(value => value.EnemyId == contact.EnemyId);
                if (guard == null && state.ContactGuards.Count < guardLimit)
                {
                    Pawn pawn = members.Where(value => value != state.Breacher && value != state.Thrower
                        && !structure.CachedAt(value.Position).WallLine
                        && RaidEntryObservation.Active(value) == null && !MapComponent_RaidTacticalOrders.Protected(value)
                        && !IsTaserOperation(value) && !state.ContactGuards.Any(existing => existing.Pawn == value))
                        .OrderBy(value => plan.Assignments.Any(assignment => assignment.Pawn == value
                            && assignment.Task != RaidTacticalTask.Entry) ? 0 : 1)
                        .ThenBy(value => value.Position.DistanceToSquared(contact.WatchPoint)).FirstOrDefault();
                    if (pawn != null) state.ContactGuards.Add(guard = new RaidContactGuard { Pawn = pawn, EnemyId = contact.EnemyId });
                }
                if (guard == null) continue;
                bool changedDirection = guard.Focus.IsValid && guard.Focus.DistanceToSquared(contact.WatchPoint) > 9;
                bool exposed = ValidReactiveCell(guard.Position) && contact.Confidence(tick) == RaidContactConfidence.Visible
                    && GenSight.LineOfSight(contact.Position, guard.Position, map, true)
                    && CoverUtility.CalculateOverallBlockChance(guard.Position, contact.Position, map) < 0.2f;
                if (!ValidReactiveCell(guard.Position) || tick >= guard.SearchAfter && (changedDirection || exposed))
                {
                    occupied.Remove(guard.Position);
                    guard.Position = FindContactGuardPosition(guard.Pawn, plan, contact.WatchPoint, center, occupied);
                    if (!guard.Position.IsValid) guard.Position = guard.Pawn.Position;
                    occupied.Add(guard.Position); guard.SearchAfter = tick + 120;
                    MapComponent_RaidTacticalTrace.Record(guard.Pawn, $"CQB guard contact #{contact.EnemyId} at {contact.WatchPoint}; hold {guard.Position}");
                }
                guard.Focus = contact.WatchPoint; guard.Until = contact.SeenTick + 600;
                ApplyContactGuard(guard);
            }
            if (pause)
            {
                if (!state.ContactPause)
                {
                    CancelOpeningObservation(state);
                    foreach (Pawn pawn in members)
                    {
                        bool tool = pawn.CurJobDef?.defName == CompSledgehammerBreach.JobDefName
                            || pawn.CurJobDef?.defName == "HD_PowerCutterBreach";
                        if (tool || RaidGrenadePreparation.Active(pawn)?.Released == false)
                            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    }
                    if (state.BreachKind != RaidBreachKind.C4) { state.Breacher = null; state.BreachTarget = null; state.BreachKind = RaidBreachKind.None; }
                    if (!state.SupportLaunched) { state.SupportIssued = false; state.Thrower = null; }
                    MapComponent_RaidTacticalTrace.Record(anchor, "CQB contact pause: cover rear/crossfire without replacing the breach plan");
                }
                foreach (Pawn pawn in members.Where(pawn => !state.ContactGuards.Any(guard => guard.Pawn == pawn)
                    && !MapComponent_RaidTacticalOrders.Protected(pawn) && !IsTaserOperation(pawn)))
                {
                    RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
                    IntVec3 position = order?.Reactive == true && order.Destination.DistanceToSquared(pawn.Position) <= 36
                        ? order.Destination : FindContactGuardPosition(pawn, plan, visible[0].Position, center, occupied);
                    if (!position.IsValid) position = pawn.Position;
                    occupied.Add(position);
                    MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Fight, position, radius: 1.5f, reactive: true);
                }
                if (state.SupportLaunched) ReturnSupportThrower(state);
                PauseReaction(state, tick);
            }
            else if (state.ContactPause)
            {
                state.DoorStateSignature = null; state.LastRoomSecurityTick = tick - 30;
                MapComponent_RaidTacticalTrace.Record(anchor, "CQB contact lost: keep passage guards and resume the existing task");
            }
            state.ContactPause = pause;
            return pause;
        }

        private static void ApplyContactGuard(RaidContactGuard guard)
        {
            Pawn pawn = guard.Pawn;
            MapComponent_RaidTacticalOrders.Set(pawn, pawn.Position == guard.Position ? RaidOrderKind.Hold : RaidOrderKind.Move,
                guard.Position, radius: 1f, reactive: true);
            if (pawn.Position == guard.Position && guard.Focus != pawn.Position)
                MapComponent_RaidTacticalOrders.Face(pawn, Rot4.FromAngleFlat((guard.Focus - pawn.Position).ToVector3().AngleFlat()));
        }

        private static void ReleaseContactGuards(ExecutionState state, RaidTacticalPlan plan, List<Pawn> members,
            Predicate<RaidContactGuard> release)
        {
            List<RaidContactGuard> removed = state.ContactGuards.Where(guard => release(guard)).ToList();
            foreach (RaidContactGuard guard in removed) state.ContactGuards.Remove(guard);
            foreach (RaidContactGuard guard in removed.Where(guard => members.Contains(guard.Pawn)))
            {
                RaidTacticalAssignment assignment = plan.Assignments.FirstOrDefault(value => value.Pawn == guard.Pawn);
                bool entered = state.Phase == RaidExecutionPhase.Assault || state.Phase == RaidExecutionPhase.ClearRoom
                    || state.Phase == RaidExecutionPhase.SecureRoom || state.Phase == RaidExecutionPhase.Complete;
                MapComponent_RaidTacticalOrders.Set(guard.Pawn, entered ? RaidOrderKind.Fight : RaidOrderKind.Move,
                    entered ? guard.Pawn.Position : assignment?.Position ?? guard.Pawn.Position, radius: entered ? 3f : 10f);
            }
        }

        private IntVec3 FindContactGuardPosition(Pawn pawn, RaidTacticalPlan plan, IntVec3 focus,
            IntVec3 center, HashSet<IntVec3> occupied)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            int room = structure.RoomAt(pawn.Position);
            if (structure.CachedAt(pawn.Position).WallLine)
                room = GenAdj.CardinalDirections.Select(direction => pawn.Position + direction)
                    .Where(cell => ValidReactiveCell(cell) && !structure.CachedAt(cell).WallLine)
                    .OrderBy(cell => cell.DistanceToSquared(center)).Select(structure.RoomAt).DefaultIfEmpty(room).First();
            var cells = new HashSet<IntVec3>(GenRadial.RadialCellsAround(pawn.Position, 5f, true)
                .Where(cell => ValidReactiveCell(cell) && structure.RoomAt(cell) == room
                    && cell.DistanceToSquared(center) <= 100 && !plan.AvoidedTrapCells.Contains(cell)));
            cells.Add(pawn.Position);
            var connected = RaidFormationTopology.Distances(cells, pawn.Position,
                cell => GenAdj.CardinalDirections.Select(direction => cell + direction), cell => true);
            return connected.Keys.Where(cell => !occupied.Contains(cell)
                && map.pawnDestinationReservationManager.CanReserve(cell, pawn))
                .OrderByDescending(cell => CoverUtility.CalculateOverallBlockChance(cell, focus, map) * 16f
                    + (!GenSight.LineOfSight(focus, cell, map, true) ? 5f : 0f) - connected[cell] * 1.5f)
                .Take(8).Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }
    }
}
