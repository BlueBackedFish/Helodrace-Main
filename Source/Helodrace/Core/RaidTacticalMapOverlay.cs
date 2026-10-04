using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helodrace.Squads;
using LudeonTK;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace Helodrace
{
    public static class RaidTacticalOverlaySettings
    {
        // Session-only debug View switches. No simulation or save state changes.
        public static bool drawRaidRoomLayout;
        public static bool drawRaidRoomClearance;
        public static bool drawRaidTacticalNodes;
        public static bool drawRaidContacts;
        internal static bool Enabled => drawRaidRoomLayout || drawRaidRoomClearance || drawRaidTacticalNodes || drawRaidContacts;
    }

    [HarmonyPatch(typeof(DebugTabMenu_Settings), "InitActions")]
    public static class Patch_DebugSettings_RaidTacticalOverlay
    {
        public static void Postfix(DebugActionNode __result)
        {
            if (__result == null) return;
            Add(__result, nameof(RaidTacticalOverlaySettings.drawRaidRoomLayout), "HD_RaidView_Layout");
            Add(__result, nameof(RaidTacticalOverlaySettings.drawRaidRoomClearance), "HD_RaidView_Clearance");
            Add(__result, nameof(RaidTacticalOverlaySettings.drawRaidTacticalNodes), "HD_RaidView_Nodes");
            Add(__result, nameof(RaidTacticalOverlaySettings.drawRaidContacts), "HD_RaidView_Contacts");
        }

        private static void Add(DebugActionNode root, string fieldName, string key)
        {
            FieldInfo field = AccessTools.Field(typeof(RaidTacticalOverlaySettings), fieldName);
            if (root.children.Any(child => child.settingsField == field)) return;
            DebugActionNode node = null;
            node = new DebugActionNode(key.Translate(), DebugActionType.Action, () => {
                field.SetValue(null, !(bool)field.GetValue(null));
                node.DirtyLabelCache();
            }) { category = "View", settingsField = field };
            root.AddChild(node);
        }
    }

    internal enum RaidDebugRoomState { Unavailable, Uncleared, CurrentTarget, Cleared }

    // Read-only projection using the selected organization's pinned room IDs.
    // Never consult live Room objects or mutate the progress being inspected.
    internal sealed class RaidRoomDebugData
    {
        internal readonly TacticalStructureVersion Version;
        internal readonly HashSet<int> Cleared = new HashSet<int>();
        internal readonly int CurrentRoom;
        internal readonly bool HasProgress;
        internal RaidRoomDebugData(TacticalStructureVersion version,
            MapComponent_RaidTacticalExecution.ExecutionState state)
        {
            Version = version;
            HasProgress = state != null;
            if (version?.Geometry == null) return;
            if (state?.ClearedRoomCells != null)
                foreach (IntVec3 cell in state.ClearedRoomCells)
                {
                    int room = RoomAt(cell);
                    if (room > 0) Cleared.Add(room);
                }
            CurrentRoom = state?.ActivePlan != null ? RoomAt(state.ActivePlan.Objective) : 0;
        }
        internal int RoomAt(IntVec3 cell)
        {
            TacticalGeometryInput input = Version?.Geometry?.Input;
            return input != null && cell.x >= 0 && cell.z >= 0 && cell.x < input.Width && cell.z < input.Height
                ? input.Cells[cell.x + cell.z * input.Width].Room : 0;
        }
        internal RaidDebugRoomState State(int room) => room <= 0 || !HasProgress ? RaidDebugRoomState.Unavailable
            : Cleared.Contains(room) ? RaidDebugRoomState.Cleared
            : room == CurrentRoom ? RaidDebugRoomState.CurrentTarget : RaidDebugRoomState.Uncleared;
    }

    public sealed class MapComponent_RaidTacticalOverlay : MapComponent
    {
        private const float RefreshSeconds = 0.5f;
        private const int ViewSideLimit = 64;
        private static readonly Color EntryColor = new Color(0.2f, 0.95f, 0.4f);
        private static readonly Color SecurityColor = new Color(0.2f, 0.8f, 1f);
        private static readonly Color ObjectiveColor = new Color(1f, 0.3f, 0.85f);
        private readonly Dictionary<int, List<IntVec3>> roomCells = new Dictionary<int, List<IntVec3>>();
        private readonly Dictionary<int, IntVec3> roomLabels = new Dictionary<int, IntVec3>();
        private readonly Dictionary<IntVec3, Node> nodes = new Dictionary<IntVec3, Node>();
        private readonly List<IntVec3> approach = new List<IntVec3>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Color> colors = new List<Color>();
        private List<CombatOrganization> organizations = new List<CombatOrganization>();
        private RaidRoomDebugData rooms;
        private RaidTacticalPlan plan;
        private Pawn lastSelectedPawn;
        private CellRect view;
        private Mesh mesh;
        private Material material;
        private float refreshAt;
        private bool visibleLastFrame, clipped;
        private int switches;
        private string organizationId, status;

        private sealed class Node
        {
            public readonly List<string> Labels = new List<string>();
            public Color Color;
        }
        public MapComponent_RaidTacticalOverlay(Map map) : base(map) { }
        private bool Visible => Prefs.DevMode && Find.CurrentMap == map && RaidTacticalOverlaySettings.Enabled;

        public override void MapComponentUpdate()
        {
            if (!Visible) { visibleLastFrame = false; return; }
            int enabled = (RaidTacticalOverlaySettings.drawRaidRoomLayout ? 1 : 0)
                | (RaidTacticalOverlaySettings.drawRaidRoomClearance ? 2 : 0)
                | (RaidTacticalOverlaySettings.drawRaidTacticalNodes ? 4 : 0)
                | (RaidTacticalOverlaySettings.drawRaidContacts ? 8 : 0);
            CellRect nextView = Find.CameraDriver.CurrentViewRect.ClipInsideMap(map);
            Pawn selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
            string selectedId = RaidTacticalDebugSession.Map == map ? RaidTacticalDebugSession.SelectedOrganizationId : null;
            if (!visibleLastFrame || Time.unscaledTime >= refreshAt || enabled != switches
                || selectedPawn != lastSelectedPawn || selectedId != organizationId || !nextView.Equals(view))
            {
                view = nextView;
                switches = enabled;
                Refresh(selectedPawn);
                refreshAt = Time.unscaledTime + RefreshSeconds;
            }
            visibleLastFrame = true;
            if (mesh != null && mesh.vertexCount > 0)
                Graphics.DrawMesh(mesh, Vector3.zero, Quaternion.identity, material, 0);
            if (!RaidTacticalOverlaySettings.drawRaidTacticalNodes && !RaidTacticalOverlaySettings.drawRaidContacts) return;
            for (int i = 1; i < approach.Count; i++)
                if (view.Contains(approach[i - 1]) || view.Contains(approach[i]))
                    GenDraw.DrawLineBetween(approach[i - 1].ToVector3Shifted(), approach[i].ToVector3Shifted(), SimpleColor.Red, 0.12f);
            foreach (KeyValuePair<IntVec3, Node> node in nodes)
                CellRenderer.RenderSpot(node.Key.ToVector3Shifted(),
                    SolidColorMaterials.SimpleSolidColorMaterial(node.Value.Color), 0.65f);
        }

        private void Refresh(Pawn selectedPawn)
        {
            organizations = OrganizationAPI.Registry?.Organizations.Where(organization => organization.AllMembers
                .Any(pawn => pawn.Spawned && pawn.Map == map && !pawn.Dead)).ToList() ?? new List<CombatOrganization>();
            if (selectedPawn != lastSelectedPawn && selectedPawn?.Map == map)
            {
                CombatOrganization selected = OrganizationAPI.GetOrganization(selectedPawn);
                if (selected != null) Select(selected.id);
            }
            lastSelectedPawn = selectedPawn;
            string id = RaidTacticalDebugSession.Map == map ? RaidTacticalDebugSession.SelectedOrganizationId : null;
            if (!organizations.Any(organization => organization.id == id)) id = organizations.FirstOrDefault()?.id;
            Select(id);
            var plans = map.GetComponent<MapComponent_RaidTacticalPlans>();
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            var state = execution?.StateFor(id);
            // The string lookup does not create/pin a structure or evaluate a plan.
            TacticalStructureVersion version = id != null ? plans?.GetStructure(id)?.Version
                : map.GetComponent<MapComponent_TacticalMapAnalysis>()?.Completed;
            rooms = new RaidRoomDebugData(version, state);
            plan = state?.ActivePlan ?? plans?.Plans.FirstOrDefault(value => value.OrganizationId == id);
            status = id != null ? execution?.Status(id) : "HD_RaidView_NoRaid".Translate().ToString();
            CaptureNodes(state);
            CaptureContacts(state);
            BuildRoomMesh();
        }

        private void Select(string id)
        {
            organizationId = id;
            RaidTacticalDebugSession.Map = map;
            RaidTacticalDebugSession.SelectedOrganizationId = id;
        }

        private void CaptureNodes(MapComponent_RaidTacticalExecution.ExecutionState state)
        {
            nodes.Clear(); approach.Clear();
            if (!RaidTacticalOverlaySettings.drawRaidTacticalNodes || plan?.Success != true) return;
            AddNode(plan.Start, "HD_RaidView_Start", Color.white);
            AddNode(plan.Entry, "HD_RaidView_Entry", EntryColor);
            AddNode(plan.BreachCell, plan.ReusePassage ? "HD_RaidView_Passage" : "HD_RaidView_Breach", Color.yellow);
            AddNode(plan.BreachInside, "HD_RaidView_Inside", EntryColor);
            AddNode(plan.Objective, "HD_RaidView_Objective", ObjectiveColor);
            AddNode(plan.FinalObjective, "HD_RaidView_Final", ObjectiveColor);
            AddNode(plan.Frontline, "HD_RaidView_Front", Color.red);
            AddNode(plan.Flank, "HD_RaidView_Flank", SecurityColor);
            approach.AddRange(plan.ApproachNodes.Where(cell => cell.InBounds(map)).Take(128));
            for (int i = 0; i < approach.Count; i++) AddNode(approach[i], "A" + (i + 1), Color.red, false);
            foreach (RaidTacticalAssignment assignment in plan.Assignments.Take(100))
            {
                string label = assignment.Pawn?.LabelShort ?? "?";
                AddNode(assignment.Position, label + ": " + assignment.Task
                    + (assignment.EntryOrder > 0 ? " #" + assignment.EntryOrder : ""),
                    assignment.Task == RaidTacticalTask.Entry ? EntryColor : SecurityColor, false);
                RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(assignment.Pawn);
                if (order != null) AddNode(order.Destination, label + ": " + order.Kind, Color.cyan, false);
            }
            if (state == null) return;
            AddNode(state.ApproachSmokeActive ? state.ApproachSmokeTarget : IntVec3.Invalid, "HD_RaidView_Smoke", Color.white);
            foreach (var crossing in state.Crossings.Take(100))
                AddNode(crossing.Destination, (crossing.Pawn?.LabelShort ?? "?") + ": " + crossing.Progress, EntryColor, false);
        }

        private void CaptureContacts(MapComponent_RaidTacticalExecution.ExecutionState state)
        {
            if (!RaidTacticalOverlaySettings.drawRaidContacts || state?.Contacts == null) return;
            int tick = GenTicks.TicksGame;
            foreach (RaidEnemyContact contact in state.Contacts.Entries)
            {
                RaidContactConfidence confidence = contact.Confidence(tick);
                if (confidence == RaidContactConfidence.Expired) continue;
                Color color = confidence == RaidContactConfidence.Visible ? Color.red
                    : confidence == RaidContactConfidence.Recent ? Color.yellow : Color.gray;
                AddNode(contact.Position, $"#{contact.EnemyId} {contact.Label} {confidence} "
                    + $"{(tick - contact.SeenTick) / 60f:0.0}s R{contact.Room}", color, false);
                AddNode(contact.Portal, "HD_RaidView_ContactPortal", Color.yellow);
            }
        }

        private void AddNode(IntVec3 cell, string label, Color color, bool translate = true)
        {
            if (!cell.InBounds(map) || !view.Contains(cell)) return;
            if (!nodes.TryGetValue(cell, out Node node)) nodes[cell] = node = new Node { Color = color };
            string text = translate ? label.Translate().ToString() : label;
            if (!node.Labels.Contains(text)) node.Labels.Add(text);
        }

        private static Color RoomColor(int room) => Color.HSVToRGB((room * 0.618034f) % 1f, 0.55f, 0.9f);
        private static Color StateColor(RaidDebugRoomState state) => state == RaidDebugRoomState.Cleared ? EntryColor
            : state == RaidDebugRoomState.CurrentTarget ? Color.yellow
            : state == RaidDebugRoomState.Uncleared ? new Color(1f, 0.3f, 0.25f) : Color.gray;

        private void BuildRoomMesh()
        {
            vertices.Clear(); triangles.Clear(); colors.Clear(); roomCells.Clear(); roomLabels.Clear();
            if (mesh != null) mesh.Clear();
            if (rooms.Version?.Geometry == null || (switches & 3) == 0) return;
            CellRect area = view;
            clipped = area.Width > ViewSideLimit || area.Height > ViewSideLimit;
            if (clipped) area = CellRect.CenteredOn(view.CenterCell, ViewSideLimit, ViewSideLimit).ClipInsideMap(map);
            TacticalGeometryInput input = rooms.Version.Geometry.Input;
            foreach (IntVec3 cell in area)
            {
                TacticalRawCell raw = input.Cells[map.cellIndices.CellToIndex(cell)];
                int room = raw.Room;
                if (room > 0)
                {
                    Color fill = RaidTacticalOverlaySettings.drawRaidRoomClearance ? StateColor(rooms.State(room)) : RoomColor(room);
                    Quad(cell.x, cell.z, cell.x + 1, cell.z + 1, fill, 0.20f);
                    if (!roomCells.TryGetValue(room, out List<IntVec3> cells)) roomCells[room] = cells = new List<IntVec3>();
                    cells.Add(cell);
                    Color edge = RaidTacticalOverlaySettings.drawRaidRoomLayout ? RoomColor(room) : fill;
                    if (rooms.RoomAt(cell + IntVec3.North) != room) Quad(cell.x, cell.z + 0.92f, cell.x + 1, cell.z + 1, edge, 0.85f);
                    if (rooms.RoomAt(cell + IntVec3.East) != room) Quad(cell.x + 0.92f, cell.z, cell.x + 1, cell.z + 1, edge, 0.85f);
                    if (rooms.RoomAt(cell + IntVec3.South) != room) Quad(cell.x, cell.z, cell.x + 1, cell.z + 0.08f, edge, 0.85f);
                    if (rooms.RoomAt(cell + IntVec3.West) != room) Quad(cell.x, cell.z, cell.x + 0.08f, cell.z + 1, edge, 0.85f);
                }
                else if (RaidTacticalOverlaySettings.drawRaidRoomLayout && (raw.Has(TacticalRawFlags.WallLine) || raw.Has(TacticalRawFlags.Door)))
                    Quad(cell.x, cell.z, cell.x + 1, cell.z + 1,
                        raw.Has(TacticalRawFlags.Door) ? Color.yellow : Color.white, 0.25f);
            }
            if (mesh == null) mesh = new Mesh { name = "Helodrace raid room debug", indexFormat = IndexFormat.UInt32 };
            if (material == null) material = SolidColorMaterials.SimpleSolidColorMaterial(Color.white, true);
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetColors(colors);
            foreach (var room in roomCells.Take(100))
            {
                IntVec3 center = new IntVec3((int)room.Value.Average(cell => cell.x), 0, (int)room.Value.Average(cell => cell.z));
                roomLabels[room.Key] = room.Value.OrderBy(cell => cell.DistanceToSquared(center)).First();
            }
        }

        private void Quad(float x1, float z1, float x2, float z2, Color color, float alpha)
        {
            float y = AltitudeLayer.MetaOverlays.AltitudeFor();
            int i = vertices.Count;
            vertices.Add(new Vector3(x1, y, z1)); vertices.Add(new Vector3(x1, y, z2));
            vertices.Add(new Vector3(x2, y, z2)); vertices.Add(new Vector3(x2, y, z1));
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
            color.a = alpha;
            for (int c = 0; c < 4; c++) colors.Add(color);
        }

        public override void MapComponentOnGUI()
        {
            if (!Visible || rooms == null) return;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            if ((switches & 3) != 0)
                foreach (KeyValuePair<int, IntVec3> room in roomLabels)
                {
                    string label = "R" + room.Key;
                    if (RaidTacticalOverlaySettings.drawRaidRoomClearance) label += " · " + RoomStateLabel(rooms.State(room.Key));
                    GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(room.Value), label, Color.white);
                }
            if (RaidTacticalOverlaySettings.drawRaidTacticalNodes || RaidTacticalOverlaySettings.drawRaidContacts)
                foreach (KeyValuePair<IntVec3, Node> node in nodes)
                {
                    Vector2 position = GenMapUI.LabelDrawPosFor(node.Key);
                    string label = string.Join(" / ", node.Value.Labels.Take(3)) + (node.Value.Labels.Count > 3 ? " +" : "");
                    if (label.Length > 90) label = label.Substring(0, 87) + "…";
                    GenMapUI.DrawThingLabel(position, label, node.Value.Color);
                    TooltipHandler.TipRegion(new Rect(position.x - 70, position.y - 12, 140, 24), string.Join("\n", node.Value.Labels));
                }
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            DrawPanel();
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private static string RoomStateLabel(RaidDebugRoomState state) => (state == RaidDebugRoomState.Cleared ? "HD_RaidView_Cleared"
            : state == RaidDebugRoomState.CurrentTarget ? "HD_RaidView_Current"
            : state == RaidDebugRoomState.Uncleared ? "HD_RaidView_Uncleared" : "HD_RaidView_Unknown").Translate();

        private void DrawPanel()
        {
            Rect panel = new Rect(12, 100, 410, 150);
            Widgets.DrawWindowBackground(panel);
            Widgets.Label(new Rect(22, 106, 390, 22), "HD_RaidView_Title".Translate(organizationId ?? "—"));
            string cache = rooms.Version != null ? "v" + rooms.Version.Id : organizationId != null
                ? "HD_RaidView_Waiting".Translate().ToString() : map.GetComponent<MapComponent_TacticalMapAnalysis>()?.BuildStatus;
            Widgets.Label(new Rect(22, 128, 390, 22), "HD_RaidView_Summary".Translate(cache, status ?? "—", rooms.Cleared.Count));
            Widgets.Label(new Rect(22, 151, 390, 40), "HD_RaidView_Legend".Translate());
            Widgets.Label(new Rect(22, 191, 390, 20), clipped && (switches & 3) != 0
                ? "HD_RaidView_Clipped".Translate() : "HD_RaidView_Refresh".Translate());
            if (Widgets.ButtonText(new Rect(22, 215, 125, 26), "HD_RaidView_Next".Translate()) && organizations.Count > 0)
            {
                int index = organizations.FindIndex(value => value.id == organizationId);
                Select(organizations[(index + 1) % organizations.Count].id);
                refreshAt = 0;
            }
            if (Widgets.ButtonText(new Rect(157, 215, 125, 26), "HD_RaidView_Details".Translate())) RaidTacticalDebugSession.Open(map);
        }

        public override void MapRemoved()
        {
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
            mesh = null; material = null;
            nodes.Clear(); roomCells.Clear(); roomLabels.Clear();
            base.MapRemoved();
        }
    }
}
