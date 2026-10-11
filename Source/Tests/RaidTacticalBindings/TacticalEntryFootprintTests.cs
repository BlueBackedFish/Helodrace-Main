using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace.Tactics;
using Verse;

internal static class TacticalEntryFootprintTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var opening = new IntVec3(20, 0, 20);
        foreach (IntVec3 inward in GenAdj.CardinalDirections)
        {
            IntVec3 tangent = new IntVec3(-inward.z, 0, inward.x);
            int Depth(IntVec3 c) => (c.x - opening.x) * inward.x + (c.z - opening.z) * inward.z;
            int Width(IntVec3 c) => (c.x - opening.x) * tangent.x + (c.z - opening.z) * tangent.z;
            bool Floor(IntVec3 c) => Depth(c) >= 1 && Depth(c) <= 10 && Math.Abs(Width(c)) <= 5;
            bool Wall(IntVec3 c) => Depth(c) == 0 || Math.Abs(Width(c)) == 6;
            var positions = new List<IntVec3>(); var reachable = new HashSet<IntVec3>();
            int reads = 0; var readCells = new HashSet<IntVec3>();
            bool Walk(IntVec3 c)
            {
                reads++; Check(readCells.Add(c), "Local floor snapshot must not query a cell twice."); return Floor(c);
            }
            Check(TacticalEntryFootprint.Build(opening, inward, 13, 1, Walk, Floor, Wall,
                positions, reachable, out IntVec3 lane), "Open room must provide a full connected footprint.");
            Check(positions.Take(4).All(c => Math.Abs(Width(c)) == 4 && Depth(c) >= 2),
                "First posts must be in front of the wall and distant along its axis.");
            Check(positions.All(c => c != opening + inward && c != lane)
                && lane == opening + inward * 2 && positions.Distinct().Count() == 13,
                "Both mouth cells must remain unoccupied and all final posts unique.");
            Check(reachable.All(c => Depth(c) <= TacticalEntryFootprint.Depth)
                && reads <= (TacticalEntryFootprint.Width * 2 + 1) * TacticalEntryFootprint.Depth,
                "Long rooms must not deepen or extend the bounded local flood.");
            Check(TacticalEntryFootprint.Build(opening, inward, 60, 1, Floor, Floor, Wall,
                positions, reachable, out lane) == false && positions.Count > 13,
                "Insufficient rooms must preserve partial seats for guard allocation.");
            Check(positions.Last(c => Depth(c) == 1 && Math.Abs(Width(c)) <= 1) == positions.Last(),
                "Near-opening wall seats must be the last choice, after distant wall/front strips.");
            Check(TacticalEntryFootprint.Build(opening, inward, 13, 1, Floor,
                c => Floor(c) && Depth(c) != 2, Wall, positions, reachable, out lane)
                && reachable.Any(c => Depth(c) == 3) && positions.All(c => Depth(c) != 2) && !lane.IsValid,
                "Walkable furniture may connect the footprint without becoming a standing post.");
            Check(!TacticalEntryFootprint.Build(opening, inward, 13, 1, c => Floor(c) && Depth(c) != 2,
                Floor, Wall, positions, reachable, out lane) && reachable.All(c => Depth(c) == 1),
                "A real wall must prevent selecting disconnected positions behind it.");
        }
        var candidates = Enumerable.Range(0, 13).Select(i => new TacticalEntryCandidate
            { Index = i, Priority = i >= 10 ? 2 : 0, Distance = i * i, Leader = i == 0 }).ToList();
        candidates[2] = new TacticalEntryCandidate { Index = 2, Priority = -1, Distance = 4 };
        var order = TacticalEntryOrder.Build(candidates);
        Check(order.Count == 13 && order.Distinct().Count() == 13 && order[6] == 0 && order[0] == 2 && order[1] == 1,
            "Breacher/near-stack assault members must lead and squad commander must be at the middle.");
        Check(order.Skip(10).All(i => i >= 10), "Long-range/support roles must follow the assault members.");
        XElement roles = XDocument.Load("Defs/Organization/Organization_GreatWar.xml").Root;
        string Function(string name) => (string)roles.Elements().Single(d => (string)d.Element("defName") == name).Element("combatFunction");
        Check(TacticalEntryOrder.Priority(Function("HD_Role_Marksman")) > TacticalEntryOrder.Priority(Function("HD_Role_Rifleman"))
            && TacticalEntryOrder.Priority(Function("HD_Role_BazookaGunner")) > TacticalEntryOrder.Priority(Function("HD_Role_Carbine")),
            "Entry priority must match actual sniper/bazooka role functions from the mod definitions.");
        var plan = new TacticalLocalPlan();
        plan.Stack.AddRange(Enumerable.Range(0, 13).Select(i => new IntVec3(18, 0, 21 + i)));
        var seats = Enumerable.Range(0, 13).Select(i => new IntVec3(22, 0, 28 - i)).ToArray();
        plan.Positions.AddRange(seats);
        Check(TacticalEntryAllocation.Allocate(plan, order) && plan.Positions[order[0]] == seats[0]
            && plan.Positions[order[6]] == seats[6] && plan.EntryOrder.SequenceEqual(order),
            "Far-to-near seat sequence must map onto role/proximity entry order without changing member identity.");
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Entry-" + Guid.NewGuid() + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "plan"); plan.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalLocalPlan(); Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            Check(loaded.EntryOrder.SequenceEqual(order) && loaded.Positions.SequenceEqual(plan.Positions),
                "Native Scribe must retain entry sequence and indexed destination assignments.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("PASS: " + checks + " entry flood/front-wall order, far posts, open mouth, furniture, bounded floor probes, role/commander order and native save checks.");
    }
}
