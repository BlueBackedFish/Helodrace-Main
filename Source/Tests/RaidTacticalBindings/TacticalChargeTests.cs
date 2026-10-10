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
        Console.WriteLine("PASS: C4 blast/fragment withdrawal extent and live fuse/fragment/explosion/settling safety; unlit fallback preserves allocation and rejects live explosives/missing backup.");
    }
}
