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

        public static RaidTacticalPlan SelectedPlan
        {
            get
            {
                string id = SelectedOrganization?.id;
                return id == null ? null : Map?.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(id)?.ActivePlan
                    ?? Map?.GetComponent<MapComponent_RaidTacticalPlans>()?.Plans.FirstOrDefault(plan => plan.OrganizationId == id);
            }
        }
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
            bool overlay = RaidTacticalOverlaySettings.drawRaidTacticalNodes;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 195f, inRect.y + 43f, 220f, 26f),
                "HD_RaidView_Nodes".Translate(), ref overlay);
            RaidTacticalOverlaySettings.drawRaidTacticalNodes = overlay;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 425f, inRect.y + 77f, 300f, 26f),
                "HD_RaidView_Layout".Translate(), ref RaidTacticalOverlaySettings.drawRaidRoomLayout);
            Widgets.CheckboxLabeled(new Rect(inRect.x + 425f, inRect.y + 107f, 300f, 26f),
                "HD_RaidView_Clearance".Translate(), ref RaidTacticalOverlaySettings.drawRaidRoomClearance);
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
            y = Mathf.Max(y + 10f, inRect.y + 142f);
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
            if (plan == null) return "Waiting for tactical structure: " + RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_TacticalMapAnalysis>()?.BuildStatus;
            if (!plan.Success) return "Plan unavailable: " + plan.Reason
                + $" ({plan.PlanningMilliseconds} ms)";
            var report = new StringBuilder();
            report.AppendLine($"{plan.OrganizationId}  doctrine={plan.Doctrine}  tick={plan.PlannedTick}");
            report.AppendLine("Execution=" + RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_RaidTacticalExecution>()?.Status(plan.OrganizationId));
            report.AppendLine($"Command efficiency={plan.CommandEfficiency:P0}  casualties={plan.CasualtyFraction:P0}");
            report.AppendLine($"Start={plan.Start}  objective={plan.Objective}  front={plan.Frontline}");
            report.AppendLine($"Flank={plan.Flank}  entry={plan.Entry}");
            report.AppendLine($"CQB intent={plan.CqbIntent}  occupied room=R{plan.OccupiedRoom}");
            report.AppendLine("Wall search: " + plan.BreachSearch);
            report.AppendLine($"Planning={plan.PlanningMilliseconds} ms  "
                + $"breach candidates={plan.BreachCandidates}  "
                + $"detailed checks={plan.DetailedBreachChecks}");
            var movement = RaidTacticalDebugSession.Map.GetComponent<MapComponent_RaidMovementAreas>();
            report.AppendLine($"Movement area requests={movement.Requests} "
                + $"grids={movement.CachedGrids} pending={movement.PendingGrids} "
                + $"preparation={movement.BuildMilliseconds} ms");
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
            report.AppendLine("Support execution: " + RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_RaidTacticalExecution>()?.SupportStatusFor(plan.OrganizationId));
            report.AppendLine($"Entry method: {plan.EntryMethod}");
            report.AppendLine($"Wait before group entry: {plan.EntryDelayTicks} ticks; "
                + $"coordination allowance: {plan.CoordinationDelayTicks} ticks");
            report.AppendLine("Approach nodes: " + string.Join(" → ", plan.ApproachNodes));
            report.AppendLine();
            report.AppendLine("Staging and security assignments:");
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .OrderBy(value => value.Task).ThenBy(value => value.EntryOrder))
            {
                report.AppendLine($"  {assignment.Pawn.LabelShort}: {assignment.Task} "
                    + (assignment.EntryOrder > 0 ? $"#{assignment.EntryOrder} " : "")
                    + $"at {assignment.Position}");
                RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(assignment.Pawn);
                if (order != null)
                    report.AppendLine($"    directive={order.Kind} destination={order.Destination} "
                        + $"room={order.Room} retryAfter={order.RetryAfter} "
                        + $"job={MapComponent_RaidTacticalTrace.Describe(assignment.Pawn.CurJob)}");
            }
            report.AppendLine();
            report.AppendLine("Recent job changes (newest first):");
            report.AppendLine(RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_RaidTacticalTrace>()?.Report(plan.OrganizationId));
            return report.ToString();
        }
    }

}
