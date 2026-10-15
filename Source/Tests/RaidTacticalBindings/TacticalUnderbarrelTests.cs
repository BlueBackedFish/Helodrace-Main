using System;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Helodrace.ModernWar;
using Helodrace.Tactics;
using Verse;

internal static class TacticalUnderbarrelTests
{
    internal static void Run()
    {
        var target=AccessTools.Method(typeof(Pawn),nameof(Pawn.TryGetAttackVerb));
        var patch=AccessTools.Method(typeof(Patch_TacticalUnderbarrelSelection),"Postfix");
        if(!target.GetParameters().Any(p=>p.Name=="target" && p.ParameterType==typeof(Thing))
            || !patch.GetParameters().Any(p=>p.Name=="__result" && p.ParameterType==typeof(Verb).MakeByRefType()))
            throw new Exception("Launcher selection does not bind to the installed attack-verb API.");
        var launcher=new CompModularUnderbarrelLauncher {props=new CompProperties_ModularUnderbarrelLauncher()};
        if(launcher.TryReload(null) || launcher.Loaded) throw new Exception("Reload must not fabricate ammunition.");
        AccessTools.Field(launcher.GetType(),"loaded").SetValue(launcher,true);
        launcher.NotifyFired();
        if(launcher.Loaded) throw new Exception("A fired launcher must empty its real chamber.");
        var method=AccessTools.Method(typeof(Verb_ShootModularUnderbarrel),"TryCastShot");
        var il=new DynamicMethod("UnderbarrelInstructions",typeof(void),Type.EmptyTypes).GetILGenerator();
        var code=PatchProcessor.GetOriginalInstructions(method,il);
        int safety=code.FindIndex(i=>i.Calls(AccessTools.Method(typeof(MapComponent_TacticalCommands),"LauncherSafe")));
        int fired=code.FindIndex(i=>i.Calls(AccessTools.Method(typeof(CompModularUnderbarrelLauncher),"NotifyFired")));
        if(safety<0 || fired<=safety) throw new Exception("AI blast safety must be revalidated before shot completion/chamber consumption.");
        Console.WriteLine("PASS: launcher hook binding, ammunition/chamber state and safety-before-fire call sequence.");
    }
}
