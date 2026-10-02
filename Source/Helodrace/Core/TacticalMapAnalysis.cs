using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace
{
    [Flags]
    public enum TacticalStructureKind : byte
    {
        None = 0,
        Door = 1,
        Opening = 2,
        Corner = 4,
        Corridor = 8,
        Junction = 16
    }

    [Flags]
    public enum TacticalOpenDirection : byte
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8
    }

    public struct TacticalCellData
    {
        public TacticalStructureKind Structures;
        public TacticalOpenDirection OpenDirections;
        public bool Standable;
        public bool WallLine;
        public Building_Door Door;
        public bool ExteriorAccess;
        public float DoorThreat;
        public float WallThreat;
        public float TotalThreat => DoorThreat + WallThreat;
    }

    // One structural snapshot per map. Pawn and door state stay outside this cache.
    public sealed class MapComponent_TacticalMapAnalysis : MapComponent
    {
        private TacticalCellData[] cells;
        private List<Building> breachStructures = new List<Building>();
        private bool built;
        private int structureSignature;

        public long LastStaticBuildMilliseconds { get; private set; }
        public IReadOnlyList<Building> CachedBreachStructures => breachStructures;

        public MapComponent_TacticalMapAnalysis(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!built) EnsureCurrent(true);
        }

        public void RequestAnalysis() => EnsureCurrent(true);

        public void ForceRebuild()
        {
            AllocateGrid();
            RebuildStatic();
            structureSignature = StructureSignature();
        }

        public TacticalCellData At(IntVec3 cell)
        {
            EnsureCurrent(false);
            return CachedAt(cell);
        }

        internal TacticalCellData CachedAt(IntVec3 cell)
        {
            return cell.InBounds(map) ? cells[map.cellIndices.CellToIndex(cell)]
                : default(TacticalCellData);
        }

        private void EnsureCurrent(bool checkChanges)
        {
            AllocateGrid();
            int signature = checkChanges || !built ? StructureSignature()
                : structureSignature;
            if (!built || signature != structureSignature)
            {
                RebuildStatic();
                structureSignature = signature;
            }
        }

        private int StructureSignature()
        {
            int result = 0;
            int count = 0;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is Pawn || !(thing is Building)
                    && thing.def.passability != Traversability.Impassable) continue;
                unchecked
                {
                    int cell = map.cellIndices.CellToIndex(thing.Position);
                    result ^= (thing.thingIDNumber * 397) ^ (cell * 7919)
                        ^ thing.def.shortHash;
                    count++;
                }
            }
            return result ^ count * 486187739;
        }

        private void AllocateGrid()
        {
            int count = map.cellIndices.NumGridCells;
            if (cells != null && cells.Length == count) return;
            cells = new TacticalCellData[count];
            built = false;
        }

        private void RebuildStatic()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Array.Clear(cells, 0, cells.Length);
            breachStructures = map.listerThings.AllThings.OfType<Building>()
                .Where(building => building.def.IsWall
                    || building is Building_Door).ToList();
            foreach (IntVec3 cell in map.AllCells)
            {
                Building edifice = cell.GetEdifice(map) as Building;
                cells[map.cellIndices.CellToIndex(cell)] = new TacticalCellData
                {
                    Standable = cell.Standable(map),
                    Door = edifice as Building_Door,
                    WallLine = edifice != null
                        && (edifice.def.IsWall || edifice is Building_Door)
                };
            }
            foreach (IntVec3 cell in map.AllCells)
            {
                TacticalCellData data = CachedAt(cell);
                bool door = data.Door != null;
                if (!data.Standable && !door) continue;

                bool northWall = IsWall(cell + IntVec3.North);
                bool eastWall = IsWall(cell + IntVec3.East);
                bool southWall = IsWall(cell + IntVec3.South);
                bool westWall = IsWall(cell + IntVec3.West);
                TacticalOpenDirection open = OpenDirections(cell);
                bool flankedNorthSouth = northWall && southWall;
                bool flankedEastWest = eastWall && westWall;
                bool passage = flankedNorthSouth || flankedEastWest;
                bool opening = !door && passage
                    && (flankedNorthSouth
                        && (open & (TacticalOpenDirection.East | TacticalOpenDirection.West))
                            == (TacticalOpenDirection.East | TacticalOpenDirection.West)
                        && !CorridorContinues(cell, IntVec3.East)
                        && !CorridorContinues(cell, IntVec3.West)
                        || flankedEastWest
                        && (open & (TacticalOpenDirection.North | TacticalOpenDirection.South))
                            == (TacticalOpenDirection.North | TacticalOpenDirection.South)
                        && !CorridorContinues(cell, IntVec3.North)
                        && !CorridorContinues(cell, IntVec3.South));
                bool corner = (northWall && eastWall || eastWall && southWall
                    || southWall && westWall || westWall && northWall)
                    && CountDirections(open) >= 2;
                bool junction = CountDirections(open) >= 3
                    && (northWall || eastWall || southWall || westWall);

                TacticalStructureKind structures = TacticalStructureKind.None;
                if (door) structures |= TacticalStructureKind.Door;
                if (opening) structures |= TacticalStructureKind.Opening;
                if (passage && !opening) structures |= TacticalStructureKind.Corridor;
                if (corner) structures |= TacticalStructureKind.Corner;
                if (junction) structures |= TacticalStructureKind.Junction;

                bool besideDoor = IsDoor(cell + IntVec3.North)
                    || IsDoor(cell + IntVec3.East)
                    || IsDoor(cell + IntVec3.South)
                    || IsDoor(cell + IntVec3.West);
                data.Structures = structures;
                data.OpenDirections = open;
                data.ExteriorAccess = (door || opening) && ConnectsExterior(cell);
                data.DoorThreat = door ? 12f : besideDoor ? 8f : 0f;
                data.WallThreat = passage ? 6f : corner ? 4f : junction ? 4f : 0f;
                cells[map.cellIndices.CellToIndex(cell)] = data;
            }
            built = true;
            stopwatch.Stop();
            LastStaticBuildMilliseconds = stopwatch.ElapsedMilliseconds;
        }

        private TacticalOpenDirection OpenDirections(IntVec3 cell)
        {
            TacticalOpenDirection result = TacticalOpenDirection.None;
            if (IsTraversable(cell + IntVec3.North)) result |= TacticalOpenDirection.North;
            if (IsTraversable(cell + IntVec3.East)) result |= TacticalOpenDirection.East;
            if (IsTraversable(cell + IntVec3.South)) result |= TacticalOpenDirection.South;
            if (IsTraversable(cell + IntVec3.West)) result |= TacticalOpenDirection.West;
            return result;
        }

        private bool CorridorContinues(IntVec3 cell, IntVec3 direction)
        {
            IntVec3 next = cell + direction;
            if (!IsTraversable(next)) return false;
            return direction == IntVec3.East || direction == IntVec3.West
                ? IsWall(next + IntVec3.North) && IsWall(next + IntVec3.South)
                : IsWall(next + IntVec3.East) && IsWall(next + IntVec3.West);
        }

        private bool ConnectsExterior(IntVec3 cell)
        {
            return DifferentSides(cell + IntVec3.North, cell + IntVec3.South)
                || DifferentSides(cell + IntVec3.East, cell + IntVec3.West);
        }

        private bool DifferentSides(IntVec3 first, IntVec3 second)
        {
            if (!IsTraversable(first) || !IsTraversable(second)) return false;
            Room firstRoom = first.GetRoom(map);
            Room secondRoom = second.GetRoom(map);
            bool firstOutside = firstRoom == null || firstRoom.PsychologicallyOutdoors;
            bool secondOutside = secondRoom == null || secondRoom.PsychologicallyOutdoors;
            if (firstOutside != secondOutside) return true;
            // An open gap can merge the two sides into one Room; roofs still
            // indicate which side of a perimeter the gap faces.
            bool firstUnroofed = map.roofGrid.RoofAt(first) == null;
            bool secondUnroofed = map.roofGrid.RoofAt(second) == null;
            return firstUnroofed != secondUnroofed;
        }

        private bool IsDoor(IntVec3 cell)
        {
            return cell.InBounds(map) && cell.GetEdifice(map) is Building_Door;
        }

        private bool IsWall(IntVec3 cell)
        {
            return cell.InBounds(map) && !cell.Standable(map)
                && cell.GetEdifice(map) != null && !IsDoor(cell);
        }

        private bool IsTraversable(IntVec3 cell)
        {
            return cell.InBounds(map) && (cell.Standable(map) || IsDoor(cell));
        }

        private static int CountDirections(TacticalOpenDirection directions)
        {
            int value = (int)directions;
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }
    }
}
