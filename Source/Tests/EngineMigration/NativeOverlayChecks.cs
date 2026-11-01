using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using GC = UnityEngine.Color;
using GV = UnityEngine.Vector2;
using V3 = UnityEngine.Vector3;
using GR = UnityEngine.Rect;
using GE = UnityEngine.Event;
using ET = UnityEngine.EventType;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private static int overlayFrame, organizationFrames, hiddenOrganizationFrames, planFrames, nodeComparisons, labelLimitFrames, organizationRefreshes, tacticalRefreshes, maximumNodes;
        private static int labelSubmissionsBoth, lineSubmissionsBoth;
        private static bool overlayActive, overlayFogged;
        private static readonly List<(int, int)> overlayLabelBits = new();
        private static int overlayLabelBitPairs;
        private static GV projected;
        private static CellRect overlayView;
        private static IntVec3 overlayMouse;
        private static CombatGroup overlayGroup;
        private static PawnOrganizationComponent overlayPawn;
        private static IEnumerable<TacticalSquadCommand> overlayCommands = Array.Empty<TacticalSquadCommand>();
        private static MapComponent_TacticalCommands overlayService;
        private static bool OverlayClock(ref int __result) { if (!active || !overlayActive) return true; __result = overlayFrame; return false; }
        private static bool OverlayView(ref CellRect __result) { if (!active || !overlayActive) return true; __result = overlayView; return false; }
        private static bool OverlayMouse(ref IntVec3 __result) { if (!active || !overlayActive) return true; __result = overlayMouse; return false; }
        private static bool OverlayGroup(PawnOrganizationComponent __instance, ref CombatGroup __result)
        { if (!active || !overlayActive || __instance != overlayPawn) return true; __result = overlayGroup; return false; }
        private static bool OverlayCommands(MapComponent_TacticalCommands __instance, ref IEnumerable<TacticalSquadCommand> __result)
        { if (!active || !overlayActive || __instance != overlayService) return true; __result = overlayCommands; return false; }
        private static bool PawnProjection(Thing __0, float __1, ref GV __result)
        { if (!active || !overlayActive) return true; Add("projection-pawn", new GR(projected.x, projected.y, 0, 0), null, __1); __result = projected; return false; }
        private static bool CellProjection(IntVec3 __0, ref GV __result)
        { if (!active || !overlayActive) return true; __result = new GV(projected.x + __0.x * 7.125f, projected.y + __0.z * 5.25f); return false; }
        private static bool OverlayLabel(GV __0, string __1, GC __2)
        { if (!active || !overlayActive) return true; Add("overlay-label", new GR(__0.x, __0.y, 0, 0), __1, __2.r, __2.g, __2.b, __2.a); overlayLabelBits.Add((BitConverter.SingleToInt32Bits(__0.x), BitConverter.SingleToInt32Bits(__0.y))); labelSubmissionsBoth++; return false; }
        private static bool OverlayLine(V3 __0, V3 __1, SimpleColor __2, float __3)
        { if (!active || !overlayActive) return true; Add("overlay-line", default, null, __0.x, __0.y, __0.z, __1.x, __1.y, __1.z, (int)__2, __3); lineSubmissionsBoth++; return false; }
        private static void OrganizationRefreshed() { if (active && overlayActive) organizationRefreshes++; }
        private static void TacticalRefreshed() { if (active && overlayActive) tacticalRefreshes++; }
        private static void OverlayComparedFrame(Action a, Action e, Action prepare = null)
        {
            overlayLabelBits.Clear(); Frame(a, e, prepare);
            int half = overlayLabelBits.Count / 2;
            if (overlayLabelBits.Count % 2 != 0 || !overlayLabelBits.Take(half).SequenceEqual(overlayLabelBits.Skip(half))) throw new InvalidOperationException("Overlay label position bits changed.");
            overlayLabelBitPairs += half;
        }
        private static object[] OrganizationCache(PawnOrganizationComponent comp) => new[] { Get(comp, "overlayRefreshFrame"), Get(comp, "overlayGroupLabel"), Get(comp, "overlayRoleLabel"), Get(comp, "overlayCommandLabel") };
        private static void RestoreOrganizationCache(PawnOrganizationComponent comp, object[] values)
        {
            string[] names = { "overlayRefreshFrame", "overlayGroupLabel", "overlayRoleLabel", "overlayCommandLabel" };
            for (int i = 0; i < names.Length; i++) Set(comp, names[i], values[i]);
        }
        private static void OrganizationFrame(bool hidden = false)
        {
            object[] before = OrganizationCache(overlayPawn), after = null;
            OverlayComparedFrame(() => { OrganizationOverlay.Draw(overlayPawn); after = OrganizationCache(overlayPawn); if (hidden && trace.Count != 0) throw new InvalidOperationException("Hidden organization overlay submitted labels: dev=" + Prefs.DevMode + ", enabled=" + OrganizationOverlaySettings.drawRaidOrganizationInfo + ", fog=" + overlayFogged + ", spawned=" + ((Pawn)overlayPawn.parent).Spawned + ", group=" + overlayGroup?.id + ", organization=" + overlayGroup?.Organization?.id); },
                () => { FrozenOverlays.OrganizationOverlay.Draw(overlayPawn); if (!after.SequenceEqual(OrganizationCache(overlayPawn))) throw new InvalidOperationException("Organization label cache/timing changed."); },
                () => RestoreOrganizationCache(overlayPawn, before));
            if (hidden) hiddenOrganizationFrames++; else organizationFrames++;
        }
        private static void OrganizationCases(Pawn pawn, Pawn leader)
        {
            var formation = DefDatabase<FormationDef>.AllDefsListForReading.First(f => f.commanderRole != null);
            var combatRole = DefDatabase<RoleDef>.AllDefsListForReading.First(r => !r.isCommandRole);
            var group = new CombatGroup { id = "native-overlay", name = "Controlled squad", formation = formation,
                roleAssignments = new List<RoleAssignment> { new RoleAssignment { pawn = pawn, combatRole = combatRole, commandRole = formation.commanderRole } } };
            var organization = new CombatOrganization { id = "NativeOverlay", faction = Faction.OfPlayer, rootGroups = new List<CombatGroup> { group } }; organization.RestoreTreeLinks(); overlayGroup = group;
            foreach (int role in new[] { 0, 1, 2, 3 })
                foreach (GV point in new[] { new GV(-0f, float.Epsilon), new GV(256.125f, 144.5f), new GV(-1000.25f, -900.5f), new GV(8192.5f, 4096.25f) })
                    foreach (int frame in new[] { 0, 29, 30, 31, 60, 61 })
                    {
                        group.commander = role == 0 ? pawn : role == 2 ? leader : null;
                        group.actingCommander = role == 1 ? pawn : null;
                        group.commandState = role == 1 ? CommandState.ActingCommander : role == 3 ? CommandState.CommanderLost : CommandState.Normal;
                        projected = point; overlayFrame = frame;
                        RestoreOrganizationCache(overlayPawn, new object[] { 0, "Cached group", "Cached role", "Cached command" });
                        OrganizationFrame();
                        bool refreshed = frame >= 30;
                        if ((int)Get(overlayPawn, "overlayRefreshFrame") != (refreshed ? frame : 0)
                            || ((string)Get(overlayPawn, "overlayGroupLabel") == "Cached group") == refreshed)
                            throw new InvalidOperationException("Organization 30-frame cache boundary was not exercised.");
                    }
            Set(overlayPawn, "overlayRefreshFrame", -1); overlayFrame = 3; OrganizationFrame();
            OrganizationOverlaySettings.drawRaidOrganizationInfo = false; OrganizationFrame(true); OrganizationOverlaySettings.drawRaidOrganizationInfo = true;
            Prefs.DevMode = false; OrganizationFrame(true); Prefs.DevMode = true;
            overlayFogged = true; pawn.Map.fogGrid.Refog(new CellRect(pawn.Position.x, pawn.Position.z, 1, 1));
            if (!pawn.Map.fogGrid.IsFogged(pawn.Position)) throw new InvalidOperationException("Actual fog fixture did not hide its cell.");
            OrganizationFrame(true); pawn.Map.fogGrid.Unfog(pawn.Position); overlayFogged = false;
            overlayGroup = null; OrganizationFrame(true); overlayGroup = new CombatGroup(); OrganizationFrame(true); overlayGroup = group;
            pawn.DeSpawn(); OrganizationFrame(true); GenSpawn.Spawn(pawn, Find.CurrentMap.Center, Find.CurrentMap);
        }
        private static List<TacticalSquadCommand> OverlayCommandCases(Map map, int count)
        {
            var list = new List<TacticalSquadCommand>();
            IntVec3 Cell(int n) => map.Center + new IntVec3(n % 13 - 6, 0, n / 13 % 13 - 6);
            for (int i = 0; i < count; i++)
            {
                var goal = Cell(i % 7); var plan = new TacticalLocalPlan { Opening = goal, Inward = new IntVec3(0, 0, 1), EntryLane = Cell(i + 3), Direct = i % 5 == 0, ReportedPassage = i % 3 == 0 };
                for (int j = 0; j < 13; j++) { plan.Stack.Add(Cell(i + j)); plan.Positions.Add(Cell(i + j + 20)); if (j % 3 == 0) plan.RetainedOutside.Add(j); }
                plan.Stack.Add(IntVec3.Invalid); plan.Stack.Add(new IntVec3(-100, 0, -100));
                var command = new TacticalSquadCommand { Id = "overlay-" + i, Goal = goal, Phase = i % 11 == 0 && i > 0 ? TacticalCommandPhase.Returning : i % 13 == 0 && i > 0 ? TacticalCommandPhase.Released : i % 17 == 0 && i > 0 ? TacticalCommandPhase.Complete : TacticalCommandPhase.Observe, CqbTeam = i % 3 - 1, Plan = plan };
                command.Mission.NextGoal = goal;
                command.FieldResponse = new TacticalFieldResponse { High = i % 2 == 0, LowFlank = i % 3 == 0, Focus = Cell(i + 4), Anchor = Cell(i + 6),
                    FirePoints = new List<IntVec3> { Cell(i + 6), Cell(i + 7), Cell(i + 8) }, Screen = new TacticalFieldSmoke { Target = Cell(i + 10) }, Maneuver = new TacticalFieldManeuver { Waypoints = new List<IntVec3> { Cell(i + 11), Cell(i + 12), Cell(i + 13) } } };
                command.ContactResponse = new TacticalContactResponse { Posts = new List<IntVec3> { Cell(i + 50), Cell(i + 51) } };
                command.OpeningAction = new TacticalOpeningAction { ObservationPosition = Cell(i + 60), Source = Cell(i + 61), Target = Cell(i + 62), ThrowIssued = i % 2 == 0 };
                for (int j = 0; j < 3; j++) command.Members.Add(new TacticalMemberCommand { FieldPostState = j % 2 == 0 ? TacticalFieldPostState.Fixed : TacticalFieldPostState.Unplaced, FieldPost = Cell(i + j + 70) });
                if (count == 80) { command.Plan = null; command.Members.Clear(); } // Dense major nodes explicitly reach the label cap.
                list.Add(command);
            }
            return list;
        }
        private static void OverlayNodes(object a, object e)
        {
            var an = (IList)Get(a, "planOverlayNodes"); var en = (IList)Get(e, "planOverlayNodes");
            if (an.Count != en.Count || !Equals(Get(a, "planOverlayRefreshAt"), Get(e, "planOverlayRefreshAt"))) throw new InvalidOperationException("Tactical overlay node count/cache timing changed.");
            foreach (var pair in an.Cast<object>().Zip(en.Cast<object>()))
                foreach (string f in new[] { "Cell", "Color", "Label", "Major", "LabelRow" }) if (!Equals(Get(pair.First, f), Get(pair.Second, f))) throw new InvalidOperationException("Tactical overlay node identity/order changed: " + f);
            if (an.Count > 256) throw new InvalidOperationException("Tactical overlay exceeded its node cap.");
            maximumNodes = Math.Max(maximumNodes, an.Count); nodeComparisons++;
        }
        private static void PlanOverlayCases(Map map)
        {
            var a = new MapComponent_TacticalCommands(map); overlayService = a; var e = new FrozenOverlays.TacticalPlanOverlay(map);
            foreach (int count in new[] { 0, 1, 10, 40, 80 })
            {
                var commands = OverlayCommandCases(map, count); overlayCommands = commands; e.ExecutionCommands = commands;
                Set(a, "planOverlayRefreshAt", -1); Set(e, "planOverlayRefreshAt", -1);
                foreach (int frame in new[] { 100, 129, 130, 131, 159, 160 })
                {
                    overlayFrame = frame; overlayView = new CellRect(0, 0, map.Size.x, map.Size.z);
                    if (frame == 129 && commands.Count > 0) commands[0].Goal += new IntVec3(1, 0, 0);
                    OverlayComparedFrame(a.MapComponentUpdate, e.MapComponentUpdate); OverlayNodes(a, e); planFrames++;
                    foreach (CellRect view in new[] { overlayView, CellRect.CenteredOn(map.Center, 3), new CellRect(-100, -100, 4, 4) })
                        foreach (IntVec3 mouse in new[] { map.Center, map.Center + new IntVec3(2, 0, 0), new IntVec3(-100, 0, -100) })
                        {
                            overlayView = view; overlayMouse = mouse; projected = new GV(-80.125f, 41.5f); GE.current = new GE { type = ET.Repaint };
                            int labels = 0;
                            OverlayComparedFrame(() => { a.MapComponentOnGUI(); labels = trace.Count(s => s.Kind == "overlay-label"); }, e.MapComponentOnGUI);
                            if (labels > 96) throw new InvalidOperationException("Tactical overlay exceeded its label cap.");
                            if (labels == 96) labelLimitFrames++; planFrames++;
                        }
                }
                GE.current = new GE { type = ET.Layout }; OverlayComparedFrame(a.MapComponentOnGUI, e.MapComponentOnGUI); planFrames++;
                TacticalPlanOverlaySettings.drawTacticalPlanNodes = false; OverlayComparedFrame(a.MapComponentUpdate, e.MapComponentUpdate); OverlayNodes(a, e); planFrames++;
                if (((IList)Get(a, "planOverlayNodes")).Count != 0 || (int)Get(a, "planOverlayRefreshAt") != -1) throw new InvalidOperationException("Disabled tactical overlay retained its cache.");
                TacticalPlanOverlaySettings.drawTacticalPlanNodes = true;
            }
        }
        internal static void RunOverlays(string output, Action<bool, string> check)
        {
            var h = new Harmony("Helodrace.Tests.NativeSquadOverlays");
            h.Patch(AccessTools.PropertyGetter(typeof(Waylith.Time), "FrameCount"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(OverlayClock)));
            h.Patch(AccessTools.PropertyGetter(typeof(PawnOrganizationComponent), "Group"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(OverlayGroup)));
            h.Patch(AccessTools.PropertyGetter(typeof(MapComponent_TacticalCommands), "ExecutionCommands"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(OverlayCommands)));
            h.Patch(AccessTools.PropertyGetter(typeof(CameraDriver), "CurrentViewRect"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(OverlayView)));
            Patch(h, typeof(UI), "MouseCell", nameof(OverlayMouse));
            h.Patch(AccessTools.Method(typeof(GenMapUI), "LabelDrawPosFor", new[] { typeof(Thing), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(PawnProjection)));
            h.Patch(AccessTools.Method(typeof(GenMapUI), "LabelDrawPosFor", new[] { typeof(IntVec3) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(CellProjection)));
            h.Patch(AccessTools.Method(typeof(GenMapUI), "DrawThingLabel", new[] { typeof(GV), typeof(string), typeof(GC) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(OverlayLabel)));
            h.Patch(AccessTools.Method(typeof(GenDraw), "DrawLineBetween", new[] { typeof(V3), typeof(V3), typeof(SimpleColor), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(OverlayLine)));
            foreach (Type t in new[] { typeof(OrganizationOverlay), typeof(FrozenOverlays.OrganizationOverlay) }) h.Patch(AccessTools.Method(t, "Refresh"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(OrganizationRefreshed)));
            foreach (Type t in new[] { typeof(MapComponent_TacticalCommands), typeof(FrozenOverlays.TacticalPlanOverlay) }) h.Patch(AccessTools.Method(t, "RefreshPlanOverlay"), prefix: new HarmonyMethod(typeof(NativeWindowSubmissionChecks), nameof(TacticalRefreshed)));
            bool dev = Prefs.DevMode, org = OrganizationOverlaySettings.drawRaidOrganizationInfo, tactical = TacticalPlanOverlaySettings.drawTacticalPlanNodes; GE original = GE.current;
            Map map = Find.CurrentMap; Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"), Faction.OfPlayer, canGeneratePawnRelations: false)), leader = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"), Faction.OfPlayer, canGeneratePawnRelations: false));
            GenSpawn.Spawn(pawn, map.Center, map); GenSpawn.Spawn(leader, map.Center + new IntVec3(2, 0, 0), map); overlayPawn = pawn.TryGetComp<PawnOrganizationComponent>();
            if (overlayPawn == null) throw new InvalidOperationException("Actual generated Helod pawn lacks the organization overlay component.");
            bool originalFog = map.fogGrid.IsFogged(pawn.Position); map.fogGrid.Unfog(pawn.Position);
            active = overlayActive = true; Prefs.DevMode = true; OrganizationOverlaySettings.drawRaidOrganizationInfo = TacticalPlanOverlaySettings.drawTacticalPlanNodes = true;
            try
            {
                OrganizationCases(pawn, leader); PlanOverlayCases(map);
                check(organizationFrames == 97 && hiddenOrganizationFrames == 6 && organizationRefreshes > 0, "native organization label geometry/palette/cache matches frozen code for commander, acting, ordinary and lost-command states and hidden/fog/despawn gates");
                check(planFrames == 310 && nodeComparisons == 35 && maximumNodes == 256 && labelLimitFrames > 0, "tactical overlay nodes/labels, merged text rows, view/mouse culling and node/label limits match frozen implementation");
                check(tacticalRefreshes == 30 && lineSubmissionsBoth > 0, "native tactical cross-stroke endpoints and 30-frame refresh cadence/disable clearing match frozen game submissions");
            }
            finally { active = overlayActive = false; trace = null; GE.current = original; Prefs.DevMode = dev; OrganizationOverlaySettings.drawRaidOrganizationInfo = org; TacticalPlanOverlaySettings.drawTacticalPlanNodes = tactical; h.UnpatchAll(h.Id); if (originalFog) map.fogGrid.Refog(new CellRect(pawn.Position.x, pawn.Position.z, 1, 1)); else map.fogGrid.Unfog(pawn.Position); pawn.Destroy(); leader.Destroy(); overlayPawn = null; overlayService = null; overlayCommands = Array.Empty<TacticalSquadCommand>(); }
            File.WriteAllText(Path.Combine(output, "native-overlays.json"), JsonSerializer.Serialize(new { Frames = frames, Submissions = widgets, OrganizationFrames = organizationFrames, HiddenOrganizationFrames = hiddenOrganizationFrames, PlanFrames = planFrames, NodeComparisons = nodeComparisons, MaximumNodes = maximumNodes, LabelLimitFrames = labelLimitFrames, OrganizationRefreshesBoth = organizationRefreshes, TacticalRefreshesBoth = tacticalRefreshes, LabelsBoth = labelSubmissionsBoth, LabelPositionBitPairs = overlayLabelBitPairs, LinesBoth = lineSubmissionsBoth, Components = components, MaximumFloatError = maximumError, NonzeroDifferences = differences, Baseline = "6e69db25", ControlledProjection = true, ControlledTimeViewMouseAndEnumeration = true, ActualFogGrid = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
