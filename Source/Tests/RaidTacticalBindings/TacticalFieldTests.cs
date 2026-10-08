using System;
using System.Linq;
using Helodrace.Tactics;
using Verse;
using HarmonyLib;

internal static class TacticalFieldTests
{
    public static void Run()
    {
        var memory = new TacticalContactMemory();
        IntVec3 anchor = new IntVec3(10, 0, 10), first = new IntVec3(10, 0, 60);
        memory.Remember(1, first, first, false, 100, "a");
        TacticalContact contact = memory.Entries.Single();
        if (TacticalFieldPolicy.Motion(contact, anchor) != TacticalObservedMotion.Unknown)
            throw new Exception("First observation invented a motion sample.");
        void Move(IntVec3 next, int tick, TacticalObservedMotion expected)
        {
            memory.Remember(1, next, next, false, tick, "a");
            if (TacticalFieldPolicy.Motion(contact, anchor) != expected) throw new Exception("Motion: " + expected);
        }
        Move(new IntVec3(10, 0, 55), 145, TacticalObservedMotion.Approaching);
        Move(new IntVec3(15, 0, 55), 190, TacticalObservedMotion.Crossing);
        Move(new IntVec3(15, 0, 65), 235, TacticalObservedMotion.Leaving);
        Move(new IntVec3(15, 0, 65), 280, TacticalObservedMotion.Stationary);
        Move(new IntVec3(15, 0, 60), 700, TacticalObservedMotion.Unknown);
        Move(new IntVec3(15, 0, 55), 745, TacticalObservedMotion.Approaching);
        TacticalContact report = contact.Copy();
        var peer = new TacticalContactMemory(); peer.Receive(report, 750);
        if (TacticalFieldPolicy.Motion(peer.Entries.Single(), anchor) != TacticalObservedMotion.Approaching
            || peer.Entries.Single().PreviousPosition != report.PreviousPosition)
            throw new Exception("Report lost its observed history.");
        memory.Remember(1, first, first, false, 790, "other-observer");
        if (TacticalFieldPolicy.Motion(contact, anchor) != TacticalObservedMotion.Unknown)
            throw new Exception("Unrelated reports were treated as continuous tracking.");
        var arc = Enumerable.Range(0, 13).Select(i => TacticalFieldPolicy.Arc(anchor, IntVec3.North, i, 13, 8)).ToArray();
        if (arc.Distinct().Count() != 13 || arc[6].z <= arc[0].z || arc[0].z != arc[12].z)
            throw new Exception("Bow formation is not unique, symmetric and forward-curved.");
        if (TacticalFieldPolicy.SightRadius <= TacticalContactState.Radius)
            throw new Exception("Field sight does not extend the CQB radius.");
        if (!TacticalMedicalPolicy.Urgent(.4f) || TacticalMedicalPolicy.Urgent(.399f)
            || !TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, true, null)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, false, null)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.BlastWait, true, null)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Stack, true, null)
            || !TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Stack, true, TacticalFieldStage.Defending)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, true, TacticalFieldStage.Screening)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Clear, true, TacticalFieldStage.Moving))
            throw new Exception("Care may only interrupt a secured room or a settled field defense.");
        if (AccessTools.DeclaredMethod(typeof(Pawn_HealthTracker), "MakeUndowned") == null
            || AccessTools.Field(typeof(Pawn_HealthTracker), "pawn")?.FieldType != typeof(Pawn))
            throw new Exception("Native recovery wake binding is missing.");
        Console.WriteLine("PASS: R6 first-sample, approach/cross/leave/stationary, stale-gap, copied report history, origin change and bow geometry checks.");
    }
}
