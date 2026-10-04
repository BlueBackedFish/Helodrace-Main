using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Helodrace;
using Verse;

internal static class RaidReportTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var report = new RaidTacticalReport { Id = "A:12:contact:3", OriginUnit = "A", ObserverId = 12,
            Revision = 100, ObservedTick = 100, ReceivedTick = 100, Kind = RaidReportKind.Contact,
            EnemyId = 3, Position = new IntVec3(8, 0, 8), Room = 7, StructureVersion = 1, Armed = true,
            Range = 35, Route = new List<string> { "A" } };
        var b = new RaidReportLedger(); var c = new RaidReportLedger();
        Check(b.Receive(report, "B", 140, false), "Physical/radio delivery imports the detached snapshot.");
        Check(report.Route.SequenceEqual(new[] { "A" }) && b.Reports[0].Route.SequenceEqual(new[] { "A", "B" }),
            "The receiver owns a deep copy and cannot mutate the sender's route.");
        Check(!b.Receive(report, "B", 160, false) && b.Reports[0].ReceivedTick == 140,
            "Duplicate delivery cannot renew either observation or reception time.");
        Check(c.Receive(b.Reports[0], "C", 180, false) && c.Reports[0].ObservedTick == 100,
            "Forwarding preserves the original observation age.");
        Check(!new RaidReportLedger().Receive(c.Reports[0], "A", 200, false), "A report cannot circulate back into its origin.");
        Check(!new RaidReportLedger().Receive(report, "D", 1300, false), "Expiry uses observation time, not the last forwarding time.");
        var memory = new RaidContactMemory();
        Check(memory.Receive(b.Reports[0], 9, 140), "Room IDs are remapped in the receiver's structure version.");
        var contact = memory.Entries.Single();
        Check(!contact.Visible && contact.Reported && contact.SeenTick == 100 && contact.ReceivedTick == 140
            && contact.OriginUnit == "A" && contact.ObserverId == 12 && contact.Room == 9,
            "Imported contacts preserve provenance and never become directly visible enemies.");
        Check(!memory.CanTarget(3, report.Position, 140), "A received report cannot authorize an exact grenade target.");
        Check(!memory.Receive(b.Reports[0], 9, 160), "Duplicate contact reports cannot refresh a memory.");
        var direct = memory.Observe(3, "direct", report.Position + IntVec3.East, 9, 20, 180, IntVec3.Invalid, true, 20, "B", 2);
        Check(!memory.Receive(c.Reports[0], 9, 200) && direct.Position != report.Position && !direct.Reported,
            "A stale relay cannot overwrite newer local observation.");
        var newer = report.Copy(); newer.Revision = newer.ObservedTick = 180;
        Check(!memory.Receive(newer, 9, 200) && direct.Visible, "Equal-time direct observation wins over a relay.");
        newer.Revision = newer.ObservedTick = 220;
        Check(memory.Receive(newer, 9, 260) && !direct.Visible && direct.SeenTick == 220,
            "Newer external intelligence replaces an old sighting as unconfirmed knowledge.");
        memory.FinishScan(1420, new HashSet<int>());
        Check(memory.Entries.Count == 0, "Imported contacts expire at the original observation boundary.");
        var personal = new RaidCommunicationState();
        personal.For(12).Reports.Publish(report);
        Check(personal.Knowledge.Reports.Count == 0 && personal.For(13).Reports.Reports.Count == 0,
            "An isolated observer's publication is not unit or teammate knowledge.");
        LoadSaveMode oldMode = Scribe.mode; XmlNode oldXml = Scribe.loader.curXmlParent; IExposable oldParent = Scribe.loader.curParent;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<root><id>A:12:contact:3</id><origin>A</origin><revision>100</revision>"
                + "<observed>100</observed><received>180</received><observer>12</observer><version>1</version>"
                + "<position>(8, 0, 8)</position><route><li>A</li><li>B</li><li>C</li></route></root>");
            var loaded = new RaidTacticalReport(); Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe.loader.curXmlParent = xml.DocumentElement; Scribe.loader.curParent = loaded; loaded.ExposeData();
            Check(loaded.ObservedTick == 100 && loaded.ReceivedTick == 180 && loaded.Route.SequenceEqual(new[] { "A", "B", "C" }),
                "Real Scribe loading retains report times, provenance and relay route.");
            Check(!new RaidReportLedger().Receive(loaded, "D", 1300, false), "A loaded report cannot receive a new lifetime.");
        }
        finally { Scribe.mode = oldMode; Scribe.loader.curXmlParent = oldXml; Scribe.loader.curParent = oldParent; }
        Check(!typeof(RaidTacticalReport).GetFields().Any(field => typeof(Thing).IsAssignableFrom(field.FieldType)),
            "No live enemy or map object reference is embedded in a report.");
        for (int i = 0; i < 80; i++) { var next = report.Copy(); next.Id = "report" + i; next.ObservedTick = i; b.Publish(next); }
        Check(b.Reports.Count == RaidCommunicationPolicy.LedgerCapacity, "Report storage has a fixed per-unit capacity.");
        Console.WriteLine($"PASS: {checks} tactical report isolation, merge, forwarding, expiry and native persistence checks.");
    }
}
