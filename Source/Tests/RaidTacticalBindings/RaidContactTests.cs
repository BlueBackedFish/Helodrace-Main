using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace;
using Verse;
using HarmonyLib;

internal static class RaidContactTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var memory = new RaidContactMemory();
        var first = new IntVec3(10, 0, 10);
        var door = first + IntVec3.North;
        RaidEnemyContact Observe(int id, IntVec3 cell, int tick, IntVec3 portal) =>
            memory.Observe(id, "Contact" + id, cell, 7, 12, tick, portal, true, 24f);
        var contact = Observe(1, first, 100, IntVec3.Invalid);
        Check(contact.Confidence(100) == RaidContactConfidence.Visible && contact.ObserverId == 12, "A real sighting records its observer and exact location.");
        Observe(1, door, 120, door);
        Check(memory.Entries.Count == 1 && contact.Previous == first && contact.Direction == IntVec3.North, "Continuous sightings update the same identity and observed direction.");
        Check(contact.Portal == door, "Only an observed transition can establish a passage.");
        Observe(2, first - IntVec3.North * 4, 120, first - IntVec3.North * 3);
        Check(memory.Entries.Count == 2, "Front and rear contacts coexist instead of overwriting one enemy slot.");
        memory.FinishScan(140, new HashSet<int>());
        Check(!contact.Visible && contact.LostTick == 140 && contact.Position == door, "Losing sight freezes the last known location.");
        Check(contact.Confidence(140) == RaidContactConfidence.Recent && contact.Confidence(240) == RaidContactConfidence.Recent,
            "A lost contact retains short-lived location confidence through the two-second boundary.");
        Check(contact.Confidence(241) == RaidContactConfidence.Area && contact.Confidence(720) == RaidContactConfidence.Fading,
            "Location intelligence decays to an area and then a low-priority memory.");
        memory.FinishScan(200, new HashSet<int>());
        Check(contact.Position == door && contact.SeenTick == 120 && contact.LostTick == 140, "Unseen scans cannot follow a moving enemy or renew its memory.");
        contact.PositionConfirmedEmpty = true;
        Check(contact.Confidence(200) == RaidContactConfidence.Area && contact.WatchPoint == door,
            "An empty last position invalidates exact targeting while retaining passage caution.");
        Observe(1, first + IntVec3.East * 5, 220, IntVec3.Invalid);
        Check(contact.Visible && !contact.PositionConfirmedEmpty && contact.Portal == IntVec3.Invalid && contact.Direction == IntVec3.Zero,
            "Reappearance after a visibility gap refreshes identity without inventing an unseen route.");
        memory.FinishScan(220, new HashSet<int>());
        Check(contact.Visible, "A same-tick dedicated peek cannot be erased by the periodic scan.");
        string report = memory.Report(230);
        Check(report.Contains("#1 Contact1") && report.Contains("observer=#12") && contact.SeenTick == 220,
            "Developer inspection reads snapshots without updating observation times.");
        Check(!typeof(RaidEnemyContact).GetFields().Any(field => typeof(Pawn).IsAssignableFrom(field.FieldType)),
            "Contact records cannot expose a hidden Pawn's live position through a saved reference.");
        memory.FinishScan(1419, new HashSet<int>());
        Check(memory.Entries.Any(value => value.EnemyId == 1), "Memory survives until the configured expiry boundary.");
        memory.FinishScan(1420, new HashSet<int>());
        Check(memory.Entries.Count == 0, "Expired intelligence is removed and cannot indefinitely block progress.");
        for (int i = 0; i < 24; i++) Observe(i + 100, first, 2000 + i, IntVec3.Invalid);
        Check(memory.Entries.Count == RaidContactMemory.Capacity && memory.Entries.Min(value => value.SeenTick) == 2008,
            "Memory capacity is bounded and retains the newest contacts.");
        var policy = typeof(RaidContactMemory).Assembly.GetType("Helodrace.RaidCqbContactPolicy");
        bool Opposed(IntVec3 a, IntVec3 b) => (bool)AccessTools.Method(policy, "Opposed")
            .Invoke(null, new object[] { first, a, b });
        foreach (IntVec3 forward in new[] { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West })
        {
            Check((bool)AccessTools.Method(policy, "Rear").Invoke(null, new object[] { first, forward, first - forward * 4 }),
                "Rear attacks are detected in every assault orientation.");
            Check(Opposed(first + forward * 5, first - forward * 5), "Two ends of a corridor require simultaneous directional coverage.");
            Check(!Opposed(first + forward * 5, first + forward * 3), "Two enemies at the same end are not crossfire.");
        }
        Check(!Opposed(first + IntVec3.North * 4, first + IntVec3.East * 4), "Perpendicular doors alone do not force an opposed-direction pause.");
        memory = new RaidContactMemory();
        var front = Observe(11, first + IntVec3.North * 5, 3000, first + IntVec3.North * 4);
        var back = Observe(12, first - IntVec3.North * 5, 3000, first - IntVec3.North * 4);
        IEnumerable<RaidEnemyContact> Watch(int tick) => (IEnumerable<RaidEnemyContact>)AccessTools.Method(policy, "Watch")
            .Invoke(null, new object[] { memory, first, tick });
        memory.FinishScan(3020, new HashSet<int>());
        Check(Watch(3300).Count() == 2 && front.Position != back.Position,
            "Both vanished corridor enemies retain separate passage watches, without inventing live locations.");
        Observe(13, first + IntVec3.North * 6, 3300, front.Portal);
        Check(Watch(3300).Count() == 2, "Multiple contacts passing through one doorway share its watch assignment.");
        Check(Watch(3900).Count() == 0, "Passage guards stop blocking the task once contact confidence has faded.");
        bool Pause(bool rear, bool opposed, bool close) => (bool)AccessTools.Method(policy, "Pause")
            .Invoke(null, new object[] { rear, opposed, close });
        Check(Pause(true, false, false) && Pause(false, true, false) && Pause(false, false, true)
            && !Pause(false, false, false), "Only current rear, crossfire or close threats pause the operation; vanished enemies keep guards instead.");
        Console.WriteLine($"PASS: {checks} shared contact snapshot, visibility-loss, decay and capacity checks");
    }
}
