using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.ResearchUI;
using RimWorld;
using Verse;
using GC = UnityEngine.Color;
using GV = UnityEngine.Vector2;
using GR = UnityEngine.Rect;
using NV = System.Numerics.Vector2;
using NR = Waylith.Math.Rect;
using GE = UnityEngine.Event;
using ET = UnityEngine.EventType;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private sealed class ResearchHost : IResearchWorkspaceHost, IResearchQueueBridge
        {
            public string Id => "controlled-current-research-host";
            public Type WindowType => typeof(Window);
            public bool IsActive(Window window) => true;
            public bool OwnsResearchQueue => true;
            internal ResearchProjectDef Selection;
            public ResearchProjectDef Selected(Window window) => Selection;
            public void Select(Window window, ResearchProjectDef project) { Selection = project; Actions.Add("select:" + project.defName); }
            public IResearchQueueBridge Queue => this;
            public bool Paused { get; private set; }
            public IList<ResearchProjectDef> Goals { get; } = new List<ResearchProjectDef>();
            public IList<ResearchProjectDef> Plan { get; } = new List<ResearchProjectDef>();
            internal readonly List<string> Actions = new();
            public void Add(ResearchProjectDef p) { Goals.Add(p); Actions.Add("add:" + p.defName); }
            public void Remove(ResearchProjectDef p) { Goals.Remove(p); Actions.Add("remove:" + p.defName); }
            public void Move(int index, int delta) { Actions.Add("move:" + index + ":" + delta); }
            public void TogglePause() { Paused = !Paused; Actions.Add("pause:" + Paused); }
        }
        private static bool researchActive, clickInvisible;
        private static int controlledHot, buildLayouts, researchDraws, researchInputs, researchActions, zoomCases, canvasLabels, researchBitPairs;
        private static readonly Dictionary<string, int> routeBuilds = new();
        private static readonly List<(int, int, int, int)> researchScrollBits = new();
        private static readonly UnityEngine.GUIStyle controlledFont = new UnityEngine.GUIStyle { fontSize = 18 };
        private static bool ResearchControl(ref int __result) { if (!active || !researchActive) return true; __result = 0x484452; return false; }
        private static bool HotGet(ref int __result) { if (!active || !researchActive) return true; __result = controlledHot; return false; }
        private static bool HotSet(int __0) { if (!active || !researchActive) return true; controlledHot = __0; return false; }
        private static bool EventType(GE __instance, ref ET __result)
        { if (!active || !researchActive) return true; __result = (ET)typeof(GE).GetField("_type", All).GetValue(__instance); return false; }
        private static bool FontStyle(ref UnityEngine.GUIStyle __result)
        { if (!active || !researchActive) return true; __result = controlledFont; return false; }
        private static bool CanvasLabel(GR __0, string __1, UnityEngine.GUIStyle __2)
        { if (!active || !researchActive) return true; Add("canvas-label", __0, __1, __2.fontSize, (float)__2.alignment); canvasLabels++; return false; }
        private static bool SearchField(GR __0, string __1, ref string __result)
        { if (!active || !researchActive) return true; Add("search-field", __0, __1); __result = __1; return false; }
        private static bool ResearchButton(MethodBase __originalMethod, GR __0, object[] __args, ref bool __result)
        {
            if (!active || !researchActive || trace == null) return true;
            BoolWidget(__originalMethod, __0, __args, ref __result);
            if (clickInvisible && !clicked && __originalMethod.Name == "ButtonInvisible") { __result = true; clicked = true; }
            return false;
        }
        private static bool ResearchScroll(GR __0, ref GV __1, GR __2)
        {
            if (!active || !researchActive || trace == null) return true;
            GV before = __1; Scroll(__0, ref __1, __2);
            researchScrollBits.Add((BitConverter.SingleToInt32Bits(before.x), BitConverter.SingleToInt32Bits(before.y), BitConverter.SingleToInt32Bits(__1.x), BitConverter.SingleToInt32Bits(__1.y))); return false;
        }
        // The native view routes on a worker thread; the frozen view routed inline inside Build.
        private static void BuildSettled(object view)
        {
            Call(view, "Build");
            ((System.Threading.Tasks.Task)Get(view, "routing"))?.Wait();
            Call(view, "AdoptRoutes");
        }
        private static void RouteBuild(object __instance)
        {
            // Native routing runs on a worker thread, where the [ThreadStatic] active flag is unset.
            if (!researchActive) return;
            string key = __instance.GetType().FullName;
            lock (routeBuilds) routeBuilds[key] = routeBuilds.GetValueOrDefault(key) + 1;
        }
        private static GV Vector(object value) => value is NV n ? new GV(n.X, n.Y) : (GV)value;
        private static GR Rectangle(object value) => value is NR n ? new GR(n.x, n.y, n.width, n.height) : (GR)value;
        private static void ResearchState(object a, object e, bool layout = false)
        {
            foreach (string f in new[] { "scroll", "detailScroll" }) Bits(Vector(Get(a, f)), Vector(Get(e, f)), "research " + f);
            foreach (string f in new[] { "zoom", "search", "page", "revision", "dirty", "lastSelected", "width", "height", "panControl", "edgeKey", "routeLayoutKey", "language" })
                if (!Equals(Get(a, f), Get(e, f))) throw new InvalidOperationException("Research state changed: " + f);
            if (!((List<ResearchProjectDef>)Get(a, "preview")).SequenceEqual((List<ResearchProjectDef>)Get(e, "preview"))) throw new InvalidOperationException("Research preview changed.");
            if (!((HashSet<string>)Get(a, "collapsed")).SetEquals((HashSet<string>)Get(e, "collapsed"))) throw new InvalidOperationException("Research folding changed.");
            if (!layout) return;
            var an = (IDictionary)Get(a, "nodes"); var en = (IDictionary)Get(e, "nodes");
            if (an.Count != en.Count) throw new InvalidOperationException("Research node count changed.");
            foreach (object key in an.Keys) { if (!en.Contains(key)) throw new InvalidOperationException("Research node key changed."); Rect(Rectangle(an[key]), Rectangle(en[key])); }
            var sa = (IList)Get(a, "sections"); var se = (IList)Get(e, "sections");
            if (sa.Count != se.Count) throw new InvalidOperationException("Research section count changed.");
            for (int i = 0; i < sa.Count; i++)
            {
                if (!Equals(Get(sa[i], "Id"), Get(se[i], "Id")) || !Equals(Get(sa[i], "Label"), Get(se[i], "Label"))) throw new InvalidOperationException("Research section identity changed.");
                Rect(Rectangle(Get(sa[i], "Bounds")), Rectangle(Get(se[i], "Bounds")));
                var c = (Waylith.Math.Color)Get(sa[i], "Color"); var d = (GC)Get(se[i], "Color"); Same(c.r, d.r); Same(c.g, d.g); Same(c.b, d.b); Same(c.a, d.a);
                if (!((List<ResearchProjectDef>)Get(sa[i], "Projects")).SequenceEqual((List<ResearchProjectDef>)Get(se[i], "Projects"))) throw new InvalidOperationException("Research section project order changed.");
            }
            var ar = (IList)Get(a, "routes"); var er = (IList)Get(e, "routes");
            if (ar.Count != er.Count) throw new InvalidOperationException("Research route count changed.");
            for (int i = 0; i < ar.Count; i++)
            {
                foreach (string f in new[] { "Key", "From", "To", "Priority" }) if (!Equals(Get(ar[i], f), Get(er[i], f))) throw new InvalidOperationException("Research route identity/priority changed.");
                var al = (List<ResearchLine>)Get(ar[i], "Lines"); var el = (List<ResearchLine>)Get(er[i], "Lines");
                if (al.Count != el.Count) throw new InvalidOperationException("Research route segments changed.");
                for (int j = 0; j < al.Count; j++) { Same(al[j].A.X, el[j].A.X); Same(al[j].A.Y, el[j].A.Y); Same(al[j].B.X, el[j].B.X); Same(al[j].B.Y, el[j].B.Y); }
            }
            buildLayouts++;
        }
        private static void ResearchFrame(Action a, Action e, ET type = ET.Repaint, GV point = default, GV delta = default, int button = 0, int hot = 0)
        {
            researchScrollBits.Clear(); (ET Type, ET Raw, int Hot) state = default;
            Frame(() => { a(); state = (GE.current.type, GE.current.rawType, controlledHot); }, () => { e(); if (state != (GE.current.type, GE.current.rawType, controlledHot)) throw new InvalidOperationException("Research event consumption/control changed."); },
                () => { GE.current = new GE { type = type, rawType = type, mousePosition = point, delta = delta, button = button }; controlledHot = hot; });
            int half = researchScrollBits.Count / 2;
            if (researchScrollBits.Count % 2 != 0 || !researchScrollBits.Take(half).SequenceEqual(researchScrollBits.Skip(half))) throw new InvalidOperationException("Research scroll by-ref bits changed.");
            researchBitPairs += half;
        }
        private static void SetResearchScroll(object a, object e, string field, GV value)
        { Set(a, field, new NV(value.x, value.y)); Set(e, field, value); }
        private static void ResearchZoomCases(object a, object e)
        {
            foreach (GV scroll in new[] { GV.zero, new GV(-0f, float.Epsilon), new GV(4100.125f, 1999.5f) })
                foreach (GV pivot in new[] { GV.zero, new GV(150.125f, 205.5f), new GV(-50, -100) })
                    foreach (float next in new[] { -1f, .3f, .30001f, .65f, 1f, 1.5f, 3f })
                    {
                        SetResearchScroll(a, e, "scroll", scroll); Call(a, "SetZoom", next, pivot); Call(e, "SetZoom", next, pivot); ResearchState(a, e); zoomCases++;
                    }
        }
        private static void ResearchViews(object a, object e, ResearchHost ah, ResearchHost eh, Window window)
        {
            var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => ResearchWorkspaceData.IsHelod(p) && !p.IsHidden).ToList();
            ResearchProjectDef part = projects.First(p => !p.IsFinished && ResearchWorkspaceData.PartsFor(p).Count > 0), link = projects.First(p => ResearchWorkspaceData.LinksFor(p).Count > 0);
            foreach (ResearchHost host in new[] { ah, eh }) { host.Goals.Add(part); host.Goals.Add(link); foreach (var p in projects.Take(8)) host.Plan.Add(p); }
            foreach (GR rect in new[] { new GR(0, 0, 1100, 800), new GR(1.125f, -.25f, 640, 600.5f), new GR(-10, 8, 1350, 980) })
                foreach (float z in new[] { .3f, .65f, 1f, 1.5f })
                    foreach (ResearchProjectDef selected in new[] { null, part, link, projects.Last() })
                        foreach (int page in new[] { 0, 1, 2 })
                            foreach (GV scroll in new[] { new GV(-0f, float.Epsilon), new GV(780.25f, 410.5f) })
                            {
                                ah.Selection = eh.Selection = selected;
                                foreach (object v in new[] { a, e }) { Set(v, "zoom", z); Set(v, "page", page); Set(v, "edgeKey", null); ((IDictionary)Get(v, "canvasStyles")).Clear(); }
                                SetResearchScroll(a, e, "scroll", scroll); SetResearchScroll(a, e, "detailScroll", new GV(float.Epsilon, -0f));
                                ResearchFrame(() => ((ResearchWorkspaceView)a).Draw(new NR(rect.x,rect.y,rect.width,rect.height), ah, window), () => ((FrozenResearch.ResearchWorkspaceView)e).Draw(rect, eh, window)); ResearchState(a, e); researchDraws++;
                            }
            // Exercise current node actions through the same host contract the real view uses.
            foreach (bool select in new[] { false, true })
            {
                clickButtons = true; clickText = select ? "unmatched controlled text" : ""; clickInvisible = select;
                ResearchFrame(() => Call(a, "DrawNode", new GR(30, 40, 180, 100), part, null, ah, window), () => Call(e, "DrawNode", new GR(30, 40, 180, 100), part, null, eh, window));
                ResearchState(a, e); researchActions++; clickButtons = clickInvisible = false;
            }
            foreach (string label in new[] { "HD_Research_Parts", "HD_Research_Links", "HD_Research_Queue", "HD_Research_Pause" })
            {
                clickButtons = true; clickText = label.Translate();
                ResearchFrame(() => Call(a, "DrawDetails", new GR(0, 0, 280, 750), ah, window, part), () => Call(e, "DrawDetails", new GR(0, 0, 280, 750), eh, window, part));
                ResearchState(a, e); researchActions++; clickButtons = false;
            }
            if (!ah.Actions.SequenceEqual(eh.Actions) || ah.Selection != eh.Selection || ah.Paused != eh.Paused || !ah.Goals.SequenceEqual(eh.Goals)) throw new InvalidOperationException("Research host callback state changed.");
            if (!ah.Actions.Any(v => v.StartsWith("add:")) || !ah.Actions.Any(v => v.StartsWith("select:")) || !ah.Actions.Any(v => v.StartsWith("pause:"))) throw new InvalidOperationException("Research host actions were not exercised.");
        }
        private static void ResearchPointerCases(object a, object e, ResearchHost ah, ResearchHost eh, Window window)
        {
            GR rect = new GR(7.125f, -.25f, 850, 670);
            foreach (float z in new[] { .3f, .65f, 1.5f })
            {
                foreach (object v in new[] { a, e }) Set(v, "zoom", z);
                foreach (GV pointer in new[] { new GV(rect.x, rect.y), new GV(rect.xMax, rect.yMax), new GV(rect.x + 200, rect.y + 100), new GV(-100, -100) })
                    foreach (float wheel in new[] { -20f, -1f, 1f, 20f })
                    {
                        ResearchFrame(() => Call(a, "DrawGraph", rect, ah, window, ah.Selection), () => Call(e, "DrawGraph", rect, eh, window, eh.Selection), ET.ScrollWheel, pointer, new GV(0, wheel)); ResearchState(a, e); researchInputs++;
                    }
                foreach (ET type in new[] { ET.MouseDown, ET.MouseDrag })
                {
                    ResearchFrame(() => Call(a, "DrawGraph", rect, ah, window, ah.Selection), () => Call(e, "DrawGraph", rect, eh, window, eh.Selection), type, new GV(rect.x + 80, rect.yMax - 70)); ResearchState(a, e); researchInputs++;
                }
                foreach (ET type in new[] { ET.MouseDown, ET.MouseDrag, ET.MouseUp })
                {
                    int hot = type == ET.MouseDown ? 0 : 0x484452;
                    ResearchFrame(() => Call(a, "DrawGraph", rect, ah, window, ah.Selection), () => Call(e, "DrawGraph", rect, eh, window, eh.Selection), type, new GV(rect.x + 50, rect.y + 50), new GV(33.125f, -27.5f), 2, hot);
                    ResearchState(a, e); researchInputs++;
                    if (type == ET.MouseUp && ((int)Get(a, "panControl") != 0 || controlledHot != 0)) throw new InvalidOperationException("Research pan did not release control.");
                }
            }
        }
        internal static void RunResearch(string output, Action<bool, string> check)
        {
            var h = new Harmony("Helodrace.Tests.NativeResearchCanvas");
            InstallUi(h, nameof(ResearchScroll), boolPrefix: nameof(ResearchButton));
            h.Patch(AccessTools.Method(typeof(UnityEngine.GUI), "Label", new[] { typeof(GR), typeof(string), typeof(UnityEngine.GUIStyle) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(CanvasLabel)));
            h.Patch(AccessTools.PropertyGetter(typeof(Text), "CurFontStyle"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(FontStyle)));
            h.Patch(AccessTools.Method(typeof(Widgets), "TextField", new[] { typeof(GR), typeof(string) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(SearchField)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.GUIUtility), "GetControlID", new[] { typeof(int), typeof(UnityEngine.FocusType) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(ResearchControl)));
            h.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.GUIUtility), "hotControl"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(HotGet)));
            h.Patch(AccessTools.PropertySetter(typeof(UnityEngine.GUIUtility), "hotControl"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(HotSet)));
            h.Patch(AccessTools.PropertyGetter(typeof(GE), "type"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(EventType)));
            foreach (Type t in new[] { typeof(ResearchWorkspaceView), typeof(FrozenResearch.ResearchWorkspaceView) }) h.Patch(AccessTools.Method(t, "BuildRoutes"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(RouteBuild)));
            GE originalEvent = GE.current; active = researchActive = true; clickButtons = false;
            var a = new ResearchWorkspaceView(); var e = new FrozenResearch.ResearchWorkspaceView(); var ah = new ResearchHost(); var eh = new ResearchHost(); var window = new MainTabWindow_Research();
            try
            {
                ResearchFrame(() => BuildSettled(a), () => Call(e, "Build")); ResearchState(a, e, true);
                ResearchZoomCases(a, e); ResearchViews(a, e, ah, eh, window); ResearchPointerCases(a, e, ah, eh, window);
                foreach (string id in ((IList)Get(a, "sections")).Cast<object>().Take(3).Select(s => (string)Get(s, "Id")).ToArray())
                {
                    foreach (bool collapse in new[] { true, false })
                    {
                        foreach (object v in new object[] { a, e }) { var set = (HashSet<string>)Get(v, "collapsed"); if (collapse) set.Add(id); else set.Remove(id); }
                        ResearchFrame(() => BuildSettled(a), () => Call(e, "Build")); ResearchState(a, e, true);
                    }
                }
                check(researchDraws == 288 && canvasLabels > 0, "native research canvas nodes, sections, minimap, queue, parts and linked-project pages match frozen submissions at narrow/wide layouts and four zoom levels");
                check(zoomCases == 63 && researchInputs == 63, "research cursor-anchored zoom/clamps, minimap selection and pan capture/drag/release match frozen state and event consumption");
                check(buildLayouts == 7 && routeBuilds.Count == 2 && routeBuilds.Values.All(v => v == 1), "native layout bounds and routed line geometry match frozen graph; zoom/selection/section folding reuse one cached route layout per implementation");
                check(researchActions == 6 && researchBitPairs > 0, "research host selection/queue callbacks, detail pages and exact by-ref scroll bits match current game contracts");
            }
            finally { GE.current = originalEvent; active = researchActive = false; trace = null; clickButtons = clickInvisible = false; h.UnpatchAll(h.Id); }
            File.WriteAllText(Path.Combine(output, "native-research-ui.json"), JsonSerializer.Serialize(new { Frames = frames, WidgetSubmissions = widgets, CanvasLabelsBoth = canvasLabels, DrawFrames = researchDraws, ZoomCases = zoomCases, PointerFrames = researchInputs, ActionFrames = researchActions, LayoutComparisons = buildLayouts, RouteBuilds = routeBuilds, ScrollBitPairs = researchBitPairs, Components = components, MaximumFloatError = maximumError, NonzeroDifferences = differences, Baseline = "ed744b82", ControlledTextMetrics = true, SyntheticGameEvents = true, ControlledHostQueue = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
