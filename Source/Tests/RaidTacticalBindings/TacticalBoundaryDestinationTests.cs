using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Helodrace.Tactics;
using Verse;

internal static class TacticalBoundaryDestinationTests
{
    internal static void Run(string savePath = null)
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        IntVec3 opening = new IntVec3(10, 0, 10), inward = IntVec3.North;
        int probes = 0;
        Check(!TacticalPortalGeometry.LeadsIndoors(opening, inward, 1,
            _ => { probes++; return true; }, _ => false) && probes == TacticalPortalGeometry.DestinationProbeCells,
            "An open yard-to-yard wall must be rejected with a fixed three-cell forward probe.");
        Check(TacticalPortalGeometry.LeadsIndoors(opening, inward, 1, _ => true, c => c == opening + inward),
            "An ordinary indoor mouth must remain eligible.");
        Check(TacticalPortalGeometry.LeadsIndoors(opening, inward, 2, _ => true, c => c == opening + inward * 4),
            "Roof loss at a double-wall mouth can still lead into the roofed room two steps farther.");
        Check(!TacticalPortalGeometry.LeadsIndoors(opening, inward, 1, _ => true,
            c => c == opening - inward || c == opening + IntVec3.East || c == opening + inward * 4),
            "A roof behind/beside the channel or beyond the forward bound cannot authorize a yard crossing.");
        Check(!TacticalPortalGeometry.LeadsIndoors(opening, inward, 1,
            c => c != opening + inward * 2, c => c == opening + inward * 3),
            "A second wall/closed door cannot be crossed to borrow another room's roof.");
        var cornerSupports = new HashSet<IntVec3> { opening, opening + IntVec3.East, opening + IntVec3.North };
        Check(TacticalPortalGeometry.TryEntryNormal(opening, new IntVec3(8, 0, 3), cornerSupports.Contains,
            (mouth, direction) => direction == IntVec3.North, out IntVec3 selected) && selected == IntVec3.North,
            "A corner's dominant travel axis must yield to the other supported indoor face.");
        Check(!TacticalPortalGeometry.TryEntryNormal(opening, new IntVec3(8, 0, 3), cornerSupports.Contains,
            (_, __) => false, out selected), "Neither outside-only corner face may become a breach frontage.");
        Check(!TacticalPortalGeometry.TryEntryNormal(opening, new IntVec3(8, 0, 0), cornerSupports.Contains,
            (mouth, direction) => direction == IntVec3.North, out selected), "The alternate face must still head toward the goal.");
        Check(TacticalPortalGeometry.TryEntryNormal(opening, new IntVec3(8, 0, 3), cornerSupports.Contains,
            (mouth, direction) => mouth == opening + IntVec3.North && direction == IntVec3.East, out selected)
            && selected == IntVec3.East, "A valid neighbouring facade must survive a corner with no direct floor.");

        if (savePath != null)
        {
            XElement map = XDocument.Load(savePath).Root.Element("game").Element("maps").Elements("li").First();
            IntVec3 size = IntVec3.FromString((string)map.Element("mapInfo").Element("size"));
            byte[] roofs;
            using (var compressed = new MemoryStream(Convert.FromBase64String((string)map.Element("roofGrid").Element("roofsDeflate"))))
            using (var inflate = new DeflateStream(compressed, CompressionMode.Decompress))
            using (var output = new MemoryStream()) { inflate.CopyTo(output); roofs = output.ToArray(); }
            Check(roofs.Length == size.x * size.z * 2, "The read-only save fixture must use native ushort roof serialization.");
            var walls = new HashSet<IntVec3>(); var doors = new HashSet<IntVec3>();
            foreach (XElement thing in map.Element("things").Elements())
            {
                string pos = (string)thing.Element("pos"); if (pos == null) continue;
                if ((string)thing.Element("def") == "Wall") walls.Add(IntVec3.FromString(pos));
                if (((string)thing.Attribute("Class"))?.Contains("Door") == true) doors.Add(IntVec3.FromString(pos));
            }
            bool Floor(IntVec3 c) => c.x >= 0 && c.z >= 0 && c.x < size.x && c.z < size.z && !walls.Contains(c) && !doors.Contains(c);
            bool Roofed(IntVec3 c) => BitConverter.ToUInt16(roofs, (c.x + c.z * size.x) * 2) != 0;
            bool Direction(IntVec3 mouth, IntVec3 heading) => TacticalPortalGeometry.TryDepth(mouth, heading,
                c => walls.Contains(c) || doors.Contains(c), Floor, out int depth)
                && TacticalPortalGeometry.LeadsIndoors(mouth, heading, depth, Floor, Roofed);
            for (int x = 159; x <= 162; x++)
            {
                IntVec3 cell = new IntVec3(x, 0, 122);
                Check(walls.Contains(cell) && !Roofed(cell + IntVec3.South), "Reported coordinates must be the saved south end wall.");
                Check(!Direction(cell, IntVec3.East) && !Direction(cell, IntVec3.West) && !Direction(cell, IntVec3.South),
                    "A player-owned end wall must not authorize a widthwise or outward outdoor crossing: " + cell);
            }
            Check(Direction(new IntVec3(160, 0, 122), IntVec3.North) && Direction(new IntVec3(161, 0, 122), IntVec3.North),
                "The two proper end-wall entrances into the saved corridor must remain eligible.");
            Check(TacticalPortalGeometry.TryDepth(new IntVec3(162, 0, 122), IntVec3.West,
                walls.Contains, Floor, out int thickness) && thickness == 4,
                "The original wrong direction really treated the four-cell building width as wall thickness.");
            Check(TacticalPortalGeometry.TryEntryNormal(new IntVec3(162, 0, 122), new IntVec3(-30, 0, 20),
                walls.Contains, Direction, out selected) && selected == IntVec3.West
                && Direction(new IntVec3(162, 0, 123), selected),
                "A corner frontage may use its neighbouring true room entrance, while rejecting the corner itself.");
            Check(TacticalPortalGeometry.TryEntryFrontage(new IntVec3(162, 0, 122), new IntVec3(-30, 0, 20),
                walls.Contains, Direction, out selected, out IntVec3 frontage) && frontage == new IntVec3(162, 0, 123)
                && Direction(frontage, selected), "The planner must anchor on the valid adjacent frontage, not classify the rejected corner again.");
        }
        Console.WriteLine("PASS: " + checks + " bounded destination-side roof/floor, corner frontage and read-only saved end-wall checks.");
    }
}
