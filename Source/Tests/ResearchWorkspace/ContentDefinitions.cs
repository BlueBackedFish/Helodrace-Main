using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

internal static partial class Program
{
    private static void ContentDefinitions()
    {
        string game = Environment.GetEnvironmentVariable("RIMWORLD_PATH") ?? @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld";
        var all = Directory.GetFiles(Path.Combine(game, "Data/Core/Defs"), "*.xml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Defs", "*.xml", SearchOption.AllDirectories))
            .SelectMany(f => XDocument.Load(f).Root.Elements()).ToList();
        var things = all.Where(e => e.Name.LocalName == "ThingDef" && e.Element("defName") != null)
            .GroupBy(e => (string)e.Element("defName")).ToDictionary(g => g.Key, g => g.Last());
        var bases = all.Where(e => e.Name.LocalName == "ThingDef" && e.Attribute("Name") != null)
            .GroupBy(e => (string)e.Attribute("Name")).ToDictionary(g => g.Key, g => g.Last());
        var research = all.Where(e => e.Name.LocalName == "ResearchProjectDef" && e.Element("defName") != null)
            .GroupBy(e => (string)e.Element("defName")).ToDictionary(g => g.Key, g => g.Last());
        var recipes = all.Where(e => e.Name.LocalName == "RecipeDef").ToList();
        var presets = all.Where(e => e.Name.LocalName.EndsWith("ModularWeaponPresetDef") && e.Element("defName") != null)
            .ToDictionary(e => (string)e.Element("defName"));
        var links = XDocument.Load("Defs/Research/ResearchLinks_Helod.xml").Root.Elements().Select(e => new[] {
            e.Element("leftProjects").Elements("li").Select(x => x.Value).ToList(),
            e.Element("rightProjects").Elements("li").Select(x => x.Value).ToList() }).ToList();

