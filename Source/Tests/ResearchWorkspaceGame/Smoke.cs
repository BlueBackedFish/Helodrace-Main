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
        public static int Draws;
        static Bootstrap()
        {
            if (!GenCommandLine.TryGetCommandLineArg("hdResearchUiSmoke", out string output)) return;
            Application.runInBackground = true;
            var driver = new GameObject("HelodResearchWorkspaceSmoke");
            driver.AddComponent<Smoke>();
            UnityEngine.Object.DontDestroyOnLoad(driver);
            LongEventHandler.ExecuteWhenFinished(() => QuickStarter.CheckQuickStart());
            new Harmony("Helodrace.ResearchWorkspaceSmoke").Patch(AccessTools.Method(typeof(ResearchWorkspaceView), "Draw"),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(Drawn)));
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
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
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
            if (stage >= 3) return;
            if (Bootstrap.Draws - startedDraw < 3) return;
            if (wait++ == 0)
            {
                string name = new[] { "queue", "parts", "or" }[stage];
                ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
                File.AppendAllText(Path.Combine(output, "checks.txt"), name + ": canvas drawn; goals="
                    + ResearchQueue.Current.Goals.Count + "; parts=" + ResearchWorkspaceData.PartsFor(window.CurTab == null ? null
                        : DefDatabase<ResearchProjectDef>.GetNamed("HelodHighAutomaticRifle")).Count + "\n");
            }
            if (wait < 12) return;
            stage++; wait = 0;
            if (stage < 3) Configure();
            else
            {
                File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS: three native canvas panels rendered\n");
                Application.Quit();
            }
        }
        private void Configure()
        {
            var project = DefDatabase<ResearchProjectDef>.GetNamed(stage == 1 ? "HelodHighAutomaticRifle" : "HelodNumericalControl");
            window.Select(project);
            var view = ResearchWorkspaceView.For(window);
            AccessTools.Field(typeof(ResearchWorkspaceView), "page").SetValue(view, stage);
            startedDraw = Bootstrap.Draws;
            if (stage > 0) AccessTools.Method(typeof(ResearchWorkspaceView), "Focus").Invoke(view, new object[] { project });
        }
    }
}
