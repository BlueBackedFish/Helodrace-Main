using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalEntryClaimTests
{
    internal static void Run()
    {
        var service = new MapComponent_TacticalCommands(null);
        var command = new TacticalSquadCommand { Plan = new TacticalLocalPlan {
            Opening = new IntVec3(10,0,10), Inward = IntVec3.East, EntryLane = new IntVec3(12,0,10) } };
        var other = new TacticalSquadCommand();
        TacticalLocalPlan plan = command.Plan;
        IntVec3 guard = new IntVec3(9,0,11), vacant = new IntVec3(8,0,11), overlay = new IntVec3(8,0,12);
        plan.Stack.AddRange(new[] { guard, vacant, overlay });
        plan.Positions.AddRange(new[] { guard, new IntVec3(11,0,11) });
        plan.RetainedOutside.Add(0);
        command.ContactResponse = new TacticalContactResponse();
        command.ContactResponse.Occupied.Add(overlay);
        var claims = (IDictionary<IntVec3,TacticalSquadCommand>)AccessTools.Field(service.GetType(), "claims").GetValue(service);
        var leases = (IDictionary<IntVec3,TacticalSquadCommand>)AccessTools.Field(service.GetType(), "leases").GetValue(service);
        foreach (IntVec3 cell in new[] { guard, vacant, overlay, plan.Positions[1], plan.Outside, plan.Opening, plan.Inside })
            claims.Add(cell, command);
        claims.Add(plan.EntryLane, other); leases.Add(plan.Opening, command);
        var retire = AccessTools.Method(service.GetType(), "RetireEntryApproach");
        retire.Invoke(service, new object[] { command });
        if (leases.Count != 0 || claims.ContainsKey(vacant) || claims.ContainsKey(plan.Outside)
            || claims.ContainsKey(plan.Opening) || claims.ContainsKey(plan.Inside)
            || claims.Count != 4 || claims[guard] != command || claims[overlay] != command
            || claims[plan.Positions[1]] != command || claims[plan.EntryLane] != other)
            throw new Exception("Completed ingress must free its unused mouth/approach while retaining guards, posts, overlays and foreign claims.");
        retire.Invoke(service, new object[] { command });
        if (claims.Count != 4) throw new Exception("Restored clear-phase survey must not change already retired claims.");
        var original = AccessTools.Method(service.GetType(), "OriginalClaim");
        command.Phase = TacticalCommandPhase.Clear;
        if ((bool)original.Invoke(null, new object[] { command, vacant })
            || (bool)original.Invoke(null, new object[] { command, plan.Outside })
            || !(bool)original.Invoke(null, new object[] { command, guard })
            || !(bool)original.Invoke(null, new object[] { command, plan.Positions[1] }))
            throw new Exception("Contact/identification cleanup must not resurrect retired approach claims in Clear.");
        command.Phase = TacticalCommandPhase.Enter;
        if (!(bool)original.Invoke(null, new object[] { command, vacant }))
            throw new Exception("In-progress ingress must preserve its approach when temporary overlays finish.");
        leases.Add(plan.Opening, other);
        retire.Invoke(service, new object[] { command });
        if (leases[plan.Opening] != other) throw new Exception("Approach retirement must never remove another squad's lease.");
        plan.Direct = true; claims.Add(vacant, command);
        retire.Invoke(service, new object[] { command });
        if (!claims.ContainsKey(vacant)) throw new Exception("Direct field posts have no entry approach to retire.");
        Console.WriteLine("PASS: completed entry releases unused approach; guards, interior posts, overlays, foreign owners and reload idempotence retained.");
    }
}
