using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace;
using Helodrace.ResearchUI;
using RimWorld;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static ResearchProjectDef Project(string name, params ResearchProjectDef[] parents) =>
        new ResearchProjectDef { defName = name, prerequisites = parents.ToList() };
    private static ResearchQueue Queue()
    {
        DefDatabase<ResearchLinkDef>.AllDefsListForReading.Clear();
        MainButtonDefOf.Research.tabWindowClass = typeof(MainTabWindow_Research);
        Find.ResearchManager = new ResearchManager();
        Current.Game = new Game();
        var queue = new ResearchQueue(Current.Game); Current.Game.Component = queue;
        return queue;
    }
    private static void Main()
    {
        PlansAndQueue(); SaveQueue(); Geometry(); FlowAndRouting(); DefinitionsAndPreviews(); Compatibility();
        Console.WriteLine("PASS: " + checks + " research workspace checks.");
    }
    private static void PlansAndQueue()
    {
        var a = Project("A"); var b = Project("B", a); var c = Project("C", a); var d = Project("D", b, c);
        var q = Queue(); q.Add(d);
        Check(q.Plan.SequenceEqual(new[] { a, b, c, d }), "Shared prerequisite appears once in topological order");
        Check(Find.ResearchManager.Active == a, "First prerequisite starts");
        q.Add(d); Check(q.Goals.Count == 1, "Duplicate goals are ignored");
        Find.ResearchManager.FinishProject(a); q.Advance();
        Check(Find.ResearchManager.Active == b, "Queue advances after completion");
        q.TogglePause(); Check(Find.ResearchManager.Active == null && q.Paused, "Pausing stops only owned research");
        q.TogglePause(); Check(Find.ResearchManager.Active == b, "Resume starts remaining prerequisite");
        var manual = Project("manual"); Find.ResearchManager.SetCurrentProject(manual);
        q.Remove(d); Check(Find.ResearchManager.Active == manual, "Removing a goal does not stop manual research");
        q = Queue(); a.IsFinished = b.IsFinished = c.IsFinished = false;
        q.Add(d); q.Add(c); q.Move(1, -1);
        Check(q.Goals.SequenceEqual(new[] { c, d }), "Goal order is editable without duplicating prerequisites");
        q.Remove(d); Check(q.Plan.SequenceEqual(new[] { a, c }), "Deleting a goal retains shared work needed by another goal");
        q.Remove(c); Check(q.Plan.Count == 0 && Find.ResearchManager.Active == null, "Removing last goal cancels orphaned owned work");
        q = Queue(); a.IsFinished = false; a.ExtraRequirementsMet = false; q.Add(a);
        Check(Find.ResearchManager.Active == null && q.Goals.Count == 1, "Unavailable benches or techprints leave a waiting goal");
        a.ExtraRequirementsMet = true; q.Advance(); Check(Find.ResearchManager.Active == a, "Available research resumes");
        var hidden = Project("hidden"); var goal = Project("goal"); goal.hiddenPrerequisites.Add(hidden);
        q = Queue(); q.Add(goal); Check(q.Plan.SequenceEqual(new[] { hidden, goal }), "Queue respects hidden prerequisites too");
        q = Queue(); q.Add(a); MainButtonDefOf.Research.tabWindowClass = typeof(ForeignWindow);
        Find.ResearchManager.SetCurrentProject(manual); q.TogglePause(); q.Advance();
        Check(Find.ResearchManager.Active == manual, "External queue ownership prevents interference");
        var cycle = Project("cycle"); cycle.prerequisites.Add(cycle); q = Queue(); q.Add(cycle);
        Check(q.Goals.Count == 0, "Cyclic mod research cannot enter the queue");
        q.Goals.Add(cycle); Check(q.Plan.Count == 0 && q.Paused, "Bad saved definitions pause safely");
        q = Queue(); a.IsFinished = false; b.IsFinished = false; q.Add(b);
        a.IsFinished = b.IsFinished = true; q.Advance();
        Check(q.Goals.Count == 0 && Find.ResearchManager.Active == null, "OR-completed goals and prerequisites are removed");
    }
    private static void SaveQueue()
    {
        var q = Queue(); var a = Project("saved"); q.Add(a); q.TogglePause();
        Scribe.mode = LoadSaveMode.Saving; q.ExposeData();
        var restored = new ResearchQueue(Current.Game);
        Scribe.mode = LoadSaveMode.LoadingVars; restored.ExposeData();
        Scribe.mode = LoadSaveMode.PostLoadInit; restored.ExposeData(); Scribe.mode = LoadSaveMode.Inactive;
        Check(restored.Goals.Count == 1 && restored.Goals[0] == a && restored.Paused, "Goals and pause survive save/load");
    }
    private sealed class ForeignWindow : Window { }
    private sealed class ForeignHost : IResearchWorkspaceHost
    {
        public string Id => "test.foreign";
        public Type WindowType => typeof(ForeignWindow);
        public bool OwnsResearchQueue { get; set; } = true;
        public bool IsActive(Window window) => window is ForeignWindow;
        public ResearchProjectDef Selected(Window window) => null;
        public void Select(Window window, ResearchProjectDef project) { }
        public IResearchQueueBridge Queue => new NativeQueueBridge();
    }
    private static void Compatibility()
    {
        Queue();
        var vanilla = new MainTabWindow_Research { CurTab = new ResearchTabDef { defName = "Main" } };
        Check(ResearchWorkspaceWindowPatch.Prefix(vanilla, new UnityEngine.Rect(), 1000), "Other vanilla tabs retain their renderer");
        vanilla.CurTab.defName = "HelodTechTab";
        int draws = ResearchWorkspaceView.Calls;
        Check(!ResearchWorkspaceWindowPatch.Prefix(vanilla, new UnityEngine.Rect(), 1000)
            && ResearchWorkspaceView.Calls == draws + 1, "Only the Helod graph area is replaced");
        MainButtonDefOf.Research.tabWindowClass = typeof(ForeignWindow);
        Check(ResearchWindowCompatibility.ExternalQueueOwnsResearch, "Unknown replacement window disables the native queue");
        Check(!ResearchWindowCompatibility.TryDraw(new ForeignWindow(), new UnityEngine.Rect()), "Unknown replacement UI falls back safely");
        Check(ResearchWorkspaceWindowPatch.Prefix(vanilla, new UnityEngine.Rect(), 1000), "Replacement main button prevents vanilla interception");
        var host = new ForeignHost(); ResearchWindowCompatibility.Register(host);
        Check(ResearchWindowCompatibility.TryDraw(new ForeignWindow(), new UnityEngine.Rect())
            && ReferenceEquals(ResearchWorkspaceView.LastHost, host), "A registered replacement uses its own host bridge");
        Check(ResearchWindowCompatibility.ExternalQueueOwnsResearch, "Registered foreign queue remains in charge");
        host.OwnsResearchQueue = false;
        Check(!ResearchWindowCompatibility.ExternalQueueOwnsResearch, "A registered renderer may explicitly retain the native queue");
        var replacement = new ForeignHost(); ResearchWindowCompatibility.Register(replacement);
        ResearchWindowCompatibility.TryDraw(new ForeignWindow(), new UnityEngine.Rect());
        Check(ReferenceEquals(ResearchWorkspaceView.LastHost, replacement), "Re-registering the same adapter ID replaces it");
    }
    private static ResearchLine Line(float ax, float ay, float bx, float by) =>
        new ResearchLine(new ResearchPoint(ax, ay), new ResearchPoint(bx, by));
    private static void Geometry()
    {
        var lower = Line(0, 0, 100, 0);
        var result = ResearchLineGeometry.VisiblePieces(lower, new[] { Line(50, -20, 50, 20) }, 5);
        Check(result.Count == 2 && result[0].B.X == 45 && result[1].A.X == 55, "Crossing removes 10px from lower line");
        result = ResearchLineGeometry.VisiblePieces(lower, new[] { Line(100, 0, 120, 20) }, 5);
        Check(result.Count == 1 && result[0].B.X == 100, "Shared endpoint is preserved");
        result = ResearchLineGeometry.VisiblePieces(lower, new[] { Line(50, 0, 50, 20) }, 5);
        Check(result.Count == 2, "Upper polyline corner still crosses above lower line");
        result = ResearchLineGeometry.VisiblePieces(lower, new[] { Line(20, 0, 80, 0) }, 5);
        Check(result.Count == 2 && result[0].B.X == 20 && result[1].A.X == 80, "Collinear overlap leaves only upper stroke");
        result = ResearchLineGeometry.VisiblePieces(lower, new[] { Line(50, -10, 50, 10), Line(52, -10, 52, 10) }, 5);
        Check(result.Count == 2 && result[1].A.X == 57, "Nearby crossing intervals merge");
        Check(ResearchLineGeometry.VisiblePieces(Line(0, 0, 0, 0), new ResearchLine[0], 5).Count == 0, "Degenerate lines are safe");
        var random = new Random(214);
        for (int i = 0; i < 500; i++)
        {
            var strokes = Enumerable.Range(0, 12).Select(_ => Line(random.Next(-20, 120), -20, random.Next(-20, 120), 20));
            result = ResearchLineGeometry.VisiblePieces(lower, strokes, 5);
            float end = 0;
            foreach (var piece in result)
            {
                Check(piece.A.X >= end - 0.001f && piece.B.X >= piece.A.X && piece.B.X <= 100.001f, "Crossing cuts remain ordered, finite and in bounds");
                end = piece.B.X;
            }
        }
    }
    private static void FlowAndRouting()
    {
        Check(ResearchZoom.Clamp(-1) == 0.3f && ResearchZoom.Clamp(0.3f) == 0.3f, "Minimum zoom is exactly 30 percent");
        Check(ResearchZoom.Clamp(5) == 1.5f, "Maximum zoom remains 150 percent");
        float shifted = ResearchZoom.Anchor(200, 70, 0.9f, 0.3f);
        Check(Math.Abs((shifted + 70) / 0.3f - (200 + 70) / 0.9f) < 0.001f, "Wheel zoom preserves the world point under the mouse");
        var a = Project("A"); var b = Project("B", a); var c = Project("C", a); var d = Project("D", b, c);
        var cells = ResearchFlowLayout.Arrange(new[] { d, c, b, a }, p => p.prerequisites);
        Check(cells[a].Column == 0 && cells[d].Column == 2, "Local dependency depth determines columns regardless of definition order");
        Check(cells[b].Column == cells[c].Column && cells[b].Row != cells[c].Row, "Branches occupy consistent columns and separate rows");
        Check(cells.Values.Select(cell => Tuple.Create(cell.Column, cell.Row)).Distinct().Count() == 4, "No overlapping layout cells");
        var e = Project("E", b); var f = Project("F", e);
        var branches = ResearchFlowLayout.Arrange(new[] { a, c, b, e, f }, p => p.prerequisites);
        Check(branches[e].Row == branches[b].Row && branches[f].Row == branches[e].Row,
            "An uninterrupted branch retains its lane across columns instead of being packed back to row zero");
        Check(cells[d].Row >= Math.Min(cells[b].Row, cells[c].Row) && cells[d].Row <= Math.Max(cells[b].Row, cells[c].Row),
            "A merge sits between its parent branches");
        a.prerequisites.Add(d);
        Check(ResearchFlowLayout.Arrange(new[] { a, b, c, d }, p => p.prerequisites).Count == 4, "Cyclic extensions still render");
        var boxes = new[] { new ResearchBox(20, -20, 60, 20), new ResearchBox(80, 10, 120, 60) };
        var from = new ResearchPoint(0, 0); var to = new ResearchPoint(140, 40);
        var router = new ResearchOrthogonalRouter(boxes, new[] { from, to });
        var route = router.Route(from, to);
        Check(route.Count >= 3 && route.First().A.X == from.X && route.Last().B.X == to.X, "Orthogonal route reaches both endpoints around obstacles");
        foreach (var line in route)
        {
            Check(line.A.X == line.B.X || line.A.Y == line.B.Y, "Every routed segment is orthogonal");
            Check(!boxes.Any(box => box.Blocks(line.A, line.B)), "Every routed segment avoids node interiors");
        }
        Check(router.Route(new ResearchPoint(17, 0), to).Count == 0, "Unknown ports cannot select a different grid vertex");
        Check(router.Route(from, from).Count == 0, "Coincident endpoints do not produce stray segments");
        var random = new Random(371);
        for (int test = 0; test < 60; test++)
        {
            var obstacles = Enumerable.Range(0, 12).Select(i => new ResearchBox(40 + i % 4 * 80, 30 + i / 4 * 80,
                80 + i % 4 * 80, 70 + i / 4 * 80)).ToList();
            from = new ResearchPoint(0, random.Next(0, 240)); to = new ResearchPoint(360, random.Next(0, 240));
            route = new ResearchOrthogonalRouter(obstacles, new[] { from, to }).Route(from, to);
            Check(route.Count > 0, "Grid routes across multiple staggered obstacles");
            for (int i = 0; i < route.Count; i++)
            {
                var line = route[i];
                Check(!obstacles.Any(box => box.Blocks(line.A, line.B)), "Random route avoids every obstacle");
                if (i > 0) Check(route[i - 1].B.X == line.A.X && route[i - 1].B.Y == line.A.Y, "Route is continuous at every bend");
            }
        }
    }
    private static void DefinitionsAndPreviews()
    {
        var race = Directory.GetFiles("Defs/Research", "*.xml").SelectMany(file => XDocument.Load(file).Root.Elements("ResearchProjectDef"))
            .ToDictionary(p => (string)p.Element("defName"));
        var sections = XDocument.Load("Defs/Research/ResearchSections_Helod.xml").Root.Elements().ToList();
        var members = sections.SelectMany(s => s.Element("projects").Elements("li")).Select(e => e.Value).ToList();
        Check(sections.Count == 8 && members.Count == race.Count && members.Distinct().Count() == race.Count,
            "Every race project belongs to exactly one of eight technology stages");
        Check(members.All(race.ContainsKey), "No unresolved section membership");
        Check(sections.Select(s => (int)s.Element("order")).SequenceEqual(Enumerable.Range(0, sections.Count)),
            "Technology stages remain in forward progression order");
        var depths = new Dictionary<string, int>();
        Func<string, int> depth = null;
        depth = name => depths.TryGetValue(name, out int value) ? value : depths[name] = 1 +
            (race[name].Element("prerequisites")?.Elements("li") ?? Enumerable.Empty<XElement>())
                .Where(e => race.ContainsKey(e.Value)).Select(e => depth(e.Value)).DefaultIfEmpty(-1).Max();
        int[] lower = { 0, 2, 4, 6, 8, 10, 13, 15 }, upper = { 1, 3, 5, 7, 9, 12, 14, 16 };
        foreach (var section in sections)
        {
            int stage = (int)section.Element("order");
            foreach (var member in section.Element("projects").Elements("li"))
                Check(depth(member.Value) >= lower[stage] && depth(member.Value) <= upper[stage],
                    "Cross-discipline membership matches real prerequisite depth: " + member.Value);
        }
        var layoutProjects = race.Keys.ToDictionary(name => name, name => Project(name));
        foreach (var pair in race)
            layoutProjects[pair.Key].prerequisites = (pair.Value.Element("prerequisites")?.Elements("li") ?? Enumerable.Empty<XElement>())
                .Where(e => layoutProjects.ContainsKey(e.Value)).Select(e => layoutProjects[e.Value]).ToList();
        var global = ResearchFlowLayout.Arrange(members.Select(name => layoutProjects[name]).ToList(), p => p.prerequisites);
        foreach (var project in layoutProjects.Values)
            foreach (var prerequisite in project.prerequisites)
                Check(global[prerequisite].Column < global[project].Column,
                    "Every real dependency advances strictly right: " + prerequisite.defName + " -> " + project.defName);
        foreach (var stage in sections.Skip(1))
        {
            int order = (int)stage.Element("order");
            var previous = sections.First(s => (int)s.Element("order") == order - 1);
            Check(previous.Element("projects").Elements("li").Max(e => global[layoutProjects[e.Value]].Column)
                < stage.Element("projects").Elements("li").Min(e => global[layoutProjects[e.Value]].Column),
                "Later sections never fold back to the left");
        }
        var sectionTranslations = XDocument.Load("Languages/Korean (한국어)/DefInjected/Helodrace.ResearchUI.HelodResearchSectionDef/ResearchSections.xml").Root;
        Check(sectionTranslations.Elements().Select(e => e.Name.LocalName).OrderBy(n => n).SequenceEqual(
            sections.Select(s => (string)s.Element("defName") + ".label").OrderBy(n => n)), "Section names have complete translations without obsolete categories");
        var en = XDocument.Load("Languages/English/Keyed/ResearchWorkspace.xml").Root;
        var ko = XDocument.Load("Languages/Korean (한국어)/Keyed/ResearchWorkspace.xml").Root;
        Check(en.Elements().Select(e => e.Name).SequenceEqual(ko.Elements().Select(e => e.Name)), "English/Korean UI keys match");
        foreach (var part in Directory.GetFiles("Defs", "*.xml", SearchOption.AllDirectories)
            .SelectMany(file => XDocument.Load(file).Descendants("requiredResearch")))
            foreach (var entry in part.Elements("li")) Check(race.ContainsKey(entry.Value), "Part research resolves: " + entry.Value);

        string game = Environment.GetEnvironmentVariable("RIMWORLD_PATH") ?? @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld";
        var defs = Directory.GetFiles(Path.Combine(game, "Data/Core/Defs/ResearchProjectDefs"), "*.xml")
            .SelectMany(file => XDocument.Load(file).Root.Elements("ResearchProjectDef"))
            .Where(e => e.Element("defName") != null).Concat(race.Values).ToList();
        var catalog = XDocument.Load("Defs/Research/ResearchLinks_Helod.xml").Root.Elements().ToList();
        foreach (string target in race.Keys)
        {
            Queue();
            var projects = defs.ToDictionary(e => (string)e.Element("defName"), e => Project((string)e.Element("defName")));
            foreach (var def in defs)
                projects[(string)def.Element("defName")].prerequisites = (def.Element("prerequisites")?.Elements("li")
                    ?? Enumerable.Empty<XElement>()).Select(e => projects[e.Value]).ToList();
            var links = catalog.Select(def => new ResearchLinkDef
            {
                leftProjects = def.Element("leftProjects").Elements("li").Select(e => projects[e.Value]).ToList(),
                rightProjects = def.Element("rightProjects").Elements("li").Select(e => projects[e.Value]).ToList()
            }).ToList();
            DefDatabase<ResearchLinkDef>.AllDefsListForReading = links;
            var expected = ResearchPlan.Predict(projects[target], projects.Values.Where(p => p.IsFinished),
                links.Select(l => Tuple.Create((IList<ResearchProjectDef>)l.leftProjects, (IList<ResearchProjectDef>)l.rightProjects)),
                p => p.prerequisites);
            Check(!projects.Values.Any(p => p.IsFinished), "Preview never mutates real completion state");
            Find.ResearchManager.FinishProject(projects[target]);
            Check(expected.SetEquals(projects.Values.Where(p => p.IsFinished)), "Preview matches real linked completion closure: " + target);
        }
    }
}
