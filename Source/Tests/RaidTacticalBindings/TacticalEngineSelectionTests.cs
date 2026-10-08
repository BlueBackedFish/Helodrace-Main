using System;
using System.Linq;
using HarmonyLib;
using Helodrace;
using Verse;
using Verse.AI.Group;
using System.Runtime.CompilerServices;
using RimWorld;

internal static class TacticalEngineSelectionTests
{
    internal static void Run()
    {
        if (TacticalEngineSelection.Parse(null) != TacticalEngineKind.New
            || TacticalEngineSelection.Parse("VANILLA") != TacticalEngineKind.Vanilla
            || TacticalEngineSelection.Parse("new") != TacticalEngineKind.New)
            throw new Exception("Engine selection parser/default failed.");
        try { TacticalEngineSelection.Parse("garbage"); throw new Exception("Invalid engine accepted."); }
        catch (ArgumentException) { }
        var auditSpeed = AccessTools.Method(typeof(MapComponent_TacticalEngineAudit), "AuditTimeSpeed");
        if ((TimeSpeed)auditSpeed.Invoke(null, new object[] { 1 }) != TimeSpeed.Normal
            || (TimeSpeed)auditSpeed.Invoke(null, new object[] { 3 }) != TimeSpeed.Fast)
            throw new Exception("Audit requested speed does not map to the real native game speed.");
        bool originalSlowdown = DebugViewSettings.neverForceNormalSpeed;
        try
        {
            var manager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
            var slower = RuntimeHelpers.GetUninitializedObject(typeof(TimeSlower));
            AccessTools.Field(typeof(TickManager), "slower").SetValue(manager, slower);
            AccessTools.Field(typeof(TimeSlower), "forceNormalSpeedUntil").SetValue(slower, int.MaxValue);
            DebugViewSettings.neverForceNormalSpeed = true;
            foreach (int speed in new[] { 1, 3 })
            {
                // The native setter needs a live Game/gravship controller;
                // seed its backing field and exercise the real rate getter.
                AccessTools.Field(typeof(TickManager), "curTimeSpeed").SetValue(manager,
                    (TimeSpeed)auditSpeed.Invoke(null, new object[] { speed }));
                if (manager.TickRateMultiplier != speed)
                    throw new Exception("Native speed control did not override the combat slowdown.");
            }
        }
        finally { DebugViewSettings.neverForceNormalSpeed = originalSlowdown; }
        var types = typeof(TacticalEngineSelection).Assembly.GetTypes();
        foreach (Type type in types)
            if (!type.IsDefined(typeof(HarmonyPatch), false) && type.GetMethods(System.Reflection.BindingFlags.DeclaredOnly
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
                .Any(method => method.IsDefined(typeof(HarmonyPatch), false)))
                throw new Exception("Patch discovery would omit a method-only target: " + type);
        foreach (Type type in new[] { typeof(Helodrace.Tactics.MapComponent_TacticalCommands), typeof(Helodrace.Tactics.GameComponent_TacticalCommands),
            typeof(Helodrace.Tactics.Patch_NewTactical_RaidCreated) })
            if (!TacticalEngineSelection.Install(type, TacticalEngineKind.New)
                || TacticalEngineSelection.Install(type, TacticalEngineKind.Vanilla)) throw new Exception("New engine component isolation failed: " + type);
        var connected = new[] { new IntVec3(0,0,0), new IntVec3(1,0,0), new IntVec3(1,0,1) };
        if (!Helodrace.Tactics.TacticalLocalPlanner.Connected(connected)
            || Helodrace.Tactics.TacticalLocalPlanner.Connected(new[] { connected[0], connected[2] })
            || Helodrace.Tactics.TacticalLocalPlanner.Connected(new[] { connected[0], connected[0] }))
            throw new Exception("A stack must be cardinally connected with unique positions; diagonal contact is insufficient.");
        var job = (LordJob)RuntimeHelpers.GetUninitializedObject(typeof(LordJob_AssaultColony));
        foreach (Type toil in new[] { typeof(LordToil_AssaultColony), typeof(LordToil_AssaultColonySappers), typeof(LordToil_AssaultColonyBreaching) })
        {
            if (!Helodrace.Tactics.GameComponent_TacticalCommands.IsAssaultPhase(job,
                (LordToil)RuntimeHelpers.GetUninitializedObject(toil))) throw new Exception("Active assault was excluded.");
        }
        if (Helodrace.Tactics.GameComponent_TacticalCommands.IsAssaultPhase(job,
            (LordToil)RuntimeHelpers.GetUninitializedObject(typeof(LordToil_ExitMap))))
            throw new Exception("An assault LordJob in ExitMap must relinquish tactical ownership.");
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
        Type fixturePatch = typeof(TacticalEngineSelection).Assembly.GetType("Helodrace.Patch_TacticalAudit_PawnRequest", true);
        var constrain = AccessTools.Method(fixturePatch, "Prefix");
        var generating = AccessTools.Field(typeof(MapComponent_TacticalEngineAudit), "GeneratingFixture");
        object[] requestArgs = { new PawnGenerationRequest { CanGeneratePawnRelations = true } };
        constrain.Invoke(null, requestArgs);
        if (!((PawnGenerationRequest)requestArgs[0]).CanGeneratePawnRelations)
            throw new Exception("Audit relation constraint leaked into normal pawn generation.");
        Type race = typeof(TacticalEngineSelection).Assembly.GetType("Helodrace.HelodRace", true);
        var xenotypes = (System.Collections.Generic.List<XenotypeDef>)AccessTools.Field(race, "HelodXenotypes").GetValue(null);
        var testXenotype = (XenotypeDef)RuntimeHelpers.GetUninitializedObject(typeof(XenotypeDef));
        testXenotype.defName = "Audit_test"; xenotypes.Add(testXenotype);
        try
        {
            generating.SetValue(null, true); constrain.Invoke(null, requestArgs);
            var constrained = (PawnGenerationRequest)requestArgs[0];
            if (constrained.CanGeneratePawnRelations || !constrained.ForceNoBackstory || !constrained.ForceNoIdeo
                || constrained.FixedBiologicalAge < 18 || constrained.FixedBiologicalAge > 35 || constrained.ForcedXenotype == null)
                throw new Exception("Audit formation generation retained uncontrolled state.");
        }
        finally { generating.SetValue(null, false); xenotypes.Remove(testXenotype); }
        Console.WriteLine("PASS: engine selection, new default/vanilla isolation, shared metadata, real FillComponents targets, stable gear fingerprint.");
    }
}
