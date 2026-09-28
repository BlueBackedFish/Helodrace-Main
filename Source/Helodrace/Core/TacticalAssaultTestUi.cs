using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public static class TacticalAssaultTestSession
    {
        public static Map Map;
        public static TacticalAssaultMode Mode = TacticalAssaultMode.Heliborne;
        public static bool HasExplosiveCharge = true;
        public static bool HasPowerCutter = true;
        public static bool HasOffensiveGrenade = true;
        public static bool HasFlashbang = true;
        public static bool HasCsGrenade = true;
        public static bool HasSmokeGrenade = true;
        public static IntVec3 StartCell = IntVec3.Invalid;
        public static IntVec3 ObjectiveCell = IntVec3.Invalid;
        public static TacticalAssaultPlan Plan;
        public static long LastCalculationMilliseconds;
        public static bool ShowRoute = true;

        public static TacticalBreachCapability Capabilities
        {
            get
            {
                TacticalBreachCapability value = TacticalBreachCapability.None;
                if (HasExplosiveCharge) value |= TacticalBreachCapability.ExplosiveCharge;
                if (HasPowerCutter) value |= TacticalBreachCapability.PowerCutter;
                return value;
            }
        }

        public static TacticalSupportCapability SupportCapabilities
        {
            get
            {
                TacticalSupportCapability value = TacticalSupportCapability.None;
                if (HasOffensiveGrenade) value |= TacticalSupportCapability.OffensiveGrenade;
                if (HasFlashbang) value |= TacticalSupportCapability.Flashbang;
                if (HasCsGrenade) value |= TacticalSupportCapability.CsGrenade;
                if (HasSmokeGrenade) value |= TacticalSupportCapability.SmokeGrenade;
                return value;
            }
        }

        public static void Open(Map map)
        {
            if (map == null) return;
            if (Map != map)
            {
                Map = map;
                StartCell = IntVec3.Invalid;
                ObjectiveCell = IntVec3.Invalid;
                Plan = null;
            }
            if (!Find.WindowStack.IsOpen<Dialog_TacticalAssaultTest>())
            {
                Find.WindowStack.Add(new Dialog_TacticalAssaultTest());
            }
        }

        public static void Calculate()
        {
            if (Map == null) return;
            Stopwatch stopwatch = Stopwatch.StartNew();
            Plan = TacticalAssaultPlanner.MakePlan(
                Map,
                Mode,
                Capabilities,
                SupportCapabilities,
                StartCell,
                ObjectiveCell);
            stopwatch.Stop();
            LastCalculationMilliseconds = stopwatch.ElapsedMilliseconds;
        }
    }

    public sealed class Dialog_TacticalAssaultTest : Window
    {
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(760f, 720f);

        public Dialog_TacticalAssaultTest()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Tactical assault path test");
            Text.Font = GameFont.Small;

            float y = inRect.y + 42f;
            Widgets.Label(new Rect(inRect.x, y, 100f, 28f), "Raid mode");
            if (Widgets.ButtonText(new Rect(inRect.x + 110f, y, 150f, 28f),
                TacticalAssaultTestSession.Mode == TacticalAssaultMode.Heliborne ? "● Heliborne" : "Heliborne"))
            {
                TacticalAssaultTestSession.Mode = TacticalAssaultMode.Heliborne;
                TacticalAssaultTestSession.Plan = null;
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 268f, y, 150f, 28f),
                TacticalAssaultTestSession.Mode == TacticalAssaultMode.Sabotage ? "● Sabotage" : "Sabotage"))
            {
                TacticalAssaultTestSession.Mode = TacticalAssaultMode.Sabotage;
                TacticalAssaultTestSession.Plan = null;
            }

            y += 40f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "Available breach equipment");
            y += 25f;
            bool explosive = TacticalAssaultTestSession.HasExplosiveCharge;
            bool cutter = TacticalAssaultTestSession.HasPowerCutter;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 10f, y, 310f, 26f),
                "Explosive breach (C-4 + igniter)", ref explosive);
            Widgets.CheckboxLabeled(new Rect(inRect.x + 330f, y, 280f, 26f),
                "Power cutter breach", ref cutter);
            if (explosive != TacticalAssaultTestSession.HasExplosiveCharge
                || cutter != TacticalAssaultTestSession.HasPowerCutter)
            {
                TacticalAssaultTestSession.HasExplosiveCharge = explosive;
                TacticalAssaultTestSession.HasPowerCutter = cutter;
                TacticalAssaultTestSession.Plan = null;
            }

            y += 40f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "Available entry support");
            y += 25f;
            bool offensive = TacticalAssaultTestSession.HasOffensiveGrenade;
            bool flashbang = TacticalAssaultTestSession.HasFlashbang;
            bool cs = TacticalAssaultTestSession.HasCsGrenade;
            bool smoke = TacticalAssaultTestSession.HasSmokeGrenade;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 10f, y, 170f, 26f), "Offensive grenade", ref offensive);
            Widgets.CheckboxLabeled(new Rect(inRect.x + 185f, y, 135f, 26f), "Flashbang", ref flashbang);
            Widgets.CheckboxLabeled(new Rect(inRect.x + 325f, y, 130f, 26f), "CS grenade", ref cs);
            Widgets.CheckboxLabeled(new Rect(inRect.x + 460f, y, 150f, 26f), "Smoke grenade", ref smoke);
            if (offensive != TacticalAssaultTestSession.HasOffensiveGrenade
                || flashbang != TacticalAssaultTestSession.HasFlashbang
                || cs != TacticalAssaultTestSession.HasCsGrenade
                || smoke != TacticalAssaultTestSession.HasSmokeGrenade)
            {
                TacticalAssaultTestSession.HasOffensiveGrenade = offensive;
                TacticalAssaultTestSession.HasFlashbang = flashbang;
                TacticalAssaultTestSession.HasCsGrenade = cs;
                TacticalAssaultTestSession.HasSmokeGrenade = smoke;
                TacticalAssaultTestSession.Plan = null;
            }

            y += 40f;
            DrawCellSelector(inRect, ref y, true);
            DrawCellSelector(inRect, ref y, false);

            y += 6f;
            if (Widgets.ButtonText(new Rect(inRect.x, y, 210f, 34f), "Calculate attack point and route"))
            {
                TacticalAssaultTestSession.Calculate();
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 220f, y, 120f, 34f), "Clear result"))
            {
                TacticalAssaultTestSession.Plan = null;
            }
            bool showRoute = TacticalAssaultTestSession.ShowRoute;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 360f, y + 4f, 190f, 28f), "Show route overlay", ref showRoute);
            TacticalAssaultTestSession.ShowRoute = showRoute;

            y += 46f;
            Rect resultRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            Widgets.DrawMenuSection(resultRect);
            string report = BuildReport(TacticalAssaultTestSession.Plan);
            float viewHeight = Mathf.Max(resultRect.height - 16f,
                Text.CalcHeight(report, resultRect.width - 34f) + 20f);
            Rect viewRect = new Rect(0f, 0f, resultRect.width - 18f, viewHeight);
            Widgets.BeginScrollView(resultRect.ContractedBy(8f), ref scrollPosition, viewRect);
            Widgets.Label(new Rect(4f, 2f, viewRect.width - 8f, viewHeight - 4f), report);
            Widgets.EndScrollView();
        }

        private void DrawCellSelector(Rect inRect, ref float y, bool start)
        {
            IntVec3 cell = start ? TacticalAssaultTestSession.StartCell : TacticalAssaultTestSession.ObjectiveCell;
            string name = start
                ? (TacticalAssaultTestSession.Mode == TacticalAssaultMode.Heliborne ? "Rope landing" : "Ingress")
                : "Objective";
            Widgets.Label(new Rect(inRect.x, y + 4f, 100f, 28f), name);
            Widgets.Label(new Rect(inRect.x + 105f, y + 4f, 220f, 28f), cell.IsValid ? cell.ToString() : "Automatic");
            if (Widgets.ButtonText(new Rect(inRect.x + 330f, y, 125f, 30f), "Pick on map"))
            {
                BeginPicking(start);
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 463f, y, 105f, 30f), "Automatic"))
            {
                if (start) TacticalAssaultTestSession.StartCell = IntVec3.Invalid;
                else TacticalAssaultTestSession.ObjectiveCell = IntVec3.Invalid;
                TacticalAssaultTestSession.Plan = null;
            }
            y += 36f;
        }

        private void BeginPicking(bool start)
        {
            Map map = TacticalAssaultTestSession.Map;
            Close(false);
            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetLocations = true,
                    canTargetBuildings = true,
                    canTargetItems = false,
                    canTargetPawns = false,
                    validator = target => target.Cell.InBounds(map)
                },
                target =>
                {
                    if (start) TacticalAssaultTestSession.StartCell = target.Cell;
                    else TacticalAssaultTestSession.ObjectiveCell = target.Cell;
                    TacticalAssaultTestSession.Plan = null;
                    TacticalAssaultTestSession.Open(map);
                },
                null,
                () => TacticalAssaultTestSession.Open(map));
        }

        private static string BuildReport(TacticalAssaultPlan plan)
        {
            if (plan == null)
            {
                return "Select the simulated equipment and optional cells, then calculate.\n\n"
                    + "Automatic Heliborne mode selects a low-threat rope landing near a strategic objective. "
                    + "Automatic Sabotage mode tests six promising map-edge ingress cells. Walls and doors are "
                    + "only traversable when one of the selected Helodrace breach systems can destroy them.";
            }
            if (!plan.Success)
            {
                return $"FAILED ({TacticalAssaultTestSession.LastCalculationMilliseconds} ms)\n{plan.FailureReason}\n"
                    + $"Objective: {plan.ObjectiveCell}  route end: {plan.RouteEndCell}";
            }

            StringBuilder builder = new StringBuilder(768);
            builder.AppendLine($"SUCCESS  {plan.Mode}  ({TacticalAssaultTestSession.LastCalculationMilliseconds} ms)");
            builder.AppendLine($"Objective: {plan.Objective?.Kind.ToString() ?? "manual"} at {plan.ObjectiveCell}");
            builder.AppendLine($"Insertion: {plan.InsertionCell}  assault/breach point: {plan.AttackCell}");
            if (plan.HelicopterHoverCell.IsValid)
            {
                builder.AppendLine($"Helicopter hover: {plan.HelicopterHoverCell}");
            }
            builder.AppendLine($"Route: {plan.Route.Count} cells  cost={plan.TotalCost:0.0}  "
                + $"threat exposure={plan.ThreatExposure:0.0}  expanded={plan.ExpandedNodes}");
            builder.AppendLine($"Decision: {plan.Recommendation}  crossed killzones: {plan.CrossedKillzones}");
            builder.AppendLine($"Entry strategy: {plan.EntryStrategy}  direction={plan.DominantThreatDirection}  "
                + $"directionality={plan.ThreatDirectionality:P0}");
            builder.AppendLine($"Threat after tactic estimate: {plan.MitigatedThreatExposure:0.0}  rationale: {plan.TacticalRationale}");
            builder.AppendLine($"Interior defenders: {plan.InteriorHostileCount}  personnel threat={plan.InteriorPersonnelThreat:0.0}");
            builder.AppendLine($"Suggested support: {plan.SuggestedSupport}  usable from selected equipment: {plan.UsableSupport}");
            builder.AppendLine($"Interior priority: {plan.PreferredInteriorSupport}  selected: {plan.SelectedInteriorSupport}"
                + (plan.SelectedInteriorSupport != TacticalSupportCapability.None
                    ? $" ({TacticalSupportDefNames.For(plan.SelectedInteriorSupport)})" : string.Empty));
            builder.AppendLine($"Route breaches: {plan.Breaches.Count}  coordinated flank charges: {plan.CoordinatedBreaches.Count}  "
                + $"total C-4: {plan.Breaches.Sum(step => step.RequiredC4) + plan.CoordinatedBreaches.Sum(step => step.RequiredC4)}");
            foreach (TacticalBreachStep breach in plan.Breaches)
            {
                builder.AppendLine($"  • {breach.Cell}: {breach.Method}, cost={breach.Cost:0.0}, "
                    + $"target={breach.Target?.LabelCap ?? "missing"}, C-4={breach.RequiredC4}");
            }
            foreach (TacticalBreachStep breach in plan.CoordinatedBreaches)
            {
                builder.AppendLine($"  • SYNC {breach.Cell}: flank charge, target={breach.Target?.LabelCap ?? "missing"}, "
                    + $"C-4={breach.RequiredC4}");
            }
            return builder.ToString();
        }
    }

    public sealed class MapComponent_TacticalAssaultTestOverlay : MapComponent
    {
        private static readonly Color RouteColor = new Color(0.15f, 0.85f, 1f);
        private static readonly Color ExplosiveColor = new Color(1f, 0.2f, 0.12f);
        private static readonly Color CutterColor = new Color(1f, 0.75f, 0.12f);

        public MapComponent_TacticalAssaultTestOverlay(Map map) : base(map)
        {
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            TacticalAssaultPlan plan = TacticalAssaultTestSession.Plan;
            if (!Prefs.DevMode || !TacticalAssaultTestSession.ShowRoute
                || TacticalAssaultTestSession.Map != map || plan?.Success != true) return;

            GenDraw.DrawFieldEdges(plan.Route, RouteColor, 0.08f);
            GenDraw.DrawRadiusRing(plan.InsertionCell, 0.9f, Color.green);
            GenDraw.DrawRadiusRing(plan.ObjectiveCell, 1.1f, Color.magenta);
            foreach (TacticalBreachStep breach in plan.Breaches)
            {
                GenDraw.DrawRadiusRing(breach.Cell, 0.8f,
                    breach.Method == TacticalBreachMethod.ExplosiveCharge ? ExplosiveColor : CutterColor);
            }
            foreach (TacticalBreachStep breach in plan.CoordinatedBreaches)
            {
                GenDraw.DrawRadiusRing(breach.Cell, 1.05f, ExplosiveColor);
            }
            if (plan.ThreatDirectionality > 0.05f)
            {
                Vector3 start = plan.AttackCell.ToVector3Shifted();
                Vector3 end = start + new Vector3(
                    plan.DominantThreatDirection.x,
                    0f,
                    plan.DominantThreatDirection.y) * 7f;
                GenDraw.DrawLineBetween(start, end, SimpleColor.Red, 0.18f);
            }
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            TacticalAssaultPlan plan = TacticalAssaultTestSession.Plan;
            if (!Prefs.DevMode || !TacticalAssaultTestSession.ShowRoute
                || TacticalAssaultTestSession.Map != map || plan?.Success != true) return;

            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(plan.InsertionCell), "INSERT", Color.green);
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(plan.AttackCell), "ATTACK", Color.yellow);
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(plan.ObjectiveCell), "OBJECTIVE", Color.magenta);
            foreach (TacticalBreachStep breach in plan.Breaches)
            {
                GenMapUI.DrawThingLabel(
                    GenMapUI.LabelDrawPosFor(breach.Cell),
                    breach.Method == TacticalBreachMethod.ExplosiveCharge
                        ? $"C-4 ×{breach.RequiredC4}"
                        : "CUTTER",
                    breach.Method == TacticalBreachMethod.ExplosiveCharge ? ExplosiveColor : CutterColor);
            }
            foreach (TacticalBreachStep breach in plan.CoordinatedBreaches)
            {
                GenMapUI.DrawThingLabel(
                    GenMapUI.LabelDrawPosFor(breach.Cell),
                    $"SYNC C-4 ×{breach.RequiredC4}",
                    ExplosiveColor);
            }
        }
    }
}
