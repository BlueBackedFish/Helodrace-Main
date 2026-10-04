using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private readonly Dictionary<Pawn, int> formationRetryAfter = new Dictionary<Pawn, int>();
        private int formationIndexTick = -1;
        private readonly Dictionary<IntVec3, List<RaidTacticalAssignment>> formationIndex =
            new Dictionary<IntVec3, List<RaidTacticalAssignment>>();

        private void IndexFormations()
        {
            if (formationIndexTick == GenTicks.TicksGame) return;
            formationIndexTick = GenTicks.TicksGame;
            formationIndex.Clear();
            foreach (ExecutionState state in states.Values)
            {
                if (state.ActivePlan == null || state.Phase > RaidExecutionPhase.CrossBreach || state.SharedOpeningWait) continue;
                foreach (RaidTacticalAssignment assignment in state.ActivePlan.Assignments)
                {
                    if (state.Phase == RaidExecutionPhase.CrossBreach && assignment.Task == RaidTacticalTask.Entry
                        || assignment.Pawn?.Spawned != true || assignment.Pawn.Map != map
                        || assignment.Pawn.Dead || assignment.Pawn.Downed || !assignment.Position.IsValid) continue;
                    if (!formationIndex.TryGetValue(assignment.Position, out List<RaidTacticalAssignment> owners))
                        formationIndex[assignment.Position] = owners = new List<RaidTacticalAssignment>();
                    owners.Add(assignment);
                }
            }
        }

        internal static bool FormationOccupied(Pawn pawn, IntVec3 cell, bool stationaryOnly = false)
        {
            if (!cell.InBounds(pawn.Map)) return true;
            foreach (Thing thing in cell.GetThingList(pawn.Map))
                if (thing is Pawn other && other != pawn && other.Spawned
                    && (!stationaryOnly || other.pather?.Moving != true)) return true;
            return false;
        }

        private static RaidFormationSlots<IntVec3> FormationClaims(Map map, RaidTacticalPlan plan)
        {
            var slots = new RaidFormationSlots<IntVec3>();
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            execution?.IndexFormations();
            var candidates = new HashSet<IntVec3>(plan.SafeStackCells.Concat(plan.SafeSupportCells)
                .Concat(plan.Assignments.Select(assignment => assignment.Position)));
            if (execution != null)
                foreach (IntVec3 cell in candidates)
                    if (execution.formationIndex.TryGetValue(cell, out List<RaidTacticalAssignment> assignments))
                        foreach (RaidTacticalAssignment assignment in assignments)
                            if (!plan.Assignments.Contains(assignment) && !assignment.Pawn.Dead && !assignment.Pawn.Downed)
                                slots.Claim(cell, assignment.Pawn.thingIDNumber);
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
                if (assignment.Pawn?.Spawned == true && assignment.Pawn.Map == map
                    && !assignment.Pawn.Dead && !assignment.Pawn.Downed && assignment.Position.IsValid)
                    slots.Claim(assignment.Position, assignment.Pawn.thingIDNumber);
            return slots;
        }

        internal static HashSet<IntVec3> OtherFormationCells(Map map, RaidTacticalPlan plan, List<Pawn> members)
        {
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            execution?.IndexFormations();
            var present = new HashSet<Pawn>(members);
            // A future plan may reuse the leading team's stack slots. Execution's
            // workspace lease prevents both teams from moving to them together.
            var queuedPeers = new HashSet<Pawn>(execution?.states.Values.Where(state => state.ActivePlan != null
                    && plan.BreachCell.IsValid && state.ActivePlan.BreachCell.IsValid
                    && state.ActivePlan.Assignments.Any(value => value.Pawn?.Faction == members[0].Faction)
                    && state.ActivePlan.BreachCell.DistanceToSquared(plan.BreachCell) <= 144)
                .SelectMany(state => state.ActivePlan.Assignments.Select(value => value.Pawn)) ?? Enumerable.Empty<Pawn>());
            var result = new HashSet<IntVec3>();
            if (execution != null)
                foreach (var pair in execution.formationIndex)
                    if (pair.Value.Any(value => !present.Contains(value.Pawn) && !queuedPeers.Contains(value.Pawn))) result.Add(pair.Key);
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                if (!present.Contains(pawn) && !queuedPeers.Contains(pawn)) result.Add(pawn.Position);
            return result;
        }

        // Called only for formation work: the opening waypoint remains a shared transit cell.
        private static void RetargetBlockedStackMembers(List<Pawn> members,
            RaidTacticalPlan plan, bool onlyBlocked = false, Pawn exempt = null, bool holdEntry = true)
        {
            using (RaidCpuProfiler.Measure(members.Count > 0 ? members[0].Map : null, RaidCpuStage.Formation))
                RetargetBlockedStackMembersCore(members, plan, onlyBlocked, exempt, holdEntry);
        }

        private static void RetargetBlockedStackMembersCore(List<Pawn> members,
            RaidTacticalPlan plan, bool onlyBlocked, Pawn exempt, bool holdEntry)
        {
            if (members.Count == 0) return;
            Map map = members[0].Map;
            var slots = FormationClaims(map, plan);
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            int tick = GenTicks.TicksGame;
            if (execution != null && tick % 120 == 0)
                foreach (Pawn departed in execution.formationRetryAfter.Keys
                    .Where(pawn => !pawn.Spawned || pawn.Dead || pawn.Downed
                        || execution.formationRetryAfter[pawn] <= tick).ToList())
                    execution.formationRetryAfter.Remove(departed);
            foreach (RaidTacticalAssignment assignment in plan.Assignments.OrderBy(value => value.Pawn?.thingIDNumber))
            {
                Pawn pawn = assignment.Pawn;
                if (!members.Contains(pawn) || pawn == exempt || IsTaserOperation(pawn)
                    || RaidEntryObservation.Active(pawn) != null
                    || !holdEntry && assignment.Task == RaidTacticalTask.Entry
                    || assignment.Task == RaidTacticalTask.Withdraw) continue;
                IntVec3 old = assignment.Position;
                bool usable = old.IsValid && old.InBounds(map) && old.Standable(map)
                    && slots.Available(old, pawn.thingIDNumber)
                    && !FormationOccupied(pawn, old, stationaryOnly: true);
                if (usable && AtStagingPosition(assignment, plan)) continue;
                // A job retry may only mean the movement area is still building.
                // Keep a valid free slot stable; the existing stall repair handles unreachable approaches.
                if (onlyBlocked && usable) continue;
                if (execution != null && execution.formationRetryAfter.TryGetValue(pawn, out int retry)
                    && retry > tick) continue;
                IEnumerable<IntVec3> safe = assignment.Task == RaidTacticalTask.Entry
                    ? (IEnumerable<IntVec3>)plan.SafeStackCells : plan.SafeSupportCells;
                if (!plan.BreachCell.IsValid && old.IsValid && old.InBounds(map))
                    safe = GenRadial.RadialCellsAround(old, 4f, true).Where(cell => cell.InBounds(map)
                        && cell.GetRoom(map) == old.GetRoom(map)
                        && GenSight.LineOfSight(old, cell, map, true)
                        && !(cell.GetEdifice(map) is Building_Trap));
                // Preserve the previous claim unless a replacement actually exists.
                if (slots.TryAssign(safe.Where(cell => cell != old && cell.InBounds(map) && cell.Standable(map))
                        .OrderBy(cell => cell.DistanceToSquared(old.IsValid ? old : pawn.Position)),
                    pawn.thingIDNumber,
                    cell => !FormationOccupied(pawn, cell)
                        && map.pawnDestinationReservationManager.CanReserve(cell, pawn),
                    cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly), out IntVec3 replacement))
                {
                    slots.Release(old, pawn.thingIDNumber);
                    assignment.Position = replacement;
                    if (execution != null) execution.formationIndexTick = -1;
                    execution?.formationRetryAfter.Remove(pawn);
                }
                else if (execution != null) execution.formationRetryAfter[pawn] = tick + 120;
            }
        }
    }
}
