using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalRoomProgress
    {
        // A later blast can join two former rooms. Require all requested floor
        // regions and actual entry history, rather than an obsolete room count.
        public static bool CoversGoal(TacticalSquadCommand command, IList<IntVec3> regions) =>
            command.GoalSecured && regions.Count > 0 && regions.All(command.SecuredCells.Contains)
                && command.SecuredPlans.Count > 0
                && command.SecuredPlans.Select(plan => plan.Opening).Distinct().Count() == command.SecuredPlans.Count;
    }

    public sealed partial class TacticalRoomFrontier
    {
        public IntVec3 Opening, Inward;
        public int Preference;
        public IntVec3 Inside => Opening + Inward;
    }

    // A one-shot local structural snapshot. No Room IDs, periodic map refresh,
    // enemy lookup or other squad's secured state. Large rooms yield between chunks.
    public sealed class TacticalRoomScan
    {
        public const int CellsPerStep = 128;
        public readonly HashSet<IntVec3> Cells = new HashSet<IntVec3>();
        public readonly List<TacticalRoomFrontier> Frontiers = new List<TacticalRoomFrontier>();
        private readonly Queue<IntVec3> pending = new Queue<IntVec3>();
        private readonly HashSet<IntVec3> boundaries = new HashSet<IntVec3>();
        private readonly Func<IntVec3, bool> floor, barrier;
        private readonly Func<IntVec3, int> preference;
        private readonly IntVec3 entry;
        private HashSet<IntVec3>.Enumerator copy;
        private bool copying;
        public bool Finished { get; private set; }
        public TacticalRoomScan(IntVec3 seed, IntVec3 entry, Func<IntVec3, bool> floor, Func<IntVec3, bool> barrier,
            Func<IntVec3, int> preference = null)
        {
            this.entry = entry; this.floor = floor; this.barrier = barrier; this.preference = preference;
            if (floor(seed)) { Cells.Add(seed); pending.Enqueue(seed); }
        }
        public bool Step()
        {
            if (Finished) return true;
            int remaining = CellsPerStep;
            while (pending.Count > 0 && remaining-- > 0)
            {
                IntVec3 cell = pending.Dequeue();
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (next == entry) continue;
                    if (barrier(next))
                    {
                        if (floor(next + direction) && boundaries.Add(next))
                            Frontiers.Add(new TacticalRoomFrontier { Opening = next, Inward = direction,
                                Preference = preference?.Invoke(next) ?? 0 });
                    }
                    else if (floor(next) && Cells.Add(next)) pending.Enqueue(next);
                }
            }
            return Finished = pending.Count == 0;
        }
        public bool CopyCellsTo(HashSet<IntVec3> secured)
        {
            if (!Finished) return false;
            if (!copying) { copy = Cells.GetEnumerator(); copying = true; }
            for (int i = 0; i < CellsPerStep; i++)
            {
                if (!copy.MoveNext()) { copy.Dispose(); return true; }
                secured.Add(copy.Current);
            }
            return false;
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long RoomScanSteps, RoomsSecured, RoomPlansAttempted, RoomToolRecoveryWaits;
        private void BeginRoomClear(TacticalSquadCommand command, int tick)
        {
            TacticalLocalPlan plan = command.Plan;
            RetireEntryApproach(command);
            IntVec3 seed = plan.Direct ? command.Members.First(m => Available(m, map)).Pawn.Position : plan.Inside;
            var geometry = new Dictionary<IntVec3, byte>();
            Func<IntVec3, bool> wall = cell => cell.InBounds(map) && cell.GetEdifice(map)?.def.IsWall == true;
            Func<IntVec3, bool> walkable = cell => cell.InBounds(map) && cell.Walkable(map);
            byte Kind(IntVec3 cell)
            {
                if (geometry.TryGetValue(cell, out byte kind)) return kind;
                kind = 0;
                if (cell.InBounds(map))
                {
                    Building building = cell.GetEdifice(map);
                    if (building is Building_Door) kind = 3;
                    else if (building?.def.IsWall == true) kind = 2;
                    // Furniture belongs to the floor; a short empty wall gap is
                    // a portal, so opening it does not silently clear both rooms.
                    else if (cell.Roofed(map) && cell.Walkable(map))
                        kind = building == null && TacticalPortalGeometry.IsGap(cell, wall, walkable) ? (byte)4 : (byte)1;
                }
                geometry[cell] = kind; return kind;
            }
            command.RoomScan = new TacticalRoomScan(seed, plan.Direct ? IntVec3.Invalid : plan.Opening,
                cell => Kind(cell) == 1, cell => Kind(cell) >= 2,
                cell => Kind(cell) == 4 ? 800 : Kind(cell) == 3 ? 400 : 0);
            command.RecoveryFrontier = null; command.RoomRecoveryUntil = 0;
            command.Phase = TacticalCommandPhase.Clear; command.PhaseStarted = tick; command.Due = tick + 1;
        }
        // All live members have completed their ingress before this transition.
        // Keep their assigned posts (including small-room exterior guards), but
        // stop reserving the unused approach/mouth throughout the room survey.
        // Bound work to this one footprint; never sweep the map's claim table.
        private void RetireEntryApproach(TacticalSquadCommand command)
        {
            TacticalLocalPlan plan = command.Plan;
            if (plan == null || plan.Direct) return;
            bool Held(IntVec3 cell) => plan.Positions.Contains(cell)
                || command.ContactResponse?.Occupied.Contains(cell) == true
                || command.FieldResponse?.Occupied.Contains(cell) == true;
            void Remove(IntVec3 cell)
            {
                if (!Held(cell) && claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command)
                    claims.Remove(cell);
            }
            foreach (IntVec3 cell in plan.Stack) Remove(cell);
            Remove(plan.Outside); Remove(plan.Opening); Remove(plan.Inside);
            if (plan.EntryLane.IsValid) Remove(plan.EntryLane);
            if (leases.TryGetValue(plan.Opening, out TacticalSquadCommand lease) && lease == command)
                leases.Remove(plan.Opening);
        }
        private void AdvanceRoomClear(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            command.Due = tick + 1;
            // Recovery also needs Plan allowance for its native reach probe.
            // Resume it before consuming that allowance for the room survey.
            if (command.RecoveryFrontier != null)
            {
                Building barrier = command.RecoveryFrontier.Opening.GetEdifice(map);
                bool opened = barrier == null || barrier is Building_Door door && (door.Open || DoorBreachFaultUtility.Jammed(door));
                if (opened || active.Any(m => TacticalBreachTools.CanUse(m.Pawn, barrier)))
                { command.RecoveryFrontier = null; command.RoomRecoveryUntil = 0; }
                else if (tick < command.RoomRecoveryUntil && RecoverBreachTool(command, active, tick))
                { RoomToolRecoveryWaits++; command.Due = tick + 30; return; }
                else
                {
                    command.RecoveryFrontier = null; command.RoomRecoveryUntil = -1;
                    command.FrontierCursor++; command.LastPlanFailure |= TacticalPlanFailure.Tool;
                }
            }
            if (!budget.TryPlan(tick)) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (command.SurveyPending)
                {
                    // Recreate only the interrupted bounded local survey. All
                    // already secured rooms/frontiers and native Jobs survive.
                    command.SurveyPending = false; BeginRoomClear(command, tick);
                    return;
                }
                if (command.RoomScan != null)
                {
                    RoomScanSteps++;
                    if (!command.RoomScan.Step()) return;
                    TacticalRoomScan scan = command.RoomScan;
                    if (!scan.CopyCellsTo(command.SecuredCells)) return;
                    if (scan.Cells.Count > 0)
                    {
                        command.GoalSecured |= scan.Cells.Contains(command.Goal);
                        command.SecuredPlans.Add(command.Plan); RoomsSecured++;
                        foreach (TacticalRoomFrontier frontier in scan.Frontiers)
                            if (command.FrontierKeys.Add(frontier.Opening))
                                command.Frontiers.Add(frontier);
                    }
                    command.RoomScan = null;
                    command.Frontiers.RemoveAll(f => command.SecuredCells.Contains(f.Inside));
                    // Goal-directed progress first, then adjacent unentered rooms.
                    // The order is latched once per completed entry, not refreshed.
                    command.Frontiers.Sort((a, b) => FrontierScore(a, command).CompareTo(FrontierScore(b, command)));
                    command.FrontierCursor = 0;
                    command.MedicalWindowUntil = tick + 3600;
                    command.NextMedicalCheck = tick + 1; command.MedicalCursor = 0;
                    command.MedicalCandidate = null; command.MedicalCandidateScore = 0;
                    // Give bounded care selection an opportunity before choosing
                    // the next room or returning a completed allocated mission.
                    return;
                }
                if (AllocatedAreaSecured(command))
                { AllocatedAreasSecured++; BeginReturn(command, tick, false); return; }
                if (tick < command.PlanRetryAt) { command.Due = command.PlanRetryAt; return; }
                bool busy = false;
                for (int examined = 0; examined < 2 && command.FrontierCursor < command.Frontiers.Count; examined++)
                {
                    TacticalRoomFrontier frontier = command.Frontiers[command.FrontierCursor++];
                    if (command.SecuredCells.Contains(frontier.Inside)) continue;
                    TacticalLocalPlan plan = PlanRoomFrontier(command, active, frontier, out bool occupied, out bool missingTool);
                    busy |= occupied; RoomPlansAttempted++;
                    if (plan == null && missingTool && command.RoomRecoveryUntil >= 0
                        && command.BreachTools.Any(tool => TacticalBreachTools.SourceFor(tool)?.Spawned == true))
                    {
                        command.RecoveryFrontier = frontier; command.RoomRecoveryUntil = tick + 1200;
                        command.FrontierCursor--; return;
                    }
                    if (plan == null) continue;
                    ReleaseClaims(command); command.Plan = plan; PlansBuilt++;
                    command.Link.Cooperation.LocalReady = false;
                    foreach (IntVec3 cell in plan.Stack.Concat(plan.Positions)) claims[cell] = command;
                    claims[plan.Outside] = claims[plan.Opening] = claims[plan.Inside] = command;
                    if (plan.EntryLane.IsValid) claims[plan.EntryLane] = command;
                    leases[plan.Opening] = command;
                    command.OpeningAction = null; command.ChargeAction = null;
                    command.HadConnectedStack &= TacticalLocalPlanner.Connected(plan.Stack);
                    command.Breacher = active.Find(m => TacticalBreachTools.CanUse(m.Pawn, plan.Barrier))?.Pawn;
                    foreach (TacticalMemberCommand member in command.Members)
                    { member.Passed = member.Crossed = member.Entered = member.EntryAssignmentDone = false; member.Parking = IntVec3.Invalid; }
                    command.BarrierHitPoints = -1; command.PhaseStarted = tick;
                    command.Phase = plan.ExistingOpening ? TacticalCommandPhase.Observe : TacticalCommandPhase.Stack;
                    return;
                }
                if (busy) command.FrontierBusy = true;
                if (command.FrontierCursor < command.Frontiers.Count) return;
                if (command.FrontierBusy && tick - command.PhaseStarted < 1200)
                {
                    command.FrontierBusy = false; command.FrontierCursor = 0;
                    command.PlanRetryAt = tick + 240; command.Due = command.PlanRetryAt; return;
                }
                bool unfinished = command.SecuredPlans.Count > 0 && (!command.GoalSecured
                    || command.Frontiers.Any(f => !command.SecuredCells.Contains(f.Inside)));
                if (unfinished) command.LastPlanFailure |= TacticalPlanFailure.Unsecured;
                BeginReturn(command, tick, false, replan: unfinished);
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }
        private int FrontierScore(TacticalRoomFrontier frontier, TacticalSquadCommand command)
        {
            int observedThreat = 0;
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                if (contact.Door && contact.Position == frontier.Opening) { observedThreat = 600; break; }
            // Latched structural preference plus actual contact knowledge.
            // Never reorder entrances by an unseen door's current Open state.
            return (command.GoalSecured ? frontier.Opening.DistanceToSquared(command.Plan.Inside)
                : frontier.Inside.DistanceToSquared(CooperativeGoal(command))) - frontier.Preference - observedThreat;
        }

        private TacticalLocalPlan PlanRoomFrontier(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalRoomFrontier frontier, out bool occupied, out bool missingTool)
        {
            occupied = missingTool = false;
            Building barrier = frontier.Opening.GetEdifice(map);
            bool opened = barrier == null || barrier is Building_Door door && (door.Open || DoorBreachFaultUtility.Jammed(door));
            if (!opened && !active.Any(m => TacticalBreachTools.CanUse(m.Pawn, barrier)))
            { missingTool = true; return null; }
            if (leases.TryGetValue(frontier.Opening, out TacticalSquadCommand owner) && owner != command)
            { occupied = true; return null; }
            bool touchedClaim = false;
            bool Claimed(IntVec3 cell)
            {
                bool blocked = claims.TryGetValue(cell, out TacticalSquadCommand other) && other != command;
                touchedClaim |= blocked; return blocked;
            }
            bool OutsideBlocked(IntVec3 cell) => !command.SecuredCells.Contains(cell) || Claimed(cell);
            bool InsideBlocked(IntVec3 cell) => command.SecuredCells.Contains(cell) || Claimed(cell);
            var plan = new TacticalLocalPlan { Opening = frontier.Opening, Inward = frontier.Inward, Barrier = barrier,
                ExistingOpening = opened };
            if (!TacticalLocalPlanner.Free(map, plan.Outside, OutsideBlocked) || !TacticalLocalPlanner.Free(map, plan.Inside, InsideBlocked))
            { occupied = Claimed(plan.Outside) || Claimed(plan.Inside); return null; }
            if (!TacticalLocalPlanner.BuildStack(map, plan, command.Members.Count, OutsideBlocked))
            { occupied = touchedClaim; return null; }
            if (!TacticalLocalPlanner.BuildPositions(map, plan, command.Members.Count, InsideBlocked,
                cell => cell.Roofed(map) && !command.SecuredCells.Contains(cell)))
            {
                if (touchedClaim) { occupied = true; return null; }
                var eligible = active.OrderByDescending(m => m.Pawn == command.Breacher).ThenBy(m => m.Rear)
                    .Select(m => command.Members.IndexOf(m)).ToArray();
                if (!TacticalEntryAllocation.Allocate(plan, eligible)) return null;
            }
            if (!active[0].Pawn.CanReach(plan.Outside, PathEndMode.OnCell, Danger.Deadly)) return null;
            return plan;
        }
    }
}
