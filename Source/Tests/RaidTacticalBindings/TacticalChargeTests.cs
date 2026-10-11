using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using Helodrace;
using Helodrace.Tactics;

internal static class TacticalChargeTests
{
    internal static void Run()
    {
        if (TacticalChargePolicy.CanTimeoutFallback(true, false, false, 1200, 2400)
            || TacticalChargePolicy.CanTimeoutFallback(false, true, false, 1200, 2400)
            || TacticalChargePolicy.CanTimeoutFallback(false, false, true, 1200, 2400)
            || TacticalChargePolicy.CanTimeoutFallback(false, false, false, 1200, 1200)
            || !TacticalChargePolicy.CanTimeoutFallback(false, false, false, 1200, 1201))
            throw new Exception("Timeout must not cancel installed C4, ongoing installation or live effects; only failed uninstalled work may fall back.");
        foreach (IntVec3 inward in GenAdj.CardinalDirections)
        {
            IntVec3 position = new IntVec3(20, 0, 20), tangent = new IntVec3(-inward.z, 0, inward.x);
            int unsafeCells = 0;
            for (int depth = -2; depth <= 8; depth++) for (int width = -4; width <= 4; width++)
            {
                bool unsafeCell = TacticalChargePolicy.UnsafeApproach(position, inward, position - inward * depth + tangent * width);
                if (unsafeCell != (depth >= 0 && depth < 6 && Math.Abs(width) <= 1))
                    throw new Exception("C4 exclusion must rotate with the actual installation face.");
                if (unsafeCell) unsafeCells++;
            }
            if (unsafeCells != 18) throw new Exception("C4 approach must exclude exactly 3x6 cells, not a fragment-radius circle.");
        }
        if (!TacticalChargePolicy.EffectsPending(true, false, false, 0, 100)
            || !TacticalChargePolicy.EffectsPending(false, true, false, 0, 100)
            || !TacticalChargePolicy.EffectsPending(false, false, true, 0, 100)
            || !TacticalChargePolicy.EffectsPending(false, false, false, -1, 100)
            || !TacticalChargePolicy.EffectsPending(false, false, false, 70, 99)
            || TacticalChargePolicy.EffectsPending(false, false, false, 70, 100))
            throw new Exception("Live fuse, fragments, explosion and thirty settled ticks must all gate entry.");
        var command = new TacticalSquadCommand { Id = "a", Phase = TacticalCommandPhase.Breach,
            Plan = new TacticalLocalPlan { Opening = new IntVec3(10, 0, 10), Inward = IntVec3.East },
            Goal = new IntVec3(20, 0, 10), PhaseStarted = 10, BarrierHitPoints = 100 };
        command.Link.Cooperation.Agenda = new TacticalCooperationAgenda("pair", "a", "b", command.Goal, IntVec3.East, 90, 6000);
        command.Link.Cooperation.Stage = TacticalAgreementStage.Started;
        command.SecuredCells.Add(new IntVec3(12, 0, 10));
        var plan = command.Plan; var agenda = command.Link.Cooperation.Agenda;
        AccessTools.Method(typeof(MapComponent_TacticalCommands), "ResumeHammerBreach")
            .Invoke(null, new object[] { command, null, 1500 });
        if (command.Plan != plan || !plan.HammerFallback || command.Link.Cooperation.Agenda != agenda
            || command.Link.Cooperation.Stage != TacticalAgreementStage.Started || command.SecuredCells.Count != 1
            || command.Goal != new IntVec3(20, 0, 10) || command.Phase != TacticalCommandPhase.Breach
            || command.PhaseStarted != 1500 || command.Due != 1501 || command.BarrierHitPoints != -1)
            throw new Exception("Unlit-charge fallback must preserve the same plan/allocation/history and restart bounded hammer work.");
        var service = (MapComponent_TacticalCommands)RuntimeHelpers.GetUninitializedObject(typeof(MapComponent_TacticalCommands));
        var fallback = AccessTools.Method(typeof(MapComponent_TacticalCommands), "TryChargeHammerFallback");
        var charge = new CompInstalledBreachCharge();
        foreach (bool triggered in new[] { false, true })
        {
            AccessTools.Field(typeof(CompInstalledBreachCharge), "triggered").SetValue(charge, triggered);
            foreach (bool detonated in new[] { false, true })
            {
                var action = new TacticalChargeAction { Charge = charge, Detonated = detonated };
                command.ChargeAction = action;
                if ((bool)fallback.Invoke(service, new object[] { command, new List<TacticalMemberCommand>(), 2000 })
                    || command.ChargeAction != action || command.Plan != plan || command.Link.Cooperation.Agenda != agenda)
                    throw new Exception("A live explosive or absent backup hammer must never be abandoned by the fallback transition.");
            }
        }
        Console.WriteLine("PASS: C4 3x6 directional exclusion in four orientations and effects settling; installed/working C4 survives timeout with mission history preserved.");
    }
}
