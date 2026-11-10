using System;
using System.Collections.Generic;
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

    private static void Near(float expected, float actual, string message)
    {
        Check(Math.Abs(expected - actual) < 0.00001f, message + ": " + actual);
    }

    private static void Main(string[] args)
    {
        foreach (var test in new[]
        {
            (BallisticPlateMaterial.Ceramic, 9f),
            (BallisticPlateMaterial.UHMWPE, 6.3f),
            (BallisticPlateMaterial.BallisticSteel, 4.5f),
            (BallisticPlateMaterial.Composite, 7.2f)
        })
        {
            float coefficient = BallisticArmorRules.MaterialCoefficient(test.Item1, 0.8f);
            Near(test.Item2, BallisticArmorRules.PlateWear(20f, 0.3f, coefficient), "Wear for " + test.Item1);
        }
        Near(0f, BallisticArmorRules.PlateWear(20f, 0f, 1f), "Zero AP causes no plate wear");
        Near(0f, BallisticArmorRules.PlateWear(0f, 0.3f, 1f), "Zero damage causes no wear");
        Near(18f, BallisticArmorRules.PlateWear(20f, 0.6f, 1f), "AP above cutoff still consumes wear");
        Near(0f, BallisticArmorRules.PlateWear(20f, -1f, 1f), "Negative AP cannot heal plate");
        Near(0f, BallisticArmorRules.MaterialCoefficient(BallisticPlateMaterial.Composite, -0.5f), "Negative composite coefficient cannot heal plate");
        Near(0.65f, BallisticArmorRules.MaterialCoefficient(BallisticPlateMaterial.Composite, 0.65f), "Composite coefficient belongs to plate");
        Near(1.2f, BallisticArmorRules.MaterialCoefficient(BallisticPlateMaterial.Composite, 1.2f), "Composite coefficient may exceed one");

        string root = args.Length > 0 ? args[0] : ".";
        var plates = XDocument.Load(System.IO.Path.Combine(root, "Defs/ModernWar/Items/ArmorPlates_ModernWar.xml"))
            .Root.Elements("ThingDef").Where(n => n.Element("defName") != null)
            .ToDictionary(n => (string)n.Element("defName"));
        int Hp(string name) => (int)plates[name].Element("statBases").Element("MaxHitPoints");
        Check(Hp("HD_ArmorPlate_SSAPI") < Hp("HD_ArmorPlate_SAPI"), "SAPI side plates have less durability");
        Check(Hp("HD_ArmorPlate_ESBI") < Hp("HD_ArmorPlate_ESAPI"), "ESAPI side plates have less durability");
        foreach (string name in new[] { "HD_ArmorPlate_SAPI", "HD_ArmorPlate_ESAPI", "HD_ArmorPlate_SSAPI", "HD_ArmorPlate_ESBI" })
        {
            Check((string)plates[name].Element("comps").Element("li").Element("material") == "Ceramic",
                "Plate explicitly declares Ceramic material: " + name);
            Check((string)plates[name].Element("comps").Attribute("Inherit") == "False",
                "Plate overrides inherited comp instead of duplicating it: " + name);
        }
        Check((string)plates["HD_ArmorPlate_RAMPART4800_Plate"].Element("comps").Element("li").Element("material") == "UHMWPE",
            "RAMPART uses memo's simplified material");
        Check((string)plates["HD_ArmorPlate_RAMPART4800_Plate"].Element("comps").Attribute("Inherit") == "False",
            "RAMPART overrides rather than duplicates inherited plate comp");
        var apparel = XDocument.Load(System.IO.Path.Combine(root, "Defs/ModernWar/Items/Apparel_ModernWar.xml"));
        foreach (var expected in new[] { ("HD_Apparel_ShieldDefenTechIIIA", "HD_Layup_ShieldHG2"), ("HD_Apparel_ShieldIronHideIV", "HD_Layup_ShieldRF3") })
        {
            var shield = apparel.Root.Elements("ThingDef").Single(n => (string)n.Element("defName") == expected.Item1);
            var comp = shield.Element("comps").Elements("li")
                .Single(n => (string)n.Attribute("Class") == "Helodrace.ModernWar.CompProperties_DirectionalBallisticShield");
            Check(comp.Element("guaranteedBlockPenetration") == null, "Shield has no guaranteed-block cutoff " + expected.Item1);
            Check(Layup(shield) == expected.Item2, "Shield ballistic layup " + expected.Item1);
        }
        var apparelDefs = apparel.Root.Elements("ThingDef")
            .Where(n => n.Element("defName") != null)
            .ToDictionary(n => (string)n.Element("defName"));
        Near(0.85f, (float)apparelDefs["HD_Apparel_IBTVAssault"].Element("statBases").Element("ArmorRating_Sharp"),
            "IBTV HG2 armor");
        Near(0.85f, (float)apparelDefs["HD_Apparel_FASTMT"].Element("statBases").Element("ArmorRating_Sharp"),
            "FAST MT HG2 armor");
        Near(0.85f, (float)apparelDefs["HD_Apparel_ShieldDefenTechIIIA"].Element("comps").Elements("li")
            .Single(n => ((string)n.Attribute("Class"))?.Contains("DirectionalBallisticShield") == true)
            .Element("sharpArmorRating"), "Light shield HG2 armor");
        Near(1.65f, (float)apparelDefs["HD_Apparel_ShieldIronHideIV"].Element("comps").Elements("li")
            .Single(n => ((string)n.Attribute("Class"))?.Contains("DirectionalBallisticShield") == true)
            .Element("sharpArmorRating"), "Heavy shield RF3 armor");

        var partDefs = XDocument.Load(System.IO.Path.Combine(root, "Defs/ModernWar/ModularArmorParts.xml"))
            .Root.Elements("Helodrace.ModernWar.ModularArmorPartDef")
            .ToDictionary(n => (string)n.Element("defName"));
        var plateStats = new Dictionary<string, float>
        {
            ["SAPI"] = 1.20f, ["SSAPI"] = 1.20f, ["ESAPI"] = 1.65f, ["ESBI"] = 1.65f, ["RAMPART4800"] = 1.65f
        };
        foreach (string position in new[] { "FrontRear", "Sides" })
        {
            foreach (var plate in plateStats)
            {
                string name = "HD_IOTVPart_" + position + "_" + plate.Key;
                if (!partDefs.ContainsKey(name)) continue;
                Near(plate.Value, (float)partDefs[name].Element("armorRatingSharp"), name + " grade armor");
                Check(partDefs[name].Element("guaranteedBlockPenetration") == null, name + " has no guaranteed-block cutoff");
            }
        }
        foreach (var plate in new[] { ("HD_ArmorPlate_SAPI", "HD_Layup_SAPI"), ("HD_ArmorPlate_SSAPI", "HD_Layup_SAPI"),
            ("HD_ArmorPlate_ESAPI", "HD_Layup_ESAPI"), ("HD_ArmorPlate_ESBI", "HD_Layup_ESAPI"),
            ("HD_ArmorPlate_RAMPART4800_Plate", "HD_Layup_RAMPART4800") })
        {
            Check(Layup(plates[plate.Item1]) == plate.Item2, "Plate item ballistic layup " + plate.Item1);
        }
        Check(Layup(apparelDefs["HD_Apparel_IBTVAssault"]) == "HD_Layup_SoftHG2", "IBTV soft panel layup");
        foreach (string suffix in new[] { "FrontRearPlates", "SidePlates", "FrontPlate", "RearPlate", "LeftPlate", "RightPlate" })
        {
            var part = partDefs["HD_IOTVPart_" + suffix];
            Near(1.20f, (float)part.Element("armorRatingSharp"), "Legacy plate lower RF1 grade: " + suffix);
            Check(part.Element("guaranteedBlockPenetration") == null, "Legacy plate has no unsupported cutoff: " + suffix);
        }
        foreach (string suffix in new[] { "FrontRearPlates", "SidePlates" })
        {
            Check((string)partDefs["HD_IOTVPart_" + suffix].Element("ballisticLayup") == "HD_Layup_SAPI",
                "Legacy RF1 plate set uses the SAPI layup: " + suffix);
        }
        foreach (string suffix in new[] { "NeckGuard", "ShoulderGuards", "LowerBody", "BackProtector" })
        {
            Near(0.70f, (float)partDefs["HD_IOTVPart_" + suffix].Element("armorRatingSharp"),
                "Soft module lower HG1 grade: " + suffix);
            Check((string)partDefs["HD_IOTVPart_" + suffix].Element("ballisticLayup") == "HD_Layup_SoftHG1",
                "Soft module HG1 layup: " + suffix);
        }
        var visor = XDocument.Load(System.IO.Path.Combine(root, "Defs/ModernWar/ModularHelmetParts.xml"))
            .Root.Elements("Helodrace.ModernWar.ModularArmorPartDef")
            .Single(n => (string)n.Element("defName") == "HD_FASTMTPart_MultiHitVisor");
        Near(0.85f, (float)visor.Element("armorRatingSharp"), "Visor memo HG2 grade");
        var belowHG1 = XDocument.Load(System.IO.Path.Combine(root, "Defs/ColdWar/Items/Apparel_ColdWar.xml"))
            .Root.Elements("ThingDef").Single(n => (string)n.Element("defName") == "HD_Apparel_M1952AFlakJacket");
        Near(0.55f, (float)belowHG1.Element("statBases").Element("ArmorRating_Sharp"),
            "Below HG1 remains unchanged");
        Console.WriteLine("PASS: " + checks + " ballistic armor checks.");
    }

    private static string Layup(XElement def) => (string)def.Element("modExtensions")?.Elements("li")
        .FirstOrDefault(n => (string)n.Attribute("Class") == "Helodrace.Ballistics.BallisticLayupExtension")
        ?.Element("layup");
}
