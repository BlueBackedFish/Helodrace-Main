using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [Flags]
    public enum KillzoneKind
    {
        None = 0,
        Temperature = 1,
        Barrel = 2,
        TShaped = 4,
        Diagonal = 8,
        Melee = 16,
        Shooting = 32
    }

    public enum StrategicTargetKind
    {
        Bedroom,
        Warehouse,
        PowerGeneration,
        PowerStorage,
        Communications,
        Production,
        Research,
        Medical,
        DefensiveControl
    }

    public enum AssaultRecommendation
    {
        Advance,
        Breach,
        Avoid,
        AirInsert
    }

    [Flags]
    public enum TacticalPoiKind
    {
        None = 0,
        Strategic = 1,
        Killzone = 2
    }

    /// <summary>
    /// A target's score describes the military effect of disabling or securing it.
    /// It deliberately never reads Thing.MarketValue.
    /// </summary>
    public sealed class StrategicTarget
    {
        public StrategicTargetKind Kind { get; internal set; }
        public IntVec3 Cell { get; internal set; }
        public CellRect Bounds { get; internal set; }
        public float Score { get; internal set; }
        public Thing Source { get; internal set; }
        public int RoomId { get; internal set; }

        public override string ToString()
        {
            return $"{Kind} at {Cell}: {Score:0.0}";
        }
    }

    public sealed class KillzoneRecord
    {
        private HashSet<int> cellIndices;

        public KillzoneKind Kinds { get; internal set; }
        public IntVec3 Center { get; internal set; }
        public CellRect Bounds { get; internal set; }
        public float Confidence { get; internal set; }
        public float ExpectedThreat { get; internal set; }
        public Vector2 DominantThreatDirection { get; internal set; }
        public float Directionality { get; internal set; }
        public int RoomId { get; internal set; }

        public bool Has(KillzoneKind kind) => (Kinds & kind) != 0;

        public bool Contains(Map map, IntVec3 cell)
        {
            return map != null && cell.InBounds(map)
                && cellIndices != null
                && cellIndices.Contains(map.cellIndices.CellToIndex(cell));
        }

        internal void SetCells(Map map, IEnumerable<IntVec3> cells)
        {
            cellIndices = new HashSet<int>(cells.Select(map.cellIndices.CellToIndex));
        }

        internal IEnumerable<IntVec3> Cells(Map map)
        {
            return cellIndices?.Select(map.cellIndices.IndexToCell) ?? Enumerable.Empty<IntVec3>();
        }

        public override string ToString()
        {
            return $"{Kinds} at {Center}: confidence {Confidence:P0}, threat {ExpectedThreat:0.0}";
        }
    }

    public struct TacticalCellData
    {
        public float StrategicValue;
        public float RangedThreat;
        public float MeleeThreat;
        public float ThermalThreat;
        public float TrapThreat;
        public float ChokeThreat;
        public Vector2 RangedThreatDirection;
        public float RangedThreatDirectionality;

        public float TotalThreat => RangedThreat + MeleeThreat + ThermalThreat + TrapThreat + ChokeThreat;
    }

    public sealed class TacticalApproachAssessment
    {
        public AssaultRecommendation Recommendation { get; internal set; }
        public float RouteThreat { get; internal set; }
        public float BreachCost { get; internal set; }
        public float ObjectiveValue { get; internal set; }
        public KillzoneKind Killzones { get; internal set; }
    }

    /// <summary>
    /// Human-readable aggregation of every tactical judgment attached to one room
    /// or exterior site. It is intended for both AI inspection and developer UI.
    /// </summary>
    public sealed class TacticalPointOfInterest
    {
        private readonly HashSet<IntVec3> cells = new HashSet<IntVec3>();
        private readonly HashSet<StrategicTargetKind> strategicKinds = new HashSet<StrategicTargetKind>();
        private readonly HashSet<string> sourceLabels = new HashSet<string>();

        public TacticalPoiKind Kind { get; internal set; }
        public IntVec3 Cell { get; internal set; }
        public CellRect Bounds { get; internal set; }
        public int RoomId { get; internal set; }
        public string RoomRole { get; internal set; }
        public float StrategicValue { get; internal set; }
        public KillzoneKind Killzones { get; internal set; }
        public float KillzoneConfidence { get; internal set; }
        public float ExpectedThreat { get; internal set; }
        public Vector2 DominantThreatDirection { get; internal set; }
        public float Directionality { get; internal set; }
        public IEnumerable<StrategicTargetKind> StrategicKinds => strategicKinds;
        public IEnumerable<string> SourceLabels => sourceLabels;

        public bool Contains(IntVec3 cell) => cells.Contains(cell);

        internal void AddCells(IEnumerable<IntVec3> addedCells)
        {
            foreach (IntVec3 cell in addedCells)
            {
                cells.Add(cell);
            }
        }

        internal void AddStrategicTarget(StrategicTarget target)
        {
            Kind |= TacticalPoiKind.Strategic;
            StrategicValue += target.Score;
            strategicKinds.Add(target.Kind);
            if (target.Source != null)
            {
                sourceLabels.Add(target.Source.LabelCap.ToString());
            }
        }

        internal void AddKillzone(KillzoneRecord killzone)
        {
            Kind |= TacticalPoiKind.Killzone;
            Killzones |= killzone.Kinds;
            KillzoneConfidence = Mathf.Max(KillzoneConfidence, killzone.Confidence);
            ExpectedThreat = Mathf.Max(ExpectedThreat, killzone.ExpectedThreat);
            if (killzone.Directionality >= Directionality)
            {
                DominantThreatDirection = killzone.DominantThreatDirection;
                Directionality = killzone.Directionality;
            }
        }
    }

    /// <summary>
    /// Settlement-wide strategic value and threat grids. Static infrastructure is
    /// refreshed slowly; mobile defenders and live temperature are refreshed often.
    /// Future raid lords should consume this component instead of rescanning the map.
    /// </summary>
    public sealed class MapComponent_TacticalMapAnalysis : MapComponent
    {
        private const int StaticRefreshTicks = 3600;
        private const int DynamicRefreshTicks = 900;
        private const int ActiveRequestTicks = 1800;
        private const int MaximumMobileDefenders = 24;
        private const float MaximumCellScore = 1000f;

        private float[] strategicValue;
        private float[] rangedThreat;
        private float[] meleeThreat;
        private float[] thermalThreat;
        private float[] trapThreat;
        private float[] chokeThreat;
        private float[] rangedDirectionX;
        private float[] rangedDirectionZ;
        private int lastStaticRefresh = -999999;
        private int lastDynamicRefresh = -999999;
        private int activeUntilTick = -1;
        private Faction threatPerspective;
        private readonly List<Room> cachedRooms = new List<Room>();
        private readonly Dictionary<int, List<RangedProjectionCell>> turretProjections =
            new Dictionary<int, List<RangedProjectionCell>>();
        private readonly List<StrategicTarget> strategicTargets = new List<StrategicTarget>();
        private readonly List<KillzoneRecord> killzones = new List<KillzoneRecord>();
        private readonly List<TacticalPointOfInterest> pointsOfInterest = new List<TacticalPointOfInterest>();

        public long LastStaticBuildMilliseconds { get; private set; }
        public long LastDynamicBuildMilliseconds { get; private set; }
        public int LastLineOfSightChecks { get; private set; }
        public int LastMobileDefendersScored { get; private set; }
        public bool IsActivelyRequested => (Find.TickManager?.TicksGame ?? 0) <= activeUntilTick;

        public MapComponent_TacticalMapAnalysis(Map map) : base(map)
        {
        }

        public IReadOnlyList<StrategicTarget> StrategicTargets
        {
            get
            {
                EnsureCurrent(threatPerspective);
                return strategicTargets;
            }
        }

        public IReadOnlyList<KillzoneRecord> Killzones
        {
            get
            {
                EnsureCurrent(threatPerspective);
                return killzones;
            }
        }

        public IReadOnlyList<TacticalPointOfInterest> PointsOfInterest
        {
            get
            {
                EnsureCurrent(threatPerspective);
                return pointsOfInterest;
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int ticks = Find.TickManager.TicksGame;
            if (ticks > activeUntilTick && !TacticalPoiOverlaySettings.Enabled)
            {
                return;
            }
            if (ticks - lastStaticRefresh >= StaticRefreshTicks
                || ticks - lastDynamicRefresh >= DynamicRefreshTicks)
            {
                EnsureCurrent(threatPerspective, false);
            }
        }

        public void RequestAnalysis(int keepActiveTicks = ActiveRequestTicks)
        {
            RequestAnalysis(threatPerspective, keepActiveTicks);
        }

        public void RequestAnalysis(Faction attacker, int keepActiveTicks = ActiveRequestTicks)
        {
            int ticks = Find.TickManager?.TicksGame ?? 0;
            activeUntilTick = Mathf.Max(activeUntilTick, ticks + Mathf.Max(1, keepActiveTicks));
            EnsureCurrent(attacker, false);
        }

        public void ForceRebuild(Faction attacker = null)
        {
            AllocateGrids();
            RebuildStatic();
            RebuildDynamic(attacker);
        }

        public TacticalCellData At(IntVec3 cell, Faction attacker = null)
        {
            EnsureCurrent(attacker);
            return CachedAt(cell);
        }

        internal TacticalCellData CachedAt(IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return default(TacticalCellData);
            }

            int index = map.cellIndices.CellToIndex(cell);
            return new TacticalCellData
            {
                StrategicValue = strategicValue[index],
                RangedThreat = rangedThreat[index],
                MeleeThreat = meleeThreat[index],
                ThermalThreat = thermalThreat[index],
                TrapThreat = trapThreat[index],
                ChokeThreat = chokeThreat[index],
                RangedThreatDirection = DirectionAt(index),
                RangedThreatDirectionality = DirectionalityAt(index)
            };
        }

        public float StrategicValueAt(IntVec3 cell)
        {
            return At(cell).StrategicValue;
        }

        public float ThreatAt(IntVec3 cell, Faction attacker = null)
        {
            return At(cell, attacker).TotalThreat;
        }

        internal float CachedThreatAt(IntVec3 cell)
        {
            return CachedAt(cell).TotalThreat;
        }

        public StrategicTarget BestObjective(Faction attacker = null, IntVec3 from = default(IntVec3))
        {
            EnsureCurrent(attacker);
            return strategicTargets
                .Where(target => target.Cell.IsValid && target.Cell.InBounds(map))
                .OrderByDescending(target => ObjectiveUtility(target, from, attacker))
                .FirstOrDefault();
        }

        public bool TryFindAirInsertionCell(
            Faction attacker,
            IntVec3 objective,
            int minimumRadius,
            int maximumRadius,
            Predicate<IntVec3> validator,
            out IntVec3 result)
        {
            EnsureCurrent(attacker);
            result = IntVec3.Invalid;
            float bestScore = float.MinValue;
            IntVec3 center = objective.IsValid ? objective : map.Center;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, maximumRadius, true))
            {
                float distance = cell.DistanceTo(center);
                if (distance < minimumRadius || !cell.InBounds(map) || !cell.Standable(map)
                    || cell.Fogged(map) || (validator != null && !validator(cell)))
                {
                    continue;
                }

                TacticalCellData data = CachedAt(cell);
                float localThreat = MeanThreat(cell, 4, attacker);
                float score = data.StrategicValue * 0.15f - localThreat * 1.8f - distance * 0.35f;
                if (score > bestScore)
                {
                    bestScore = score;
                    result = cell;
                }
            }

            return result.IsValid;
        }

        public TacticalApproachAssessment AssessApproach(
            IEnumerable<IntVec3> route,
            IntVec3 objective,
            float breachCost,
            Faction attacker = null)
        {
            EnsureCurrent(attacker);
            List<IntVec3> cells = route?.Where(cell => cell.InBounds(map)).ToList()
                ?? new List<IntVec3>();
            float routeThreat = cells.Count == 0 ? float.MaxValue : cells.Average(CachedThreatAt);
            KillzoneKind encountered = KillzoneKind.None;
            foreach (KillzoneRecord zone in killzones)
            {
                if (cells.Any(cell => zone.Contains(map, cell)))
                {
                    encountered |= zone.Kinds;
                    routeThreat += zone.Confidence * 35f;
                }
            }

            float objectiveValue = objective.InBounds(map) ? StrategicValueAt(objective) : 0f;
            AssaultRecommendation recommendation;
            if ((encountered & (KillzoneKind.Temperature | KillzoneKind.Melee)) != 0
                || routeThreat > breachCost * 1.15f)
            {
                recommendation = breachCost < 180f ? AssaultRecommendation.Breach : AssaultRecommendation.Avoid;
            }
            else if (objectiveValue >= 120f && routeThreat >= 70f)
            {
                recommendation = AssaultRecommendation.AirInsert;
            }
            else
            {
                recommendation = AssaultRecommendation.Advance;
            }

            return new TacticalApproachAssessment
            {
                Recommendation = recommendation,
                RouteThreat = routeThreat,
                BreachCost = breachCost,
                ObjectiveValue = objectiveValue,
                Killzones = encountered
            };
        }

        private void EnsureCurrent(Faction attacker, bool markActive = true)
        {
            AllocateGrids();
            int ticks = Find.TickManager?.TicksGame ?? 0;
            if (markActive)
            {
                activeUntilTick = Mathf.Max(activeUntilTick, ticks + ActiveRequestTicks);
            }
            if (ticks - lastStaticRefresh >= StaticRefreshTicks)
            {
                RebuildStatic();
            }

            if (ticks - lastDynamicRefresh >= DynamicRefreshTicks || attacker != threatPerspective)
            {
                RebuildDynamic(attacker);
            }
        }

        private void AllocateGrids()
        {
            int count = map.cellIndices.NumGridCells;
            if (strategicValue != null && strategicValue.Length == count)
            {
                return;
            }

            strategicValue = new float[count];
            rangedThreat = new float[count];
            meleeThreat = new float[count];
            thermalThreat = new float[count];
            trapThreat = new float[count];
            chokeThreat = new float[count];
            rangedDirectionX = new float[count];
            rangedDirectionZ = new float[count];
        }

        private void RebuildStatic()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Array.Clear(strategicValue, 0, strategicValue.Length);
            Array.Clear(trapThreat, 0, trapThreat.Length);
            Array.Clear(chokeThreat, 0, chokeThreat.Length);
            strategicTargets.Clear();
            turretProjections.Clear();

            HashSet<Room> rooms = CollectRooms();
            cachedRooms.Clear();
            cachedRooms.AddRange(rooms);
            foreach (Room room in rooms)
            {
                ScoreStrategicRoom(room);
            }

            ScoreStockpiles();
            ScoreStrategicBuildings();
            ScoreTrapsAndTopology();
            lastStaticRefresh = Find.TickManager?.TicksGame ?? 0;
            stopwatch.Stop();
            LastStaticBuildMilliseconds = stopwatch.ElapsedMilliseconds;
        }

        private void RebuildDynamic(Faction attacker)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            AllocateGrids();
            Array.Clear(rangedThreat, 0, rangedThreat.Length);
            Array.Clear(meleeThreat, 0, meleeThreat.Length);
            Array.Clear(thermalThreat, 0, thermalThreat.Length);
            Array.Clear(trapThreat, 0, trapThreat.Length);
            Array.Clear(rangedDirectionX, 0, rangedDirectionX.Length);
            Array.Clear(rangedDirectionZ, 0, rangedDirectionZ.Length);
            threatPerspective = attacker;
            LastLineOfSightChecks = 0;

            ScoreTemperature();
            ScoreTraps(attacker);
            foreach (Building_TurretGun turret in map.listerThings.AllThings.OfType<Building_TurretGun>())
            {
                if (IsThreatSource(turret, attacker) && turret.AttackVerb != null)
                {
                    float powerFactor = turret.TryGetComp<CompPowerTrader>()?.PowerOn == false ? 0.2f : 1f;
                    AddTurretThreat(turret, 22f * powerFactor);
                }
            }

            List<Pawn> defenders = map.mapPawns.AllPawnsSpawned
                .Where(pawn => !pawn.Dead && !pawn.Downed && IsThreatSource(pawn, attacker))
                .OrderByDescending(DefenderPriority)
                .Take(MaximumMobileDefenders)
                .ToList();
            LastMobileDefendersScored = defenders.Count;
            foreach (Pawn pawn in defenders)
            {
                VerbProperties verb = pawn.equipment?.Primary?.def?.Verbs?.FirstOrDefault(candidate => candidate.isPrimary);
                if (verb != null && verb.range > 2f)
                {
                    int shootingSkill = pawn.skills?.GetSkill(SkillDefOf.Shooting).Level ?? 0;
                    AddRangedThreat(pawn.Position, verb.range, 8f + shootingSkill * 0.7f, verb.minRange);
                }

                AddRadial(meleeThreat, pawn.Position, 2.9f, 9f + (pawn.skills?.GetSkill(SkillDefOf.Melee).Level ?? 0) * 0.5f, false);
            }

            DetectKillzones();
            RebuildPointsOfInterest();
            lastDynamicRefresh = Find.TickManager?.TicksGame ?? 0;
            stopwatch.Stop();
            LastDynamicBuildMilliseconds = stopwatch.ElapsedMilliseconds;
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

        private void ScoreTrapsAndTopology()
        {
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!cell.Standable(map)) continue;
                bool northSouth = Standable(cell + IntVec3.North) && Standable(cell + IntVec3.South);
                bool eastWest = Standable(cell + IntVec3.East) && Standable(cell + IntVec3.West);
                int open = CardinalDirections.Count(direction => Standable(cell + direction));
                if (open <= 2 && (northSouth || eastWest))
                {
                    chokeThreat[map.cellIndices.CellToIndex(cell)] = 5f;
                }
            }
        }

        private void ScoreTraps(Faction attacker)
        {
            foreach (Building_Trap trap in map.listerThings.AllThings.OfType<Building_Trap>())
            {
                if (trap.Faction == null || IsThreatSource(trap, attacker))
                {
                    AddRadial(trapThreat, trap.Position, 1.5f, 38f, false);
                }
            }
        }

        private void ScoreTemperature()
        {
            foreach (Room room in cachedRooms)
            {
                if (room == null) continue;
                float temperature = room.Temperature;
                float threat = temperature > 45f ? (temperature - 45f) * 0.7f
                    : temperature < -35f ? (-35f - temperature) * 0.45f : 0f;
                if (threat <= 0f) continue;
                foreach (IntVec3 cell in room.Cells)
                {
                    thermalThreat[map.cellIndices.CellToIndex(cell)] = Mathf.Min(180f, threat);
                }
            }

            ThingDef fireDef = ThingDefOf.Fire;
            foreach (Thing fire in map.listerThings.ThingsOfDef(fireDef))
            {
                Room room = fire.Position.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors && room.CellCount <= 900)
                {
                    foreach (IntVec3 cell in room.Cells)
                    {
                        int index = map.cellIndices.CellToIndex(cell);
                        thermalThreat[index] = Mathf.Min(180f, thermalThreat[index] + 55f);
                    }
                }
                else
                {
                    AddRadial(thermalThreat, fire.Position, 3f, 35f, false);
                }
            }
        }

        private void AddTurretThreat(Building_TurretGun turret, float strength)
        {
            if (!turretProjections.TryGetValue(turret.thingIDNumber, out List<RangedProjectionCell> projection))
            {
                projection = BuildRangedProjection(
                    turret.Position,
                    turret.AttackVerb.verbProps.range,
                    turret.AttackVerb.verbProps.minRange);
                turretProjections[turret.thingIDNumber] = projection;
            }

            foreach (RangedProjectionCell projected in projection)
            {
                AddRangedContribution(
                    projected.Index,
                    strength * projected.Factor,
                    projected.DirectionX,
                    projected.DirectionZ);
            }
        }

        private List<RangedProjectionCell> BuildRangedProjection(IntVec3 source, float range, float minimumRange)
        {
            List<RangedProjectionCell> result = new List<RangedProjectionCell>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(source, range, true))
            {
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                float distance = source.DistanceTo(cell);
                if (distance < minimumRange) continue;
                LastLineOfSightChecks++;
                if (!GenSight.LineOfSight(source, cell, map, true)) continue;
                result.Add(new RangedProjectionCell
                {
                    Index = map.cellIndices.CellToIndex(cell),
                    Factor = Mathf.Lerp(1f, 0.42f, distance / Mathf.Max(1f, range)),
                    DirectionX = (source.x - cell.x) / Mathf.Max(0.001f, distance),
                    DirectionZ = (source.z - cell.z) / Mathf.Max(0.001f, distance)
                });
            }
            return result;
        }

        private void AddRangedThreat(IntVec3 source, float range, float strength, float minimumRange)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(source, range, true))
            {
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                float distance = source.DistanceTo(cell);
                if (distance < minimumRange) continue;
                LastLineOfSightChecks++;
                if (!GenSight.LineOfSight(source, cell, map, true)) continue;
                float falloff = Mathf.Lerp(1f, 0.42f, distance / Mathf.Max(1f, range));
                int index = map.cellIndices.CellToIndex(cell);
                float contribution = strength * falloff;
                AddRangedContribution(
                    index,
                    contribution,
                    (source.x - cell.x) / Mathf.Max(0.001f, distance),
                    (source.z - cell.z) / Mathf.Max(0.001f, distance));
            }
        }

        private void AddRangedContribution(int index, float contribution, float directionX, float directionZ)
        {
            rangedThreat[index] = Mathf.Min(MaximumCellScore, rangedThreat[index] + contribution);
            rangedDirectionX[index] += directionX * contribution;
            rangedDirectionZ[index] += directionZ * contribution;
        }

        private Vector2 DirectionAt(int index)
        {
            Vector2 vector = new Vector2(rangedDirectionX[index], rangedDirectionZ[index]);
            return vector.sqrMagnitude > 0.0001f ? vector.normalized : Vector2.zero;
        }

        private float DirectionalityAt(int index)
        {
            if (rangedThreat[index] <= 0.001f) return 0f;
            float magnitude = Mathf.Sqrt(
                rangedDirectionX[index] * rangedDirectionX[index]
                + rangedDirectionZ[index] * rangedDirectionZ[index]);
            return Mathf.Clamp01(magnitude / rangedThreat[index]);
        }

        private void AddRadial(float[] grid, IntVec3 center, float radius, float strength, bool requireLineOfSight)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || (requireLineOfSight && !GenSight.LineOfSight(center, cell, map, true))) continue;
                float falloff = 1f - cell.DistanceTo(center) / (radius + 1f);
                int index = map.cellIndices.CellToIndex(cell);
                grid[index] = Mathf.Min(MaximumCellScore, grid[index] + strength * falloff);
            }
        }

        private void DetectKillzones()
        {
            killzones.Clear();
            foreach (Room room in cachedRooms)
            {
                List<IntVec3> cells = room.Cells.Where(cell => cell.Standable(map)).ToList();
                if (cells.Count < 5 || cells.Count > 900) continue;
                CellRect bounds = BoundsFor(cells);
                float aspect = Mathf.Max(bounds.Width, bounds.Height) / (float)Mathf.Max(1, Mathf.Min(bounds.Width, bounds.Height));
                float meanRanged = cells.Average(cell => rangedThreat[map.cellIndices.CellToIndex(cell)]);
                float meanMelee = cells.Average(cell => meleeThreat[map.cellIndices.CellToIndex(cell)]);
                float meanThermal = cells.Average(cell => thermalThreat[map.cellIndices.CellToIndex(cell)]);
                float meanTrap = cells.Average(cell => trapThreat[map.cellIndices.CellToIndex(cell)]);
                int chokeCells = cells.Count(cell => chokeThreat[map.cellIndices.CellToIndex(cell)] > 0f);
                HashSet<IntVec3> roomCellSet = new HashSet<IntVec3>(cells);
                int junctions = cells.Count(cell => CardinalDirections.Count(direction => roomCellSet.Contains(cell + direction)) >= 3);
                int diagonalGates = cells.Count(HasDiagonalGate);
                int coverObjects = cells.Count(cell => cell.GetThingList(map).Any(thing => thing.def.fillPercent >= 0.35f));
                int exits = CountBoundaryDoors(cells);
                float fillRatio = cells.Count / (float)Mathf.Max(1, bounds.Area);

                KillzoneKind kinds = KillzoneKind.None;
                float confidence = 0f;
                if (meanThermal >= 18f || IsPotentialHeatTrap(room, cells, exits))
                {
                    kinds |= KillzoneKind.Temperature;
                    confidence = Mathf.Max(confidence, Mathf.Clamp01(0.45f + meanThermal / 100f));
                }
                if (aspect >= 2.7f && cells.Count >= 24 && meanRanged >= 8f && chokeCells >= cells.Count / 5)
                {
                    kinds |= KillzoneKind.Barrel;
                    confidence = Mathf.Max(confidence, Mathf.Clamp01(0.5f + aspect / 12f));
                }
                if (fillRatio <= 0.72f && bounds.Width >= 5 && bounds.Height >= 5
                    && junctions >= 2 && HasThreeLongArms(cells))
                {
                    kinds |= KillzoneKind.TShaped;
                    confidence = Mathf.Max(confidence, 0.68f);
                }
                if (diagonalGates >= 2 && meanRanged >= 5f)
                {
                    kinds |= KillzoneKind.Diagonal;
                    confidence = Mathf.Max(confidence, Mathf.Clamp01(0.52f + diagonalGates * 0.04f));
                }
                if (exits <= 2 && Mathf.Min(bounds.Width, bounds.Height) <= 5 && cells.Count <= 70
                    && chokeCells >= Mathf.Max(2, cells.Count / 4)
                    && (meanMelee >= 4f || meanRanged >= 4f || meanTrap >= 4f))
                {
                    kinds |= KillzoneKind.Melee;
                    confidence = Mathf.Max(confidence, 0.58f);
                }
                if (meanRanged >= 12f && cells.Count >= 20 && exits <= 3
                    && (aspect >= 1.3f || chokeCells >= 2) && coverObjects <= cells.Count / 5)
                {
                    kinds |= KillzoneKind.Shooting;
                    confidence = Mathf.Max(confidence, Mathf.Clamp01(0.5f + meanRanged / 120f));
                }

                if (kinds != KillzoneKind.None)
                {
                    DirectionSummary direction = SummarizeDirection(cells);
                    KillzoneRecord record = new KillzoneRecord
                    {
                        Kinds = kinds,
                        Center = ClosestCellToAverage(cells),
                        Bounds = bounds,
                        Confidence = confidence,
                        ExpectedThreat = meanRanged + meanMelee + meanThermal + meanTrap + chokeCells * 0.15f,
                        DominantThreatDirection = direction.Direction,
                        Directionality = direction.Directionality,
                        RoomId = room.ID
                    };
                    record.SetCells(map, cells);
                    killzones.Add(record);
                }
            }

            DetectUnenclosedKillzones();
        }

        private void DetectUnenclosedKillzones()
        {
            bool[] visited = new bool[map.cellIndices.NumGridCells];
            Queue<IntVec3> open = new Queue<IntVec3>();
            foreach (IntVec3 origin in map.AllCells)
            {
                int originIndex = map.cellIndices.CellToIndex(origin);
                if (visited[originIndex] || !IsSpatialRangedCandidate(origin)) continue;

                List<IntVec3> cells = new List<IntVec3>();
                visited[originIndex] = true;
                open.Enqueue(origin);
                while (open.Count > 0)
                {
                    IntVec3 cell = open.Dequeue();
                    cells.Add(cell);
                    foreach (IntVec3 direction in CardinalDirections)
                    {
                        IntVec3 adjacent = cell + direction;
                        if (!adjacent.InBounds(map)) continue;
                        int index = map.cellIndices.CellToIndex(adjacent);
                        if (visited[index] || !IsSpatialRangedCandidate(adjacent)) continue;
                        visited[index] = true;
                        open.Enqueue(adjacent);
                    }
                }

                if (cells.Count < 12) continue;
                AddSpatialKillzone(cells);
            }
        }

        private bool IsSpatialRangedCandidate(IntVec3 cell)
        {
            if (!cell.Standable(map)) return false;
            int index = map.cellIndices.CellToIndex(cell);
            bool exposed = rangedThreat[index] >= 18f
                || (rangedThreat[index] >= 11f
                    && DirectionalityAt(index) >= 0.62f
                    && chokeThreat[index] > 0f);
            return exposed && !cell.GetThingList(map).Any(thing => thing.def.fillPercent >= 0.75f);
        }

        private void AddSpatialKillzone(List<IntVec3> cells)
        {
            CellRect bounds = BoundsFor(cells);
            float aspect = Mathf.Max(bounds.Width, bounds.Height)
                / (float)Mathf.Max(1, Mathf.Min(bounds.Width, bounds.Height));
            float meanRanged = cells.Average(cell => rangedThreat[map.cellIndices.CellToIndex(cell)]);
            float meanChoke = cells.Average(cell => chokeThreat[map.cellIndices.CellToIndex(cell)]);
            DirectionSummary direction = SummarizeDirection(cells);
            KillzoneKind kinds = KillzoneKind.None;
            float confidence = 0f;

            if (aspect >= 2.35f && Mathf.Min(bounds.Width, bounds.Height) <= 14
                && meanRanged >= 10f && direction.Directionality >= 0.55f)
            {
                kinds |= KillzoneKind.Barrel;
                confidence = Mathf.Max(confidence,
                    Mathf.Clamp01(0.42f + aspect / 12f + direction.Directionality * 0.18f));
            }
            if (cells.Count >= 20 && meanRanged >= 18f)
            {
                kinds |= KillzoneKind.Shooting;
                confidence = Mathf.Max(confidence, Mathf.Clamp01(0.48f + meanRanged / 130f));
            }
            if (kinds == KillzoneKind.None) return;

            // A room-based detector may already own the same firing area. Preserve
            // the spatial record only when it contributes substantial exterior cells.
            bool duplicate = killzones.Any(existing => (existing.Kinds & kinds) != 0
                && cells.Count(cell => existing.Contains(map, cell)) >= cells.Count * 0.7f);
            if (duplicate) return;

            KillzoneRecord record = new KillzoneRecord
            {
                Kinds = kinds,
                Center = ClosestCellToAverage(cells),
                Bounds = bounds,
                Confidence = confidence,
                ExpectedThreat = meanRanged + meanChoke,
                DominantThreatDirection = direction.Direction,
                Directionality = direction.Directionality,
                RoomId = -1
            };
            record.SetCells(map, cells);
            killzones.Add(record);
        }

        private DirectionSummary SummarizeDirection(IEnumerable<IntVec3> cells)
        {
            float x = 0f;
            float z = 0f;
            float total = 0f;
            foreach (IntVec3 cell in cells)
            {
                int index = map.cellIndices.CellToIndex(cell);
                x += rangedDirectionX[index];
                z += rangedDirectionZ[index];
                total += rangedThreat[index];
            }
            Vector2 vector = new Vector2(x, z);
            return new DirectionSummary
            {
                Direction = vector.sqrMagnitude > 0.0001f ? vector.normalized : Vector2.zero,
                Directionality = total > 0.001f ? Mathf.Clamp01(vector.magnitude / total) : 0f
            };
        }

        private void RebuildPointsOfInterest()
        {
            pointsOfInterest.Clear();
            Dictionary<int, TacticalPointOfInterest> roomPois = new Dictionary<int, TacticalPointOfInterest>();

            foreach (StrategicTarget target in strategicTargets)
            {
                TacticalPointOfInterest poi = target.RoomId >= 0
                    ? GetOrCreateRoomPoi(roomPois, target.RoomId, target.Cell, target.Bounds)
                    : CreateSitePoi(target.Cell, target.Bounds);
                poi.AddStrategicTarget(target);
                if (target.RoomId < 0)
                {
                    pointsOfInterest.Add(poi);
                }
            }

            foreach (KillzoneRecord killzone in killzones)
            {
                TacticalPointOfInterest poi = killzone.RoomId >= 0
                    ? GetOrCreateRoomPoi(roomPois, killzone.RoomId, killzone.Center, killzone.Bounds)
                    : CreateSitePoi(killzone.Center, killzone.Bounds, killzone.Cells(map));
                poi.AddKillzone(killzone);
                if (killzone.RoomId < 0)
                {
                    pointsOfInterest.Add(poi);
                }
            }

            pointsOfInterest.AddRange(roomPois.Values);
            pointsOfInterest.Sort((left, right) =>
                (right.StrategicValue + right.ExpectedThreat).CompareTo(left.StrategicValue + left.ExpectedThreat));
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

        private bool IsPotentialHeatTrap(Room room, List<IntVec3> cells, int exits)
        {
            if (room.PsychologicallyOutdoors || exits > 3 || cells.Count < 12) return false;
            int flammable = 0;
            int ignition = 0;
            foreach (IntVec3 cell in cells)
            {
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing.def.BaseFlammability > 0.4f) flammable++;
                    string name = thing.def.defName ?? string.Empty;
                    if (thing is Fire || name.IndexOf("Incendiary", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("IED", StringComparison.OrdinalIgnoreCase) >= 0) ignition++;
                }
            }

            return flammable >= 6 && ignition >= 1;
        }

        private bool HasDiagonalGate(IntVec3 cell)
        {
            bool ne = Standable(cell + IntVec3.North + IntVec3.East)
                && !Standable(cell + IntVec3.North) && !Standable(cell + IntVec3.East);
            bool nw = Standable(cell + IntVec3.North + IntVec3.West)
                && !Standable(cell + IntVec3.North) && !Standable(cell + IntVec3.West);
            bool se = Standable(cell + IntVec3.South + IntVec3.East)
                && !Standable(cell + IntVec3.South) && !Standable(cell + IntVec3.East);
            bool sw = Standable(cell + IntVec3.South + IntVec3.West)
                && !Standable(cell + IntVec3.South) && !Standable(cell + IntVec3.West);
            return ne || nw || se || sw;
        }

        private bool HasThreeLongArms(List<IntVec3> cells)
        {
            HashSet<IntVec3> set = new HashSet<IntVec3>(cells);
            foreach (IntVec3 origin in cells)
            {
                int arms = 0;
                foreach (IntVec3 direction in CardinalDirections)
                {
                    bool lengthThree = true;
                    for (int step = 1; step <= 3; step++)
                    {
                        if (!set.Contains(origin + direction * step))
                        {
                            lengthThree = false;
                            break;
                        }
                    }
                    if (lengthThree) arms++;
                }
                if (arms >= 3) return true;
            }
            return false;
        }

        private int CountBoundaryDoors(List<IntVec3> cells)
        {
            HashSet<IntVec3> set = new HashSet<IntVec3>(cells);
            HashSet<Thing> doors = new HashSet<Thing>();
            foreach (IntVec3 cell in cells)
            {
                foreach (IntVec3 direction in CardinalDirections)
                {
                    IntVec3 adjacent = cell + direction;
                    if (!adjacent.InBounds(map) || set.Contains(adjacent)) continue;
                    Building_Door door = adjacent.GetEdifice(map) as Building_Door;
                    if (door != null) doors.Add(door);
                }
            }
            return doors.Count;
        }

        private float ObjectiveUtility(StrategicTarget target, IntVec3 from, Faction attacker)
        {
            float distanceCost = from.IsValid ? from.DistanceTo(target.Cell) * 0.35f : 0f;
            return target.Score - MeanThreat(target.Cell, 3, attacker) * 0.7f - distanceCost;
        }

        private float MeanThreat(IntVec3 center, int radius, Faction attacker)
        {
            float total = 0f;
            int count = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map)) continue;
                int index = map.cellIndices.CellToIndex(cell);
                total += rangedThreat[index] + meleeThreat[index] + thermalThreat[index] + trapThreat[index] + chokeThreat[index];
                count++;
            }
            return count > 0 ? total / count : 0f;
        }

        private bool IsThreatSource(Thing thing, Faction attacker)
        {
            if (thing?.Faction == null) return false;
            return attacker == null ? thing.Faction == Faction.OfPlayer : thing.HostileTo(attacker);
        }

        private static float DefenderPriority(Pawn pawn)
        {
            float priority = pawn.Drafted ? 100f : 0f;
            priority += pawn.equipment?.Primary != null ? 30f : 0f;
            priority += pawn.skills?.GetSkill(SkillDefOf.Shooting).Level ?? 0;
            priority += (pawn.skills?.GetSkill(SkillDefOf.Melee).Level ?? 0) * 0.5f;
            return priority;
        }

        private struct RangedProjectionCell
        {
            public int Index;
            public float Factor;
            public float DirectionX;
            public float DirectionZ;
        }

        private struct DirectionSummary
        {
            public Vector2 Direction;
            public float Directionality;
        }

        private int IndoorRoomId(IntVec3 cell)
        {
            Room room = cell.GetRoom(map);
            return room != null && !room.PsychologicallyOutdoors ? room.ID : -1;
        }

        private bool Standable(IntVec3 cell)
        {
            return cell.InBounds(map) && cell.Standable(map);
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
