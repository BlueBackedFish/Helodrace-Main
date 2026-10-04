using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public enum RaidMovementNodePurpose { Transit, Portal, Gather, BreachPreparation, Cover }

    public sealed class RaidMovementNode : IExposable
    {
        public int Id;
        public int StructureVersion;
        public IntVec3 Center;
        public RaidMovementNodePurpose Purpose;
        public int RouteIndex;
        public int Next = -1;
        public List<IntVec3> ArrivalCells = new List<IntVec3>();
        public List<int> AllowedRooms = new List<int>();
        public List<IntVec3> AllowedPortals = new List<IntVec3>();
        public int Capacity => ArrivalCells.Count;
        public int ArrivalRadius = 3;
        public int RefreshTicks = 60;
        public int RetryTicks = 120;
        [NonSerialized] public int NextAreaRefresh;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref StructureVersion, "structureVersion");
            Scribe_Values.Look(ref Center, "center");
            Scribe_Values.Look(ref Purpose, "purpose");
            Scribe_Values.Look(ref RouteIndex, "routeIndex");
            Scribe_Values.Look(ref Next, "next", -1);
            Scribe_Values.Look(ref ArrivalRadius, "arrivalRadius", 3);
            Scribe_Values.Look(ref RefreshTicks, "refreshTicks", 60);
            Scribe_Values.Look(ref RetryTicks, "retryTicks", 120);
            Scribe_Collections.Look(ref ArrivalCells, "arrivalCells", LookMode.Value);
            Scribe_Collections.Look(ref AllowedRooms, "allowedRooms", LookMode.Value);
            Scribe_Collections.Look(ref AllowedPortals, "allowedPortals", LookMode.Value);
        }
    }

    public sealed class RaidNodeMemberProgress : IExposable
    {
        public Pawn Pawn;
        public int Completed = -1;
        public IntVec3 Destination = IntVec3.Invalid;
        public int DestinationNode = -1;
        public int RetryAfter;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref Completed, "completed", -1);
            Scribe_Values.Look(ref Destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref DestinationNode, "destinationNode", -1);
            Scribe_Values.Look(ref RetryAfter, "retryAfter");
        }
    }

    internal static class RaidNodeRoute
    {
        internal static void Prepare(Map map, RaidTacticalPlan plan, Helodrace.Squads.DoctrineDef doctrine = null)
        {
            if (plan.MovementNodes.Count > 0) return;
            List<IntVec3> route = plan.ApproachPath.Where(cell => cell.InBounds(map)).ToList();
            if (route.Count == 0) route.Add(plan.Entry);
            if (route.Any(cell => !cell.InBounds(map))) return;
            RaidStructureSnapshot structure = map.GetComponent<MapComponent_RaidTacticalPlans>()
                .GetStructure(plan.OrganizationId);
            bool Portal(int i) => route[i].GetEdifice(map) is Building_Door
                || i > 0 && !(route[i - 1].GetEdifice(map) is Building_Door) && structure != null
                    && structure.RoomAt(route[i - 1]) != structure.RoomAt(route[i]);
            bool Cover(IntVec3 cell) => structure != null && structure.RoomAt(cell) == 0
                && GenAdj.CardinalDirections.Select(offset => cell + offset).Where(next => next.InBounds(map))
                    .Any(next => structure.Version.Geometry.Input.Cells[map.cellIndices.CellToIndex(next)]
                        .Has(TacticalRawFlags.Edifice));
            bool[] cover = route.Select(Cover).ToArray();
            bool Boundary(int i) => i >= 3 && i + 2 < route.Count
                && (cover[i] && cover[i + 1] && cover[i + 2] && !cover[i - 1]
                    || !cover[i] && cover[i - 1] && cover[i - 2] && cover[i - 3]);
            var indices = TacticalNodeProgress.Select(route.Count, i => Portal(i) || Boundary(i),
                (a, b) => WalkLine(map, route[a], route[b]), doctrine?.movementNodeSpan ?? 16);
            foreach (int index in indices)
            {
                bool last = index == route.Count - 1;
                var node = new RaidMovementNode {
                    Id = plan.MovementNodes.Count, RouteIndex = index, Center = route[index],
                    StructureVersion = structure?.Version.Id ?? 0,
                    Purpose = last ? plan.BreachCell.IsValid ? RaidMovementNodePurpose.BreachPreparation
                        : RaidMovementNodePurpose.Gather : Portal(index) ? RaidMovementNodePurpose.Portal
                        : Boundary(index) && cover[index] ? RaidMovementNodePurpose.Cover : RaidMovementNodePurpose.Transit
                };
                // Wait beyond a portal, never occupy the door tile while the rest crosses.
                if (node.Purpose == RaidMovementNodePurpose.Portal && route[index].GetEdifice(map) is Building_Door
                    && index + 1 < route.Count)
                    node.Center = route[index + 1];
                node.ArrivalRadius = node.Purpose == RaidMovementNodePurpose.Portal
                    ? doctrine?.movementPortalRadius ?? 2 : doctrine?.movementArrivalRadius ?? 3;
                node.RefreshTicks = doctrine?.movementArrivalRefreshTicks ?? 60;
                node.RetryTicks = doctrine?.movementDestinationRetryTicks ?? 120;
                node.ArrivalCells = ConnectedArea(map, node.Center, node.ArrivalRadius, plan.AvoidedTrapCells);
                if (plan.MovementNodes.Count > 0) plan.MovementNodes.Last().Next = node.Id;
                plan.MovementNodes.Add(node);
            }
            foreach (RaidMovementNode node in plan.MovementNodes)
            {
                int from = node.Id == 0 ? 0 : plan.MovementNodes[node.Id - 1].RouteIndex;
                List<IntVec3> segment = route.Skip(from).Take(node.RouteIndex - from + 2).ToList();
                node.AllowedRooms = segment.Select(cell => structure?.RoomAt(cell) ?? 0)
                    .Concat(new[] { structure?.RoomAt(node.Center) ?? 0 }).Distinct().ToList();
                node.AllowedPortals = segment.Where(cell => cell.GetEdifice(map) is Building_Door).Distinct().ToList();
            }
        }

        // Walkability, including diagonal shoulders: visibility alone does not prove a connection.
        internal static bool WalkLine(Map map, IntVec3 from, IntVec3 to)
        {
            if (!from.InBounds(map) || !to.InBounds(map)) return false;
            return TacticalNodeProgress.WalkLine(from.x, from.z, to.x, to.z,
                (x, z) => new IntVec3(x, 0, z).Standable(map));
        }

        internal static List<IntVec3> ConnectedArea(Map map, IntVec3 center, int radius,
            ICollection<IntVec3> avoided)
        {
            var result = new List<IntVec3>();
            if (!center.InBounds(map) || !center.Standable(map)) return result;
            var queue = new Queue<IntVec3>();
            var seen = new HashSet<IntVec3> { center };
            queue.Enqueue(center);
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                if (!avoided.Contains(cell)) result.Add(cell);
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + offset;
                    if (next.DistanceToSquared(center) > radius * radius || !next.InBounds(map)
                        || !seen.Add(next) || !next.Standable(map)
                        || next.GetEdifice(map) is Building_Door || !WalkLine(map, center, next)) continue;
                    queue.Enqueue(next);
                }
            }
            return result;
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        internal bool AllowsNodeStep(Pawn pawn, IntVec3 cell)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
            if (order == null || order.Reactive || !cell.InBounds(map)) return true;
            ExecutionState state = StateFor(order.UnitId);
            RaidTacticalPlan plan = state?.ActivePlan;
            if (plan == null || state.Phase != RaidExecutionPhase.Assemble || state.ApproachComplete) return true;
            RaidNodeMemberProgress progress = state.NodeMembers.FirstOrDefault(member => member.Pawn == pawn);
            if (progress == null || progress.Completed >= plan.MovementNodes.Count - 1) return true;
            int next = Math.Min(progress.Completed + 1, plan.MovementNodes.Count - 1);
            if (next < 0) return true;
            // Physical door crossing permission is separate from path-cost preference.
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure == null) return true;
            RaidMovementNode connection = plan.MovementNodes[next];
            int currentRoom = structure.RoomAt(pawn.Position);
            // An evaded member may exit its off-route room to rejoin, but cannot use another entry as a shortcut.
            int room = structure.RoomAt(cell);
            return TacticalNodeProgress.AllowsStep(currentRoom, room, cell.GetEdifice(map) is Building_Door,
                connection.AllowedPortals.Contains(cell), connection.AllowedRooms.Contains);
        }

        private static bool FollowNodes(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (state.ApproachComplete) return true;
            Map map = members[0].Map;
            RaidNodeRoute.Prepare(map, plan, Helodrace.Squads.RaidTacticalUnit.ForPawn(members[0])?.Organization.doctrine);
            List<RaidTacticalAssignment> group = plan.Assignments.Where(assignment =>
                assignment.Task != RaidTacticalTask.Withdraw && members.Contains(assignment.Pawn)).ToList();
            if (group.Count == 0)
            {
                state.CurrentNode = plan.MovementNodes.Count;
                state.ApproachComplete = true;
                return true;
            }
            state.NodeMembers.RemoveAll(progress => !members.Contains(progress.Pawn));
            foreach (RaidTacticalAssignment assignment in group)
                if (!state.NodeMembers.Any(progress => progress.Pawn == assignment.Pawn))
                    state.NodeMembers.Add(new RaidNodeMemberProgress { Pawn = assignment.Pawn });
            if (plan.MovementNodes.Count == 0) return false;
            int target = Math.Min(state.CurrentNode, plan.MovementNodes.Count - 1);
            var occupied = new HashSet<IntVec3>(state.NodeMembers.Where(progress =>
                progress.Destination.IsValid && progress.Completed < progress.DestinationNode)
                .Select(progress => progress.Destination));
            occupied.UnionWith(group.Select(assignment => assignment.Pawn.Position));
            float remaining = 0;
            foreach (RaidNodeMemberProgress progress in state.NodeMembers)
            {
                if (!group.Any(assignment => assignment.Pawn == progress.Pawn)) continue;
                Pawn pawn = progress.Pawn;
                // Each member crosses required nodes in order. The lead element never returns for the tail.
                int before = progress.Completed;
                progress.Completed = TacticalNodeProgress.Arrive(before, target, plan.MovementNodes.Count, nextIndex =>
                {
                    RaidMovementNode node = plan.MovementNodes[nextIndex];
                    bool arrived = node.ArrivalCells.Contains(pawn.Position)
                        && RaidNodeRoute.WalkLine(map, node.Center, pawn.Position);
                    // A scattered raid starts from individual positions; the origin is not a mandatory rally.
                    if (nextIndex == 0 && plan.MovementNodes.Count > 1 && node.Purpose == RaidMovementNodePurpose.Transit)
                        arrived = true;
                    return arrived;
                });
                if (progress.Completed > before) state.ApproachProgressTick = tick;
                int next = progress.Completed + 1;
                if (next > target || next >= plan.MovementNodes.Count)
                {
                    if (!IsTaserOperation(pawn))
                    {
                        if (progress.Completed == plan.MovementNodes.Count - 1)
                        {
                            RaidTacticalAssignment assignment = group.First(value => value.Pawn == pawn);
                            if (!AtStagingPosition(assignment, plan)) TryGoto(pawn, assignment.Position);
                            else { HoldPosition(pawn); FaceStackSector(pawn, plan, assignment); }
                        }
                        else HoldPosition(pawn);
                    }
                    continue;
                }
                RaidMovementNode destinationNode = plan.MovementNodes[next];
                remaining += pawn.Position.DistanceTo(destinationNode.Center);
                if (IsTaserOperation(pawn)) continue;
                if (progress.DestinationNode != next || !progress.Destination.InBounds(map)
                    || !progress.Destination.Standable(map)
                    || !RaidNodeRoute.WalkLine(map, destinationNode.Center, progress.Destination))
                {
                    if (tick < progress.RetryAfter) continue;
                    if (tick >= destinationNode.NextAreaRefresh)
                    {
                        destinationNode.ArrivalCells = RaidNodeRoute.ConnectedArea(map, destinationNode.Center,
                            destinationNode.ArrivalRadius, plan.AvoidedTrapCells);
                        destinationNode.NextAreaRefresh = tick + destinationNode.RefreshTicks;
                    }
                    progress.Destination = destinationNode.ArrivalCells.Where(cell => cell.Standable(map)
                            && !occupied.Contains(cell) && RaidNodeRoute.WalkLine(map, destinationNode.Center, cell))
                        .OrderBy(cell => cell.DistanceToSquared(destinationNode.Center))
                        .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                        .DefaultIfEmpty(IntVec3.Invalid).First();
                    progress.DestinationNode = next;
                    if (!progress.Destination.IsValid) { progress.RetryAfter = tick + destinationNode.RetryTicks; continue; }
                    MapComponent_RaidTacticalTrace.Record(pawn, $"node {next}/{plan.MovementNodes.Count - 1} → {progress.Destination}");
                }
                occupied.Add(progress.Destination);
                TryGoto(pawn, progress.Destination);
            }
            if (remaining + 1 < state.ApproachBestRemaining)
            {
                state.ApproachBestRemaining = remaining;
                state.ApproachProgressTick = tick;
            }
            RaidMovementNode current = plan.MovementNodes[target];
            List<RaidNodeMemberProgress> active = state.NodeMembers.Where(progress => group.Any(a => a.Pawn == progress.Pawn)).ToList();
            bool gather = current.Purpose == RaidMovementNodePurpose.Gather
                || current.Purpose == RaidMovementNodePurpose.BreachPreparation;
            Pawn commander = Helodrace.Squads.RaidTacticalUnit.ForPawn(members[0])?.Commander;
            bool required = active.Where(progress => progress.Pawn == state.Breacher || progress.Pawn == commander)
                .All(progress => progress.Completed >= target);
            // A doorway or single-file corner is a transit point, not a place to fit the whole squad.
            // Wait for the tail at the final staging area instead of blocking its only passage.
            if (TacticalNodeProgress.CanAdvance(active.Count, active.Count(progress => progress.Completed >= target),
                required, gather, current.Capacity))
            {
                state.CurrentNode = target + 1;
                state.ApproachBestRemaining = float.MaxValue;
                state.ApproachProgressTick = tick;
                if (state.CurrentNode >= plan.MovementNodes.Count)
                {
                    state.ApproachComplete = true;
                    state.PhaseStarted = tick;
                    state.ReadySince = -1;
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_RaidNodeMovement_AllowedStep
    {
        public static bool Prefix(Pawn ___pawn, IntVec3 ___nextCell)
        {
            if (___pawn?.Spawned != true || !MapComponent_RaidTacticalOrders.Owned(___pawn.CurJob)
                || ___pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.AllowsNodeStep(___pawn, ___nextCell) != false) return true;
            MapComponent_RaidTacticalTrace.Record(___pawn, $"Node connection rejects crossing at {___nextCell}");
            ___pawn.pather.StopDead();
            ___pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            return false;
        }
    }
}