        IEnumerable<XElement> Chain(XElement e)
        {
            yield return e;
            var seen = new HashSet<string>();
            while (e.Attribute("ParentName") != null && seen.Add((string)e.Attribute("ParentName")))
            {
                if (!bases.TryGetValue((string)e.Attribute("ParentName"), out e)) yield break;
                yield return e;
            }
        }
        XElement At(XElement e, string path) => path.Split('/').Aggregate(e, (current, segment) => current?.Element(segment));
        bool Reset(XElement e) => string.Equals((string)e?.Attribute("Inherit"), "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals((string)e?.Attribute("IsNull"), "true", StringComparison.OrdinalIgnoreCase);
        XElement Inherited(XElement e, string path)
        {
            foreach (var ancestor in Chain(e))
            {
                var found = At(ancestor, path);
                if (found != null) return found;
                var segments = path.Split('/');
                for (int i = 1; i < segments.Length; i++)
                    if (Reset(At(ancestor, string.Join("/", segments.Take(i))))) return null;
            }
            return null;
        }
        List<string> Values(XElement e, string path) => Inherited(e, path)?.Elements("li").Select(x => x.Value).ToList() ?? new List<string>();
        List<string> RecipeGates(XElement e) => Values(e, "recipeMaker/researchPrerequisites")
            .Concat(new[] { (string)Inherited(e, "recipeMaker/researchPrerequisite") }).Where(x => !string.IsNullOrEmpty(x)).ToList();
        List<string> PartGates(XElement e)
        {
            foreach (var ancestor in Chain(e))
            {
                var component = ancestor.Element("comps")?.Elements("li")
                    .FirstOrDefault(c => ((string)c.Attribute("Class"))?.EndsWith("CompProperties_ModularWeaponNode") == true);
                if (component?.Element("requiredResearch") != null)
                    return component.Element("requiredResearch").Elements("li").Select(x => x.Value).ToList();
            }
            return new List<string>();
        }
        HashSet<string> Finish(IEnumerable<string> requirements)
        {
            var result = new HashSet<string>();
            void Add(string id)
            {
                Check(research.ContainsKey(id), "Content research reference resolves: " + id);
                if (!result.Add(id)) return;
                foreach (var p in research[id].Element("prerequisites")?.Elements("li") ?? Enumerable.Empty<XElement>()) Add(p.Value);
            }
            foreach (string id in requirements) Add(id);
            int before;
            do
            {
                before = result.Count;
                foreach (var link in links)
                    for (int side = 0; side < 2; side++)
                        if (link[side].All(result.Contains)) foreach (string id in link[1 - side]) Add(id);
            } while (before != result.Count);
            return result;
        }

        Check(!research.ContainsKey("HelodColdWarGrenades"), "The single-item M67 research is consolidated");
        Check(research.ContainsKey("HelodLessLethalClose") && research.ContainsKey("HelodLowHeadSet"),
            "Both future-content research nodes remain available");
        Check(RecipeGates(things["HD_Grenade_M67_Item"]).Contains("HelodColdWarTech"), "M67 follows the consolidated Cold War research");
        Check(RecipeGates(things["HD_Gun_M79_Weapon"]).Contains("HelodLowVelocityGrenades"), "M79 requires the dedicated launcher research");
        foreach (string id in new[] { "HD_40mmM381HE_Round", "HD_40mmM433HEDP_Round", "HD_40mmM576MP_Round", "HD_40mmM651CS_Round", "HD_40mmSponge_Round" })
            Check(RecipeGates(things[id]).Contains("HelodLowVelocityGrenades"), "Low-velocity ammunition shares the platform gate: " + id);
        Check(RecipeGates(things["HD_40mmM651CS_Round"]).Contains("HelodLessLethalChemical"), "CS ammunition also requires chemical control");
        Check(RecipeGates(things["HD_40mmSponge_Round"]).Contains("HelodLessLethalLowRanged"), "Sponge ammunition also requires less-lethal control");
        foreach (string id in new[] { "HD_ModularPart_Underbarrel_M203A2_Part", "HD_ModularPart_UnderbarrelBarrel_M203A2_Part", "HD_ModularPart_UnderbarrelTrigger_M203A2_Part" })
            Check(PartGates(things[id]).Contains("HelodLowVelocityGrenades") && PartGates(things[id]).Contains("HelodHighAutomaticRifle"), "Underbarrel variants retain both technology gates: " + id);
        foreach (string id in new[] { "HD_CrowsComputer", "HD_Building_CrowsII" })
            Check(Values(things[id], "researchPrerequisites").SequenceEqual(new[] { "HelodRemoteWeaponControl" }), "The entire remote system uses its dedicated research: " + id);
        Check(RecipeGates(things["HD_Gun_M249_Weapon"]).Contains("HelodMidAutomaticRifle"), "Basic M249 moves out of early machine guns");
        Check(PartGates(things["HD_ModularPart_Handguard_M249E3_Part"]).Contains("HelodHighAutomaticRifle"), "Late M249 accessories remain later upgrades");
        Check(PartGates(things["HD_Ammunition_556M855A1_Ammo"]).Contains("HelodAdvancedAmmunition"), "Enhanced ammunition is a separate investment");
        foreach (string id in new[] { "HD_Apparel_IBTVAssault", "HD_Apparel_MOLLEBattleBelt" })
            Check(RecipeGates(things[id]).Contains("HelodPASGT"), "Modern protective clothing is gated on its production recipe: " + id);

        int baselineCount = 0;
        foreach (var thing in things.Values)
        {
            var presetName = thing.Descendants("spawnPreset").Select(x => x.Value).FirstOrDefault();
            if (presetName == null || !(((string)thing.Element("defName"))?.StartsWith("HD_Gun_") ?? false)) continue;
            var gates = RecipeGates(thing).Concat(Values(thing, "researchPrerequisites")).ToList();
            foreach (string bench in Values(thing, "recipeMaker/recipeUsers"))
                if (things.TryGetValue(bench, out var benchDef)) gates.AddRange(Values(benchDef, "researchPrerequisites"));
            foreach (var recipe in recipes.Where(r => r.Element("products")?.Element((string)thing.Element("defName")) != null))
            {
                gates.AddRange(recipe.Element("researchPrerequisites")?.Elements("li").Select(x => x.Value) ?? Enumerable.Empty<string>());
                if (recipe.Element("researchPrerequisite") != null) gates.Add((string)recipe.Element("researchPrerequisite"));
            }
            Check(gates.Count > 0, "A basic weapon has a defined production milestone: " + thing.Element("defName").Value);
            var completed = Finish(gates);
            foreach (var part in presets[presetName].Descendants("part"))
            {
                Check(things.ContainsKey(part.Value), "Basic preset parts resolve: " + part.Value);
                Check(PartGates(things[part.Value]).All(completed.Contains),
                    "Every basic preset component is researched when its weapon is available: " + presetName + " / " + part.Value);
                var chosenMount = part.Parent.Element("mountId")?.Value;
                if (chosenMount != null)
                    Check(Chain(things[part.Value]).Any(e => e.Descendants("mounts").Elements("li").Any(m => (string)m.Element("id") == chosenMount)),
                        "Preset mount still exists after changing basic ammunition: " + part.Value + " / " + chosenMount);
            }
            baselineCount++;
        }
        Check(baselineCount == 10, "All ten automatically generated weapon configurations are covered");
    }
}
