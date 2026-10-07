using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Helodrace.Profiling
{
    // Read native IL once at registration. Do not reflect or expand a call graph
    // during capture, and do not instrument property getters or all game ticks.
    internal static class PawnProfileTargets
    {
        internal static MethodInfo[] Resolve()
        {
            var result = new HashSet<MethodInfo>();
            foreach (Type type in new[] { typeof(Pawn), typeof(ThingWithComps), typeof(Pawn_JobTracker) })
                foreach (MethodInfo root in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == "Tick" || m.Name == "TickInterval" || m.Name == "JobTrackerTick" || m.Name == "JobTrackerTickInterval"))
                {
                    result.Add(root);
                    var generator = new DynamicMethod("ReadPawnProfileScope", typeof(void), Type.EmptyTypes).GetILGenerator();
                    foreach (var instruction in PatchProcessor.ReadMethodBody(root, generator))
                        if ((instruction.Key == OpCodes.Call || instruction.Key == OpCodes.Callvirt)
                            && instruction.Value is MethodInfo child && Supported(child) && child.Name.Contains("Tick"))
                            result.Add(child);
                }
            return result.Where(Supported).OrderBy(m => m.DeclaringType.FullName, StringComparer.Ordinal)
                .ThenBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.ToString(), StringComparer.Ordinal).ToArray();
        }

        private static bool Supported(MethodInfo method) => method.DeclaringType.Assembly == typeof(Pawn).Assembly
            && !method.IsSpecialName && !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null;
    }
}
