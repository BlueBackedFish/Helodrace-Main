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
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private readonly List<Section> sections = new List<Section>();
        private List<Stroke> strokes = new List<Stroke>();
        private readonly List<Edge> routes = new List<Edge>();
        private readonly Dictionary<GameFont, GUIStyle> canvasStyles = new Dictionary<GameFont, GUIStyle>();
        private List<ResearchProjectDef> preview = new List<ResearchProjectDef>();
        private Vector2 scroll, detailScroll;
        private float zoom = 0.9f;
        private string search = "", language, edgeKey;
        private int page, revision = -1;
        private bool dirty = true;
        private ResearchProjectDef lastSelected;
        private float width, height;
        private int panControl;
        private const float NodeWidth = 180, ColumnWidth = 320, RowGap = 72, StageGap = 200;

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
                if (dirty || language != currentLanguage || revision != ResearchWorkspaceRevision.Value) Build();
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
            sections.Clear(); nodes.Clear(); canvasStyles.Clear();
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
            Text.Font = GameFont.Small;
            var ordered = sections.SelectMany(s => s.Projects).ToList();
            var cells = ResearchFlowLayout.Arrange(ordered, ResearchQueue.Prerequisites);
            float nodeHeight = Mathf.Max(84, ordered.Select(p => Text.CalcHeight(p.LabelCap, NodeWidth - 12) + 38).DefaultIfEmpty(84).Max());
            float pitch = nodeHeight + RowGap;
            int topLane = cells.Values.Select(c => c.Row).DefaultIfEmpty(0).Min();
            var boundaries = sections.Select(s => s.Projects.Min(p => cells[p].Column)).Distinct().OrderBy(c => c).ToList();
            Func<int, float> columnX = column => 84 + column * ColumnWidth + StageGap * boundaries.Count(b => b > 0 && b <= column);
            foreach (var section in sections)
            {
                var frames = section.Projects.ToDictionary(p => p, p => new Rect(columnX(cells[p].Column),
                    92 + (cells[p].Row - topLane) * pitch, NodeWidth, nodeHeight));
                float left = frames.Values.Min(n => n.xMin) - 64, right = frames.Values.Max(n => n.xMax) + 64;
                float top = frames.Values.Min(n => n.yMin) - 60, bottom = frames.Values.Max(n => n.yMax) + 44;
                var bounds = new Rect(left, top, right - left, bottom - top);
                // Extension sections may span the same depth range. Keep their panels separate without reversing time.
                float offset = 0;
                foreach (var earlier in sections.TakeWhile(s => s != section))
                    if (bounds.Overlaps(earlier.Bounds.ExpandedBy(40)))
                    {
                        float shift = earlier.Bounds.yMax + 120 - bounds.yMin;
                        bounds.y += shift; offset += shift;
                    }
                if (collapsed.Contains(section.Id)) bounds.height = 38;
                section.Bounds = bounds;
                if (!collapsed.Contains(section.Id))
                    foreach (var pair in frames)
                    { Rect node = pair.Value; node.y += offset; nodes[pair.Key] = node; }
            }
            width = sections.Select(s => s.Bounds.xMax).DefaultIfEmpty(700).Max() + 24;
            height = sections.Select(s => s.Bounds.yMax).DefaultIfEmpty(500).Max() + 24;
            BuildRoutes();
            dirty = false; language = LanguageDatabase.activeLanguage?.folderName; edgeKey = null;
            Text.Font = GameFont.Small;
        }

        private void DrawToolbar(Rect rect, IResearchWorkspaceHost host, Window window)
        {
            float x = rect.x;
            if (Widgets.ButtonText(new Rect(x, rect.y, 100, 28), "HD_Research_Sections".Translate()))
                Find.WindowStack.Add(new FloatMenu(sections.Select(section => new FloatMenuOption(section.Label, () =>
                {
                    collapsed.Remove(section.Id); dirty = true; Build(); scroll = section.Bounds.position * zoom;
                })).ToList()));
            x += 106;
            if (Widgets.ButtonText(new Rect(x, rect.y, 28, 28), "−")) { SetZoom(zoom - 0.1f, Vector2.zero); }
            x += 32;
            if (Widgets.ButtonText(new Rect(x, rect.y, 58, 28), (zoom * 100).ToString("0") + "%")) { SetZoom(1, Vector2.zero); }
            x += 62;
            if (Widgets.ButtonText(new Rect(x, rect.y, 28, 28), "+")) { SetZoom(zoom + 0.1f, Vector2.zero); }
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

        private void SetZoom(float value, Vector2 pivot)
        {
            float next = ResearchZoom.Clamp(value);
            scroll = new Vector2(ResearchZoom.Anchor(scroll.x, pivot.x, zoom, next),
                ResearchZoom.Anchor(scroll.y, pivot.y, zoom, next));
            zoom = next; edgeKey = null; canvasStyles.Clear();
        }

        private void DrawGraph(Rect rect, IResearchWorkspaceHost host, Window window, ResearchProjectDef selected)
        {
            var evt = Event.current;
            if (rect.Contains(evt.mousePosition) && evt.type == EventType.ScrollWheel)
            {
                SetZoom(zoom - evt.delta.y * 0.05f, evt.mousePosition - rect.position);
                evt.Use();
            }
            Rect map = new Rect(rect.x + 10, rect.yMax - 126, Mathf.Min(190, rect.width - 38), 104);
            Rect mapContent = map.ContractedBy(6); mapContent.yMin += 16;
            Vector2 pointer = evt.mousePosition - rect.position + scroll;
            bool overControl = nodes.Values.Any(n => Scaled(n).Contains(pointer))
                || sections.Any(s => Header(s).Contains(pointer));
            int control = GUIUtility.GetControlID(0x484452, FocusType.Passive);
            if (map.Contains(evt.mousePosition) && evt.button == 0
                && (evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag))
            {
                scroll = new Vector2((evt.mousePosition.x - mapContent.x) / mapContent.width * width * zoom - rect.width / 2,
                    (evt.mousePosition.y - mapContent.y) / mapContent.height * height * zoom - rect.height / 2);
                evt.Use();
            }
            if (evt.type == EventType.MouseDown && rect.ContractedBy(18).Contains(evt.mousePosition)
                && (evt.button == 2 || evt.button == 0 && !overControl))
            { panControl = control; GUIUtility.hotControl = control; evt.Use(); }
            if (panControl == control && GUIUtility.hotControl == control)
            {
                if (evt.type == EventType.MouseDrag) { scroll -= evt.delta; evt.Use(); }
                if (evt.rawType == EventType.MouseUp)
                { panControl = 0; GUIUtility.hotControl = 0; evt.Use(); }
            }
            scroll.x = Mathf.Clamp(scroll.x, 0, Mathf.Max(0, width * zoom - rect.width + 18));
            scroll.y = Mathf.Clamp(scroll.y, 0, Mathf.Max(0, height * zoom - rect.height + 18));
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
                Text.Font = GameFont.Small;
                foreach (var section in sections)
                {
                    Rect header = Header(section);
                    if (!visible.Overlaps(header)) continue;
                    string label = (collapsed.Contains(section.Id) ? "+ " : "− ") + section.Label + "  "
                        + section.Projects.Count(p => p.IsFinished) + "/" + section.Projects.Count;
                    bool clicked = Widgets.ButtonText(header, "");
                    DrawCanvasLabel(header, label, GameFont.Small, TextAnchor.MiddleCenter);
                    if (clicked)
                    { if (!collapsed.Add(section.Id)) collapsed.Remove(section.Id); dirty = true; }
                }
                Text.Font = GameFont.Small;
            }
            finally { Widgets.EndScrollView(); }
            DrawMinimap(map, mapContent, rect, selected);

        }

        private Rect Header(Section section) => new Rect((section.Bounds.x + 12) * zoom,
            (section.Bounds.y + 4) * zoom, Mathf.Min(360, section.Bounds.width - 24) * zoom, 30 * zoom);

        private void DrawMinimap(Rect rect, Rect content, Rect viewport, ResearchProjectDef selected)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.09f, 0.11f, 0.98f));
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 6, rect.y + 2, rect.width - 12, 18), "HD_Research_Map".Translate());
            foreach (var section in sections)
            {
                var bounds = section.Bounds;
                Widgets.DrawBoxSolid(new Rect(content.x + bounds.x / width * content.width,
                    content.y + bounds.y / height * content.height, bounds.width / width * content.width,
                    bounds.height / height * content.height), section.Color * 0.7f);
            }
            if (selected != null && nodes.TryGetValue(selected, out Rect node))
                Widgets.DrawBoxSolid(new Rect(content.x + node.center.x / width * content.width - 2,
                    content.y + node.center.y / height * content.height - 2, 4, 4), Color.white);
            var view = new Rect(content.x + scroll.x / zoom / width * content.width,
                content.y + scroll.y / zoom / height * content.height,
                Mathf.Min(1, viewport.width / zoom / width) * content.width,
                Mathf.Min(1, viewport.height / zoom / height) * content.height);
            GUI.color = new Color(0.4f, 0.95f, 1); Widgets.DrawBox(view, 1); GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, "HD_Research_PanTip".Translate());
            Text.Font = GameFont.Small;
        }

        private void DrawCanvasLabel(Rect rect, string text, GameFont font, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            if (!canvasStyles.TryGetValue(font, out var style))
            {
                Text.Font = font;
                style = new GUIStyle(Text.CurFontStyle);
                int baseSize = style.fontSize > 0 ? style.fontSize : font == GameFont.Tiny ? 12 : 18;
                style.fontSize = Mathf.Max(4, Mathf.RoundToInt(baseSize * zoom));
                canvasStyles[font] = style;
            }
            style.alignment = anchor;
            GUI.Label(rect, text, style);
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
            int count = ResearchWorkspaceData.PartsFor(project).Count;
            int links = ResearchWorkspaceData.LinksFor(project).Count;
            string badges = (count > 0 ? "  P:" + count : "") + (links > 0 ? "  OR" : "");
            DrawCanvasLabel(new Rect(rect.x + 6 * zoom, rect.y + 4 * zoom, rect.width - 12 * zoom, rect.height - 32 * zoom),
                project.LabelCap, GameFont.Small);
            DrawCanvasLabel(new Rect(rect.x + 6 * zoom, rect.yMax - 28 * zoom, rect.width - 36 * zoom, 22 * zoom),
                project.CostApparent.ToString("0") + badges, GameFont.Tiny);
            Widgets.DrawBoxSolid(new Rect(rect.x + 1, rect.yMax - 4, (rect.width - 2) * project.ProgressPercent, 3), new Color(0.3f, 0.8f, 0.7f));
            if (!project.IsFinished)
            {
                Rect add = new Rect(rect.xMax - 27 * zoom, rect.yMax - 27 * zoom, 24 * zoom, 22 * zoom);
                if (Widgets.ButtonText(add, "")) host.Queue.Add(project);
                DrawCanvasLabel(add, "+", GameFont.Small, TextAnchor.MiddleCenter);
            }
            if (Widgets.ButtonInvisible(rect))
            {
                host.Select(window, project); detailScroll = Vector2.zero;
                if (count > 0) page = 1; else if (links > 0) page = 2;
            }
            TooltipHandler.TipRegion(rect, project.LabelCap + "\n" + project.description + "\n"
                + "HD_Research_NodeTip".Translate());
            Text.Font = GameFont.Small;
        }

        private void BuildRoutes()
        {
            routes.Clear();
            var boxes = nodes.Values.Select(n => new ResearchBox(n.xMin - 8, n.yMin - 8, n.xMax + 8, n.yMax + 8))
                .Concat(sections.Select(s => new ResearchBox(s.Bounds.x, s.Bounds.y, s.Bounds.xMax, s.Bounds.y + 38))).ToList();
            var ports = nodes.Values.SelectMany(n => new[] { new ResearchPoint(n.xMax + 8, n.center.y),
                new ResearchPoint(n.xMin - 8, n.center.y) }).ToList();
            var router = new ResearchOrthogonalRouter(boxes, ports);
            foreach (var pair in nodes.OrderBy(p => p.Key.defName, StringComparer.Ordinal))
                foreach (var parent in ResearchQueue.Prerequisites(pair.Key).Distinct())
                {
                    if (!nodes.TryGetValue(parent, out Rect from)) continue;
                    Rect to = pair.Value;
                    bool right = to.center.x >= from.center.x;
                    float fromX = right ? from.xMax : from.xMin, toX = right ? to.xMin : to.xMax;
                    float direction = right ? 1 : -1;
                    var start = new ResearchPoint(fromX + direction * 8, from.center.y);
                    var end = new ResearchPoint(toX - direction * 8, to.center.y);
                    var lines = router.Route(start, end);
                    if (lines.Count == 0) continue;
                    lines.Insert(0, new ResearchLine(new ResearchPoint(fromX + direction, from.center.y), start));
                    lines.Add(new ResearchLine(end, new ResearchPoint(toX - direction * 2, to.center.y)));
                    routes.Add(new Edge { From = parent, To = pair.Key, Lines = lines,
                        Key = parent.defName + ">" + pair.Key.defName });
                }
        }

        private void DrawEdges(ResearchProjectDef selected, IList<ResearchProjectDef> plan)
        {
            string key = (selected?.defName ?? "") + ":" + revision + ":" + string.Join(",", plan.Select(p => p.defName));
            if (key != edgeKey)
            {
                var queued = new HashSet<ResearchProjectDef>(plan);
                foreach (var edge in routes)
                    edge.Priority = edge.From == selected || edge.To == selected ? 3 : queued.Contains(edge.To) ? 2
                        : edge.From.IsFinished && edge.To.IsFinished ? 1 : 0;
                var edges = routes.OrderBy(e => e.Priority).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
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
                DrawOrthogonal(stroke.Line, color, priority == 3 ? 2 : 1);
            }
            foreach (var edge in routes.Where(e => e.Priority >= 2))
            {
                var end = edge.Lines[edge.Lines.Count - 1];
                float direction = end.B.X >= end.A.X ? 1 : -1;
                Vector2 tip = new Vector2(end.B.X * zoom, end.B.Y * zoom);
                Color color = edge.Priority == 3 ? new Color(0.35f, 0.95f, 1) : new Color(0.95f, 0.74f, 0.3f);
                for (int i = 0; i < 3; i++) Widgets.DrawBoxSolid(new Rect(Mathf.Round(tip.x) - direction * (1 + i),
                    Mathf.Round(tip.y) - i, 1, i * 2 + 1), color);
            }
        }

        private void DrawOrthogonal(ResearchLine line, Color color, float thickness)
        {
            // Solid rectangles avoid IMGUI rotated-line texture overshoot at fractional zoom.
            float ax = Mathf.Round(line.A.X * zoom), ay = Mathf.Round(line.A.Y * zoom);
            float bx = Mathf.Round(line.B.X * zoom), by = Mathf.Round(line.B.Y * zoom);
            if (ax == bx && ay == by) return;
            if (Mathf.Abs(ax - bx) < 0.01f)
                Widgets.DrawBoxSolid(new Rect(ax - thickness / 2, Mathf.Min(ay, by), thickness, Mathf.Abs(by - ay)), color);
            else
                Widgets.DrawBoxSolid(new Rect(Mathf.Min(ax, bx), ay - thickness / 2, Mathf.Abs(bx - ax), thickness), color);
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

        private float CardHeight(ResearchProjectDef project, float width) => Mathf.Max(60, Text.CalcHeight(project.LabelCap, width - 14) + 28);
        private float GroupHeight(IList<ResearchProjectDef> projects, float width) =>
            projects.Sum(p => CardHeight(p, width - 12) + 6) + (projects.Count > 1 ? 22 : 6) + 6;

        private void DrawResearchCard(Rect rect, ResearchProjectDef project, ResearchProjectDef selected,
            IResearchWorkspaceHost host, Window window)
        {
            Color accent = project == selected ? new Color(0.35f, 0.9f, 1)
                : project.IsFinished ? new Color(0.3f, 0.8f, 0.6f)
                : preview.Contains(project) ? new Color(1, 0.75f, 0.3f) : new Color(0.4f, 0.5f, 0.6f);
            Widgets.DrawBoxSolid(rect, project == selected ? new Color(0.13f, 0.3f, 0.38f) : new Color(0.13f, 0.16f, 0.2f));
            GUI.color = accent; Widgets.DrawBox(rect, project == selected ? 2 : 1); GUI.color = Color.white;
            Widgets.DrawBoxSolid(new Rect(rect.x + 5, rect.y + 7, 5, 5), accent);
            Widgets.Label(new Rect(rect.x + 14, rect.y + 2, rect.width - 18, 19),
                (ResearchWorkspaceData.IsHelod(project) ? "HD_Research_Helod" : "HD_Research_Vanilla").Translate());
            Widgets.Label(new Rect(rect.x + 7, rect.y + 22, rect.width - 14, rect.height - 24), project.LabelCap);
            Widgets.DrawBoxSolid(new Rect(rect.x + 1, rect.yMax - 3, (rect.width - 2) * project.ProgressPercent, 2), accent);
            if (Widgets.ButtonInvisible(rect)) { host.Select(window, project); Focus(project); }
            TooltipHandler.TipRegion(rect, project.LabelCap + "\n" + project.description + "\n"
                + (project.IsFinished ? "HD_Research_Completed" : preview.Contains(project) ? "HD_Research_LinkNext" : "HD_Research_Select").Translate());
        }

        private void DrawLinkGroup(Rect rect, IList<ResearchProjectDef> projects, ResearchProjectDef selected,
            IResearchWorkspaceHost host, Window window)
        {
            Color color = projects.All(p => p.IsFinished) ? new Color(0.2f, 0.5f, 0.4f) : new Color(0.25f, 0.3f, 0.35f);
            GUI.color = color; Widgets.DrawBox(rect); GUI.color = Color.white;
            float y = rect.y + 6;
            if (projects.Count > 1)
            { Widgets.Label(new Rect(rect.x + 6, y, rect.width - 12, 18), "AND"); y += 22; }
            foreach (var project in projects)
            {
                float h = CardHeight(project, rect.width - 12);
                DrawResearchCard(new Rect(rect.x + 6, y, rect.width - 12, h), project, selected, host, window); y += h + 6;
            }
        }

        private void DrawLinks(Rect rect, ResearchProjectDef selected, IResearchWorkspaceHost host, Window window)
        {
            if (selected == null) { Widgets.Label(rect, "HD_Research_Select".Translate()); return; }
            var links = ResearchWorkspaceData.LinksFor(selected);
            float w = rect.width - 20, groupWidth = (w - 32) / 2;
            Text.Font = GameFont.Tiny;
            string caption = "HD_Research_WillComplete".Translate(preview.Count);
            float captionHeight = Text.CalcHeight(caption, w);
            var heights = links.Select(link => Mathf.Max(GroupHeight(link.leftProjects, groupWidth), GroupHeight(link.rightProjects, groupWidth))).ToList();
            float contentHeight = captionHeight + 8 + heights.Sum(h => h + 18) + preview.Sum(p => CardHeight(p, w) + 6);
            Widgets.BeginScrollView(rect, ref detailScroll, new Rect(0, 0, Mathf.Max(1, rect.width - 18), contentHeight));
            try
            {
                float y = 0;
                for (int i = 0; i < links.Count; i++)
                {
                    var link = links[i]; float h = heights[i];
                    float leftHeight = GroupHeight(link.leftProjects, groupWidth), rightHeight = GroupHeight(link.rightProjects, groupWidth);
                    Color lineColor = new Color(0.4f, 0.8f, 0.85f);
                    Widgets.DrawBoxSolid(new Rect(groupWidth, y + h / 2 - 1, 32, 2), lineColor);
                    Rect badge = new Rect(groupWidth + 2, y + h / 2 - 14, 28, 28);
                    Widgets.DrawBoxSolid(badge, new Color(0.12f, 0.25f, 0.3f));
                    Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(badge, "↔\nOR"); Text.Anchor = TextAnchor.UpperLeft;
                    DrawLinkGroup(new Rect(0, y + (h - leftHeight) / 2, groupWidth, leftHeight), link.leftProjects, selected, host, window);
                    DrawLinkGroup(new Rect(groupWidth + 32, y + (h - rightHeight) / 2, groupWidth, rightHeight), link.rightProjects, selected, host, window);
                    y += h + 18;
                }
                Widgets.Label(new Rect(0, y, w, captionHeight), caption); y += captionHeight + 8;
                foreach (var project in preview)
                {
                    float h = CardHeight(project, w);
                    DrawResearchCard(new Rect(0, y, w, h), project, selected, host, window); y += h + 6;
                }
            }
            finally { Widgets.EndScrollView(); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; }
        }

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
