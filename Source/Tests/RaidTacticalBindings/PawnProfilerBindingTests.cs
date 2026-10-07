using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class PawnProfilerBindingTests
{
    internal static void Run()
    {
        var assembly = typeof(RaidTacticalPlan).Assembly;
        var targets = (MethodInfo[])AccessTools.Method(assembly.GetType("Helodrace.Profiling.PawnProfileTargets", true), "Resolve")
            .Invoke(null, null);
        string[] required = { "JobTrackerTick", "JobTrackerTickInterval", "DriverTick", "DriverTickInterval",
            "HealthTickInterval", "MindStateTickInterval", "NeedsTrackerTickInterval", "GeneTrackerTickInterval" };
        foreach (string name in required)
            if (!targets.Any(m => m.Name == name)) throw new Exception("Missing native pawn scope: " + name);
        if (targets.Length > 75 || targets.Any(m => m.IsSpecialName || m.IsAbstract || m.ContainsGenericParameters)
            || !targets.SequenceEqual((MethodInfo[])AccessTools.Method(assembly.GetType("Helodrace.Profiling.PawnProfileTargets"), "Resolve").Invoke(null, null)))
            throw new Exception("Pawn targets must be bounded, concrete and stable.");
        var profiler = assembly.GetType("Helodrace.Profiling.AgentMethodProfiler", true);
        foreach (var method in targets)
        {
            string prefix = (string)AccessTools.Method(profiler, "Prefix").Invoke(null, new object[] { method });
            if (prefix == "EnterTracker" && AccessTools.Field(method.DeclaringType, "pawn")?.FieldType != typeof(Pawn))
                throw new Exception("Invalid pawn tracker binding: " + method);
            if (prefix == "EnterPawnArgument" && !method.GetParameters().Any(p => p.Name == "pawn" && p.ParameterType == typeof(Pawn)))
                throw new Exception("Invalid pawn argument binding: " + method);
        }
        Console.WriteLine("PASS: " + targets.Length + " native pawn profiler scopes and typed context bindings.");
    }
}
