using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Helodrace.Squads;

namespace Helodrace
{
    internal struct RaidKnownCqbCell
    {
        public int Building;
        public bool Usable, Portal;
        public bool SameAs(RaidKnownCqbCell other) => Building == other.Building
            && Usable == other.Usable && Portal == other.Portal;
    }

    internal sealed class RaidKnownCqbRecord : IExposable
    {
        public IntVec3 Position;
        public RaidKnownCqbCell Cell;
        public int Tick, Version;
        public string Origin;
        public bool Direct;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Position, "position"); Scribe_Values.Look(ref Cell.Building, "building");
            Scribe_Values.Look(ref Cell.Usable, "usable"); Scribe_Values.Look(ref Cell.Portal, "portal");
            Scribe_Values.Look(ref Tick, "tick"); Scribe_Values.Look(ref Version, "version");
            Scribe_Values.Look(ref Origin, "origin"); Scribe_Values.Look(ref Direct, "direct");
        }
    }

    internal sealed class RaidCqbKnowledge : IExposable
    {
        private readonly Dictionary<IntVec3, RaidKnownCqbRecord> cells = new Dictionary<IntVec3, RaidKnownCqbRecord>();
        private List<RaidKnownCqbRecord> saved;
        internal int ObservedTick, Version;
        internal string Origin;
        internal System.Action<IntVec3, RaidKnownCqbCell> OnObserved;
        public RaidKnownCqbCell Read(IntVec3 cell, RaidKnownCqbCell baseline, RaidKnownCqbCell live,
            System.Func<IntVec3, bool> observed)
        {
            RaidKnownCqbCell known = cells.TryGetValue(cell, out RaidKnownCqbRecord record) ? record.Cell : baseline;
            // Inspect LOS only for an actual state difference, not every floor tile in the window.
            if (!known.SameAs(live) && observed(cell))
            {
                known = live;
                cells[cell] = new RaidKnownCqbRecord { Position = cell, Cell = known, Tick = ObservedTick,
                    Version = Version, Origin = Origin, Direct = true };
                OnObserved?.Invoke(cell, known);
            }
            return known;
        }
        public static bool ReplaceEntry(bool obstructed, bool observed) => obstructed && observed;
        internal void RememberDirect(IntVec3 position, RaidKnownCqbCell cell, int tick, int version, string origin) =>
            cells[position] = new RaidKnownCqbRecord { Position = position, Cell = cell,
                Tick = tick, Version = version, Origin = origin, Direct = true };
        internal bool Receive(RaidTacticalReport report)
        {
            if (report.Kind != RaidReportKind.Passage || cells.TryGetValue(report.Position, out RaidKnownCqbRecord known)
                && !RaidCommunicationPolicy.Newer(report.ObservedTick, false, known.Tick, known.Direct)) return false;
            cells[report.Position] = new RaidKnownCqbRecord { Position = report.Position, Tick = report.ObservedTick,
                Origin = report.OriginUnit, Version = report.StructureVersion, Direct = false,
                Cell = new RaidKnownCqbCell { Building = report.Building, Usable = report.Usable, Portal = report.IsPortal } };
            return true;
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) saved = cells.Values.ToList();
            Scribe_Collections.Look(ref saved, "observedCells", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                cells.Clear();
                foreach (RaidKnownCqbRecord record in saved ?? new List<RaidKnownCqbRecord>()) cells[record.Position] = record;
                saved = null;
            }
        }
    }

    internal sealed class RaidCqbLocalMap
    {
        public const int Radius = 16;
        public const int RefreshTicks = 30;
        private int lastTick = -RefreshTicks;
        private IntVec3 origin;
        private CqbLocalTopology topology;
        private int[] liveBuildings;
        internal RaidCqbKnowledge Knowledge { get; }
        public RaidCqbLocalMap(RaidCqbKnowledge knowledge = null) { Knowledge = knowledge ?? new RaidCqbKnowledge(); }
        public int Revision { get; private set; }
        public int CellCount => topology?.Rooms.Length ?? 0;

        public bool Refresh(Map map, RaidStructureSnapshot structure, Pawn pawn,
            IntVec3 center, int tick, ISet<IntVec3> avoided, bool force = false,
            System.Func<IntVec3, bool> observed = null)
        {
            Knowledge.ObservedTick = tick; Knowledge.Version = structure.Version.Id;
            if (observed == null) observed = cell => RaidObservationSight.CanSeeCell(map, pawn.Position, cell,
                RaidContactMemory.Radius, (a, b) => GenSight.LineOfSight(a, b, map, true)
                    && !GenSight.PointsOnLineOfSight(a, b).Any(value => RaidSmokeUtility.CoveringSmokeAt(map, value)));
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
                TacticalCellData cached = structure.CachedAt(cell);
                rooms[i] = structure.RoomAt(cell);
                bool portal = cached.WallLine || cached.ExteriorAccess || building is Building_Door
                    || building?.def.IsWall == true;
                RaidKnownCqbCell known = Knowledge.Read(cell, new RaidKnownCqbCell {
                    Building = structure.Version.Geometry.Input.Cells[map.cellIndices.CellToIndex(cell)].StructureId,
                    Portal = cached.WallLine || cached.ExteriorAccess,
                    // Layout knowledge does not reveal an unseen door's current open state.
                    Usable = cached.Standable && !cached.WallLine && (rooms[i] > 0 || cached.ExteriorAccess)
                }, new RaidKnownCqbCell {
                    Building = portal ? building?.thingIDNumber ?? 0 : 0,
                    Portal = portal,
                    Usable = cell.Walkable(map) && (rooms[i] > 0 || portal)
                        && (!(building is Building_Door door) || door.Open || door.PawnCanOpen(pawn))
                }, observed);
                buildings[i] = known.Building; portals[i] = known.Portal;
                usable[i] = known.Usable && !(avoided?.Contains(cell) == true);
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
        public HashSet<IntVec3> Reachable(IntVec3 source, ISet<int> allowedRooms)
        {
            if (topology == null) return new HashSet<IntVec3>();
            int[] distances = topology.Distances(Index(source), out _, allowedRooms);
            return new HashSet<IntVec3>(Enumerable.Range(0, distances.Length).Where(index => distances[index] >= 0).Select(Cell));
        }
        public bool IsPortal(IntVec3 cell) => Index(cell) >= 0 && topology.Portals[Index(cell)];
        public bool Contains(IntVec3 cell) => Index(cell) >= 0;
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private bool RecoverCqbIntent(RaidTacticalUnit unit, List<Pawn> members,
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
            var local = new RaidCqbLocalMap(state.CqbKnowledge);
            local.Refresh(map, structure, observer, observer.Position, tick, plan.AvoidedTrapCells,
                observed: cell => CanObserveMapCell(members, cell));
            bool alreadyInside = occupied.Count() * 2 >= entry.Count && objectiveRoom == occupied.Key
                && (local.Path(observer.Position, plan.Objective).Count > 0
                    || !local.Contains(plan.Objective) && map.reachability.CanReach(observer.Position,
                        plan.Objective, PathEndMode.OnCell, TraverseParms.For(observer)));
            bool wrongBreach = plan.PlannedBreach != null && (insideRoom != occupied.Key && cleared.Contains(insideRoom)
                || insideRoom == occupied.Key && local.Path(observer.Position, plan.BreachInside).Count > 0);
            if (!alreadyInside && !wrongBreach) return false;
            if (tick - state.LastLocalReplanTick < 60) return false;
            state.LastLocalReplanTick = tick;
            RaidTacticalPlan next = RaidTacticalPlanner.MakePlan(map, unit, plan.Objective);
            if (next?.Success != true || wrongBreach && next.PlannedBreach == plan.PlannedBreach) return false;
            if (alreadyInside && next.CqbIntent != RaidCqbIntent.ClearCurrentRoom) return false;
            if (state.Breacher?.CurJobDef?.defName == CompSledgehammerBreach.JobDefName
                || state.Breacher?.CurJobDef?.defName == "HD_PowerCutterBreach")
                state.Breacher.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            next.ObjectiveIsIntermediate = plan.ObjectiveIsIntermediate;
            next.ObjectiveIsRecheck = plan.ObjectiveIsRecheck;
            ActivateNextRoomPlan(unit, members, state, next, tick);
            if (next.CqbIntent == RaidCqbIntent.ClearCurrentRoom) Advance(state, RaidExecutionPhase.Assault, tick);
            MapComponent_RaidTacticalTrace.Record(observer, alreadyInside
                ? "CQB recovery: target room already occupied; clear without stacking or demolition"
                : "CQB recovery: reject breach back into a reachable/cleared room");
            return true;
        }

        private bool RefreshLocalCqb(RaidTacticalUnit unit, List<Pawn> members,
            RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure == null || !structure.IsIndoor(plan.Objective)) return false;
            Pawn observer = members.OrderBy(pawn => pawn.Position.DistanceToSquared(plan.Entry)).First();
            if (state.LocalCqb == null) state.LocalCqb = new RaidCqbLocalMap(state.CqbKnowledge);
            IntVec3 center = state.Phase == RaidExecutionPhase.ClearRoom || state.Phase == RaidExecutionPhase.SecureRoom
                ? plan.Objective : plan.Entry;
            state.LocalCqb.Refresh(map, structure, observer, center, tick, plan.AvoidedTrapCells,
                observed: cell => CanObserveMapCell(members, cell));
            bool obstructed = plan.BreachCell.InBounds(map) && !BreachOpened(plan)
                && (plan.ReusePassage || plan.PlannedBreach != null
                    && plan.BreachCell.GetEdifice(map) != plan.PlannedBreach);
            bool entryBlocked = plan.Entry.InBounds(map) && !plan.Entry.Walkable(map);
            // Keep the committed entrance even if another door opens. Replace only a locally
            // observed obstruction of this entrance; never reset launched support or C4.
            if (!RaidCqbKnowledge.ReplaceEntry(obstructed || entryBlocked,
                    CanObserveMapCell(members, entryBlocked ? plan.Entry : plan.BreachCell))
                || tick - state.LastLocalReplanTick < 60 || state.BreachKind == RaidBreachKind.C4
                || state.Phase != RaidExecutionPhase.Assemble && state.Phase != RaidExecutionPhase.Breach) return false;
            if ((state.SupportIssued || state.SupportLaunched)
                && (SupportEffectsPending(state, tick) || state.SupportReturnRequired)) return false;
            state.LastLocalReplanTick = tick;
            RaidTacticalPlan next = RaidTacticalPlanner.MakePlan(map, unit, plan.Objective);
            if (next?.Success != true
                || !entryBlocked && plan.PlannedBreach != null && next.PlannedBreach == plan.PlannedBreach
                || next.ReusePassage && !BreachOpened(next)) return false;
            if (state.Breacher?.CurJobDef?.defName == CompSledgehammerBreach.JobDefName
                || state.Breacher?.CurJobDef?.defName == "HD_PowerCutterBreach")
                state.Breacher.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            next.ObjectiveIsIntermediate = plan.ObjectiveIsIntermediate;
            next.ObjectiveIsRecheck = plan.ObjectiveIsRecheck;
            ActivateNextRoomPlan(unit, members, state, next, tick);
            Assemble(members, next);
            MapComponent_RaidTacticalTrace.Record(observer, "Observed CQB entrance obstruction; replace committed entry plan");
            return true;
        }
    }
}
