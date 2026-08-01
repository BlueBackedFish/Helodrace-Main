using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class MapComponent_CSGasGrid : MapComponent
    {
        private const float MinDensity = 0.025f;
        private const float PropagationFalloff = 0.52f;
        private const float DiagonalFactor = 0.88f;
        private const float SourceDissipationPerTick = 0.00045f;
        private const int SimulationIntervalTicks = 30;
        private const int ExposureIntervalTicks = 120;
        private const int VisualIntervalTicks = 18;
        private const int MaxVisualSamplesPerPulse = 24;
        private const float SeverityPerTickAtFullDensity = 0.12f / 240f;

        private Dictionary<int, float> sources = new Dictionary<int, float>();
        private Dictionary<int, float> densities = new Dictionary<int, float>();
        private Dictionary<int, Gas_CS> markers = new Dictionary<int, Gas_CS>();
        private readonly List<int> tmpIndices = new List<int>();
        private readonly Queue<PropagationNode> frontier = new Queue<PropagationNode>();
        private ThingDef gasDef;

        public MapComponent_CSGasGrid(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sources, "csGasSources", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sources = sources ?? new Dictionary<int, float>();
                densities = new Dictionary<int, float>();
                markers = new Dictionary<int, Gas_CS>();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            gasDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_CSGas");
            RecomputeDensity();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int tick = Find.TickManager.TicksGame + map.Index * 11;
            if (tick % VisualIntervalTicks == 0)
            {
                ThrowAmbientFlecks();
            }

            if (tick % SimulationIntervalTicks != 0)
            {
                return;
            }

            DecaySources();
            RecomputeDensity();
            if (tick % ExposureIntervalTicks == 0)
            {
                ApplyExposure();
            }
        }

        public bool AddGas(IntVec3 cell, ThingDef def, float amount)
        {
            if (def == null || amount <= 0f || !CanOccupy(cell))
            {
                return false;
            }

            gasDef = def;
            int index = map.cellIndices.CellToIndex(cell);
            sources.TryGetValue(index, out float current);
            sources[index] = Mathf.Clamp01(current + amount);
            return true;
        }

        public float DensityAt(IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return 0f;
            }

            densities.TryGetValue(map.cellIndices.CellToIndex(cell), out float density);
            return density;
        }

        private void DecaySources()
        {
            tmpIndices.Clear();
            tmpIndices.AddRange(sources.Keys);
            float decay = SourceDissipationPerTick * SimulationIntervalTicks;
            for (int i = 0; i < tmpIndices.Count; i++)
            {
                int index = tmpIndices[i];
                float next = sources[index] - decay;
                if (next <= MinDensity)
                {
                    sources.Remove(index);
                }
                else
                {
                    sources[index] = next;
                }
            }
        }

        private void RecomputeDensity()
        {
            Dictionary<int, float> nextDensities = new Dictionary<int, float>();
            foreach (KeyValuePair<int, float> source in sources)
            {
                IntVec3 sourceCell = map.cellIndices.IndexToCell(source.Key);
                AddDensity(nextDensities, source.Key, source.Value);
                frontier.Enqueue(new PropagationNode(sourceCell, source.Value));

                while (frontier.Count > 0)
                {
                    PropagationNode node = frontier.Dequeue();
                    for (int x = -1; x <= 1; x++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            if (x == 0 && z == 0)
                            {
                                continue;
                            }

                            IntVec3 nextCell = node.Cell + new IntVec3(x, 0, z);
                            if (!CanOccupy(nextCell) || (x != 0 && z != 0 && !CanMoveDiagonally(node.Cell, x, z)))
                            {
                                continue;
                            }

                            float nextDensity = node.Density * PropagationFalloff * (x != 0 && z != 0 ? DiagonalFactor : 1f);
                            int nextIndex = map.cellIndices.CellToIndex(nextCell);
                            nextDensities.TryGetValue(nextIndex, out float previous);
                            if (nextDensity <= MinDensity || nextDensity <= previous + 0.008f)
                            {
                                continue;
                            }

                            nextDensities[nextIndex] = nextDensity;
                            frontier.Enqueue(new PropagationNode(nextCell, nextDensity));
                        }
                    }
                }
            }

            densities = nextDensities;
            UpdateMarkers();
        }

        private void UpdateMarkers()
        {
            tmpIndices.Clear();
            tmpIndices.AddRange(markers.Keys);
            for (int i = 0; i < tmpIndices.Count; i++)
            {
                int index = tmpIndices[i];
                if (!densities.ContainsKey(index))
                {
                    Gas_CS marker = markers[index];
                    if (marker != null && !marker.Destroyed)
                    {
                        marker.Destroy(DestroyMode.Vanish);
                    }
                    markers.Remove(index);
                }
            }

            if (gasDef == null)
            {
                return;
            }

            foreach (int index in densities.Keys)
            {
                if (markers.TryGetValue(index, out Gas_CS marker) && marker != null && !marker.Destroyed)
                {
                    continue;
                }

                IntVec3 cell = map.cellIndices.IndexToCell(index);
                marker = null;
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing is Gas_CS existing)
                    {
                        marker = existing;
                        break;
                    }
                }

                if (marker == null)
                {
                    marker = ThingMaker.MakeThing(gasDef) as Gas_CS;
                    if (marker != null)
                    {
                        GenSpawn.Spawn(marker, cell, map);
                    }
                }
                markers[index] = marker;
            }
        }

        private void ApplyExposure()
        {
            foreach (KeyValuePair<int, float> entry in densities)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(entry.Key);
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn pawn)
                    {
                        Gas_CS.ApplyExposureTo(pawn, entry.Value, SeverityPerTickAtFullDensity * ExposureIntervalTicks);
                    }
                }
            }
        }

        private void ThrowAmbientFlecks()
        {
            if (densities.Count == 0)
            {
                return;
            }

            tmpIndices.Clear();
            tmpIndices.AddRange(densities.Keys);
            int sampleCount = Mathf.Min(
                MaxVisualSamplesPerPulse,
                Mathf.Max(2, tmpIndices.Count / 5 + 1));

            for (int i = 0; i < sampleCount; i++)
            {
                int index = tmpIndices[Rand.Range(0, tmpIndices.Count)];
                float density = densities[index];
                IntVec3 cell = map.cellIndices.IndexToCell(index);
                if (density <= MinDensity || !cell.ShouldSpawnMotesAt(map))
                {
                    continue;
                }

                int fleckCount = density >= 0.65f ? 3 : density >= 0.28f ? 2 : 1;
                float scale = Mathf.Lerp(0.75f, 2.6f, density);
                for (int j = 0; j < fleckCount; j++)
                {
                    Vector3 location = cell.ToVector3Shifted();
                    location.x += Rand.Range(-0.32f, 0.32f);
                    location.z += Rand.Range(-0.32f, 0.32f);
                    FleckMaker.Static(location, map, FleckDefOf.Smoke, scale * Rand.Range(0.85f, 1.15f));
                }
            }
        }

        private bool CanOccupy(IntVec3 cell)
        {
            return cell.InBounds(map) && cell.Standable(map);
        }

        private bool CanMoveDiagonally(IntVec3 origin, int x, int z)
        {
            return CanOccupy(origin + new IntVec3(x, 0, 0)) &&
                CanOccupy(origin + new IntVec3(0, 0, z));
        }

        private static void AddDensity(Dictionary<int, float> values, int index, float density)
        {
            values.TryGetValue(index, out float previous);
            if (density > previous)
            {
                values[index] = density;
            }
        }

        private struct PropagationNode
        {
            public readonly IntVec3 Cell;
            public readonly float Density;

            public PropagationNode(IntVec3 cell, float density)
            {
                Cell = cell;
                Density = density;
            }
        }
    }

    public sealed class Gas_CS : ThingWithComps
    {
        private const int MaterialBuckets = 8;
        private static Material[] materials;
        private static HediffDef exposureHediff;

        public static bool AddGasAt(IntVec3 cell, Map map, ThingDef gasDef, float density)
        {
            return map?.GetComponent<MapComponent_CSGasGrid>()?.AddGas(cell, gasDef, density) ?? false;
        }

        public float DensityPercent => Spawned && Map != null
            ? Map.GetComponent<MapComponent_CSGasGrid>()?.DensityAt(Position) ?? 0f
            : 0f;

        public override string LabelMouseover => $"{base.LabelMouseover} ({DensityPercent * 100f:F0}%)";

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float density = DensityPercent;
            if (density <= 0f)
            {
                return;
            }

            drawLoc.y = AltitudeLayer.Gas.AltitudeFor();
            int bucket = Mathf.Clamp(Mathf.CeilToInt(density * MaterialBuckets) - 1, 0, MaterialBuckets - 1);
            materials = materials ?? new Material[MaterialBuckets];
            if (materials[bucket] == null)
            {
                float strength = (bucket + 1f) / MaterialBuckets;
                materials[bucket] = new Material(ShaderDatabase.Transparent)
                {
                    mainTexture = BaseContent.WhiteTex,
                    color = new Color(0.76f, 0.78f, 0.55f, Mathf.Lerp(0.10f, 0.34f, strength))
                };
            }

            float size = Mathf.Lerp(1.15f, 2.25f, density) / 10f;
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(drawLoc, Quaternion.identity, new Vector3(size, 1f, size)),
                materials[bucket],
                0);
        }

        internal static void ApplyExposureTo(Pawn pawn, float density, float severityAtFullDensity)
        {
            if (pawn?.health == null || pawn.Dead || pawn.RaceProps?.IsFlesh != true)
            {
                return;
            }

            if (pawn.RaceProps.Humanlike && !GasUtility.IsAffectedByExposure(pawn))
            {
                return;
            }

            float resistance = pawn.GetStatValue(StatDefOf.ToxicEnvironmentResistance);
            if (resistance >= 1f)
            {
                return;
            }

            exposureHediff = exposureHediff ?? DefDatabase<HediffDef>.GetNamedSilentFail("HD_CSGasExposure");
            if (exposureHediff == null)
            {
                return;
            }

            float gain = severityAtFullDensity * Mathf.Clamp01(density) * Mathf.Clamp01(1f - resistance);
            Hediff exposure = pawn.health.hediffSet.GetFirstHediffOfDef(exposureHediff);
            if (exposure is Hediff_CSGasExposure csExposure)
            {
                csExposure.AddDose(gain);
                return;
            }

            if (exposure != null)
            {
                exposure.Severity = Mathf.Min(exposureHediff.maxSeverity, exposure.Severity + gain);
                return;
            }

            Hediff newExposure = HediffMaker.MakeHediff(exposureHediff, pawn);
            if (newExposure is Hediff_CSGasExposure newCSExposure)
            {
                newCSExposure.AddDose(gain);
            }
            else
            {
                newExposure.Severity = gain;
            }
            pawn.health.AddHediff(newExposure);
        }
    }

    public sealed class Hediff_CSGasExposure : HediffWithComps
    {
        private const int RecoveryDelayTicks = 150;
        private const float RecoveryPerTick = 0.00035f;

        private int lastExposureTick = -1;

        public override bool ShouldRemove => base.ShouldRemove || Severity <= 0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastExposureTick, "lastCSExposureTick", -1);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (lastExposureTick < 0)
            {
                lastExposureTick = Find.TickManager.TicksGame;
                return;
            }

            if (Find.TickManager.TicksGame - lastExposureTick >= RecoveryDelayTicks)
            {
                Severity = Mathf.Max(0f, Severity - RecoveryPerTick * delta);
            }
        }

        public void AddDose(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            Severity = Mathf.Min(def.maxSeverity, Severity + amount);
            lastExposureTick = Find.TickManager.TicksGame;
        }
    }
}
