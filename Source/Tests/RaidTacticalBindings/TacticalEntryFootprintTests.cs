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
            var positions = new List<IntVec3>(); var reachable = new HashSet<IntVec3>();
            int reads = 0; var readCells = new HashSet<IntVec3>();
            bool Walk(IntVec3 c)
            {
                reads++; Check(readCells.Add(c), "Local floor snapshot must not query a cell twice."); return Floor(c);
            }
            Check(TacticalEntryFootprint.Build(opening, inward, 13, 1, Walk, Floor,
                positions, reachable, out IntVec3 lane), "Open room must provide a full connected footprint.");
            Check(positions.Take(2).All(c => Math.Abs(Width(c)) == 5)
                && positions.All(c => Depth(c) <= 2) && reachable.Count == 15 && reads < 40,
                "Collect only a compact 13-seat footprint near the mouth before ranking it; do not survey the full room.");
            Check(positions.Zip(positions.Skip(1), (a, b) => Math.Abs(Width(a)) > Math.Abs(Width(b))
                || Math.Abs(Width(a)) == Math.Abs(Width(b)) && a.DistanceToSquared(opening) >= b.DistanceToSquared(opening)).All(v => v),
                "Rank selected seats by distance from the opening axis, then simple distance from the opening.");
            Check(positions.All(c => c != opening + inward && c != lane)
                && lane == opening + inward * 2 && positions.Distinct().Count() == 13,
                "Both mouth cells must remain unoccupied and all final posts unique.");
            Check(reachable.All(c => Depth(c) <= TacticalEntryFootprint.Depth)
                && reads <= (TacticalEntryFootprint.Width * 2 + 1) * TacticalEntryFootprint.Depth,
                "Long rooms must not deepen or extend the bounded local flood.");
            var occupied = new HashSet<IntVec3>(positions); var footprint = new HashSet<IntVec3>(reachable);
            IntVec3 reservedLane = lane;
            var replacement = new List<IntVec3>(); var replacementReachable = new HashSet<IntVec3>();
            Check(TacticalEntryFootprint.Build(opening, inward, 1, 1, Floor,
                c => Floor(c) && c != reservedLane && !occupied.Contains(c), replacement, replacementReachable, out _)
                && !footprint.Contains(replacement[0]) && replacement[0] != opening + inward,
                "An observed occupied seat must find one bounded local replacement without using the occupied footprint or reserved lane.");
            Check(TacticalEntryFootprint.Build(opening, inward, TacticalEntryFootprint.CellLimit, 1, Floor, Floor,
                positions, reachable, out lane) == false && positions.Count > 13,
                "Insufficient rooms must preserve partial seats for guard allocation.");
            Check(TacticalEntryFootprint.Build(opening, inward, 13, 1, Floor,
                c => Floor(c) && Depth(c) != 2, positions, reachable, out lane)
                && reachable.Any(c => Depth(c) == 3) && positions.All(c => Depth(c) != 2) && !lane.IsValid,
                "Walkable furniture may connect the footprint without becoming a standing post.");
            Check(!TacticalEntryFootprint.Build(opening, inward, 13, 1, c => Floor(c) && Depth(c) != 2,
                Floor, positions, reachable, out lane) && reachable.All(c => Depth(c) == 1),
                "A real wall must prevent selecting disconnected positions behind it.");
            bool Corridor(IntVec3 c) => Depth(c) >= 1 && Depth(c) <= 20 && Math.Abs(Width(c)) <= 1;
            Check(TacticalEntryFootprint.Build(opening, inward, 13, 1, Corridor, Corridor, positions, reachable, out lane)
                && positions.Count == 13 && positions.Max(Depth) <= 5 && reachable.Count == 15,
                "Three-wide corridors must grow just enough to fit a squad instead of stopping at four rows.");
            bool Tiny(IntVec3 c) => Depth(c) >= 1 && Depth(c) <= 2 && Width(c) == 0;
            Check(!TacticalEntryFootprint.Build(opening, inward, 13, 1, Tiny, Tiny, positions, reachable, out lane)
                && positions.SequenceEqual(new[] { opening + inward * 2 }) && !lane.IsValid,
                "A two-cell closet must admit one entrant rather than stall because every cell was reserved for transit.");
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
        Console.WriteLine("PASS: " + checks + " squad-sized entry flood, axis/distance order, narrow/short rooms, furniture, bounded probes and role/commander order.");
    }
}
