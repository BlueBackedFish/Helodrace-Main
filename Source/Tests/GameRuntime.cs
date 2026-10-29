using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// Shared by tests that inspect the real game. Reference-only DLLs cannot be executed.
internal static class GameRuntime
{
    internal static string Root { get; private set; }
    private static string harmonyAssembly;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Root = Environment.GetEnvironmentVariable("HELODRACE_GAME_ROOT");
        harmonyAssembly = Environment.GetEnvironmentVariable("HELODRACE_HARMONY_ASSEMBLY");
        foreach (AssemblyMetadataAttribute attribute in Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attribute.Key == "Helodrace.GameRoot" && string.IsNullOrWhiteSpace(Root)) Root = attribute.Value;
            if (attribute.Key == "Helodrace.HarmonyAssembly" && string.IsNullOrWhiteSpace(harmonyAssembly)) harmonyAssembly = attribute.Value;
        }
        if (string.IsNullOrWhiteSpace(Root)) throw new InvalidOperationException("A RimWorld 1.7 runtime path is required.");
        Root = Path.GetFullPath(Root);
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string path = Path.Combine(Root, name.Name + ".dll");
        if (!File.Exists(path) && name.Name == "0Harmony" && !string.IsNullOrWhiteSpace(harmonyAssembly))
            path = Path.GetFullPath(harmonyAssembly);
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }
}
