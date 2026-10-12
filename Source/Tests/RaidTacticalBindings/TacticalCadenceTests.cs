using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalCadenceTests
{
    internal static void Run()
    {
        var service = new MapComponent_TacticalCommands(null);
        var squads = (Dictionary<string, TacticalSquadCommand>)AccessTools.Field(service.GetType(), "squads").GetValue(service);
        var first = new TacticalSquadCommand { Id = "a", Phase = TacticalCommandPhase.Pending };
        var second = new TacticalSquadCommand { Id = "b", Phase = TacticalCommandPhase.Pending };
        squads.Add(first.Id, first); squads.Add(second.Id, second);
        var method = AccessTools.Method(service.GetType(), "AdvanceCoordination");
        bool Held(int tick) => (bool)method.Invoke(service, new object[] { first, new List<TacticalMemberCommand>(), tick });
        if (Held(1) || Held(100)) throw new Exception("Two squads without an actual agreement must begin planning instead of holding for a hypothetical partner.");
        first.Link.Cooperation.Agenda = new TacticalCooperationAgenda("ab", "a", "b", IntVec3.Zero, IntVec3.East, 300, 6000);
        first.Link.Cooperation.Stage = TacticalAgreementStage.Offered;
        first.Link.Cooperation.NegotiationStarted = 1;
        if (!Held(50) || first.Due != 60) throw new Exception("Actual negotiation retains ownership and checks again in ten ticks.");
        first.Link.Cooperation.Stage = TacticalAgreementStage.Agreed;
        if (Held(60)) throw new Exception("A confirmed agreement may prepare its stack before the agreed breach time.");
        first.Phase = TacticalCommandPhase.Stack;
        if (TacticalCadence.DecisionInterval(first.Phase) != 10 || TacticalCadence.DecisionInterval(TacticalCommandPhase.Enter) != 10
            || TacticalCadence.DecisionInterval(TacticalCommandPhase.Complete) < 120)
            throw new Exception("Active movement polls quickly; completed missions must retain cheap idle polling.");
        Console.WriteLine("PASS: no hypothetical-partner startup hold, actual negotiation preserved, ten-tick active cadence and idle throttling.");
    }
}
