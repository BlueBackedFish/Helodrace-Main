using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalSupportTests
{
    public static void Run()
    {
        if (!TacticalSupportPolicy.Hold(599, 600, false) || TacticalSupportPolicy.Hold(600, 600, false)
            || !TacticalSupportPolicy.Hold(3000, 600, true))
            throw new Exception("Support wait must respect the minimum and a still-active strike.");
        if (TacticalSupportPolicy.CanAdvance(true, false) || TacticalSupportPolicy.CanAdvance(false, true)
            || !TacticalSupportPolicy.CanAdvance(false, false))
            throw new Exception("Fixed defense and actual support waits must never begin forward bounds.");
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.jobs = (Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        var job = (Job)RuntimeHelpers.GetUninitializedObject(typeof(Job));
        job.def = new JobDef { defName = "HD_CASStationaryGuidance" };
        AccessTools.Field(typeof(Pawn_JobTracker), "curJob").SetValue(pawn.jobs, job);
        var member = new TacticalMemberCommand { Pawn = pawn };
        var check = AccessTools.Method(typeof(MapComponent_TacticalCommands), "StationaryGuidance");
        if (!(bool)check.Invoke(null, new object[] { member })) throw new Exception("Actual CAS guidance Job not protected.");
        job.def = new JobDef { defName = "HD_NewTacticalContactGuard" };
        if ((bool)check.Invoke(null, new object[] { member })) throw new Exception("Ordinary tactical guard incorrectly protected as CAS guidance.");
        Console.WriteLine("PASS: support minimum/active-strike expiry, nonadvancing defense and actual native guidance Job protection.");
    }
}
