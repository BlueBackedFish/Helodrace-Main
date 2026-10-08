using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helodrace;
using Verse;
using Verse.AI;
using RimWorld;
using System.Collections.Generic;

internal static class PawnProfilerBindingTests
{
    internal static void Run()
    {
        var assembly = typeof(TacticalEngineSelection).Assembly;
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
        var resolver = AccessTools.Method(assembly.GetType("Helodrace.Profiling.FocusedProfileTargets", true), "Resolve");
        foreach (string preset in new[] { "needs-spikes", "jobs-spikes", "path-spikes" })
        {
            var focused = (MethodInfo[])resolver.Invoke(null, new object[] { preset });
            if (focused.Length == 0 || focused.Length > 64 || focused.Any(m => m.IsSpecialName || m.IsAbstract || m.ContainsGenericParameters)
                || !focused.SequenceEqual((MethodInfo[])resolver.Invoke(null, new object[] { preset })))
                throw new Exception("Invalid or unbounded focused scopes: " + preset);
            if (preset == "needs-spikes" && (!focused.Any(m => m.Name == "NeedsTrackerTickInterval")
                || !focused.Any(m => m.Name == "NeedInterval" && m.DeclaringType.Name == "Need_Food")
                || !focused.Any(m => m.Name == "NeedInterval" && m.DeclaringType.Name == "Need_Mood")))
                throw new Exception("Concrete need dispatch is missing.");
            if (preset == "jobs-spikes" && !focused.Any(m => m.Name == "TryFindCastPosition"))
                throw new Exception("Shooting position search boundary is missing.");
            if (preset == "path-spikes" && !focused.Any(m => m.Name == "ForceCompleteScheduledJobs"))
                throw new Exception("Path completion wait boundary is missing.");
            foreach (var method in focused)
            {
                string prefix = (string)AccessTools.Method(profiler, "Prefix").Invoke(null, new object[] { method });
                if (prefix == "EnterTracker" && AccessTools.Field(method.DeclaringType, "pawn")?.FieldType != typeof(Pawn))
                    throw new Exception("Invalid focused tracker binding: " + method);
            }
            var registration = new HashSet<MethodInfo>();
            void Add(Type type, params string[] names)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (names.Contains(method.Name) && !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null)
                        registration.Add(method);
            }
            Add(typeof(TickManager), "DoSingleTick");
            Add(typeof(Map), "MapPreTick", "MapPostTick", "MapUpdate");
            Add(typeof(MapComponentUtility), "MapComponentTick", "MapComponentUpdate");
            Add(typeof(GameComponentUtility), "GameComponentTick", "GameComponentUpdate");
            Add(typeof(TickList), "Tick"); Add(typeof(Pawn), "TickInterval");
            Add(typeof(Pawn_JobTracker), "StartJob", "EndCurrentJob", "TryFindAndStartJob", "DetermineNextJob");
            Add(typeof(Pawn_PathFollower), "PatherTick"); Add(typeof(ThinkNode_JobGiver), "TryIssueJobPackage");
            Add(typeof(JobGiver_AIFightEnemy), "TryGiveJob");
            Add(typeof(PathFinder), "PathFinderTick", "ForceCompleteScheduledJobs", "CreateRequest");
            Add(assembly.GetType("Helodrace.Tactics.MapComponent_TacticalCommands"), "Advance", "ReturnMembers", "EndOwned", "Issue", "Find");
            foreach (Type type in assembly.GetTypes().Where(t => !t.ContainsGenericParameters
                && (typeof(MapComponent).IsAssignableFrom(t) || typeof(GameComponent).IsAssignableFrom(t))
                && t.Namespace != profiler.Namespace && !t.Name.Contains("Audit")))
                Add(type, "MapComponentTick", "MapComponentUpdate", "GameComponentTick", "GameComponentUpdate");
            foreach (var method in focused) Add(method.DeclaringType, method.Name);
            if (registration.Count > 128) throw new Exception("Focused preset exceeds global target budget: " + preset);
            if (registration.Any(m => m.DeclaringType == typeof(GenRadial) && m.Name == nameof(GenRadial.NumCellsInRadius)))
                throw new Exception("Focused preset instruments its own Core calibration: " + preset);
            Console.WriteLine("PASS: " + preset + " " + focused.Length + " focused / " + registration.Count + " total native diagnostic scopes.");
        }
    }
}
