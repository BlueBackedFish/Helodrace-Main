using System;
using System.Linq;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class TacticalEngineSelectionTests
{
    internal static void Run()
    {
        if (TacticalEngineSelection.Parse(null) != TacticalEngineKind.Legacy
            || TacticalEngineSelection.Parse("VANILLA") != TacticalEngineKind.Vanilla
            || TacticalEngineSelection.Parse("new") != TacticalEngineKind.New)
            throw new Exception("Engine selection parser/default failed.");
        try { TacticalEngineSelection.Parse("garbage"); throw new Exception("Invalid engine accepted."); }
        catch (ArgumentException) { }
        var types = typeof(TacticalEngineSelection).Assembly.GetTypes();
        foreach (Type type in types)
            if (!type.IsDefined(typeof(HarmonyPatch), false) && type.GetMethods(System.Reflection.BindingFlags.DeclaredOnly
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
                .Any(method => method.IsDefined(typeof(HarmonyPatch), false)))
                throw new Exception("Patch discovery would omit a method-only target: " + type);
        var marked = types.Where(TacticalEngineSelection.IsLegacy).ToArray();
        var components = marked.Where(type => typeof(MapComponent).IsAssignableFrom(type)
            || typeof(GameComponent).IsAssignableFrom(type)).ToArray();
        var patches = marked.Where(type => type.IsDefined(typeof(HarmonyPatch), false)).ToArray();
        if (components.Length != 11 || patches.Length != 25) throw new Exception("Legacy isolation manifest changed: " + components.Length + "/" + patches.Length);
        foreach (Type type in marked)
            if (!TacticalEngineSelection.Install(type, TacticalEngineKind.Legacy)
                || TacticalEngineSelection.Install(type, TacticalEngineKind.Vanilla)
                || TacticalEngineSelection.Install(type, TacticalEngineKind.New)) throw new Exception("Legacy filter failed: " + type);
        foreach (Type type in new[] { typeof(Helodrace.Tactics.MapComponent_TacticalCommands), typeof(Helodrace.Tactics.GameComponent_TacticalCommands) })
            if (!TacticalEngineSelection.Install(type, TacticalEngineKind.New)
                || TacticalEngineSelection.Install(type, TacticalEngineKind.Vanilla)
                || TacticalEngineSelection.Install(type, TacticalEngineKind.Legacy)) throw new Exception("New engine component isolation failed: " + type);
        var connected = new[] { new IntVec3(0,0,0), new IntVec3(1,0,0), new IntVec3(1,0,1) };
        if (!Helodrace.Tactics.TacticalLocalPlanner.Connected(connected)
            || Helodrace.Tactics.TacticalLocalPlanner.Connected(new[] { connected[0], connected[2] })
            || Helodrace.Tactics.TacticalLocalPlanner.Connected(new[] { connected[0], connected[0] }))
            throw new Exception("A stack must be cardinally connected with unique positions; diagonal contact is insufficient.");
        foreach (Type type in new[] { typeof(Patch_TacticalEngine_MapComponents), typeof(Patch_TacticalEngine_GameComponents),
            typeof(MapComponent_TacticalEngineAudit), typeof(Helodrace.Squads.GameComponent_CombatOrganizations) })
            if (Enum.GetValues<TacticalEngineKind>().Any(engine => !TacticalEngineSelection.Install(type, engine)))
                throw new Exception("Shared fixture/organization/filter incorrectly disabled.");
        foreach (Type patch in new[] { typeof(Patch_TacticalEngine_MapComponents), typeof(Patch_TacticalEngine_GameComponents) })
        {
            HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
            if (AccessTools.DeclaredMethod(info.declaringType, info.methodName) == null) throw new Exception("Engine filter target missing: " + patch);
        }
        var canonical = AccessTools.Method(typeof(MapComponent_TacticalEngineAudit), "CanonicalPreset");
        const string firstPreset = "<Defs><Preset><defName>Exported_20261007_123</defName><part>M16A4</part><slot>radio</slot></Preset></Defs>";
        string first = (string)canonical.Invoke(null, new object[] { firstPreset });
        string second = (string)canonical.Invoke(null, new object[] { firstPreset.Replace("20261007_123", "20261008_456") });
        string differentGear = (string)canonical.Invoke(null, new object[] { firstPreset.Replace("M16A4", "M4") });
        if (first != second || first == differentGear || !first.Contains("<slot>radio</slot>"))
            throw new Exception("Equipment hash canonicalization removed gear or retained the export timestamp.");
        Console.WriteLine("PASS: engine selection, 11 components/25 patch classes isolation, shared metadata, real FillComponents targets, stable gear fingerprint.");
    }
}
