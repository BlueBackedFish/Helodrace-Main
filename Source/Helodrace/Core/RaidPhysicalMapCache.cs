using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    internal sealed class RaidPhysicalMapCache
    {
        private const int ChunkSize = 16, MaxChunks = 512;
        private static readonly ConditionalWeakTable<Map, RaidPhysicalMapCache> maps =
            new ConditionalWeakTable<Map, RaidPhysicalMapCache>();
        internal static RaidPhysicalMapCache For(Map map) => maps.GetValue(map, value => new RaidPhysicalMapCache(value));
        private readonly Map map;
        private sealed class Chunk
        {
            internal readonly Building[] Buildings = new Building[ChunkSize * ChunkSize];
            internal readonly bool[] Walkable = new bool[ChunkSize * ChunkSize];
            internal int Tick, LastUse;
            internal bool Dirty = true;
        }
        private readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        private readonly TacticalSpatialIndex<Pawn> pawns = new TacticalSpatialIndex<Pawn>();
        private readonly Dictionary<int, Pawn> pawnIds = new Dictionary<int, Pawn>();
        private readonly struct SightResult
        {
            internal readonly int Tick;
            internal readonly bool Visible;
            internal SightResult(int tick, bool visible) { Tick = tick; Visible = visible; }
        }
        private readonly Dictionary<long, SightResult> lines = new Dictionary<long, SightResult>();
        private int pawnTick = -1, pawnCount;
        internal int StructureRevision { get; private set; }
        internal readonly TacticalServiceBudget ObservationBudget = new TacticalServiceBudget(2048, 10, 32);
        internal long ChunkReads, ChunkHits, SpatialBuilds, LosChecks, LosHits;
        private RaidPhysicalMapCache(Map map) { this.map = map; }
        private int Columns => (map.Size.x + ChunkSize - 1) / ChunkSize;
        private int ChunkId(IntVec3 cell) => cell.x / ChunkSize + cell.z / ChunkSize * Columns;
        internal static void Dirty(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map) || !maps.TryGetValue(map, out RaidPhysicalMapCache cache)) return;
            if (cache.chunks.TryGetValue(cache.ChunkId(cell), out Chunk chunk)) chunk.Dirty = true;
            // Observation may use stale sight for up to 60 ticks. Physical path
            // permissions still use the dirty chunk; unrelated doors do not flush sight.
            cache.StructureRevision++;
        }
        internal static void Dirty(Building building)
        {
            if (building.Map == null || !maps.TryGetValue(building.Map, out _)) return;
            foreach (IntVec3 cell in building.OccupiedRect()) Dirty(building.Map, cell);
        }
        internal void Read(IntVec3 cell, int tick, out Building building, out bool walkable)
        {
            int id = ChunkId(cell);
            if (!chunks.TryGetValue(id, out Chunk chunk))
            {
                if (chunks.Count >= MaxChunks)
                    chunks.Remove(chunks.OrderBy(pair => pair.Value.LastUse).First().Key);
                chunks[id] = chunk = new Chunk();
            }
            // Rare polling catches terrain/mod changes that bypass vanilla notifications.
            if (chunk.Dirty || tick - chunk.Tick >= 120)
            {
                int x = id % Columns * ChunkSize, z = id / Columns * ChunkSize;
                for (int i = 0; i < chunk.Buildings.Length; i++)
                {
                    IntVec3 value = new IntVec3(x + i % ChunkSize, 0, z + i / ChunkSize);
                    if (!value.InBounds(map)) continue;
                    chunk.Buildings[i] = value.GetEdifice(map) as Building;
                    chunk.Walkable[i] = value.Walkable(map);
                }
                chunk.Tick = tick; chunk.Dirty = false; ChunkReads++;
            }
            else ChunkHits++;
            chunk.LastUse = tick;
            int local = cell.x % ChunkSize + cell.z % ChunkSize * ChunkSize;
            building = chunk.Buildings[local]; walkable = chunk.Walkable[local];
        }
        private void RefreshPawns(int tick)
        {
            if (pawnTick < 0 || tick < pawnTick || tick - pawnTick >= 60 || pawnCount != map.mapPawns.AllPawnsSpawned.Count)
            {
                pawns.Clear(); pawnIds.Clear();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    pawns.Add(pawn, pawn.Position.x, pawn.Position.z);
                    pawnIds[pawn.thingIDNumber] = pawn;
                }
                pawnTick = tick; pawnCount = map.mapPawns.AllPawnsSpawned.Count; SpatialBuilds++;
            }
        }
        internal Pawn FindPawn(int id, int tick)
        {
            RefreshPawns(tick);
            return pawnIds.TryGetValue(id, out Pawn pawn) && pawn.Spawned && pawn.Map == map ? pawn : null;
        }
        internal IEnumerable<Pawn> Nearby(IEnumerable<Pawn> members, int radius, int tick)
        {
            // Shared broad phase only, not squad knowledge. A movement margin
            // covers ordinary movement in this intentionally stale snapshot.
            RefreshPawns(tick);
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            foreach (Pawn member in members)
            {
                minX = Math.Min(minX, member.Position.x); minZ = Math.Min(minZ, member.Position.z);
                maxX = Math.Max(maxX, member.Position.x); maxZ = Math.Max(maxZ, member.Position.z);
            }
            if (minX == int.MaxValue) yield break;
            int margin = radius + 32;
            foreach (Pawn pawn in pawns.QueryBounds(minX - margin, minZ - margin, maxX + margin, maxZ + margin))
                if (pawn.Spawned && pawn.Map == map) yield return pawn;
        }
        internal bool ClearLine(IntVec3 source, IntVec3 target, int tick, Func<bool> calculate)
        {
            long key = ((long)map.cellIndices.CellToIndex(source) << 32)
                | (uint)map.cellIndices.CellToIndex(target);
            if (lines.TryGetValue(key, out SightResult cached) && tick >= cached.Tick && tick - cached.Tick < 60)
            { LosHits++; return cached.Visible; }
            LosChecks++; bool visible = calculate();
            if (lines.Count >= 8192 && !lines.ContainsKey(key)) lines.Clear();
            lines[key] = new SightResult(tick, visible);
            return visible;
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.SpawnSetup))]
    internal static class Patch_RaidPhysicalCache_Spawn
    {
        private static void Postfix(Building __instance) => RaidPhysicalMapCache.Dirty(__instance);
    }
    [HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
    internal static class Patch_RaidPhysicalCache_Despawn
    {
        private static void Prefix(Building __instance) => RaidPhysicalMapCache.Dirty(__instance);
    }
    [HarmonyPatch(typeof(Building_Door), "DoorOpen")]
    internal static class Patch_RaidPhysicalCache_DoorOpen
    {
        private static void Prefix(Building_Door __instance, out bool __state) => __state = __instance.Open;
        private static void Postfix(Building_Door __instance, bool __state)
        { if (__state != __instance.Open) RaidPhysicalMapCache.Dirty(__instance.Map, __instance.Position); }
    }
    [HarmonyPatch(typeof(Building_Door), "DoorTryClose")]
    internal static class Patch_RaidPhysicalCache_DoorClose
    {
        private static void Prefix(Building_Door __instance, out bool __state) => __state = __instance.Open;
        private static void Postfix(Building_Door __instance, bool __state)
        { if (__state != __instance.Open) RaidPhysicalMapCache.Dirty(__instance.Map, __instance.Position); }
    }
    [HarmonyPatch(typeof(TerrainGrid), nameof(TerrainGrid.SetTerrain))]
    internal static class Patch_RaidPhysicalCache_Terrain
    {
        private static void Postfix(Map ___map, IntVec3 c) => RaidPhysicalMapCache.Dirty(___map, c);
    }
}
