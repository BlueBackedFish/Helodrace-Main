using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Profiling
{
    // Bounded diagnostic scopes, resolved only at startup. In particular, virtual
    // NeedInterval calls require concrete implementations, not just the base IL.
    internal static class FocusedProfileTargets
    {
        internal static MethodInfo[] Resolve(string preset)
        {
            var result = new HashSet<MethodInfo>();
            if (preset == "needs-spikes")
            {
                Add(result, typeof(Pawn_NeedsTracker), "NeedsTrackerTick", "NeedsTrackerTickInterval");
                foreach (Assembly assembly in new[] { typeof(Pawn).Assembly, typeof(FocusedProfileTargets).Assembly })
                    foreach (Type type in assembly.GetTypes().Where(t => typeof(Need).IsAssignableFrom(t) && !t.ContainsGenericParameters))
                        Add(result, type, "NeedInterval");
            }
            else if (preset == "jobs-spikes")
            {
                Add(result, typeof(Pawn_JobTracker), "DetermineNextJob", "CheckForJobOverride");
                Add(result, typeof(JobGiver_AIFightEnemy), "TryGiveJob", "TryFindShootingPosition", "FindAttackTarget");
                Add(result, typeof(CastPositionFinder), "TryFindCastPosition");
                ExpandDirectCalls(result);
            }
            else if (preset == "path-spikes")
            {
                Add(result, typeof(PathFinder), "PathFinderTick", "ForceCompleteScheduledJobs", "CreateRequest");
                Add(result, typeof(Pawn_PathFollower), "StartPath", "TrySetNewPath", "NeedNewPath");
                ExpandDirectCalls(result);
            }
            else throw new ArgumentException("Unknown focused profiler preset: " + preset);
            return result.OrderBy(m => m.DeclaringType.FullName, StringComparer.Ordinal)
                .ThenBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.ToString(), StringComparer.Ordinal).ToArray();
        }

        private static void Add(HashSet<MethodInfo> result, Type type, params string[] names)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (names.Contains(method.Name) && Supported(method)) result.Add(method);
        }

        private static void ExpandDirectCalls(HashSet<MethodInfo> result)
        {
            // One level only. Do not recursively instrument the game's call graph,
            // Unity workers, getters, or generic collection helpers.
            foreach (MethodInfo root in result.ToArray())
            {
                var generator = new DynamicMethod("ReadFocusedProfileScope", typeof(void), Type.EmptyTypes).GetILGenerator();
                foreach (var instruction in PatchProcessor.ReadMethodBody(root, generator))
                    if ((instruction.Key == OpCodes.Call || instruction.Key == OpCodes.Callvirt)
                        && instruction.Value is MethodInfo child && Supported(child)
                        && (child.DeclaringType == root.DeclaringType || child.DeclaringType.Namespace == "Verse.AI"
                            || child.DeclaringType.Name.StartsWith("JobGiver_", StringComparison.Ordinal))) result.Add(child);
            }
        }

        private static bool Supported(MethodInfo method) => (method.DeclaringType.Assembly == typeof(Pawn).Assembly
            || method.DeclaringType.Assembly == typeof(FocusedProfileTargets).Assembly)
            && !method.IsSpecialName && !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null
            && !(method.DeclaringType == typeof(GenRadial) && method.Name == nameof(GenRadial.NumCellsInRadius))
            && method.DeclaringType.Namespace != typeof(FocusedProfileTargets).Namespace;
    }
}
