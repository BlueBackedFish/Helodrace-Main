using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public abstract class HelodGasWorker
    {
        public abstract void Apply(Pawn pawn, float density, float severityAtFullDensity);

        public virtual void CellTick(IntVec3 cell, Map map, float density)
        {
        }
    }

    public sealed class HelodGasWorker_Photochlorogen : HelodGasWorker
    {
        public override void Apply(Pawn pawn, float density, float severityAtFullDensity)
        {
            Gas_Photochlorogen.ApplyExposureTo(pawn, density, severityAtFullDensity);
        }
    }

    public sealed class HelodGasWorker_SweetGas : HelodGasWorker
    {
        public override void Apply(Pawn pawn, float density, float severityAtFullDensity)
        {
            Gas_SweetGas.ApplyExposureTo(pawn, density, severityAtFullDensity);
        }
    }

    public sealed class HelodGasWorker_CS : HelodGasWorker
    {
        public override void Apply(Pawn pawn, float density, float severityAtFullDensity)
        {
            ModernWar.Gas_CS.ApplyExposureTo(pawn, density, severityAtFullDensity);
        }
    }

    public sealed class HelodGasDef : Def
    {
        public Color color = new Color(1f, 1f, 1f, 0.35f);
        public string texPath = "Things/Gas/GasCloudThickA";
        public int dissipationRate = 4;
        public bool diffuses = true;
        public int diffusionThreshold = 17;
        public int diffusionTransferDivisor = 2;
        public bool equalizesThroughVents = true;
        public int minDensityForEffect = 5;
        public float severityPerTickAtFullDensity;
        public float accuracyFactor = 1f;
        public Type workerClass;

        [Unsaved]
        private HelodGasWorker worker;

        [Unsaved]
        private Material material;

        public HelodGasWorker Worker
        {
            get
            {
                if (worker == null && workerClass != null)
                {
                    worker = (HelodGasWorker)Activator.CreateInstance(workerClass);
                }

                return worker;
            }
        }

        public Material Material
        {
            get
            {
                if (material == null)
                {
                    Color materialColor = new Color(color.r, color.g, color.b, 1f);
                    material = MaterialPool.MatFrom(
                        ContentFinder<Texture2D>.Get(texPath),
                        ShaderDatabase.Transparent,
                        materialColor,
                        3000);
                }

                return material;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (workerClass != null && !typeof(HelodGasWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from HelodGasWorker";
            }
            if (dissipationRate < 0 || dissipationRate > byte.MaxValue)
            {
                yield return "dissipationRate must be between 0 and 255";
            }
            if (diffusionThreshold < 1 || diffusionThreshold > byte.MaxValue)
            {
                yield return "diffusionThreshold must be between 1 and 255";
            }
            if (diffusionTransferDivisor < 2 || diffusionTransferDivisor > 16)
            {
                yield return "diffusionTransferDivisor must be between 2 and 16";
            }
            if (accuracyFactor <= 0f || accuracyFactor > 1f)
            {
                yield return "accuracyFactor must be greater than 0 and no greater than 1";
            }
        }
    }

    [DefOf]
    public static class HelodGasDefOf
    {
        public static HelodGasDef HD_PhotochlorogenGasGrid;
        public static HelodGasDef HD_SweetGasGrid;
        public static HelodGasDef HD_CSGasGrid;
        public static HelodGasDef HD_WhitePhosphorusSmokeGrid;

        static HelodGasDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HelodGasDefOf));
        }
    }

    internal static class HelodGasStore
    {
        private static readonly ConditionalWeakTable<Map, HelodGasState> States =
            new ConditionalWeakTable<Map, HelodGasState>();

        public static HelodGasState For(Map map)
        {
            return map == null ? null : States.GetValue(map, value => new HelodGasState(value));
        }

        public static bool TryGet(Map map, out HelodGasState state)
        {
            if (map == null)
            {
                state = null;
                return false;
            }

            return States.TryGetValue(map, out state);
        }

        public static bool AddGas(IntVec3 cell, Map map, HelodGasDef gas, float normalizedAmount)
        {
            if (map == null || gas == null || normalizedAmount <= 0f || !cell.InBounds(map))
            {
                return false;
            }

            int amount = Mathf.Max(1, Mathf.RoundToInt(normalizedAmount * byte.MaxValue));
            // Existing Helod emitters already enumerate every target cell and used
            // clamped source strengths. Clamp here as well; diffusion performs the
            // outward spread without turning repeated canister pulses into a large
            // overflow flood-fill.
            return For(map).AddGas(cell, gas, amount, false);
        }

        public static bool AddGasRadius(IntVec3 cell, Map map, HelodGasDef gas, float radius)
        {
            if (map == null || gas == null || radius <= 0f || !cell.InBounds(map))
            {
                return false;
            }

            int amount = byte.MaxValue * GenRadial.NumCellsInRadius(radius);
            return For(map).AddGas(cell, gas, amount, true);
        }

        public static float DensityPercentAt(IntVec3 cell, Map map, HelodGasDef gas)
        {
            if (map == null || gas == null || !cell.InBounds(map) || !TryGet(map, out HelodGasState state))
            {
                return 0f;
            }

            return state.DensityAt(cell, gas) / (float)byte.MaxValue;
        }

        public static bool GasCanMoveTo(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }

            Building edifice = cell.GetEdifice(map);
            if (edifice == null || edifice.def.Fillage != FillCategory.Full)
            {
                return true;
            }

            Building_Door door = edifice as Building_Door;
            return door != null && door.Open;
        }
    }

    internal sealed class HelodGasState
    {
        private const float CellsToDissipatePerTickFactor = 1f / 64f;
        private const float CellsToDiffusePerTickFactor = 1f / 32f;
        private const float RoofedDissipationFactor = 0.5f;
        private const float MaxOverflowFloodfillRadius = 40f;
        private const int EffectIntervalTicks = 50;

        private static readonly IntVec3[] CardinalDirections =
        {
            IntVec3.North,
            IntVec3.East,
            IntVec3.South,
            IntVec3.West
        };

        private readonly Map map;
        private readonly List<HelodGasDef> defs;
        private byte[][] grids;
        private int[] activeCellCounts;
        private int totalActiveCells;
        private int cycleIndexDissipation;
        private int cycleIndexDiffusion;

        public bool HasGas => totalActiveCells > 0;

        public bool HasAccuracyAffectingGas
        {
            get
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    if (activeCellCounts[i] > 0 && defs[i].accuracyFactor < 1f)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public HelodGasState(Map map)
        {
            this.map = map;
            defs = DefDatabase<HelodGasDef>.AllDefsListForReading;
            grids = new byte[defs.Count][];
            activeCellCounts = new int[defs.Count];
            int cellCount = map.cellIndices.NumGridCells;
            for (int i = 0; i < grids.Length; i++)
            {
                grids[i] = new byte[cellCount];
            }

            cycleIndexDiffusion = map.Index * 7919 % Math.Max(1, map.Area);
        }

        public byte DensityAt(IntVec3 cell, HelodGasDef gas)
        {
            return grids[gas.index][map.cellIndices.CellToIndex(cell)];
        }

        public bool AnyGasAt(IntVec3 cell)
        {
            int index = map.cellIndices.CellToIndex(cell);
            for (int i = 0; i < grids.Length; i++)
            {
                if (grids[i][index] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        public float AccuracyFactorAlong(ShootLine line)
        {
            if (!HasGas)
            {
                return 1f;
            }

            float result = 1f;
            foreach (IntVec3 cell in line.Points())
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                int cellIndex = map.cellIndices.CellToIndex(cell);
                for (int i = 0; i < defs.Count; i++)
                {
                    HelodGasDef gas = defs[i];
                    if (gas.accuracyFactor < result && grids[i][cellIndex] != 0)
                    {
                        result = gas.accuracyFactor;
                    }
                }
            }

            return result;
        }

        public bool AddGas(IntVec3 cell, HelodGasDef gas, int amount, bool canOverflow = true)
        {
            if (amount <= 0 || !HelodGasStore.GasCanMoveTo(cell, map))
            {
                return false;
            }

            int gasIndex = gas.index;
            int cellIndex = map.cellIndices.CellToIndex(cell);
            int combined = grids[gasIndex][cellIndex] + amount;
            int overflow = Mathf.Max(0, combined - byte.MaxValue);
            SetDensity(gasIndex, cellIndex, (byte)Mathf.Min(byte.MaxValue, combined));
            if (canOverflow && overflow > 0)
            {
                Overflow(cell, gas, overflow);
            }

            return true;
        }

        public void Tick()
        {
            if (!HasGas)
            {
                return;
            }

            int area = map.Area;
            List<IntVec3> cells = map.cellsInRandomOrder.GetAll();
            int dissipateCount = Mathf.CeilToInt(area * CellsToDissipatePerTickFactor);
            for (int i = 0; i < dissipateCount; i++)
            {
                if (cycleIndexDissipation >= area)
                {
                    cycleIndexDissipation = 0;
                }

                TryDissipate(cells[cycleIndexDissipation]);
                cycleIndexDissipation++;
            }

            int diffuseCount = Mathf.CeilToInt(area * CellsToDiffusePerTickFactor);
            for (int i = 0; i < diffuseCount; i++)
            {
                if (cycleIndexDiffusion >= area)
                {
                    cycleIndexDiffusion = 0;
                }

                TryDiffuse(cells[cycleIndexDiffusion]);
                cycleIndexDiffusion++;
            }
        }

        public void ApplyEffects(Pawn pawn)
        {
            if (!HasGas || pawn == null || !pawn.Spawned || pawn.Dead)
            {
                return;
            }

            int cellIndex = map.cellIndices.CellToIndex(pawn.Position);
            for (int i = 0; i < defs.Count; i++)
            {
                byte density = grids[i][cellIndex];
                HelodGasDef gas = defs[i];
                if (density < gas.minDensityForEffect || gas.Worker == null)
                {
                    continue;
                }

                gas.Worker.Apply(
                    pawn,
                    density / (float)byte.MaxValue,
                    gas.severityPerTickAtFullDensity * EffectIntervalTicks);
            }
        }

        public void NotifyThingSpawned(Thing thing)
        {
            if (!HasGas || thing == null || !thing.Spawned || thing.def.Fillage != FillCategory.Full)
            {
                return;
            }

            foreach (IntVec3 cell in thing.OccupiedRect())
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                int index = map.cellIndices.CellToIndex(cell);
                for (int i = 0; i < grids.Length; i++)
                {
                    if (grids[i][index] != 0)
                    {
                        SetDensity(i, index, 0);
                    }
                }
            }
        }

        public void EqualizeGasThroughBuilding(Building building, bool twoWay)
        {
            if (!HasGas || building == null || !building.Spawned)
            {
                return;
            }

            IntVec3[] cells = new IntVec3[4];
            int count = 0;
            if (twoWay)
            {
                AddEqualizationCell(building.Position + building.Rotation.FacingCell, cells, ref count);
                AddEqualizationCell(building.Position - building.Rotation.FacingCell, cells, ref count);
            }
            else
            {
                for (int i = 0; i < CardinalDirections.Length; i++)
                {
                    AddEqualizationCell(building.Position + CardinalDirections[i], cells, ref count);
                }
            }

            if (count <= 1)
            {
                return;
            }

            for (int gasIndex = 0; gasIndex < defs.Count; gasIndex++)
            {
                if (!defs[gasIndex].equalizesThroughVents || activeCellCounts[gasIndex] == 0)
                {
                    continue;
                }

                int total = 0;
                for (int i = 0; i < count; i++)
                {
                    total += grids[gasIndex][map.cellIndices.CellToIndex(cells[i])];
                }

                int average = total / count;
                int remainder = total % count;
                for (int i = 0; i < count; i++)
                {
                    SetDensity(
                        gasIndex,
                        map.cellIndices.CellToIndex(cells[i]),
                        (byte)(average + (i < remainder ? 1 : 0)));
                }
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < grids.Length; i++)
            {
                Array.Clear(grids[i], 0, grids[i].Length);
                activeCellCounts[i] = 0;
            }

            totalActiveCells = 0;
            map.mapDrawer.WholeMapChanged(MapMeshFlagDefOf.Gas);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref cycleIndexDissipation, "cycleIndexDissipation", 0);
            Scribe_Values.Look(ref cycleIndexDiffusion, "cycleIndexDiffusion", 0);
            int cellCount = map.cellIndices.NumGridCells;
            for (int i = 0; i < defs.Count; i++)
            {
                DataExposeUtility.LookByteArray(ref grids[i], "density_" + defs[i].defName);
                if (grids[i] == null || grids[i].Length != cellCount)
                {
                    grids[i] = new byte[cellCount];
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RecalculateActiveCounts();
                if (HasGas)
                {
                    map.mapDrawer.WholeMapChanged(MapMeshFlagDefOf.Gas);
                }
            }
        }

        private void TryDissipate(IntVec3 cell)
        {
            int index = map.cellIndices.CellToIndex(cell);
            float factor = cell.Roofed(map) ? RoofedDissipationFactor : 1f;
            for (int i = 0; i < defs.Count; i++)
            {
                byte density = grids[i][index];
                if (density == 0)
                {
                    continue;
                }

                HelodGasDef gas = defs[i];
                if (density >= gas.minDensityForEffect && gas.Worker != null)
                {
                    gas.Worker.CellTick(cell, map, density / (float)byte.MaxValue);
                }

                if (gas.dissipationRate > 0)
                {
                    int removal = Mathf.Max(1, Mathf.RoundToInt(gas.dissipationRate * factor));
                    SetDensity(i, index, (byte)Mathf.Max(0, density - removal));
                }
            }
        }

        private void TryDiffuse(IntVec3 cell)
        {
            int index = map.cellIndices.CellToIndex(cell);
            for (int gasIndex = 0; gasIndex < defs.Count; gasIndex++)
            {
                HelodGasDef gas = defs[gasIndex];
                if (!gas.diffuses || activeCellCounts[gasIndex] == 0)
                {
                    continue;
                }

                int sourceDensity = grids[gasIndex][index];
                if (sourceDensity < gas.diffusionThreshold)
                {
                    continue;
                }

                int directionOffset = (index + cycleIndexDiffusion + gasIndex * 3) & 3;
                for (int d = 0; d < CardinalDirections.Length; d++)
                {
                    IntVec3 neighbor = cell + CardinalDirections[(d + directionOffset) & 3];
                    if (!HelodGasStore.GasCanMoveTo(neighbor, map))
                    {
                        continue;
                    }

                    int neighborIndex = map.cellIndices.CellToIndex(neighbor);
                    int neighborDensity = grids[gasIndex][neighborIndex];
                    int delta = sourceDensity - neighborDensity;
                    if (delta < gas.diffusionThreshold)
                    {
                        continue;
                    }

                    int transfer = Mathf.Max(1, delta / gas.diffusionTransferDivisor);
                    sourceDensity -= transfer;
                    SetDensity(gasIndex, neighborIndex, (byte)(neighborDensity + transfer));
                    if (sourceDensity < gas.diffusionThreshold)
                    {
                        break;
                    }
                }

                SetDensity(gasIndex, index, (byte)sourceDensity);
            }
        }

        private void Overflow(IntVec3 origin, HelodGasDef gas, int amount)
        {
            int remaining = amount;
            map.floodFiller.FloodFill(
                origin,
                cell => HelodGasStore.GasCanMoveTo(cell, map),
                delegate(IntVec3 cell)
                {
                    int space = byte.MaxValue - DensityAt(cell, gas);
                    int added = Mathf.Min(space, remaining);
                    if (added > 0)
                    {
                        AddGas(cell, gas, added, false);
                        remaining -= added;
                    }

                    return remaining <= 0;
                },
                GenRadial.NumCellsInRadius(MaxOverflowFloodfillRadius),
                true);
        }

        private void AddEqualizationCell(IntVec3 cell, IntVec3[] cells, ref int count)
        {
            if (count < cells.Length && HelodGasStore.GasCanMoveTo(cell, map))
            {
                cells[count++] = cell;
            }
        }

        private void SetDensity(int gasIndex, int cellIndex, byte value)
        {
            byte previous = grids[gasIndex][cellIndex];
            if (previous == value)
            {
                return;
            }

            grids[gasIndex][cellIndex] = value;
            if (previous == 0 && value != 0)
            {
                activeCellCounts[gasIndex]++;
                totalActiveCells++;
            }
            else if (previous != 0 && value == 0)
            {
                activeCellCounts[gasIndex]--;
                totalActiveCells--;
            }

            if (previous == 0 || value == 0 || (previous >> 4) != (value >> 4))
            {
                map.mapDrawer.MapMeshDirty(map.cellIndices.IndexToCell(cellIndex), MapMeshFlagDefOf.Gas);
            }
        }

        private void RecalculateActiveCounts()
        {
            Array.Clear(activeCellCounts, 0, activeCellCounts.Length);
            totalActiveCells = 0;
            for (int gasIndex = 0; gasIndex < grids.Length; gasIndex++)
            {
                byte[] grid = grids[gasIndex];
                for (int i = 0; i < grid.Length; i++)
                {
                    if (grid[i] != 0)
                    {
                        activeCellCounts[gasIndex]++;
                        totalActiveCells++;
                    }
                }
            }
        }
    }

    public sealed class SectionLayer_HelodGas : SectionLayer
    {
        public SectionLayer_HelodGas(Section section) : base(section)
        {
            relevantChangeTypes = MapMeshFlagDefOf.Gas;
        }

        public override bool Visible => DebugViewSettings.drawGas;

        public override void Regenerate()
        {
            ClearSubMeshes(MeshParts.All);
            if (!HelodGasStore.TryGet(Map, out HelodGasState state) || !state.HasGas)
            {
                return;
            }

            List<HelodGasDef> defs = DefDatabase<HelodGasDef>.AllDefsListForReading;
            float altitude = AltitudeLayer.Gas.AltitudeFor() + 0.002f;
            foreach (IntVec3 cell in section.CellRect)
            {
                if (!state.AnyGasAt(cell))
                {
                    continue;
                }

                for (int i = 0; i < defs.Count; i++)
                {
                    byte density = state.DensityAt(cell, defs[i]);
                    if (density == 0)
                    {
                        continue;
                    }

                    LayerSubMesh subMesh = GetSubMesh(defs[i].Material);
                    AddCell(cell, defs[i], density, subMesh, altitude);
                }
            }

            FinalizeMesh(MeshParts.All);
        }

        private void AddCell(IntVec3 cell, HelodGasDef gas, byte density, LayerSubMesh subMesh, float altitude)
        {
            int mapIndex = Map.cellIndices.CellToIndex(cell);
            uint hash = (uint)(mapIndex * 747796405) + (uint)(gas.index * 2891336453u);
            hash ^= hash >> 16;
            float scaleOffset = 0.4f + (hash & 255u) / 1275f;
            float xOffset = ((hash >> 8) & 255u) / 637.5f - 0.2f;
            float zOffset = ((hash >> 16) & 255u) / 637.5f - 0.2f;
            float alpha = Mathf.Lerp(0.16f, gas.color.a, density / (float)byte.MaxValue);
            Color vertexColor = new Color(1f, 1f, 1f, alpha);

            float x1 = cell.x - scaleOffset + xOffset;
            float x2 = cell.x + 1f + scaleOffset + xOffset;
            float z1 = cell.z - scaleOffset + zOffset;
            float z2 = cell.z + 1f + scaleOffset + zOffset;
            int start = subMesh.verts.Count;
            subMesh.verts.Add(new Vector3(x1, altitude, z1));
            subMesh.verts.Add(new Vector3(x1, altitude, z2));
            subMesh.verts.Add(new Vector3(x2, altitude, z2));
            subMesh.verts.Add(new Vector3(x2, altitude, z1));
            subMesh.uvs.Add(new Vector3(0f, 0f, mapIndex));
            subMesh.uvs.Add(new Vector3(0f, 1f, mapIndex));
            subMesh.uvs.Add(new Vector3(1f, 1f, mapIndex));
            subMesh.uvs.Add(new Vector3(1f, 0f, mapIndex));
            for (int i = 0; i < 4; i++)
            {
                subMesh.colors.Add(vertexColor);
            }
            subMesh.tris.Add(start);
            subMesh.tris.Add(start + 1);
            subMesh.tris.Add(start + 2);
            subMesh.tris.Add(start);
            subMesh.tris.Add(start + 2);
            subMesh.tris.Add(start + 3);
        }
    }

    [HarmonyPatch(typeof(GasGrid), nameof(GasGrid.Tick))]
    internal static class Patch_HelodGasGrid_Tick
    {
        private static void Postfix(Map ___map)
        {
            if (HelodGasStore.TryGet(___map, out HelodGasState state))
            {
                state.Tick();
            }
        }
    }

    [HarmonyPatch(typeof(GasGrid), nameof(GasGrid.ExposeData))]
    internal static class Patch_HelodGasGrid_ExposeData
    {
        private static void Postfix(Map ___map)
        {
            HelodGasState state;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (!HelodGasStore.TryGet(___map, out state) || !state.HasGas)
                {
                    return;
                }
            }

            if (!Scribe.EnterNode("helodCustomGasGrid"))
            {
                return;
            }

            try
            {
                state = HelodGasStore.For(___map);
                state.ExposeData();
            }
            finally
            {
                Scribe.ExitNode();
            }
        }
    }

    [HarmonyPatch(typeof(GasGrid), nameof(GasGrid.Notify_ThingSpawned))]
    internal static class Patch_HelodGasGrid_NotifyThingSpawned
    {
        private static void Postfix(Thing thing, Map ___map)
        {
            if (HelodGasStore.TryGet(___map, out HelodGasState state))
            {
                state.NotifyThingSpawned(thing);
            }
        }
    }

    [HarmonyPatch(typeof(GasGrid), nameof(GasGrid.EqualizeGasThroughBuilding))]
    internal static class Patch_HelodGasGrid_Equalize
    {
        private static void Postfix(Building b, bool twoWay, Map ___map)
        {
            if (HelodGasStore.TryGet(___map, out HelodGasState state))
            {
                state.EqualizeGasThroughBuilding(b, twoWay);
            }
        }
    }

    [HarmonyPatch(typeof(GasGrid), nameof(GasGrid.Debug_ClearAll))]
    internal static class Patch_HelodGasGrid_ClearAll
    {
        private static void Postfix(Map ___map)
        {
            if (HelodGasStore.TryGet(___map, out HelodGasState state))
            {
                state.ClearAll();
            }
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    internal static class Patch_HelodGas_ShotReport
    {
        private static readonly FieldInfo FactorFromCoveringGasField =
            AccessTools.Field(typeof(ShotReport), "factorFromCoveringGas");
        private static readonly FieldInfo ShootLineField =
            AccessTools.Field(typeof(ShotReport), "shootLine");

        private static void Postfix(Thing caster, ref ShotReport __result)
        {
            if (caster?.Map == null || FactorFromCoveringGasField == null || ShootLineField == null ||
                !HelodGasStore.TryGet(caster.Map, out HelodGasState state) || !state.HasAccuracyAffectingGas)
            {
                return;
            }

            object boxedReport = __result;
            ShootLine line = (ShootLine)ShootLineField.GetValue(boxedReport);
            float currentFactor = (float)FactorFromCoveringGasField.GetValue(boxedReport);
            float customGasFactor = state.AccuracyFactorAlong(line);
            if (customGasFactor >= currentFactor)
            {
                return;
            }

            FactorFromCoveringGasField.SetValue(boxedReport, customGasFactor);
            __result = (ShotReport)boxedReport;
        }
    }

    [HarmonyPatch]
    internal static class Patch_HelodGasEffects_1_6
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GasUtility), "PawnGasEffectsTickInterval");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(Pawn pawn, int delta)
        {
            if (pawn == null || !pawn.Spawned || !Gen.IsHashIntervalTick(pawn, 50, delta))
            {
                return;
            }

            if (HelodGasStore.TryGet(pawn.Map, out HelodGasState state))
            {
                state.ApplyEffects(pawn);
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_HelodGasEffects_1_5
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GasUtility), "PawnGasEffectsTick");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || !pawn.IsHashIntervalTick(50))
            {
                return;
            }

            if (HelodGasStore.TryGet(pawn.Map, out HelodGasState state))
            {
                state.ApplyEffects(pawn);
            }
        }
    }
}
