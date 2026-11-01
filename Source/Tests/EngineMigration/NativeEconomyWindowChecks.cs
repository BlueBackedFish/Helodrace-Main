using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using Helodrace.Economy;
using RimWorld;
using Verse;
using GV = UnityEngine.Vector2;
using GR = UnityEngine.Rect;
using NV = System.Numerics.Vector2;

namespace Helodrace.Tests
{
    // Reuse the game API submission observer; the product windows share no test adapter.
    internal static partial class NativeWindowSubmissionChecks
    {
        private static bool economyActive, guiEnabled = true;
        private static GV expectedScrollInput, controlledScrollOutput;
        private static string controlledBorrowFailure;
        private static int loanFrames, lossFrames, lossPopups, numericEdits, enabledChanges, closeCalls, sizeCases;
        private static readonly HashSet<string> loanStates = new();
        private static readonly HashSet<CaravanLossKind> lossKinds = new();
        private static bool EnabledSet(bool __0)
        {
            if (!active || !economyActive) return true;
            guiEnabled = __0; Add("gui-enabled", default, null, __0 ? 1 : 0); enabledChanges++; return false;
        }
        private static bool EnabledGet(ref bool __result)
        { if (!active || !economyActive) return true; __result = guiEnabled; return false; }
        private static bool EconomyClose()
        { if (!active || !economyActive) return true; Add("close", default); closeCalls++; return false; }
        private static bool BorrowFailure(ref string __result)
        { if (!active || !economyActive) return true; __result = controlledBorrowFailure; return false; }
        private static bool EconomyScroll(GR __0, ref GV __1, GR __2)
        {
            if (!active || !economyActive || trace == null) return true;
            Bits(__1, expectedScrollInput, "scroll input");
            Add("scroll", __0, null, __1.x, __1.y); Add("view", __2);
            __1 = controlledScrollOutput; scrollCalls++; return false;
        }
        private static bool EconomyNumeric(GR __0, ref int __1, ref string __2, float __3, float __4)
        {
            if (!active || !economyActive || trace == null) return true;
            int min = (int)__3, max = (int)__4;
            Add("numeric", __0, __2, __1, min, max);
            // Widgets owns numeric parsing/clamping. Exercise its by-ref result at
            // both bounds; this test does not claim real text input coverage.
            __1 = Math.Clamp(numericInput, min, max); __2 = __1.ToString(); numericEdits++; return false;
        }
        private static void Bits(GV a, GV e, string scope)
        {
            if (BitConverter.SingleToInt32Bits(a.x) != BitConverter.SingleToInt32Bits(e.x)
                || BitConverter.SingleToInt32Bits(a.y) != BitConverter.SingleToInt32Bits(e.y))
                throw new InvalidOperationException("Economy " + scope + " changed bits.");
        }
        private static void SetScroll(object a, object e, GV value)
        { Set(a, "scroll", new NV(value.x, value.y)); Set(e, "scroll", value); expectedScrollInput = value; }
        private static void CheckEconomyState(object a, object e)
        {
            State(a, e);
            var n = (NV)Get(a, "scroll"); Bits(new GV(n.X, n.Y), (GV)Get(e, "scroll"), "native/frozen scroll state");
            Bits(new GV(n.X, n.Y), controlledScrollOutput, "scroll return");
            foreach (string field in new[] { "amountBuffer", "daysBuffer", "repaymentBuffer" })
            {
                var f = a.GetType().GetField(field, All);
                if (f != null && !Equals(f.GetValue(a), Get(e, field))) throw new InvalidOperationException("Economy numeric buffer changed: " + field);
            }
        }
        private static void LoanWindows(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;
            foreach (GR rect in new[] { new GR(0, 0, 760, 950), new GR(1.125f, -.25f, 440.5f, 560.25f), new GR(-30, 5, 1020, 1200) })
                foreach (string phase in new[] { "draft", "pending", "due", "overdue", "paid", "cancelled" })
                    foreach (string rejection in new[] { null, "Controlled unavailable transmission, with enough text to wrap on the narrow page." })
                        foreach (int edit in new[] { -1, 1, 60, 2000, 2001 })
                        {
                            var a = new Dialog_LoanContract(pawn, terminal); var e = new FrozenEconomy.Dialog_LoanContract(pawn, terminal);
                            LoanContract contract = phase == "draft" ? null : new LoanContract { Lender = provider, Destination = pawn.Map,
                                Account = new LoanAccount { ApprovedAmount = 1000, DurationDays = 15, Disbursed = phase == "cancelled" ? 0 : 800,
                                    Repaid = phase == "paid" ? 800 : phase == "cancelled" ? 0 : 125, DeliveryFinished = phase != "pending", FundedTick = phase == "overdue" ? now - 15 * 60000 : now } };
                            foreach (object v in new object[] { a, e }) { Set(v, "contract", contract); Set(v, "repayment", edit % 2 == 0 ? 10000 : 0); }
                            controlledBorrowFailure = phase == "draft" && edit == 2001 ? "Controlled insufficient credit" : null;
                            failure = rejection; numericInput = edit;
                            GV input = edit == -1 ? new GV(-0f, float.Epsilon) : new GV(.125f, 116.125f);
                            controlledScrollOutput = edit == -1 ? new GV(float.Epsilon, -0f) : new GV(.25f, 345.5f);
                            SetScroll(a, e, input);
                            Frame(() => a.DoWindowContents(rect), () => e.DoWindowContents(rect), () => guiEnabled = true);
                            CheckEconomyState(a, e); loanFrames++; loanStates.Add(phase);
                            if (phase == "draft" && ((int)Get(a, "amount") != Math.Clamp(edit, 1, LoanRules.MaximumAmount)
                                || (int)Get(a, "days") != Math.Clamp(edit, LoanRules.MinimumDays, LoanRules.MaximumDays)))
                                throw new InvalidOperationException("Draft did not consume controlled numeric bounds: edit=" + edit + ", amount=" + Get(a, "amount") + ", days=" + Get(a, "days") + ", observed=" + numericEdits);
                            if (phase is "due" or "overdue" && (int)Get(a, "repayment") != Math.Clamp(edit, 1, contract.Account.Outstanding))
                                throw new InvalidOperationException("Repayment did not consume controlled numeric bounds.");
                        }
            failure = null; controlledBorrowFailure = null;
        }
        private static List<CaravanLossEntry> LossEntries(int count, Faction second)
        {
            var result = new List<CaravanLossEntry>();
            for (int i = 0; i < count; i++)
            {
                var kind = (CaravanLossKind)(i % 5); lossKinds.Add(kind);
                result.Add(new CaravanLossEntry { Faction = i % 3 == 0 ? null : i % 3 == 1 ? provider : second,
                    FactionName = "Archived faction", CaravanKey = "Visit" + i, MapName = "Controlled map", PawnName = i % 2 == 0 ? "Pawn" + i : null,
                    Subject = "Subject" + i, Kind = kind, Tick = i * 12345, Count = i + 1, UnitValue = 12.75, Value = 12.75 * (i + 1),
                    GoodwillPenalty = -3, FinancialPenalty = -1.25f });
            }
            return result;
        }
        private static void LossWindows()
        {
            var ledger = GameComponent_CaravanLosses.Current; object original = Get(ledger, "entries");
            Faction second = Find.FactionManager.AllFactions.First(f => f != provider);
            try
            {
                foreach (int count in new[] { 0, 1, 5, 50 })
                    foreach (Faction filter in new[] { null, provider, second })
                        foreach (GR rect in new[] { new GR(0, 0, 1040, 760), new GR(7.125f, -.25f, 540.5f, 580.25f), new GR(0, 0, 1200, 1000) })
                            foreach (float y in new[] { -0f, float.Epsilon, MathF.BitDecrement(116f), 116f, MathF.BitIncrement(116f), 345.5f, 5800f })
                            {
                                Set(ledger, "entries", LossEntries(count, second));
                                var a = new Dialog_CaravanLosses(filter); var e = new FrozenEconomy.Dialog_CaravanLosses(filter);
                                controlledScrollOutput = new GV(float.Epsilon, y); SetScroll(a, e, new GV(-0f, y));
                                Frame(() => a.DoWindowContents(rect), () => e.DoWindowContents(rect), () => guiEnabled = true);
                                CheckEconomyState(a, e); lossFrames++;
                            }
                Set(ledger, "entries", LossEntries(50, second));
                foreach (Faction target in new[] { provider, second, null })
                {
                    var a = new Dialog_CaravanLosses(provider); var e = new FrozenEconomy.Dialog_CaravanLosses(provider);
                    SetScroll(a, e, new GV(20, 300)); expectedScrollInput = controlledScrollOutput = GV.zero;
                    clickButtons = true; clickText = "HD_CaravanLoss_SelectFaction".Translate();
                    popupSelection = target?.Name ?? "HD_CaravanLoss_AllFactions".Translate().ToString();
                    Frame(() => a.DoWindowContents(new GR(0, 0, 1040, 760)), () => e.DoWindowContents(new GR(0, 0, 1040, 760)), () => guiEnabled = true);
                    if (!ReferenceEquals(Get(a, "faction"), target) || !ReferenceEquals(Get(e, "faction"), target)) throw new InvalidOperationException("Ledger popup filter callback did not run.");
                    CheckEconomyState(a, e); lossPopups++; clickButtons = false; popupSelection = null;
                }
            }
            finally { Set(ledger, "entries", original); }
        }
        private static void InitialSizes(Pawn pawn)
        {
            int width = UI.screenWidth, height = UI.screenHeight;
            try
            {
                foreach (var size in new[] { (Width: 480, Height: 360), (Width: 1100, Height: 820), (Width: 2560, Height: 1440) })
                {
                    UI.screenWidth = size.Width; UI.screenHeight = size.Height;
                    Bits(new Dialog_LoanContract(pawn, terminal).InitialSize, new FrozenEconomy.Dialog_LoanContract(pawn, terminal).InitialSize, "loan initial size");
                    Bits(new Dialog_CaravanLosses(null).InitialSize, new FrozenEconomy.Dialog_CaravanLosses(null).InitialSize, "ledger initial size"); sizeCases += 2;
                }
            }
            finally { UI.screenWidth = width; UI.screenHeight = height; }
        }
        internal static void RunEconomy(string output, Action<bool, string> check)
        {
            var h = new Harmony("Helodrace.Tests.NativeEconomyWindows");
            InstallUi(h, nameof(EconomyScroll), nameof(EconomyNumeric), nameof(EconomyClose));
            h.Patch(AccessTools.PropertySetter(typeof(UnityEngine.GUI), "enabled"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(EnabledSet)));
            h.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.GUI), "enabled"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(EnabledGet)));
            Patch(h, typeof(GameComponent_Loans), "BorrowFailure", nameof(BorrowFailure));
            h.Patch(AccessTools.PropertyGetter(typeof(CompTelegraphTable), "Provider"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Provider)));
            h.Patch(AccessTools.PropertyGetter(typeof(CompTelegraphTable), "ConnectionFailure"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(Connection)));
            Map map = Find.CurrentMap; var table = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_TelegraphTable"), ThingDefOf.Steel); table.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(table, map.Center, map); terminal = table.GetComp<CompTelegraphTable>();
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); GenSpawn.Spawn(pawn, map.Center + new IntVec3(3, 0, 0), map);
            provider = Find.FactionManager.AllFactions.First(f => TelegraphNetworkRules.IsProvider(f.def.defName));
            active = economyActive = true; clickButtons = false;
            try
            {
                InitialSizes(pawn); LoanWindows(pawn); LossWindows();
                // Observe Close callbacks separately, without claiming real WindowStack lifetime/input.
                clickButtons = true; clickText = "CloseButton".Translate(); controlledScrollOutput = GV.zero;
                var a = new Dialog_LoanContract(pawn, terminal); var e = new FrozenEconomy.Dialog_LoanContract(pawn, terminal); SetScroll(a, e, GV.zero);
                Frame(() => a.DoWindowContents(new GR(0, 0, 760, 950)), () => e.DoWindowContents(new GR(0, 0, 760, 950)), () => guiEnabled = true);
                var la = new Dialog_CaravanLosses(null); var le = new FrozenEconomy.Dialog_CaravanLosses(null); SetScroll(la, le, GV.zero);
                Frame(() => la.DoWindowContents(new GR(0, 0, 1040, 760)), () => le.DoWindowContents(new GR(0, 0, 1040, 760)), () => guiEnabled = true);
                check(sizeCases == 6, "native economy initial sizes match frozen windows at three screen dimensions");
                check(loanFrames == 180 && loanStates.Count == 6 && numericEdits > 0 && enabledChanges > 0, "loan draft, pending, due, overdue, paid and cancelled layouts and bounded numeric by-ref state match frozen windows");
                check(lossFrames == 252 && lossKinds.Count == 5, "ledger empty/list/filter layouts and visible rows match frozen UI at adjacent 116-pixel scroll boundaries");
                check(lossPopups == 3 && closeCalls == 4, "ledger faction popup callbacks reset scroll and both economy Close submissions match frozen windows");
                check(scrollCalls > 0, "economy scroll entry and returned state preserve signed-zero/subnormal bits across game/native boundaries");
            }
            finally { active = economyActive = false; trace = null; clickButtons = false; popupSelection = null; h.UnpatchAll(h.Id); pawn.Destroy(); table.Destroy(); terminal = null; }
            File.WriteAllText(Path.Combine(output, "native-economy-ui.json"), JsonSerializer.Serialize(new { Frames = frames, WidgetSubmissions = widgets, LoanFrames = loanFrames, LoanStates = loanStates, LedgerFrames = lossFrames, LedgerPopups = lossPopups, NumericEditsBoth = numericEdits, EnabledChangesBoth = enabledChanges, CloseCallsBoth = closeCalls, InitialSizeCases = sizeCases, ScrollObservationsBoth = scrollCalls, Components = components, MaximumFloatError = maximumError, NonzeroDifferences = differences, Baseline = "06ce2293", ControlledTextMetrics = true, ControlledNumericWidget = true, ControlledBorrowRejections = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
