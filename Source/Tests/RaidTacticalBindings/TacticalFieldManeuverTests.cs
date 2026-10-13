using System;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalFieldManeuverTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
        Check(TacticalFieldPolicy.Continue(1000, 500, -1), "Short observation gaps must preserve the field mission.");
        Check(TacticalFieldPolicy.Continue(1000, 0, 900), "Actual recent shots must preserve combat after stale scheduled observation.");
        Check(!TacticalFieldPolicy.Continue(900, 0, -1), "A lost mission must terminate after its bounded grace period.");
        Check(!TacticalFieldPolicy.Prepare(false, false, false, 30, 31 * 31), "Attackers must keep approaching a distant passive observation.");
        Check(TacticalFieldPolicy.Prepare(false, false, false, 30, 30 * 30), "Targets at the weapon boundary allow preparation.");
        Check(TacticalFieldPolicy.Prepare(true, false, false, 30, 10000)
            && TacticalFieldPolicy.Prepare(false, true, false, 30, 10000)
            && TacticalFieldPolicy.Prepare(false, false, true, 30, 10000), "Defense, support and incoming fire retain early responses.");
        Check(AccessTools.Method(typeof(Pawn), nameof(Pawn.Notify_UsedVerb), new[] { typeof(Pawn), typeof(Verb) }) != null,
            "Actual successful-shot notifications must bind the installed native game signature.");
        Console.WriteLine("PASS: " + checks + " field feedback, contact grace and attack approach checks.");
    }
}
