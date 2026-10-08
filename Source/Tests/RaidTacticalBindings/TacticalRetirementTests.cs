using System;
using System.Collections.Generic;
using System.Reflection;
using Helodrace.Tactics;
using Verse;

internal static class TacticalRetirementTests
{
    public static void Run()
    {
        var service = new TacticalCommunications();
        var retired = new TacticalSquadCommand { Id = "retired" };
        var offerer = new TacticalSquadCommand { Id = "offerer", Due = 900 };
        var detached = new TacticalSquadCommand { Id = "detached", Due = 900 };
        var unrelated = new TacticalSquadCommand { Id = "unrelated", Due = 900 };
        var agenda = new TacticalCooperationAgenda("offer", offerer.Id, retired.Id,
            IntVec3.Zero, IntVec3.East, 100, 1000);
        offerer.Link.Peer = retired;
        offerer.Link.Cooperation.Agenda = agenda;
        offerer.Link.Cooperation.Stage = TacticalAgreementStage.Offered;
        detached.Link.Cooperation.Agenda = new TacticalCooperationAgenda("accepted", retired.Id, detached.Id,
            IntVec3.Zero, IntVec3.East, 100, 1000);
        detached.Link.Cooperation.Stage = TacticalAgreementStage.Accepted;
        foreach (var command in new[] { offerer, detached, unrelated }) command.Link.Identified[retired.Id] = 5;
        var pending = (List<TacticalMessage>)typeof(TacticalCommunications).GetField("pending",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
        pending.Add(new TacticalMessage { From = offerer, To = retired, Kind = TacticalMessageKind.Offer });
        pending.Add(new TacticalMessage { From = retired, To = detached });
        var retained = new TacticalMessage { From = offerer, To = unrelated };
        pending.Add(retained);
        typeof(TacticalCommunications).GetMethod("Forget", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(service, new object[] { retired, new List<TacticalSquadCommand> { offerer, detached, unrelated }, 50 });
        if (offerer.Link.Peer != null || offerer.Link.Cooperation.Stage != TacticalAgreementStage.Aborted
            || offerer.Due != 51 || detached.Link.Cooperation.Stage != TacticalAgreementStage.Aborted || detached.Due != 51)
            throw new Exception("Retirement must invalidate incoming-only offers and detached agreements immediately.");
        if (unrelated.Due != 900 || unrelated.Link.Cooperation.Stage != TacticalAgreementStage.None
            || pending.Count != 1 || pending[0] != retained || service.MessagesDropped != 2)
            throw new Exception("Retirement must preserve unrelated commands and messages.");
        foreach (var command in new[] { offerer, detached, unrelated })
            if (command.Link.Identified.ContainsKey(retired.Id)) throw new Exception("Retired identity reference remained.");
        Console.WriteLine("PASS: one-sided offer/detached agreement retirement, bounded packet cleanup and unrelated-state preservation.");
    }
}
