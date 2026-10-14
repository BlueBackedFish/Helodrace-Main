using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalPathAdmissionTests
{
    private sealed class Driver : TacticalJobDriver
    {
        internal bool Start() => StartAdmittedPath(IntVec3.Zero);
        protected override IEnumerable<Toil> MakeNewToils() { yield break; }
    }
    internal static void Run()
    {
        var pawn=new Pawn();
        AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(pawn,(sbyte)0);
        pawn.health=(Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        pawn.jobs=(Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        var health=AccessTools.Field(typeof(Pawn_HealthTracker),"healthState");
        var job=new Job(); AccessTools.Field(typeof(Pawn_JobTracker),"curJob").SetValue(pawn.jobs,job);
        var driver=new Driver {pawn=pawn,job=job};
        health.SetValue(pawn.health,PawnHealthState.Down);
        if(driver.Start()) throw new Exception("A downed actor must not admit a new path.");
        health.SetValue(pawn.health,PawnHealthState.Dead);
        if(driver.Start()) throw new Exception("A dead actor must not admit a new path.");
        health.SetValue(pawn.health,PawnHealthState.Mobile); driver.job=new Job();
        if(driver.Start()) throw new Exception("An obsolete driver must not admit a path for a replaced job.");
        var start=AccessTools.Method(typeof(TacticalJobDriver),"StartAdmittedPath");
        var generator=new DynamicMethod("PathAdmissionInstructions",typeof(void),Type.EmptyTypes).GetILGenerator();
        var code=PatchProcessor.GetOriginalInstructions(start,generator);
        int admission=code.FindIndex(i=>i.Calls(AccessTools.Method(typeof(TacticalWorkBudget),"TryPath")));
        int native=code.FindIndex(i=>i.Calls(AccessTools.Method(typeof(Pawn_PathFollower),"StartPath")));
        if(admission<0 || native<=admission || !code.Any(i=>i.Calls(AccessTools.Method(typeof(TacticalWorkBudget),"Account")))
            || !start.GetMethodBody().ExceptionHandlingClauses.Any(c=>c.Flags==ExceptionHandlingClauseOptions.Finally))
            throw new Exception("Native synchronous path startup must be admitted first and accounted in finally.");
        Console.WriteLine("PASS: path startup rejects incapacitated/stale owners and accounts synchronous work in finally.");
    }
}
