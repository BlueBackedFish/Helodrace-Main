using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ResearchUI
{
    // Foreign research windows can register a bridge and draw this same canvas in their own viewport.
    // A bridge owns selection and queue actions; the renderer never writes a foreign queue directly.
    public interface IResearchQueueBridge
    {
        bool Paused { get; }
        IList<ResearchProjectDef> Goals { get; }
        IList<ResearchProjectDef> Plan { get; }
        void Add(ResearchProjectDef project);
        void Remove(ResearchProjectDef project);
        void Move(int index, int delta);
        void TogglePause();
    }

    public interface IResearchWorkspaceHost
    {
        string Id { get; }
        Type WindowType { get; }
        bool IsActive(Window window);
        bool OwnsResearchQueue { get; }
        ResearchProjectDef Selected(Window window);
        void Select(Window window, ResearchProjectDef project);
        IResearchQueueBridge Queue { get; }
    }

    public static class ResearchWindowCompatibility
    {
        private static readonly List<IResearchWorkspaceHost> hosts = new List<IResearchWorkspaceHost>();
        internal static readonly VanillaResearchHost Vanilla = new VanillaResearchHost();
        public static void Register(IResearchWorkspaceHost host)
        {
            if (host == null || string.IsNullOrEmpty(host.Id) || host.WindowType == null
                || !typeof(Window).IsAssignableFrom(host.WindowType))
                throw new ArgumentException("A research host needs an ID and window type");
            hosts.RemoveAll(existing => existing.Id == host.Id);
            hosts.Add(host);
        }
        public static bool ExternalQueueOwnsResearch
        {
            get
            {
                Type windowType = MainButtonDefOf.Research.tabWindowClass;
                var host = hosts.FirstOrDefault(candidate => candidate.WindowType.IsAssignableFrom(windowType));
                return host?.OwnsResearchQueue ?? windowType != typeof(MainTabWindow_Research);
            }
        }

        public static bool TryDraw(Window window, Rect viewport)
        {
            var host = hosts.FirstOrDefault(candidate => candidate.IsActive(window));
            if (host == null && Vanilla.IsActive(window)) host = Vanilla;
            if (host == null || host.Queue == null) return false;
            ResearchWorkspaceView.For(window).Draw(viewport, host, window);
            return true;
        }
    }

    internal sealed class NativeQueueBridge : IResearchQueueBridge
    {
        public bool Paused => ResearchQueue.Current?.Paused ?? true;
        public IList<ResearchProjectDef> Goals => ResearchQueue.Current?.Goals ?? new List<ResearchProjectDef>();
        public IList<ResearchProjectDef> Plan => ResearchQueue.Current?.Plan ?? new List<ResearchProjectDef>();
        public void Add(ResearchProjectDef project) => ResearchQueue.Current?.Add(project);
        public void Remove(ResearchProjectDef project) => ResearchQueue.Current?.Remove(project);
        public void Move(int index, int delta) => ResearchQueue.Current?.Move(index, delta);
        public void TogglePause() => ResearchQueue.Current?.TogglePause();
    }

    internal sealed class VanillaResearchHost : IResearchWorkspaceHost
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Research, ResearchProjectDef> selected =
            AccessTools.FieldRefAccess<MainTabWindow_Research, ResearchProjectDef>("selectedProject");
        private readonly IResearchQueueBridge queue = new NativeQueueBridge();
        public string Id => "vanilla";
        public Type WindowType => typeof(MainTabWindow_Research);
        public bool OwnsResearchQueue => false;
        public bool IsActive(Window window) => window?.GetType() == typeof(MainTabWindow_Research)
            && MainButtonDefOf.Research.tabWindowClass == typeof(MainTabWindow_Research);
        public ResearchProjectDef Selected(Window window) => selected((MainTabWindow_Research)window);
        public void Select(Window window, ResearchProjectDef project) => ((MainTabWindow_Research)window).Select(project);
        public IResearchQueueBridge Queue => queue;
    }

    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.PostOpen))]
    internal static class BlancasResearchTabIntegration
    {
        private static readonly System.Reflection.FieldInfo Tabs = AccessTools.Field(typeof(MainTabWindow_Research), "tabs");
        private static readonly System.Reflection.FieldInfo TabDef = AccessTools.Field(
            AccessTools.Inner(typeof(MainTabWindow_Research), "ResearchTabRecord"), "def");

        private static void Postfix(MainTabWindow_Research __instance)
        {
            ResearchTabDef standalone = DefDatabase<ResearchTabDef>.GetNamedSilentFail("BD_HelodTechTab");
            // Keep the published Def and any third-party research still using it.
            // Main only hides the empty navigation entry after moving this branch.
            if (standalone == null || DefDatabase<ResearchProjectDef>.AllDefsListForReading.Any(p => p.tab == standalone)) return;
            var tabs = (System.Collections.IList)Tabs.GetValue(__instance);
            for (int i = tabs.Count - 1; i >= 0; i--)
                if (ReferenceEquals(TabDef.GetValue(tabs[i]), standalone)) tabs.RemoveAt(i);
            if (__instance.CurTab == standalone)
                __instance.CurTab = DefDatabase<ResearchTabDef>.GetNamed("HelodTechTab");
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Research), "DrawRightRect")]
    public static class ResearchWorkspaceWindowPatch
    {
        private static readonly System.Reflection.FieldInfo tabsField = AccessTools.Field(typeof(MainTabWindow_Research), "tabs");
        [HarmonyPrefix]
        public static bool Prefix(MainTabWindow_Research __instance, Rect __0, float __1)
        {
            if (!ResearchWindowCompatibility.Vanilla.IsActive(__instance)
                || __instance.CurTab?.defName != "HelodTechTab") return true;
            var tabs = ((IEnumerable<TabRecord>)tabsField.GetValue(__instance)).ToList();
            Rect header = __0; header.xMax = __1;
            __0.yMin += TabDrawer.GetOverflowTabHeight(header, tabs, 100f, 200f);
            Widgets.DrawMenuSection(__0);
            TabDrawer.DrawTabsOverflow(header, tabs, 100f, 200f);
            // A tab click may have switched away while drawing the shared tab strip.
            if (__instance.CurTab?.defName == "HelodTechTab")
                return !ResearchWindowCompatibility.TryDraw(__instance, __0.ContractedBy(8));
            return false;
        }
    }
}
