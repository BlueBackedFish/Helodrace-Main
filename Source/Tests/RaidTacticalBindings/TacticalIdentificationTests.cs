using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalIdentificationTests
{
    internal static void Run()
    {
        var service = new MapComponent_TacticalCommands(null);
        var advance = AccessTools.Method(service.GetType(), "AdvanceCoordination");
        foreach (TacticalCommandPhase phase in new[] { TacticalCommandPhase.Clear, TacticalCommandPhase.Enter,
            TacticalCommandPhase.Stack, TacticalCommandPhase.Breach, TacticalCommandPhase.Observe, TacticalCommandPhase.Support })
        {
            IntVec3 inside = new IntVec3(11,0,10);
            var command = new TacticalSquadCommand { Phase = phase, PhaseStarted = 80, Goal = inside, GoalSecured = true,
                Plan = new TacticalLocalPlan { Opening = new IntVec3(10,0,10), Inward = IntVec3.East },
                OpeningAction = new TacticalOpeningAction { ObservationDone = true, EffectsCleared = true } };
            command.RoomScan = new TacticalRoomScan(inside, command.Plan.Opening, cell => cell == inside, cell => false);
            var scan = command.RoomScan;
            command.SecuredCells.Add(inside); command.SecuredPlans.Add(command.Plan);
            var member = new TacticalMemberCommand { Passed = true, Crossed = true, EverEntered = true };
            command.Members.Add(member);
            command.Link.IdentifyStarted = 100; command.Link.IdentifyUntil = 145;
            // Exercise the real phase/hold logic without native pawn jobs. Pawn
            // parking and movement are covered by the isolated live raid.
            var active = new List<TacticalMemberCommand>();
            bool Held(int tick) => (bool)advance.Invoke(service, new object[] { command, active, tick });
            var expected = phase == TacticalCommandPhase.Support ? TacticalCommandPhase.Observe : phase;
            if (!Held(101) || command.Phase != expected || command.OpeningAction != null
                || !command.Link.IdentificationHolding || !ReferenceEquals(scan, command.RoomScan)
                || command.SecuredPlans.Count != 1 || !command.SecuredCells.Contains(inside) || !command.GoalSecured)
                throw new Exception("Identification must not rewind ingress/clear or replace secured progress: " + phase);
            if (Held(145) != (phase == TacticalCommandPhase.Clear) || command.Phase != expected || command.Link.IdentificationHolding
                || command.ContactRestoring != (phase == TacticalCommandPhase.Clear)
                || command.PhaseStarted != 125 || !member.Passed || !member.Crossed || !member.EverEntered
                || !ReferenceEquals(scan, command.RoomScan) || command.SecuredPlans.Count != 1)
                throw new Exception("Identification must resume the same phase, survey and actual crossing history: " + phase);
            if (phase == TacticalCommandPhase.Clear && command.Due != 146)
                throw new Exception("Clear must yield to post restoration next tick before it may complete/return.");
        }
        var live = new TacticalSquadCommand { Phase = TacticalCommandPhase.BlastWait,
            OpeningAction = new TacticalOpeningAction { Launched = true, EffectsCleared = false } };
        live.Link.IdentifyUntil = 145;
        var action = live.OpeningAction;
        if ((bool)advance.Invoke(service, new object[] { live, new List<TacticalMemberCommand>(), 101 })
            || live.Link.IdentificationHolding || live.Phase != TacticalCommandPhase.BlastWait || live.OpeningAction != action)
            throw new Exception("Identification must not interrupt live grenade safety.");
        live.Phase = TacticalCommandPhase.Breach; live.ChargeAction = new TacticalChargeAction();
        live.OpeningAction = null;
        if ((bool)advance.Invoke(service, new object[] { live, new List<TacticalMemberCommand>(), 101 })
            || live.Link.IdentificationHolding || live.Phase != TacticalCommandPhase.Breach || live.ChargeAction == null)
            throw new Exception("Identification must not interrupt installed charge safety.");
        Console.WriteLine("PASS: real identification hold/resume preserves ingress/clear progress and live explosive safety.");
    }
}
