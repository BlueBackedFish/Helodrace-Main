using System;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public struct TacticalCellData
    {
        public float DoorThreat;
        public float WallThreat;
        public float TotalThreat => DoorThreat + WallThreat;
    }

    // Only fixed doors and walls are cached. Raid plans sample current pawns on demand.
    public sealed class MapComponent_TacticalMapAnalysis : MapComponent
    {
        private const int StaticRefreshTicks = 3600;
        private const int ActiveRequestTicks = 1800;

        private float[] doorThreat;
        private float[] wallThreat;
        private int lastStaticRefresh = -999999;
        private int activeUntilTick = -1;

        public long LastStaticBuildMilliseconds { get; private set; }

        public MapComponent_TacticalMapAnalysis(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int ticks = Find.TickManager.TicksGame;
            if (ticks <= activeUntilTick && ticks - lastStaticRefresh >= StaticRefreshTicks)
                EnsureCurrent(false);
        }

        public void RequestAnalysis(int keepActiveTicks = ActiveRequestTicks)
        {
            activeUntilTick = Mathf.Max(activeUntilTick,
                (Find.TickManager?.TicksGame ?? 0) + Mathf.Max(1, keepActiveTicks));
            EnsureCurrent(false);
        }

        public void ForceRebuild()
        {
            AllocateGrids();
            RebuildStatic();
        }

        public TacticalCellData At(IntVec3 cell)
        {
            EnsureCurrent();
            return CachedAt(cell);
        }

        internal TacticalCellData CachedAt(IntVec3 cell)
        {
            if (!cell.InBounds(map)) return default(TacticalCellData);
            int index = map.cellIndices.CellToIndex(cell);
            return new TacticalCellData
            {
                DoorThreat = doorThreat[index],
                WallThreat = wallThreat[index]
            };
        }

        public float ThreatAt(IntVec3 cell) => At(cell).TotalThreat;

        private void EnsureCurrent(bool markActive = true)
        {
            AllocateGrids();
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (markActive)
                activeUntilTick = Mathf.Max(activeUntilTick, ticks + ActiveRequestTicks);
            if (ticks - lastStaticRefresh >= StaticRefreshTicks) RebuildStatic();
        }

        private void AllocateGrids()
        {
            int count = map.cellIndices.NumGridCells;
            if (doorThreat != null && doorThreat.Length == count) return;
            doorThreat = new float[count];
            wallThreat = new float[count];
        }

        private void RebuildStatic()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Array.Clear(doorThreat, 0, doorThreat.Length);
            Array.Clear(wallThreat, 0, wallThreat.Length);
            ScoreDoorAndWallGeometry();
            lastStaticRefresh = Find.TickManager?.TicksGame ?? 0;
            stopwatch.Stop();
            LastStaticBuildMilliseconds = stopwatch.ElapsedMilliseconds;
        }

        private void ScoreDoorAndWallGeometry()
        {
            foreach (IntVec3 cell in map.AllCells)
            {
                bool door = cell.GetEdifice(map) is Building_Door;
                if (!cell.Standable(map) && !door) continue;
                bool north = IsWall(cell + IntVec3.North);
                bool east = IsWall(cell + IntVec3.East);
                bool south = IsWall(cell + IntVec3.South);
                bool west = IsWall(cell + IntVec3.West);
                bool besideDoor = CardinalDirections.Any(direction =>
                    (cell + direction).InBounds(map)
                    && (cell + direction).GetEdifice(map) is Building_Door);
                int index = map.cellIndices.CellToIndex(cell);
                doorThreat[index] = door ? 12f : besideDoor ? 8f : 0f;
                bool corridor = north && south || east && west;
                bool corner = north && east || east && south || south && west || west && north;
                wallThreat[index] = corridor ? 6f : corner ? 4f : 0f;
            }
        }

        private bool IsWall(IntVec3 cell)
        {
            return cell.InBounds(map) && !cell.Standable(map)
                && cell.GetEdifice(map) != null
                && !(cell.GetEdifice(map) is Building_Door);
        }

        private static readonly IntVec3[] CardinalDirections =
        {
            IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West
        };
    }
}
