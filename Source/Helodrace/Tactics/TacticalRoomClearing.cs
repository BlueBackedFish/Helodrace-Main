using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed class TacticalRoomFrontier
    {
        public IntVec3 Opening, Inward;
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
        private readonly IntVec3 entry;
        private HashSet<IntVec3>.Enumerator copy;
        private bool copying;
        public bool Finished { get; private set; }
        public TacticalRoomScan(IntVec3 seed, IntVec3 entry, Func<IntVec3, bool> floor, Func<IntVec3, bool> barrier)
        {
            this.entry = entry; this.floor = floor; this.barrier = barrier;
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
                            Frontiers.Add(new TacticalRoomFrontier { Opening = next, Inward = direction });
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
                    if (building?.def.IsWall == true || building is Building_Door) kind = 2;
                    // Furniture belongs to the floor; a short empty wall gap is
                    // a portal, so opening it does not silently clear both rooms.
                    else if (cell.Roofed(map) && cell.Walkable(map))
                        kind = building == null && TacticalPortalGeometry.IsGap(cell, wall, walkable) ? (byte)2 : (byte)1;
                }
                geometry[cell] = kind; return kind;
            }
            command.RoomScan = new TacticalRoomScan(seed, plan.Direct ? IntVec3.Invalid : plan.Opening,
                cell => Kind(cell) == 1, cell => Kind(cell) == 2);
            command.RecoveryFrontier = null; command.RoomRecoveryUntil = 0;
            command.Phase = TacticalCommandPhase.Clear; command.PhaseStarted = tick; command.Due = tick + 1;
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
                }
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
                    foreach (IntVec3 cell in plan.Stack.Concat(plan.Positions)) claims[cell] = command;
                    claims[plan.Outside] = claims[plan.Opening] = claims[plan.Inside] = command;
                    if (plan.EntryLane.IsValid) claims[plan.EntryLane] = command;
                    leases[plan.Opening] = command;
                    command.OpeningAction = null; command.ChargeAction = null;
                    command.HadConnectedStack &= TacticalLocalPlanner.Connected(plan.Stack);
                    command.Breacher = active.Find(m => TacticalBreachTools.CanUse(m.Pawn, plan.Barrier))?.Pawn;
                    foreach (TacticalMemberCommand member in command.Members)
                    { member.Passed = member.Crossed = member.Entered = false; member.Parking = IntVec3.Invalid; }
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
                BeginReturn(command, tick, unfinished);
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }
        private int FrontierScore(TacticalRoomFrontier frontier, TacticalSquadCommand command) =>
            (command.GoalSecured ? frontier.Opening.DistanceToSquared(command.Plan.Inside) : frontier.Inside.DistanceToSquared(command.Goal))
                - (frontier.Opening.GetEdifice(map) == null ? 800
                    : frontier.Opening.GetEdifice(map) is Building_Door door && (door.Open || DoorBreachFaultUtility.Jammed(door)) ? 800
                    : frontier.Opening.GetEdifice(map) is Building_Door ? 400 : 0);

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
            bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out TacticalSquadCommand other) && other != command;
            bool OutsideBlocked(IntVec3 cell) => !command.SecuredCells.Contains(cell) || Claimed(cell);
            bool InsideBlocked(IntVec3 cell) => command.SecuredCells.Contains(cell) || Claimed(cell);
            var plan = new TacticalLocalPlan { Opening = frontier.Opening, Inward = frontier.Inward, Barrier = barrier,
                ExistingOpening = opened };
            if (!TacticalLocalPlanner.Free(map, plan.Outside, OutsideBlocked) || !TacticalLocalPlanner.Free(map, plan.Inside, InsideBlocked))
            { occupied = Claimed(plan.Outside) || Claimed(plan.Inside); return null; }
            if (!TacticalLocalPlanner.BuildStack(map, plan, command.Members.Count, OutsideBlocked)
                || !TacticalLocalPlanner.BuildPositions(map, plan, command.Members.Count, InsideBlocked,
                    cell => cell.Roofed(map) && !command.SecuredCells.Contains(cell))) return null;
            if (!active[0].Pawn.CanReach(plan.Outside, PathEndMode.OnCell, Danger.Deadly)) return null;
            return plan;
        }
    }
}
