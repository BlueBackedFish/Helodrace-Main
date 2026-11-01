using System;
using System.Collections.Generic;
using System.Linq;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    public static class AccessTools
    {
        public delegate F FieldRef<T, F>(T instance);
        public static Type Inner(Type type, string name) => type.GetNestedType(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        public static System.Reflection.FieldInfo Field(Type type, string name) => type.GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        public static FieldRef<T, F> FieldRefAccess<T, F>(string name) => instance => (F)Field(typeof(T), name).GetValue(instance);
    }
}
namespace Verse
{
    public class Def
    {
        public string defName;
        public virtual IEnumerable<string> ConfigErrors() { yield break; }
    }
    public class ResearchProjectDef : Def
    {
        public bool IsFinished;
        public bool ExtraRequirementsMet = true;
        public RimWorld.ResearchTabDef tab;
        public List<ResearchProjectDef> prerequisites = new List<ResearchProjectDef>();
        public List<ResearchProjectDef> hiddenPrerequisites = new List<ResearchProjectDef>();
        public bool CanStartNow => !IsFinished && ExtraRequirementsMet
            && prerequisites.Concat(hiddenPrerequisites).All(p => p.IsFinished);
    }
    public static class DefDatabase<T> where T : Def
    {
        public static List<T> AllDefsListForReading = new List<T>();
        public static T GetNamedSilentFail(string name) => AllDefsListForReading.FirstOrDefault(d => d.defName == name);
        public static T GetNamed(string name) => AllDefsListForReading.First(d => d.defName == name);
    }
    public class Pawn { }
    public class Game
    {
        public object Component;
        public T GetComponent<T>() where T : class => Component as T;
    }
    public static class Current { public static Game Game; }
    public class GameComponent
    {
        public virtual void StartedNewGame() { }
        public virtual void LoadedGame() { }
        public virtual void GameComponentTick() { }
        public virtual void ExposeData() { }
    }
    public class TickManager { public int TicksGame; }
    public static class Find
    {
        public static RimWorld.ResearchManager ResearchManager;
        public static TickManager TickManager = new TickManager();
    }
    public static class Log { public static void Warning(string message) { } }
    public class Window { public virtual void PostOpen() { } }
    public class TabRecord { }
    public static class TabDrawer
    {
        public static float GetOverflowTabHeight<T>(UnityEngine.Rect rect, List<T> tabs, float min, float max) => 0;
        public static TabRecord DrawTabsOverflow<T>(UnityEngine.Rect rect, List<T> tabs, float min, float max) => null;
    }
    public static class Widgets { public static void DrawMenuSection(UnityEngine.Rect rect) { } }
    public enum LookMode { Def }
    public enum LoadSaveMode { Inactive, Saving, LoadingVars, PostLoadInit }
    public static class Scribe
    {
        public static LoadSaveMode mode;
        public static Dictionary<string, object> Data = new Dictionary<string, object>();
    }
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> list, string key, LookMode mode)
        {
            if (Scribe.mode == LoadSaveMode.Saving) Scribe.Data[key] = new List<T>(list);
            if (Scribe.mode == LoadSaveMode.LoadingVars) list = new List<T>((List<T>)Scribe.Data[key]);
        }
    }
    public static class Scribe_Values
    {
        public static void Look(ref bool value, string key)
        {
            if (Scribe.mode == LoadSaveMode.Saving) Scribe.Data[key] = value;
            if (Scribe.mode == LoadSaveMode.LoadingVars) value = (bool)Scribe.Data[key];
        }
    }
    public static class Scribe_Defs
    {
        public static void Look<T>(ref T value, string key) where T : Def
        {
            if (Scribe.mode == LoadSaveMode.Saving) Scribe.Data[key] = value;
            if (Scribe.mode == LoadSaveMode.LoadingVars) value = (T)Scribe.Data[key];
        }
    }
}
namespace RimWorld
{
    public class ResearchTabDef : Verse.Def { }
    public class MainTabWindow_Research : Verse.Window
    {
        private sealed class ResearchTabRecord : Verse.TabRecord { public ResearchTabDef def = null; }
        private Verse.ResearchProjectDef selectedProject;
        private List<Verse.TabRecord> tabs = new List<Verse.TabRecord>();
        public ResearchTabDef CurTab;
        public void Select(Verse.ResearchProjectDef project) { selectedProject = project; CurTab = project.tab; }
    }
    public class MainButtonDef { public Type tabWindowClass = typeof(MainTabWindow_Research); }
    public static class MainButtonDefOf { public static MainButtonDef Research = new MainButtonDef(); }
    public class ResearchManager
    {
        public Verse.ResearchProjectDef Active;
        private int calls;
        public Verse.ResearchProjectDef GetProject(object category) => Active;
        public void SetCurrentProject(Verse.ResearchProjectDef project) { Active = project; }
        public bool IsCurrentProject(Verse.ResearchProjectDef project) => Active == project;
        public void StopProject(Verse.ResearchProjectDef project) { if (Active == project) Active = null; }
        public void FinishProject(Verse.ResearchProjectDef project, bool dialog = false, Verse.Pawn researcher = null, bool letter = true)
        {
            if (++calls > 3000) throw new Exception("Non-convergent completion");
            foreach (var parent in project.prerequisites) if (!parent.IsFinished) FinishProject(parent, dialog, researcher, letter);
            project.IsFinished = true; StopProject(project);
            Helodrace.ResearchManagerFinishProjectLinkedResearchPatch.Postfix(this);
        }
    }
}
namespace Helodrace.ResearchUI
{
    public static class ResearchWorkspaceRevision { public static void Invalidate() { } }
    public sealed class ResearchWorkspaceView
    {
        public static int Calls;
        public static IResearchWorkspaceHost LastHost;
        public static ResearchWorkspaceView For(Verse.Window window) => new ResearchWorkspaceView();
        public void Draw(UnityEngine.Rect rect, IResearchWorkspaceHost host, Verse.Window window) { Calls++; LastHost = host; }
    }
}
namespace UnityEngine
{
    public struct Rect
    {
        public float x, y, width, height;
        public float xMax { get => x + width; set => width = value - x; }
        public float yMin { get => y; set { height += y - value; y = value; } }
        public Rect ContractedBy(float value) => this;
    }
}
