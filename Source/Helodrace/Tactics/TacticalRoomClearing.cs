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
        // A blast may remove the roof above the mouth without turning the
        // occupied room into an outdoor passage. Use an actual indoor arrival.
        public static IntVec3 SurveySeed(TacticalLocalPlan plan, IEnumerable<IntVec3> arrived, Func<IntVec3, bool> indoorFloor)
        {
            if (!plan.Direct && indoorFloor(plan.Inside)) return plan.Inside;
            foreach (IntVec3 cell in arrived)
                if (indoorFloor(cell)) return cell;
            return IntVec3.Invalid;
        }

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
        public int Depth = 1;
        public bool Excluded, Outdoors, Approach;
        public IntVec3 Inside => Opening + Inward * Depth;
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
        private readonly Func<IntVec3, bool> exterior;
        private readonly IntVec3 entry;
        private readonly Func<IntVec3, IntVec3, int> portalDepth;
        private HashSet<IntVec3>.Enumerator copy;
        private bool copying;
        private bool copied;
        public bool Finished { get; private set; }
        public TacticalRoomScan(IntVec3 seed, IntVec3 entry, Func<IntVec3, bool> floor, Func<IntVec3, bool> barrier,
            Func<IntVec3, int> preference = null, Func<IntVec3, bool> exterior = null,
            Func<IntVec3, IntVec3, int> portalDepth = null)
        {
            this.portalDepth = portalDepth;
            this.entry = entry; this.floor = floor; this.barrier = barrier; this.preference = preference; this.exterior = exterior;
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
                        int depth = portalDepth?.Invoke(next, direction) ?? 1;
                        if (depth < 1) continue;
                        IntVec3 destination = next + direction * depth;
                        bool outside = exterior?.Invoke(destination) == true;
                        if ((floor(destination) || outside) && boundaries.Add(next))
                            Frontiers.Add(new TacticalRoomFrontier { Opening = next, Inward = direction,
                                Depth = depth, Preference = preference?.Invoke(next) ?? 0, Outdoors = outside });
                    }
                    else if (floor(next) && Cells.Add(next)) pending.Enqueue(next);
                }
            }
            return Finished = pending.Count == 0;
        }
        public bool CopyCellsTo(HashSet<IntVec3> secured, Action<IntVec3> copiedCell = null)
        {
            if (!Finished) return false;
            if (copied) return true;
            if (!copying) { copy = Cells.GetEnumerator(); copying = true; }
            for (int i = 0; i < CellsPerStep; i++)
            {
                if (!copy.MoveNext()) { copy.Dispose(); copied = true; return true; }
                secured.Add(copy.Current);
                copiedCell?.Invoke(copy.Current);
            }
            return false;
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long RoomScanSteps, RoomsSecured, RoomPlansAttempted, RoomToolRecoveryWaits;
        private void BeginRoomClear(TacticalSquadCommand command, int tick)
        {
            command.CqbTogether = false;
            if (command.CqbParent != null) command.CqbEntered = true;
            TacticalLocalPlan plan = command.Plan;
            command.ReportBatch.Clear(); command.ReportFrontierCursor = 0;
            if (command.CqbFollow && plan.ReportedPassage && TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, plan))
            { command.CancelEntryPreview(); command.RoomScan = null; command.Phase = TacticalCommandPhase.Clear; command.Due = tick + 1; return; }
            if (plan.ReportedPassage && TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, plan))
            { command.CancelEntryPreview(); FinishOutdoorPassage(command, tick); return; }
            RetireEntryApproach(command);
            TacticalRoomScan preparedScan = command.EntryPreview?.Matches(command) == true ? command.EntryPreview.Scan : null;
            if (preparedScan == null)
            {
                IntVec3 seed = TacticalRoomProgress.SurveySeed(plan,
                    command.Members.Where(m => Available(m, map) && m.Entered).Select(m => m.Pawn.Position),
                    cell => cell.InBounds(map) && cell.Roofed(map) && cell.Walkable(map) && !(cell.GetEdifice(map) is Building_Door));
                if (!seed.IsValid) { command.CancelEntryPreview(); FinishOutdoorPassage(command, tick); return; }
                preparedScan = CreateRoomScan(command, seed);
            }
            command.Mission.NextGoal = IntVec3.Invalid; command.Mission.FailedPlans = 0;
            command.Mission.Stage = TacticalMissionStage.Searching; command.Mission.Survey = null;
            command.Mission.EmptySurveys = 0; command.Mission.SearchAt = tick;
            command.RoomScan = preparedScan;
            if (TacticalHighCqbPolicy.High(command)) command.CancelEntryPreview();
            command.RecoveryFrontier = null; command.RoomRecoveryUntil = 0;
            command.Phase = TacticalCommandPhase.Clear; command.PhaseStarted = tick; command.Due = tick + 1;
        }
        private TacticalRoomScan CreateRoomScan(TacticalSquadCommand command, IntVec3 seed)
        {
            TacticalLocalPlan plan = command.Plan;
            var geometry = new Dictionary<IntVec3, byte>();
            Func<IntVec3, bool> wall = cell => cell.InBounds(map) && cell.GetEdifice(map)?.def.IsWall == true;
            Func<IntVec3, bool> walkable = cell => cell.InBounds(map) && cell.Walkable(map);
            Func<IntVec3, bool> gapFloor = cell => walkable(cell) && !(cell.GetEdifice(map) is Building_Door);
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
                    else if (cell.Walkable(map))
                        kind = cell != seed && building == null && TacticalPortalGeometry.IsGap(cell, wall, gapFloor)
                            ? (byte)4 : cell.Roofed(map) ? (byte)1 : (byte)0;
                }
                geometry[cell] = kind; return kind;
            }
            int PortalDepth(IntVec3 opening, IntVec3 inward) => TacticalPortalGeometry.TryDepth(opening, inward,
                cell => Kind(cell) == 2 || Kind(cell) == 3,
                walkable, out int depth) ? depth : 0;
            int doorSightProbes = 0;
            int CapturedPreference(IntVec3 cell)
            {
                if (Kind(cell) == 4) return 800;
                if (Kind(cell) != 3) return 0;
                // Capture at most two visible open doors once, during this room
                // survey. Later unseen opening/closing never reorders the plan.
                if (TacticalHighCqbPolicy.High(command) && cell.DistanceToSquared(seed) <= 196
                    && cell.GetEdifice(map) is Building_Door door && door.Open && doorSightProbes++ < 2
                    && GenSight.LineOfSight(seed, cell, map, true)) return 600;
                return 400;
            }
            var scan = new TacticalRoomScan(seed, plan.Direct ? IntVec3.Invalid : plan.InnerOpening,
                cell => Kind(cell) == 1, cell => Kind(cell) >= 2,
                CapturedPreference,
                cell => cell.InBounds(map) && !cell.Roofed(map) && cell.Walkable(map), PortalDepth);
            // The entry was deliberately excluded from the floor survey. Keep
            // its reverse face as the preferred outdoor exit instead of opening
            // a second hole in the same exterior wall after clearing this room.
            if (!plan.Direct && plan.Outside.InBounds(map) && !plan.Outside.Roofed(map))
                scan.Frontiers.Add(new TacticalRoomFrontier { Opening = plan.InnerOpening,
                    Inward = -plan.Inward, Depth = plan.Depth, Outdoors = true, Preference = 1600 });
            return scan;
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
                || command.ContactResponse?.Occupied.Contains(cell) == true;
            void Remove(IntVec3 cell)
            {
                if (!Held(cell) && claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command)
                    claims.Remove(cell);
            }
            foreach (IntVec3 cell in plan.Stack) Remove(cell);
            Remove(plan.Outside); Remove(plan.Inside);
            for (int layer = 0; layer < plan.Depth; layer++)
            {
                IntVec3 cell = plan.Opening + plan.Inward * layer; Remove(cell);
                if (leases.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command) leases.Remove(cell);
            }
            if (plan.EntryLane.IsValid) Remove(plan.EntryLane);
        }
        private void AdvanceRoomClear(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            // Actual combat holds the room; the quiet tail belongs to a small
            // watch while the remaining members can receive the next intent.
            if (HoldRoomCombat(command, active, tick)) return;
            if (command.CqbParent != null && !command.CqbEntered && command.CqbTask != null
                && !TacticalMissionRouting.UnenteredRoom(command.CqbParent, command.CqbTask))
            {
                if (command.CqbTask.Excluded)
                    foreach (var frontier in command.CqbParent.Frontiers)
                        if (frontier.Opening == command.CqbTask.Opening) frontier.Excluded = true;
                command.CqbTask = null; command.CqbArea.Clear(); command.CancelPreparation();
                command.Phase = TacticalCommandPhase.Complete; command.Due = tick + 10; return;
            }
            if (command.CqbParent != null && command.CqbEntered && command.RoomScan == null && !command.SurveyPending)
            { FinishCqbRoom(command, tick); return; }
            if (command.RoomScan == null && !command.SurveyPending && command.RecoveryFrontier == null && tick < command.PlanRetryAt)
            { command.Due = command.PlanRetryAt; return; }
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            command.Due = tick + 1;
            // Recovery also needs Plan allowance for its native reach probe.
            // Resume it before consuming that allowance for the room survey.
            if (command.RecoveryFrontier != null)
            {
                TacticalRoomFrontier recovery = command.RecoveryFrontier;
                Building barrier = TacticalLocalPlanner.NextBarrier(map, recovery.Opening, recovery.Inward, recovery.Depth);
                bool opened = TacticalLocalPlanner.PassageOpen(map, recovery.Opening, recovery.Inward, recovery.Depth);
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
                if (!PrepareCqbPursuit(command,tick,ownsPlanAllowance:true)) return;
                PrepareStructureTargets(command, tick);
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
                    if (!scan.CopyCellsTo(command.SecuredCells, cell => ReportSecuredCell(command, cell, tick))) return;
                    FlushSecuredReport(command, tick);
                    for (int i = 0; i < 2 && command.ReportFrontierCursor < scan.Frontiers.Count; i++)
                        FlushSecuredReport(command, tick, portalOnly: true);
                    if (command.ReportFrontierCursor < scan.Frontiers.Count) return;
                    if (scan.Cells.Count > 0)
                    {
                        command.Contacts.Pursuit.RecordEntered(scan.Cells,scan.Frontiers,tick);
                        command.GoalSecured |= scan.Cells.Contains(command.Goal);
                        if (!command.SecuredPlans.Any(p => p.Opening == command.Plan.Opening)) command.SecuredPlans.Add(command.Plan);
                        command.Mission.Traversed.Add(command.Plan.Opening); RoomsSecured++;
                        command.SecuredRegionProbes.Clear();
                        foreach (TacticalRoomFrontier frontier in scan.Frontiers)
                            if (command.FrontierKeys.Add(frontier.Opening))
                                command.Frontiers.Add(frontier);
                    }
                    command.RoomScan = null;
                    command.Frontiers.RemoveAll(f => command.SecuredCells.Contains(f.Inside));
                    // Sighted room/its exits first, then goal-directed rooms.
                    // The order is latched once per completed entry, not refreshed.
                    command.Frontiers.Sort((a, b) => FrontierScore(a, command).CompareTo(FrontierScore(b, command)));
                    command.CqbPursuitOrderVersion=command.Contacts.Pursuit.Version;
                    command.FrontierCursor = 0;
                    command.MedicalWindowUntil = tick + 3600;
                    command.NextMedicalCheck = tick + 1; command.MedicalCursor = 0;
                    command.MedicalCandidate = null; command.MedicalCandidateScore = 0;
                    // Give bounded care selection an opportunity before choosing
                    // the next room or returning a completed allocated mission.
                    return;
                }
                if (command.CqbPursuitOrderVersion != command.Contacts.Pursuit.Version)
                {
                    if (command.Contacts.Pursuit.Version > 0)
                        command.Frontiers.Sort((a,b)=>FrontierScore(a,command).CompareTo(FrontierScore(b,command)));
                    command.FrontierCursor=0; command.CqbPursuitOrderVersion=command.Contacts.Pursuit.Version;
                }
                // An allocated room is not the whole settlement. Continue to
                // known yards/buildings instead of stopping at its first room.
                bool busy = false;
                for (int examined = 0; examined < 2 && command.FrontierCursor < command.Frontiers.Count; examined++)
                {
                    TacticalRoomFrontier frontier = command.Frontiers[command.FrontierCursor++];
                    if (frontier.Excluded || command.SecuredCells.Contains(frontier.Inside) || command.SecuredKnowledge.Cleared(frontier.Inside)
                        || !frontier.Outdoors && command.Mission.Traversed.Contains(frontier.Opening)) continue;
                    if (frontier.Outdoors && (!TacticalMissionRouting.TransitGoal(command).IsValid || frontier.Approach)) continue;
                    if (command.Link.Cooperation.Active && !command.Link.Cooperation.Agenda.OwnsSide(command.Id, frontier.Inside)) continue;
                    if (frontier.Approach)
                    {
                        if (!frontier.Inside.InBounds(map)
                            || !TacticalMissionRouting.FacadeDestination(command, frontier.Opening, frontier.Inside, frontier.Inside.Roofed(map)))
                        { frontier.Excluded = true; continue; }
                        TacticalStructureClass kind = ClassifyStructure(command, frontier.Opening, frontier.Inward, active[0].Pawn, frontier.Depth);
                        if (kind == TacticalStructureClass.Pending) { command.FrontierCursor--; return; }
                        frontier.Excluded = true;
                        if (kind == TacticalStructureClass.Target)
                        {
                            IntVec3 approach = frontier.Opening - frontier.Inward;
                            SelectMissionDestination(command, frontier.Inside, tick, approach.InBounds(map) && !approach.Roofed(map)); return;
                        }
                        continue;
                    }
                    if (!PrepareRoomFrontier(command, active, frontier, out TacticalLocalPlan plan, out bool occupied, out bool missingTool))
                    { command.FrontierCursor--; command.Due = tick + 1; return; }
                    if (command.FurnitureAction != null) { command.FrontierCursor--; command.Due = tick + 1; return; }
                    if (plan == null && command.DeferredWork) { command.FrontierCursor--; return; }
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
                    foreach (IntVec3 cell in plan.Positions) claims[cell] = command;
                    if (!plan.ReportedPassage)
                    {
                        foreach (IntVec3 cell in plan.Stack) claims[cell] = command;
                        claims[plan.Outside] = claims[plan.Inside] = command;
                        for (int layer = 0; layer < plan.Depth; layer++)
                        {
                            IntVec3 cell = plan.Opening + plan.Inward * layer;
                            claims[cell] = command; leases[cell] = command;
                        }
                        if (plan.EntryLane.IsValid) claims[plan.EntryLane] = command;
                    }
                    command.OpeningAction = null; command.ChargeAction = null;
                    command.MovementCursor = 0; command.MovementDispatchAt = tick;
                    bool ownExit = frontier.Outdoors && plan.ExistingOpening;
                    if (!ownExit && !plan.ReportedPassage) command.HadConnectedStack &= TacticalEntryAllocation.ConnectedStack(plan);
                    command.Breacher = active.Find(m => (plan.StackMembers.Count == 0 || plan.StackMembers.Contains(command.Members.IndexOf(m)))
                        && TacticalBreachTools.CanUse(m.Pawn, plan.Barrier))?.Pawn;
                    foreach (TacticalMemberCommand member in command.Members)
                    { member.Passed = member.Crossed = member.Entered = member.EntryAssignmentDone = false; member.Parking = IntVec3.Invalid; }
                    command.BarrierHitPoints = -1; command.PhaseStarted = tick;
                    command.Phase = ownExit || plan.ReportedPassage ? TacticalCommandPhase.Enter
                        : plan.ExistingOpening ? TacticalCommandPhase.Observe : TacticalCommandPhase.Stack;
                    if (ownExit)
                        foreach (TacticalMemberCommand member in active)
                        {
                            IntVec3 delta = member.Pawn.Position - plan.Opening;
                            if (delta.x * plan.Inward.x + delta.z * plan.Inward.z >= plan.Depth && !member.Pawn.Position.Roofed(map))
                                member.Passed = member.Crossed = true;
                        }
                    return;
                }
                if (busy) command.FrontierBusy = true;
                if (command.FrontierCursor < command.Frontiers.Count) return;
                if (command.CqbParent != null)
                {
                    // A failed assigned boundary is not a completed room. Keep
                    // the task and retry slowly instead of starting a new survey.
                    command.FrontierCursor = 0; command.PlanRetryAt = tick + 600;
                    command.Due = command.PlanRetryAt; return;
                }
                if (command.FrontierBusy && tick - command.PhaseStarted < 1200)
                {
                    command.FrontierBusy = false; command.FrontierCursor = 0;
                    command.PlanRetryAt = tick + 240; command.Due = command.PlanRetryAt; return;
                }
                IntVec3 transit = TacticalMissionRouting.TransitGoal(command);
                if (transit.IsValid)
                {
                    // A temporarily unusable exit must not replace the observed
                    // destination with another survey of the building we are in.
                    SelectMissionDestination(command, transit, tick); return;
                }
                if (ContinueAssaultMission(command, active, tick)) return;
                BeginReturn(command, tick, false);
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }
        private int FrontierScore(TacticalRoomFrontier frontier, TacticalSquadCommand command, bool? goalSecured = null)
        {
            int observedThreat = 0;
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                if (contact.Door && contact.Position == frontier.Opening) { observedThreat = 600; break; }
            // Latched structural preference plus actual contact knowledge.
            // Never reorder entrances by an unseen door's current Open state.
            var root=command.CqbParent ?? command;
            bool follow=root.Link.PursuitDivision.Role(root.Id,root.Goal,GenTicks.TicksGame)!=TacticalPursuitRole.Clear;
            return command.Contacts.Pursuit.Priority(frontier,GenTicks.TicksGame,follow) * 200000
                + ((goalSecured ?? command.GoalSecured) ? frontier.Opening.DistanceToSquared(command.Plan.Inside)
                : frontier.Inside.DistanceToSquared(CooperativeGoal(command)))
                + TacticalCqbDoctrine.EntryPenalty(TacticalHighCqbPolicy.High(command), frontier.Preference >= 800,
                    frontier.Preference == 600, frontier.Preference >= 400 && frontier.Preference < 800, false) - observedThreat
                + (frontier.Outdoors ? 100000 : 0);
        }

        private bool PrepareRoomFrontier(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalRoomFrontier frontier, out TacticalLocalPlan plan, out bool occupied, out bool missingTool)
        {
            plan = null; occupied = missingTool = false;
            if (command.Preparation != null && !command.Preparation.Matches(command, active, frontier.Inside)) command.CancelPreparation();
            if (command.Preparation == null)
            {
                if (!AdoptEntryPreview(command, active, frontier))
                {
                    var preparation = command.Preparation = new TacticalPlanPreparation(command, active, frontier.Inside);
                    preparation.Search = PlanRoomFrontier(command, active, frontier);
                    preparation.Stage = TacticalPreparationStage.Search;
                }
            }
            bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command;
            if (!FinishPreparation(command, active, Claimed, out plan, out TacticalPlanFailure failure)) return false;
            command.LastPlanFailure = failure;
            occupied = (failure & TacticalPlanFailure.Busy) != 0;
            missingTool = (failure & TacticalPlanFailure.Tool) != 0;
            command.DeferredWork |= (failure & TacticalPlanFailure.Classification) != 0;
            return true;
        }
        private TacticalPlanSearch PlanRoomFrontier(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalRoomFrontier frontier)
        {
            var search = new TacticalPlanSearch(); search.Start(RoomFrontierSteps(command, active, frontier, search)); return search;
        }
        private IEnumerable<TacticalLocalPlan> RoomFrontierSteps(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalRoomFrontier frontier, TacticalPlanSearch search, HashSet<IntVec3> previewFloor = null)
        {
            if (frontier.Outdoors && !TacticalMissionRouting.TransitGoal(command).IsValid) yield break;
            TacticalStructureClass kind = frontier.Outdoors && command.SecuredCells.Contains(frontier.Opening - frontier.Inward)
                ? TacticalStructureClass.Target : ClassifyStructure(command, frontier.Opening, frontier.Inward, active[0].Pawn, frontier.Depth);
            if (kind == TacticalStructureClass.Pending) { search.Failure = TacticalPlanFailure.Classification; yield break; }
            if (kind != TacticalStructureClass.Target) { frontier.Excluded = true; yield break; }
            var plan = new TacticalLocalPlan { Opening = frontier.Opening, Inward = frontier.Inward, Depth = frontier.Depth };
            if (!TacticalLocalPlanner.SetDepth(map, plan, allowFurniture: true)) yield break;
            plan.Depth = Math.Max(plan.Depth, frontier.Depth);
            // A changed roof/channel can turn a stored room boundary into an
            // outdoor exit. It must obey the same transit-only rule as a new exit.
            if (!plan.Inside.Roofed(map)) frontier.Outdoors = true;
            if (frontier.Outdoors && (!TacticalMissionRouting.TransitGoal(command).IsValid
                || !TacticalLocalPlanner.PassageOpen(map, plan))) yield break;
            Building barrier = plan.Barrier = TacticalLocalPlanner.NextBarrier(map, plan);
            bool opened = plan.ExistingOpening = TacticalLocalPlanner.PassageOpen(map, plan);
            bool cleared = opened && TacticalSharedPassagePolicy.Cleared(command.SecuredKnowledge, plan);
            if (!opened && !active.Any(m => TacticalBreachTools.CanUse(m.Pawn, barrier)))
            { search.Failure = TacticalPlanFailure.Tool; yield break; }
            for (int layer = 0; !cleared && layer < plan.Depth; layer++)
                if (leases.TryGetValue(plan.Opening + plan.Inward * layer, out TacticalSquadCommand owner) && owner != command)
                { search.Failure = TacticalPlanFailure.Busy; yield break; }
            yield return null;
            bool touchedClaim = false;
            bool Claimed(IntVec3 cell)
            {
                bool blocked = claims.TryGetValue(cell, out TacticalSquadCommand other) && other != command;
                touchedClaim |= blocked; return blocked;
            }
            if (cleared)
            {
                if (!TacticalLocalPlanner.BuildClearedTransit(map, plan, command.Members.Count, Claimed))
                { if (touchedClaim) search.Failure = TacticalPlanFailure.Busy; yield break; }
                yield return null;
                if (active[0].Pawn.CanReach(plan.Inside, PathEndMode.OnCell, Danger.Deadly)) yield return plan;
                yield break;
            }
            bool Visited(IntVec3 cell) => command.SecuredCells.Contains(cell) || previewFloor?.Contains(cell) == true;
            bool OutsideBlocked(IntVec3 cell) => !(previewFloor?.Contains(cell) == true || FurnitureApproachSecured(command, cell)) || Claimed(cell);
            bool InsideBlocked(IntVec3 cell) => Visited(cell) || Claimed(cell);
            bool InsideAllowed(IntVec3 cell) => cell.InBounds(map) && cell.Roofed(map) != frontier.Outdoors
                && !Visited(cell);
            bool RecoverFurniture() => previewFloor == null && TryPrepareFurniture(command, active, plan, OutsideBlocked, InsideBlocked,
                InsideAllowed, requireFull: false);
            IEnumerable<TacticalLocalPlan> FurniturePreview()
            {
                if (plan.PendingFurniture == null) yield break; // Actual work is queued; retry this frontier.
                if (plan.Stack.Count < command.Members.Count)
                {
                    bool bound = false;
                    foreach (TacticalLocalPlan result in BindNearbyApproachSteps(command,active,plan,Visited,Claimed))
                    { if (result == null) yield return null; else bound = true; }
                    if (!bound) yield break;
                }
                if (active[0].Pawn.CanReach(plan.Outside, PathEndMode.OnCell, Danger.Deadly)) yield return plan;
            }
            if (!TacticalLocalPlanner.Free(map, plan.Outside, OutsideBlocked) || !TacticalLocalPlanner.Free(map, plan.Inside, InsideBlocked))
            {
                yield return null;
                if (RecoverFurniture()) { foreach (TacticalLocalPlan preview in FurniturePreview()) yield return preview; yield break; }
                if (Claimed(plan.Outside) || Claimed(plan.Inside)) search.Failure = TacticalPlanFailure.Busy;
                yield break;
            }
            var eligible = TacticalEntryOrder.For(command, active, plan)
                .OrderByDescending(i => TacticalBreachTools.CanUse(command.Members[i].Pawn, barrier)).ToArray();
            if (!TacticalLocalPlanner.BuildStack(map, plan, command.Members.Count, OutsideBlocked, allowPartial: !frontier.Outdoors))
            {
                yield return null;
                if (RecoverFurniture()) { foreach (TacticalLocalPlan preview in FurniturePreview()) yield return preview; yield break; }
                // Reusing our own exit needs no second full indoor stack. A
                // tiny room may retain guards outside and cannot hold everyone.
                if (opened && frontier.Outdoors && command.Plan != null
                    && command.Plan.Positions.Count == command.Members.Count
                    && command.Plan.Positions.Distinct().Count() == command.Members.Count
                    && command.Plan.Positions.All(cell => TacticalLocalPlanner.Free(map, cell, Claimed)))
                    plan.Stack.AddRange(command.Plan.Positions);
                else { if (touchedClaim) search.Failure = TacticalPlanFailure.Busy; yield break; }
            }
            yield return null;
            bool partialStack = plan.Stack.Count < command.Members.Count;
            if (partialStack)
            {
                if (RecoverFurniture()) { foreach (TacticalLocalPlan preview in FurniturePreview()) yield return preview; yield break; }
                bool bound = false;
                foreach (TacticalLocalPlan result in BindNearbyApproachSteps(command,active,plan,Visited,Claimed,eligible))
                { if (result == null) yield return null; else bound = true; }
                if (!bound) yield break;
            }
            yield return null;
            bool fullInside = TacticalLocalPlanner.BuildPositions(map, plan, command.Members.Count, InsideBlocked,
                InsideAllowed);
            yield return null;
            if (!fullInside && !touchedClaim && RecoverFurniture())
            { foreach (TacticalLocalPlan preview in FurniturePreview()) yield return preview; yield break; }
            if (!fullInside && touchedClaim && plan.Positions.Count == 0) { search.Failure = TacticalPlanFailure.Busy; yield break; }
            Pawn planner = previewFloor == null ? active[0].Pawn : PendingPlanner(active, plan.Outside);
            if (!planner.CanReach(plan.Outside, PathEndMode.OnCell, Danger.Deadly)) yield break;
            yield return plan;
        }
        private IEnumerable<TacticalLocalPlan> BindNearbyApproachSteps(TacticalSquadCommand command,
            List<TacticalMemberCommand> active, TacticalLocalPlan plan, Func<IntVec3,bool> visited,
            Func<IntVec3,bool> claimed, IList<int> eligible = null)
        {
            eligible = eligible ?? TacticalEntryOrder.For(command,active,plan);
            int count = command.Members.Count, entering = Math.Min(plan.Stack.Count,eligible.Count);
            var used = new HashSet<IntVec3>(plan.Stack);
            bool Walk(IntVec3 cell) => cell.InBounds(map) && visited(cell) && cell.Walkable(map)
                && !claimed(cell) && (!(cell.GetEdifice(map) is Building_Door door) || door.Open || DoorBreachFaultUtility.Jammed(door));
            bool Seat(IntVec3 cell) => !used.Contains(cell) && cell != plan.Outside && cell != plan.EntryLane
                && !(cell.GetEdifice(map) is Building_Door) && TacticalLocalPlanner.Free(map,cell,claimed);
            var guards = new TacticalApproachGuards(plan.Outside,count-entering,Walk,Seat);
            while (!guards.Step()) yield return null;
            if (guards.Valid && TacticalEntryAllocation.BindApproach(plan,count,eligible,guards.Posts)) yield return plan;
        }
    }
}
