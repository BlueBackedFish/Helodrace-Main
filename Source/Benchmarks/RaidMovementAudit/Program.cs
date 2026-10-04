using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Helodrace;

// Analysis tool using production value-only code. No Pawn/Map/Unity simulation;
// elapsed timings exclude live capture, worker scheduling, native publication and pathfinding.
internal static class Program
{
    private static void Main()
    {
        int[] allowedRooms = { 2 };
        bool Allowed(int room) => allowedRooms.Contains(room);
        var exitCases = new[] {
            new { Name = "previous_room_floor", Room = 1, Door = false, Selected = false },
            new { Name = "door_own_room", Room = 3, Door = true, Selected = false },
            new { Name = "selected_door_own_room", Room = 3, Door = true, Selected = true },
            new { Name = "demolished_wall_room_zero", Room = 0, Door = false, Selected = false },
            new { Name = "next_room_floor", Room = 2, Door = false, Selected = false }
        }.Select(value => new { value.Name, value.Room,
            Permitted = TacticalNodeProgress.AllowsStep(1, value.Room, value.Door, value.Selected, Allowed) }).ToArray();

        var raw = new TacticalGeometryInput(4, 1);
        raw.Cells[0] = new TacticalRawCell { Room = 1, Flags = TacticalRawFlags.Standable };
        raw.Cells[1] = new TacticalRawCell { Room = 3, Flags = TacticalRawFlags.Standable | TacticalRawFlags.Door | TacticalRawFlags.WallLine };
        raw.Cells[2] = new TacticalRawCell { Room = 0, Flags = TacticalRawFlags.Standable | TacticalRawFlags.WallLine };
        raw.Cells[3] = new TacticalRawCell { Room = 2, Flags = TacticalRawFlags.Standable };
        var exitMask = TacticalMovementMask.Calculate(new TacticalMovementMaskInput {
            Width = 4, Height = 1, Structure = TacticalGeometry.Calculate(raw, CancellationToken.None),
            InitialRoom = 1, RestrictRooms = true, AllowedRooms = allowedRooms, RestrictPortals = true
        }, CancellationToken.None);

        var samples = new List<object>();
        foreach (int size in new[] { 250, 400, 600 })
        {
            var input = new TacticalGeometryInput(size, size);
            for (int i = 0; i < input.Cells.Length; i++)
                input.Cells[i] = new TacticalRawCell { Room = 1, Flags = TacticalRawFlags.Standable };
            var geometry = TacticalGeometry.Calculate(input, CancellationToken.None);
            int center = size / 2;
            int[] local = Enumerable.Range(center - 15, 31)
                .SelectMany(z => Enumerable.Range(center - 15, 31).Select(x => z * size + x)).ToArray();
            foreach (bool indoor in new[] { false, true })
            {
                var mask = new TacticalMovementMaskInput {
                    Width = size, Height = size, Structure = geometry, InitialRoom = 1,
                    RestrictRooms = !indoor, AllowedRooms = new[] { 1 }, RestrictPortals = !indoor,
                    RestrictCells = indoor, AllowedCells = indoor ? local : Array.Empty<int>(), BreachIndex = -1
                };
                for (int i = 0; i < 8; i++) TacticalMovementMask.Calculate(mask, CancellationToken.None);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var timings = new double[48];
                ushort[] last = null;
                for (int i = 0; i < timings.Length; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    last = TacticalMovementMask.Calculate(mask, CancellationToken.None);
                    timings[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Array.Sort(timings);
                samples.Add(new {
                    Size = size, Mode = indoor ? "indoor_961_cells" : "personal_node",
                    Iterations = timings.Length, MedianMs = Math.Round(timings[timings.Length / 2], 4),
                    P95Ms = Math.Round(timings[(int)Math.Ceiling(timings.Length * 0.95) - 1], 4),
                    AllocatedKiBPerMask = Math.Round(allocated / (double)timings.Length / 1024, 2),
                    OffsetArrayBytes = (long)size * size * sizeof(ushort),
                    Retained64MasksMiB = Math.Round((long)size * size * sizeof(ushort) * 64.0 / 1048576, 2),
                    Retained256MasksMiB = Math.Round((long)size * size * sizeof(ushort) * 256.0 / 1048576, 2),
                    Checksum = last[center * size + center]
                });
                GC.KeepAlive(last);
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            Runtime = RuntimeInformation.FrameworkDescription,
            Scope = "Release recommended; synthetic immutable inputs, production policies; not game TPS or Unity job latency",
            GuardHoldingPreviousPosition = new {
                Members = 13, GuardAwayFromFinalNode = 1, OtherMembersArrived = 12,
                CanAdvanceFinalNode = TacticalNodeProgress.Advance(true, 13, 12),
                Scope = "Policy counterexample with the current FollowNodes cohort; not live contact-guard simulation"
            },
            OffRouteExit = exitCases,
            OffRouteMaskPermitted = exitMask.Select(cost => cost != ushort.MaxValue).ToArray(),
            Measurements = samples
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
