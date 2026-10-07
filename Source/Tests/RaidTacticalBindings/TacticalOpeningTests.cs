using System;
using System.Collections.Generic;
using Helodrace.Tactics;
using Verse;

internal static class TacticalOpeningTests
{
    internal static void Run()
    {
        if (TacticalOpeningPolicy.WantsSupport(false, 16, false)
            || !TacticalOpeningPolicy.WantsSupport(false, 16, true)
            || !TacticalOpeningPolicy.WantsSupport(false, 17, false)
            || !TacticalOpeningPolicy.WantsSupport(true, 1, false)) throw new Exception("Room/contact support policy failed.");
        var cells = new HashSet<IntVec3>();
        for (int x = 0; x < 4; x++) for (int z = 0; z < 4; z++) cells.Add(new IntVec3(x,0,z));
        if (TacticalOpeningPolicy.ClassifySize(IntVec3.Zero, cells.Contains) != 16) throw new Exception("Small room must not overflow.");
        cells.Add(new IntVec3(4,0,0)); cells.Add(new IntVec3(5,0,0));
        if (TacticalOpeningPolicy.ClassifySize(IntVec3.Zero, cells.Contains) != 17) throw new Exception("Large room classification must cap at 17.");
        int reads = 0;
        if (TacticalOpeningPolicy.ClassifySize(IntVec3.Zero, cell => { reads++; return true; }) != 17 || reads > 69)
            throw new Exception("An unbounded outdoor region must still use bounded probes.");
        if (TacticalOpeningPolicy.FarEnough(IntVec3.Zero, IntVec3.North)
            || !TacticalOpeningPolicy.FarEnough(IntVec3.Zero, IntVec3.North * 2)) throw new Exception("Opening throw distance failed.");
        if (!TacticalOpeningPolicy.EffectsPending(true, false, true, 0, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, true, true, 0, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, false, false, 0, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, false, true, -1, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, false, true, 100, 129)
            || TacticalOpeningPolicy.EffectsPending(false, false, true, 100, 130))
            throw new Exception("Entry must wait for projectile, explosion damage, return and settling.");
        if (TacticalOpeningPolicy.ReleaseTicks != 18 || TacticalOpeningPolicy.ObservationTicks != 90)
            throw new Exception("Release/observation timings changed.");
        Console.WriteLine("PASS: bounded room classification, small-room/contact exception, throw distance, projectile/explosion/return safety.");
    }
}
