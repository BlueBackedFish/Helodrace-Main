using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace.ModernWar;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Main(string[] args)
    {
        Check(AmmoPouchRules.EstimateCapacity(1, 12f) == 16, "Single shot weapon capacity");
        Check(AmmoPouchRules.EstimateCapacity(3, 12f) == 24, "Burst weapon capacity");
        Check(AmmoPouchRules.EstimateCapacity(3, 20f) == 20, "Damage lowers capacity");
        Check(AmmoPouchRules.EstimateCapacity(3, 40f) == 16,
            "Higher damage lowers capacity at the same burst count");
        Check(AmmoPouchRules.EstimateCapacity(50, 100f) == 60, "Capacity cap");
        Check(AmmoPouchRules.RoundsFor(1f, 30) == 30, "Fresh pouch");
        Check(AmmoPouchRules.RoundsFor(0f, 30) == 0, "Empty pouch");
        Check(AmmoPouchRules.RoundsFor(0.5f, 17) == 8, "Weapon change floors rounds");
        float fraction = AmmoPouchRules.AfterShot(17, 30);
        Check(AmmoPouchRules.RoundsFor(fraction, 30) == 16, "Each shot consumes one round");
        Check(AmmoPouchRules.RoundsFor(fraction, 17) == 9, "Fraction follows new capacity");
        Check(AmmoPouchRules.RoundsFor(fraction, 30) == 16, "Changing back preserves stored percent");
        Check(AmmoPouchRules.RoundsFor(AmmoPouchRules.AfterShot(1, 30), 30) == 0,
            "Last round ends bonus");
        Check(AmmoPouchRules.SteelNeeded(0f) == 5, "Empty pouch costs five steel");
        Check(AmmoPouchRules.SteelNeeded(0.5f) == 3, "Partial pouch costs less steel");
        Check(AmmoPouchRules.SteelNeeded(0.8f) == 1, "Small deficit costs one steel");
        Check(AmmoPouchRules.SteelNeeded(1f) == 0, "Full pouch needs no steel");
        Check(AmmoPouchRules.RoundsFor(AmmoPouchRules.AfterReplenish(0.5f, 2), 17) == 15,
            "Each steel restores twenty percent across weapon capacities");
        Check(AmmoPouchRules.AfterReplenish(0.5f, 3) == 1f,
            "Three steel fill a half-empty pouch");
        Check(AmmoPouchRules.AfterReplenish(0.5f, 20) == 1f,
            "Replenishment cannot exceed full capacity");
        Check(Math.Abs(AmmoPouchRules.Cooldown(2f, 0f) - 1.5f) < 0.0001f,
            "Generic weapon cooldown multiplier");
        Check(Math.Abs(AmmoPouchRules.Cooldown(0.4f, 0.35f) - 0.35f) < 0.0001f,
            "Modular weapon mechanical floor");

        string root = args.Length > 0 ? args[0] : ".";
        var items = XDocument.Load(Path.Combine(root, "Defs/ModernWar/Items/ModularArmorPartItems.xml"));
        var pouch = items.Root.Elements("ThingDef")
            .Single(item => (string)item.Element("defName") == "HD_ModularPart_RifleMagazinePouch");
        Check(pouch.Element("comps").Elements("li")
            .Any(comp => (string)comp.Attribute("Class") ==
                "Helodrace.ModernWar.CompProperties_AmmoPouch"),
            "Physical magazine pouch owns persistent ammo comp");
        var parts = XDocument.Load(Path.Combine(root, "Defs/ModernWar/ModularArmorParts.xml"));
        var part = parts.Root.Elements("Helodrace.ModernWar.ModularArmorPartDef")
            .Single(item => (string)item.Element("defName") == "HD_IOTVPart_RifleMagazinePouch");
        Check((string)part.Element("partThingDef") == "HD_ModularPart_RifleMagazinePouch",
            "IBTV/belt part uses physical ammo pouch");
        Check(part.Element("compatibleArmorTags").Elements("li").Count() >= 2,
            "Pouch remains available on multiple armor types");
        var jobs = XDocument.Load(Path.Combine(root, "Defs/ModernWar/Jobs_ModernWar.xml"));
        Check(jobs.Root.Elements("JobDef").Any(job =>
            (string)job.Element("defName") == "HD_ReplenishAmmoPouch" &&
            (string)job.Element("driverClass") == "Helodrace.ModernWar.JobDriver_ReplenishAmmoPouch"),
            "Workbench recharge job is defined");
        foreach (string language in new[] { "English", "Korean (한국어)" })
        {
            var keys = XDocument.Load(Path.Combine(root, "Languages", language, "Keyed", "AmmoPouch.xml"));
            Check(keys.Root.Element("HD_AmmoPouch_Rounds") != null &&
                keys.Root.Element("HD_AmmoPouch_Replenished") != null,
                language + " ammo pouch labels exist");
        }
        Console.WriteLine("PASS: " + checks + " ammo pouch checks.");
    }
}
