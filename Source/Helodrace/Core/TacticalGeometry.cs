using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Helodrace
{
    [Flags]
    public enum TacticalStructureKind : byte
    {
        None = 0, Door = 1, Opening = 2, Corner = 4, Corridor = 8, Junction = 16
    }

    [Flags]
    public enum TacticalOpenDirection : byte
    {
        None = 0, North = 1, East = 2, South = 4, West = 8
    }

    [Flags]
    internal enum TacticalRawFlags : byte
    {
        None = 0, Standable = 1, WallLine = 2, Door = 4, Edifice = 8,
        Outside = 16, Unroofed = 32, Anchor = 64
    }

    // Value-only input. Its owner stops writing before handing it to a worker.
    internal struct TacticalRawCell
    {
        public TacticalRawFlags Flags;
        public int Room;
        public int StructureId;
        public bool Has(TacticalRawFlags flags) => (Flags & flags) != 0;
    }

    internal struct TacticalGeometryCell
    {
        public TacticalStructureKind Structures;
        public TacticalOpenDirection OpenDirections;
        public bool ExteriorAccess;
        public byte DoorThreat;
        public byte WallThreat;
    }

    internal sealed class TacticalGeometryInput
    {
        public readonly int Width;
        public readonly int Height;
        public readonly TacticalRawCell[] Cells;

        public TacticalGeometryInput(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
            Width = width;
            Height = height;
            Cells = new TacticalRawCell[checked(width * height)];
        }
    }

    // Arrays are immutable after construction and never contain game objects.
    internal sealed class TacticalGeometryResult
    {
        public readonly TacticalGeometryInput Input;
        public readonly TacticalGeometryCell[] Cells;
        public readonly int[] Components;
        public readonly int ComponentCount;
        public readonly int[] Breaches;
        public readonly int[] Doors;
        public readonly int[] Anchors;
        public readonly IReadOnlyDictionary<int, int> RoomAreas;
        public readonly double CalculationMilliseconds;

        public TacticalGeometryResult(TacticalGeometryInput input,
            TacticalGeometryCell[] cells, int[] components, int componentCount,
            int[] breaches, int[] doors, int[] anchors, double milliseconds, Dictionary<int, int> roomAreas)
        {
            Input = input;
            Cells = cells;
            Components = components;
            ComponentCount = componentCount;
            Breaches = breaches;
            Doors = doors;
            Anchors = anchors;
            RoomAreas = roomAreas;
            CalculationMilliseconds = milliseconds;
        }
    }

    internal static class TacticalGeometry
    {
        public static TacticalGeometryResult Calculate(TacticalGeometryInput input,
            CancellationToken cancellation)
        {
            var watch = Stopwatch.StartNew();
            var cells = new TacticalGeometryCell[input.Cells.Length];
            var breaches = new List<int>();
            var doors = new List<int>();
            var anchors = new List<int>();
            var roomAreas = new Dictionary<int, int>();
            for (int z = 0; z < input.Height; z++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < input.Width; x++)
                {
                    int i = z * input.Width + x;
                    TacticalRawCell raw = input.Cells[i];
                    CountRoomCell(raw, roomAreas);
                    if (raw.Has(TacticalRawFlags.WallLine)) breaches.Add(i);
                    if (raw.Has(TacticalRawFlags.Door)) doors.Add(i);
                    if (raw.Has(TacticalRawFlags.Anchor) && raw.Room > 0) anchors.Add(i);
                    if (!Traversable(input, x, z)) continue;
                    bool door = raw.Has(TacticalRawFlags.Door);
                    bool north = Wall(input, x, z + 1), east = Wall(input, x + 1, z);
                    bool south = Wall(input, x, z - 1), west = Wall(input, x - 1, z);
                    TacticalOpenDirection open = TacticalOpenDirection.None;
                    if (Traversable(input, x, z + 1)) open |= TacticalOpenDirection.North;
                    if (Traversable(input, x + 1, z)) open |= TacticalOpenDirection.East;
                    if (Traversable(input, x, z - 1)) open |= TacticalOpenDirection.South;
                    if (Traversable(input, x - 1, z)) open |= TacticalOpenDirection.West;
                    bool ns = north && south, ew = east && west;
                    bool passage = ns || ew;
                    bool opening = !door && passage
                        && (ns && Traversable(input, x - 1, z) && Traversable(input, x + 1, z)
                            && !Corridor(input, x - 1, z, true) && !Corridor(input, x + 1, z, true)
                            || ew && Traversable(input, x, z - 1) && Traversable(input, x, z + 1)
                            && !Corridor(input, x, z - 1, false) && !Corridor(input, x, z + 1, false));
                    int directions = CountDirections(open);
                    bool corner = (north && east || east && south || south && west || west && north)
                        && directions >= 2;
                    bool junction = directions >= 3 && (north || east || south || west);
                    TacticalStructureKind kind = TacticalStructureKind.None;
                    if (door) kind |= TacticalStructureKind.Door;
                    if (opening) kind |= TacticalStructureKind.Opening;
                    if (passage && !opening) kind |= TacticalStructureKind.Corridor;
                    if (corner) kind |= TacticalStructureKind.Corner;
                    if (junction) kind |= TacticalStructureKind.Junction;
                    bool besideDoor = Door(input, x, z + 1) || Door(input, x + 1, z)
                        || Door(input, x, z - 1) || Door(input, x - 1, z);
                    cells[i] = new TacticalGeometryCell {
                        Structures = kind, OpenDirections = open,
                        ExteriorAccess = (door || opening)
                            && (DifferentSides(input, x, z + 1, x, z - 1)
                                || DifferentSides(input, x + 1, z, x - 1, z)),
                        DoorThreat = (byte)(door ? 12 : besideDoor ? 8 : 0),
                        WallThreat = (byte)(passage ? 6 : corner || junction ? 4 : 0)
                    };
                }
            }
            // Doors deliberately separate components. Live permissions/openings
            // can join the small graph without repeating a full-map flood fill.
            int[] components = Components(input, cancellation, out int componentCount);
            return new TacticalGeometryResult(input, cells, components, componentCount,
                breaches.ToArray(), doors.ToArray(), anchors.ToArray(), watch.Elapsed.TotalMilliseconds, roomAreas);
        }

        internal static void CountRoomCell(TacticalRawCell raw, Dictionary<int, int> areas)
        {
            // Count floor area, including furniture footprints, but not doors or walls.
            if (raw.Room <= 0 || raw.Has(TacticalRawFlags.WallLine) || raw.Has(TacticalRawFlags.Door)) return;
            areas.TryGetValue(raw.Room, out int area);
            areas[raw.Room] = area + 1;
        }

        private static int[] Components(TacticalGeometryInput input,
            CancellationToken cancellation, out int componentCount)
        {
            var result = new int[input.Cells.Length];
            var queue = new Queue<int>();
            componentCount = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                if (result[i] != 0 || !ComponentCell(input.Cells[i])) continue;
                result[i] = ++componentCount;
                queue.Enqueue(i);
                int processed = 0;
                while (queue.Count != 0)
                {
                    if ((processed++ & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    int current = queue.Dequeue();
                    int x = current % input.Width, z = current / input.Width;
                    if (x > 0) Visit(input, current - 1, componentCount, result, queue);
                    if (x + 1 < input.Width) Visit(input, current + 1, componentCount, result, queue);
                    if (z > 0) Visit(input, current - input.Width, componentCount, result, queue);
                    if (z + 1 < input.Height) Visit(input, current + input.Width, componentCount, result, queue);
                }
            }
            return result;
        }

        private static bool ComponentCell(TacticalRawCell cell) => cell.Has(TacticalRawFlags.Standable)
            && !cell.Has(TacticalRawFlags.Door);

        private static void Visit(TacticalGeometryInput input, int i, int id,
            int[] result, Queue<int> queue)
        {
            if (result[i] != 0 || !ComponentCell(input.Cells[i])) return;
            result[i] = id;
            queue.Enqueue(i);
        }

        private static bool Inside(TacticalGeometryInput input, int x, int z) => x >= 0
            && z >= 0 && x < input.Width && z < input.Height;
        private static bool Traversable(TacticalGeometryInput input, int x, int z) => Inside(input, x, z)
            && input.Cells[z * input.Width + x].Has(TacticalRawFlags.Standable | TacticalRawFlags.Door);
        private static bool Door(TacticalGeometryInput input, int x, int z) => Inside(input, x, z)
            && input.Cells[z * input.Width + x].Has(TacticalRawFlags.Door);
        private static bool Wall(TacticalGeometryInput input, int x, int z) => Inside(input, x, z)
            && input.Cells[z * input.Width + x].Has(TacticalRawFlags.Edifice)
            && !Traversable(input, x, z);
        private static bool Corridor(TacticalGeometryInput input, int x, int z, bool horizontal) =>
            Traversable(input, x, z) && (horizontal
                ? Wall(input, x, z + 1) && Wall(input, x, z - 1)
                : Wall(input, x + 1, z) && Wall(input, x - 1, z));

        private static bool DifferentSides(TacticalGeometryInput input, int x1, int z1, int x2, int z2)
        {
            if (!Traversable(input, x1, z1) || !Traversable(input, x2, z2)) return false;
            TacticalRawCell first = input.Cells[z1 * input.Width + x1];
            TacticalRawCell second = input.Cells[z2 * input.Width + x2];
            return first.Has(TacticalRawFlags.Outside) != second.Has(TacticalRawFlags.Outside)
                || first.Has(TacticalRawFlags.Unroofed) != second.Has(TacticalRawFlags.Unroofed);
        }

        private static int CountDirections(TacticalOpenDirection directions)
        {
            int count = 0;
            for (int value = (int)directions; value != 0; value >>= 1) count += value & 1;
            return count;
        }
    }

    internal static class TacticalGeometryWorker
    {
        private static readonly object gate = new object();
        private static Task running;
        public static void Release(Task task)
        {
            lock (gate)
                if (ReferenceEquals(running, task) && task.IsCompleted) running = null;
        }

        // One running calculation across all maps; waiting input stays with its
        // map owner. No task closure captures a Map, component, or game callback.
        public static bool TryStart(TacticalGeometryInput input, CancellationToken cancellation,
            out Task<TacticalGeometryResult> task)
            => TryStart(() => TacticalGeometry.Calculate(input, cancellation), cancellation, out task);

        public static bool TryStart(TacticalMovementMaskInput input, CancellationToken cancellation,
            out Task<ushort[]> task)
            => TryStart(() => TacticalMovementMask.Calculate(input, cancellation), cancellation, out task);

        private static bool TryStart<T>(Func<T> calculate, CancellationToken cancellation, out Task<T> task)
        {
            lock (gate)
            {
                task = null;
                if (running != null && !running.IsCompleted) return false;
                if (running?.IsFaulted == true) _ = running.Exception;
                task = Task.Factory.StartNew(calculate,
                    cancellation, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                running = task;
                return true;
            }
        }
    }
}
