using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using UnityEngine;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalPlanOverlaySettings
    {
        // Session-only developer View switch, off by default.
        public static bool drawTacticalPlanNodes;
    }

    [HarmonyPatch(typeof(DebugTabMenu_Settings), "InitActions")]
    public static class Patch_DebugSettings_TacticalPlanNodes
    {
        public static void Postfix(DebugActionNode __result)
        {
            var field = AccessTools.Field(typeof(TacticalPlanOverlaySettings), nameof(TacticalPlanOverlaySettings.drawTacticalPlanNodes));
            if (__result == null || __result.children.Any(child => child.settingsField == field)) return;
            DebugActionNode node = null;
            node = new DebugActionNode("HD_Tactics_ShowPlanNodes".Translate(), DebugActionType.Action, () =>
            {
                TacticalPlanOverlaySettings.drawTacticalPlanNodes = !TacticalPlanOverlaySettings.drawTacticalPlanNodes;
                node.DirtyLabelCache();
            }) { category = "View", settingsField = field };
            __result.AddChild(node);
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private sealed class PlanOverlayNode
        {
            internal IntVec3 Cell;
            internal SimpleColor Color;
            internal string Label;
            internal bool Major;
            internal int LabelRow;
        }
        private const int OverlayNodeLimit = 256, OverlayRefreshFrames = 30, OverlayLabelLimit = 96;
        private readonly List<PlanOverlayNode> planOverlayNodes = new List<PlanOverlayNode>();
        private int planOverlayRefreshAt = -1;
        private bool PlanOverlayEnabled => Prefs.DevMode && TacticalPlanOverlaySettings.drawTacticalPlanNodes && Find.CurrentMap == map;

        public override void MapComponentUpdate()
        {
            if (!PlanOverlayEnabled)
            {
                if (planOverlayNodes.Count > 0) planOverlayNodes.Clear();
                planOverlayRefreshAt = -1; return;
            }
            if (Waylith.Time.FrameCount >= planOverlayRefreshAt)
            {
                RefreshPlanOverlay(); planOverlayRefreshAt = Waylith.Time.FrameCount + OverlayRefreshFrames;
            }
            CellRect view = Find.CameraDriver.CurrentViewRect;
            foreach (PlanOverlayNode node in planOverlayNodes)
            {
                if (!view.Contains(node.Cell)) continue;
                Vector3 center = node.Cell.ToVector3Shifted(); center.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                // Two short strokes rather than a many-segment circle per slot.
                GenDraw.DrawLineBetween(center + new Vector3(-.3f,0,-.3f), center + new Vector3(.3f,0,.3f), node.Color, .06f);
                GenDraw.DrawLineBetween(center + new Vector3(-.3f,0,.3f), center + new Vector3(.3f,0,-.3f), node.Color, .06f);
            }
        }

        public override void MapComponentOnGUI()
        {
            if (!PlanOverlayEnabled || Event.current.type != EventType.Repaint) return;
            CellRect view = Find.CameraDriver.CurrentViewRect;
            IntVec3 mouse = UI.MouseCell(); int drawn = 0;
            // Named primary nodes stay legible; pawn slots reveal their number
            // near the mouse instead of covering an entire 13-man footprint.
            foreach (PlanOverlayNode node in planOverlayNodes)
            {
                if (!view.Contains(node.Cell) || !node.Major && node.Cell.DistanceToSquared(mouse) > 4) continue;
                if (drawn++ >= OverlayLabelLimit) break;
                Vector2 position = GenMapUI.LabelDrawPosFor(node.Cell); position.y -= 14f * (node.LabelRow + 1);
                GenMapUI.DrawThingLabel(position, node.Label, node.Color.ToUnityColor());
            }
        }

        private void RefreshPlanOverlay()
        {
            planOverlayNodes.Clear(); int squad = 0;
            var labelRows = new Dictionary<IntVec3,int>();
            foreach (TacticalSquadCommand command in ExecutionCommands)
            {
                if (command.Terminal || command.Phase == TacticalCommandPhase.Returning || command.Phase == TacticalCommandPhase.Complete) continue;
                string prefix = (++squad) + (command.CqbTeam >= 0 ? " FT" + (command.CqbTeam + 1) : "") + ": ";
                var atCell = new Dictionary<IntVec3, PlanOverlayNode>();
                void Add(IntVec3 cell, string key, SimpleColor color, int number = 0, bool major = true)
                {
                    if (!cell.IsValid || !cell.InBounds(map)) return;
                    string text = key.Translate() + (number > 0 ? " " + number : "");
                    if (atCell.TryGetValue(cell, out PlanOverlayNode existing))
                    {
                        existing.Label += " / " + text; existing.Major |= major; return;
                    }
                    if (planOverlayNodes.Count >= OverlayNodeLimit) return;
                    labelRows.TryGetValue(cell,out int row); labelRows[cell] = row + 1;
                    var node = new PlanOverlayNode { Cell=cell, Color=color, Label=prefix+text, Major=major, LabelRow=row };
                    atCell.Add(cell,node); planOverlayNodes.Add(node);
                }
                Add(command.Goal,"HD_Tactics_NodeGoal",SimpleColor.White);
                Add(command.Mission.NextGoal,"HD_Tactics_NodeNext",SimpleColor.Cyan);
                if (command.Plan is TacticalLocalPlan plan)
                {
                    Add(plan.Opening,"HD_Tactics_NodeOpening",SimpleColor.Red);
                    if (!plan.Direct)
                    {
                        if (!plan.ReportedPassage) Add(plan.Outside,"HD_Tactics_NodeApproach",SimpleColor.Yellow);
                        Add(plan.Inside,"HD_Tactics_NodeInside",SimpleColor.Green);
                        if (!plan.ReportedPassage) Add(plan.EntryLane,"HD_Tactics_NodeLane",SimpleColor.Cyan);
                    }
                    for(int i=0;!plan.ReportedPassage && i<plan.Stack.Count;i++)
                        Add(plan.Stack[i],"HD_Tactics_NodeStack",SimpleColor.Yellow,i+1,false);
                    for(int i=0;i<plan.Positions.Count;i++)
                        Add(plan.Positions[i],plan.RetainedOutside.Contains(i) ? "HD_Tactics_NodeGuard" : "HD_Tactics_NodeEntry",
                            plan.RetainedOutside.Contains(i) ? SimpleColor.Blue : SimpleColor.Green,i+1,false);
                }
                if (command.FieldResponse is TacticalFieldResponse field)
                {
                    Add(field.Focus,"HD_Tactics_NodeFocus",SimpleColor.Red);
                    if (field.High) for(int i=0;i<field.FirePoints.Count;i++) Add(field.FirePoints[i],"HD_Tactics_NodeFireArea",SimpleColor.Cyan,i+1);
                    else
                    {
                        Add(field.Anchor,"HD_Tactics_NodeFireArea",SimpleColor.Cyan,1);
                        if (field.LowFlank) Add(field.FirePoints[1],"HD_Tactics_NodeFireArea",SimpleColor.Cyan,2);
                    }
                    if(field.Screen != null) Add(field.Screen.Target,"HD_Tactics_NodeSmoke",SimpleColor.White);
                    for (int i=0;i<command.Members.Count;i++)
                        if (command.Members[i].FieldPostState == TacticalFieldPostState.Fixed)
                            Add(command.Members[i].FieldPost,"HD_Tactics_NodeGuard",SimpleColor.Blue,i+1,false);
                    if (field.Maneuver != null) for (int i=0;i<field.Maneuver.Waypoints.Count;i++)
                        Add(field.Maneuver.Waypoints[i],"HD_Tactics_NodeFireArea",SimpleColor.Cyan,i+1);
                }
                if (command.ContactResponse is TacticalContactResponse contact)
                    for(int i=0;i<contact.Posts.Count;i++) Add(contact.Posts[i],"HD_Tactics_NodeGuard",SimpleColor.Blue,i+1,false);
                if (command.OpeningAction is TacticalOpeningAction opening)
                {
                    Add(opening.ObservationPosition,"HD_Tactics_NodeObserve",SimpleColor.Cyan);
                    if (opening.ShowThrow)
                    {
                        Add(opening.Source,"HD_Tactics_NodeThrowFrom",SimpleColor.Yellow);
                        Add(opening.Target,"HD_Tactics_NodeThrowAt",SimpleColor.Red);
                    }
                }
                if (planOverlayNodes.Count >= OverlayNodeLimit) break;
            }
        }
    }
}
