using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;
using Verse;

internal static partial class Program
{
    private static void AuthoredPartConflictTests()
    {
        // Resolve real XML references to shared ThingDefs, as the game's cross-reference loader does.
        var authored = definitions.Values.Where(e => e.Name.LocalName == "ThingDef")
            .Select(Resolve).Where(e => e.Element("comps")?.Elements()
                .Any(c => (string)c.Attribute("Class") == CompClass) == true)
            .ToDictionary(e => (string)e.Element("defName"), e => e.Element("comps").Elements()
                .Single(c => (string)c.Attribute("Class") == CompClass));
        var defs = authored.Keys.ToDictionary(name => name, name =>
        {
            var def = Bare<ThingDef>(); def.defName = name; def.label = name; return def;
        });
        foreach (var entry in authored)
        {
            var props = new CompProperties_ModularWeaponNode();
            foreach (string key in new[]{"incompatibleParts", "conditionalIncompatibleParts"})
                if (entry.Value.Element(key) != null)
                {
                    var field = typeof(CompProperties_ModularWeaponNode).GetField(key);
                    field.SetValue(props, Parse(field.FieldType, entry.Value.Element(key)));
                }
            props.incompatibleParts = props.incompatibleParts.Select(d => defs[d.defName]).ToList();
            foreach (var rule in props.conditionalIncompatibleParts)
            {
                rule.part = defs[rule.part.defName];
                rule.whenAnyParts = rule.whenAnyParts.Select(d => defs[d.defName]).ToList();
            }
            defs[entry.Key].comps = new List<CompProperties>{props};
        }
        foreach (var def in defs.Values)
            foreach (var rule in def.GetCompProperties<CompProperties_ModularWeaponNode>().conditionalIncompatibleParts)
                Check(!rule.ConfigErrors(def).Any(), "Authored conditional rule resolves valid modular parts: " + def.defName);

        const string sight = "HD_ModularPart_GasBlock_AR15_Part";
        const string shortGuard = "HD_ModularPart_Handguard_HACURXM4UP";
        string[] barrels = {"HD_ModularPart_Barrel_AR15103_Part", "HD_ModularPart_Barrel_AR15145_Part",
            "HD_ModularPart_Barrel_AR15300BLK103_Part", "HD_ModularPart_Barrel_AR1520_Part", "HD_ModularPart_Barrel_AR1520Pencil"};
        string[] guards = {shortGuard, "HD_ModularPart_Handguard_HACRISFDE", "HD_ModularPart_Handguard_HACURXUP",
            "HD_ModularPart_Handguard_M16A1_Part", "HD_ModularPart_Handguard_M16A2UP"};
        string[] blocks = {sight, "HD_ModularPart_GasBlock_MK12LP", "HD_ModularPart_GasBlock_300BLKLP"};
        int[][] orders = {new[]{0,1,2}, new[]{0,2,1}, new[]{1,0,2}, new[]{1,2,0}, new[]{2,0,1}, new[]{2,1,0}};
        foreach (string barrel in barrels)
        foreach (string guard in guards)
        foreach (string block in blocks)
        foreach (int[] order in orders)
        {
            bool blocked = block == sight && (barrel == barrels[0] || barrel == barrels[2]
                || (barrel == barrels[1] && guard != shortGuard));
            var names = new[]{barrel, guard, block};
            var tree = PartConflictTests.Node("AR15");
            var first = PartConflictTests.Node(names[order[0]], defs[names[order[0]]]); PartConflictTests.Add(tree, first);
            var second = PartConflictTests.Node(names[order[1]], defs[names[order[1]]]);
            Check(ModularWeaponPartConflicts.Find(tree, second.parent.def, second) == null,
                "Two parts alone must not activate a three-part clearance rule");
            PartConflictTests.Add(tree, second);
            var incoming = PartConflictTests.Node(names[order[2]], defs[names[order[2]]]);
            var conflict = ModularWeaponPartConflicts.Find(tree, incoming.parent.def, incoming);
            Check((conflict != null) == blocked, "Actual AR15 clearance in every installation order: " + string.Join("/", names));
            Check((ModularWeaponPartConflicts.Find(tree, incoming.parent.def) != null) == blocked,
                "Candidate listing agrees with the actual attachment gate");
            if (blocked)
                Check(conflict.Part == defs[sight] && conflict.Other == defs[guard] && conflict.Condition == defs[barrel],
                    "The rejection identifies the front sight, handguard and triggering barrel");
            Check(tree.ChildCount == 2 && incoming.parent.ParentHolder == null, "Conditional rejection never mutates the assembly");
        }
        int presets = 0;
        foreach (var preset in definitions.Values.Where(e => e.Name.LocalName == "Helodrace.ModernWar.ModularWeaponPresetDef"))
        {
            var tree = PartConflictTests.Node((string)preset.Element("weaponDef"), defs[(string)preset.Element("weaponDef")]);
            foreach (var part in preset.Descendants("part"))
                PartConflictTests.Add(tree, PartConflictTests.Node(part.Value, defs[part.Value]));
            Check(ModularWeaponPartConflicts.Find(PartConflictTests.Node("empty"), tree.parent.def, tree) == null,
                "Every shipped preset remains valid under real unconditional and conditional rules: " + preset.Element("defName"));
            presets++;
        }
        Console.WriteLine("PASS: 75 authored AR15 barrel/handguard/gas-block combinations in all 6 orders; " + presets + " valid presets.");
    }
}
