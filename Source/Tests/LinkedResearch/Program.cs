using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace;
using RimWorld;
using Verse;

internal static class Program
{
    private static int checks;
    private static Dictionary<string, XElement> definitions;
    private static List<XElement> linkDefinitions;
    private static Dictionary<string, ResearchProjectDef> projects;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Main(string[] args)
    {
        string game = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("RIMWORLD_PATH")
            ?? @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld";
        // Biotech is loaded for the toxic gas link.
        definitions = Directory.GetFiles(Path.Combine(game, "Data/Core/Defs/ResearchProjectDefs"), "*.xml")
            .Concat(Directory.GetFiles(Path.Combine(game, "Data/Biotech/Defs"), "*.xml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles("Defs/Research", "*.xml"))
            .SelectMany(file => XDocument.Load(file).Root.Elements("ResearchProjectDef"))
            .Where(def => def.Element("defName") != null)
            .ToDictionary(def => (string)def.Element("defName"));
        linkDefinitions = XDocument.Load("Defs/Research/ResearchLinks_Helod.xml").Root
            .Elements("Helodrace.ResearchLinkDef").ToList();
        Definitions();
        ConsolidatedMotorResearch();
        TechnologyMilestones();
        BothDirections();
        GroupGates();
        LifecycleAndStartingResearch();
        PrerequisitePropagation();
        ChainsAndRecovery();
        Console.WriteLine("PASS: " + checks + " linked research checks.");
    }

    private static List<ResearchLinkDef> Reset()
    {
        projects = definitions.Keys.ToDictionary(id => id, id => new ResearchProjectDef { defName = id });
        foreach (var item in definitions)
            projects[item.Key].prerequisites = (item.Value.Element("prerequisites")?.Elements("li")
                ?? Enumerable.Empty<XElement>()).Select(entry => projects[entry.Value]).ToList();
        var links = linkDefinitions.Select(def => new ResearchLinkDef
        {
            defName = (string)def.Element("defName"),
            leftProjects = def.Element("leftProjects").Elements("li").Select(e => projects[e.Value]).ToList(),
            rightProjects = def.Element("rightProjects").Elements("li").Select(e => projects[e.Value]).ToList()
        }).ToList();
        DefDatabase<ResearchLinkDef>.AllDefsListForReading = links;
        Find.ResearchManager = new ResearchManager();
        return links;
    }

    private static void Definitions()
    {
        var links = Reset();
        Check(links.Count == 18, "The curated catalog contains 18 links");
        Check(links.Select(link => link.defName).Distinct().Count() == links.Count, "Link names are unique");
        string documentation = File.ReadAllText("Docs/LinkedResearch.md");
        foreach (var link in links)
        {
            Check(!link.ConfigErrors().Any(), "Valid link: " + link.defName);
            Check(link.leftProjects.All(p => !p.defName.StartsWith("Helod")), "Left side uses vanilla research");
            Check(link.rightProjects.All(p => p.defName.StartsWith("Helod")), "Right side uses Helod research");
            Check(!link.leftProjects.Intersect(link.rightProjects).Any(), "The sides do not overlap");
            Check(link.leftProjects.Distinct().Count() == link.leftProjects.Count
                && link.rightProjects.Distinct().Count() == link.rightProjects.Count, "No duplicate members");
            Check(documentation.Contains("`" + link.defName + "`"), "Catalog documents " + link.defName);
            foreach (var project in link.leftProjects.Concat(link.rightProjects))
                Check(documentation.Contains("`" + project.defName + "`"), "Catalog documents " + project.defName);
        }
    }

    private static void BothDirections()
    {
        foreach (string id in linkDefinitions.Select(def => (string)def.Element("defName")))
        {
            for (int direction = 0; direction < 2; direction++)
            {
                var link = Reset().Single(candidate => candidate.defName == id);
                var source = direction == 0 ? link.leftProjects : link.rightProjects;
                var target = direction == 0 ? link.rightProjects : link.leftProjects;
                foreach (var project in source) Find.ResearchManager.FinishProject(project);
                Check(target.All(project => project.IsFinished), id + " completes the other path in direction " + direction);
                foreach (string excluded in new[] { "Fabrication", "AdvancedFabrication", "ShipBasics", "PoweredArmor",
                    "HelodIndustrialAutomation", "HelodMultiTaskMachining", "HelodHighAutomaticRifle", "HelodWireGuidedMissile", "HelodPASGT",
                    "HelodLowVelocityGrenades", "HelodAdvancedAmmunition", "HelodRemoteWeaponControl" })
                    Check(!projects[excluded].IsFinished, id + " does not grant unrelated later research " + excluded);
                int calls = Find.ResearchManager.FinishCalls;
                LinkedResearchUtility.Synchronize(Find.ResearchManager);
                Check(Find.ResearchManager.FinishCalls == calls, "Repeated synchronization is idempotent: " + id);
            }
        }
    }

    private static void ConsolidatedMotorResearch()
    {
        Check(!definitions.ContainsKey("HelodMiniMotor"), "The separate compact-motor research is removed");
        Check((int)definitions["HelodNumericalControl"].Element("baseCost") == 1800,
            "Consolidated numerical control uses the Stage I 1800-point infrastructure budget");
        Check(Requires("HelodNumericalControl", "HelodBasicMotor")
            && Requires("HelodNumericalControl", "HelodHighElectronics"),
            "The consolidated research follows motors and vacuum-tube electronics");
        Check(Requires("HelodAntiAirMachineGun", "HelodNumericalControl"),
            "Rotary anti-aircraft weapons use the consolidated motor milestone");
        var items = new[] { "Defs/ColdWar/Items/Weapons_ColdWar.xml", "Defs/ModernWar/Items/Weapons_ModernWar.xml" }
            .SelectMany(file => XDocument.Load(file).Root.Elements("ThingDef"))
            .Where(def => def.Element("defName") != null).ToDictionary(def => (string)def.Element("defName"));
        foreach (string id in new[] { "HD_PowerCutter", "HD_PowerforgeJackhammer", "HD_Gun_Nailgun_Weapon" })
            Check((string)items[id].Element("recipeMaker")?.Element("researchPrerequisite") == "HelodNumericalControl",
                "Motor-driven equipment follows numerical control: " + id);
        foreach (string file in Directory.GetFiles("Defs", "*.xml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Languages", "*.xml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles("Patches", "*.xml", SearchOption.AllDirectories)))
            Check(!File.ReadAllText(file).Contains("HelodMiniMotor"), "No obsolete motor definition/reference: " + file);
    }

    private static bool Requires(string id, string milestone)
    {
        return id == milestone || (definitions[id].Element("prerequisites")?.Elements("li")
            ?? Enumerable.Empty<XElement>()).Any(entry => Requires(entry.Value, milestone));
    }

    private static void TechnologyMilestones()
    {
        foreach (var link in Reset())
        {
            foreach (var milestone in new[] { ("Electricity", "HelodHighElectronics"),
                ("Machining", "HelodNumericalControl"), ("MicroelectronicsBasics", "HelodTransistor") })
            {
                if (!link.leftProjects.Any(project => Requires(project.defName, milestone.Item1))) continue;
                Check(link.rightProjects.Any(project => Requires(project.defName, milestone.Item2)),
                    link.defName + " preserves the " + milestone.Item1 + " milestone");
            }
        }

        Reset();
        // Even completing every race project outside the vacuum-tube branch must not bypass electricity.
        foreach (string id in definitions.Keys.Where(id => id.StartsWith("Helod")
            && !Requires(id, "HelodHighElectronics")))
            Find.ResearchManager.FinishProject(projects[id]);
        foreach (string id in definitions.Keys.Where(id => !id.StartsWith("Helod") && Requires(id, "Electricity")))
            Check(!projects[id].IsFinished, "Pre-vacuum research does not finish " + id);

        Find.ResearchManager.FinishProject(projects["HelodHighElectronics"]);
        Check(projects["Electricity"].IsFinished && !projects["HelodNumericalControl"].IsFinished,
            "Vacuum-tube electronics completes electricity without skipping numerical control");
        foreach (string id in definitions.Keys.Where(id => !id.StartsWith("Helod")
            && (Requires(id, "Machining") || Requires(id, "MicroelectronicsBasics"))))
            Check(!projects[id].IsFinished, "Vacuum tubes alone do not finish the later milestone " + id);

        Find.ResearchManager.FinishProject(projects["HelodNumericalControl"]);
        Check(projects["Machining"].IsFinished && !projects["MicroelectronicsBasics"].IsFinished,
            "Numerical control completes machining without skipping transistors");
        Check(!projects["MedicineProduction"].IsFinished && !projects["PrecisionRifling"].IsFinished,
            "Medicine and precision rifling wait for their microelectronics milestone");
        Find.ResearchManager.FinishProject(projects["HelodTransistor"]);
        Check(projects["MicroelectronicsBasics"].IsFinished && projects["MedicineProduction"].IsFinished
            && projects["PrecisionRifling"].IsFinished, "Transistors release the ready microelectronics descendants");
    }

    private static void GroupGates()
    {
        foreach (string id in linkDefinitions.Select(def => (string)def.Element("defName")))
        {
            for (int direction = 0; direction < 2; direction++)
            {
                var link = Reset().Single(candidate => candidate.defName == id);
                var source = direction == 0 ? link.leftProjects : link.rightProjects;
                var target = direction == 0 ? link.rightProjects : link.leftProjects;
                if (source.Count < 2) continue;
                DefDatabase<ResearchLinkDef>.AllDefsListForReading = new List<ResearchLinkDef> { link };
                foreach (var project in source.Take(source.Count - 1)) project.IsFinished = true;
                LinkedResearchUtility.Synchronize(Find.ResearchManager);
                Check(target.All(project => !project.IsFinished), "A partial group does not satisfy " + id);
                source.Last().IsFinished = true;
                LinkedResearchUtility.Synchronize(Find.ResearchManager);
                Check(target.All(project => project.IsFinished), "The entire group satisfies " + id);
            }
        }
    }

    private static void LifecycleAndStartingResearch()
    {
        Reset();
        projects["AirConditioning"].IsFinished = true;
        new LinkedResearchGameComponent(new Game()).LoadedGame();
        Check(projects["HelodHeatPump"].IsFinished, "Loading synchronizes already completed vanilla research");
        Reset();
        projects["HelodHeatPump"].IsFinished = true;
        new LinkedResearchGameComponent(new Game()).StartedNewGame();
        Check(!projects["AirConditioning"].IsFinished, "A heat pump alone does not bypass the electricity milestone");
        projects["HelodHighElectronics"].IsFinished = true;
        new LinkedResearchGameComponent(new Game()).StartedNewGame();
        Check(projects["AirConditioning"].IsFinished, "New games synchronize the complete Helod path");
        Reset();
        foreach (string id in new[] { "ComplexFurniture", "ComplexClothing", "Smithing", "HelodFrontierSpirit",
            "HelodMechanicalEngineering", "HelodExperimentalAnalysis" }) projects[id].IsFinished = true;
        new LinkedResearchGameComponent(new Game()).StartedNewGame();
        Check(projects.Values.Count(project => project.IsFinished) == 6, "Wild West starting research grants no additional tiers");
    }

    private static void PrerequisitePropagation()
    {
        Reset();
        Find.ResearchManager.FinishProject(projects["Electricity"]);
        Check(projects["HelodHighElectronics"].IsFinished && !projects["Machining"].IsFinished
            && !projects["HelodNumericalControl"].IsFinished,
            "Vanilla electricity reaches vacuum tubes but does not grant machining");
        Reset();
        Find.ResearchManager.FinishProject(projects["HelodTotalSynthesisMedicine"]);
        Check(!projects["MedicineProduction"].IsFinished && !projects["MicroelectronicsBasics"].IsFinished
            && !projects["HelodTransistor"].IsFinished && !projects["Electricity"].IsFinished,
            "Early synthesis cannot complete medicine and pull in later electronic prerequisites");
        Find.ResearchManager.FinishProject(projects["HelodTransistor"]);
        Check(projects["MedicineProduction"].IsFinished,
            "Synthesis and transistors together complete vanilla medicine production");
    }

    private static void ChainsAndRecovery()
    {
        var a = new ResearchProjectDef { defName = "A", IsFinished = true };
        var b = new ResearchProjectDef { defName = "B" };
        var c = new ResearchProjectDef { defName = "C" };
        DefDatabase<ResearchLinkDef>.AllDefsListForReading = new List<ResearchLinkDef>
        {
            Pair(b, c), Pair(a, b), Pair(c, a)
        };
        var manager = new ResearchManager();
        LinkedResearchUtility.Synchronize(manager);
        Check(a.IsFinished && b.IsFinished && c.IsFinished, "Reversed-order cyclic links converge through nested finish callbacks");
        Check(manager.FinishCalls == 2 && manager.Dialogs == 0 && manager.Letters == 0,
            "Linked completion runs once per unfinished project without duplicate popups");
        b.IsFinished = false;
        manager.ThrowOn = "B";
        try { LinkedResearchUtility.Synchronize(manager); throw new Exception("Expected injected failure"); }
        catch (InvalidOperationException) { }
        manager.ThrowOn = null;
        LinkedResearchUtility.Synchronize(manager);
        Check(b.IsFinished, "The synchronization guard recovers after an exception");
        LinkedResearchUtility.Synchronize(null);
    }

    private static ResearchLinkDef Pair(ResearchProjectDef left, ResearchProjectDef right)
    {
        return new ResearchLinkDef { leftProjects = new List<ResearchProjectDef> { left },
            rightProjects = new List<ResearchProjectDef> { right } };
    }
}
