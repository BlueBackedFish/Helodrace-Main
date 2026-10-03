using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Helodrace.Squads;

namespace Helodrace
{
    internal sealed class RaidCqbLocalMap
    {
        public const int Radius = 16;
        public const int RefreshTicks = 30;
        private int lastTick = -RefreshTicks;
        private IntVec3 origin;
        private CqbLocalTopology topology;
        private int[] liveBuildings;
        public int Revision { get; private set; }
        public int CellCount => topology?.Rooms.Length ?? 0;

        public bool Refresh(Map map, RaidStructureSnapshot structure, Pawn pawn,
            IntVec3 center, int tick, ISet<IntVec3> avoided, bool force = false)
        {
            IntVec3 nextOrigin = new IntVec3(System.Math.Max(0, center.x - Radius), 0,
                System.Math.Max(0, center.z - Radius));
            if (!force && topology != null && origin == nextOrigin && tick - lastTick < RefreshTicks) return false;
            int width = System.Math.Min(map.Size.x, center.x + Radius + 1) - nextOrigin.x;
            int height = System.Math.Min(map.Size.z, center.z + Radius + 1) - nextOrigin.z;
            int[] rooms = new int[width * height];
            int[] buildings = new int[rooms.Length];
            bool[] usable = new bool[rooms.Length], portals = new bool[rooms.Length];
            for (int i = 0; i < rooms.Length; i++)
            {
                IntVec3 cell = nextOrigin + new IntVec3(i % width, 0, i / width);
                Building building = cell.GetEdifice(map) as Building;
                buildings[i] = building?.thingIDNumber ?? 0;
                TacticalCellData cached = structure.CachedAt(cell);
                rooms[i] = structure.RoomAt(cell);
                portals[i] = cached.WallLine || cached.ExteriorAccess || building is Building_Door
                    || building?.def.IsWall == true;
                usable[i] = cell.Walkable(map) && (rooms[i] > 0 || portals[i])
                    && !(avoided?.Contains(cell) == true)
                    && (!(building is Building_Door door) || door.Open || door.PawnCanOpen(pawn));
            }
            var next = new CqbLocalTopology(width, height, rooms, usable, portals);
            bool same = next.SameAs(topology) && liveBuildings != null && buildings.SequenceEqual(liveBuildings);
            bool changed = topology != null && origin == nextOrigin && !same;
            if (topology == null || origin != nextOrigin || !same) Revision++;
            topology = next; liveBuildings = buildings; origin = nextOrigin; lastTick = tick;
            return changed;
        }
        private int Index(IntVec3 cell) => topology == null || cell.x < origin.x || cell.z < origin.z
            || cell.x >= origin.x + topology.Width || cell.z >= origin.z + topology.Height ? -1
            : cell.x - origin.x + (cell.z - origin.z) * topology.Width;
        private IntVec3 Cell(int index) => origin + new IntVec3(index % topology.Width, 0, index / topology.Width);
        public List<IntVec3> Path(IntVec3 source, IntVec3 target, ISet<int> allowedRooms = null) => topology == null
            ? new List<IntVec3>() : topology.Path(Index(source), Index(target), allowedRooms).Select(Cell).ToList();
        public IEnumerable<IntVec3> NeighborTargets(IntVec3 source, ISet<int> cleared) => topology == null
            ? Enumerable.Empty<IntVec3>() : topology.NeighborTargets(Index(source), cleared).Select(Cell);
        public bool IsPortal(IntVec3 cell) => Index(cell) >= 0 && topology.Portals[Index(cell)];
        public bool Contains(IntVec3 cell) => Index(cell) >= 0;
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private bool RecoverCqbIntent(CombatOrganization organization, List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (plan.IsDefensive || state.Phase == RaidExecutionPhase.Assault || state.Phase == RaidExecutionPhase.ClearRoom
                || state.Phase == RaidExecutionPhase.SecureRoom || state.Phase == RaidExecutionPhase.Hold
                || state.Phase == RaidExecutionPhase.Complete || tick - state.LastCqbValidationTick < 30) return false;
            state.LastCqbValidationTick = tick;
            RaidStructureSnapshot structure = StructureFor(map, plan);
            List<Pawn> entry = EntryPawns(members, plan);
            if (structure == null || entry.Count == 0) return false;
            var occupied = entry.GroupBy(pawn => structure.RoomAt(pawn.Position)).OrderByDescending(group => group.Count()).First();
            if (occupied.Key <= 0) return false;
            int objectiveRoom = structure.RoomAt(plan.Objective), insideRoom = structure.RoomAt(plan.BreachInside);
            var cleared = new HashSet<int>(state.ClearedRoomCells.Select(structure.RoomAt).Where(room => room > 0));
            if (objectiveRoom != occupied.Key && (plan.PlannedBreach == null
                || insideRoom != occupied.Key && !cleared.Contains(insideRoom))) return false;
            if (state.BreachKind == RaidBreachKind.C4 || SupportEffectsPending(state, tick) || state.SupportReturnRequired) return false;
            Pawn observer = occupied.OrderBy(pawn => pawn.Position.DistanceToSquared(plan.Objective)).First();
            var local = new RaidCqbLocalMap();
            local.Refresh(map, structure, observer, observer.Position, tick, plan.AvoidedTrapCells);
            bool alreadyInside = occupied.Count() * 2 >= entry.Count && objectiveRoom == occupied.Key
                && (local.Path(observer.Position, plan.Objective).Count > 0
                    || !local.Contains(plan.Objective) && map.reachability.CanReach(observer.Position,
                        plan.Objective, PathEndMode.OnCell, TraverseParms.For(observer)));
            bool wrongBreach = plan.PlannedBreach != null && (insideRoom != occupied.Key && cleared.Contains(insideRoom)
                || insideRoom == occupied.Key && local.Path(observer.Position, plan.BreachInside).Count > 0);
            if (!alreadyInside && !wrongBreach) return false;
            if (tick - state.LastLocalReplanTick < 60) return false;
            state.LastLocalReplanTick = tick;
            RaidTacticalPlan next = RaidTacticalPlanner.MakePlan(map, organization, plan.Objective);
            if (next?.Success != true || wrongBreach && next.PlannedBreach == plan.PlannedBreach) return false;
            if (alreadyInside && next.CqbIntent != RaidCqbIntent.ClearCurrentRoom) return false;
            if (state.Breacher?.CurJobDef?.defName == CompSledgehammerBreach.JobDefName
                || state.Breacher?.CurJobDef?.defName == "HD_PowerCutterBreach")
                state.Breacher.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            next.ObjectiveIsIntermediate = plan.ObjectiveIsIntermediate;
            ActivateNextRoomPlan(organization, members, state, next, tick);
            if (next.CqbIntent == RaidCqbIntent.ClearCurrentRoom) Advance(state, RaidExecutionPhase.Assault, tick);
            MapComponent_RaidTacticalTrace.Record(observer, alreadyInside
                ? "CQB recovery: target room already occupied; clear without stacking or demolition"
                : "CQB recovery: reject breach back into a reachable/cleared room");
            return true;
        }

