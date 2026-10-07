using System;
using Helodrace;
using Helodrace.Tactics;

internal static class TacticalChargeTests
{
    internal static void Run()
    {
        var props = new CompProperties_InstalledBreachCharge { beyondFragmentRadius = 8,
            explosionRadiusBase = 1.5f, explosionRadiusPerC4 = .3f };
        if (TacticalChargePolicy.SafeRadius(props, 1) < 9) throw new Exception("Withdrawal must include far-side fragment reach.");
        props.explosionRadiusBase = 12;
        if (TacticalChargePolicy.SafeRadius(props, 4) < 13.6f) throw new Exception("Large charges must extend withdrawal beyond blast reach.");
        if (!TacticalChargePolicy.EffectsPending(true, false, false, 0, 100)
            || !TacticalChargePolicy.EffectsPending(false, true, false, 0, 100)
            || !TacticalChargePolicy.EffectsPending(false, false, true, 0, 100)
            || !TacticalChargePolicy.EffectsPending(false, false, false, -1, 100)
            || !TacticalChargePolicy.EffectsPending(false, false, false, 70, 99)
            || TacticalChargePolicy.EffectsPending(false, false, false, 70, 100))
            throw new Exception("Live fuse, fragments, explosion and thirty settled ticks must all gate entry.");
        Console.WriteLine("PASS: C4 blast/fragment withdrawal extent and live fuse/fragment/explosion/settling safety.");
    }
}
