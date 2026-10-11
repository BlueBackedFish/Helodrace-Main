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
                c => { sightReads++; return Math.Abs(Width(c)) <= 1; }, posts), "Covered formation must be reachable through the unoccupied mouth/peek cells.");
            Check(posts.All(c => Depth(c) == 1 && Math.Abs(Width(c)) >= 2)
                && posts.Count(c => Width(c) > 0) == 3 && posts.Count(c => Width(c) < 0) == 3,
                "Weighted flood must grow along both wall wings before expanding backwards.");
            Check(!posts.Contains(Cell(1, 0)) && posts.All(c => Width(c) != 0), "Entire ingress axis must stay clear, not just the mouth.");
            Check(TacticalStackFormation.ConnectedWings(opening, inward, posts) && posts.Distinct().Count() == 6,
                "Each reachable wing must remain connected and posts must not overlap.");
            Check(reads <= TacticalStackFormation.CellLimit && sightReads <= reads, "Native cell/LOS probes must have a fixed bound.");

            // Narrow corridor: the side strip must not consume its centre lane.
            side = 1;
            Check(TacticalStackFormation.Build(opening, inward, 6, ref side, c => Width(c) == 0,
                _ => true, posts) && posts.All(c => Width(c) == 0) && TacticalLocalPlanner.Connected(posts),
                "One-tile corridor must use centre posts when LOS and axis filters cannot fit anyone.");
            side = 1;
            Check(TacticalStackFormation.Build(opening, inward, 2, ref side, _ => true,
                c => Width(c) != 0, posts) && posts.All(c => Width(c) == 0),
                "LOS-only seats must not also apply the axis exclusion.");
            side = -1;
            Check(TacticalStackFormation.Build(opening, inward, 6, ref side, c => Math.Abs(Width(c)) <= 1,
                _ => true, posts) && posts.Count(c => Width(c) == -1) == 3 && posts.Count(c => Width(c) == 1) == 3,
                "An existing side preference must not prevent bilateral corridor stacking.");
            side = -1;
            Check(TacticalStackFormation.Build(opening, inward, 13, ref side, c => Math.Abs(Width(c)) <= 1,
                _ => true, posts) && posts.All(c => Width(c) != 0) && side == -1
                && posts.Count(c => Width(c) < 0) == 7 && posts.Count(c => Width(c) > 0) == 6
                && TacticalStackFormation.ConnectedWings(opening, inward, posts),
                "A 13-person squad must use both corridor edges and preserve its centre lane in all four orientations.");
            var bilateral = new TacticalLocalPlan { Opening = opening, Inward = inward };
            bilateral.Stack.AddRange(posts);
            Check(TacticalEntryAllocation.ConnectedStack(bilateral), "Bilateral stack validation must allow the unoccupied lane between connected wings.");

            // Two disconnected covered pockets, each too small for the squad.
            side = 1;
            Check(!TacticalStackFormation.Build(opening, inward, 7, ref side, _ => true,
                _ => false, posts, c => !(Depth(c) <= 3 && (Width(c) == 2 || Width(c) == 4))) && posts.Count == 0,
                "Flood travel through exposed cells must not combine disconnected post components.");
            // An obstacle isolates all side cover from the mouth, despite free destinations.
            side = 1;
            Check(!TacticalStackFormation.Build(opening, inward, 9, ref side, c => Width(c) >= 0 && Width(c) != 1,
                _ => false, posts), "Flood must not jump over a blocked approach to reach side cover.");
            side = 0;
            Check(TacticalStackFormation.Build(opening, inward, 13, ref side, c => Width(c) <= 0,
                c => Math.Abs(Width(c)) <= 1, posts) && side == -1 && posts.All(c => Width(c) < 0),
                "When only one wing is available, use it and retain that observation preference.");

            // C4 shares the ordinary bilateral formation search.
            side = 1;
            IntVec3 innerCharge = opening + inward;
            Check(TacticalStackFormation.Build(opening, inward, 13, ref side, _ => true,
                _ => false, posts, c => TacticalChargePolicy.UnsafeApproach(innerCharge, inward, c)),
                "Inner-layer C4 must admit a connected lateral withdrawal without a nine-tile radial retreat.");
            Check(posts.All(c => !TacticalChargePolicy.UnsafeApproach(innerCharge, inward, c))
                && posts.Any(c => Depth(c) == 1 && Width(c) == 2)
                && posts.Any(c => Width(c) < 0) && TacticalStackFormation.ConnectedWings(opening, inward, posts),
                "C4 posts must exclude the actual installation rectangle while retaining nearby wall cover.");
            side = 1; reads = 0;
            Check(!TacticalStackFormation.Build(opening, inward, 13, ref side,
                c => { reads++; return true; }, _ => true, posts, _ => true) && reads <= TacticalStackFormation.CellLimit,
                "A fully exposed frontage must fail within the fixed work bound.");
        }
        // Validate the actual game's LOS argument: true skips the opaque source.
        Check(typeof(GenSight).GetMethods().Any(m => m.Name == "LineOfSight"
            && m.GetParameters().Length > 3 && m.GetParameters()[3].Name == "skipFirstCell"),
            "Installed RimWorld LOS must skip the source wall with the supplied boolean argument.");
        CheckNativeLos(Check, opening);
        CheckWorkshop(Check);
        CheckSave(Check, opening);
        CheckWithdrawalClaims(Check, opening);
        Console.WriteLine("PASS: " + checks + " weighted bilateral stack, corridor axis, disconnected cover, bounded probes, C4 withdrawal and native save/claim checks.");
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
        check(posts.All(c => !Exposed(c)) && posts.Any(c => c.z > opening.z) && posts.Any(c => c.z < opening.z)
            && TacticalStackFormation.ConnectedWings(opening, IntVec3.East, posts), "Native wall LOS must produce connected cover on both wings.");
    }

    private static void CheckWorkshop(Action<bool, string> check)
    {
        // Exterior2 save workshop: x=148..157,z=135..140; north door
        // (149,141), two 3x1 benches along the north face, 2x1 bench south.
        // Translate to a small native LOS fixture; no live command was saved.
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(50, 1, 50) };
        map.cellIndices = new CellIndices(50, 50); map.edificeGrid = new EdificeGrid(map);
        var grid = (Building[])typeof(EdificeGrid).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Building[])).GetValue(map.edificeGrid);
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); def.fillPercent = 1;
        for (int x = 19; x <= 30; x++) for (int z = 13; z <= 20; z++)
        {
            if (x != 19 && x != 30 && z != 13 && z != 20) continue;
            var wall = (Building)RuntimeHelpers.GetUninitializedObject(typeof(Building)); wall.def = def;
            grid[map.cellIndices.CellToIndex(new IntVec3(x, 0, z))] = wall;
        }
        bool Free(IntVec3 c) => c.x >= 20 && c.x <= 29 && c.z >= 14 && c.z <= 19
            && !(c.z == 19 && c.x >= 24) && !(c.z == 14 && c.x >= 28);
        var opening = new IntVec3(19, 0, 17); // West partition into the adjacent room.
        int side = 0; var posts = new List<IntVec3>();
        bool formed = TacticalStackFormation.Build(opening, IntVec3.West, 13, ref side, Free,
            c => GenSight.LineOfSight(opening, c, map, true), posts);
        check(formed && posts.Count == 13, "Workshop must form a full squad after relaxing LOS before using a partial party.");
        check(TacticalStackFormation.Build(opening, IntVec3.West, 13, ref side, Free,
            c => GenSight.LineOfSight(opening, c, map, true), posts, allowPartial: true)
            && posts.Count == 13 && TacticalStackFormation.ConnectedWings(opening, IntVec3.West, posts),
            "Workshop west partition must admit a small connected breach party instead of stalling the whole squad.");
        Console.WriteLine("Workshop west partition: connected breach party=" + posts.Count + "/13");
        var plan = new TacticalLocalPlan { Opening = opening, Inward = IntVec3.West }; plan.Stack.AddRange(posts);
        var previous = new List<IntVec3>();
        for (int x = 20; x <= 29 && previous.Count < 13; x++) for (int z = 14; z <= 19 && previous.Count < 13; z++)
        { var cell = new IntVec3(x, 0, z); if (Free(cell)) previous.Add(cell); }
        check(TacticalEntryAllocation.BindApproach(plan, previous, Enumerable.Range(0, 13).ToArray(), Free)
            && plan.Stack.Count == 13 && plan.Stack.Distinct().Count() == 13
            && TacticalEntryAllocation.ConnectedStack(plan) ,
            "Small approach must keep unique secured guard posts without combining them into the breach formation.");
        plan.Positions.AddRange(new[] { new IntVec3(17, 0, 17), new IntVec3(17, 0, 18) });
        check(TacticalEntryAllocation.Allocate(plan, plan.StackMembers)
            && plan.StackMembers.All(i => !plan.RetainedOutside.Contains(i) || plan.Stack.Contains(plan.Positions[i]))
            && plan.Positions.Distinct().Count() == plan.Stack.Count,
            "Only the selected breach party may take inside posts; remaining members get unique entrance guards.");
    }

    private static void CheckSave(Action<bool, string> check, IntVec3 opening)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Stack-" + Guid.NewGuid() + ".xml");
        try
        {
            var plan = new TacticalLocalPlan { Opening = opening, Inward = IntVec3.East, StackSide = -1 };
            plan.Stack.Add(opening - IntVec3.East + IntVec3.South * 2);
            plan.StackMembers.Add(0);
            Scribe.saver.InitSaving(path, "plan"); plan.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalLocalPlan(); Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            check(loaded.StackSide == -1 && loaded.Stack.SequenceEqual(plan.Stack) && loaded.StackMembers.SequenceEqual(plan.StackMembers),
                "Native save must retain the observation preference, indexed posts and partial approach members.");
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
