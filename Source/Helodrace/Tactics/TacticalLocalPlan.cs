using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    [Flags]
    public enum TacticalPlanFailure { None = 0, Busy = 1, NotBoundary = 2, Tool = 4, Obstructed = 8, Stack = 16, Inside = 32, Unreachable = 64, Unsecured = 128, Classification = 256 }
    // Only the small physical footprint of one command. No topology/grid cache.
    public sealed partial class TacticalLocalPlan
    {
        public IntVec3 Opening, Inward;
        public int Depth = 1;
        public int StackSide; // Preferred observation/entry side; stack uses both wings.
        public IntVec3 EntryLane = IntVec3.Invalid;
        public IntVec3 Outside => Opening - Inward;
        public IntVec3 Inside => Opening + Inward * Depth;
        public IntVec3 InnerOpening => Inside - Inward;
        public Building Barrier;
        // A validated future footprint that needs reachable furniture cleared
        // after opening the wall, before observation/entry can begin.
        public Building PendingFurniture;
        public List<IntVec3> Stack = new List<IntVec3>();
        // Empty means the whole squad; otherwise remaining members guard their
        // existing secured-room posts rather than requiring a full local stack.
        public List<int> StackMembers = new List<int>();
        public List<int> EntryOrder = new List<int>();
        public List<IntVec3> Positions = new List<IntVec3>();
        // Member indices held on the secured approach face during a small-room entry.
        public HashSet<int> RetainedOutside = new HashSet<int>();
        public HashSet<IntVec3> Interior = new HashSet<IntVec3>();
        public bool Direct, ExistingOpening;
        // A failed unlit charge must not be selected again for this same barrier.
        public bool HammerFallback;
        public bool ReportedPassage;
    }

    public static partial class TacticalLocalPlanner
    {
        private static readonly int[] Offsets = { 0, -1, 1, -3, 3, -6, 6, 10 };
        // Sample between sparse offsets before moving to a distant frontage.
        // Otherwise a visible door at offset -7 or -5 is never considered.
        private static readonly int[] RetryBands = { 0, -1, 1, -2, 2, -12, 12, -24, 24 };
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

        internal static bool SetDepth(Map map, TacticalLocalPlan plan, bool allowFurniture = false) =>
            TryDepth(map, plan.Opening, plan.Inward, out plan.Depth, allowFurniture);

        internal static bool TryDepth(Map map, IntVec3 opening, IntVec3 inward, out int depth, bool allowFurniture = false) => TacticalPortalGeometry.TryDepth(opening, inward,
            cell => cell.InBounds(map) && (cell.GetEdifice(map)?.def.IsWall == true || cell.GetEdifice(map) is Building_Door),
            cell => cell.InBounds(map) && (cell.Walkable(map)
                || allowFurniture && TacticalFurniturePolicy.IsFurniture(cell.GetEdifice(map))), out depth);

        internal static Building NextBarrier(Map map, TacticalLocalPlan plan) => NextBarrier(map, plan.Opening, plan.Inward, plan.Depth);

        internal static Building NextBarrier(Map map, IntVec3 opening, IntVec3 inward, int depth)
        {
            IntVec3 cell = TacticalPortalGeometry.FirstBlocked(opening, inward, depth,
                position => CellOpen(map, position));
            return cell.IsValid && cell.InBounds(map) ? cell.GetEdifice(map) : null;
        }

        private static bool CellOpen(Map map, IntVec3 cell) => cell.InBounds(map)
            && (cell.GetEdifice(map) is Building_Door door ? door.Open || DoorBreachFaultUtility.Jammed(door) : cell.Standable(map));

        internal static bool PassageOpen(Map map, TacticalLocalPlan plan) => PassageOpen(map, plan.Opening, plan.Inward, plan.Depth);

        internal static bool PassageOpen(Map map, IntVec3 opening, IntVec3 inward, int depth)
        {
            return TacticalPortalGeometry.PassageOpen(opening, inward, depth, cell => CellOpen(map, cell));
        }

        internal static TacticalPlanSearch Find(Map map, Pawn leader, IntVec3 goal,
            int count, Func<IntVec3, bool> claimed, Func<IntVec3, bool> leased, Func<Building, bool> canBreach,
            out TacticalPlanFailure failure, int failedAttempts, int frontage = 0, Func<IntVec3, bool> secured = null,
            Func<IntVec3, IntVec3, TacticalStructureClass> classify = null, Func<TacticalLocalPlan, bool> recoverFurniture = null,
            Func<TacticalLocalPlan, bool> clearedPassage = null, bool highDoctrine = false)
        {
            failure = TacticalPlanFailure.None;
            var search = new TacticalPlanSearch();
            search.Start(FindSteps(map, leader, goal, count, claimed, leased, canBreach, search,
                failedAttempts, frontage, secured, classify, recoverFurniture, clearedPassage, highDoctrine));
            return search;
        }
        private static IEnumerable<TacticalLocalPlan> FindSteps(Map map, Pawn leader, IntVec3 goal,
            int count, Func<IntVec3, bool> claimed, Func<IntVec3, bool> leased, Func<Building, bool> canBreach,
            TacticalPlanSearch search, int failedAttempts, int frontage, Func<IntVec3, bool> secured,
            Func<IntVec3, IntVec3, TacticalStructureClass> classify, Func<TacticalLocalPlan, bool> recoverFurniture,
            Func<TacticalLocalPlan, bool> clearedPassage, bool highDoctrine)
        {
            IntVec3 from = leader.Position;
            IntVec3 preferred = Math.Abs(goal.x - from.x) >= Math.Abs(goal.z - from.z)
                ? new IntVec3(Math.Sign(goal.x - from.x), 0, 0) : new IntVec3(0, 0, Math.Sign(goal.z - from.z));
            IntVec3 travel = goal - from;
            bool Wall(IntVec3 cell) => cell.InBounds(map) && cell.GetEdifice(map)?.def.IsWall == true;
            bool Support(IntVec3 cell) => cell.InBounds(map)
                && (cell.GetEdifice(map)?.def.IsWall == true || cell.GetEdifice(map) is Building_Door);
            bool GapFloor(IntVec3 cell) => cell.InBounds(map) && cell.Walkable(map)
                && !(cell.GetEdifice(map) is Building_Door);
            IntVec3 first = IntVec3.Invalid;
            int steps = Math.Max(Math.Abs(goal.x - from.x), Math.Abs(goal.z - from.z));
            // One directional probe, capped at 256 cells. It does not run A*.
            for (int i = 1; i <= Math.Min(steps, 256); i++)
            {
                if (i % 32 == 0) yield return null;
                var cell = new IntVec3(from.x + (int)Math.Round((goal.x - from.x) * (double)i / steps), 0,
                    from.z + (int)Math.Round((goal.z - from.z) * (double)i / steps));
                Building building = cell.GetEdifice(map);
                // Ignore an isolated rock/block in front of the actual facade.
                // The local footprint validator still rejects obstructed slots.
                if (building is Building_Door || building?.def.IsWall == true)
                {
                    bool face = TacticalPortalGeometry.TryWallNormal(cell, travel, Support, out IntVec3 normal);
                    if (face || building is Building_Door)
                    {
                        IntVec3 inward = face ? normal : preferred;
                        if (secured?.Invoke(cell + inward) == true) continue;
                        TacticalStructureClass kind = classify?.Invoke(cell, inward) ?? TacticalStructureClass.Target;
                        if (kind == TacticalStructureClass.Pending) { search.Failure = TacticalPlanFailure.Classification; yield break; }
                        if (kind != TacticalStructureClass.Target) continue;
                        preferred = inward; first = cell; break;
                    }
                }
                // An already opened wall gap is still a portal. Do not turn
                // later squads into unconstrained vanilla destination paths.
                if (building == null && TacticalPortalGeometry.TryGapNormal(cell, Wall, GapFloor, out IntVec3 gapNormal))
                {
                    int heading = gapNormal.x * travel.x + gapNormal.z * travel.z;
                    if (heading != 0)
                    {
                        IntVec3 inward = gapNormal * Math.Sign(heading);
                        if (secured?.Invoke(cell + inward) == true) continue;
                        TacticalStructureClass kind = classify?.Invoke(cell, inward) ?? TacticalStructureClass.Target;
                        if (kind == TacticalStructureClass.Pending) { search.Failure = TacticalPlanFailure.Classification; yield break; }
                        if (kind != TacticalStructureClass.Target) continue;
                        preferred = inward; first = cell; break;
                    }
                }
            }
            if (!first.IsValid)
            {
                yield return null;
                var direct = new TacticalLocalPlan { Direct = true, Opening = goal, Inward = IntVec3.North };
                // Keep the posts in the objective's connected local space.
                // Radial standability alone could put them across a nearby wall.
                bool roofed = goal.Roofed(map);
                direct.Positions.AddRange(TacticalDestinationFootprint.Find(goal, count,
                    cell => cell.InBounds(map) && cell.Walkable(map) && cell.Roofed(map) == roofed
                        && !(cell.GetEdifice(map) is Building_Door) && secured?.Invoke(cell) != true, cell => Free(map, cell, claimed)));
                if (direct.Positions.Count != count) { search.Failure = TacticalPlanFailure.Inside; yield break; }
                yield return null;
                if (leader.CanReach(direct.Positions[0], Verse.AI.PathEndMode.OnCell, Danger.Deadly)) { yield return direct; yield break; }
                search.Failure = TacticalPlanFailure.Unreachable; yield break;
            }
            var candidates = new List<TacticalLocalPlan>(8);
            IntVec3 tangent = new IntVec3(-preferred.z, 0, preferred.x);
            // Change the sampled frontage after repeated failures; never add
            // candidates, native reach probes or an unbounded facade search.
            int band = SearchBand(failedAttempts);
            foreach (int offset in Offsets)
            {
                // One cheap geometry candidate per slice. Expensive footprints
                // are built only after ranking, never for all eight choices.
                yield return null;
                IntVec3 opening = first + tangent * (offset + frontage + band);
                if (!opening.InBounds(map)) { search.Failure |= TacticalPlanFailure.NotBoundary; continue; }
                TacticalStructureClass target = classify?.Invoke(opening, preferred) ?? TacticalStructureClass.Target;
                if (target == TacticalStructureClass.Pending) { search.Failure |= TacticalPlanFailure.Classification; continue; }
                if (target != TacticalStructureClass.Target) { search.Failure |= TacticalPlanFailure.NotBoundary; continue; }
                Building barrier = opening.GetEdifice(map);
                if (barrier == null)
                {
                    if (!TacticalPortalGeometry.IsGap(opening, Wall, GapFloor))
                    { search.Failure |= TacticalPlanFailure.NotBoundary; continue; }
                }
                bool openDoor = barrier is Building_Door door && (door.Open || DoorBreachFaultUtility.Jammed(door));
                if (barrier != null && !openDoor && !canBreach(barrier))
                { search.Failure |= TacticalPlanFailure.Tool; continue; }
                if (barrier == null && !opening.Standable(map)) { search.Failure |= TacticalPlanFailure.Obstructed; continue; }
                var plan = new TacticalLocalPlan { Opening = opening, Inward = preferred, Barrier = barrier,
                    ExistingOpening = barrier == null || openDoor };
                if (!SetDepth(map, plan, recoverFurniture != null)) { search.Failure |= TacticalPlanFailure.Inside; continue; }
                plan.ExistingOpening = PassageOpen(map, plan);
                if (plan.ExistingOpening && clearedPassage?.Invoke(plan) == true)
                {
                    plan.ReportedPassage = true; candidates.Add(plan);
                    continue;
                }
                bool busy = false;
                for (int layer = 0; layer < plan.Depth; layer++) busy |= leased(opening + preferred * layer);
                if (busy) { search.Failure |= TacticalPlanFailure.Busy; continue; }
                bool tools = true;
                for (int layer = 0; layer < plan.Depth; layer++)
                {
                    Building next = (opening + preferred * layer).GetEdifice(map);
                    if (next != null && !(next is Building_Door d && (d.Open || DoorBreachFaultUtility.Jammed(d))) && !canBreach(next))
                    { tools = false; break; }
                }
                if (!tools) { search.Failure |= TacticalPlanFailure.Tool; continue; }
                plan.Barrier = NextBarrier(map, plan); plan.ExistingOpening = PassageOpen(map, plan);
                if (secured?.Invoke(plan.Inside) == true) { search.Failure |= TacticalPlanFailure.NotBoundary; continue; }
                candidates.Add(plan);
            }
            var scores = new Dictionary<TacticalLocalPlan, int>(); int doorObservations = 0;
            foreach (TacticalLocalPlan p in candidates)
            {
                bool door = p.Opening.GetEdifice(map) is Building_Door;
                bool visibleOpen = p.ExistingOpening && door && (!highDoctrine
                    || doorObservations++ < 2 && GenSight.LineOfSight(from, p.Opening, map, true));
                scores[p] = p.Outside.DistanceToSquared(from) + TacticalCqbDoctrine.EntryPenalty(highDoctrine,
                    p.ExistingOpening && !door, visibleOpen, door, p.ReportedPassage);
            }
            candidates.Sort((a, b) => scores[a].CompareTo(scores[b]));
            int probes = 0;
            foreach (TacticalLocalPlan plan in candidates)
            {
                yield return null;
                if (plan.ReportedPassage)
                {
                    if (!BuildClearedTransit(map, plan, count, claimed)) { search.Failure |= TacticalPlanFailure.Inside; continue; }
                }
                else
                {
                if (!Free(map, plan.Outside, claimed) || !Free(map, plan.Inside, claimed))
                {
                    yield return null;
                    if (recoverFurniture?.Invoke(plan) == true)
                    { if (plan.PendingFurniture == null) yield break; yield return plan; yield break; }
                    search.Failure |= TacticalPlanFailure.Obstructed; continue;
                }
                if (!BuildStack(map, plan, count, claimed))
                {
                    yield return null;
                    if (recoverFurniture?.Invoke(plan) == true)
                    { if (plan.PendingFurniture == null) yield break; yield return plan; yield break; }
                    search.Failure |= TacticalPlanFailure.Stack; continue;
                }
                yield return null;
                // A blast at a corner can connect the local flood to outdoors.
                // Keep this footprint on the inside mouth's roof/floor space,
                // as the direct objective and later room-entry planners do.
                bool insideRoofed = plan.Inside.Roofed(map);
                if (!BuildPositions(map, plan, count, claimed, cell => cell.Roofed(map) == insideRoofed && secured?.Invoke(cell) != true))
                {
                    yield return null;
                    if (recoverFurniture?.Invoke(plan) == true)
                    { if (plan.PendingFurniture == null) yield break; yield return plan; yield break; }
                    // A small room still gets entered; surplus members take
                    // covered entrance guards during indexed allocation.
                    if (plan.Positions.Count == 0) { search.Failure |= TacticalPlanFailure.Inside; continue; }
                }
                }
                yield return null;
                if (probes++ >= 2) break;
                if (leader.CanReach(plan.ReportedPassage ? plan.Inside : plan.Outside, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
                { search.Failure = TacticalPlanFailure.None; yield return plan; yield break; }
                search.Failure |= TacticalPlanFailure.Unreachable;
            }
            if (candidates.Count > 0 && search.Failure == TacticalPlanFailure.None) search.Failure = TacticalPlanFailure.Unreachable;
        }

        internal static TacticalPlanSearch ReusedFootprint(Map map, Pawn leader, TacticalLocalPlan plan, int count,
            Func<IntVec3,bool> claimed)
        {
            var search = new TacticalPlanSearch();
            search.Start(ReusedFootprintSteps(map, leader, plan, count, claimed, search)); return search;
        }
        internal static TacticalPlanSearch ClearedTransitSearch(Map map, Pawn leader, TacticalLocalPlan source, int count,
            Func<IntVec3,bool> claimed)
        {
            var search = new TacticalPlanSearch();
            search.Start(ClearedTransitSteps(map, leader, source, count, claimed, search)); return search;
        }
        private static IEnumerable<TacticalLocalPlan> ClearedTransitSteps(Map map, Pawn leader, TacticalLocalPlan source, int count,
            Func<IntVec3,bool> claimed, TacticalPlanSearch search)
        {
            var plan = new TacticalLocalPlan { Opening = source.Opening, Inward = source.Inward, Depth = source.Depth,
                ExistingOpening = true, ReportedPassage = true };
            if (!BuildClearedTransit(map, plan, count, claimed)) { search.Failure = TacticalPlanFailure.Inside; yield break; }
            yield return null;
            if (!leader.CanReach(plan.Inside, Verse.AI.PathEndMode.OnCell, Danger.Deadly)) { search.Failure = TacticalPlanFailure.Unreachable; yield break; }
            yield return plan;
        }
        private static IEnumerable<TacticalLocalPlan> ReusedFootprintSteps(Map map, Pawn leader, TacticalLocalPlan plan,
            int count, Func<IntVec3,bool> claimed, TacticalPlanSearch search)
        {
            if (!BuildStack(map, plan, count, claimed)) { search.Failure=TacticalPlanFailure.Stack; yield break; }
            yield return null;
            if (!BuildPositions(map, plan, count, claimed, cell => cell.Roofed(map)==plan.Inside.Roofed(map)) && plan.Positions.Count==0)
            { search.Failure=TacticalPlanFailure.Inside; yield break; }
            yield return null;
            if (!leader.CanReach(plan.Outside, Verse.AI.PathEndMode.OnCell, Danger.Deadly))
            { search.Failure=TacticalPlanFailure.Unreachable; yield break; }
            yield return plan;
        }

        internal static bool BuildStack(Map map, TacticalLocalPlan plan, int count, Func<IntVec3, bool> claimed,
            bool allowPartial = false, Building ignored = null)
        {
            return BuildFormation(map, plan, count, claimed, plan.Stack, allowPartial: allowPartial, ignored: ignored);
        }

        internal static bool BuildFormation(Map map, TacticalLocalPlan plan, int count,
            Func<IntVec3, bool> claimed, List<IntVec3> posts, Func<IntVec3, bool> excluded = null,
            bool allowPartial = false, Building ignored = null)
        {
            // Skip the opaque source, but not intervening walls.
            IntVec3 barrier = plan.Barrier?.Position ?? plan.Opening;
            bool Exposed(IntVec3 cell) => GenSight.LineOfSight(plan.Opening, cell, map, true)
                || barrier != plan.Opening && GenSight.LineOfSight(barrier, cell, map, true);
            return TacticalStackFormation.Build(plan.Opening, plan.Inward, count, ref plan.StackSide,
                cell => TacticalFurniturePolicy.FreeIgnoring(map, cell, claimed, ignored), Exposed, posts, excluded, allowPartial);
        }

        internal static bool BuildPositions(Map map, TacticalLocalPlan plan, int count, Func<IntVec3, bool> claimed,
            Func<IntVec3, bool> insideAllowed = null, Building ignored = null)
        {
            return TacticalEntryFootprint.Build(plan.InnerOpening, plan.Inward, count, plan.StackSide,
                cell => TacticalFurniturePolicy.WalkIgnoring(map, cell, ignored) && !(cell.GetEdifice(map) is Building_Door)
                    && (insideAllowed == null || insideAllowed(cell)),
                cell => TacticalFurniturePolicy.FreeIgnoring(map, cell, claimed, ignored), plan.Positions, plan.Interior, out plan.EntryLane);
        }

    }
}
