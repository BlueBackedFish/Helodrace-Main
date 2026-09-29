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
        Check(AmmoPouchRules.Capacity(17, 30) == 17,
            "Authored spare magazine does not inherit the weapon capacity");
        Check(AmmoPouchRules.Capacity(21, 17) == 21,
            "Swapping spare magazine parts selects their own round count");
        Check(AmmoPouchRules.Capacity(0, 30) == 30,
            "Armor pouch still follows the weapon capacity");
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

        foreach (int count in new[] { 17, 21, 30 })
        {
            float remaining = 1f;
            for (int shot = 0; shot < count; shot++)
            {
                int rounds = AmmoPouchRules.RoundsFor(remaining, count);
                Check(rounds == count - shot,
                    count + "-round spare magazine has the correct remaining shots");
                remaining = AmmoPouchRules.AfterShot(rounds, count);
            }
            Check(AmmoPouchRules.RoundsFor(remaining, count) == 0,
                count + "-round spare magazine ejects only after its final shot");
        }

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
        var p320 = XDocument.Load(Path.Combine(root,
            "Defs/ModernWar/Items/ModularWeapons_P320_Development.xml"));
        var p320Variants = XDocument.Load(Path.Combine(root,
            "Defs/ModernWar/Items/ModularWeapons_P320Variants.xml"));
        foreach (var chassis in p320.Root.Elements("ThingDef")
            .Concat(p320Variants.Root.Elements("ThingDef"))
            .Where(def => ((string)def.Element("defName"))
                ?.StartsWith("HD_ModularPart_Receiver_FluxRaiderKit") == true))
        {
            var node = chassis.Element("comps").Elements("li").First();
            Check(node.Element("sockets").Elements("li").Any(socket =>
                (string)socket.Element("id") == "front_magazine"),
                "Flux chassis has a front magazine socket");
            Check(node.Element("defaultAttachments").Elements("li").Any(item =>
                (string)item.Element("socketId") == "front_magazine"),
                "Flux chassis spawns with a spare magazine");
        }
        foreach (int count in new[] { 17, 21, 30 })
        {
            string name = "HD_ModularPart_FrontMagazine_P320" + count;
            var magazine = p320.Root.Elements("ThingDef").Single(def =>
                (string)def.Element("defName") == name);
            var comps = magazine.Element("comps").Elements("li").ToList();
            Check(comps.Count == 2 && comps[1].Attribute("Class")?.Value ==
                "Helodrace.ModernWar.CompProperties_AmmoPouch", name + " has ammo comp");
            Check((int)comps[1].Element("fixedCapacity") == count,
                name + " has authored capacity");
            Check(comps[0].Element("magazineCapacity") == null,
                name + " cannot override the active feed magazine");
            Check((string)comps[1].Element("emptyEjectMoteDef") ==
                "HD_Mote_FrontMagazine_P320" + count, name + " has its own drop animation");
        }
        var motes = XDocument.Load(Path.Combine(root,
            "Defs/ModernWar/Items/ModularWeaponCasings.xml"));
        foreach (int count in new[] { 17, 21, 30 })
            Check(motes.Root.Elements("ThingDef").Any(def =>
                (string)def.Element("defName") == "HD_Mote_FrontMagazine_P320" + count
                && (string)def.Element("graphicData")?.Element("texPath") ==
                    "Weapons/Modular/Magazines/HD_Magazine_P320" + count),
                count + "-round ejection mote uses the matching magazine texture");
        foreach (string presetName in new[] { "P320_RaiderKit.xml", "P320_RaiderKitSuppressed.xml" })
        {
            var preset = XDocument.Load(Path.Combine(root,
                "Defs/ModernWar/ModularPresets", presetName));
            Check(preset.Descendants("socketId").Any(socket =>
                (string)socket == "front_magazine"), presetName + " includes spare magazine");
        }
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
