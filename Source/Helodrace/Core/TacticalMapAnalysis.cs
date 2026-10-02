using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public enum StrategicTargetKind
    {
        Bedroom, Warehouse, PowerGeneration, PowerStorage, Communications,
        Production, Research, Medical, DefensiveControl
    }

    public sealed class StrategicTarget
    {
        public StrategicTargetKind Kind { get; internal set; }
        public IntVec3 Cell { get; internal set; }
        public CellRect Bounds { get; internal set; }
        public float Score { get; internal set; }
        public Thing Source { get; internal set; }
        public int RoomId { get; internal set; }

        public override string ToString() => $"{Kind} at {Cell}: {Score:0.0}";
    }

    public struct TacticalCellData
    {
        public float StrategicValue;
        public float DoorThreat;
        public float WallThreat;
        public float TotalThreat => DoorThreat + WallThreat;
    }

    public sealed class TacticalPointOfInterest
    {
        private readonly HashSet<IntVec3> cells = new HashSet<IntVec3>();
        private readonly HashSet<StrategicTargetKind> strategicKinds = new HashSet<StrategicTargetKind>();
        private readonly HashSet<string> sourceLabels = new HashSet<string>();

        public IntVec3 Cell { get; internal set; }
        public CellRect Bounds { get; internal set; }
        public int RoomId { get; internal set; }
        public string RoomRole { get; internal set; }
        public float StrategicValue { get; internal set; }
        public IEnumerable<StrategicTargetKind> StrategicKinds => strategicKinds;
        public IEnumerable<string> SourceLabels => sourceLabels;

        public bool Contains(IntVec3 cell) => cells.Contains(cell);

        internal void AddCells(IEnumerable<IntVec3> addedCells)
        {
            foreach (IntVec3 cell in addedCells) cells.Add(cell);
        }

        internal void AddStrategicTarget(StrategicTarget target)
        {
            StrategicValue += target.Score;
            strategicKinds.Add(target.Kind);
            if (target.Source != null) sourceLabels.Add(target.Source.LabelCap.ToString());
        }
    }

    // Only fixed doors and walls are cached. A raid plan samples hostile pawns on demand.
    public sealed class MapComponent_TacticalMapAnalysis : MapComponent
    {
        private const int StaticRefreshTicks = 3600;
        private const int ActiveRequestTicks = 1800;
        private const float MaximumCellScore = 1000f;

        private float[] strategicValue;
        private float[] doorThreat;
        private float[] wallThreat;
        private int lastStaticRefresh = -999999;
        private int activeUntilTick = -1;
        private readonly List<StrategicTarget> strategicTargets = new List<StrategicTarget>();
        private readonly List<TacticalPointOfInterest> pointsOfInterest = new List<TacticalPointOfInterest>();

        public long LastStaticBuildMilliseconds { get; private set; }
        public bool IsActivelyRequested => (Find.TickManager?.TicksGame ?? 0) <= activeUntilTick;

        public MapComponent_TacticalMapAnalysis(Map map) : base(map) { }

        public IReadOnlyList<StrategicTarget> StrategicTargets
        {
            get { EnsureCurrent(); return strategicTargets; }
        }

        public IReadOnlyList<TacticalPointOfInterest> PointsOfInterest
        {
            get { EnsureCurrent(); return pointsOfInterest; }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int ticks = Find.TickManager.TicksGame;
            if ((ticks <= activeUntilTick || TacticalPoiOverlaySettings.Enabled)
                && ticks - lastStaticRefresh >= StaticRefreshTicks)
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
                StrategicValue = strategicValue[index],
                DoorThreat = doorThreat[index],
                WallThreat = wallThreat[index]
            };
        }

        public float StrategicValueAt(IntVec3 cell) => At(cell).StrategicValue;
        public float ThreatAt(IntVec3 cell) => At(cell).TotalThreat;

        public StrategicTarget BestObjective(IntVec3 from)
        {
            EnsureCurrent();
            return strategicTargets
                .Where(target => target.Cell.IsValid && target.Cell.InBounds(map))
                .OrderByDescending(target => target.Score
                    - MeanThreat(target.Cell, 3) * 0.7f
                    - (from.IsValid ? from.DistanceTo(target.Cell) * 0.35f : 0f))
                .FirstOrDefault();
        }

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
            if (strategicValue != null && strategicValue.Length == count) return;
            strategicValue = new float[count];
            doorThreat = new float[count];
            wallThreat = new float[count];
        }

        private void RebuildStatic()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Array.Clear(strategicValue, 0, strategicValue.Length);
            Array.Clear(doorThreat, 0, doorThreat.Length);
            Array.Clear(wallThreat, 0, wallThreat.Length);
            strategicTargets.Clear();

            foreach (Room room in CollectRooms()) ScoreStrategicRoom(room);
            ScoreStockpiles();
            ScoreStrategicBuildings();
            ScoreDoorAndWallGeometry();
            RebuildPointsOfInterest();
            lastStaticRefresh = Find.TickManager?.TicksGame ?? 0;
            stopwatch.Stop();
            LastStaticBuildMilliseconds = stopwatch.ElapsedMilliseconds;
        }
        private HashSet<Room> CollectRooms()
        {
            HashSet<Room> rooms = new HashSet<Room>();
            foreach (IntVec3 cell in map.AllCells)
            {
                Room room = cell.GetRoom(map);
                if (room != null && room.ID >= 0)
                {
                    rooms.Add(room);
                }
            }

            return rooms;
        }

        private void ScoreStrategicRoom(Room room)
        {
            string role = room.Role?.defName ?? string.Empty;
            StrategicTargetKind? kind = null;
            float score = 0f;
            if (role.IndexOf("Bedroom", StringComparison.OrdinalIgnoreCase) >= 0
                || role.IndexOf("Barracks", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = StrategicTargetKind.Bedroom;
                score = role.IndexOf("Barracks", StringComparison.OrdinalIgnoreCase) >= 0 ? 48f : 36f;
            }
            else if (role.IndexOf("Hospital", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = StrategicTargetKind.Medical;
                score = 88f;
            }
            else if (role.IndexOf("Laboratory", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = StrategicTargetKind.Research;
                score = 76f;
            }

            if (!kind.HasValue)
            {
                return;
            }

            List<IntVec3> cells = room.Cells.ToList();
            AddRoomTarget(kind.Value, room, cells, score + Mathf.Min(30f, cells.Count * 0.25f));
        }

        private void ScoreStockpiles()
        {
            foreach (Zone_Stockpile zone in map.zoneManager.AllZones.OfType<Zone_Stockpile>())
            {
                List<IGrouping<int, IntVec3>> roomSections = zone.Cells
                    .Where(cell => cell.InBounds(map))
                    .GroupBy(IndoorRoomId)
                    .ToList();
                foreach (IGrouping<int, IntVec3> section in roomSections)
                {
                    List<IntVec3> cells = section.ToList();
                    if (cells.Count == 0) continue;

                    List<Thing> supplies = cells
                        .SelectMany(cell => cell.GetThingList(map))
                        .Where(thing => thing.def.category == ThingCategory.Item)
                        .ToList();
                    int occupiedCells = cells.Count(cell => cell.GetThingList(map)
                        .Any(thing => thing.def.category == ThingCategory.Item));
                    float supplyImportance = supplies.Sum(StrategicSupplyImportance);
                    float score = 52f + Mathf.Min(65f,
                        cells.Count * 0.25f + occupiedCells * 1.1f + supplyImportance);
                    AddTarget(StrategicTargetKind.Warehouse, cells, score, null, section.Key);
                }
            }
        }

        private void ScoreStrategicBuildings()
        {
            foreach (Building building in map.listerThings.AllThings.OfType<Building>())
            {
                if (building.Destroyed || building.def == null || building.Faction == null)
                {
                    continue;
                }

                StrategicTargetKind? kind = null;
                float score = 0f;
                CompPowerTrader power = building.TryGetComp<CompPowerTrader>();
                string name = building.def.defName ?? string.Empty;
                if (building.TryGetComp<CompPowerBattery>() != null)
                {
                    kind = StrategicTargetKind.PowerStorage;
                    score = 82f;
                }
                else if (building.TryGetComp<CompPowerPlant>() != null || (power != null && power.PowerOutput > 0f))
                {
                    kind = StrategicTargetKind.PowerGeneration;
                    score = 92f + Mathf.Min(65f, Mathf.Abs(power.PowerOutput) / 80f);
                }
                else if (name.IndexOf("Comms", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Console", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    kind = StrategicTargetKind.Communications;
                    score = 74f;
                }
                else if (building is Building_TurretGun)
                {
                    kind = StrategicTargetKind.DefensiveControl;
                    score = 62f;
                }
                else if (name.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    kind = StrategicTargetKind.Research;
                    score = 68f;
                }
                else if (building is Building_WorkTable)
                {
                    kind = StrategicTargetKind.Production;
                    score = 44f;
                }

                if (!kind.HasValue)
                {
                    continue;
                }

                List<IntVec3> occupied = building.OccupiedRect().Cells.Where(cell => cell.InBounds(map)).ToList();
                AddTarget(kind.Value, occupied, score, building, IndoorRoomId(building.Position));
            }
        }

        private void AddRoomTarget(StrategicTargetKind kind, Room room, List<IntVec3> cells, float score)
        {
            AddTarget(kind, cells, score, null, room.ID);
        }

        private static float StrategicSupplyImportance(Thing thing)
        {
            string name = thing.def.defName ?? string.Empty;
            float stackFactor = Mathf.Clamp(thing.stackCount / 20f, 0.25f, 2f);
            if (thing.def.IsWeapon) return 2.5f;
            if (name.IndexOf("Medicine", StringComparison.OrdinalIgnoreCase) >= 0) return 3f * stackFactor;
            if (name.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0) return 2.2f * stackFactor;
            if (name.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0) return 1.8f * stackFactor;
            if (thing.def.IsNutritionGivingIngestible) return 1.2f * stackFactor;
            return 0.15f * stackFactor;
        }

        private void AddTarget(StrategicTargetKind kind, List<IntVec3> cells, float score, Thing source, int roomId)
        {
            if (cells.NullOrEmpty())
            {
                return;
            }

            IntVec3 center = ClosestCellToAverage(cells);
            float perCell = score / Mathf.Sqrt(cells.Count);
            foreach (IntVec3 cell in cells)
            {
                int index = map.cellIndices.CellToIndex(cell);
                strategicValue[index] = Mathf.Min(MaximumCellScore, strategicValue[index] + perCell);
            }

            // A soft halo makes objectives useful to landing and path planners without
            // pretending that adjacent ground is itself infrastructure.
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 6f, true))
            {
                if (!cell.InBounds(map)) continue;
                float falloff = 1f - cell.DistanceTo(center) / 7f;
                int index = map.cellIndices.CellToIndex(cell);
                strategicValue[index] = Mathf.Min(MaximumCellScore, strategicValue[index] + score * 0.12f * falloff);
            }

            strategicTargets.Add(new StrategicTarget
            {
                Kind = kind,
                Cell = center,
                Bounds = BoundsFor(cells),
                Score = score,
                Source = source,
                RoomId = roomId
            });
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

        private float MeanThreat(IntVec3 center, int radius)
        {
            float total = 0f;
            int count = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map)) continue;
                int index = map.cellIndices.CellToIndex(cell);
                total += doorThreat[index] + wallThreat[index];
                count++;
            }
            return count == 0 ? 0f : total / count;
        }

        private void RebuildPointsOfInterest()
        {
            pointsOfInterest.Clear();
            var roomPois = new Dictionary<int, TacticalPointOfInterest>();
            foreach (StrategicTarget target in strategicTargets)
            {
                TacticalPointOfInterest poi = target.RoomId >= 0
                    ? GetOrCreateRoomPoi(roomPois, target.RoomId, target.Cell, target.Bounds)
                    : CreateSitePoi(target.Cell, target.Bounds);
                poi.AddStrategicTarget(target);
                if (target.RoomId < 0) pointsOfInterest.Add(poi);
            }
            pointsOfInterest.AddRange(roomPois.Values);
            pointsOfInterest.Sort((left, right) =>
                right.StrategicValue.CompareTo(left.StrategicValue));
        }

        private TacticalPointOfInterest GetOrCreateRoomPoi(
            Dictionary<int, TacticalPointOfInterest> roomPois,
            int roomId,
            IntVec3 fallbackCell,
            CellRect fallbackBounds)
        {
            if (roomPois.TryGetValue(roomId, out TacticalPointOfInterest existing))
            {
                return existing;
            }

            Room room = fallbackCell.GetRoom(map);
            List<IntVec3> cells = room != null && room.ID == roomId
                ? room.Cells.ToList()
                : fallbackBounds.Cells.Where(cell => cell.InBounds(map)).ToList();
            TacticalPointOfInterest created = new TacticalPointOfInterest
            {
                Cell = cells.Count > 0 ? ClosestCellToAverage(cells) : fallbackCell,
                Bounds = cells.Count > 0 ? BoundsFor(cells) : fallbackBounds,
                RoomId = roomId,
                RoomRole = room?.Role?.LabelCap.ToString() ?? "Room"
            };
            created.AddCells(cells);
            roomPois.Add(roomId, created);
            return created;
        }

        private TacticalPointOfInterest CreateSitePoi(
            IntVec3 center,
            CellRect bounds,
            IEnumerable<IntVec3> exactCells = null)
        {
            TacticalPointOfInterest poi = new TacticalPointOfInterest
            {
                Cell = center,
                Bounds = bounds,
                RoomId = -1,
                RoomRole = "Exterior site"
            };
            poi.AddCells(exactCells ?? bounds.Cells.Where(cell => cell.InBounds(map)));
            return poi;
        }

        private int IndoorRoomId(IntVec3 cell)
        {
            Room room = cell.GetRoom(map);
            return room != null && !room.PsychologicallyOutdoors ? room.ID : -1;
        }

        private static readonly IntVec3[] CardinalDirections =
        {
            IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West
        };

        private static CellRect BoundsFor(List<IntVec3> cells)
        {
            int minX = cells.Min(cell => cell.x);
            int maxX = cells.Max(cell => cell.x);
            int minZ = cells.Min(cell => cell.z);
            int maxZ = cells.Max(cell => cell.z);
            return CellRect.FromLimits(minX, minZ, maxX, maxZ);
        }

        private static IntVec3 ClosestCellToAverage(List<IntVec3> cells)
        {
            float x = (float)cells.Average(cell => cell.x);
            float z = (float)cells.Average(cell => cell.z);
            return cells.OrderBy(cell => (cell.x - x) * (cell.x - x) + (cell.z - z) * (cell.z - z)).First();
        }
    }
}
