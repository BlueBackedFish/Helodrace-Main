using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using GC = UnityEngine.Color;
using GV = UnityEngine.Vector2;
using GR = UnityEngine.Rect;
using NV = System.Numerics.Vector2;
using NR = Waylith.Math.Rect;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private sealed record Submission(string Kind, GR Rect, string Text, float[] Values);
        [ThreadStatic] private static List<Submission> trace;
        [ThreadStatic] private static bool active;
        private static GC color = GC.white;
        private static GameFont font = GameFont.Small;
        private static UnityEngine.TextAnchor anchor;
        private static double realtime;
        private static float credit = 100000;
        private static string failure;
        private static Faction provider;
        private static CompTelegraphTable terminal;
        private static int checkboxMode, numericInput = 2;
        private static bool clickButtons;
        private static string clickText;
        private static bool clicked;
        private static string popupSelection;
        private static int popupFrames;
        private static int components, frames, widgets, scrollCalls, helperCases, animations, drafts, editors, casFrames, supportFrames, ammoFrames;
        private static float maximumError;
        private static readonly Dictionary<string, float> differences = new Dictionary<string, float>();
        private static object Get(object v, string name) => v.GetType().GetField(name, All).GetValue(v);
        private static void Set(object v, string name, object data) => v.GetType().GetField(name, All).SetValue(v, data);
        private static void EnumField(object v, string name, string value) => Set(v, name, Enum.Parse(v.GetType().GetField(name, All).FieldType, value));
        private static object[] Args(MethodInfo method, object[] args)
        {
            var result = (object[])args.Clone(); var parameters = method.GetParameters();
            for (int i = 0; i < result.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type == typeof(NR) && result[i] is GR r) result[i] = new NR(r.x, r.y, r.width, r.height);
                if (type == typeof(NV) && result[i] is GV v) result[i] = new NV(v.x, v.y);
                if (type.IsEnum && result[i] is string s) result[i] = Enum.Parse(type, s);
            }
            return result;
        }
        private static object Call(object v, string name, params object[] args)
        { var method = v.GetType().GetMethod(name, All); return method.Invoke(v, Args(method, args)); }
        private static object Static(Type type, string name, params object[] args)
        { var method = type.GetMethod(name, All); return method.Invoke(null, Args(method, args)); }
        private static void Same(float a, float e, string scope = "state/helper")
        {
            components++; float error = Math.Abs(a - e); maximumError = Math.Max(maximumError, error);
            if (error > 0) differences[scope] = Math.Max(differences.GetValueOrDefault(scope), error);
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1, Math.Abs(e))) throw new InvalidOperationException("Telegraph layout component changed: " + a + " != " + e);
        }
        private static void Rect(GR a, GR e) { Same(a.x, e.x, "rect"); Same(a.y, e.y, "rect"); Same(a.width, e.width, "rect"); Same(a.height, e.height, "rect"); }
        private static float[] Style() => economyActive
            ? new[] { color.r, color.g, color.b, color.a, (float)font, (float)anchor, guiEnabled ? 1f : 0f }
            : new[] { color.r, color.g, color.b, color.a, (float)font, (float)anchor };
        private static void Add(string kind, GR rect, string text = null, params float[] values)
            => trace?.Add(new Submission(kind, rect, text, Style().Concat(values).ToArray()));
        private static string TextArg(object[] args) => string.Join("|", args.Skip(1).Where(v => v is string || v is TaggedString).Select(v => v.ToString()));
        private static bool VoidWidget(MethodBase __originalMethod, GR __0, object[] __args)
        {
            if (!active || trace == null) return true;
            var values = __args.Skip(1).SelectMany(v => v switch { float f => new[] { f }, int n => new[] { (float)n }, bool b => new[] { b ? 1f : 0f }, GC c => new[] { c.r, c.g, c.b, c.a }, _ => Array.Empty<float>() }).ToArray();
            Add(__originalMethod.Name, __0, TextArg(__args), values); return false;
        }
        private static bool BoolWidget(MethodBase __originalMethod, GR __0, object[] __args, ref bool __result)
        {
            if (!active || trace == null) return true;
            var values = __args.Skip(1).SelectMany(v => v switch { float f => new[] { f }, int n => new[] { (float)n }, bool b => new[] { b ? 1f : 0f }, GC c => new[] { c.r, c.g, c.b, c.a }, _ => Array.Empty<float>() }).ToArray();
            Add(__originalMethod.Name, __0, TextArg(__args), values);
            __result = clickButtons && !clicked && __originalMethod.Name == "ButtonText" && TextArg(__args) == clickText;
            if (__result) clicked = true;
            return false;
        }
        private static bool Scroll(GR __0, ref GV __1, GR __2)
        {
            if (!active || trace == null) return true;
            Add("scroll", __0, null, __1.x, __1.y); Add("view", __2);
            // Native and frozen controllers receive the same UI-owned result.
            __1 = new GV(MathF.BitIncrement(__1.x), __1.y + .125f); scrollCalls++; return false;
        }
        private static bool EndScroll() => !active || trace == null;
        private static bool Checkbox(GR __0, string __1, ref bool __2)
        { if (!active || trace == null) return true; Add("checkbox", __0, __1, __2 ? 1 : 0); if (checkboxMode == 1) __2 = true; else if (checkboxMode == 2) __2 = false; return false; }
        private static bool Numeric(GR __0, ref int __1, ref string __2)
        { if (!active || trace == null) return true; Add("numeric", __0, __2, __1); __1 = numericInput; __2 = numericInput.ToString(); return false; }
        private static bool ColorSet(GC __0) { if (!active) return true; color = __0; return false; }
        private static bool ColorGet(ref GC __result) { if (!active) return true; __result = color; return false; }
        private static bool FontSet(GameFont __0) { if (!active) return true; font = __0; return false; }
        private static bool FontGet(ref GameFont __result) { if (!active) return true; __result = font; return false; }
        private static bool AnchorSet(UnityEngine.TextAnchor __0) { if (!active) return true; anchor = __0; return false; }
        private static bool AnchorGet(ref UnityEngine.TextAnchor __result) { if (!active) return true; __result = anchor; return false; }
        private static bool Height(string __0, float __1, ref float __result)
        {
            if (!active) return true;
            Add("measure-height", new GR(0, 0, __1, 0), __0);
            __result = MathF.Max(1, MathF.Ceiling((__0?.Length ?? 0) * 8f / MathF.Max(1, __1))) * (font == GameFont.Medium ? 30 : 22); return false;
        }
        private static bool Time(ref double __result) { if (!active) return true; __result = realtime; return false; }
        private static bool Credit(ref float __result) { if (!active) return true; __result = credit; return false; }
        private static bool Provider(CompTelegraphTable __instance, ref Faction __result) { if (!active || __instance != terminal) return true; __result = provider; return false; }
        private static bool Connection(CompTelegraphTable __instance, ref string __result) { if (!active || __instance != terminal) return true; __result = failure; return false; }
        private static bool Texture(GR __0) { if (!active || trace == null) return true; Add("texture", __0); return false; }
        private static bool Tip(GR __0) { if (!active || trace == null) return true; Add("tip", __0); return false; }
        private static bool Line(float __0, float __1, float __2) { if (!active || trace == null) return true; Add("line-horizontal", new GR(__0, __1, __2, 0)); return false; }
        private static bool Close() => !active;
        private static bool WindowAdd(Window __0)
        {
            if (!active) return true;
            Add("window-add", __0.windowRect, __0.GetType().Name);
            if (__0 is FloatMenu)
            {
                var options = (List<FloatMenuOption>)Get(__0, "options");
                foreach (var option in options) Add("menu-option", default, option.Label, option.Disabled ? 1 : 0);
                if (popupSelection != null)
                {
                    var option = options.Single(o => o.Label == popupSelection);
                    if (option.Disabled || option.action == null) throw new InvalidOperationException("Controlled contract popup selection was unavailable.");
                    option.action();
                }
            }
            return false;
        }
        private static void Frame(Action native, Action frozen, Action prepare = null)
        {
            prepare?.Invoke(); color = GC.white; font = GameFont.Small; anchor = UnityEngine.TextAnchor.UpperLeft; clicked = false;
            trace = new List<Submission>(); native(); var actual = trace.ToArray();
            prepare?.Invoke(); color = GC.white; font = GameFont.Small; anchor = UnityEngine.TextAnchor.UpperLeft; clicked = false;
            trace.Clear(); frozen();
            if (actual.Length != trace.Count) throw new InvalidOperationException("Telegraph widget count changed: " + actual.Length + " != " + trace.Count);
            for (int i = 0; i < actual.Length; i++)
            {
                var a = actual[i]; var e = trace[i];
                if (a.Kind != e.Kind || a.Text != e.Text || a.Values.Length != e.Values.Length) throw new InvalidOperationException("Telegraph submission order/text changed: " + a.Kind + ":" + a.Text + " != " + e.Kind + ":" + e.Text);
                Rect(a.Rect, e.Rect); for (int j = 0; j < a.Values.Length; j++) Same(a.Values[j], e.Values[j], a.Kind + ":value" + j);
            }
            widgets += actual.Length; frames++; trace = null;
        }
        private static void State(object a, object e)
        {
            foreach (var field in a.GetType().GetFields(All | BindingFlags.DeclaredOnly).Where(f => !f.IsStatic))
            {
                object av = field.GetValue(a), ev = Get(e, field.Name);
                if (av is NV v && ev is GV g) { Same(v.X, g.x); Same(v.Y, g.y); }
                else if (field.FieldType.IsEnum) { if (av.ToString() != ev.ToString()) throw new InvalidOperationException("Telegraph enum changed: " + field.Name); }
                else if (av is bool || av is int || av is double || av is float) { if (!Equals(av, ev)) throw new InvalidOperationException("Telegraph state changed: " + field.Name); }
                else if (av is IDictionary ad && ev is IDictionary ed)
                {
                    if (ad.Count != ed.Count) throw new InvalidOperationException("Telegraph unit dictionary count changed.");
                    foreach (object key in ad.Keys) if (!Equals(ad[key], ed[key])) throw new InvalidOperationException("Telegraph unit count/buffer changed.");
                }
            }
        }
        private static void Helpers()
        {
            Assembly product = typeof(Dialog_TelegraphTable).Assembly; Type ui = product.GetType("Helodrace.UiMath", true), spatial = product.GetType("Helodrace.SpatialMath", true);
            foreach (GR r in new[] { new GR(0, 0, 100, 50), new GR(.125f, -.25f, -20, 0), new GR(1000.125f, -2000.5f, .001f, 3000) })
                foreach (float margin in new[] { -100f, -0f, .125f, 2f, 22f, 100f })
                {
                    var n = (NR)Static(ui, "Inset", r, margin); Rect(new GR(n.x, n.y, n.width, n.height), r.ContractedBy(margin)); helperCases++;
                }
            foreach (GV from in new[] { GV.zero, new GV(-0f, float.Epsilon), new GV(1000.125f, -1104), new GV(-820, 2160) })
                foreach (GV to in new[] { GV.zero, new GV(.125f, -.25f), new GV(1920, 1080) })
                    foreach (float t in new[] { -.1f, 0f, .0001f, .125f, .5f, .99999f, 1f, 2f })
                    {
                        var a = (NV)Static(spatial, "Interpolate2D", new NV(from.x, from.y), new NV(to.x, to.y), t); var e = GV.Lerp(from, to, t); Same(a.X, e.x); Same(a.Y, e.y); helperCases++;
                    }
        }
        private static object Nested(object owner, string name, params object[] args) => Activator.CreateInstance(owner.GetType().GetNestedType(name, All), All, null, args, null);
        private static void Telegraph(Thing table, Pawn pawn)
        {
            var a = new Dialog_TelegraphTable(table, pawn); var e = new Frozen.Dialog_TelegraphTable(table, pawn);
            foreach (GR rect in new[] { new GR(0, 0, 800, 1020), new GR(7.5f, -.25f, 960.25f, 760.5f), new GR(0, 0, 420, 540) })
            {
                failure = "Controlled offline layout"; Frame(() => a.DoWindowContents(rect), () => e.DoWindowContents(rect)); State(a, e); failure = null;
                Frame(() => a.DoWindowContents(rect), () => e.DoWindowContents(rect)); State(a, e);
                foreach (TelegraphCompany company in Enum.GetValues<TelegraphCompany>())
                {
                    a.GetType().GetProperty("SelectedCompany", All).SetValue(a, company); e.GetType().GetProperty("SelectedCompany", All).SetValue(e, company);
                    Frame(() => Call(a, "DrawCompanyPage", rect, terminal), () => Call(e, "DrawCompanyPage", rect, terminal)); State(a, e);
                }
                foreach (string kind in new[] { "PB", "FB", "COP", "FOB" })
                    foreach (string cost in new[] { "FFP", "CostReimbursement" })
                        foreach (string duration in new[] { "Days30", "Days60", "Days120", "Days240" })
                            foreach (float available in new[] { 0f, 100000f })
                            {
                                credit = available;
                                foreach (object v in new object[] { a, e }) { EnumField(v, "selectedForwardBaseKind", kind); EnumField(v, "selectedContractCostKind", cost); EnumField(v, "selectedContractDuration", duration); Call(v, "ApplySelectedServiceFlags", Enumerable.Repeat(true, 13).ToArray()); }
                                Frame(() => Call(a, "DrawA4StandaloneForwardBaseContract", rect), () => Call(e, "DrawA4StandaloneForwardBaseContract", rect)); State(a, e); drafts++;
                            }
                credit = 100000;
                var ae = (Window)Nested(a, "ForwardBaseServiceEditor", a); var ee = (Window)Nested(e, "ForwardBaseServiceEditor", e);
                foreach (int mode in new[] { 1, 0, 2 })
                    foreach (int count in new[] { -1, 1, 7, 1000000 })
                    {
                        checkboxMode = mode; numericInput = count;
                        foreach (object v in new object[] { a, e }) { EnumField(v, "selectedForwardBaseKind", "FOB"); EnumField(v, "selectedContractCostKind", "FFP"); Call(v, "ApplySelectedServiceFlags", Enumerable.Repeat(true, 13).ToArray()); }
                        Frame(() => ae.DoWindowContents(rect), () => ee.DoWindowContents(rect)); State(a, e); State(ae, ee); editors++;
                    }
                checkboxMode = 0;
                foreach (var option in new[] {
                    (Field: "selectedForwardBaseKind", Initial: "PB", Target: "FOB", Label: "ForwardBaseKindLabel", Open: "OpenBaseKindEditor"),
                    (Field: "selectedContractCostKind", Initial: "FFP", Target: "CostReimbursement", Label: "ContractCostKindLabel", Open: "OpenCostKindEditor"),
                    (Field: "selectedContractDuration", Initial: "Days30", Target: "Days240", Label: "ContractDurationLabel", Open: "OpenDurationEditor") })
                {
                    foreach (object v in new object[] { a, e }) EnumField(v, option.Field, option.Initial);
                    popupSelection = (string)Static(a.GetType(), option.Label, option.Target);
                    object[] args = option.Open == "OpenDurationEditor" ? Array.Empty<object>() : new object[] { 100000f };
                    Frame(() => Call(a, option.Open, args), () => Call(e, option.Open, args)); State(a, e);
                    if (Get(a, option.Field).ToString() != option.Target) throw new InvalidOperationException("Current contract popup callback was not exercised.");
                    popupSelection = null; popupFrames++;
                }
                foreach (int value in ((IDictionary)Get(a, "ffpServiceUnitCounts")).Values) if (value < 1 || value > 999999) throw new InvalidOperationException("Service unit input escaped its production clamp.");
                var viewerA = (Window)Nested(a, "ForwardBaseContractViewer", null, "A controlled contract record\nwith multiple lines.");
                var viewerE = (Window)Nested(e, "ForwardBaseContractViewer", null, "A controlled contract record\nwith multiple lines.");
                Frame(() => viewerA.DoWindowContents(rect), () => viewerE.DoWindowContents(rect));
            }
            foreach (double time in new[] { 100d, 100.001d, 100.125d, 100.3d, 100.5999d, 100.6d, 101d })
            {
                realtime = 100; Call(a, "StartContractEntranceAnimation"); Call(e, "StartContractEntranceAnimation"); realtime = time;
                Call(a, "UpdateContractEntranceAnimation"); Call(e, "UpdateContractEntranceAnimation"); Rect(a.windowRect, e.windowRect); State(a, e); animations++;
            }
            realtime = 100; failure = null; credit = 100000;
        }
        private static void Support(Map map, Pawn pawn)
        {
            var baseObject = (HelodForwardBase)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("HD_ForwardBase")); baseObject.SetFaction(provider); baseObject.Tile = map.Tile;
            baseObject.SetContractServices(Enum.GetValues<HelodForwardBaseService>(), Enumerable.Repeat(100, Enum.GetValues<HelodForwardBaseService>().Length));
            var support = map.GetComponent<MapComponent_HelodCasSupport>(); var original = Get(support, "playtimeStates");
            try
            {
                foreach (GR rect in new[] { new GR(0, 0, 720, 650), new GR(1.125f, -.25f, 960, 820), new GR(0, 0, 420, 540) })
                {
                    var sa = new Dialog_ForwardBaseSupport(provider); var se = new Frozen.Dialog_ForwardBaseSupport(provider);
                    Frame(() => sa.DoWindowContents(rect), () => se.DoWindowContents(rect)); supportFrames++;
                    foreach (HelodCasAircraftKind aircraft in Enum.GetValues<HelodCasAircraftKind>())
                    {
                        var a = new Dialog_HelodCasControl(map, baseObject, pawn, null); var e = new Frozen.Dialog_HelodCasControl(map, baseObject, pawn, null);
                        Set(a, "selectedAircraft", aircraft); Set(e, "selectedAircraft", aircraft);
                        foreach (bool requested in new[] { false, true })
                        {
                            Action reset = () => { Set(support, "playtimeStates", new List<HelodCasPlaytimeState>()); if (requested) ((List<HelodCasPlaytimeState>)Get(support, "playtimeStates")).Add(new HelodCasPlaytimeState(baseObject, aircraft)); if (requested) ((List<HelodCasPlaytimeState>)Get(support, "playtimeStates"))[0].RequestFlight(Find.TickManager.TicksGame); };
                            Frame(() => a.DoWindowContents(rect), () => e.DoWindowContents(rect), reset); casFrames++;
                        }
                        foreach (float fraction in new[] { -.1f, 0f, .125f, .5f, 1f, 2f }) Frame(() => Static(a.GetType(), "DrawPlaytimeBar", rect, fraction, "controlled"), () => Static(e.GetType(), "DrawPlaytimeBar", rect, fraction, "controlled"));
                    }
                    foreach (HelodForwardBaseService service in new[] { HelodForwardBaseService.InfantryMortarSupport, HelodForwardBaseService.Artillery105mmSupport, HelodForwardBaseService.Artillery155mmSupport })
                    {
                        var a = new Dialog_MortarAmmoSelection(map, baseObject, null, pawn, service); var e = new Frozen.Dialog_MortarAmmoSelection(map, baseObject, null, pawn, service);
                        Frame(() => a.DoWindowContents(rect), () => e.DoWindowContents(rect)); State(a, e); ammoFrames++;
                    }
                }
            }
            finally { Set(support, "playtimeStates", original); }
        }
        private static void Patch(Harmony h, Type type, string method, string prefix) => h.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), prefix));
        private static void InstallUi(Harmony h, string scrollPrefix = nameof(Scroll), string numericPrefix = nameof(Numeric), string closePrefix = nameof(Close), string boolPrefix = nameof(BoolWidget))
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m => m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(GR) && !m.IsGenericMethodDefinition))
            {
                if (method.Name == "BeginScrollView") h.Patch(method, prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), scrollPrefix));
                else if (method.Name == "CheckboxLabeled") h.Patch(method, prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Checkbox)));
                else if (method.ReturnType == typeof(void)) h.Patch(method, prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(VoidWidget)));
                else if (method.ReturnType == typeof(bool)) h.Patch(method, prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), boolPrefix));
            }
            h.Patch(AccessTools.GetDeclaredMethods(typeof(Widgets)).Single(m => m.Name == "TextFieldNumeric" && m.IsGenericMethodDefinition).MakeGenericMethod(typeof(int)), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), numericPrefix));
            Patch(h, typeof(Widgets), "EndScrollView", nameof(EndScroll));
            h.Patch(AccessTools.Method(typeof(Widgets), "DrawLineHorizontal", new[] { typeof(float), typeof(float), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Line)));
            h.Patch(AccessTools.Method(typeof(TooltipHandler), "TipRegion", new[] { typeof(GR), typeof(TipSignal) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Tip)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.GUI), "DrawTexture", new[] { typeof(GR), typeof(UnityEngine.Texture) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Texture)));
            h.Patch(AccessTools.PropertySetter(typeof(UnityEngine.GUI), "color"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(ColorSet))); h.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.GUI), "color"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(ColorGet)));
            h.Patch(AccessTools.PropertySetter(typeof(Verse.Text), "Font"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(FontSet))); h.Patch(AccessTools.PropertyGetter(typeof(Verse.Text), "Font"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(FontGet)));
            h.Patch(AccessTools.PropertySetter(typeof(Verse.Text), "Anchor"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(AnchorSet))); h.Patch(AccessTools.PropertyGetter(typeof(Verse.Text), "Anchor"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(AnchorGet)));
            Patch(h, typeof(Verse.Text), "CalcHeight", nameof(Height)); Patch(h, typeof(Window), "Close", closePrefix); Patch(h, typeof(WindowStack), "Add", nameof(WindowAdd));
        }
        internal static void Run(string output, Action<bool, string> check)
        {
            var h = new Harmony("Helodrace.Tests.NativeTelegraphWindows");
            InstallUi(h);
            h.Patch(AccessTools.PropertyGetter(typeof(Waylith.Time), "RealtimeSinceStartupAsDouble"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Time)));
            foreach (Type t in new[] { typeof(Dialog_TelegraphTable), typeof(Frozen.Dialog_TelegraphTable) }) Patch(h, t, "MilitaryCredit", nameof(Credit));
            h.Patch(AccessTools.PropertyGetter(typeof(CompTelegraphTable), "Provider"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Provider))); h.Patch(AccessTools.PropertyGetter(typeof(CompTelegraphTable), "ConnectionFailure"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Connection)));
            Map map = Find.CurrentMap; var table = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_TelegraphTable"), ThingDefOf.Steel); table.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(table, map.Center, map); terminal = table.GetComp<CompTelegraphTable>();
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); GenSpawn.Spawn(pawn, map.Center + new IntVec3(3, 0, 0), map); provider = Find.FactionManager.AllFactions.FirstOrDefault(f => TelegraphNetworkRules.IsProvider(f.def.defName)) ?? Faction.OfPlayer;
            active = true; realtime = 100; clickButtons = false; clickText = string.Empty;
            try
            {
                if (Waylith.Time.RealtimeSinceStartupAsDouble != 100) throw new InvalidOperationException("Controlled UI clock did not bind.");
                Helpers(); Telegraph(table, pawn); Support(map, pawn);
                check(helperCases > 0, "native rectangle inset and 2D interpolation match actual 1.7 arithmetic including negative extents and clamp boundaries");
                check(drafts == 192 && editors == 36 && popupFrames == 9, "current A4 contract variants, live popup callbacks and service-editor layout/selection/unit clamps match frozen windows");
                check(animations == 7 && scrollCalls > 0, "native window sizes, entrance animation and by-ref scroll state match frozen UI contracts");
                check(casFrames == 12 && ammoFrames == 9 && supportFrames == 3, "CAS/loadout/playtime palette and forward-base/ammunition window submission geometry match frozen implementations");
            }
            finally { active = false; trace = null; h.UnpatchAll(h.Id); pawn.Destroy(); table.Destroy(); terminal = null; }
            File.WriteAllText(Path.Combine(output, "native-telegraph-ui.json"), JsonSerializer.Serialize(new { Frames = frames, WidgetSubmissions = widgets, ScrollObservationsBoth = scrollCalls, HelperCases = helperCases, ContractDraftFrames = drafts, ServiceEditorFrames = editors, PopupFrames = popupFrames, EntranceCases = animations, CasFrames = casFrames, SupportFrames = supportFrames, AmmoFrames = ammoFrames, Components = components, MaximumFloatError = maximumError, NonzeroDifferences = differences, Baseline = "6474bd48", ControlledTextMetrics = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
