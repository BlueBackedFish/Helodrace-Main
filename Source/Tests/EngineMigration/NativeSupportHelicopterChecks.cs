using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using NV = System.Numerics.Vector2;
using GV = UnityEngine.Vector2;
using G3 = UnityEngine.Vector3;
using GR = UnityEngine.Rect;
using GM = UnityEngine.Matrix4x4;
using Mat = UnityEngine.Material;

namespace Helodrace.Tests
{
    internal static class NativeSupportHelicopterChecks
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        [ThreadStatic] private static List<(GM Matrix, Mat Material)> meshes;
        [ThreadStatic] private static List<(string Kind, GR Rect, string Text)> widgets;
        [ThreadStatic] private static GV returnedScroll;
        [ThreadStatic] private static GV expectedInputScroll;
        [ThreadStatic] private static int inputMode;
        private static int components, visualCases, meshCalls, dialogCases, widgetCalls, tips;
        private static float maximumError;
        private static object Get(object v, string name) => v.GetType().GetField(name, All).GetValue(v);
        private static void Set(object v, string name, object data) => v.GetType().GetField(name, All).SetValue(v, data);
        private static object Call(object v, string name, params object[] args) => v.GetType().GetMethod(name, All).Invoke(v, args);
        private static void Same(float a, float e)
        { components++; maximumError = Math.Max(maximumError, Math.Abs(a - e)); if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1f, Math.Abs(e))) throw new InvalidOperationException("Support helicopter component changed: " + a + " != " + e); }
        private static void Rect(GR a, GR e) { Same(a.x, e.x); Same(a.y, e.y); Same(a.width, e.width); Same(a.height, e.height); }
        private static bool Mesh(GM __1, Mat __2) { if (meshes == null) return true; meshes.Add((__1, __2)); return false; }
        private static bool Label(GR __0, object[] __args) { if (widgets == null) return true; widgets.Add(("label", __0, __args[1]?.ToString())); return false; }
        private static bool Scroll(GR __0, ref GV __1, GR __2)
        {
            if (widgets == null) return true;
            if (BitConverter.SingleToInt32Bits(__1.x) != BitConverter.SingleToInt32Bits(expectedInputScroll.x) || BitConverter.SingleToInt32Bits(__1.y) != BitConverter.SingleToInt32Bits(expectedInputScroll.y)) throw new InvalidOperationException("Medevac game scroll input bits changed.");
            widgets.Add(("scroll", __0, null)); widgets.Add(("view", __2, null)); widgets.Add(("scroll-input", new GR(__1.x, __1.y, 0, 0), null)); __1 = returnedScroll; return false;
        }
        private static bool EndScroll() => widgets == null;
        private static bool Checkbox(GR __0, string __1, ref bool __2)
        { if (widgets == null) return true; widgets.Add(("checkbox", __0, __1)); if (inputMode == 1) __2 = true; else if (inputMode == 2) __2 = false; return false; }
        private static bool Button(GR __0, string __1, ref bool __result)
        { if (widgets == null) return true; widgets.Add(("button", __0, __1)); __result = inputMode == 3; return false; }
        private static bool Tip(GR __0) { if (widgets == null) return true; widgets.Add(("tip", __0, null)); return false; }
        private static bool Close(Window __instance) => widgets == null || !(__instance is Dialog_HelodMedevac || __instance is Frozen.Dialog_HelodMedevac);

        private static void Visuals()
        {
            var a = (HelodSupportHelicopter)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_SupportHelicopter"));
            Type phaseType = a.GetType().GetField("phase", All).FieldType; int originalTick = Find.TickManager.TicksGame;
            try
            {
                foreach (int phase in Enumerable.Range(0, 7))
                    foreach (bool mist in new[] { false, true })
                        foreach (int age in new[] { -1, 0, 1, 30, 59, 60, 61, 179, 180, 181, 239, 240, 241 })
                            foreach (G3 origin in new[] { G3.zero, new G3(.125f, -.25f, 10.5f), new G3(250, 4, -100) })
                                foreach (int gameTick in Enumerable.Range(0, 8))
                                {
                                    Find.TickManager.DebugSetTicksGame(gameTick); Set(a, "phase", Enum.ToObject(phaseType, phase)); Set(a, "phaseTicks", age); Set(a, "mistDispatch", mist);
                                    var e = new Frozen.SupportHelicopterVisual(phase, age, mist);
                                    meshes = new List<(GM, Mat)>(); Call(a, "DrawAt", origin, false); var actual = meshes.ToArray(); meshes.Clear(); e.DrawAt(origin);
                                    if (actual.Length != 5 || meshes.Count != 5) throw new InvalidOperationException("Support helicopter layer count changed.");
                                    for (int i = 0; i < 5; i++)
                                    {
                                        for (int c = 0; c < 16; c++) Same(actual[i].Matrix[c], meshes[i].Matrix[c]);
                                        if (actual[i].Material != meshes[i].Material) throw new InvalidOperationException("Support helicopter layer/rotor material changed.");
                                    }
                                    visualCases++; meshCalls += 5; meshes = null;
                                }
            }
            finally { Find.TickManager.DebugSetTicksGame(originalTick); meshes = null; a.Destroy(); }
        }
        private static void Frame(Dialog_HelodMedevac a, Frozen.Dialog_HelodMedevac e, GR rect, GV start, GV result, int mode)
        {
            Set(a, "scroll", new NV(start.x, start.y)); Set(e, "scroll", start); expectedInputScroll = start; returnedScroll = result; inputMode = mode;
            widgets = new List<(string, GR, string)>(); a.DoWindowContents(rect); var actual = widgets.ToArray(); widgets.Clear(); e.DoWindowContents(rect);
            if (actual.Length != widgets.Count) throw new InvalidOperationException("Medevac widget count changed.");
            for (int i = 0; i < actual.Length; i++)
            { if (actual[i].Kind != widgets[i].Kind || actual[i].Text != widgets[i].Text) throw new InvalidOperationException("Medevac widget order/labels changed."); Rect(actual[i].Rect, widgets[i].Rect); }
            var scroll = (NV)Get(a, "scroll"); var old = (GV)Get(e, "scroll");
            if (BitConverter.SingleToInt32Bits(scroll.X) != BitConverter.SingleToInt32Bits(result.x) || BitConverter.SingleToInt32Bits(scroll.Y) != BitConverter.SingleToInt32Bits(result.y)
                || BitConverter.SingleToInt32Bits(old.x) != BitConverter.SingleToInt32Bits(result.x) || BitConverter.SingleToInt32Bits(old.y) != BitConverter.SingleToInt32Bits(result.y)) throw new InvalidOperationException("Medevac native scroll failed exact by-ref transfer.");
            if (!((List<Pawn>)Get(a, "selected")).SequenceEqual((List<Pawn>)Get(e, "selected"))) throw new InvalidOperationException("Medevac selection changed.");
            tips += actual.Count(w => w.Kind == "tip"); widgetCalls += actual.Length; widgets = null; dialogCases++;
        }
        private static void Dialog(Map map)
        {
            var added = new List<Pawn>();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, fixedBiologicalAge: 25, fixedChronologicalAge: 25, canGeneratePawnRelations: false));
                    pawn.health.RemoveAllHediffs(); GenSpawn.Spawn(pawn, map.Center + new IntVec3(i, 0, 0), map); added.Add(pawn);
                }
                Helodrace.Tactical.TcccUtility.ApplyTimed(added[0], "HD_TCCC_Mist", 60000).report = "Native medevac geometry fixture";
                List<Pawn> accepted = null, oldAccepted = null;
                var a = new Dialog_HelodMedevac(map, p => accepted = p); var e = new Frozen.Dialog_HelodMedevac(map, p => oldAccepted = p);
                Same(a.InitialSize.x, e.InitialSize.x); Same(a.InitialSize.y, e.InitialSize.y);
                foreach (float width in new[] { 320f, 520f, 1024f, 1536.5f })
                    foreach (float height in new[] { 240f, 550f, 768.25f })
                        foreach (GV scroll in new[] { GV.zero, new GV(-0f, float.Epsilon), new GV(.125f, MathF.BitIncrement(34f)), new GV(100f, 256.25f) })
                        {
                            var rect = new GR(7.5f, -.125f, width, height);
                            Frame(a, e, rect, scroll, new GV(scroll.x, scroll.y + .125f), 1);
                            if (((List<Pawn>)Get(a, "selected")).Count != 3) throw new InvalidOperationException("Medevac three-patient limit changed.");
                            Frame(a, e, rect, scroll, scroll, 3);
                            if (accepted?.Count != 3 || oldAccepted?.Count != 3 || !accepted.SequenceEqual(oldAccepted)) throw new InvalidOperationException("Medevac accept callback/order changed.");
                            Frame(a, e, rect, scroll, scroll, 2);
                            if (((List<Pawn>)Get(a, "selected")).Count != 0) throw new InvalidOperationException("Medevac deselection changed.");
                        }
                ((List<Pawn>)Get(a, "selected")).Add(added[4]); ((List<Pawn>)Get(e, "selected")).Add(added[4]); added[4].DeSpawn();
                Frame(a, e, new GR(0, 0, 520, 550), GV.zero, GV.zero, 0);
                if (((List<Pawn>)Get(a, "selected")).Count != 0) throw new InvalidOperationException("Medevac unavailable patient remained selected.");
            }
            finally { widgets = null; foreach (Pawn pawn in added) if (!pawn.Destroyed) pawn.Destroy(); }
        }
        internal static void Run(string output, Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.NativeSupportHelicopter");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(Mat), typeof(int) }), prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(Mesh)));
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m => m.Name == "Label" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(GR))) harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(Label)));
            harmony.Patch(AccessTools.Method(typeof(Widgets), "BeginScrollView"), prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(Scroll)));
            harmony.Patch(AccessTools.Method(typeof(Widgets), "EndScrollView"), prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(EndScroll)));
            harmony.Patch(AccessTools.Method(typeof(Widgets), "CheckboxLabeled"), prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(Checkbox)));
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m => m.Name == "ButtonText")) harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(Button)));
            harmony.Patch(AccessTools.Method(typeof(TooltipHandler), "TipRegion", new[] { typeof(GR), typeof(TipSignal) }), prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(Tip)));
            harmony.Patch(AccessTools.Method(typeof(Window), "Close"), prefix: new HarmonyMethod(typeof(NativeSupportHelicopterChecks), nameof(Close)));
            try
            {
                Visuals(); Dialog(Find.CurrentMap);
                check(visualCases == 4368 && meshCalls == 21840, "seven support-helicopter phases, MIST timing, body/door offsets and all four rotor frames preserve five-layer draw matrices/materials");
                check(dialogCases == 145 && widgetCalls > 0 && tips > 0, "native medevac layout and widget order/labels/MIST tooltips match frozen geometry across sizes and scroll states");
                check(typeof(Dialog_HelodMedevac).GetField("scroll", All).FieldType == typeof(NV), "owned scroll is native and transfers exact by-ref game values without changing selection/accept/removal logic");
            }
            finally { meshes = null; widgets = null; harmony.UnpatchAll(harmony.Id); }
            File.WriteAllText(Path.Combine(output, "native-support-helicopter.json"), JsonSerializer.Serialize(new { VisualCases = visualCases, MeshSubmissions = meshCalls, DialogFrames = dialogCases, WidgetSubmissions = widgetCalls, TooltipSubmissions = tips, Components = components, MaximumFloatError = maximumError, Baseline = "9066b886" }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
