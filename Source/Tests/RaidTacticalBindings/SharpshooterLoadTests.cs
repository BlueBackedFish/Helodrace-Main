using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class SharpshooterLoadTests
{
    internal static void Run()
    {
        string path=Path.Combine(Path.GetTempPath(),"hd-sharp-load-"+Guid.NewGuid()+".xml");
        // Native Scribe runs without Unity's startup converter catalogue.
        var converters=AccessTools.Field(typeof(BackCompatibility),"conversionChain");
        object oldConverters=converters.GetValue(null);
        bool oldChanged=ScribeMetaHeaderUtility.modListChanged;
        ScribeMetaHeaderUtility.modListChanged=true;
        converters.SetValue(null,new List<BackCompatibilityConverter>());
        var typeCache=AccessTools.Field(typeof(GenTypes),"typeCache");
        object oldCache=typeCache.GetValue(null);
        var cache=(System.Collections.IDictionary)Activator.CreateInstance(typeCache.FieldType);
        var key=typeof(GenTypes).GetNestedType("TypeCacheKey",System.Reflection.BindingFlags.NonPublic);
        cache[AccessTools.Constructor(key,new[]{typeof(string),typeof(string)})
            .Invoke(new object[]{typeof(SharpshooterPreparationClock).FullName,null})]=typeof(SharpshooterPreparationClock);
        typeCache.SetValue(null,cache);
        try
        {
            foreach(string clock in new[]{"","<stationaryPreparation IsNull=\"True\" />"})
            {
                File.WriteAllText(path,"<root>"+clock+"</root>");
                var comp=new CompSharpshooterWeapon();
                Scribe.loader.InitLoading(path); comp.PostExposeData(); Scribe.loader.ForceStop();
                if(comp.Preparation==null || comp.Preparation.StartedAt!=-1)
                    throw new Exception("Loaded weapons must initialize a missing/null preparation clock.");
                comp.Notify_Unequipped(null);
                if(comp.Preparation.StartedAt!=-1) throw new Exception("Drop notification must reset a loaded clock safely.");
            }
            var source=new CompSharpshooterWeapon(); source.Preparation.StartedAt=120;
            source.Preparation.Position=new IntVec3(5,0,8);
            Scribe.saver.InitSaving(path,"root"); source.PostExposeData(); Scribe.saver.FinalizeSaving();
            var restored=new CompSharpshooterWeapon();
            Scribe.loader.InitLoading(path); restored.PostExposeData(); Scribe.loader.ForceStop();
            if(restored.Preparation.StartedAt!=120 || restored.Preparation.Position!=source.Preparation.Position)
                throw new Exception("Initializing defaults must not replace an existing loaded clock.");
            Console.WriteLine("PASS: missing/null sharpshooter clocks load safely, drop notifications reset them, and saved clocks retain state.");
        }
        finally
        {
            Scribe.loader.ForceStop(); converters.SetValue(null,oldConverters);
            typeCache.SetValue(null,oldCache);
            ScribeMetaHeaderUtility.modListChanged=oldChanged;
            if(File.Exists(path)) File.Delete(path);
        }
    }
}