        private bool RefreshLocalCqb(CombatOrganization organization, List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure == null || !structure.IsIndoor(plan.Objective)) return false;
            Pawn observer = members.OrderBy(pawn => pawn.Position.DistanceToSquared(plan.Entry)).First();
            if (state.LocalCqb == null) state.LocalCqb = new RaidCqbLocalMap();
            IntVec3 center = state.Phase == RaidExecutionPhase.ClearRoom || state.Phase == RaidExecutionPhase.SecureRoom
                ? plan.Objective : plan.Entry;
            bool changed = state.LocalCqb.Refresh(map, structure, observer, center, tick, plan.AvoidedTrapCells);
            bool obstructed = plan.BreachCell.InBounds(map) && !BreachOpened(plan)
                && (plan.ReusePassage || plan.PlannedBreach != null
                    && plan.BreachCell.GetEdifice(map) != plan.PlannedBreach);
            // Reuse a newly opened interior passage only before support starts.
            // Never reset a launched grenade or an installed C4 sequence.
            if ((!changed && !obstructed) || plan.PlannedBreach == null && !obstructed
                || tick - state.LastLocalReplanTick < 60 || state.BreachKind == RaidBreachKind.C4
                || state.Phase != RaidExecutionPhase.Assemble && state.Phase != RaidExecutionPhase.Breach
                || !obstructed && (!structure.IsIndoor(observer.Position)
                    || state.LocalCqb.Path(observer.Position, plan.Objective).Count == 0)) return false;
            if ((state.SupportIssued || state.SupportLaunched)
                && (SupportEffectsPending(state, tick) || state.SupportReturnRequired)) return false;
            state.LastLocalReplanTick = tick;
            RaidTacticalPlan next = RaidTacticalPlanner.MakePlan(map, organization, plan.Objective);
            if (next?.Success != true || !obstructed && next.PlannedBreach != null
                || obstructed && plan.PlannedBreach != null && next.PlannedBreach == plan.PlannedBreach
                || obstructed && next.ReusePassage && !BreachOpened(next)) return false;
            if (state.Breacher?.CurJobDef?.defName == CompSledgehammerBreach.JobDefName
                || state.Breacher?.CurJobDef?.defName == "HD_PowerCutterBreach")
                state.Breacher.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            next.ObjectiveIsIntermediate = plan.ObjectiveIsIntermediate;
            ActivateNextRoomPlan(organization, members, state, next, tick);
            Assemble(members, next);
            MapComponent_RaidTacticalTrace.Record(observer, obstructed
                ? "Local CQB passage obstructed; replace stale entry plan"
                : "Local CQB opening changed; reuse passage to the same room");
            return true;
        }
    }
}
