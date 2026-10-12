using System;
using System.IO;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class TacticalEquipmentRulesTests
{
    internal static void Run()
    {
        var bag = new CompMedibag { props = new CompProperties_Medibag { plasmaTransfusionHediff = new HediffDef() } };
        var stock = AccessTools.Field(typeof(CompMedibag), "storedSupplies");
        var ready = AccessTools.Field(typeof(CompMedibag), "readyAt");
        var commit = AccessTools.Method(typeof(CompMedibag), "CompleteTreatment");
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
        Check(bag.HemostasisAvailable && bag.PlasmaAvailable, "An empty ready bag must allow both treatments.");
        commit.Invoke(bag, new object[] { 1 });
        Check((int)stock.GetValue(bag) == 0 && bag.CooldownRemaining == 2500 && !bag.HemostasisAvailable,
            "Normal successful use starts shared one-hour cooldown without supplies.");
        stock.SetValue(bag, 2);
        Check(bag.HemostasisAvailable && bag.PlasmaAvailable, "Loaded stock permits either early treatment.");
        commit.Invoke(bag, new object[] { 1 });
        Check((int)stock.GetValue(bag) == 1, "Early use must consume one supply.");
        string path = Path.Combine(Path.GetTempPath(), "hd-bag-" + Guid.NewGuid() + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "bag"); bag.PostExposeData(); Scribe.saver.FinalizeSaving();
            var restored = new CompMedibag { props = bag.props };
            Scribe.loader.InitLoading(path); restored.PostExposeData(); Scribe.loader.ForceStop();
            Check(restored.CooldownRemaining == 2500 && (int)stock.GetValue(restored) == 1,
                "The bag, rather than the wearer, must persist resources and shared cooldown.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        ready.SetValue(bag, 0); stock.SetValue(bag, 0);
        Check(bag.HemostasisAvailable, "Expired cooldown restores resource-free treatment.");
        Console.WriteLine("PASS: A4 bag normal/early resource use, shared cooldown and native Scribe checks.");
    }
}
