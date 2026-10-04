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
        public static string SelectedUnitId;

        public static void Open(Map map)
        {
            if (map == null) return;
            if (Map != map)
            {
                Map = map;
                SelectedUnitId = null;
            }
            if (!Find.WindowStack.IsOpen<Dialog_RaidTacticalPlans>())
                Find.WindowStack.Add(new Dialog_RaidTacticalPlans());
        }

        public static RaidTacticalUnit SelectedUnit => RaidTacticalUnit.All
            .FirstOrDefault(unit => unit.Id == SelectedUnitId
                && unit.Members.Any(pawn => pawn.Spawned && pawn.Map == Map));

        public static RaidTacticalPlan SelectedPlan
        {
            get
            {
                string id = SelectedUnit?.Id;
                return id == null ? null : Map?.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(id)?.ActivePlan
                    ?? Map?.GetComponent<MapComponent_RaidTacticalPlans>()?.Plans.FirstOrDefault(plan => plan.UnitId == id);
            }
        }
    }

    public sealed class Dialog_RaidTacticalPlans : Window
    {
        private Vector2 scroll;
        private List<RaidTacticalUnit> cachedUnits;
        private Map cachedMap;
        private float nextUnitsRefresh, nextReportRefresh;
        private RaidTacticalPlan cachedPlan;
        private string cachedReport, cachedUnitId;
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
            float now = Time.realtimeSinceStartup;
            if (cachedUnits == null || cachedMap != RaidTacticalDebugSession.Map || now >= nextUnitsRefresh)
            {
                cachedMap = RaidTacticalDebugSession.Map;
                cachedUnits = RaidTacticalUnit.All.Where(unit => unit.Members.Any(pawn => pawn.Spawned
                    && pawn.Map == cachedMap)).ToList();
                nextUnitsRefresh = now + 0.25f;
                nextReportRefresh = 0f;
            }
            List<RaidTacticalUnit> units = cachedUnits;
            float y = inRect.y + 42f;
            if (units.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 50f),
                    "No active organized raid is present on this map.");
                return;
            }
            if (!units.Any(unit => unit.Id == RaidTacticalDebugSession.SelectedUnitId))
                RaidTacticalDebugSession.SelectedUnitId = units[0].Id;
            if (Widgets.ButtonText(new Rect(inRect.x, y, 180f, 28f),
                RaidTacticalDebugSession.SelectedUnit?.Label ?? "Select unit"))
            {
                Find.WindowStack.Add(new FloatMenu(units.Select(unit => new FloatMenuOption(
                    unit.Label + " [" + unit.Id + "]",
                    () => RaidTacticalDebugSession.SelectedUnitId = unit.Id)).ToList()));
            }
            y += 32f;
            bool overlay = RaidTacticalOverlaySettings.drawRaidTacticalNodes;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 195f, inRect.y + 43f, 220f, 26f),
                "HD_RaidView_Nodes".Translate(), ref overlay);
            RaidTacticalOverlaySettings.drawRaidTacticalNodes = overlay;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 425f, inRect.y + 77f, 300f, 26f),
                "HD_RaidView_Layout".Translate(), ref RaidTacticalOverlaySettings.drawRaidRoomLayout);
            Widgets.CheckboxLabeled(new Rect(inRect.x + 425f, inRect.y + 107f, 300f, 26f),
                "HD_RaidView_Clearance".Translate(), ref RaidTacticalOverlaySettings.drawRaidRoomClearance);
            Widgets.CheckboxLabeled(new Rect(inRect.x + 425f, inRect.y + 137f, 300f, 26f),
                "HD_RaidView_Contacts".Translate(), ref RaidTacticalOverlaySettings.drawRaidContacts);
            bool trace = MapComponent_RaidTacticalTrace.Enabled;
            Widgets.CheckboxLabeled(new Rect(inRect.x + 195f, inRect.y + 73f, 220f, 26f),
                "Trace tactical job changes", ref trace);
            MapComponent_RaidTacticalTrace.Enabled = trace;
            if (Widgets.ButtonText(new Rect(inRect.x + 425f, inRect.y + 42f, 175f, 29f),
                "Re-evaluate now"))
            {
                RaidTacticalDebugSession.Map.GetComponent<MapComponent_RaidTacticalPlans>()
                    .GetPlan(RaidTacticalDebugSession.SelectedUnit, true);
            }
            y = Mathf.Max(y + 10f, inRect.y + 172f);
            RaidTacticalPlan plan = RaidTacticalDebugSession.SelectedPlan;
            Rect area = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            Widgets.DrawMenuSection(area);
            if (cachedReport == null || cachedPlan != plan || cachedUnitId != RaidTacticalDebugSession.SelectedUnitId
                || now >= nextReportRefresh)
            {
                cachedPlan = plan; cachedUnitId = RaidTacticalDebugSession.SelectedUnitId;
                cachedReport = Report(plan); nextReportRefresh = now + 0.25f;
            }
            string report = cachedReport;
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
            report.AppendLine($"{plan.UnitId}  doctrine={plan.Doctrine}  tick={plan.PlannedTick}");
            report.AppendLine($"unit={plan.OrganizationId}, command group={plan.GroupId}");
            report.AppendLine("Execution=" + RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_RaidTacticalExecution>()?.Status(plan.UnitId));
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
                + $"waitingPawns={movement.WaitingPawns} peakPending={movement.PeakPendingGrids} "
                + $"oldestWait={movement.OldestWaitFrames} frames notifications={movement.PreparedNotifications} "
                + $"created={movement.CreatedGrids} hits={movement.CacheHits} "
                + $"native={movement.NativeMemoryBytes / 1048576.0:0.00} MiB "
                + $"peak={movement.PeakNativeMemoryBytes / 1048576.0:0.00} MiB "
                + $"evictions={movement.CacheEvictions} memoryWaits={movement.MemoryDeferrals} "
                + $"preparation wall time={movement.BuildMilliseconds} ms");
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
                ?.GetComponent<MapComponent_RaidTacticalExecution>()?.SupportStatusFor(plan.UnitId));
            var observation = RaidTacticalDebugSession.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                ?.StateFor(plan.UnitId)?.Observation;
            if (observation != null)
                report.AppendLine($"Opening observer={observation.Observer?.LabelShort ?? "none"} at {observation.Position} "
                    + $"peek={observation.ObservedTicks}/90 ticks complete={observation.Complete} "
                    + $"returned={observation.ReturnComplete} unavailable={observation.Unavailable} visible cells={observation.VisibleCells.Count} "
                    + $"enemy contact={observation.EnemyCell}");
            report.AppendLine($"Entry method: {plan.EntryMethod}");
            report.AppendLine("Unit contact memory:");
            report.Append(RaidTacticalDebugSession.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                ?.StateFor(plan.UnitId)?.Contacts.Report(GenTicks.TicksGame));
            var contactsState = RaidTacticalDebugSession.Map.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(plan.UnitId);
            if (contactsState != null)
            {
                report.AppendLine("Communication: " + contactsState.Communication.Status);
                report.Append(RaidTacticalDebugSession.Map.GetComponent<MapComponent_RaidTacticalCommunications>()?.ReportFor(plan.UnitId));
                foreach (RaidObserverMemory observer in contactsState.Communication.Observers)
                    report.AppendLine($"  Personal #{observer.PawnId}: {observer.Contacts.Entries.Count} contacts, {observer.Reports.Reports.Count} reports");
                foreach (RaidReportReceipt receipt in contactsState.Communication.Receipts.OrderByDescending(value => value.Tick).Take(12))
                    report.AppendLine($"  {receipt.Status}: {receipt.Peer} {receipt.ReportId}@{receipt.Revision} tick={receipt.Tick}");
                foreach (RaidTacticalReport known in contactsState.Communication.Knowledge.Reports.OrderByDescending(value => value.ObservedTick).Take(12))
                    report.AppendLine($"  {known.Kind}: {known.Position} R{known.Room}/V{known.StructureVersion} "
                        + $"source={known.OriginUnit}/#{known.ObserverId} seen={known.ObservedTick} received={known.ReceivedTick} route={string.Join(" → ", known.Route)}");
                report.AppendLine($"CQB contact pause={contactsState.ContactPause}");
                foreach (RaidContactGuard guard in contactsState.ContactGuards)
                    report.AppendLine($"  Guard {guard.Pawn?.LabelShort}: #{guard.EnemyId} focus={guard.Focus} hold={guard.Position} until={guard.Until}");
                foreach (RaidRoomSecurityRecord room in contactsState.RoomSecurity.Rooms)
                    report.AppendLine($"  R{room.Room}: recheck={room.NeedsRecheck} contact={room.Concern} "
                        + $"last threat={room.LastThreatTick} checked={room.LastCheckedTick}");
            }
            report.AppendLine($"Known-passage recheck plan={plan.ObjectiveIsRecheck}");
            report.AppendLine($"Wait before group entry: {plan.EntryDelayTicks} ticks; "
                + $"coordination allowance: {plan.CoordinationDelayTicks} ticks");
            report.AppendLine("Approach nodes: " + string.Join(" → ", plan.MovementNodes.Select(node => node.Center)));
            if (contactsState != null)
            {
                report.AppendLine($"Movement node: {contactsState.CurrentNode}/{plan.MovementNodes.Count}; complete={contactsState.ApproachComplete}");
                foreach (RaidExteriorIngress ingress in contactsState.ExteriorIngress.Where(value => !value.Complete))
                    report.AppendLine($"  Exterior join {ingress.Pawn?.LabelShort}: opening={ingress.Opening}, room={ingress.InsideRoom}, active={ingress.Active}, entered={ingress.Entered}, waypoint={ingress.MovementDestination}, clearance={ingress.Destination}, intent={ingress.Requested}");
                foreach (RaidMovementNode node in plan.MovementNodes)
                    report.AppendLine($"  N{node.Id} {node.Purpose}: {node.Center} → N{node.Next}; guidance={node.GuidanceCells.Count}; V{node.StructureVersion}");
                foreach (RaidNodeMemberProgress progress in contactsState.NodeMembers)
                    report.AppendLine($"  {progress.Pawn?.LabelShort}: passed connection N{progress.Completed}, target N{progress.DestinationNode} at {progress.Destination}");
            }
            report.AppendLine();
            report.AppendLine("Staging and security assignments:");
            foreach (RaidTacticalAssignment assignment in plan.Assignments
                .OrderBy(value => value.Task).ThenBy(value => value.EntryOrder))
            {
                report.AppendLine($"  {assignment.Pawn.LabelShort}: {assignment.Task} "
                    + (assignment.EntryOrder > 0 ? $"#{assignment.EntryOrder} " : "")
                    + $"at {assignment.Position} group={assignment.GroupId}");
                RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(assignment.Pawn);
                if (order != null)
                {
                    report.AppendLine($"    directive={order.Kind} destination={order.Destination} "
                        + $"owner={order.UnitId} group={order.GroupId} "
                        + $"room={order.Room} retryAfter={order.RetryAfter} "
                        + $"job={MapComponent_RaidTacticalTrace.Describe(assignment.Pawn.CurJob)}");
                    report.AppendLine($"    requested={order.Movement.RequestedKind}@{order.Movement.RequestedDestination} "
                        + $"controller={order.Movement.Controller} blocked={order.Movement.BlockReason} "
                        + $"blockedTicks={(order.Movement.BlockedSince < 0 ? 0 : GenTicks.TicksGame - order.Movement.BlockedSince)} "
                        + $"requestedTick={order.Movement.RequestedTick} actualGotoTick={order.Movement.StartedTick} "
                        + $"lastStartLatency={order.Movement.LastStartLatency}");
                }
                if (contactsState != null)
                {
                    RaidNodeMemberProgress progress = contactsState.NodeMembers.FirstOrDefault(value => value.Pawn == assignment.Pawn);
                    RaidExteriorIngress ingress = contactsState.ExteriorIngress.FirstOrDefault(value => value.Pawn == assignment.Pawn && !value.Complete);
                    bool personalApproachDone = plan.MovementNodes.Count > 0 && progress != null
                        && progress.Completed >= plan.MovementNodes.Count - 1;
                    report.AppendLine($"    approach unitDone={contactsState.ApproachComplete} memberDone={personalApproachDone}; "
                        + $"participating={MapComponent_RaidTacticalExecution.ContactGuardFor(contactsState, assignment.Pawn, GenTicks.TicksGame) == null} "
                        + $"slot={assignment.Position} current={assignment.Pawn.Position}");
                    if (progress?.JoinConnection != null)
                        report.AppendLine($"    personalJoin={progress.JoinConnection.Center} "
                            + $"cells={progress.JoinConnection.RestrictedCells.Count} portals={progress.JoinConnection.AllowedPortals.Count}");
                    if (ingress != null)
                        report.AppendLine($"    ingress active={ingress.Active} entered={ingress.Entered} opening={ingress.Opening} "
                            + $"clearance={ingress.Destination} requested={ingress.Requested} room={ingress.InsideRoom} "
                            + $"direct={ingress.Direct} waitingOutside={ingress.Waiting} searchAfter={ingress.SearchAfter} "
                            + $"yielding={ingress.Yielding} yieldCell={ingress.YieldCell}");
                }
            }
            report.AppendLine();
            report.AppendLine("Recent job changes (newest first):");
            report.AppendLine(RaidTacticalDebugSession.Map
                ?.GetComponent<MapComponent_RaidTacticalTrace>()?.Report(plan.UnitId));
            return report.ToString();
        }
    }

}
