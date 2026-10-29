using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Helodrace.ResearchUI;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ResearchWorkspaceSmoke
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        public static int Draws, RouteBuilds;
        public static bool LeftDrag, MapClick, CardClick, WheelZoom, MinimumZoom;
        public sealed class PointerState
        {
            public EventType Type; public int Button; public Vector2 Pointer, Scroll;
            public float Zoom; public bool Control;
        }
        private static readonly FieldInfo Scroll = AccessTools.Field(typeof(ResearchWorkspaceView), "scroll");
        static Bootstrap()
        {
            if (!GenCommandLine.TryGetCommandLineArg("hdResearchUiSmoke", out string output)) return;
            Application.runInBackground = true;
            var driver = new GameObject("HelodResearchWorkspaceSmoke");
            driver.AddComponent<Smoke>();
            UnityEngine.Object.DontDestroyOnLoad(driver);
            LongEventHandler.ExecuteWhenFinished(() => QuickStarter.CheckQuickStart());
            var harmony = new Harmony("Helodrace.ResearchWorkspaceSmoke");
            harmony.Patch(AccessTools.Method(typeof(ResearchWorkspaceView), "Draw"),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(Drawn)));
            harmony.Patch(AccessTools.Method(typeof(ResearchWorkspaceView), "DrawGraph"),
                prefix: new HarmonyMethod(typeof(Bootstrap), nameof(ObserveInput)),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(ObservedInput)));
            harmony.Patch(AccessTools.Method(typeof(ResearchWorkspaceView), "DrawResearchCard"),
                prefix: new HarmonyMethod(typeof(Bootstrap), nameof(BeforeCard)),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(AfterCard)));
            harmony.Patch(AccessTools.Method(typeof(ResearchWorkspaceView), "BuildRoutes"),
                prefix: new HarmonyMethod(typeof(Bootstrap), nameof(BeforeRoutes)),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(AfterRoutes)));
        }
        public static void BeforeRoutes(out System.Diagnostics.Stopwatch __state) => __state = System.Diagnostics.Stopwatch.StartNew();
        public static void AfterRoutes(System.Diagnostics.Stopwatch __state)
        {
            RouteBuilds++; __state.Stop();
            if (GenCommandLine.TryGetCommandLineArg("hdResearchUiSmoke", out string output))
                File.AppendAllText(Path.Combine(output, "routing-times.txt"), "build " + RouteBuilds + ": " + __state.ElapsedMilliseconds + " ms\n");
        }
        public static void ObserveInput(ResearchWorkspaceView __instance, out PointerState __state)
        {
            __state = new PointerState { Type = Event.current.type, Button = Event.current.button,
                Pointer = Event.current.mousePosition, Scroll = (Vector2)Scroll.GetValue(__instance), Control = Event.current.control,
                Zoom = (float)AccessTools.Field(typeof(ResearchWorkspaceView), "zoom").GetValue(__instance) };
        }
        public static void ObservedInput(ResearchWorkspaceView __instance, Rect __0, PointerState __state)
        {
            bool moved = Vector2.Distance(__state.Scroll, (Vector2)Scroll.GetValue(__instance)) > 0.1f;
            if (moved && __state.Button == 0 && __state.Type == EventType.MouseDrag) LeftDrag = true;
            Rect map = new Rect(__0.x + 10, __0.yMax - 126, Mathf.Min(190, __0.width - 38), 104);
            if (moved && __state.Button == 0 && __state.Type == EventType.MouseDown && map.Contains(__state.Pointer)) MapClick = true;
            float zoom = (float)AccessTools.Field(typeof(ResearchWorkspaceView), "zoom").GetValue(__instance);
            if (__state.Type == EventType.ScrollWheel && !__state.Control && Math.Abs(zoom - __state.Zoom) > 0.001f) WheelZoom = true;
            if (__state.Type == EventType.ScrollWheel && !__state.Control && Math.Abs(zoom - 0.3f) < 0.001f) MinimumZoom = true;
        }
        public static void BeforeCard(IResearchWorkspaceHost __3, Window __4, out ResearchProjectDef __state)
        { __state = __3.Selected(__4); }
        public static void AfterCard(ResearchProjectDef __1, IResearchWorkspaceHost __3, Window __4, ResearchProjectDef __state)
        {
            if (__state != __1 && !ResearchWorkspaceData.IsHelod(__1) && __3.Selected(__4) == __1) CardClick = true;
        }
        public static void Drawn() { if (Event.current.type == EventType.Repaint) Draws++; }
    }
    public sealed class Smoke : MonoBehaviour
    {
        private int stage = -1, startedDraw, wait;
        private string output;
        private MainTabWindow_Research window;
        private float heartbeat;
        private float guiHeartbeat;
        private bool panCaptured;
        private int selectionStep, selectionDraw;
        private Vector2 selectionScroll;
        public void Update()
        {
            if (!GenCommandLine.TryGetCommandLineArg("hdResearchUiSmoke", out output) || Time.realtimeSinceStartup < heartbeat) return;
            heartbeat = Time.realtimeSinceStartup + 10;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "heartbeat.txt"), Current.ProgramState + "; map=" + (Find.CurrentMap != null)
                + "; stage=" + stage + "; draws=" + Bootstrap.Draws);
            // Hidden test windows may not repaint the entry menu that normally starts -quicktest.
            if (Current.ProgramState == ProgramState.Entry && !LongEventHandler.AnyEventNowOrWaiting)
                QuickStarter.CheckQuickStart();
        }
        public void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !GenCommandLine.TryGetCommandLineArg("hdResearchUiSmoke", out output)) return;
            Directory.CreateDirectory(output);
            if (Time.realtimeSinceStartup >= guiHeartbeat)
            {
                guiHeartbeat = Time.realtimeSinceStartup + 10;
                File.WriteAllText(Path.Combine(output, "gui-heartbeat.txt"), Current.ProgramState + "; waiting=" + LongEventHandler.AnyEventNowOrWaiting);
            }
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            if (stage < 0)
            {
                Directory.CreateDirectory(output);
                // Existing mod asset/config warnings should remain in the log, not obscure UI captures.
                AccessTools.Field(typeof(LudeonTK.EditWindow_Log), "canAutoOpen").SetValue(null, false);
                AccessTools.Field(typeof(LudeonTK.EditWindow_Log), "wantsToOpen").SetValue(null, false);
                foreach (var open in Find.WindowStack.Windows.ToArray())
                    if (open.GetType().Name == "EditWindow_Log") open.Close(false);
                ((Dictionary<ResearchProjectDef, float>)AccessTools.Field(typeof(ResearchManager), "progress").GetValue(Find.ResearchManager)).Clear();
                Find.TickManager.Pause();
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research, false);
                window = (MainTabWindow_Research)MainButtonDefOf.Research.TabWindow;
                ResearchQueue.Current.Add(DefDatabase<ResearchProjectDef>.GetNamed("HelodNumericalControl"));
                ResearchQueue.Current.Add(DefDatabase<ResearchProjectDef>.GetNamed("HelodHighAutomaticRifle"));
                stage = 0; Configure();
            }
            if (stage >= 6) return;
            if (Bootstrap.Draws - startedDraw < 3) return;
            if (wait++ == 0)
            {
                string name = new[] { "queue", "parts", "or", "flow-30-percent", "pan", "synthetic-chemistry" }[stage];
                ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
                File.AppendAllText(Path.Combine(output, "checks.txt"), name + ": canvas drawn; goals="
                    + ResearchQueue.Current.Goals.Count + "; parts=" + ResearchWorkspaceData.PartsFor(window.CurTab == null ? null
                        : DefDatabase<ResearchProjectDef>.GetNamed("HelodHighAutomaticRifle")).Count + "\n");
            }
            if (stage == 4 && !CheckSelection()) return;
            if (stage == 4 && GenCommandLine.TryGetCommandLineArg("hdResearchUiInteractive", out _))
            {
                File.WriteAllText(Path.Combine(output, "interaction.txt"), "leftDrag=" + Bootstrap.LeftDrag + "; mapClick=" + Bootstrap.MapClick
                    + "; cardClick=" + Bootstrap.CardClick + "; wheelZoom=" + Bootstrap.WheelZoom + "; minimumZoom=" + Bootstrap.MinimumZoom + "; tab=" + window.CurTab?.defName);
                if (Bootstrap.LeftDrag && Bootstrap.MapClick && !panCaptured)
                { ScreenCapture.CaptureScreenshot(Path.Combine(output, "pan.png")); panCaptured = true; }
                if (!Bootstrap.LeftDrag || !Bootstrap.MapClick || !Bootstrap.CardClick || !Bootstrap.WheelZoom || !Bootstrap.MinimumZoom) return;
                File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS: native left-drag, overview click, plain-wheel zoom to 30% and vanilla OR card navigation\n");
            }
            if (wait < 12) return;
            stage++; wait = 0;
            if (stage < 6) Configure();
            else
            {
                File.AppendAllText(Path.Combine(output, "checks.txt"), ValidateFlow() + "\nPASS: six native canvas panels rendered\n");
                Application.Quit();
            }
        }
        private bool CheckSelection()
        {
            if (selectionStep == 3) return true;
            var view = ResearchWorkspaceView.For(window);
            var field = AccessTools.Field(typeof(ResearchWorkspaceView), "scroll");
            if (selectionStep == 0)
            {
                selectionScroll = (Vector2)field.GetValue(view);
                window.Select(DefDatabase<ResearchProjectDef>.GetNamed("HelodFrontierSpirit"));
                selectionDraw = Bootstrap.Draws; selectionStep = 1; return false;
            }
            if (Bootstrap.Draws - selectionDraw < 2) return false;
            bool stable = Vector2.Distance(selectionScroll, (Vector2)field.GetValue(view)) < 0.1f;
            File.AppendAllText(Path.Combine(output, "checks.txt"), (stable ? "PASS" : "FAIL") + ": research selection preserves viewport, step " + selectionStep + "\n");
            if (selectionStep == 1)
            {
                window.Select(DefDatabase<ResearchProjectDef>.GetNamed("HelodNumericalControl"));
                selectionDraw = Bootstrap.Draws; selectionStep = 2; return false;
            }
            selectionStep = 3; return true;
        }
        private string ValidateFlow()
        {
            var view = ResearchWorkspaceView.For(window);
            var nodes = (Dictionary<ResearchProjectDef, Rect>)AccessTools.Field(typeof(ResearchWorkspaceView), "nodes").GetValue(view);
            var routes = (System.Collections.IList)AccessTools.Field(typeof(ResearchWorkspaceView), "routes").GetValue(view);
            int expected = nodes.Keys.Sum(p => ResearchQueue.Prerequisites(p).Distinct().Count(nodes.ContainsKey));
            if (routes.Count != expected) return "FAIL: missing routes " + routes.Count + "/" + expected;
            foreach (var pair in nodes)
            {
                foreach (var parent in ResearchQueue.Prerequisites(pair.Key).Where(nodes.ContainsKey))
                    if (nodes[parent].xMax >= pair.Value.xMin) return "FAIL: a dependency does not advance right";
                foreach (var other in nodes.Where(n => n.Key != pair.Key))
                    if (pair.Value.Overlaps(other.Value)) return "FAIL: overlapping node bodies";
            }
            foreach (var pair in nodes)
                foreach (var other in nodes)
                {
                    if (Math.Abs(pair.Key.ResearchViewX - other.Key.ResearchViewX) < 0.001f
                        && Math.Abs(pair.Value.x - other.Value.x) > 0.1f) return "FAIL: authored milestone column lost";
                    if (Math.Abs(pair.Key.ResearchViewY - other.Key.ResearchViewY) < 0.001f
                        && Math.Abs(pair.Value.y - other.Value.y) > 0.1f) return "FAIL: authored branch lane lost";
                }
            var localSections = (System.Collections.IList)AccessTools.Field(typeof(ResearchWorkspaceView), "sections").GetValue(view);
            if (localSections.Count != 30) return "FAIL: missing local sections";
            if (Bootstrap.RouteBuilds != 1) return "FAIL: zoom or selection rebuilt stable route geometry";
            var collapsed = (HashSet<string>)AccessTools.Field(typeof(ResearchWorkspaceView), "collapsed").GetValue(view);
            collapsed.Add("HD_ResearchSection_SyntheticChemistry");
            AccessTools.Method(typeof(ResearchWorkspaceView), "Build").Invoke(view, null);
            if (nodes.Count != 58 || Bootstrap.RouteBuilds != 1) return "FAIL: folding did not hide its six nodes or rerouted the graph";
            collapsed.Remove("HD_ResearchSection_SyntheticChemistry");
            AccessTools.Method(typeof(ResearchWorkspaceView), "Build").Invoke(view, null);
            if (nodes.Count != 64 || Bootstrap.RouteBuilds != 1) return "FAIL: unfolding did not restore its nodes from cached geometry";
            File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS: zoom, selection and section folding reuse one route layout\n");
            for (int i = 0; i < localSections.Count; i++)
            {
                object section = localSections[i]; var type = section.GetType();
                var projects = (List<ResearchProjectDef>)AccessTools.Field(type, "Projects").GetValue(section);
                if (projects.Count < 1 || projects.Count > 6) return "FAIL: section is not a small research group";
                Rect bounds = (Rect)AccessTools.Field(type, "Bounds").GetValue(section);
                for (int j = i + 1; j < localSections.Count; j++)
                    if (bounds.Overlaps((Rect)AccessTools.Field(type, "Bounds").GetValue(localSections[j])))
                        return "FAIL: overlapping local section panels";
                foreach (var other in nodes.Where(n => !projects.Contains(n.Key)))
                    if (bounds.Overlaps(other.Value)) return "FAIL: local section encloses unrelated research";
            }
            foreach (var route in routes)
                foreach (ResearchLine line in (List<ResearchLine>)AccessTools.Field(route.GetType(), "Lines").GetValue(route))
                    if (nodes.Values.Any(n => new ResearchBox(n.xMin, n.yMin, n.xMax, n.yMax).Blocks(line.A, line.B)))
                        return "FAIL: route crosses a node body";
            for (int i = 0; i < routes.Count; i++)
                for (int j = i + 1; j < routes.Count; j++)
                {
                    object a = routes[i], b = routes[j]; var type = a.GetType();
                    if (ResearchRouteGeometry.SameFamily(AccessTools.Field(type, "From").GetValue(a), AccessTools.Field(type, "To").GetValue(a),
                        AccessTools.Field(type, "From").GetValue(b), AccessTools.Field(type, "To").GetValue(b))) continue;
                    if (ResearchRouteGeometry.Conflicts((List<ResearchLine>)AccessTools.Field(type, "Lines").GetValue(a),
                        (List<ResearchLine>)AccessTools.Field(type, "Lines").GetValue(b)))
                        return "FAIL: unrelated routes share a bend or trunk: " + AccessTools.Field(type, "Key").GetValue(a) + " / " + AccessTools.Field(type, "Key").GetValue(b);
                }
            return "PASS: " + nodes.Count + " nodes; " + routes.Count + " complete routes preserve authored lanes and 30 local sections, advance right, avoid nodes and separate unrelated bends/trunks";
        }
        private void Configure()
        {
            var project = DefDatabase<ResearchProjectDef>.GetNamed(stage == 1 ? "HelodHighAutomaticRifle" : stage == 5 ? "HelodTotalSynthesisMedicine" : "HelodNumericalControl");
            window.Select(project);
            var view = ResearchWorkspaceView.For(window);
            AccessTools.Field(typeof(ResearchWorkspaceView), "page").SetValue(view, Mathf.Min(2, stage));
            if (stage >= 3)
            {
                AccessTools.Method(typeof(ResearchWorkspaceView), "SetZoom").Invoke(view,
                    new object[] { stage == 3 ? 0.3f : stage == 5 ? 0.55f : 0.65f, Vector2.zero });
                AccessTools.Field(typeof(ResearchWorkspaceView), "scroll").SetValue(view, new Vector2(0, 0));
            }
            startedDraw = Bootstrap.Draws;
            if (stage > 0 && stage != 3) AccessTools.Method(typeof(ResearchWorkspaceView), "Focus").Invoke(view, new object[] { project });
        }
    }
}
