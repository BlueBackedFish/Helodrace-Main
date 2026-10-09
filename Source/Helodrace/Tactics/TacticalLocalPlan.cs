using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    [Flags]
    public enum TacticalPlanFailure { None = 0, Busy = 1, NotBoundary = 2, Tool = 4, Obstructed = 8, Stack = 16, Inside = 32, Unreachable = 64, Unsecured = 128 }
    // Only the small physical footprint of one command. No topology/grid cache.
    public sealed partial class TacticalLocalPlan
    {
        public IntVec3 Opening, Inward;
        public IntVec3 EntryLane = IntVec3.Invalid;
        public IntVec3 Outside => Opening - Inward;
        public IntVec3 Inside => Opening + Inward;
        public Building Barrier;
        public List<IntVec3> Stack = new List<IntVec3>();
        public List<IntVec3> Positions = new List<IntVec3>();
        // Member indices held on the secured approach face during a small-room entry.
        public HashSet<int> RetainedOutside = new HashSet<int>();
        public HashSet<IntVec3> Interior = new HashSet<IntVec3>();
        public bool Direct, ExistingOpening;
    }

    public static class TacticalLocalPlanner
    {
        private static readonly int[] Offsets = { 0, -1, 1, -3, 3, -6, 6, 10 };
        private static readonly int[] RetryBands = { 0, -12, 12, -24, 24 };
        internal static int SearchBand(int failedAttempts) => failedAttempts < 2 ? 0 : RetryBands[(failedAttempts - 1) % RetryBands.Length];
        internal static bool SearchFreshOpening(bool triedKnown, TacticalPlanFailure failure, int failedAttempts) =>
            !triedKnown || failedAttempts >= 2 && failure == TacticalPlanFailure.Busy;
        public static bool Connected(IList<IntVec3> cells)
        {
            if (cells.Count == 0) return false;
            var remaining = new HashSet<IntVec3>(cells);
            if (remaining.Count != cells.Count) return false;
            var pending = new Queue<IntVec3>(); pending.Enqueue(cells[0]); remaining.Remove(cells[0]);
            while (pending.Count > 0)
            {
                IntVec3 cell = pending.Dequeue();
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                    if (remaining.Remove(cell + direction)) pending.Enqueue(cell + direction);
            }
            return remaining.Count == 0;
        }

        internal static bool Free(Map map, IntVec3 cell, Func<IntVec3, bool> claimed) =>
            cell.InBounds(map) && cell.Standable(map) && !claimed(cell);

        internal static TacticalLocalPlan Find(Map map, Pawn leader, IntVec3 goal,
            int count, Func<IntVec3, bool> claimed, Func<IntVec3, bool> leased, Func<Building, bool> canBreach,
            out TacticalPlanFailure failure, int failedAttempts, int frontage = 0)
        {
            failure = TacticalPlanFailure.None;
            IntVec3 from = leader.Position;
            IntVec3 preferred = Math.Abs(goal.x - from.x) >= Math.Abs(goal.z - from.z)
                ? new IntVec3(Math.Sign(goal.x - from.x), 0, 0) : new IntVec3(0, 0, Math.Sign(goal.z - from.z));
            IntVec3 travel = goal - from;
            bool Wall(IntVec3 cell) => cell.InBounds(map) && cell.GetEdifice(map)?.def.IsWall == true;
            bool Support(IntVec3 cell) => cell.InBounds(map)
                && (cell.GetEdifice(map)?.def.IsWall == true || cell.GetEdifice(map) is Building_Door);
            bool Walkable(IntVec3 cell) => cell.InBounds(map) && cell.Walkable(map);
            IntVec3 first = IntVec3.Invalid;
            int steps = Math.Max(Math.Abs(goal.x - from.x), Math.Abs(goal.z - from.z));
            // One directional probe, capped at 256 cells. It does not run A*.
            for (int i = 1; i <= Math.Min(steps, 256); i++)
            {
                var cell = new IntVec3(from.x + (int)Math.Round((goal.x - from.x) * (double)i / steps), 0,
                    from.z + (int)Math.Round((goal.z - from.z) * (double)i / steps));
                Building building = cell.GetEdifice(map);
                // Ignore an isolated rock/block in front of the actual facade.
                // The local footprint validator still rejects obstructed slots.
                if (building is Building_Door || building?.def.IsWall == true)
                {
                    bool face = TacticalPortalGeometry.TryWallNormal(cell, travel, Support, out IntVec3 normal);
                    if (face || building is Building_Door)
                    { if (face) preferred = normal; first = cell; break; }
                }
                // An already opened wall gap is still a portal. Do not turn
                // later squads into unconstrained vanilla destination paths.
                if (building == null && TacticalPortalGeometry.TryGapNormal(cell, Wall, Walkable, out IntVec3 gapNormal))
                {
                    int heading = gapNormal.x * travel.x + gapNormal.z * travel.z;
                    if (heading != 0) { preferred = gapNormal * Math.Sign(heading); first = cell; break; }
                }
            }
            if (!first.IsValid)
            {
                var direct = new TacticalLocalPlan { Direct = true, Opening = goal, Inward = IntVec3.North };
                // Keep the posts in the objective's connected local space.
                // Radial standability alone could put them across a nearby wall.
                bool roofed = goal.Roofed(map);
                direct.Positions.AddRange(TacticalDestinationFootprint.Find(goal, count,
                    cell => cell.InBounds(map) && cell.Walkable(map) && cell.Roofed(map) == roofed
                        && !(cell.GetEdifice(map) is Building_Door), cell => Free(map, cell, claimed)));
                if (direct.Positions.Count != count) { failure = TacticalPlanFailure.Inside; return null; }
                if (leader.CanReach(direct.Positions[0], Verse.AI.PathEndMode.OnCell, Danger.Deadly)) return direct;
                failure = TacticalPlanFailure.Unreachable; return null;
            }
            var candidates = new List<TacticalLocalPlan>(8);
            IntVec3 tangent = new IntVec3(-preferred.z, 0, preferred.x);
            // Change the sampled frontage after repeated failures; never add
            // candidates, native reach probes or an unbounded facade search.
            int band = SearchBand(failedAttempts);
            foreach (int offset in Offsets)
            {
                IntVec3 opening = first + tangent * (offset + frontage + band);
                if (!opening.InBounds(map)) { failure |= TacticalPlanFailure.NotBoundary; continue; }
                if (leased(opening)) { failure |= TacticalPlanFailure.Busy; continue; }
                Building barrier = opening.GetEdifice(map);
                if (barrier == null)
                {
                    if (!TacticalPortalGeometry.IsGap(opening, Wall, Walkable))
                    { failure |= TacticalPlanFailure.NotBoundary; continue; }
                }
                bool openDoor = barrier is Building_Door door && (door.Open || DoorBreachFaultUtility.Jammed(door));
                if (barrier != null && !openDoor && !canBreach(barrier))
                { failure |= TacticalPlanFailure.Tool; continue; }
                if (barrier == null && !opening.Standable(map)) { failure |= TacticalPlanFailure.Obstructed; continue; }
                var plan = new TacticalLocalPlan { Opening = opening, Inward = preferred, Barrier = barrier,
                    ExistingOpening = barrier == null || openDoor };
                if (!Free(map, plan.Outside, claimed) || !Free(map, plan.Inside, claimed)) { failure |= TacticalPlanFailure.Obstructed; continue; }
                if (!BuildStack(map, plan, count, claimed)) { failure |= TacticalPlanFailure.Stack; continue; }
                // A blast at a corner can connect the local flood to outdoors.
                // Keep this footprint on the inside mouth's roof/floor space,
                // as the direct objective and later room-entry planners do.
                bool insideRoofed = plan.Inside.Roofed(map);
                if (!BuildPositions(map, plan, count, claimed, cell => cell.Roofed(map) == insideRoofed))
                { failure |= TacticalPlanFailure.Inside; continue; }
                candidates.Add(plan);
            }
            // Prefer a suitable ordinary door; never spend more than two final
            // reachability probes per command attempt.
            candidates.Sort((a, b) => Score(a, from).CompareTo(Score(b, from)));
            for (int i = 0; i < Math.Min(2, candidates.Count); i++)
                if (leader.CanReach(candidates[i].Outside, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
                { failure = TacticalPlanFailure.None; return candidates[i]; }
            if (candidates.Count > 0) failure |= TacticalPlanFailure.Unreachable;
            return null;
        }
        private static int Score(TacticalLocalPlan plan, IntVec3 from) => plan.Outside.DistanceToSquared(from)
            - (plan.Barrier is Building_Door ? 400 : 0);

        internal static bool BuildStack(Map map, TacticalLocalPlan plan, int count, Func<IntVec3, bool> claimed)
        {
            IntVec3 lateral = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
            foreach (int side in new[] { 1, -1 })
            {
                plan.Stack.Clear();
                // A rectangular, cardinally connected patch on ONE face of the
                // wall. Every column must have a continuous wall behind it;
                // the first corner/obstacle terminates that template.
                for (int column = 1; column <= 8 && plan.Stack.Count < count; column++)
                {
                    IntVec3 wallCell = plan.Opening + lateral * (column * side);
                    Building support = wallCell.InBounds(map) ? wallCell.GetEdifice(map) : null;
                    // A blast-made opening can span several cells. Begin the
                    // connected covered patch at its intact edge, not in the gap.
                    if (support == null && plan.ExistingOpening && column <= 3 && wallCell.InBounds(map) && wallCell.Standable(map)) continue;
                    if (support == null || !(support.def.IsWall || support is Building_Door)) break;
                    for (int depth = 1; depth <= 3 && plan.Stack.Count < count; depth++)
                    {
                        IntVec3 cell = wallCell - plan.Inward * depth;
                        if (!Free(map, cell, claimed)) break;
                        plan.Stack.Add(cell);
                    }
                }
                if (plan.Stack.Count == count && Connected(plan.Stack)) return true;
            }
            plan.Stack.Clear();
            // Short wall: join both sides BEHIND the mouth. Still one wall face,
            // still connected, and neither the outside mouth nor a corner is a post.
            var queue = new Queue<IntVec3>();
            var seen = new HashSet<IntVec3>();
            IntVec3 root = plan.Opening - plan.Inward * 2;
            if (!Free(map, root, claimed)) return false;
            queue.Enqueue(root); seen.Add(root);
            while (queue.Count > 0 && plan.Stack.Count < count)
            {
                IntVec3 cell = queue.Dequeue(); plan.Stack.Add(cell);
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction, delta = next - plan.Opening;
                    int depth = -(delta.x * plan.Inward.x + delta.z * plan.Inward.z);
                    int width = delta.x * lateral.x + delta.z * lateral.z;
                    if (depth < 1 || depth > 3 || Math.Abs(width) > 8 || depth == 1 && width == 0
                        || seen.Contains(next) || !Free(map, next, claimed)) continue;
                    bool continuous = true;
                    for (int i = 1; i <= Math.Abs(width); i++)
                    {
                        IntVec3 wall = plan.Opening + lateral * (i * Math.Sign(width));
                        Building support = wall.InBounds(map) ? wall.GetEdifice(map) : null;
                        if (support == null || !(support.def.IsWall || support is Building_Door)) { continuous = false; break; }
                    }
                    if (!continuous) continue;
                    seen.Add(next); queue.Enqueue(next);
                }
            }
            if (plan.Stack.Count == count && Connected(plan.Stack)) return true;
            plan.Stack.Clear(); return false;
        }

        internal static bool BuildPositions(Map map, TacticalLocalPlan plan, int count, Func<IntVec3, bool> claimed,
            Func<IntVec3, bool> insideAllowed = null)
        {
            IntVec3 lateral = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
            var reachable = plan.Interior;
            reachable.Clear(); reachable.Add(plan.Inside);
            var queue = new Queue<IntVec3>(); queue.Enqueue(plan.Inside);
            // Small near-wall patch, not a live room graph. Expansion is
            // bounded; only cells connected to the inside mouth are selected.
            while (queue.Count > 0 && reachable.Count <= 80)
            {
                IntVec3 current = queue.Dequeue();
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 cell = current + direction;
                    IntVec3 delta = cell - plan.Opening;
                    int depth = delta.x * plan.Inward.x + delta.z * plan.Inward.z;
                    int width = delta.x * lateral.x + delta.z * lateral.z;
                    if (depth < 1 || depth > 4 || Math.Abs(width) > 8 || !cell.InBounds(map)
                        || !cell.Standable(map) || cell.GetEdifice(map) is Building_Door || reachable.Contains(cell)
                        || insideAllowed != null && !insideAllowed(cell)) continue;
                    if (reachable.Count >= 81) break;
                    reachable.Add(cell); queue.Enqueue(cell);
                }
            }
            for (int depth = 1; depth <= 4; depth++)
                for (int width = 1; width <= 8; width++)
                    foreach (int side in new[] { 1, -1 })
                    {
                        IntVec3 cell = plan.Opening + plan.Inward * depth + lateral * (width * side);
                        if (depth == 4 && !GenAdj.CardinalDirections.Any(direction =>
                            (cell + direction).InBounds(map) && (cell + direction).GetEdifice(map)?.def.IsWall == true)) continue;
                        if (plan.Positions.Count < count && reachable.Contains(cell) && Free(map, cell, claimed))
                            plan.Positions.Add(cell);
                    }
            // A tiny room cannot fit a full squad if every center-line tile is
            // discarded. Only the first inside mouth must remain clear.
            // Keep a second mouth cell free: pawn avoidance can otherwise strand
            // a late entrant behind the stationary near-wall posts on both sides.
            IntVec3 lane = plan.Inside + plan.Inward;
            if (reachable.Contains(lane) && Free(map, lane, claimed)) plan.EntryLane = lane;
            for (int depth = 2; depth <= 4 && plan.Positions.Count < count; depth++)
            {
                IntVec3 cell = plan.Opening + plan.Inward * depth;
                if (cell == plan.EntryLane) continue;
                if (reachable.Contains(cell) && Free(map, cell, claimed)) plan.Positions.Add(cell);
            }
            return plan.Positions.Count == count;
        }
    }
}
