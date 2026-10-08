using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalAuditProtectionTests
{
    public static void Run()
    {
        var assembly = typeof(TacticalSquadCommand).Assembly;
        var patch = assembly.GetType("Helodrace.Patch_RaidRuntimeAudit_Owner", true);
        if ((bool)AccessTools.Method(patch, "Prepare").Invoke(null, null))
            throw new Exception("Audit-only damage protection must not install in a normal command line.");
        HarmonyMethod target = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
        if (AccessTools.DeclaredMethod(target.declaringType, target.methodName, target.argumentTypes) == null)
            throw new Exception("Extracted damage protection lost its real native patch target.");
        var ownerField = AccessTools.Field(assembly.GetType("Helodrace.TacticalAuditProtection", true), "ProtectedOwner");
        object previous = ownerField.GetValue(null);
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        var other = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        try
        {
            ownerField.SetValue(null, pawn);
            var prefix = AccessTools.Method(patch, "Prefix");
            if ((bool)prefix.Invoke(null, new object[] { pawn, null })
                || !(bool)prefix.Invoke(null, new object[] { other, null }))
                throw new Exception("Extracted protection must protect only the explicit audit owner.");
        }
        finally { ownerField.SetValue(null, previous); }
        Console.WriteLine("PASS: shared native audit damage target, normal-session exclusion and explicit-owner-only protection.");
    }
}
