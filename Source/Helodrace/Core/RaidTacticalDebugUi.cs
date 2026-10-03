using System.Collections.Generic;
using System.Linq;
using System.Text;
using Helodrace.Squads;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public static class RaidTacticalDebugSession
    {
        public static Map Map;
        public static string SelectedOrganizationId;
        public static bool ShowOverlay = true;

        public static void Open(Map map)
        {
            if (map == null) return;
            if (Map != map)
            {
                Map = map;
                SelectedOrganizationId = null;
            }
            if (!Find.WindowStack.IsOpen<Dialog_RaidTacticalPlans>())
                Find.WindowStack.Add(new Dialog_RaidTacticalPlans());
        }

        public static CombatOrganization SelectedOrganization => OrganizationAPI.Registry
            ?.Organizations.FirstOrDefault(organization => organization.id == SelectedOrganizationId
                && organization.AllMembers.Any(pawn => pawn.Spawned && pawn.Map == Map));

        public static RaidTacticalPlan SelectedPlan => SelectedOrganization == null ? null
            : Map?.GetComponent<MapComponent_RaidTacticalPlans>()?.GetPlan(SelectedOrganization);
    }

    public sealed class Dialog_RaidTacticalPlans : Window
    {
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(790f, 720f);

        public Dialog_RaidTacticalPlans()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Raid tactical planning");
            Text.Font = GameFont.Small;
            List<CombatOrganization> organizations = OrganizationAPI.Registry?.Organizations
                .Where(organization => organization.AllMembers.Any(pawn => pawn.Spawned
                    && pawn.Map == RaidTacticalDebugSession.Map)).ToList()
                ?? new List<CombatOrganization>();
            float y = inRect.y + 42f;
            if (organizations.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 50f),
                    "No active organized raid is present on this map.");
                return;
            }
            if (!organizations.Any(organization => organization.id == RaidTacticalDebugSession.SelectedOrganizationId))
                RaidTacticalDebugSession.SelectedOrganizationId = organizations[0].id;
            foreach (CombatOrganization organization in organizations.Take(5))
            {
                string label = organization.id == RaidTacticalDebugSession.SelectedOrganizationId
                    ? "● " + organization.id : organization.id;
                if (Widgets.ButtonText(new Rect(inRect.x, y, 180f, 28f), label))
                    RaidTacticalDebugSession.SelectedOrganizationId = organization.id;
                y += 32f;
            }
            bool overlay = RaidTacticalDebugSession.ShowOverlay;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 195f, inRect.y + 43f, 220f, 26f),
                "Show tactical nodes", ref overlay);
            RaidTacticalDebugSession.ShowOverlay = overlay;
            bool trace = MapComponent_RaidTacticalTrace.Enabled;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 195f, inRect.y + 73f, 220f, 26f),
                "Trace tactical job changes", ref trace);
            MapComponent_RaidTacticalTrace.Enabled = trace;
            if (Widgets.ButtonText(new Rect(inRect.x + 425f, inRect.y + 42f, 175f, 29f),
                "Re-evaluate now"))
            {
                RaidTacticalDebugSession.Map.GetComponent<MapComponent_RaidTacticalPlans>()
                    .GetPlan(RaidTacticalDebugSession.SelectedOrganization, true);
            }
            y = Mathf.Max(y + 10f, inRect.y + 90f);
            RaidTacticalPlan plan = RaidTacticalDebugSession.SelectedPlan;
            Rect area = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            Widgets.DrawMenuSection(area);
            string report = Report(plan);
            float contentHeight = Mathf.Max(area.height - 16f,
                Text.CalcHeight(report, area.width - 40f) + 24f);
            Rect content = new Rect(0f, 0f, area.width - 18f, contentHeight);
            Widgets.BeginScrollView(area.ContractedBy(8f), ref scroll, content);
            Widgets.Label(new Rect(4f, 2f, content.width - 8f, contentHeight - 4f), report);
            Widgets.EndScrollView();
        }

        private static string Report(RaidTacticalPlan plan)
        {
            if (plan == null) return "Select an active raid.";
            if (!plan.Success) return "Plan unavailable: " + plan.Reason
                + $" ({plan.PlanningMilliseconds} ms)";
            var report = new StringBuilder();
            report.AppendLine($"{plan.OrganizationId}  doctrine={plan.Doctrine}  tick={plan.PlannedTick}");
            report.AppendLine("Execution=" + RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_RaidTacticalExecution>()?.Status(plan.OrganizationId));
            report.AppendLine($"Command efficiency={plan.CommandEfficiency:P0}  casualties={plan.CasualtyFraction:P0}");
            report.AppendLine($"Start={plan.Start}  objective={plan.Objective}  front={plan.Frontline}");
            report.AppendLine($"Flank={plan.Flank}  entry={plan.Entry}");
            report.AppendLine("Wall search: " + plan.BreachSearch);
            report.AppendLine($"Planning={plan.PlanningMilliseconds} ms  "
                + $"breach candidates={plan.BreachCandidates}  "
                + $"detailed checks={plan.DetailedBreachChecks}");
            if (plan.PlannedBreach != null)
                report.AppendLine($"Breach={plan.PlannedBreach.LabelShort} at "
                    + $"{plan.BreachCell}  outside={plan.Entry}"
                    + $"  inside={plan.BreachInside}");
            else if (plan.BreachCell.IsValid)
                report.AppendLine($"Open entry={plan.BreachCell}  outside={plan.Entry}"
                    + $"  inside={plan.BreachInside}");
            report.AppendLine();
            report.AppendLine("Ranked maneuvers:");
            for (int i = 0; i < plan.Options.Count; i++)
            {
                RaidTacticalOption option = plan.Options[i];
                report.AppendLine($"  {i + 1}. {option.Maneuver}  score={option.Score:0.0}"
                    + (option == plan.Selected ? "  SELECTED" : ""));
                report.AppendLine("     " + option.Reason);
            }
            report.AppendLine();
            report.AppendLine($"Support: {plan.EntrySupport}");
            report.AppendLine($"Entry method: {plan.EntryMethod}");
            report.AppendLine($"Wait before group entry: {plan.EntryDelayTicks} ticks; "
                + $"coordination allowance: {plan.CoordinationDelayTicks} ticks");
            report.AppendLine("Approach nodes: " + string.Join(" → ", plan.ApproachNodes));
            report.AppendLine();
            report.AppendLine("Staging and security assignments:");
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .OrderBy(value => value.Task).ThenBy(value => value.EntryOrder))
                report.AppendLine($"  {assignment.Pawn.LabelShort}: {assignment.Task} "
                    + (assignment.EntryOrder > 0 ? $"#{assignment.EntryOrder} " : "")
                    + $"at {assignment.Position}");
            report.AppendLine();
            report.AppendLine("Recent job changes (newest first):");
            report.AppendLine(RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_RaidTacticalTrace>()?.Report(plan.OrganizationId));
            return report.ToString();
        }
    }

    public sealed class MapComponent_RaidTacticalOverlay : MapComponent
    {
        private static readonly Color EntryColor = new Color(0.15f, 0.85f, 0.25f);
        private static readonly Color SecurityColor = new Color(0.15f, 0.7f, 1f);
        private static readonly Color ObjectiveColor = new Color(0.9f, 0.2f, 0.8f);

        public MapComponent_RaidTacticalOverlay(Map map) : base(map) { }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            RaidTacticalPlan plan = VisiblePlan();
            if (plan == null) return;
            for (int i = 1; i < plan.ApproachPath.Count; i++)
                GenDraw.DrawLineBetween(plan.ApproachPath[i - 1].ToVector3Shifted(),
                    plan.ApproachPath[i].ToVector3Shifted(), SimpleColor.Red, 0.13f);
            GenDraw.DrawRadiusRing(plan.Objective, 1.1f, ObjectiveColor);
            GenDraw.DrawRadiusRing(plan.Entry, 1.0f, EntryColor);
            if (plan.BreachCell.IsValid)
                GenDraw.DrawRadiusRing(plan.BreachCell, 1.0f, Color.yellow);
            if (plan.Flank.IsValid) GenDraw.DrawRadiusRing(plan.Flank, 0.9f, SecurityColor);
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
                if (assignment.Position.IsValid)
                    GenDraw.DrawRadiusRing(assignment.Position, 0.45f,
                        assignment.Task == RaidTacticalTask.Entry ? EntryColor : SecurityColor);
        }

        public override void MapComponentOnGUI()
        {
            base.MapComponentOnGUI();
            RaidTacticalPlan plan = VisiblePlan();
            if (plan == null) return;
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(plan.Objective), "OBJECTIVE", ObjectiveColor);
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(plan.Entry),
                plan.BreachCell.IsValid ? "OUTSIDE" : "ENTRY", EntryColor);
            if (plan.BreachCell.IsValid)
                GenMapUI.DrawThingLabel(
                    GenMapUI.LabelDrawPosFor(plan.BreachCell),
                    plan.PlannedBreach != null ? "BREACH ENTRY" : "OPEN ENTRY", Color.yellow);
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(plan.Frontline), "FRONT", Color.red);
            foreach (RaidTacticalAssignment assignment in plan.Assignments)
                if (assignment.Position.IsValid)
                    GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(assignment.Position),
                        assignment.EntryOrder > 0 ? "E" + assignment.EntryOrder
                            : assignment.Task == RaidTacticalTask.FireSupport ? "F"
                            : assignment.Task == RaidTacticalTask.Response ? "R"
                            : assignment.Task == RaidTacticalTask.Withdraw ? "W" : "S",
                        assignment.Task == RaidTacticalTask.Entry ? EntryColor : SecurityColor);
        }

        private RaidTacticalPlan VisiblePlan()
        {
            return Prefs.DevMode && RaidTacticalDebugSession.ShowOverlay
                && RaidTacticalDebugSession.Map == map
                ? RaidTacticalDebugSession.SelectedPlan : null;
        }
    }
}
