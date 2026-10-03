using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
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

    // All maps share one frame budget, including paused updates and 3x ticks.
    internal static class TacticalCacheBudget
    {
        private static int frame = -1;
        private static long deadline;
        private static int remaining;
        public static bool TakeCell()
        {
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500;
                remaining = 2048;
            }
            if (remaining <= 0 || Stopwatch.GetTimestamp() >= deadline) return false;
            remaining--;
            return true;
        }
    }

    public sealed class MapComponent_TacticalMapAnalysis : MapComponent
    {
        private TacticalGeometryInput collecting;
        private readonly Dictionary<Room, int> roomIds = new Dictionary<Room, int>();
        private readonly HashSet<int> dirtyCells = new HashSet<int>();
        private int cursor;
        private bool rescanRooms;
        private bool dirty = true;
        private bool subscribed;
        private bool removed;
        private long revision;
        private long workRevision;
        private int nextVersion;
        private Task<TacticalGeometryResult> calculation;
        private CancellationTokenSource cancellation;
        private double captureMilliseconds;
        private Stopwatch buildWatch;
        private int retryAfterFrame;
        internal TacticalStructureVersion Completed { get; private set; }
        public long LastStaticBuildMilliseconds { get; private set; }
        public double LastCaptureMilliseconds { get; private set; }
        public double LastCalculationMilliseconds { get; private set; }
        public double MaximumCaptureSliceMilliseconds { get; private set; }
        public int DiscardedCalculations { get; private set; }
        public string BuildStatus => removed ? "Removed" : calculation != null ? "Calculating"
            : collecting != null ? $"Collecting {cursor}/{collecting.Cells.Length}"
            : dirty ? "Queued" : "Ready";
        public MapComponent_TacticalMapAnalysis(Map map) : base(map) { }
        public override void FinalizeInit() { base.FinalizeInit(); Subscribe(); }
        public override void MapComponentTick() { base.MapComponentTick(); Pump(); }
        public override void MapComponentUpdate() { base.MapComponentUpdate(); Pump(); }
        public override void MapRemoved()
        {
            removed = true;
            Unsubscribe();
            AbandonCalculation();
            collecting = null;
            Completed = null;
            dirtyCells.Clear();
            roomIds.Clear();
            base.MapRemoved();
        }
        // Reads and debug requests never perform a synchronous full build.
        public void RequestAnalysis() { Subscribe(); }
        public void ForceRebuild() { Subscribe(); OnRoomsChanged(); }
        internal void ReserveVersion(int id) => nextVersion = Math.Max(nextVersion, id);
        public TacticalCellData At(IntVec3 cell) { RequestAnalysis(); return CachedAt(cell); }
        internal TacticalCellData CachedAt(IntVec3 cell) => Completed?.At(map, cell)
            ?? default(TacticalCellData);
        private void Subscribe()
        {
            if (subscribed || removed || map.events == null) return;
            map.events.ThingSpawned += OnThingChanged;
            map.events.ThingDespawned += OnThingChanged;
            map.events.TerrainChanged += OnCellChanged;
            map.events.RoofChanged += OnCellChanged;
            map.events.RegionsRoomsChanged += OnRoomsChanged;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            map.events.ThingSpawned -= OnThingChanged;
            map.events.ThingDespawned -= OnThingChanged;
            map.events.TerrainChanged -= OnCellChanged;
            map.events.RoofChanged -= OnCellChanged;
            map.events.RegionsRoomsChanged -= OnRoomsChanged;
            subscribed = false;
        }
        private void OnThingChanged(Thing thing)
        {
            if (!(thing is Building) && thing.def.passability != Traversability.Impassable) return;
            foreach (IntVec3 cell in thing.OccupiedRect()) OnCellChanged(cell);
        }
        private void OnCellChanged(IntVec3 cell)
        {
            if (!cell.InBounds(map)) return;
            revision++;
            dirty = true;
            if (collecting != null) dirtyCells.Add(map.cellIndices.CellToIndex(cell));
            if (calculation != null) cancellation.Cancel();
        }
        private void OnRoomsChanged()
        {
            revision++;
            dirty = true;
            rescanRooms = true;
            if (calculation != null) cancellation.Cancel();
        }
        private void Pump()
        {
            if (removed || map.cellIndices.NumGridCells <= 0) return;
            Subscribe();
            if (calculation != null)
            {
                if (!calculation.IsCompleted) return;
                Task<TacticalGeometryResult> task = calculation;
                TacticalGeometryWorker.Release(task);
                calculation = null;
                cancellation.Dispose();
                cancellation = null;
                if (task.IsFaulted)
                {
                    Log.Error("[Helodrace] Tactical geometry calculation failed: " + task.Exception);
                    retryAfterFrame = Time.frameCount + 300;
                }
                else if (!task.IsCanceled && workRevision == revision)
                {
                    TacticalGeometryResult result = task.GetAwaiter().GetResult();
                    Completed = new TacticalStructureVersion(++nextVersion, result);
                    dirty = false;
                    LastCaptureMilliseconds = captureMilliseconds;
                    LastCalculationMilliseconds = result.CalculationMilliseconds;
                    LastStaticBuildMilliseconds = buildWatch.ElapsedMilliseconds;
                    return;
                }
                else DiscardedCalculations++;
                dirty = true;
            }
            if (!dirty || Time.frameCount < retryAfterFrame) return;
            // Vanilla refreshes rooms before components in MapUpdate. Never trigger its
            // potentially unbounded rebuild from inside a budgeted cell read.
            if (map.regionAndRoomUpdater == null || map.regionAndRoomUpdater.AnythingToRebuild) return;
            var slice = Stopwatch.StartNew();
            if (collecting == null)
            {
                if (!TacticalCacheBudget.TakeCell()) return;
                collecting = new TacticalGeometryInput(map.Size.x, map.Size.z);
                cursor = 0;
                rescanRooms = false;
                roomIds.Clear();
                dirtyCells.Clear();
                captureMilliseconds = 0;
                buildWatch = Stopwatch.StartNew();
            }
            while (cursor < collecting.Cells.Length && TacticalCacheBudget.TakeCell()) Capture(cursor++);
            if (cursor == collecting.Cells.Length && rescanRooms)
            {
                // Finish a pass before replaying room changes, instead of
                // restarting the cursor on each construction event mid-pass.
                cursor = 0;
                rescanRooms = false;
                roomIds.Clear();
                dirtyCells.Clear();
            }
            if (cursor == collecting.Cells.Length)
                foreach (int index in dirtyCells.Take(256).ToArray())
                {
                    if (!TacticalCacheBudget.TakeCell()) break;
                    Capture(index);
                    dirtyCells.Remove(index);
                }
            slice.Stop();
            captureMilliseconds += slice.Elapsed.TotalMilliseconds;
            MaximumCaptureSliceMilliseconds = Math.Max(MaximumCaptureSliceMilliseconds, slice.Elapsed.TotalMilliseconds);
            if (cursor != collecting.Cells.Length || dirtyCells.Count != 0 || rescanRooms) return;
            if (cancellation == null) cancellation = new CancellationTokenSource();
            if (TacticalGeometryWorker.TryStart(collecting, cancellation.Token, out calculation))
            {
                workRevision = revision;
                collecting = null;
                roomIds.Clear();
            }
        }
        private void Capture(int index)
        {
            IntVec3 cell = map.cellIndices.IndexToCell(index);
            Building edifice = cell.GetEdifice(map) as Building;
            bool standable = cell.Standable(map);
            bool door = edifice is Building_Door;
            bool wall = edifice != null && (edifice.def.IsWall || door);
            Room room = cell.GetRoom(map);
            bool outside = room == null || room.PsychologicallyOutdoors;
            int roomId = 0;
            if (!outside && !roomIds.TryGetValue(room, out roomId)) roomIds.Add(room, roomId = roomIds.Count + 1);
            bool anchor = false;
            if (!outside)
                foreach (Thing thing in cell.GetThingList(map))
                    if (thing is Building building && building.Position == cell
                        && building.Faction == Faction.OfPlayer && !building.def.IsWall
                        && !(building is Building_Door)) { anchor = true; break; }
            TacticalRawFlags flags = TacticalRawFlags.None;
            if (standable) flags |= TacticalRawFlags.Standable;
            if (door) flags |= TacticalRawFlags.Door;
            if (wall) flags |= TacticalRawFlags.WallLine;
            if (edifice != null) flags |= TacticalRawFlags.Edifice;
            if (outside) flags |= TacticalRawFlags.Outside;
            if (map.roofGrid.RoofAt(cell) == null) flags |= TacticalRawFlags.Unroofed;
            if (anchor) flags |= TacticalRawFlags.Anchor;
            collecting.Cells[index] = new TacticalRawCell {
                Flags = flags, Room = roomId, StructureId = wall ? edifice.thingIDNumber : 0
            };
        }
        private void AbandonCalculation()
        {
            CancellationTokenSource source = cancellation;
            Task<TacticalGeometryResult> task = calculation;
            cancellation = null;
            calculation = null;
            if (source == null) return;
            source.Cancel();
            if (task == null) source.Dispose();
            else task.ContinueWith(finished => {
                if (finished.IsFaulted) _ = finished.Exception;
                TacticalGeometryWorker.Release(finished);
                source.Dispose();
            }, TaskScheduler.Default);
        }
    }

    // One immutable buffer per saved version, shared by all its organizations.
    public sealed class TacticalStructureVersion : IExposable
    {
        public int Id;
        private string packed;
        internal TacticalGeometryResult Geometry { get; private set; }
        private IReadOnlyList<IntVec3> anchors;
        public TacticalStructureVersion() { }
        internal TacticalStructureVersion(int id, TacticalGeometryResult geometry) { Id = id; Geometry = geometry; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            if (Scribe.mode == LoadSaveMode.Saving && packed == null) packed = TacticalGeometryCodec.Pack(Geometry);
            Scribe_Values.Look(ref packed, "structure");
        }
        internal bool Restore(Map map)
        {
            try
            {
                Geometry = TacticalGeometryCodec.Unpack(packed, map.Size.x, map.Size.z);
                return Id > 0;
            }
            catch (Exception exception)
            {
                Log.Warning("[Helodrace] Could not restore raid structure version: " + exception);
                return false;
            }
        }
        internal TacticalCellData At(Map map, IntVec3 cell)
        {
            if (Geometry == null || !cell.InBounds(map)) return default(TacticalCellData);
            int index = map.cellIndices.CellToIndex(cell);
            TacticalGeometryCell data = Geometry.Cells[index];
            TacticalRawCell raw = Geometry.Input.Cells[index];
            return new TacticalCellData {
                Structures = data.Structures, OpenDirections = data.OpenDirections,
                Standable = raw.Has(TacticalRawFlags.Standable), WallLine = raw.Has(TacticalRawFlags.WallLine),
                Door = raw.Has(TacticalRawFlags.Door) ? CurrentStructure(map, cell, index) as Building_Door : null,
                ExteriorAccess = data.ExteriorAccess, DoorThreat = data.DoorThreat, WallThreat = data.WallThreat
            };
        }
        internal Building CurrentStructure(Map map, IntVec3 cell, int index)
        {
            Building building = cell.GetEdifice(map) as Building;
            return building?.Spawned == true && !building.Destroyed
                && building.thingIDNumber == Geometry.Input.Cells[index].StructureId ? building : null;
        }
        internal IReadOnlyList<IntVec3> Anchors(Map map) => anchors ?? (anchors = Geometry.Anchors
            .Select(map.cellIndices.IndexToCell).ToArray());
    }

    public sealed class RaidStructureSnapshot : IExposable
    {
        private Map map;
        internal TacticalStructureVersion Version { get; private set; }
        public string OrganizationId;
        private int versionId;
        public RaidStructureSnapshot() { }
        internal RaidStructureSnapshot(Map map, TacticalStructureVersion version, string organizationId)
        {
            this.map = map;
            Version = version;
            versionId = version.Id;
            OrganizationId = organizationId;
        }
        public IReadOnlyList<IntVec3> ObjectiveAnchors => Version.Anchors(map);
        public TacticalCellData CachedAt(IntVec3 cell) => Version.At(map, cell);
        public int RoomAt(IntVec3 cell) => map != null && cell.InBounds(map)
            ? Version.Geometry.Input.Cells[map.cellIndices.CellToIndex(cell)].Room : 0;
        public bool IsIndoor(IntVec3 cell) => RoomAt(cell) > 0;
        public IEnumerable<Building> CachedBreachStructures
        {
            get
            {
                foreach (int index in Version.Geometry.Breaches)
                {
                    Building building = Version.CurrentStructure(map, map.cellIndices.IndexToCell(index), index);
                    if (building != null) yield return building;
                }
            }
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref OrganizationId, "organizationId");
            Scribe_Values.Look(ref versionId, "structureVersion");
        }
        internal bool Restore(Map currentMap, Dictionary<int, TacticalStructureVersion> versions)
        {
            map = currentMap;
            if (!versions.TryGetValue(versionId, out TacticalStructureVersion version)) return false;
            Version = version;
            return true;
        }
    }
}
