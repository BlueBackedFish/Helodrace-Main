using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class MapComponent_SweetGasGrid : MapComponent
    {
        private const float MinDensity = 0.02f;
        private const int SimulationIntervalTicks = 30;
        private const int ExposureIntervalTicks = 300;
        private const int VisualIntervalTicks = 45;
        private const int VisualSamplesPerPulse = 5;
        private const float DensityDissipationPerTick = 0.00010f;
        private const float SeverityPerTickAtFullDensity = 0.28f / 240f;
        private const float VisualFleckScale = 3.0f;
        private const float SourceDissipationPerTick = 0.00010f;
        private const float PropagationFalloff = 0.74f;
        private const float DiagonalPropagationFactor = 0.88f;
        private const float MinPropagationDensity = 0.04f;
        private const float PropagationImprovementThreshold = 0.008f;
        private const int AirflowCacheRefreshIntervalTicks = 2500;
        private const int MaxMarkerRemovalsPerTick = 64;

        private static readonly PropagationDirection[] PropagationDirections =
        {
            new PropagationDirection(new IntVec3(0, 0, 1), false),
            new PropagationDirection(new IntVec3(1, 0, 1), true),
            new PropagationDirection(new IntVec3(1, 0, 0), false),
            new PropagationDirection(new IntVec3(1, 0, -1), true),
            new PropagationDirection(new IntVec3(0, 0, -1), false),
            new PropagationDirection(new IntVec3(-1, 0, -1), true),
            new PropagationDirection(new IntVec3(-1, 0, 0), false),
            new PropagationDirection(new IntVec3(-1, 0, 1), true),
        };

        private float[] densityMap;
        private float[] previousDensityMap;
        private float[] sourceStrengthMap;
        private float[] scratchDensityMap;
        private bool[] airflowCache;
        private bool[] dynamicAirflowCache;
        private byte[] flowAlongCache;
        private int[] flowAlongCacheStamps;
        private int[] computedDensityStamps;
        private int[] propagationIndexOffsets;
        private int[] propagationSideAIndexOffsets;
        private int[] propagationSideBIndexOffsets;
        private int propagationIndexOffsetMapWidth;
        private Dictionary<int, float> savedDensities = new Dictionary<int, float>();
        private Dictionary<int, Gas_SweetGas> markers = new Dictionary<int, Gas_SweetGas>();
        private HashSet<int> activeIndices = new HashSet<int>();
        private HashSet<int> activeSourceIndices = new HashSet<int>();
        private HashSet<int> visualIndices = new HashSet<int>();
        private HashSet<int> pendingMarkerRemovalSet = new HashSet<int>();
        private List<int> tmpComputedCells = new List<int>();
        private List<int> tmpCells = new List<int>();
        private List<int> tmpSources = new List<int>();
        private List<GasPropagationNode> tmpFrontier = new List<GasPropagationNode>();
        private List<GasPropagationNode> tmpNextFrontier = new List<GasPropagationNode>();
        private Queue<int> pendingMarkerRemovals = new Queue<int>();
        private ThingDef gasDef;
        private int nextAirflowCacheRefreshTick;
        private int lastDensityUpdateTick;
        private int flowAlongCacheStamp;
        private int computedDensityStamp;

        public MapComponent_SweetGasGrid(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                BuildSavedDensities();
            }

            Scribe_Collections.Look(ref savedDensities, "sweetGasDensities", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (savedDensities == null)
                {
                    savedDensities = new Dictionary<int, float>();
                }

                markers = new Dictionary<int, Gas_SweetGas>();
                activeIndices = new HashSet<int>();
                activeSourceIndices = new HashSet<int>();
                visualIndices = new HashSet<int>();
                pendingMarkerRemovalSet = new HashSet<int>();
                tmpComputedCells = new List<int>();
                tmpCells = new List<int>();
                tmpSources = new List<int>();
                tmpFrontier = new List<GasPropagationNode>();
                tmpNextFrontier = new List<GasPropagationNode>();
                pendingMarkerRemovals = new Queue<int>();
                InitRuntimeMaps();
                foreach (KeyValuePair<int, float> savedDensity in savedDensities)
                {
                    if (savedDensity.Key >= 0 && savedDensity.Key < sourceStrengthMap.Length && savedDensity.Value > MinDensity)
                    {
                        sourceStrengthMap[savedDensity.Key] = Mathf.Clamp01(savedDensity.Value);
                        activeSourceIndices.Add(savedDensity.Key);
                    }
                }
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            InitRuntimeMaps();
            gasDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_SweetGas");
            ImportLegacyMarkers();
            RecomputeDensityFromSources(0);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            ProcessPendingMarkerRemovals();
            if (activeSourceIndices.Count == 0 && activeIndices.Count == 0 && visualIndices.Count == 0)
            {
                return;
            }

            int staggeredTick = Find.TickManager.TicksGame + map.Index + 17;
            bool simulationTick = staggeredTick % SimulationIntervalTicks == 0;
            bool exposureTick = staggeredTick % ExposureIntervalTicks == 0;
            bool visualTick = staggeredTick % VisualIntervalTicks == 0;

            if (!simulationTick)
            {
                if (visualTick)
                {
                    ThrowAmbientVisualFlecks();
                }

                return;
            }

            RefreshAirflowCacheIfNeeded();
            TickGasGrid(SimulationIntervalTicks, exposureTick);
            if (visualTick)
            {
                ThrowAmbientVisualFlecks();
            }
        }

        public bool AddGas(IntVec3 cell, ThingDef def, float amount)
        {
            if (map == null || def == null || amount <= 0f)
            {
                return false;
            }

            gasDef = def;
            InitRuntimeMaps();
            if (activeSourceIndices.Count == 0)
            {
                RebuildAirflowCache();
            }
            else
            {
                RefreshAirflowCacheIfNeeded();
            }

            if (!CanGasFlowInto(cell))
            {
                return false;
            }

            int index = map.cellIndices.CellToIndex(cell);
            previousDensityMap[index] = VisualDensityAtIndex(index);
            sourceStrengthMap[index] = Mathf.Clamp01(sourceStrengthMap[index] + amount);
            densityMap[index] = Mathf.Max(densityMap[index], sourceStrengthMap[index]);
            lastDensityUpdateTick = Find.TickManager.TicksGame;
            activeSourceIndices.Add(index);
            activeIndices.Add(index);
            visualIndices.Add(index);
            return true;
        }

        public bool CanGasOccupy(IntVec3 cell)
        {
            return CanGasFlowInto(cell);
        }

        public float DensityAt(IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
            {
                return 0f;
            }

            return DensityAtIndex(map.cellIndices.CellToIndex(cell));
        }

        public float VisualDensityAt(IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
            {
                return 0f;
            }

            return VisualDensityAtIndex(map.cellIndices.CellToIndex(cell));
        }

        private float DensityAtIndex(int index)
        {
            if (densityMap == null || index < 0 || index >= densityMap.Length)
            {
                return 0f;
            }

            return Mathf.Clamp01(densityMap[index]);
        }

        private float VisualDensityAtIndex(int index)
        {
            if (densityMap == null || previousDensityMap == null || index < 0 || index >= densityMap.Length)
            {
                return 0f;
            }

            float t = Mathf.Clamp01((Find.TickManager.TicksGame - lastDensityUpdateTick) / (float)SimulationIntervalTicks);
            return Mathf.Lerp(previousDensityMap[index], densityMap[index], t);
        }

        private void TickGasGrid(int ticks, bool applyExposure)
        {
            DecaySources(ticks);
            RecomputeDensityFromSources(ticks);

            if (!applyExposure)
            {
                return;
            }

            tmpCells.Clear();
            tmpCells.AddRange(activeIndices);
            for (int i = 0; i < tmpCells.Count; i++)
            {
                int index = tmpCells[i];
                float density = DensityAtIndex(index);
                if (density <= 0f)
                {
                    continue;
                }

                IntVec3 cell = map.cellIndices.IndexToCell(index);
                ApplyExposure(cell, density, ExposureIntervalTicks);
            }
        }

        private void DecaySources(int ticks)
        {
            tmpSources.Clear();
            tmpSources.AddRange(activeSourceIndices);
            float decay = SourceDissipationPerTick * ticks;
            for (int i = 0; i < tmpSources.Count; i++)
            {
                int index = tmpSources[i];
                float next = sourceStrengthMap[index] - decay;
                if (next <= MinDensity)
                {
                    sourceStrengthMap[index] = 0f;
                    activeSourceIndices.Remove(index);
                }
                else
                {
                    sourceStrengthMap[index] = next;
                }
            }
        }

        private void RecomputeDensityFromSources(int ticks)
        {
            tmpCells.Clear();
            tmpCells.AddRange(activeIndices);
            CapturePreviousVisualDensities();
            foreach (int index in tmpComputedCells)
            {
                scratchDensityMap[index] = 0f;
            }

            tmpComputedCells.Clear();
            BeginComputedDensityPass();
            tmpSources.Clear();
            tmpSources.AddRange(activeSourceIndices);
            BeginFlowAlongCachePass();
            PropagateSources(tmpSources);

            for (int i = 0; i < tmpCells.Count; i++)
            {
                int index = tmpCells[i];
                if (computedDensityStamps[index] != computedDensityStamp)
                {
                    densityMap[index] = 0f;
                }
            }

            activeIndices.Clear();
            foreach (int index in tmpComputedCells)
            {
                float density = scratchDensityMap[index] - DensityDissipationPerTick * ticks;
                if (density > MinDensity)
                {
                    densityMap[index] = Mathf.Clamp01(density);
                    activeIndices.Add(index);
                    visualIndices.Add(index);
                }
                else
                {
                    densityMap[index] = 0f;
                }
            }

            lastDensityUpdateTick = Find.TickManager.TicksGame;
            UpdateVisualMarkers();
        }

        private void CapturePreviousVisualDensities()
        {
            foreach (int index in visualIndices)
            {
                previousDensityMap[index] = VisualDensityAtIndex(index);
            }

            for (int i = 0; i < tmpCells.Count; i++)
            {
                int index = tmpCells[i];
                previousDensityMap[index] = VisualDensityAtIndex(index);
                visualIndices.Add(index);
            }
        }

        private void UpdateVisualMarkers()
        {
            tmpCells.Clear();
            tmpCells.AddRange(visualIndices);
            for (int i = 0; i < tmpCells.Count; i++)
            {
                int index = tmpCells[i];
                float targetDensity = DensityAtIndex(index);
                float visualDensity = VisualDensityAtIndex(index);
                if (targetDensity <= MinDensity && visualDensity <= MinDensity)
                {
                    previousDensityMap[index] = 0f;
                    visualIndices.Remove(index);
                    EnqueueMarkerRemoval(index);
                }
            }
        }

        private void PropagateSources(List<int> sourceIndices)
        {
            tmpFrontier.Clear();
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                int sourceIndex = sourceIndices[i];
                float strength = sourceStrengthMap[sourceIndex];
                if (strength <= MinDensity)
                {
                    continue;
                }

                SetScratchDensity(sourceIndex, strength);
                IntVec3 sourceCell = map.cellIndices.IndexToCell(sourceIndex);
                tmpFrontier.Add(new GasPropagationNode(sourceIndex, sourceCell.x, sourceCell.z, strength));
            }

            while (tmpFrontier.Count > 0)
            {
                tmpNextFrontier.Clear();
                for (int i = 0; i < tmpFrontier.Count; i++)
                {
                    GasPropagationNode node = tmpFrontier[i];
                    for (int j = 0; j < PropagationDirections.Length; j++)
                    {
                        PropagationDirection direction = PropagationDirections[j];
                        if (!CanGasFlowAlong(node.Index, node.X, node.Z, direction, out int targetIndex))
                        {
                            continue;
                        }

                        float nextDensity = node.Density * direction.StepFactor;
                        if (nextDensity <= MinPropagationDensity || nextDensity <= scratchDensityMap[targetIndex] + PropagationImprovementThreshold)
                        {
                            continue;
                        }

                        SetScratchDensity(targetIndex, nextDensity);
                        tmpNextFrontier.Add(new GasPropagationNode(targetIndex, node.X + direction.X, node.Z + direction.Z, nextDensity));
                    }
                }

                List<GasPropagationNode> swap = tmpFrontier;
                tmpFrontier = tmpNextFrontier;
                tmpNextFrontier = swap;
            }
        }

        private void SetScratchDensity(int index, float density)
        {
            if (density > scratchDensityMap[index])
            {
                scratchDensityMap[index] = Mathf.Clamp01(density);
                if (computedDensityStamps[index] != computedDensityStamp)
                {
                    computedDensityStamps[index] = computedDensityStamp;
                    tmpComputedCells.Add(index);
                }
            }
        }

        private void ApplyExposure(IntVec3 cell, float density, int ticks)
        {
            foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
            {
                if (thing is Pawn pawn)
                {
                    Gas_SweetGas.ApplyExposureTo(pawn, density, SeverityPerTickAtFullDensity * ticks);
                }
            }
        }

        private void ThrowAmbientVisualFlecks()
        {
            if (visualIndices.Count == 0)
            {
                return;
            }

            tmpCells.Clear();
            tmpCells.AddRange(visualIndices);
            int sampleCount = Mathf.Min(VisualSamplesPerPulse, Mathf.Max(1, tmpCells.Count / 12 + 1));
            for (int i = 0; i < sampleCount; i++)
            {
                int index = tmpCells[Rand.Range(0, tmpCells.Count)];
                float density = VisualDensityAtIndex(index);
                if (density <= MinDensity)
                {
                    continue;
                }

                IntVec3 cell = map.cellIndices.IndexToCell(index);
                if (cell.ShouldSpawnMotesAt(map))
                {
                    Vector3 loc = cell.ToVector3Shifted();
                    loc.x += Rand.Range(-0.28f, 0.28f);
                    loc.z += Rand.Range(-0.28f, 0.28f);
                    FleckMaker.Static(loc, map, FleckDefOf.Smoke, Mathf.Lerp(0.8f, VisualFleckScale, density));
                }
            }
        }

        private void EnsureMarker(int index)
        {
            if (gasDef == null)
            {
                gasDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_SweetGas");
            }

            if (gasDef == null)
            {
                return;
            }

            pendingMarkerRemovalSet.Remove(index);
            if (markers.TryGetValue(index, out Gas_SweetGas existing) && existing != null && !existing.Destroyed && existing.Spawned)
            {
                return;
            }

            IntVec3 cell = map.cellIndices.IndexToCell(index);
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (thing is Gas_SweetGas gas && !gas.Destroyed)
                {
                    markers[index] = gas;
                    return;
                }
            }

            Thing thingToSpawn = ThingMaker.MakeThing(gasDef);
            if (thingToSpawn is Gas_SweetGas marker)
            {
                markers[index] = marker;
            }

            GenSpawn.Spawn(thingToSpawn, cell, map);
        }

        private void RemoveMarker(int index)
        {
            if (markers.TryGetValue(index, out Gas_SweetGas marker) && marker != null && !marker.Destroyed)
            {
                marker.Destroy(DestroyMode.Vanish);
            }

            markers.Remove(index);
        }

        private void EnqueueMarkerRemoval(int index)
        {
            if (pendingMarkerRemovalSet.Add(index))
            {
                pendingMarkerRemovals.Enqueue(index);
            }
        }

        private void ProcessPendingMarkerRemovals()
        {
            int removals = Mathf.Min(MaxMarkerRemovalsPerTick, pendingMarkerRemovals.Count);
            for (int i = 0; i < removals; i++)
            {
                int index = pendingMarkerRemovals.Dequeue();
                pendingMarkerRemovalSet.Remove(index);
                if (DensityAtIndex(index) <= MinDensity && !visualIndices.Contains(index))
                {
                    RemoveMarker(index);
                }
            }
        }

        private void ImportLegacyMarkers()
        {
            if (gasDef == null)
            {
                return;
            }

            List<Thing> existingMarkers = map.listerThings.ThingsOfDef(gasDef);
            for (int i = existingMarkers.Count - 1; i >= 0; i--)
            {
                Gas_SweetGas gas = existingMarkers[i] as Gas_SweetGas;
                if (gas == null || gas.Destroyed || !gas.Spawned)
                {
                    continue;
                }

                int index = map.cellIndices.CellToIndex(gas.Position);
                if (gas.LegacyDensityForMigration > 0f)
                {
                    sourceStrengthMap[index] = Mathf.Clamp01(sourceStrengthMap[index] + gas.LegacyDensityForMigration);
                    activeSourceIndices.Add(index);
                }

                if (markers.TryGetValue(index, out Gas_SweetGas previous) && previous != gas && previous != null && !previous.Destroyed)
                {
                    previous.Destroy(DestroyMode.Vanish);
                }

                markers[index] = gas;
            }
        }

        private void InitRuntimeMaps()
        {
            if (map == null)
            {
                return;
            }

            int cellCount = map.cellIndices.NumGridCells;
            if (densityMap == null || densityMap.Length != cellCount)
            {
                densityMap = new float[cellCount];
            }

            if (previousDensityMap == null || previousDensityMap.Length != cellCount)
            {
                previousDensityMap = new float[cellCount];
            }

            if (sourceStrengthMap == null || sourceStrengthMap.Length != cellCount)
            {
                sourceStrengthMap = new float[cellCount];
            }

            if (scratchDensityMap == null || scratchDensityMap.Length != cellCount)
            {
                scratchDensityMap = new float[cellCount];
            }

            if (computedDensityStamps == null || computedDensityStamps.Length != cellCount)
            {
                computedDensityStamps = new int[cellCount];
            }

            if (airflowCache == null || airflowCache.Length != cellCount)
            {
                airflowCache = new bool[cellCount];
            }

            if (dynamicAirflowCache == null || dynamicAirflowCache.Length != cellCount)
            {
                dynamicAirflowCache = new bool[cellCount];
            }

            int flowCacheLength = cellCount * PropagationDirections.Length;
            if (flowAlongCache == null || flowAlongCache.Length != flowCacheLength)
            {
                flowAlongCache = new byte[flowCacheLength];
            }

            if (flowAlongCacheStamps == null || flowAlongCacheStamps.Length != flowCacheLength)
            {
                flowAlongCacheStamps = new int[flowCacheLength];
            }

            InitPropagationIndexOffsets(map.Size.x);

            if (nextAirflowCacheRefreshTick <= 0)
            {
                nextAirflowCacheRefreshTick = 1;
                RebuildAirflowCache();
            }
        }

        private void BuildSavedDensities()
        {
            savedDensities.Clear();
            if (sourceStrengthMap == null)
            {
                return;
            }

            foreach (int index in activeSourceIndices)
            {
                float sourceStrength = sourceStrengthMap[index];
                if (sourceStrength > MinDensity)
                {
                    savedDensities[index] = sourceStrength;
                }
            }
        }

        private bool CanGasFlowInto(IntVec3 cell)
        {
            InitRuntimeMaps();
            return CanGasFlowIntoInitialized(cell, out _);
        }

        private bool CanGasFlowIntoInitialized(IntVec3 cell, out int index)
        {
            index = -1;
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }

            index = map.cellIndices.CellToIndex(cell);
            return CanGasFlowIntoKnownIndex(cell, index);
        }

        private bool CanGasFlowIntoKnownIndex(IntVec3 cell, int index)
        {
            return CanGasFlowIntoKnownIndex(index, cell.x, cell.z);
        }

        private bool CanGasFlowIntoKnownIndex(int index, int x, int z)
        {
            if (map == null || x < 0 || z < 0 || x >= map.Size.x || z >= map.Size.z)
            {
                return false;
            }

            if (!dynamicAirflowCache[index])
            {
                return airflowCache[index];
            }

            Building_Door door = new IntVec3(x, 0, z).GetDoor(map);
            return door != null && door.Open;
        }

        private bool CanGasFlowAlong(int originIndex, int originX, int originZ, PropagationDirection direction, out int targetIndex)
        {
            targetIndex = originIndex + propagationIndexOffsets[direction.Ordinal];
            int cacheIndex = originIndex * PropagationDirections.Length + direction.Ordinal;
            if (flowAlongCacheStamps[cacheIndex] == flowAlongCacheStamp)
            {
                return flowAlongCache[cacheIndex] == 2;
            }

            bool canFlow = CanGasFlowIntoKnownIndex(targetIndex, originX + direction.X, originZ + direction.Z);
            if (canFlow && direction.IsDiagonal)
            {
                canFlow = CanGasFlowIntoKnownIndex(originIndex + propagationSideAIndexOffsets[direction.Ordinal], originX + direction.X, originZ) &&
                    CanGasFlowIntoKnownIndex(originIndex + propagationSideBIndexOffsets[direction.Ordinal], originX, originZ + direction.Z);
            }

            flowAlongCacheStamps[cacheIndex] = flowAlongCacheStamp;
            flowAlongCache[cacheIndex] = canFlow ? (byte)2 : (byte)1;
            return canFlow;
        }

        private void BeginFlowAlongCachePass()
        {
            flowAlongCacheStamp++;
            if (flowAlongCacheStamp == int.MaxValue)
            {
                System.Array.Clear(flowAlongCacheStamps, 0, flowAlongCacheStamps.Length);
                flowAlongCacheStamp = 1;
            }
        }

        private void BeginComputedDensityPass()
        {
            computedDensityStamp++;
            if (computedDensityStamp == int.MaxValue)
            {
                System.Array.Clear(computedDensityStamps, 0, computedDensityStamps.Length);
                computedDensityStamp = 1;
            }
        }

        private void InitPropagationIndexOffsets(int mapWidth)
        {
            if (propagationIndexOffsets == null || propagationIndexOffsets.Length != PropagationDirections.Length)
            {
                propagationIndexOffsets = new int[PropagationDirections.Length];
                propagationSideAIndexOffsets = new int[PropagationDirections.Length];
                propagationSideBIndexOffsets = new int[PropagationDirections.Length];
                propagationIndexOffsetMapWidth = 0;
            }

            if (propagationIndexOffsetMapWidth == mapWidth)
            {
                return;
            }

            for (int i = 0; i < PropagationDirections.Length; i++)
            {
                PropagationDirection direction = PropagationDirections[i];
                propagationIndexOffsets[i] = direction.X + direction.Z * mapWidth;
                propagationSideAIndexOffsets[i] = direction.X;
                propagationSideBIndexOffsets[i] = direction.Z * mapWidth;
            }

            propagationIndexOffsetMapWidth = mapWidth;
        }

        private void RefreshAirflowCacheIfNeeded()
        {
            if (Find.TickManager.TicksGame < nextAirflowCacheRefreshTick)
            {
                return;
            }

            RebuildAirflowCache();
        }

        private void RebuildAirflowCache()
        {
            if (map == null || airflowCache == null || dynamicAirflowCache == null)
            {
                return;
            }

            for (int i = 0; i < airflowCache.Length; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                Building_Door door = cell.GetDoor(map);
                dynamicAirflowCache[i] = door != null;
                airflowCache[i] = door != null || cell.Standable(map);
            }

            nextAirflowCacheRefreshTick = Find.TickManager.TicksGame + AirflowCacheRefreshIntervalTicks;
        }

        private struct PropagationDirection
        {
            public readonly IntVec3 Offset;
            public readonly IntVec3 SideA;
            public readonly IntVec3 SideB;
            public readonly int Ordinal;
            public readonly int X;
            public readonly int Z;
            public readonly float StepFactor;
            public readonly bool IsDiagonal;

            public PropagationDirection(IntVec3 offset, bool isDiagonal)
            {
                Offset = offset;
                SideA = new IntVec3(offset.x, 0, 0);
                SideB = new IntVec3(0, 0, offset.z);
                Ordinal = offset.x == 0 && offset.z == 1 ? 0 :
                    offset.x == 1 && offset.z == 1 ? 1 :
                    offset.x == 1 && offset.z == 0 ? 2 :
                    offset.x == 1 && offset.z == -1 ? 3 :
                    offset.x == 0 && offset.z == -1 ? 4 :
                    offset.x == -1 && offset.z == -1 ? 5 :
                    offset.x == -1 && offset.z == 0 ? 6 : 7;
                X = offset.x;
                Z = offset.z;
                IsDiagonal = isDiagonal;
                StepFactor = isDiagonal ? PropagationFalloff * DiagonalPropagationFactor : PropagationFalloff;
            }

        }

        private struct GasPropagationNode
        {
            public readonly int Index;
            public readonly int X;
            public readonly int Z;
            public readonly float Density;

            public GasPropagationNode(int index, int x, int z, float density)
            {
                Index = index;
                X = x;
                Z = z;
                Density = density;
            }
        }
    }

    public class Gas_SweetGas : ThingWithComps
    {
        private const string SweetGasProtectionPouchDefName = "HD_Apparel_GreatWarCBRNPouch";
        private const float InitialDensity = 0.35f;
        private const float OverlayMinSize = 1.25f;
        private const float OverlayMaxSize = 2.55f;
        private const float OverlayMinAlpha = 0.12f;
        private const float OverlayMaxAlpha = 0.40f;
        private const int OverlayMaterialBuckets = 8;

        private float density;

        private static HediffDef exposureHediff;
        private static HediffDef riskHediff;
        private static StatDef vacuumResistanceStat;
        private static Material[] overlayMaterials;

        public float LegacyDensityForMigration => density;

        public float DensityPercent
        {
            get
            {
                if (this.Spawned && this.Map != null)
                {
                    return this.Map.GetComponent<MapComponent_SweetGasGrid>()?.DensityAt(this.Position) ?? 0f;
                }

                return Mathf.Clamp01(density);
            }
        }

        public override string LabelMouseover => $"{base.LabelMouseover} ({DensityLabel})";

        public override string GetInspectString()
        {
            string inspectString = base.GetInspectString();
            string densityString = DensityLabel;
            if (string.IsNullOrEmpty(inspectString))
            {
                return densityString;
            }

            return inspectString + "\n" + densityString;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref density, "density", InitialDensity);
        }

        public void AddDensity(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            density = Mathf.Clamp01(density + amount);
        }

        public static bool AddGasAt(IntVec3 cell, Map map, ThingDef gasDef, float densityToAdd)
        {
            if (map == null || gasDef == null || !cell.InBounds(map))
            {
                return false;
            }

            return map.GetComponent<MapComponent_SweetGasGrid>()?.AddGas(cell, gasDef, densityToAdd) ?? false;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float densityPercent = this.Spawned && this.Map != null
                ? this.Map.GetComponent<MapComponent_SweetGasGrid>()?.VisualDensityAt(this.Position) ?? DensityPercent
                : DensityPercent;
            if (densityPercent <= 0f)
            {
                return;
            }

            drawLoc.y = AltitudeLayer.Gas.AltitudeFor();
            float size = Mathf.Lerp(OverlayMinSize, OverlayMaxSize, densityPercent);
            Vector3 scale = new Vector3(size / 10f, 1f, size / 10f);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawLoc, Quaternion.identity, scale), OverlayMaterialFor(densityPercent), 0);
        }

        internal static void ApplyExposureTo(Pawn pawn, float densityPercent, float severityAtFullDensity)
        {
            HediffDef hediffDef = ExposureHediff;
            if (hediffDef == null || densityPercent <= 0f || !CanBeAffectedBySweetGas(pawn))
            {
                return;
            }

            float resistance = VacuumResistanceStat != null ? pawn.GetStatValue(VacuumResistanceStat) : 0f;
            if (resistance >= 1f)
            {
                return;
            }

            float severityGain = severityAtFullDensity * Mathf.Clamp01(densityPercent) * Mathf.Clamp01(1f - resistance);
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing is Hediff_SweetGasExposure sweetExposure)
            {
                sweetExposure.AddDose(severityGain);
                EnsureRiskHediff(pawn);
                return;
            }

            if (existing != null)
            {
                existing.Severity = Mathf.Min(existing.Severity + severityGain, hediffDef.maxSeverity);
                EnsureRiskHediff(pawn);
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            hediff.Severity = Mathf.Min(severityGain, hediffDef.maxSeverity);
            pawn.health.AddHediff(hediff);
            if (hediff is Hediff_SweetGasExposure newSweetExposure)
            {
                newSweetExposure.MarkDoseChanged();
            }

            EnsureRiskHediff(pawn);
        }

        private static bool CanBeAffectedBySweetGas(Pawn pawn)
        {
            if (pawn.Dead || pawn.health == null || pawn.RaceProps == null || !pawn.RaceProps.IsFlesh)
            {
                return false;
            }

            return !WearingSweetGasProtection(pawn);
        }

        private static bool WearingSweetGasProtection(Pawn pawn)
        {
            List<Apparel> wornApparel = pawn.apparel?.WornApparel;
            if (wornApparel == null)
            {
                return false;
            }

            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (wornApparel[i]?.def?.defName == SweetGasProtectionPouchDefName)
                {
                    return true;
                }
            }

            return false;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            if (!respawningAfterLoad && density <= 0f)
            {
                density = 0f;
            }
        }

        private string DensityLabel => "HD_SweetGas_Density".Translate((DensityPercent * 100f).ToString("F0")).Resolve();

        private static Material OverlayMaterialFor(float densityPercent)
        {
            if (overlayMaterials == null)
            {
                overlayMaterials = new Material[OverlayMaterialBuckets];
            }

            int bucket = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(densityPercent) * OverlayMaterialBuckets) - 1, 0, OverlayMaterialBuckets - 1);
            if (overlayMaterials[bucket] == null)
            {
                float t = (bucket + 1f) / OverlayMaterialBuckets;
                Color color = new Color(0.95f, 0.86f, 0.35f, Mathf.Lerp(OverlayMinAlpha, OverlayMaxAlpha, t));
                Material material = new Material(ShaderDatabase.Transparent);
                material.mainTexture = BaseContent.WhiteTex;
                material.color = color;
                overlayMaterials[bucket] = material;
            }

            return overlayMaterials[bucket];
        }

        private static void EnsureRiskHediff(Pawn pawn)
        {
            HediffDef riskDef = RiskHediff;
            if (riskDef == null || pawn.health.hediffSet.HasHediff(riskDef))
            {
                return;
            }

            Hediff risk = HediffMaker.MakeHediff(riskDef, pawn);
            risk.Severity = 0.04f;
            pawn.health.AddHediff(risk);
        }

        private static HediffDef ExposureHediff
        {
            get
            {
                if (exposureHediff == null)
                {
                    exposureHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_SweetGasExposure");
                    if (exposureHediff == null)
                    {
                        Log.WarningOnce("Helodrace: HD_SweetGasExposure hediff is missing, so sweet gas cannot apply exposure.", 85219501);
                    }
                }

                return exposureHediff;
            }
        }

        public static HediffDef RiskHediff
        {
            get
            {
                if (riskHediff == null)
                {
                    riskHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_SweetGasRisk");
                    if (riskHediff == null)
                    {
                        Log.WarningOnce("Helodrace: HD_SweetGasRisk hediff is missing, so sweet gas cannot progress delayed injury.", 85219502);
                    }
                }

                return riskHediff;
            }
        }

        private static StatDef VacuumResistanceStat
        {
            get
            {
                if (vacuumResistanceStat == null)
                {
                    vacuumResistanceStat = DefDatabase<StatDef>.GetNamedSilentFail("VacuumResistance");
                }

                return vacuumResistanceStat;
            }
        }
    }

    public class Hediff_SweetGasExposure : HediffWithComps
    {
        private const int StaleExposureTicks = GenDate.TicksPerHour * 4;
        private const int CheckIntervalTicks = 600;
        private const float TreatmentSeverityReductionBase = 0.18f;
        private const float TreatmentSeverityReductionQualityFactor = 0.32f;

        private int lastDoseChangedTick = -1;

        public override bool ShouldRemove => base.ShouldRemove || this.Severity <= 0f || ExposureIsStale;

        public override float TendPriority => 1.8f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastDoseChangedTick, "lastDoseChangedTick", -1);
        }

        public override bool TendableNow(bool ignoreTimer = false)
        {
            return this.pawn != null && !this.pawn.Dead;
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (lastDoseChangedTick < 0)
            {
                lastDoseChangedTick = Find.TickManager.TicksGame;
            }

            if (this.pawn != null && this.pawn.IsHashIntervalTick(CheckIntervalTicks) && ExposureIsStale)
            {
                this.Severity = 0f;
            }
        }

        public void AddDose(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            float previousSeverity = this.Severity;
            this.Severity = Mathf.Min(this.Severity + amount, this.def.maxSeverity);
            if (!Mathf.Approximately(previousSeverity, this.Severity))
            {
                MarkDoseChanged();
            }
        }

        public void MarkDoseChanged()
        {
            lastDoseChangedTick = Find.TickManager.TicksGame;
        }

        public override void Tended(float quality, float maxQuality, int batchPosition = 0)
        {
            base.Tended(quality, maxQuality, batchPosition);

            float qualityFactor = maxQuality > 0f ? Mathf.Clamp01(quality / maxQuality) : Mathf.Clamp01(quality);
            float reduction = TreatmentSeverityReductionBase + qualityFactor * TreatmentSeverityReductionQualityFactor;
            this.Severity = Mathf.Max(0f, this.Severity - reduction);
            MarkDoseChanged();
        }

        private bool ExposureIsStale => lastDoseChangedTick >= 0 && Find.TickManager.TicksGame - lastDoseChangedTick >= StaleExposureTicks;
    }

    public class Hediff_SweetGasRisk : HediffWithComps
    {
        private const int ProgressIntervalTicks = 120;
        private const int ScarIntervalTicks = 1200;
        private const int DeathCheckIntervalTicks = 2500;
        private const int ScarsPerInterval = 3;
        private const float ExposureRiskPerDay = 15.00f;
        private const float CommittedRiskPerDay = 4.00f;
        private const float TreatmentProgressFactor = 0.20f;
        private const float ActivePhaseSeverity = 0.35f;
        private const float LateActivePhaseSeverity = 0.72f;
        private const float LateActiveDeathChance = 0.65f;

        private static HediffDef exposureHediff;
        private static HediffDef acidBurnHediff;

        public override bool ShouldRemove => base.ShouldRemove || this.Severity <= 0f;

        public override string LabelInBrackets => "HD_SweetGasRisk_Progress".Translate((this.Severity * 100f).ToString("F0")).Resolve();

        public override float TendPriority => 2.5f;

        public override bool TendableNow(bool ignoreTimer = false)
        {
            return this.pawn != null && !this.pawn.Dead && base.TendableNow(ignoreTimer);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);

            if (this.pawn == null || this.pawn.Dead)
            {
                return;
            }

            if (this.pawn.IsHashIntervalTick(ProgressIntervalTicks))
            {
                ProgressRisk(ProgressIntervalTicks);
            }

            if (this.Severity >= ActivePhaseSeverity && this.pawn.IsHashIntervalTick(ScarIntervalTicks))
            {
                ScarExternalParts();
            }

            if (this.Severity >= LateActivePhaseSeverity && this.pawn.IsHashIntervalTick(DeathCheckIntervalTicks) && Rand.Chance(LateActiveDeathChance))
            {
                this.pawn.Kill(null, this);
            }
        }

        private void ProgressRisk(int ticks)
        {
            float exposure = CurrentExposureSeverity;
            float riskPerDay = exposure * ExposureRiskPerDay;
            if (this.Severity >= ActivePhaseSeverity)
            {
                riskPerDay += CommittedRiskPerDay;
            }

            if (TreatmentEffectActive && riskPerDay > 0f)
            {
                riskPerDay *= TreatmentProgressFactor;
            }

            this.Severity = Mathf.Clamp(this.Severity + riskPerDay * ticks / GenDate.TicksPerDay, 0f, this.def.maxSeverity);
        }

        private void ScarExternalParts()
        {
            HediffDef scarDef = AcidBurnHediff;
            if (scarDef == null)
            {
                return;
            }

            List<BodyPartRecord> parts = this.pawn.health.hediffSet.GetNotMissingParts()
                .Where(part => part.depth == BodyPartDepth.Outside && !HasSweetGasScar(part))
                .ToList();
            if (parts.Count == 0)
            {
                return;
            }

            int scarsToAdd = Mathf.Min(ScarsPerInterval, parts.Count);
            for (int i = 0; i < scarsToAdd; i++)
            {
                BodyPartRecord partToScar = parts.RandomElementByWeight(part => Mathf.Max(part.coverageAbs, 0.01f));
                parts.Remove(partToScar);
                Hediff scar = HediffMaker.MakeHediff(scarDef, this.pawn, partToScar);
                scar.Severity = Rand.Range(1.20f, 1.70f);
                HediffComp_GetsPermanent permanentComp = scar.TryGetComp<HediffComp_GetsPermanent>();
                if (permanentComp != null)
                {
                    permanentComp.IsPermanent = true;
                }

                this.pawn.health.AddHediff(scar, partToScar);
            }
        }

        private bool HasSweetGasScar(BodyPartRecord part)
        {
            HediffDef scarDef = AcidBurnHediff;
            if (scarDef == null)
            {
                return false;
            }

            return this.pawn.health.hediffSet.hediffs.Any(hediff =>
                hediff.def == scarDef &&
                hediff.Part == part &&
                hediff.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent == true);
        }

        private float CurrentExposureSeverity
        {
            get
            {
                HediffDef exposureDef = ExposureHediff;
                if (exposureDef == null)
                {
                    return 0f;
                }

                Hediff exposure = this.pawn.health.hediffSet.GetFirstHediffOfDef(exposureDef);
                return exposure?.Severity ?? 0f;
            }
        }

        private bool TreatmentEffectActive
        {
            get
            {
                HediffComp_TendDuration tendComp = this.TryGetComp<HediffComp_TendDuration>();
                return tendComp != null && tendComp.IsTended;
            }
        }

        private static HediffDef ExposureHediff
        {
            get
            {
                if (exposureHediff == null)
                {
                    exposureHediff = DefDatabase<HediffDef>.GetNamedSilentFail("HD_SweetGasExposure");
                }

                return exposureHediff;
            }
        }

        private static HediffDef AcidBurnHediff
        {
            get
            {
                if (acidBurnHediff == null)
                {
                    acidBurnHediff = DefDatabase<HediffDef>.GetNamedSilentFail("AcidBurn");
                }

                return acidBurnHediff;
            }
        }
    }

    public class CompProperties_SweetGasCan : CompProperties
    {
        public float spawnChance = 1f;
        public float densityPerPulse = 0.75f;
        public float emissionRadius = 2.4f;
        public float edgeDensityFactor = 0.55f;
        public int burnDurationTicks = 1800;
        public int gasSimulationIntervalTicks = 30;
        public int spreadIntervalTicks = 600;
        public ThingDef gasDef;
        public bool destroyOnUse = true;

        public CompProperties_SweetGasCan()
        {
            compClass = typeof(CompSweetGasCan);
        }
    }

    public class CompSweetGasCan : ThingComp
    {
        private const string IgniteJobDefName = "HD_IgniteSweetGasCan";

        private bool used;
        private bool burning;
        private int burnEndTick;
        private int nextSpreadTick;

        public CompProperties_SweetGasCan Props => (CompProperties_SweetGasCan)props;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref used, "used", false);
            Scribe_Values.Look(ref burning, "burning", false);
            Scribe_Values.Look(ref burnEndTick, "burnEndTick", 0);
            Scribe_Values.Look(ref nextSpreadTick, "nextSpreadTick", 0);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!burning || parent.Map == null)
            {
                return;
            }

            if (parent.IsHashIntervalTick(60))
            {
                ThrowBurningEffect();
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame >= nextSpreadTick)
            {
                int simulationTicks = Mathf.Clamp(Props.gasSimulationIntervalTicks, 1, Props.spreadIntervalTicks);
                SpreadGas(simulationTicks);
                nextSpreadTick = ticksGame + simulationTicks;
            }

            if (ticksGame >= burnEndTick)
            {
                FinishBurning();
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn == null || selPawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            string label = "HD_SweetGasCan_Deploy_Label".Translate();
            if (used)
            {
                yield return new FloatMenuOption(label + ": " + (burning ? "HD_SweetGasCan_Deploy_Burning" : "HD_SweetGasCan_Deploy_Used").Translate(), null);
                yield break;
            }

            if (!selPawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                yield break;
            }

            if (!selPawn.CanReserve(parent))
            {
                yield return new FloatMenuOption(label + ": " + "Reserved".Translate(), null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(IgniteJobDefName);
                if (jobDef == null)
                {
                    Log.ErrorOnce("Helodrace: HD_IgniteSweetGasCan JobDef is missing.", 93214701);
                    return;
                }

                Job job = JobMaker.MakeJob(jobDef, parent);
                selPawn.jobs.TryTakeOrderedJob(job);
            });
        }

        public void Ignite(Pawn igniter)
        {
            if (used || parent.Map == null || Props.gasDef == null)
            {
                return;
            }

            used = true;
            burning = true;
            burnEndTick = Find.TickManager.TicksGame + Props.burnDurationTicks;
            nextSpreadTick = Find.TickManager.TicksGame;
            ThrowIgnitionEffect();
            Messages.Message("HD_SweetGasCan_Deploy_Message".Translate(igniter?.LabelShort ?? parent.LabelShort, parent.LabelShort, Props.emissionRadius.ToString("F0")), parent, MessageTypeDefOf.NegativeEvent);
        }

        private void SpreadGas(int simulationTicks)
        {
            if (parent.Map == null || Props.gasDef == null)
            {
                return;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.emissionRadius, true))
            {
                if (!CanGasOccupy(cell))
                {
                    continue;
                }

                float distanceFactor = Mathf.InverseLerp(Props.emissionRadius, 0f, parent.Position.DistanceTo(cell));
                float densityFactor = Mathf.Lerp(Props.edgeDensityFactor, 1f, distanceFactor);
                float simulationFactor = Props.spreadIntervalTicks > 0 ? simulationTicks / (float)Props.spreadIntervalTicks : 1f;
                Gas_SweetGas.AddGasAt(cell, parent.Map, Props.gasDef, Props.densityPerPulse * densityFactor * Props.spawnChance * simulationFactor);
            }
        }

        private bool CanGasOccupy(IntVec3 cell)
        {
            if (parent.Map == null || !cell.InBounds(parent.Map))
            {
                return false;
            }

            MapComponent_SweetGasGrid gasGrid = parent.Map.GetComponent<MapComponent_SweetGasGrid>();
            return gasGrid?.CanGasOccupy(cell) ?? cell.Standable(parent.Map);
        }

        private void FinishBurning()
        {
            burning = false;
            if (Props.destroyOnUse && !parent.Destroyed)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        private void ThrowIgnitionEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 2.0f);
            FleckMaker.ThrowMicroSparks(loc, parent.Map);
            FleckMaker.ThrowSmoke(loc, parent.Map, 1.4f);
        }

        private void ThrowBurningEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 1.25f);
            FleckMaker.ThrowSmoke(loc, parent.Map, 0.7f);
        }
    }

    public class JobDriver_IgniteSweetGasCan : JobDriver
    {
        private const TargetIndex CanInd = TargetIndex.A;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(CanInd);
            this.FailOnBurningImmobile(CanInd);
            this.FailOn(() => TargetThingA.TryGetComp<CompSweetGasCan>() == null);

            yield return Toils_Goto.GotoThing(CanInd, PathEndMode.Touch);

            Toil ignite = Toils_General.Wait(120);
            ignite.WithProgressBarToilDelay(CanInd);
            ignite.FailOnCannotTouch(CanInd, PathEndMode.Touch);
            yield return ignite;

            yield return new Toil
            {
                initAction = delegate
                {
                    Thing thing = job.GetTarget(CanInd).Thing;
                    thing?.TryGetComp<CompSweetGasCan>()?.Ignite(pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(CanInd), job, 1, -1, null, errorOnFailed);
        }
    }
}
