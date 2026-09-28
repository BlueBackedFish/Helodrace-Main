using System;
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

        foreach (float cutoff in new[] { 0.19f, 0.5f })
        {
            Check(BallisticArmorRules.CanGuaranteeBlock(cutoff, cutoff, 0.75f,
                BallisticArmorRules.ShieldGuaranteedBlockMinimumDurability), "Inclusive shield AP and durability boundaries");
            Check(!BallisticArmorRules.CanGuaranteeBlock(cutoff, cutoff, 0.7499f,
                BallisticArmorRules.ShieldGuaranteedBlockMinimumDurability), "Shield below 75% loses guarantee");
            Check(!BallisticArmorRules.CanGuaranteeBlock(cutoff + 0.0001f, cutoff, 1f,
                BallisticArmorRules.ShieldGuaranteedBlockMinimumDurability), "High AP bypasses guarantee");
        }
        Check(BallisticArmorRules.CanGuaranteeBlock(0.3f, 0.5f, 0.4f,
            BallisticArmorRules.PlateGuaranteedBlockMinimumDurability), "Plate boundary remains 40%");
        Check(!BallisticArmorRules.CanGuaranteeBlock(0f, 0f, 1f, 0.75f), "Zero cutoff disables guarantee");
        Check(!BallisticArmorRules.CanGuaranteeBlock(0.1f, 0.5f, 0f, 0.4f), "Destroyed plate cannot guarantee block");

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
        Check((string)plates["HD_ArmorPlate_RAMPART4800"].Element("comps").Element("li").Element("material") == "UHMWPE",
            "RAMPART uses memo's simplified material");
        Check((string)plates["HD_ArmorPlate_RAMPART4800"].Element("comps").Attribute("Inherit") == "False",
            "RAMPART overrides rather than duplicates inherited plate comp");
        var apparel = XDocument.Load(System.IO.Path.Combine(root, "Defs/ModernWar/Items/Apparel_ModernWar.xml"));
        foreach (var expected in new[] { ("HD_Apparel_ShieldDefenTechIIIA", 0.19f), ("HD_Apparel_ShieldIronHideIV", 0.5f) })
        {
            var shield = apparel.Root.Elements("ThingDef").Single(n => (string)n.Element("defName") == expected.Item1);
            var comp = shield.Element("comps").Elements("li")
                .Single(n => (string)n.Attribute("Class") == "Helodrace.ModernWar.CompProperties_DirectionalBallisticShield");
            Near(expected.Item2, (float)comp.Element("guaranteedBlockPenetration"), "Shield cutoff " + expected.Item1);
        }
        Console.WriteLine("PASS: " + checks + " ballistic armor checks.");
    }
}
