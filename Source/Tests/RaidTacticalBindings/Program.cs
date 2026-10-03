using System;
using System.IO;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Helodrace;

internal static class Program
{
    private static int Main()
    {
        // Check target signatures and injected private fields against the real
        // installed game. This is not a Unity/Mono runtime patch simulation.
        string managed = @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed";
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Type[] patches = {
            typeof(Patch_RaidTacticalTrace_StartJob), typeof(Patch_RaidTacticalTrace_EndJob),
            typeof(Patch_RaidTacticalDuty), typeof(Patch_RaidTacticalDutyConstant),
            typeof(Patch_RaidTacticalContinuation), typeof(Patch_RaidTacticalHoldFacing),
            typeof(Patch_RaidTacticalPendingAttack),
            typeof(Patch_RaidTacticalSupportFlee),
            typeof(Patch_RaidMovementArea_Request), typeof(Patch_RaidMovementArea_Dispose)
        };
        try
        {
            foreach (Type patch in patches)
            {
                HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
                MethodInfo target = AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                if (target == null) throw new Exception("Missing target: " + patch.Name);
                foreach (MethodInfo hook in patch.GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Where(method => method.Name == "Prefix" || method.Name == "Postfix"))
                    foreach (ParameterInfo parameter in hook.GetParameters())
                    {
                        Type actual = parameter.ParameterType.IsByRef
                            ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                        Type expected;
                        if (parameter.Name.StartsWith("___"))
                            expected = AccessTools.Field(info.declaringType, parameter.Name.Substring(3))?.FieldType;
                        else if (parameter.Name == "__instance") expected = info.declaringType;
                        else if (parameter.Name == "__result") expected = target.ReturnType;
                        else expected = target.GetParameters().FirstOrDefault(value => value.Name == parameter.Name)?.ParameterType;
                        if (expected == null || !actual.IsAssignableFrom(expected))
                            throw new Exception(patch.Name + ": invalid binding " + parameter.Name);
                    }
                Console.WriteLine("PASS: " + patch.Name);
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
