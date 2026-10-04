using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidCqbKnowledgeTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var assembly = typeof(RaidContactMemory).Assembly;
        var knowledgeType = assembly.GetType("Helodrace.RaidCqbKnowledge");
        var cellType = assembly.GetType("Helodrace.RaidKnownCqbCell");
        object knowledge = Activator.CreateInstance(knowledgeType, true);
        object Cell(int building, bool usable, bool portal = true)
        {
            object cell = Activator.CreateInstance(cellType);
            AccessTools.Field(cellType, "Building").SetValue(cell, building);
            AccessTools.Field(cellType, "Usable").SetValue(cell, usable);
            AccessTools.Field(cellType, "Portal").SetValue(cell, portal);
            return cell;
        }
        bool Usable(object value) => (bool)AccessTools.Field(cellType, "Usable").GetValue(value);
        int Building(object value) => (int)AccessTools.Field(cellType, "Building").GetValue(value);
        object Read(IntVec3 position, object baseline, object live, bool observed) =>
            AccessTools.Method(knowledgeType, "Read").Invoke(knowledge, new object[] { position, baseline, live,
                new Func<IntVec3, bool>(cell => observed) });
        var a = new IntVec3(3, 0, 1); var b = new IntVec3(3, 0, 5);
        object closedA = Cell(11, false), openA = Cell(11, true);
        object closedB = Cell(12, false), openB = Cell(12, true);
        Check(!Usable(Read(b, closedB, openB, false)), "Initial layout knowledge must not reveal an unseen open door.");
        Check(Usable(Read(a, closedA, openA, true)), "A visible opening immediately becomes a known passage.");
        for (int tick = 0; tick < 20; tick++)
        {
            Check(!Usable(Read(b, closedB, tick % 2 == 0 ? openB : closedB, false)),
                "Repeated hidden opposite-door toggles must not change the tactical topology.");
            Check(Usable(Read(a, closedA, closedA, false)), "Losing sight preserves the last observed entry state instead of tracking hidden closure.");
        }
        Check(!Usable(Read(a, closedA, closedA, true)), "A visible closure updates passage reachability.");
        Check(Usable(Read(b, closedB, openB, true)), "The second entry becomes usable only after observation.");
        Check(Usable(Read(b, closedB, closedB, false)), "Returning to a cache window preserves its last observed door state.");
        var wall = new IntVec3(2, 0, 3);
        Check(Building(Read(wall, Cell(15, false), Cell(0, true), false)) == 15,
            "An unseen destroyed wall must not expose a new route.");
        Check(Building(Read(wall, Cell(15, false), Cell(0, true), true)) == 0,
            "An observed new hole updates the structure identity and route.");
        int calls = 0;
        AccessTools.Method(knowledgeType, "Read").Invoke(knowledge, new object[] { wall, Cell(15, false), Cell(0, true),
            new Func<IntVec3, bool>(cell => { calls++; return true; }) });
        Check(calls == 0, "Unchanged cells must not spend LOS work on each cache refresh.");
        bool Replace(bool blocked, bool seen) => (bool)AccessTools.Method(knowledgeType, "ReplaceEntry")
            .Invoke(null, new object[] { blocked, seen });
        Check(!Replace(false, false) && !Replace(false, true), "A valid committed entrance stays fixed even when a different visible door opens.");
        Check(!Replace(true, false) && Replace(true, true), "Only an observed obstruction of the chosen entrance permits replacement.");

        var doorsType = assembly.GetType("Helodrace.RaidLocalMapState");
        string Signature(string previous, params KeyValuePair<int, bool>[] seen) =>
            (string)AccessTools.Method(doorsType, "ObservedDoorSignature").Invoke(null, new object[] { previous, seen });
        KeyValuePair<int, bool> Door(int id, bool open) => new KeyValuePair<int, bool>(id, open);
        string signature = Signature(null, Door(11, true), Door(12, false));
        Check(signature == "11:1,12:0", "Door knowledge has a deterministic signature independent of scan order.");
        Check(Signature(signature, Door(11, true)) == signature && Signature(signature) == signature,
            "Doors leaving sight do not look like closure/removal events or trigger room security reshuffling.");
        Check(Signature(signature, Door(12, true)) == "11:1,12:1", "A visible opposite door change updates security knowledge.");
        Check(Signature(signature, Door(11, false)) == "11:0,12:0", "An observed selected-door closure remains available to the counter-door response.");
        Check(!Signature(null, Door(11, true)).Contains("12:"), "Unknown doors cannot influence open-door security scoring.");

        // Both entrances lead into the same room. Feed observed knowledge to the real CQB BFS.
        var topologyType = assembly.GetType("Helodrace.CqbLocalTopology");
        int[] rooms = { 7, 0, 8, 7, 8, 8, 7, 0, 8 };
        bool[] portals = { false, true, false, false, false, false, false, true, false };
        bool[] walkable = { true, true, true, true, false, true, true, false, true };
        int[] Path(bool hiddenOpen, bool observed)
        {
            knowledge = Activator.CreateInstance(knowledgeType, true);
            walkable[7] = Usable(Read(b, closedB, hiddenOpen ? openB : closedB, observed));
            object topology = Activator.CreateInstance(topologyType, new object[] { 3, 3, rooms, walkable, portals });
            return ((IEnumerable<int>)AccessTools.Method(topologyType, "Path").Invoke(topology,
                new object[] { 6, 8, null })).ToArray();
        }
        Check(Path(false, false).SequenceEqual(Path(true, false)), "Opening the hidden opposite doorway cannot change the chosen BFS route.");
        Check(Path(true, true).SequenceEqual(new[] { 6, 7, 8 }), "After observation, the newly known opening can be used by a subsequent plan.");
        Console.WriteLine($"PASS: {checks} observed CQB topology, entry commitment and door-state memory checks");
    }
}
