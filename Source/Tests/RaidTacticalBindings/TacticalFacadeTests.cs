using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalFacadeTests
{
    internal static void Run()
    {
        IntVec3 root = new IntVec3(100,0,110);
        var supports = new HashSet<IntVec3> { root, root + IntVec3.North, root - IntVec3.North };
        var method = AccessTools.Method(typeof(TacticalPortalGeometry), "TryWallNormal");
        bool Face(IntVec3 travel, IntVec3 expected, bool found = true)
        {
            object[] args = { root, travel, (Func<IntVec3,bool>)supports.Contains, IntVec3.Invalid };
            return (bool)method.Invoke(null, args) == found && (IntVec3)args[3] == expected;
        }
        if (!Face(new IntVec3(7,0,20), IntVec3.East) || !Face(new IntVec3(-7,0,20), IntVec3.West))
            throw new Exception("Diagonal approach must cross the wall's actual face rather than use its dominant travel axis.");
        supports.Remove(root - IntVec3.North);
        if (!Face(new IntVec3(7,0,20), IntVec3.East))
            throw new Exception("A wall beside a destroyed segment still supplies a facade orientation.");
        if (!Face(IntVec3.North, IntVec3.Invalid, false))
            throw new Exception("Travel parallel to a facade is not a wall crossing.");
        supports.Clear(); supports.Add(root);
        if (!Face(IntVec3.East, IntVec3.Invalid, false))
            throw new Exception("An isolated rock/block must not choose the whole squad's entry face.");
        supports.Add(root + IntVec3.East); supports.Add(root - IntVec3.East);
        if (!Face(new IntVec3(20,0,-7), IntVec3.South))
            throw new Exception("A horizontal wall must retain its north/south face on diagonal approach.");
        supports.Clear();
        supports.Add(root + IntVec3.North * 3); supports.Add(root - IntVec3.North * 3);
        var gapMethod = AccessTools.Method(typeof(TacticalPortalGeometry), "TryGapNormal");
        object[] gap = { root, (Func<IntVec3,bool>)supports.Contains, (Func<IntVec3,bool>)(c => !supports.Contains(c)), IntVec3.Invalid };
        if (!(bool)gapMethod.Invoke(null, gap) || Math.Abs(((IntVec3)gap[3]).x) != 1 || ((IntVec3)gap[3]).z != 0)
            throw new Exception("Five-cell gap detection must also return its real wall normal.");
        var search = AccessTools.Method(typeof(TacticalLocalPlanner), "SearchBand");
        int Band(int failures) => (int)search.Invoke(null, new object[] { failures });
        var bands = new HashSet<int>();
        for (int failures = 0; failures < 100; failures++)
        {
            int band = Band(failures);
            if (Math.Abs(band) > 24 || failures < 2 && band != 0)
                throw new Exception("Initial cooperative frontage and bounded search extent must survive retries.");
            if (failures >= 2) bands.Add(band);
        }
        if (!new HashSet<int> { -24, -12, 0, 12, 24 }.IsSubsetOf(bands) || Band(int.MinValue) != 0 || Math.Abs(Band(int.MaxValue)) > 24)
            throw new Exception("Retry sampling must cover both flanks instead of repeating a permanently obstructed frontage.");
        var offsets = (int[])AccessTools.Field(typeof(TacticalLocalPlanner), "Offsets").GetValue(null);
        if (offsets.Length > 8) throw new Exception("Retry coverage must not increase the per-attempt candidate budget.");
        // Native High13 shared-door failure: the two approach rays meet the wall
        // at z115/z113; their only open door is z108. Both must be sampled early.
        foreach (int doorOffset in new[] { -7, -5 })
        {
            bool sampled = false;
            for (int failures = 2; failures <= 3; failures++)
                foreach (int offset in offsets) sampled |= offset + Band(failures) == doorOffset;
            if (!sampled) throw new Exception("Nearby open door is skipped by bounded retries: " + doorOffset);
        }
        Console.WriteLine("PASS: diagonal/fragmented wall and widened-gap normals, isolated-block rejection and bounded retry frontage coverage.");
    }
}
