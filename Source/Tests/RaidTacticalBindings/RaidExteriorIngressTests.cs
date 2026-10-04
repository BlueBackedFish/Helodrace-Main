using System;
using System.Xml;
using Helodrace;
using Verse;

internal static class RaidExteriorIngressTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var opening = new IntVec3(10, 0, 10);
        foreach (IntVec3 offset in new[] { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West })
        {
            var ingress = new RaidExteriorIngress { Opening = opening, Inside = opening + offset,
                InsideRoom = 7, Active = true, Destination = opening + offset * 2 };
            Check(ingress.MovementDestination == opening,
                "The requested path ends at the mandatory opening first, avoiding a rejected diagonal interior shortcut.");
            ingress.ObservePosition(ingress.Inside, 7);
            Check(!ingress.Complete, "Entering the room through an old entrance must not complete the committed connection.");
            ingress.ObservePosition(opening, 9);
            Check(ingress.Entered && !ingress.Complete, "The doorway can have its own cached room ID.");
            Check(ingress.MovementDestination == ingress.Destination,
                "Arrival at the opening immediately provides the interior clearance destination.");
            ingress.ObservePosition(opening - offset, 0);
            Check(!ingress.Complete && !ingress.Entered, "Touching the opening and retreating resets the uncompleted inward step.");
            ingress.ObservePosition(opening, 9);
            ingress.ObservePosition(ingress.Inside, 8);
            Check(!ingress.Complete, "The wrong room cannot complete ingress.");
            ingress.ObservePosition(opening, 9);
            ingress.ObservePosition(ingress.Inside, 7);
            Check(!ingress.Complete && ingress.Entered && ingress.Active,
                "The first inside mouth cell cannot hand over to staging or reset the passage latch.");
            ingress.ObservePosition(ingress.Destination, 7);
            Check(ingress.Complete && !ingress.Active, "Actual inward passage releases the restriction immediately.");
            ingress.ObservePosition(opening - offset, 0);
            Check(ingress.Complete, "A completed connection does not force a pawn back to the opening.");
        }
        LoadSaveMode mode = Scribe.mode;
        XmlNode previous = Scribe.loader.curXmlParent;
        IExposable parent = Scribe.loader.curParent;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<root><opening>(10, 0, 10)</opening><inside>(11, 0, 10)</inside>"
                + "<insideRoom>7</insideRoom><entered>True</entered><active>True</active>"
                + "<destination>(12, 0, 10)</destination><requested>(20, 0, 10)</requested></root>");
            var loaded = new RaidExteriorIngress();
            Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe.loader.curXmlParent = xml.DocumentElement;
            Scribe.loader.curParent = loaded;
            loaded.ExposeData();
            Check(loaded.Opening == opening && loaded.InsideRoom == 7 && loaded.Entered && loaded.Active
                && loaded.Destination == new IntVec3(12, 0, 10) && loaded.Requested == new IntVec3(20, 0, 10),
                "Real Scribe loading preserves the clearance goal, original intent and physical passage latch.");
            loaded.ObservePosition(new IntVec3(11, 0, 10), 7);
            Check(!loaded.Complete && loaded.MovementDestination == loaded.Destination,
                "Loading inside the mouth continues clearing instead of holding or returning to the opening.");
            loaded.ObservePosition(new IntVec3(12, 0, 10), 7);
            Check(loaded.Complete, "A restored pending crossing completes after the inward step.");
        }
        finally { Scribe.mode = mode; Scribe.loader.curXmlParent = previous; Scribe.loader.curParent = parent; }
        Console.WriteLine($"PASS: {checks} exterior ingress physical passage and native persistence checks.");
    }
}
