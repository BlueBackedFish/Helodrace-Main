using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalStackFormationTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var opening = new IntVec3(20, 0, 20);
        var posts = new List<IntVec3>();
        foreach (IntVec3 inward in GenAdj.CardinalDirections)
        {
            IntVec3 tangent = new IntVec3(-inward.z, 0, inward.x);
            IntVec3 Cell(int depth, int width) => opening - inward * depth + tangent * width;
            int Width(IntVec3 c) => (c.x - opening.x) * tangent.x + (c.z - opening.z) * tangent.z;
            int Depth(IntVec3 c) => -(c.x - opening.x) * inward.x - (c.z - opening.z) * inward.z;
            int side = 1, reads = 0, sightReads = 0;
            var probed = new HashSet<IntVec3>();
            bool Free(IntVec3 c) { reads++; Check(probed.Add(c), "Each cell must be probed only once per formation."); return true; }
            Check(TacticalStackFormation.Build(opening, inward, 6, ref side, Free,
                c => { sightReads++; return Math.Abs(Width(c)) == 1; }, posts), "Covered formation must be reachable through the unoccupied mouth/peek cells.");
            Check(posts.Take(4).All(c => Depth(c) == 1) && posts.Count(c => Depth(c) == 1) >= 5
                && posts.All(c => Width(c) >= 2), "Weighted flood must favour wall-parallel growth over depth expansion.");
            Check(!posts.Contains(Cell(1, 0)) && posts.All(c => Width(c) != 0), "Entire ingress axis must stay clear, not just the mouth.");
            Check(TacticalLocalPlanner.Connected(posts) && posts.Distinct().Count() == 6, "Posts must remain one connected, nonoverlapping patch.");
            Check(reads <= TacticalStackFormation.CellLimit && sightReads <= reads, "Native cell/LOS probes must have a fixed bound.");

            // Narrow corridor: the side strip must not consume its centre lane.
            side = -1;
            Check(TacticalStackFormation.Build(opening, inward, 6, ref side, c => Math.Abs(Width(c)) <= 1,
                _ => false, posts) && posts.All(c => Width(c) == -1), "Narrow-corridor stack must remain on its selected edge.");
            side = -1;
            Check(!TacticalStackFormation.Build(opening, inward, 9, ref side, c => Math.Abs(Width(c)) <= 1,
                _ => false, posts) && posts.Count == 0 && side == -1, "A locked side must fail rather than fill the opposite corridor edge.");

            // Two disconnected covered pockets, each too small for the squad.
            side = 1;
            Check(!TacticalStackFormation.Build(opening, inward, 7, ref side, _ => true,
                c => !(Depth(c) <= 3 && (Width(c) == 2 || Width(c) == 4)), posts) && posts.Count == 0,
                "Flood travel through exposed cells must not combine disconnected post components.");
            // An obstacle isolates all side cover from the mouth, despite free destinations.
            side = 1;
            Check(!TacticalStackFormation.Build(opening, inward, 1, ref side, c => Width(c) != 1,
                _ => false, posts), "Flood must not jump over a blocked approach to reach side cover.");
            side = 0;
            Check(TacticalStackFormation.Build(opening, inward, 13, ref side, c => Width(c) <= 0,
                c => Math.Abs(Width(c)) == 1, posts) && side == -1 && posts.All(c => Width(c) < 0),
                "An initially unselected side must choose available cover and lock it for the whole formation.");

            // C4 shares the ordinary formation search, on the same locked side.
            side = 1;
            IntVec3 innerCharge = opening + inward;
            Check(TacticalStackFormation.Build(opening, inward, 13, ref side, _ => true,
                _ => false, posts, c => TacticalChargePolicy.UnsafeApproach(innerCharge, inward, c)),
                "Inner-layer C4 must admit a connected lateral withdrawal without a nine-tile radial retreat.");
            Check(posts.All(c => !TacticalChargePolicy.UnsafeApproach(innerCharge, inward, c))
                && posts.Any(c => Depth(c) == 1 && Width(c) == 2) && TacticalLocalPlanner.Connected(posts),
                "C4 posts must exclude the actual installation rectangle while retaining nearby wall cover.");
            side = 1; reads = 0;
            Check(!TacticalStackFormation.Build(opening, inward, 13, ref side,
                c => { reads++; return true; }, _ => true, posts) && reads <= TacticalStackFormation.CellLimit,
                "A fully exposed frontage must fail within the fixed work bound.");
        }
        // Validate the actual game's LOS argument: true skips the opaque source.
        Check(typeof(GenSight).GetMethods().Any(m => m.Name == "LineOfSight"
            && m.GetParameters().Length > 3 && m.GetParameters()[3].Name == "skipFirstCell"),
            "Installed RimWorld LOS must skip the source wall with the supplied boolean argument.");
        CheckNativeLos(Check, opening);
        CheckSave(Check, opening);
        CheckWithdrawalClaims(Check, opening);
        Console.WriteLine("PASS: " + checks + " weighted stack, corridor axis, locked side, disconnected cover, bounded probes, C4 withdrawal and native save/claim checks.");
    }

    private static void CheckNativeLos(Action<bool, string> check, IntVec3 opening)
    {
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(50, 1, 50) };
        map.cellIndices = new CellIndices(50, 50);
        map.edificeGrid = new EdificeGrid(map);
        var grid = (Building[])typeof(EdificeGrid).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Building[])).GetValue(map.edificeGrid);
        var wallDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        wallDef.fillPercent = 1;
        for (int z = 5; z <= 35; z++)
        {
            var wall = (Building)RuntimeHelpers.GetUninitializedObject(typeof(Building)); wall.def = wallDef;
            grid[map.cellIndices.CellToIndex(new IntVec3(opening.x, 0, z))] = wall;
        }
        int side = 1;
        var posts = new List<IntVec3>();
        bool Exposed(IntVec3 cell) => GenSight.LineOfSight(opening, cell, map, true);
        check(TacticalStackFormation.Build(opening, IntVec3.East, 13, ref side,
            c => c.x < opening.x, Exposed, posts), "Actual RimWorld LOS must admit wall-covered stack posts.");
        check(posts.All(c => !Exposed(c)) && posts.All(c => c.z > opening.z)
            && TacticalLocalPlanner.Connected(posts), "Native wall LOS and side lock must produce connected cover.");
    }

    private static void CheckSave(Action<bool, string> check, IntVec3 opening)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Stack-" + Guid.NewGuid() + ".xml");
        try
        {
            var plan = new TacticalLocalPlan { Opening = opening, Inward = IntVec3.East, StackSide = -1 };
            plan.Stack.Add(opening - IntVec3.East + IntVec3.South * 2);
            Scribe.saver.InitSaving(path, "plan"); plan.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalLocalPlan(); Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            check(loaded.StackSide == -1 && loaded.Stack.SequenceEqual(plan.Stack), "Native save must retain side lock and indexed posts.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
    }

    private static void CheckWithdrawalClaims(Action<bool, string> check, IntVec3 opening)
    {
        var service = (MapComponent_TacticalCommands)RuntimeHelpers.GetUninitializedObject(typeof(MapComponent_TacticalCommands));
        var claims = new Dictionary<IntVec3, TacticalSquadCommand>();
        AccessTools.Field(typeof(MapComponent_TacticalCommands), "claims").SetValue(service, claims);
        var plan = new TacticalLocalPlan();
        var command = new TacticalSquadCommand { Plan = plan, ChargeAction = new TacticalChargeAction { Withdrawing = true } };
        IntVec3 shared = opening + IntVec3.North * 2, original = shared + IntVec3.North, extra = shared + IntVec3.South;
        command.ChargeAction.OriginalStack.AddRange(new[] { shared, original });
        command.ChargeAction.Withdrawal.AddRange(new[] { shared, extra });
        plan.Stack.AddRange(command.ChargeAction.Withdrawal);
        claims[shared] = claims[original] = claims[extra] = command;
        AccessTools.Method(typeof(MapComponent_TacticalCommands), "RestoreChargeStack").Invoke(service, new object[] { command });
        check(claims.ContainsKey(shared) && claims.ContainsKey(original) && !claims.ContainsKey(extra)
            && plan.Stack.SequenceEqual(new[] { shared, original }), "C4 return must preserve reservations of shared/original posts.");
    }
}
