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
        definitions = Directory.GetFiles(Path.Combine(game, "Data/Core/Defs/ResearchProjectDefs"), "*.xml")
            .Concat(Directory.GetFiles("Defs/Research", "*.xml"))
            .SelectMany(file => XDocument.Load(file).Root.Elements("ResearchProjectDef"))
            .Where(def => def.Element("defName") != null)
            .ToDictionary(def => (string)def.Element("defName"));
        linkDefinitions = XDocument.Load("Defs/Research/ResearchLinks_Helod.xml").Root
            .Elements("Helodrace.ResearchLinkDef").ToList();
        Definitions();
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
        Check(links.Count == 15, "The curated catalog contains 15 links");
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
                    "HelodIndustrialAutomation", "HelodMultiTaskMachining", "HelodHighAutomaticRifle", "HelodWireGuidedMissile", "HelodPASGT" })
                    Check(!projects[excluded].IsFinished, id + " does not grant unrelated later research " + excluded);
                int calls = Find.ResearchManager.FinishCalls;
                LinkedResearchUtility.Synchronize(Find.ResearchManager);
                Check(Find.ResearchManager.FinishCalls == calls, "Repeated synchronization is idempotent: " + id);
            }
        }
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
        Check(projects["AirConditioning"].IsFinished, "New games synchronize already completed Helod research");
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
        Check(projects["HelodBasicMotor"].IsFinished && projects["Machining"].IsFinished,
            "The electrical motor's prerequisites also synchronize machining");
        Reset();
        Find.ResearchManager.FinishProject(projects["HelodTotalSynthesisMedicine"]);
        Check(projects["MedicineProduction"].IsFinished && projects["MicroelectronicsBasics"].IsFinished
            && projects["HelodTransistor"].IsFinished, "Medicine production also completes its vanilla microelectronics prerequisite");
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
