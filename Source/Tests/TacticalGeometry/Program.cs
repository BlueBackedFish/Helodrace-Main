using System;
using System.IO;
using System.Linq;
using System.Threading;
using Helodrace;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static TacticalGeometryInput Open(int width, int height)
    {
        var input = new TacticalGeometryInput(width, height);
        for (int i = 0; i < input.Cells.Length; i++)
            input.Cells[i].Flags = TacticalRawFlags.Standable | TacticalRawFlags.Outside | TacticalRawFlags.Unroofed;
        return input;
    }
    private static void Wall(TacticalGeometryInput input, int x, int z)
    {
        input.Cells[z * input.Width + x] = new TacticalRawCell {
            Flags = TacticalRawFlags.WallLine | TacticalRawFlags.Edifice, StructureId = z * input.Width + x + 1
        };
    }
    private static int Main()
    {
        try
        {
            var divided = Open(7, 5);
            for (int z = 0; z < 5; z++) Wall(divided, 3, z);
            int doorIndex = 2 * 7 + 3;
            divided.Cells[doorIndex].Flags |= TacticalRawFlags.Door;
            for (int z = 0; z < 5; z++)
                for (int x = 4; x < 7; x++)
                    divided.Cells[z * 7 + x] = new TacticalRawCell {
                        Flags = TacticalRawFlags.Standable, Room = 1
                    };
            divided.Cells[18].Flags |= TacticalRawFlags.Anchor;
            string inputBefore = string.Join(",", divided.Cells.Select(cell => $"{cell.Flags}:{cell.Room}:{cell.StructureId}"));
            TacticalGeometryResult geometry = TacticalGeometry.Calculate(divided, CancellationToken.None);
            Check(geometry.ComponentCount == 2, "A closed dividing door leaves two structural components");
            Check(geometry.Components[doorIndex] == 0, "Door cells are joined through live state, not permanently flooded");
            Check(geometry.Components[14] != geometry.Components[20], "Opposite door sides stay independent");
            Check(geometry.Cells[doorIndex].ExteriorAccess, "The door between indoor and outdoor flags is exterior access");
            Check(geometry.Cells[doorIndex].Structures.HasFlag(TacticalStructureKind.Door), "Door structure classification survives capture");
            Check(geometry.Cells[doorIndex].DoorThreat == 12 && geometry.Cells[16].DoorThreat == 8,
                "Door and adjacent geometry scores preserve the previous rules");
            Check(geometry.Doors.SequenceEqual(new[] { doorIndex }), "Only actual doors enter the door overlay list");
            Check(geometry.Breaches.Length == 5, "Every original wall/door remains a breach target");
            Check(geometry.Anchors.SequenceEqual(new[] { 18 }), "Indoor objective anchors are value indices");
            Check(inputBefore == string.Join(",", divided.Cells.Select(cell => $"{cell.Flags}:{cell.Room}:{cell.StructureId}")),
                "Calculation does not mutate the captured input");

            var gap = Open(7, 5);
            Wall(gap, 3, 1); Wall(gap, 3, 3);
            gap.Cells[16].Flags &= ~TacticalRawFlags.Unroofed;
            TacticalGeometryResult opening = TacticalGeometry.Calculate(gap, CancellationToken.None);
            Check(opening.Cells[17].Structures.HasFlag(TacticalStructureKind.Opening), "An isolated wall gap is classified as an opening");
            Check(opening.Cells[17].ExteriorAccess, "Roof difference marks exterior access even in a merged vanilla room");
            Check(opening.Cells[17].WallThreat == 6, "An isolated passage retains its geometry score");
            var corridor = Open(7, 5);
            for (int x = 0; x < 7; x++) { Wall(corridor, x, 1); Wall(corridor, x, 3); }
            TacticalGeometryResult hallway = TacticalGeometry.Calculate(corridor, CancellationToken.None);
            Check(hallway.Cells[17].Structures.HasFlag(TacticalStructureKind.Corridor), "A continuing hallway is not an isolated breach opening");
            Check(!hallway.Cells[17].Structures.HasFlag(TacticalStructureKind.Opening), "Neighbor lookahead preserves corridor continuity");

            var wrap = new TacticalGeometryInput(3, 2);
            wrap.Cells[2].Flags = TacticalRawFlags.Standable;
            wrap.Cells[3].Flags = TacticalRawFlags.Standable;
            TacticalGeometryResult edges = TacticalGeometry.Calculate(wrap, CancellationToken.None);
            Check(edges.ComponentCount == 2, "Flat indexing never connects opposite row edges");
            var furniture = Open(3, 3);
            furniture.Cells[7].Flags = TacticalRawFlags.Edifice;
            furniture.Cells[5].Flags = TacticalRawFlags.Edifice;
            TacticalGeometryResult corner = TacticalGeometry.Calculate(furniture, CancellationToken.None);
            Check(corner.Cells[4].Structures.HasFlag(TacticalStructureKind.Corner), "Impassable furniture keeps the legacy corner classification");
            Check(furniture.Cells.All(cell => !cell.Has(TacticalRawFlags.WallLine)), "Furniture is not turned into a physical breach wall");

            for (int seed = 0; seed < 40; seed++)
            {
                var random = new Random(seed);
                var input = Open(19, 13);
                for (int i = 0; i < input.Cells.Length; i++)
                {
                    if (random.Next(4) == 0) Wall(input, i % 19, i / 19);
                    if (random.Next(15) == 0) input.Cells[i].Flags |= TacticalRawFlags.Door;
                    input.Cells[i].Room = i % 3;
                }
                TacticalGeometryResult first = TacticalGeometry.Calculate(input, CancellationToken.None);
                TacticalGeometryResult second = TacticalGeometry.Calculate(input, CancellationToken.None);
                Check(first.Components.SequenceEqual(second.Components), "Component numbering is deterministic");
                string packed = TacticalGeometryCodec.Pack(first);
                Check(packed == TacticalGeometryCodec.Pack(second), "Pure geometry output is reproducible");
                TacticalGeometryResult restored = TacticalGeometryCodec.Unpack(packed, 19, 13);
                Check(packed == TacticalGeometryCodec.Pack(restored), "Shared version codec roundtrip preserves all values");
                Check(first.Breaches.SequenceEqual(restored.Breaches) && first.Doors.SequenceEqual(restored.Doors)
                    && first.Anchors.SequenceEqual(restored.Anchors), "Target lists reconstruct from the shared saved cells");
            }
            bool invalid = false;
            try { TacticalGeometryCodec.Unpack(TacticalGeometryCodec.Pack(geometry), 8, 5); }
            catch (InvalidDataException) { invalid = true; }
            Check(invalid, "A version from a different map size is rejected");
            invalid = false;
            try { TacticalGeometryCodec.Unpack("invalid base64", 7, 5); }
            catch (FormatException) { invalid = true; }
            Check(invalid, "Corrupt saved geometry is rejected");

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool canceled = false;
                try { TacticalGeometry.Calculate(divided, cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "An obsolete map request can cancel pure calculation before publication");
            }
            Check(TacticalGeometryWorker.TryStart(divided, CancellationToken.None, out var task), "A worker accepts value-only input");
            Check(task.Wait(TimeSpan.FromSeconds(10)), "Worker completion has a bounded test timeout");
            Check(TacticalGeometryCodec.Pack(task.Result) == TacticalGeometryCodec.Pack(geometry), "Background calculation equals the synchronous reference");
            TacticalGeometryWorker.Release(task);
            Check(TacticalGeometryWorker.TryStart(wrap, CancellationToken.None, out var next), "A completed worker slot is reusable by another map");
            Check(next.Wait(TimeSpan.FromSeconds(10)) && next.Result.ComponentCount == 2, "A reused slot does not return the previous map's result");
            TacticalGeometryWorker.Release(next);
            Console.WriteLine($"PASS: {checks} tactical geometry, immutable input, codec and worker checks.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
