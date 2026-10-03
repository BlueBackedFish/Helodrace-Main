using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Helodrace
{
    internal static class TacticalGeometryCodec
    {
        public static string Pack(TacticalGeometryResult geometry)
        {
            using (var bytes = new MemoryStream())
            {
                using (var zip = new DeflateStream(bytes, CompressionMode.Compress, true))
                using (var writer = new BinaryWriter(zip))
                {
                    writer.Write(geometry.Input.Width);
                    writer.Write(geometry.Input.Height);
                    writer.Write(geometry.ComponentCount);
                    for (int i = 0; i < geometry.Cells.Length; i++)
                    {
                        TacticalRawCell raw = geometry.Input.Cells[i];
                        TacticalGeometryCell cell = geometry.Cells[i];
                        writer.Write((byte)((byte)raw.Flags | (cell.ExteriorAccess ? 128 : 0)));
                        writer.Write((byte)cell.Structures);
                        writer.Write((byte)cell.OpenDirections);
                        writer.Write(cell.DoorThreat);
                        writer.Write(cell.WallThreat);
                        writer.Write(raw.Room);
                        writer.Write(raw.StructureId);
                        writer.Write(geometry.Components[i]);
                    }
                }
                return Convert.ToBase64String(bytes.ToArray());
            }
        }
        public static TacticalGeometryResult Unpack(string packed, int width, int height)
        {
            using (var bytes = new MemoryStream(Convert.FromBase64String(packed)))
            using (var zip = new DeflateStream(bytes, CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            {
                if (reader.ReadInt32() != width || reader.ReadInt32() != height)
                    throw new InvalidDataException("Structure dimensions differ from map.");
                var input = new TacticalGeometryInput(width, height);
                var cells = new TacticalGeometryCell[input.Cells.Length];
                var components = new int[cells.Length];
                int componentCount = reader.ReadInt32();
                if (componentCount < 0 || componentCount > cells.Length) throw new InvalidDataException("Invalid component count.");
                var breaches = new List<int>();
                var doors = new List<int>();
                var anchors = new List<int>();
                for (int i = 0; i < cells.Length; i++)
                {
                    byte flags = reader.ReadByte();
                    cells[i] = new TacticalGeometryCell {
                        Structures = (TacticalStructureKind)reader.ReadByte(),
                        OpenDirections = (TacticalOpenDirection)reader.ReadByte(),
                        ExteriorAccess = (flags & 128) != 0,
                        DoorThreat = reader.ReadByte(), WallThreat = reader.ReadByte()
                    };
                    input.Cells[i] = new TacticalRawCell {
                        Flags = (TacticalRawFlags)(flags & 127), Room = reader.ReadInt32(), StructureId = reader.ReadInt32()
                    };
                    components[i] = reader.ReadInt32();
                    if (input.Cells[i].Room < 0 || components[i] < 0 || components[i] > componentCount)
                        throw new InvalidDataException("Invalid structure cell.");
                    if (input.Cells[i].Has(TacticalRawFlags.WallLine)) breaches.Add(i);
                    if (input.Cells[i].Has(TacticalRawFlags.Door)) doors.Add(i);
                    if (input.Cells[i].Has(TacticalRawFlags.Anchor) && input.Cells[i].Room > 0) anchors.Add(i);
                }
                if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing structure data.");
                return new TacticalGeometryResult(input, cells, components, componentCount,
                    breaches.ToArray(), doors.ToArray(), anchors.ToArray(), 0);
            }
        }
    }
}
