using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ResearchUI
{
    public sealed class ResearchWorkspaceView
    {
        private sealed class Section
        {
            public string Id, Label;
            public Color Color;
            public Rect Bounds;
            public List<ResearchProjectDef> Projects;
        }
        private sealed class Edge
        {
            public ResearchProjectDef From, To;
            public List<ResearchLine> Lines;
            public string Key;
            public int Priority;
        }
        private sealed class Stroke
        {
            public Edge Edge;
            public ResearchLine Line;
        }
        private static readonly ConditionalWeakTable<Window, ResearchWorkspaceView> views =
            new ConditionalWeakTable<Window, ResearchWorkspaceView>();
        public static ResearchWorkspaceView For(Window window) => views.GetValue(window, _ => new ResearchWorkspaceView());
        private readonly Dictionary<ResearchProjectDef, Rect> nodes = new Dictionary<ResearchProjectDef, Rect>();
        private readonly Dictionary<ResearchProjectDef, Section> nodeSections = new Dictionary<ResearchProjectDef, Section>();
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private readonly List<Section> sections = new List<Section>();
        private List<Stroke> strokes = new List<Stroke>();
        private List<ResearchProjectDef> preview = new List<ResearchProjectDef>();
        private Vector2 scroll, detailScroll;
        private float zoom = 0.9f, builtZoom;
        private string search = "", language, edgeKey;
        private int page, revision = -1;
        private bool dirty = true;
        private ResearchProjectDef lastSelected;
        private float width, height;
        private const float NodeWidth = 180, ColumnWidth = 210;

        public void Draw(Rect rect, IResearchWorkspaceHost host, Window window)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                ResearchProjectDef selected = host.Selected(window);
                string currentLanguage = LanguageDatabase.activeLanguage?.folderName;
                if (dirty || builtZoom != zoom || language != currentLanguage || revision != ResearchWorkspaceRevision.Value) Build();
                if (lastSelected != selected || revision != ResearchWorkspaceRevision.Value)
                {
                    if (lastSelected != selected)
                    {
                        var section = sections.FirstOrDefault(s => s.Projects.Contains(selected));
                        if (section != null && collapsed.Remove(section.Id)) Build();
                        Focus(selected);
                    }
                    lastSelected = selected; revision = ResearchWorkspaceRevision.Value;
                    detailScroll = Vector2.zero; edgeKey = null;
                    preview = selected == null ? new List<ResearchProjectDef>() : ResearchWorkspaceData.Predict(selected)
                        .Where(p => p != selected && !p.IsFinished).OrderBy(p => p.label).ToList();
                }
                DrawToolbar(new Rect(rect.x, rect.y, rect.width, 30), host, window);
                rect.yMin += 36;
                bool narrow = rect.width < 650;
                Rect graph, details;
                if (narrow)
                {
                    float panelHeight = Mathf.Min(230, rect.height * 0.43f);
                    graph = new Rect(rect.x, rect.y, rect.width, rect.height - panelHeight - 6);
                    details = new Rect(rect.x, graph.yMax + 6, rect.width, panelHeight);
                }
                else
                {
                    float panelWidth = Mathf.Clamp(rect.width * 0.27f, 230, 300);
                    graph = new Rect(rect.x, rect.y, rect.width - panelWidth - 6, rect.height);
                    details = new Rect(graph.xMax + 6, rect.y, panelWidth, rect.height);
                }
                DrawGraph(graph, host, window, selected);
                DrawDetails(details, host, window, selected);
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; }
        }

        private void Build()
        {
            sections.Clear(); nodes.Clear(); nodeSections.Clear();
            var visible = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => ResearchWorkspaceData.IsHelod(p) && !p.IsHidden).ToList();
            var remaining = new HashSet<ResearchProjectDef>(visible);
            foreach (var def in DefDatabase<HelodResearchSectionDef>.AllDefsListForReading.OrderBy(s => s.order).ThenBy(s => s.defName))
            {
                var projects = (def.projects ?? new List<ResearchProjectDef>()).Where(p => p != null && remaining.Remove(p)).ToList();
                if (projects.Count > 0) sections.Add(new Section { Id = def.defName, Label = def.LabelCap,
                    Color = def.color, Projects = projects });
            }
            if (remaining.Count > 0) sections.Add(new Section { Id = "other", Label = "HD_Research_Other".Translate(),
                Color = new Color(0.65f, 0.5f, 0.7f), Projects = remaining.OrderBy(p => p.defName).ToList() });
            width = Mathf.Max(700, visible.Select(p => Mathf.Round(p.ResearchViewX / 0.86f) * ColumnWidth + NodeWidth + 48)
                .DefaultIfEmpty(700).Max());
            float y = 8;
            Text.Font = zoom < 0.85f ? GameFont.Tiny : GameFont.Small;
            foreach (var section in sections)
            {
                float nodeHeight = Mathf.Max(72, section.Projects.Max(p =>
                    (Text.CalcHeight(p.LabelCap, NodeWidth * zoom - 12) + 38) / zoom));
                var rows = section.Projects.Select(p => p.ResearchViewY).Distinct().OrderBy(v => v).ToList();
                float sectionHeight = collapsed.Contains(section.Id) ? 38 : 48 + rows.Count * (nodeHeight + 16);
                section.Bounds = new Rect(4, y, width, sectionHeight);
                if (!collapsed.Contains(section.Id))
                    foreach (var project in section.Projects)
                    {
                        nodes[project] = new Rect(24 + Mathf.Round(project.ResearchViewX / 0.86f) * ColumnWidth,
                            y + 42 + rows.IndexOf(project.ResearchViewY) * (nodeHeight + 16), NodeWidth, nodeHeight);
                        nodeSections[project] = section;
                    }
                y += sectionHeight + 32;
            }
            height = y; width += 112;
            dirty = false; builtZoom = zoom; language = LanguageDatabase.activeLanguage?.folderName; edgeKey = null;
            Text.Font = GameFont.Small;
        }

        private void DrawToolbar(Rect rect, IResearchWorkspaceHost host, Window window)
        {
            float x = rect.x;
            if (Widgets.ButtonText(new Rect(x, rect.y, 100, 28), "HD_Research_Sections".Translate()))
                Find.WindowStack.Add(new FloatMenu(sections.Select(section => new FloatMenuOption(section.Label, () =>
                {
                    collapsed.Remove(section.Id); dirty = true; Build(); scroll.y = section.Bounds.y * zoom;
                })).ToList()));
            x += 106;
            if (Widgets.ButtonText(new Rect(x, rect.y, 28, 28), "−")) { zoom = Mathf.Max(0.65f, zoom - 0.1f); dirty = true; }
            x += 32;
            if (Widgets.ButtonText(new Rect(x, rect.y, 58, 28), (zoom * 100).ToString("0") + "%")) { zoom = 1; dirty = true; }
            x += 62;
            if (Widgets.ButtonText(new Rect(x, rect.y, 28, 28), "+")) { zoom = Mathf.Min(1.5f, zoom + 0.1f); dirty = true; }
            x += 36;
            if (rect.xMax - x < 140) return;
            float fieldWidth = Mathf.Min(210, rect.xMax - x - 64);
            search = Widgets.TextField(new Rect(x, rect.y, fieldWidth, 28), search);
            if (Widgets.ButtonText(new Rect(x + fieldWidth + 4, rect.y, 60, 28), "HD_Research_Find".Translate()))
            {
                var match = sections.SelectMany(s => s.Projects).FirstOrDefault(Matches);
                if (match != null)
                {
                    var section = sections.First(s => s.Projects.Contains(match));
                    collapsed.Remove(section.Id); dirty = true; Build();
                    host.Select(window, match); Focus(match);
                }
            }
        }

        private bool Matches(ResearchProjectDef project) => search.Length == 0
            || project.LabelCap.ToString().IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
            || project.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        private Rect Scaled(Rect rect) => new Rect(rect.x * zoom, rect.y * zoom, rect.width * zoom, rect.height * zoom);
        private void Focus(ResearchProjectDef project)
        {
            if (project == null) return;
            if (nodes.TryGetValue(project, out Rect node)) scroll = new Vector2(Mathf.Max(0, node.x * zoom - 80), Mathf.Max(0, node.y * zoom - 60));
        }

        private void DrawGraph(Rect rect, IResearchWorkspaceHost host, Window window, ResearchProjectDef selected)
        {
            var evt = Event.current;
            if (rect.Contains(evt.mousePosition) && evt.type == EventType.ScrollWheel && evt.control)
            {
                float old = zoom;
                zoom = Mathf.Clamp(zoom - evt.delta.y * 0.05f, 0.65f, 1.5f);
                scroll = (scroll + evt.mousePosition - rect.position) * (zoom / old) - (evt.mousePosition - rect.position);
                dirty = true; evt.Use();
            }
            if (rect.Contains(evt.mousePosition) && evt.type == EventType.MouseDrag && evt.button == 2)
            { scroll -= evt.delta; evt.Use(); }
            Widgets.BeginScrollView(rect, ref scroll, new Rect(0, 0, width * zoom, height * zoom));
            try
            {
                foreach (var section in sections)
                {
                    Rect bounds = Scaled(section.Bounds);
                    Widgets.DrawBoxSolid(bounds, new Color(section.Color.r * 0.12f, section.Color.g * 0.12f, section.Color.b * 0.12f, 0.95f));
                    Widgets.DrawBoxSolid(new Rect(bounds.x, bounds.y, 4, bounds.height), section.Color * 0.75f);
                }
                DrawEdges(selected, host.Queue.Plan);
                Rect visible = new Rect(scroll.x, scroll.y, rect.width, rect.height).ExpandedBy(30);
                foreach (var pair in nodes)
                {
                    Rect node = Scaled(pair.Value);
                    if (visible.Overlaps(node)) DrawNode(node, pair.Key, selected, host, window);
                }
                // Section names remain at the viewport edge while panning across a wide tree.
                Text.Font = zoom < 0.85f ? GameFont.Tiny : GameFont.Small;
                foreach (var section in sections)
                {
                    Rect bounds = Scaled(section.Bounds);
                    if (!visible.Overlaps(bounds)) continue;
                    float headerWidth = Mathf.Min(360, rect.width - 24);
                    float headerHeight = Mathf.Max(25, 30 * zoom);
                    float x = Mathf.Max(bounds.x + 12 * zoom, scroll.x + 10);
                    float y = Mathf.Clamp(scroll.y + 3, bounds.y + 3, Mathf.Max(bounds.y + 3, bounds.yMax - headerHeight));
                    string label = (collapsed.Contains(section.Id) ? "+ " : "− ") + section.Label + "  "
                        + section.Projects.Count(p => p.IsFinished) + "/" + section.Projects.Count;
                    if (Widgets.ButtonText(new Rect(x, y, headerWidth, headerHeight), label))
                    { if (!collapsed.Add(section.Id)) collapsed.Remove(section.Id); dirty = true; }
                }
                Text.Font = GameFont.Small;
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawNode(Rect rect, ResearchProjectDef project, ResearchProjectDef selected,
            IResearchWorkspaceHost host, Window window)
        {
            Color fill = project.IsFinished ? new Color(0.12f, 0.3f, 0.25f)
                : project.CanStartNow ? new Color(0.19f, 0.28f, 0.34f) : new Color(0.16f, 0.18f, 0.21f);
            if (project == selected) fill = new Color(0.15f, 0.38f, 0.48f);
            if (!Matches(project)) fill *= 0.55f;
            fill.a = 1;
            Widgets.DrawBoxSolid(rect, fill);
            GUI.color = project == selected ? new Color(0.35f, 0.9f, 1) : preview.Contains(project)
                ? new Color(1, 0.75f, 0.3f) : new Color(0.35f, 0.45f, 0.5f);
            Widgets.DrawBox(rect, project == selected || preview.Contains(project) ? 2 : 1); GUI.color = Color.white;
            Text.Font = zoom < 0.85f ? GameFont.Tiny : GameFont.Small;
            Widgets.Label(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, rect.height - 32), project.LabelCap);
            Text.Font = GameFont.Tiny;
            int count = ResearchWorkspaceData.PartsFor(project).Count;
            int links = ResearchWorkspaceData.LinksFor(project).Count;
            string badges = (count > 0 ? "  P:" + count : "") + (links > 0 ? "  OR" : "");
            Widgets.Label(new Rect(rect.x + 6, rect.yMax - 28, rect.width - 36, 22), project.CostApparent.ToString("0") + badges);
            Widgets.DrawBoxSolid(new Rect(rect.x + 1, rect.yMax - 4, (rect.width - 2) * project.ProgressPercent, 3), new Color(0.3f, 0.8f, 0.7f));
            if (!project.IsFinished && Widgets.ButtonText(new Rect(rect.xMax - 27, rect.yMax - 27, 24, 22), "+"))
                host.Queue.Add(project);
            if (Widgets.ButtonInvisible(rect))
            {
                host.Select(window, project); detailScroll = Vector2.zero;
                if (count > 0) page = 1; else if (links > 0) page = 2;
            }
            TooltipHandler.TipRegion(rect, project.LabelCap + "\n" + project.description + "\n"
                + "HD_Research_NodeTip".Translate());
            Text.Font = GameFont.Small;
        }

        private void DrawEdges(ResearchProjectDef selected, IList<ResearchProjectDef> plan)
        {
            string key = (selected?.defName ?? "") + ":" + revision + ":" + string.Join(",", plan.Select(p => p.defName));
            if (key != edgeKey)
            {
                var queued = new HashSet<ResearchProjectDef>(plan);
                var edges = new List<Edge>();
                foreach (var pair in nodes.OrderBy(p => p.Key.defName, StringComparer.Ordinal))
                    foreach (var parent in ResearchQueue.Prerequisites(pair.Key).Distinct())
                    {
                        if (!nodes.TryGetValue(parent, out Rect from)) continue;
                        Rect to = pair.Value;
                        var points = new List<ResearchPoint> { new ResearchPoint(from.xMax, from.y + 25) };
                        var a = nodeSections[parent]; var b = nodeSections[pair.Key];
                        if (a != b)
                        {
                            bool down = a.Bounds.y < b.Bounds.y;
                            float exit = down ? a.Bounds.yMax + 9 : a.Bounds.y - 9;
                            float entry = down ? b.Bounds.y - 9 : b.Bounds.yMax + 9;
                            float channel = width - 90 + edges.Count % 10 * 7;
                            points.Add(new ResearchPoint(from.xMax + 10, from.y + 25));
                            points.Add(new ResearchPoint(from.xMax + 10, exit));
                            points.Add(new ResearchPoint(channel, exit));
                            points.Add(new ResearchPoint(channel, entry));
                            points.Add(new ResearchPoint(to.x - 10, entry));
                            points.Add(new ResearchPoint(to.x - 10, to.y + 25));
                        }
                        points.Add(new ResearchPoint(to.x, to.y + 25));
                        var lines = new List<ResearchLine>();
                        for (int i = 1; i < points.Count; i++) lines.Add(new ResearchLine(points[i - 1], points[i]));
                        edges.Add(new Edge { From = parent, To = pair.Key, Lines = lines,
                            Key = parent.defName + ">" + pair.Key.defName,
                            Priority = parent == selected || pair.Key == selected ? 3 : queued.Contains(pair.Key) ? 2
                                : parent.IsFinished && pair.Key.IsFinished ? 1 : 0 });
                    }
                edges = edges.OrderBy(e => e.Priority).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
                strokes = new List<Stroke>();
                for (int i = 0; i < edges.Count; i++)
                {
                    var above = edges.Skip(i + 1).SelectMany(e => e.Lines).ToList();
                    foreach (var line in edges[i].Lines)
                        foreach (var piece in ResearchLineGeometry.VisiblePieces(line, above, 5 / zoom))
                            strokes.Add(new Stroke { Edge = edges[i], Line = piece });
                }
                edgeKey = key;
            }
            foreach (var stroke in strokes)
            {
                int priority = stroke.Edge.Priority;
                Color color = priority == 3 ? new Color(0.35f, 0.95f, 1)
                    : priority == 2 ? new Color(0.95f, 0.74f, 0.3f)
                    : priority == 1 ? new Color(0.3f, 0.52f, 0.46f) : new Color(0.24f, 0.29f, 0.33f);
                Widgets.DrawLine(new Vector2(stroke.Line.A.X, stroke.Line.A.Y) * zoom,
                    new Vector2(stroke.Line.B.X, stroke.Line.B.Y) * zoom, color, priority == 3 ? 2.5f : 1.2f);
            }
        }

        private void DrawDetails(Rect rect, IResearchWorkspaceHost host, Window window, ResearchProjectDef selected)
        {
            Widgets.DrawMenuSection(rect); rect = rect.ContractedBy(6);
            string[] labels = { "HD_Research_Queue", "HD_Research_Parts", "HD_Research_Links" };
            for (int i = 0; i < labels.Length; i++)
                if (Widgets.ButtonText(new Rect(rect.x + rect.width / 3 * i, rect.y, rect.width / 3 - 2, 28), labels[i].Translate()))
                { page = i; detailScroll = Vector2.zero; }
            rect.yMin += 34;
            if (page == 0) DrawQueue(rect, host, window);
            else if (page == 1) DrawParts(rect, selected);
            else DrawLinks(rect, selected, host, window);
        }

        private void DrawQueue(Rect rect, IResearchWorkspaceHost host, Window window)
        {
            var queue = host.Queue;
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, rect.width, 26),
                (queue.Paused ? "HD_Research_Resume" : "HD_Research_Pause").Translate())) queue.TogglePause();
            rect.yMin += 32;
            var goals = queue.Goals.ToList(); var plan = queue.Plan.ToList();
            float panelWidth = Mathf.Max(1, rect.width - 20);
            var goalHeights = goals.Select((p, i) => Mathf.Max(52, Text.CalcHeight((i + 1) + ". " + p.LabelCap, panelWidth - 78) + 8)).ToList();
            Text.Font = GameFont.Tiny;
            var planHeights = plan.Select(p => Mathf.Max(24, Text.CalcHeight("▶ " + p.LabelCap, panelWidth) + 4)).ToList();
            Text.Font = GameFont.Small;
            float contentHeight = 54 + goalHeights.Sum(h => h + 8) + planHeights.Sum(h => h + 3);
            Widgets.BeginScrollView(rect, ref detailScroll, new Rect(0, 0, Mathf.Max(1, rect.width - 18), contentHeight));
            try
            {
                float y = 0, w = rect.width - 20;
                if (goals.Count == 0) { Widgets.Label(new Rect(0, 0, w, 90), "HD_Research_EmptyQueue".Translate()); return; }
                for (int i = 0; i < goals.Count; i++)
                {
                    var goal = goals[i];
                    if (Widgets.ButtonText(new Rect(0, y, w - 78, goalHeights[i]), (i + 1) + ". " + goal.LabelCap))
                    { host.Select(window, goal); Focus(goal); }
                    if (Widgets.ButtonText(new Rect(w - 76, y, 24, 25), "↑")) queue.Move(i, -1);
                    if (Widgets.ButtonText(new Rect(w - 50, y, 24, 25), "↓")) queue.Move(i, 1);
                    if (Widgets.ButtonText(new Rect(w - 24, y, 24, 25), "×")) queue.Remove(goal);
                    y += goalHeights[i] + 8;
                }
                Widgets.Label(new Rect(0, y, w, 24), "HD_Research_Plan".Translate()); y += 28;
                Text.Font = GameFont.Tiny;
                for (int i = 0; i < plan.Count; i++)
                {
                    var project = plan[i];
                    string mark = Find.ResearchManager.IsCurrentProject(project) ? "▶ " : "  ";
                    GUI.color = project.CanStartNow ? Color.white : Color.gray;
                    if (Widgets.ButtonText(new Rect(0, y, w, planHeights[i]), mark + project.LabelCap))
                    { host.Select(window, project); Focus(project); }
                    y += planHeights[i] + 3;
                }
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawParts(Rect rect, ResearchProjectDef selected)
        {
            if (selected == null) { Widgets.Label(rect, "HD_Research_Select".Translate()); return; }
            var parts = ResearchWorkspaceData.PartsFor(selected);
            if (parts.Count == 0) { Widgets.Label(rect, "HD_Research_NoParts".Translate()); return; }
            var projected = new HashSet<ResearchProjectDef>(preview); projected.Add(selected);
            var rowHeights = parts.Select(part => Mathf.Max(40, Text.CalcHeight(part.LabelCap, Mathf.Max(1, rect.width - 60)) + 8)).ToList();
            Widgets.BeginScrollView(rect, ref detailScroll, new Rect(0, 0, Mathf.Max(1, rect.width - 18), rowHeights.Sum(h => h + 36)));
            try
            {
                float y = 0, w = rect.width - 20;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i]; float headerHeight = rowHeights[i];
                    var requirements = ResearchWorkspaceData.RequirementsFor(part);
                    bool open = requirements.All(p => p.IsFinished);
                    bool next = requirements.All(p => p.IsFinished || projected.Contains(p));
                    Widgets.ThingIcon(new Rect(0, y, 36, 36), part);
                    if (Widgets.ButtonText(new Rect(40, y, w - 40, headerHeight), part.LabelCap)) Find.WindowStack.Add(new Dialog_InfoCard(part));
                    Text.Font = GameFont.Tiny;
                    GUI.color = open ? new Color(0.45f, 0.9f, 0.65f) : next ? new Color(1, 0.8f, 0.4f) : Color.gray;
                    Widgets.Label(new Rect(0, y + headerHeight + 3, w, 28), (open ? "HD_Research_PartOpen" : next ? "HD_Research_PartNext" : "HD_Research_PartBlocked").Translate());
                    TooltipHandler.TipRegion(new Rect(0, y, w, headerHeight + 32), string.Join("\n", requirements.Select(p => (p.IsFinished ? "✓ " : "• ") + p.LabelCap)));
                    GUI.color = Color.white; Text.Font = GameFont.Small; y += headerHeight + 36;
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawLinks(Rect rect, ResearchProjectDef selected, IResearchWorkspaceHost host, Window window)
        {
            if (selected == null) { Widgets.Label(rect, "HD_Research_Select".Translate()); return; }
            var links = ResearchWorkspaceData.LinksFor(selected);
            float w = rect.width - 20;
            string caption = "HD_Research_WillComplete".Translate(preview.Count);
            float captionHeight = Text.CalcHeight(caption, w);
            var rowHeights = preview.Select(p => Mathf.Max(34, Text.CalcHeight(p.LabelCap, w) + 8)).ToList();
            float contentHeight = captionHeight + 8 + links.Sum(link => Text.CalcHeight(LinkText(link), w) + 12) + rowHeights.Sum(h => h + 4);
            Widgets.BeginScrollView(rect, ref detailScroll, new Rect(0, 0, Mathf.Max(1, rect.width - 18), contentHeight));
            try
            {
                float y = 0;
                foreach (var link in links)
                {
                    string text = LinkText(link); float h = Text.CalcHeight(text, w);
                    Widgets.Label(new Rect(0, y, w, h), text); y += h + 12;
                }
                Widgets.Label(new Rect(0, y, w, captionHeight), caption); y += captionHeight + 8;
                for (int i = 0; i < preview.Count; i++)
                {
                    var project = preview[i];
                    if (Widgets.ButtonText(new Rect(0, y, w, rowHeights[i]), project.LabelCap)) { host.Select(window, project); Focus(project); }
                    y += rowHeights[i] + 4;
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private static string LinkText(ResearchLinkDef link) =>
            string.Join(" + ", link.leftProjects.Select(p => (p.IsFinished ? "✓ " : "• ") + p.LabelCap))
            + "\n↔ OR\n" + string.Join(" + ", link.rightProjects.Select(p => (p.IsFinished ? "✓ " : "• ") + p.LabelCap));
    }

    public static class ResearchWorkspaceRevision
    {
        public static int Value { get; private set; }
        public static void Invalidate() { Value++; }
    }
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class ResearchWorkspaceCompletionPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix() => ResearchWorkspaceRevision.Invalidate();
    }
}
