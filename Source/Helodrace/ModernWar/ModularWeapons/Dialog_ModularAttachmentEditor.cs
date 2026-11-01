using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Waylith.Math;
using Rect = Waylith.Math.Rect;
using Color = Waylith.Math.Color;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularAttachmentEditor : Window
    {
        private enum EditTarget
        {
            Graphic,
            Mount,
            Socket,
            Flashlight,
            Laser,
            Sight,
            AnimationPart,
            CasingPort,
            FeedStart,
            FeedEnd
        }

        private enum EditorSection
        {
            Placement,
            Animation
        }

        private enum MovementConstraint
        {
            Free,
            Horizontal,
            Vertical
        }

        private CompModularWeaponNode root;
        private string selectedPath = "root";
        private int selectedMountIndex;
        private int selectedSocketIndex;
        private EditTarget target = EditTarget.Socket;
        private EditorSection section = EditorSection.Placement;
        private MovementConstraint movementConstraint = MovementConstraint.Free;
        private Vector2 treeScroll;
        private Vector2 inspectorScroll;
        private float inspectorContentHeight = 1000f;
        private string inspectorScrollContext;
        private readonly Dictionary<string, string> numericBuffers =
            new Dictionary<string, string>();
        private readonly Dictionary<string, string> textBuffers =
            new Dictionary<string, string>();
        private float zoom = 1f;
        private Vector2 pan;
        private readonly List<EditorSnapshot> undoHistory = new List<EditorSnapshot>();
        private string activeEditKey;

        public Dialog_ModularAttachmentEditor(CompModularWeaponNode root)
        {
            this.root = root;
            workspace = Current.Game?.GetComponent<GameComponent_ModularPlacementWorkspace>()?.Workspace
                ?? new ModularPlacementWorkspace();
            workspace.Visit(root);
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
        }

        public override UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(1380f, 850f);

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            HandleUndoInput();
            if (Event.current.type == EventType.MouseUp) activeEditKey = null;
            DrawWorkspaceControls(inRect);

            if (root?.parent == null || root.parent.Destroyed)
            {
                Widgets.Label(new Rect(0f, 112f, inRect.width, 40f).ToGame(), "The edited weapon no longer exists. Switch to another weapon above.");
                return;
            }

            List<ModularRenderNode> snapshot = root.RenderSnapshot();
            ModularRenderNode selected = FindSelected(snapshot);
            float header = 112f;
            Rect treeRect = new Rect(0f, header, 290f, inRect.height - header - 46f);
            Rect previewRect = new Rect(302f, header, inRect.width - 682f,
                inRect.height - header - 46f);
            Rect inspectorRect = new Rect(inRect.width - 368f, header, 368f,
                inRect.height - header - 46f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f).ToGame(),
                "Modular attachment editor — " + root.parent.LabelCap);
            Text.Font = GameFont.Small;

            float sectionWidth = 150f;
            if (ModeButton(new Rect(0f, 36f, sectionWidth, 30f), "Placement",
                section == EditorSection.Placement))
                SetSection(EditorSection.Placement, selected);
            if (ModeButton(new Rect(sectionWidth + 8f, 36f, sectionWidth, 30f),
                "Animation", section == EditorSection.Animation))
                SetSection(EditorSection.Animation, selected);

            Rect liveRect = new Rect(inRect.width - 190f, 2f, 188f, 29f);
            GUI.color = new Color(0.55f, 1f, 0.58f).ToGame();
            if (Widgets.ButtonText(liveRect.ToGame(), "LIVE APPLY • refresh now"))
                root.InvalidateTree();
            GUI.color = Color.white.ToGame();

            DrawTree(treeRect, snapshot);
            DrawPreview(previewRect, snapshot, selected);
            DrawInspector(inspectorRect, selected);

            Rect footer = new Rect(0f, inRect.height - 38f, inRect.width, 38f);
            if (Widgets.ButtonText(new Rect(footer.x, footer.y + 4f, 110f, 30f).ToGame(),
                "Copy XML"))
            {
                string xml = BuildXml(snapshot);
                GUIUtility.systemCopyBuffer = xml;
                Log.Message("[Helodrace] Modular attachment editor export:\n" + xml);
                Messages.Message("Modular attachment XML copied to clipboard.",
                    MessageTypeDefOf.PositiveEvent, false);
            }

            if (Widgets.ButtonText(new Rect(footer.x + 118f, footer.y + 4f, 130f, 30f).ToGame(),
                "HD_Placement_CopyBatch".Translate())) CopyBatch();
            if (Widgets.ButtonText(new Rect(footer.x + 256f, footer.y + 4f, 130f, 30f).ToGame(),
                "HD_Placement_SaveBatch".Translate())) SaveBatch();

            if (Widgets.ButtonText(new Rect(footer.x + 394f, footer.y + 4f, 105f, 30f).ToGame(),
                "Reset view"))
            {
                zoom = 1f;
                pan = Vector2.Zero;
            }

            if (Widgets.ButtonText(new Rect(footer.x + 507f, footer.y + 4f, 145f, 30f).ToGame(),
                "Undo (Ctrl+Z) " + undoHistory.Count))
                UndoLastEdit();

            Widgets.Label(new Rect(footer.x + 664f, footer.y + 8f,
                Mathf.Max(1f, footer.width - 796f), 26f).ToGame(), "HD_Placement_MultiHint".Translate());

            if (Widgets.ButtonText(new Rect(footer.xMax - 120f, footer.y + 4f, 120f, 30f).ToGame(),
                "CloseButton".Translate()))
                Close();
        }

        private void DrawTree(Rect rect, List<ModularRenderNode> snapshot)
        {
            Widgets.DrawMenuSection(rect.ToGame());
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f).ToGame(),
                "Assembly tree");

            Rect outRect = new Rect(rect.x + 4f, rect.y + 34f, rect.width - 8f, rect.height - 38f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f,
                Mathf.Max(outRect.height, snapshot.Count * 30f + 8f));
            UnityEngine.Vector2 gameScroll = treeScroll.ToGame();
            Widgets.BeginScrollView(outRect.ToGame(), ref gameScroll, viewRect.ToGame());
            treeScroll = gameScroll.ToNative();

            List<ModularRenderNode> treeOrder = new List<ModularRenderNode>(snapshot);
            treeOrder.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            float y = 4f;
            for (int i = 0; i < treeOrder.Count; i++)
            {
                ModularRenderNode node = treeOrder[i];
                Rect row = new Rect(4f + node.depth * 15f, y,
                    viewRect.width - 8f - node.depth * 15f, 25f);
                if (node.path == selectedPath || selectedPaths.Contains(node.path)) Widgets.DrawHighlightSelected(row.ToGame());
                if (Widgets.ButtonInvisible(row.ToGame()))
                {
                    SelectNode(node, Event.current.control || Event.current.command);
                }

                Widgets.Label(new Rect(row.x+4f,row.y+2f,row.width-8f,row.height-4f).ToGame(),
                    (node.depth == 0 ? "◆ " : "└ ") + node.thing.LabelCap);
                y += 30f;
            }

            Widgets.EndScrollView();
        }

        private void DrawPreview(
            Rect rect,
            List<ModularRenderNode> snapshot,
            ModularRenderNode selected)
        {
            Widgets.DrawBoxSolidWithOutline(rect.ToGame(),new Color(0.055f, 0.06f, 0.07f, 1f).ToGame(),new Color(0.35f, 0.38f, 0.42f).ToGame(), 2);

            Rect inner = UiMath.Inset(rect, 14f);
            HandlePreviewNavigation(inner);
            Vector2 origin = inner.center + pan;
            Graphic rootGraphic = root.parent.Graphic;
            Vector2 rootSize = rootGraphic?.drawSize.ToNative() ?? Vector2.One;
            float cells = Mathf.Max(rootSize.X, rootSize.Y, 0.01f);
            float pixelsPerCell = Mathf.Min(inner.width, inner.height) * 0.86f * zoom / cells;

            var visualSnapshot = ModularWeaponVisualLayers.Expand(snapshot);
            // The production renderer places every _Outline texture below the complete
            // assembly. Mirror that ordering here before drawing any normal part texture.
            for (int i = 0; i < visualSnapshot.Count; i++)
            {
                ModularRenderNode node = visualSnapshot[i];
                if (!ModularWeaponAssemblyRenderer.ShouldDrawNode(node)) continue;
                Graphic graphic = node.Graphic;
                string texturePath = node.TexturePath;
                Texture2D outline = texturePath.NullOrEmpty()
                    ? null
                    : ContentFinder<Texture2D>.Get(texturePath + "_Outline", false);
                if (graphic == null || outline == null) continue;

                ModularWeaponAssemblyRenderer.RealtimeNodeGeometry(
                    root, node, false, out Vector2 posedCenter, out float posedAngle);
                Vector2 center = WorldToGui(posedCenter, origin, pixelsPerCell);
                var graphicScale = node.GraphicScale;
                Vector2 size = graphic.drawSize.ToNative() *
                    new Vector2(Mathf.Abs(graphicScale.X), Mathf.Abs(graphicScale.Y)) * pixelsPerCell;
                Rect drawRect = CenteredRect(center, size);
                Matrix4x4 matrix = GUI.matrix;
                if (!Mathf.Approximately(posedAngle, 0f))
                    UI.RotateAroundPivot(-posedAngle, drawRect.center.ToGame());
                DrawTexture(drawRect, outline, node.GraphicVerticallyFlipped);
                GUI.matrix = matrix;
            }

            for (int i = 0; i < visualSnapshot.Count; i++)
            {
                ModularRenderNode node = visualSnapshot[i];
                if (!ModularWeaponAssemblyRenderer.ShouldDrawNode(node)) continue;
                Graphic graphic = node.Graphic;
                Texture texture = graphic?.MatSingle?.mainTexture;
                if (texture == null) continue;

                ModularWeaponAssemblyRenderer.RealtimeNodeGeometry(
                    root, node, false, out Vector2 posedCenter, out float posedAngle);
                Vector2 center = WorldToGui(posedCenter, origin, pixelsPerCell);
                var graphicScale = node.GraphicScale;
                Vector2 size = graphic.drawSize.ToNative() *
                    new Vector2(Mathf.Abs(graphicScale.X), Mathf.Abs(graphicScale.Y)) * pixelsPerCell;
                Rect drawRect = CenteredRect(center, size);
                Color old = GUI.color.ToNative();
                GUI.color = ((node.path == selectedPath || selectedPaths.Contains(node.path))
                    ? graphic.Color.ToNative()
                    : new Color(graphic.Color.r, graphic.Color.g, graphic.Color.b, 0.72f)).ToGame();
                Matrix4x4 matrix = GUI.matrix;
                if (!Mathf.Approximately(posedAngle, 0f))
                    UI.RotateAroundPivot(-posedAngle, drawRect.center.ToGame());
                DrawTexture(drawRect, texture, node.GraphicVerticallyFlipped);
                GUI.matrix = matrix;
                GUI.color = old.ToGame();
            }

            if (target == EditTarget.Sight)
                DrawSightGroupGuides(inner, origin, pixelsPerCell);

            if (selected != null)
            {
                DrawMarkers(selected, origin, pixelsPerCell);
                HandleMarkerDrag(inner, selected, pixelsPerCell);
            }

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x + 4f, inner.y + 4f, inner.width - 8f, 22f).ToGame(),
                "Wheel: zoom   Middle drag: pan   Left drag: move   Shift: temporary axis lock   "
                + AxisLabel() + "   "
                + zoom.ToString("0.00", CultureInfo.InvariantCulture) + "x");
            Text.Font = GameFont.Small;
        }

        private void DrawSightGroupGuides(
            Rect rect,
            Vector2 origin,
            float pixelsPerCell)
        {
            IReadOnlyList<ModularSightGroupStatus> groups = root.SightGroups;
            for (int i = 0; i < groups.Count; i++)
            {
                ModularSightGroupStatus group = groups[i];
                float lineY = WorldToGuiAxes(
                    new Vector2(0f, group.axisHeight),
                    origin,
                    pixelsPerCell).Y;
                Color color = group.isActive
                    ? new Color(0.25f, 1f, 0.35f, 0.85f)
                    : group.belowWeaponSightCount == group.sightCount
                        ? new Color(1f, 0.3f, 0.25f, 0.8f)
                        : new Color(0.55f, 0.55f, 0.65f, 0.65f);
                Widgets.DrawLine((
                    new Vector2(rect.x, lineY)).ToGame(),(
                    new Vector2(rect.xMax, lineY)).ToGame(),color.ToGame(),
                    group.isActive ? 2f : 1f);
                Text.Font = GameFont.Tiny;
                GUI.color = color.ToGame();
                Widgets.Label(new Rect(rect.x + 4f, lineY - 18f, 260f, 18f).ToGame(),
                    "Group " + (i + 1) + (group.isActive ? " ACTIVE" : string.Empty)
                    + (group.belowWeaponSightCount == group.sightCount
                        ? " DISABLED" : string.Empty)
                    + "  H " + group.axisHeight.ToString("0.###",
                        CultureInfo.InvariantCulture)
                    + "  " + group.efficiency.ToString("P0"));
                GUI.color = Color.white.ToGame();
                Text.Font = GameFont.Small;
            }
        }

        private void DrawMarkers(ModularRenderNode node, Vector2 origin, float pixelsPerCell)
        {
            if (node.attachedToRail)
            {
                Vector2 railStart = WorldToGui(node.railStart, origin, pixelsPerCell);
                Vector2 railEnd = WorldToGui(node.railEnd, origin, pixelsPerCell);
                Vector2 occupiedStart = WorldToGui(
                    node.occupiedRailStart, origin, pixelsPerCell);
                Vector2 occupiedEnd = WorldToGui(
                    node.occupiedRailEnd, origin, pixelsPerCell);
                Widgets.DrawLine((railStart).ToGame(),( railEnd).ToGame(),new Color(0.15f, 0.65f, 1f).ToGame(), 4f);
                Widgets.DrawLine((occupiedStart).ToGame(),( occupiedEnd).ToGame(),Color.yellow.ToGame(), 7f);
                DrawCross(occupiedStart, Color.yellow, 4f);
                DrawCross(occupiedEnd, Color.yellow, 4f);
            }

            Vector2 logical = WorldToGui(node.transform.position, origin, pixelsPerCell);
            DrawCross(logical, Color.red, 7f);

            Vector2 graphic = WorldToGui(node.GraphicCenter, origin, pixelsPerCell);
            DrawCross(graphic, target == EditTarget.Graphic ? Color.yellow : Color.white, 6f);

            ModularWeaponFlashlightProperties flashlight = node.Props.flashlight;
            if (section == EditorSection.Placement && flashlight != null)
            {
                Vector3 emitterLocal = node.Props.graphicOffset.ToNative() + flashlight.emitterOffset.ToNative();
                var emitterWorld = node.transform.TransformPoint(emitterLocal);
                Vector2 emitter = WorldToGui(emitterWorld, origin, pixelsPerCell);
                Color lightColor = target == EditTarget.Flashlight
                    ? Color.yellow
                    : new Color(1f, 0.9f, 0.35f);
                DrawCross(emitter, lightColor, 7f);

                float previewLength = Mathf.Min(1.4f, flashlight.maxRange);
                Vector3 farTopLocal = emitterLocal
                    + new Vector3(previewLength, 0f, flashlight.farWidth * 0.5f);
                Vector3 farBottomLocal = emitterLocal
                    + new Vector3(previewLength, 0f, -flashlight.farWidth * 0.5f);
                Vector2 farTop = WorldToGui(node.transform.TransformPoint(farTopLocal),
                    origin, pixelsPerCell);
                Vector2 farBottom = WorldToGui(node.transform.TransformPoint(farBottomLocal),
                    origin, pixelsPerCell);
                Widgets.DrawLine((emitter).ToGame(),( farTop).ToGame(),( lightColor).ToGame(), 2f);
                Widgets.DrawLine((emitter).ToGame(),( farBottom).ToGame(),( lightColor).ToGame(), 2f);
                Widgets.DrawLine((farTop).ToGame(),( farBottom).ToGame(),( lightColor).ToGame(), 2f);
            }

            ModularWeaponLaserProperties laser = node.Props.laser;
            if (section == EditorSection.Placement && laser != null)
            {
                Vector3 emitterLocal = node.Props.graphicOffset.ToNative() + laser.emitterOffset.ToNative();
                var emitterWorld = node.transform.TransformPoint(emitterLocal);
                Vector2 emitter = WorldToGui(emitterWorld, origin, pixelsPerCell);
                Color laserColor = target == EditTarget.Laser
                    ? Color.yellow
                    : laser.color.ToNative();
                DrawCross(emitter, laserColor, 7f);
                Vector2 endpoint = WorldToGui(
                    node.transform.TransformPoint(emitterLocal
                        + new Vector3(Mathf.Min(1.4f, laser.maxLength), 0f, 0f)),
                    origin,
                    pixelsPerCell);
                Widgets.DrawLine((emitter).ToGame(),( endpoint).ToGame(),( laserColor).ToGame(),
                    Mathf.Max(1f, laser.beamWidth * pixelsPerCell));
            }

            if (section == EditorSection.Placement
                && node.Props.verticalOccupancy > 0f)
            {
                float half = node.Props.verticalOccupancy * 0.5f;
                Vector3 lowLocal = node.Props.graphicOffset.ToNative() + new Vector3(
                    0f, 0f, node.Props.verticalOccupancyOffset - half);
                Vector3 highLocal = node.Props.graphicOffset.ToNative() + new Vector3(
                    0f, 0f, node.Props.verticalOccupancyOffset + half);
                Vector2 low = WorldToGui(node.transform.TransformPoint(lowLocal),
                    origin, pixelsPerCell);
                Vector2 high = WorldToGui(node.transform.TransformPoint(highLocal),
                    origin, pixelsPerCell);
                Color occupancyColor = target == EditTarget.Sight
                    ? new Color(0.2f, 1f, 0.75f)
                    : new Color(0.2f, 0.75f, 0.6f, 0.8f);
                Widgets.DrawLine((low).ToGame(),( high).ToGame(),( occupancyColor).ToGame(), 5f);
                DrawCross(low, occupancyColor, 4f);
                DrawCross(high, occupancyColor, 4f);
            }

            ModularWeaponSightProperties sight = node.Props.sight;
            if (section == EditorSection.Placement && sight != null)
            {
                Vector3 axisLocal = node.Props.graphicOffset.ToNative() + sight.axisOffset.ToNative();
                Vector2 axis = WorldToGui(node.transform.TransformPoint(axisLocal),
                    origin, pixelsPerCell);
                Vector3 lowLocal = axisLocal
                    + new Vector3(0f, 0f, -sight.sightClearanceRadius);
                Vector3 highLocal = axisLocal
                    + new Vector3(0f, 0f, sight.sightClearanceRadius);
                Vector2 low = WorldToGui(node.transform.TransformPoint(lowLocal),
                    origin, pixelsPerCell);
                Vector2 high = WorldToGui(node.transform.TransformPoint(highLocal),
                    origin, pixelsPerCell);
                Color sightColor = target == EditTarget.Sight
                    ? Color.yellow
                    : new Color(0.95f, 0.35f, 1f);
                Widgets.DrawLine((low).ToGame(),( high).ToGame(),( sightColor).ToGame(), 3f);
                Widgets.DrawLine((axis).ToGame(),( axis + new Vector2(80f, 0f)).ToGame(),( sightColor).ToGame(), 2f);
                DrawCross(axis, sightColor, 7f);
            }

            if (section == EditorSection.Animation
                && node.Props.animatedPart != ModularWeaponAnimatedPartKind.None)
            {
                Vector3 motionStartLocal = node.Props.graphicOffset.ToNative();
                Vector3 motionEndLocal = node.Props.graphicOffset.ToNative()
                    + node.Props.animationTravel.ToNative();
                Vector3 pivotLocal = node.Props.graphicOffset.ToNative()
                    + node.Props.animationPivot.ToNative();
                Vector2 motionStart = WorldToGui(
                    node.transform.TransformPoint(motionStartLocal),
                    origin,
                    pixelsPerCell);
                Vector2 motionEnd = WorldToGui(
                    node.transform.TransformPoint(motionEndLocal),
                    origin,
                    pixelsPerCell);
                Vector2 pivot = WorldToGui(
                    node.transform.TransformPoint(pivotLocal),
                    origin,
                    pixelsPerCell);
                Color motionColor = target == EditTarget.AnimationPart
                    ? Color.yellow
                    : new Color(1f, 0.65f, 0.15f);
                Widgets.DrawLine((motionStart).ToGame(),( motionEnd).ToGame(),( motionColor).ToGame(), 5f);
                DrawCross(motionStart, new Color(0.95f, 0.5f, 0.1f), 5f);
                DrawCross(motionEnd, motionColor, 8f);
                DrawCross(pivot, new Color(0.75f, 0.35f, 1f), 5f);
                Text.Font = GameFont.Tiny;
                GUI.color = (motionColor).ToGame();
                Widgets.Label(new Rect(motionEnd.X + 7f, motionEnd.Y - 18f,
                    100f, 18f).ToGame(), "motion end");
                GUI.color = Color.white.ToGame();
                Text.Font = GameFont.Small;
            }

            ModularWeaponCasingPortProperties casingPort = node.Props.casingPort;
            if (section == EditorSection.Animation && casingPort != null)
            {
                Vector3 casingLocal = node.Props.graphicOffset.ToNative() + casingPort.offset.ToNative();
                Vector2 casing = WorldToGui(
                    node.transform.TransformPoint(casingLocal),
                    origin,
                    pixelsPerCell);
                Vector3 startLocal = node.Props.graphicOffset.ToNative()
                    + casingPort.feedStartOffset.ToNative();
                Vector3 endLocal = node.Props.graphicOffset.ToNative()
                    + casingPort.feedEndOffset.ToNative();
                Vector2 start = WorldToGui(
                    node.transform.TransformPoint(startLocal),
                    origin,
                    pixelsPerCell);
                Vector2 end = WorldToGui(
                    node.transform.TransformPoint(endLocal),
                    origin,
                    pixelsPerCell);
                Color startColor = target == EditTarget.FeedStart
                    ? Color.yellow
                    : new Color(0.25f, 1f, 0.85f);
                Color endColor = target == EditTarget.FeedEnd
                    ? Color.yellow
                    : new Color(1f, 0.35f, 0.85f);
                Color casingColor = target == EditTarget.CasingPort
                    ? Color.yellow
                    : new Color(0.35f, 0.75f, 1f);
                Widgets.DrawLine((start).ToGame(),( end).ToGame(),new Color(0.75f, 0.8f, 1f).ToGame(), 3f);
                DrawCross(casing, casingColor, 8f);
                DrawCross(start, startColor, 7f);
                DrawCross(end, endColor, 7f);
                Text.Font = GameFont.Tiny;
                GUI.color = (startColor).ToGame();
                Widgets.Label(new Rect(start.X + 7f, start.Y - 18f, 90f, 18f).ToGame(),
                    "feed start");
                GUI.color = (endColor).ToGame();
                Widgets.Label(new Rect(end.X + 7f, end.Y - 18f, 90f, 18f).ToGame(),
                    "feed end");
                GUI.color = (casingColor).ToGame();
                Widgets.Label(new Rect(casing.X + 7f, casing.Y - 18f, 100f, 18f).ToGame(),
                    "casing port");
                GUI.color = Color.white.ToGame();
                Text.Font = GameFont.Small;
            }

            for (int i = 0; section == EditorSection.Placement
                && i < node.Props.mounts.Count; i++)
            {
                ModularAttachmentMount mount = node.Props.mounts[i];
                Vector2 point = WorldToGui(node.transform.TransformPoint(mount.transform.position.ToNative()),
                    origin, pixelsPerCell);
                Color color = target == EditTarget.Mount && i == selectedMountIndex
                    ? Color.yellow : new Color(1f, 0.45f, 0.08f);
                if (mount.EffectivePhysicalLength > 0f)
                {
                    Vector3 leftLocal = mount.transform.position.ToNative()
                        + new Vector3(mount.physicalCenterOffset, 0f, 0f);
                    Vector3 rightLocal = leftLocal;
                    leftLocal.X -= mount.EffectivePhysicalLength * 0.5f;
                    rightLocal.X += mount.EffectivePhysicalLength * 0.5f;
                    Vector2 left = WorldToGui(node.transform.TransformPoint(leftLocal),
                        origin, pixelsPerCell);
                    Vector2 right = WorldToGui(node.transform.TransformPoint(rightLocal),
                        origin, pixelsPerCell);
                    Widgets.DrawLine((left).ToGame(),( right).ToGame(),color.ToGame(),
                        target == EditTarget.Mount && i == selectedMountIndex ? 7f : 3f);
                    DrawCross(left, color, 3f);
                    DrawCross(right, color, 3f);
                }
                if (mount.EffectiveRailContactLength > 0f)
                {
                    Vector3 leftLocal = mount.transform.position.ToNative();
                    Vector3 rightLocal = leftLocal;
                    leftLocal.X -= mount.EffectiveRailContactLength * 0.5f;
                    rightLocal.X += mount.EffectiveRailContactLength * 0.5f;
                    Widgets.DrawLine((
                        WorldToGui(node.transform.TransformPoint(leftLocal),
                            origin, pixelsPerCell)).ToGame(),(
                        WorldToGui(node.transform.TransformPoint(rightLocal),
                            origin, pixelsPerCell)).ToGame(),new Color(0.25f, 0.9f, 0.55f).ToGame(), 2f);
                }
                Widgets.DrawBox(new Rect(point.X - 5f, point.Y - 5f, 10f, 10f).ToGame(), 2, null);
                GUI.color = color.ToGame();
                Widgets.DrawBox(new Rect(point.X - 4f, point.Y - 4f, 8f, 8f).ToGame(), 2, null);
                GUI.color = Color.white.ToGame();
            }

            for (int i = 0; section == EditorSection.Placement
                && i < node.Props.sockets.Count; i++)
            {
                ModularAttachmentSocket socket = node.Props.sockets[i];
                Vector2 point = WorldToGui(node.transform.TransformPoint(socket.transform.position.ToNative()),
                    origin, pixelsPerCell);
                Color color = target == EditTarget.Socket && i == selectedSocketIndex
                    ? Color.yellow : new Color(0.15f, 0.65f, 1f);
                if (socket.isRail && socket.railLength > 0f)
                {
                    Vector3 leftLocal = socket.transform.position.ToNative();
                    Vector3 rightLocal = socket.transform.position.ToNative();
                    leftLocal.X -= socket.railLength * 0.5f;
                    rightLocal.X += socket.railLength * 0.5f;
                    Vector2 left = WorldToGui(node.transform.TransformPoint(leftLocal),
                        origin, pixelsPerCell);
                    Vector2 right = WorldToGui(node.transform.TransformPoint(rightLocal),
                        origin, pixelsPerCell);
                    Widgets.DrawLine((left).ToGame(),( right).ToGame(),color.ToGame(), 3f);
                }
                DrawCross(point, color, 5f);
                Text.Font = GameFont.Tiny;
                GUI.color = color.ToGame();
                Widgets.Label(new Rect(point.X + 6f, point.Y - 9f, 130f, 18f).ToGame(), socket.id);
                GUI.color = Color.white.ToGame();
                Text.Font = GameFont.Small;
            }
        }

        private void DrawInspector(Rect rect, ModularRenderNode node)
        {
            Widgets.DrawMenuSection(rect.ToGame());
            Rect inner = UiMath.Inset(rect, 10f);
            if (node == null)
            {
                Widgets.Label(inner.ToGame(), "Select a node.");
                return;
            }

            float y = inner.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f).ToGame(), node.thing.LabelCap);
            Text.Font = GameFont.Small;
            y += 34f;

            if (section == EditorSection.Placement)
            {
                float buttonWidth = (inner.width - 30f) / 6f;
                if (ModeButton(new Rect(inner.x, y, buttonWidth, 28f), "Graphic",
                    target == EditTarget.Graphic)) SetTarget(EditTarget.Graphic);
                if (ModeButton(new Rect(inner.x + buttonWidth + 6f, y, buttonWidth, 28f), "Mount",
                    target == EditTarget.Mount)) SetTarget(EditTarget.Mount);
                if (ModeButton(new Rect(inner.x + (buttonWidth + 6f) * 2f, y, buttonWidth, 28f), "Socket",
                    target == EditTarget.Socket)) SetTarget(EditTarget.Socket);
                if (ModeButton(new Rect(inner.x + (buttonWidth + 6f) * 3f, y, buttonWidth, 28f), "Light",
                    target == EditTarget.Flashlight)) SetTarget(EditTarget.Flashlight);
                if (ModeButton(new Rect(inner.x + (buttonWidth + 6f) * 4f, y, buttonWidth, 28f), "Laser",
                    target == EditTarget.Laser)) SetTarget(EditTarget.Laser);
                if (ModeButton(new Rect(inner.x + (buttonWidth + 6f) * 5f, y, buttonWidth, 28f), "Sight",
                    target == EditTarget.Sight)) SetTarget(EditTarget.Sight);
            }
            else
            {
                float buttonWidth = (inner.width - 18f) / 4f;
                if (ModeButton(new Rect(inner.x, y, buttonWidth, 28f), "Motion",
                    target == EditTarget.AnimationPart))
                    SetTarget(EditTarget.AnimationPart);
                if (ModeButton(new Rect(inner.x + buttonWidth + 6f, y,
                        buttonWidth, 28f), "Casing",
                    target == EditTarget.CasingPort))
                    SetTarget(EditTarget.CasingPort);
                if (ModeButton(new Rect(inner.x + (buttonWidth + 6f) * 2f, y,
                        buttonWidth, 28f), "Feed start",
                    target == EditTarget.FeedStart))
                    SetTarget(EditTarget.FeedStart);
                if (ModeButton(new Rect(inner.x + (buttonWidth + 6f) * 3f, y,
                        buttonWidth, 28f), "Feed end",
                    target == EditTarget.FeedEnd))
                    SetTarget(EditTarget.FeedEnd);
            }
            y += 38f;

            Widgets.Label(new Rect(inner.x, y + 3f, 58f, 24f).ToGame(), "Move");
            float axisWidth = (inner.width - 64f - 12f) / 3f;
            if (ModeButton(new Rect(inner.x + 64f, y, axisWidth, 28f), "Free",
                movementConstraint == MovementConstraint.Free))
                SetMovementConstraint(MovementConstraint.Free);
            if (ModeButton(new Rect(inner.x + 70f + axisWidth, y, axisWidth, 28f), "Horizontal",
                movementConstraint == MovementConstraint.Horizontal))
                SetMovementConstraint(MovementConstraint.Horizontal);
            if (ModeButton(new Rect(inner.x + 76f + axisWidth * 2f, y, axisWidth, 28f), "Vertical",
                movementConstraint == MovementConstraint.Vertical))
                SetMovementConstraint(MovementConstraint.Vertical);
            y += 38f;

            string scrollContext = node.path + ":" + section + ":" + target;
            if (inspectorScrollContext != scrollContext)
            {
                inspectorScrollContext = scrollContext;
                inspectorScroll = Vector2.Zero;
                inspectorContentHeight = 1000f;
            }
            Rect propertyViewport = new Rect(inner.x, y, inner.width,
                Mathf.Max(1f, inner.yMax - y));
            Rect propertyContent = new Rect(0f, 0f,
                Mathf.Max(1f, propertyViewport.width - 18f),
                Mathf.Max(propertyViewport.height, inspectorContentHeight));
            UnityEngine.Vector2 gameInspectorScroll = inspectorScroll.ToGame();
            Widgets.BeginScrollView(propertyViewport.ToGame(), ref gameInspectorScroll, propertyContent.ToGame());
            inspectorScroll = gameInspectorScroll.ToNative();
            inner = propertyContent;
            y = 0f;

            if (section == EditorSection.Placement)
            {
                DrawFirePerformanceEditor(inner, node, ref y);
                DrawAttachedRailPlacement(inner, node, ref y);
            }

            if (target == EditTarget.Socket && !node.Props.sockets.NullOrEmpty())
            {
                selectedSocketIndex = Mathf.Clamp(
                    selectedSocketIndex, 0, node.Props.sockets.Count - 1);
                ModularAttachmentSocket socket = node.Props.sockets[selectedSocketIndex];
                Widgets.Label(new Rect(inner.x, y, 92f, 24f).ToGame(), "Socket types");
                string key = node.path + ":socketTypes:" + selectedSocketIndex;
                string value;
                if (!textBuffers.TryGetValue(key, out value))
                    value = socket.tags.NullOrEmpty()
                        ? string.Empty
                        : string.Join(", ", socket.tags.ToArray());
                string edited = Widgets.TextField(
                    new Rect(inner.x + 96f, y, inner.width - 96f, 24f).ToGame(), value);
                textBuffers[key] = edited;
                if (edited != value)
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":socketTypes");
                    socket.tags = ParseTags(edited);
                    root.InvalidateTree();
                }
                TooltipHandler.TipRegion(new Rect(inner.x + 96f, y, inner.width - 96f, 24f).ToGame(),
                    "Comma-separated compatibility types, for example: picatinny, top_rail");
                y += 32f;

                bool rail = socket.isRail;
                Widgets.CheckboxLabeled(new Rect(inner.x, y, inner.width, 24f).ToGame(),
                    "Continuous horizontal rail", ref rail);
                if (rail != socket.isRail)
                {
                    PushUndoSnapshot();
                    socket.isRail = rail;
                    if (rail)
                    {
                        socket.transform.angle = 0f;
                        if (socket.railLength <= 0f) socket.railLength = 0.1f;
                    }
                    root.InvalidateTree();
                }
                y += 28f;
                if (socket.isRail)
                {
                    float length = SliderRow(inner, ref y, "Rail length",
                        socket.railLength, 0.01f, 1.5f);
                    if (!Mathf.Approximately(length, socket.railLength))
                    {
                        EnsureUndoCheckpoint(CurrentEditKey(node) + ":railLength");
                        socket.railLength = length;
                        root.InvalidateTree();
                    }
                    DrawRailSurfaceRow(inner, ref y, "Rail surface",
                        ref socket.railSurface,
                        CurrentEditKey(node) + ":railSurface");
                }
            }
            else if (target == EditTarget.Mount && !node.Props.mounts.NullOrEmpty())
            {
                selectedMountIndex = Mathf.Clamp(
                    selectedMountIndex, 0, node.Props.mounts.Count - 1);
                ModularAttachmentMount mount = node.Props.mounts[selectedMountIndex];
                float contact = SliderRow(inner, ref y, "Rail contact",
                    mount.EffectiveRailContactLength, 0f, 1.5f);
                if (!Mathf.Approximately(contact, mount.EffectiveRailContactLength))
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":railContactLength");
                    mount.railContactLength = contact;
                    root.InvalidateTree();
                }
                TooltipHandler.TipRegion(new Rect(inner.x, y - 28f, inner.width, 24f).ToGame(),
                    "Only this contact length must fit on the rail. "
                    + "The green segment marks the rail contact.");
                float bodyLength = SliderRow(inner, ref y, "Body length",
                    mount.EffectivePhysicalLength, 0f, 1.5f);
                float bodyOffset = SliderRow(inner, ref y, "Body center offset",
                    mount.physicalCenterOffset, -1.5f, 1.5f);
                if (!Mathf.Approximately(bodyLength, mount.EffectivePhysicalLength)
                    || !Mathf.Approximately(bodyOffset,
                        mount.physicalCenterOffset))
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":physicalLength");
                    mount.physicalLength = bodyLength;
                    mount.physicalCenterOffset = bodyOffset;
                    root.InvalidateTree();
                }
                if (mount.socketTags?.Contains("picatinny") == true)
                {
                    DrawRailSurfaceRow(inner, ref y, "Native surface",
                        ref mount.railSurface,
                        CurrentEditKey(node) + ":mountSurface");
                    if (mount.railSurface == ModularRailSurface.Top
                        || mount.railSurface == ModularRailSurface.Bottom)
                    {
                        float oppositeX = SliderRow(inner, ref y, "Opposite X",
                            mount.oppositeSurfaceOffset.ToNative().X, -1.5f, 1.5f);
                        float oppositeZ = SliderRow(inner, ref y, "Opposite Z",
                            mount.oppositeSurfaceOffset.ToNative().Z, -1.5f, 1.5f);
                        if (!Mathf.Approximately(oppositeX,
                                mount.oppositeSurfaceOffset.ToNative().X)
                            || !Mathf.Approximately(oppositeZ,
                                mount.oppositeSurfaceOffset.ToNative().Z))
                        {
                            EnsureUndoCheckpoint(CurrentEditKey(node)
                                + ":oppositeSurfaceOffset");
                            mount.oppositeSurfaceOffset = new Vector3(
                                oppositeX, 0f, oppositeZ).ToGame();
                            root.InvalidateTree();
                        }
                        TooltipHandler.TipRegion(new Rect(inner.x, y - 56f,
                            inner.width, 52f).ToGame(),
                            "Extra local offset used only when a top/bottom part is "
                            + "mounted on the opposite surface. Vertical texture and "
                            + "child transforms are flipped automatically.");
                    }
                }
            }

            if (target == EditTarget.Flashlight)
            {
                DrawFlashlightEditor(inner, node, ref y);
            }
            else if (target == EditTarget.Laser)
            {
                DrawLaserEditor(inner, node, ref y);
            }
            else if (target == EditTarget.Sight)
            {
                DrawSightEditor(inner, node, ref y);
            }
            else if (target == EditTarget.AnimationPart)
            {
                DrawPartAnimationEditor(inner, node, ref y);
            }
            else if (target == EditTarget.CasingPort)
            {
                DrawCasingPortEditor(inner, node, ref y);
            }
            else if (target == EditTarget.FeedStart
                || target == EditTarget.FeedEnd)
            {
                DrawFeedPointEditor(inner, node, ref y);
            }
            else
            {
            ModularAttachmentTransform authored = target == EditTarget.Graphic ? null : ActiveTransform(node);
            ModularAttachmentPose? transform = target == EditTarget.Graphic
                ? new ModularAttachmentPose(node.Props.graphicOffset.ToNative(), node.Props.graphicAngle,
                    node.Props.graphicScale.ToNative(), node.Props.graphicLayer)
                : authored?.ToNativePose();
            if (transform == null)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 25f).ToGame(),
                    target == EditTarget.Mount ? "This node has no mount." : "This node has no socket.");
                y += 32f;
            }
            else
            {
                bool changed = false;
                float x = SliderRow(inner, ref y, "X", transform.Value.position.X, -1.5f, 1.5f);
                float z = SliderRow(inner, ref y, "Z", transform.Value.position.Z, -1.5f, 1.5f);
                string angleLabel = target == EditTarget.Graphic
                    ? "Assembly angle"
                    : "Angle";
                float angle = SliderRow(inner, ref y, angleLabel, transform.Value.angle, -180f, 180f);
                ModularAttachmentSocket activeSocket = target == EditTarget.Socket
                    && !node.Props.sockets.NullOrEmpty()
                    ? node.Props.sockets[selectedSocketIndex]
                    : null;
                if (activeSocket?.isRail == true) angle = 0f;
                float scaleX = SliderRow(inner, ref y, "Scale X", transform.Value.scale.X, 0.1f, 3f);
                float scaleY = SliderRow(inner, ref y, "Scale Z", transform.Value.scale.Y, 0.1f, 3f);
                float layer = SliderRow(inner, ref y, "Layer", transform.Value.layer, -20f, 20f);

                changed |= !Mathf.Approximately(x, transform.Value.position.X)
                    || !Mathf.Approximately(z, transform.Value.position.Z)
                    || !Mathf.Approximately(angle, transform.Value.angle)
                    || !Mathf.Approximately(scaleX, transform.Value.scale.X)
                    || !Mathf.Approximately(scaleY, transform.Value.scale.Y)
                    || !Mathf.Approximately(layer, transform.Value.layer);
                if (changed)
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node));
                    var pose = new ModularAttachmentPose(new Vector3(x, 0f, z), angle,
                        new Vector2(scaleX, scaleY), layer);
                    if (target == EditTarget.Graphic)
                    {
                        node.Props.graphicOffset = pose.position.ToGame();
                        node.Props.graphicAngle = pose.angle;
                        node.Props.graphicScale = pose.scale.ToGame();
                        node.Props.graphicLayer = pose.layer;
                    }
                    else
                    {
                        authored.position = pose.position.ToGame();
                        authored.angle = pose.angle;
                        authored.scale = pose.scale.ToGame();
                        authored.layer = pose.layer;
                    }
                    root.InvalidateTree();
                }
            }
            }

            y += 8f;
            if (target == EditTarget.Mount)
                DrawMountSelector(inner, node, ref y);
            else if (target == EditTarget.Socket)
                DrawSocketSelector(inner, node, ref y);

            y += 10f;
            Widgets.Label(new Rect(inner.x, y, inner.width, 90f).ToGame(),
                "Red: logical origin\nWhite: texture centre\nOrange: incoming mount\nBlue: outgoing socket\nYellow: active edit target");
            inspectorContentHeight = y + 98f;
            Widgets.EndScrollView();
        }

        private void DrawFlashlightEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            ModularWeaponFlashlightProperties light = node.Props.flashlight;
            if (light == null)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 48f).ToGame(),
                    "This part has no <flashlight> data.");
                y += 54f;
                return;
            }

            float x = SliderRow(inner, ref y, "Emitter X",
                light.emitterOffset.ToNative().X, -1.5f, 1.5f);
            float z = SliderRow(inner, ref y, "Emitter Z",
                light.emitterOffset.ToNative().Z, -1.5f, 1.5f);
            float near = SliderRow(inner, ref y, "Near width",
                light.nearWidth, 0.01f, 1.5f);
            float far = SliderRow(inner, ref y, "Far width",
                light.farWidth, 0.05f, 4f);
            float pool = SliderRow(inner, ref y, "Pool radius",
                light.circleRadius, 0.01f, 2f);
            float range = SliderRow(inner, ref y, "Maximum range",
                light.maxRange, 1f, 60f);
            float overshoot = SliderRow(inner, ref y, "Length overshoot",
                light.lengthOvershoot, -2f, 2f);

            if (!Mathf.Approximately(x, light.emitterOffset.ToNative().X)
                || !Mathf.Approximately(z, light.emitterOffset.ToNative().Z)
                || !Mathf.Approximately(near, light.nearWidth)
                || !Mathf.Approximately(far, light.farWidth)
                || !Mathf.Approximately(pool, light.circleRadius)
                || !Mathf.Approximately(range, light.maxRange)
                || !Mathf.Approximately(overshoot, light.lengthOvershoot))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node));
                light.emitterOffset = new Vector3(x, 0f, z).ToGame();
                light.nearWidth = near;
                light.farWidth = far;
                light.circleRadius = pool;
                light.maxRange = range;
                light.lengthOvershoot = overshoot;
                root.InvalidateTree();
            }
        }

        private void DrawFeedPointEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            ModularWeaponCasingPortProperties port = node.Props.casingPort;
            if (port == null)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 48f).ToGame(),
                    "This part has no <casingPort> data.");
                y += 54f;
                return;
            }

            bool start = target == EditTarget.FeedStart;
            Vector3 value = start ? port.feedStartOffset.ToNative() : port.feedEndOffset.ToNative();
            string label = start ? "Feed start" : "Feed end";
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f).ToGame(),
                label + " — drag the marker or use X/Z below");
            y += 28f;
            float x = SliderRow(inner, ref y, label + " X", value.X, -1.5f, 1.5f);
            float z = SliderRow(inner, ref y, label + " Z", value.Z, -1.5f, 1.5f);
            if (!Mathf.Approximately(x, value.X)
                || !Mathf.Approximately(z, value.Z))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node));
                Vector3 edited = new Vector3(x, 0f, z);
                if (start) port.feedStartOffset = edited.ToGame();
                else port.feedEndOffset = edited.ToGame();
                root.InvalidateTree();
            }
        }

        private void DrawPartAnimationEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 26f).ToGame(),
                "Animated part: " + node.Props.animatedPart))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (ModularWeaponAnimatedPartKind kind in
                    Enum.GetValues(typeof(ModularWeaponAnimatedPartKind)))
                {
                    ModularWeaponAnimatedPartKind selectedKind = kind;
                    options.Add(new FloatMenuOption(kind.ToString(), () =>
                    {
                        EnsureUndoCheckpoint(CurrentEditKey(node));
                        node.Props.animatedPart = selectedKind;
                        root.InvalidateTree();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            y += 30f;
            if (node.Props.animatedPart == ModularWeaponAnimatedPartKind.None)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 48f).ToGame(),
                    "This part has no animatedPart data.");
                y += 54f;
                return;
            }

            Widgets.Label(new Rect(inner.x, y, inner.width, 24f).ToGame(),
                "Animated part: " + node.Props.animatedPart);
            y += 28f;
            Vector3 travel = node.Props.animationTravel.ToNative();
            Vector3 pivot = node.Props.animationPivot.ToNative();
            float travelX = SliderRow(inner, ref y, "Travel X",
                travel.X, -1.5f, 1.5f);
            float travelZ = SliderRow(inner, ref y, "Travel Z",
                travel.Z, -1.5f, 1.5f);
            float pivotX = SliderRow(inner, ref y, "Pivot X",
                pivot.X, -1.5f, 1.5f);
            float pivotZ = SliderRow(inner, ref y, "Pivot Z",
                pivot.Z, -1.5f, 1.5f);
            float angle = SliderRow(inner, ref y, "Rotation",
                node.Props.animationAngle, -180f, 180f);
            if (!Mathf.Approximately(travelX, travel.X)
                || !Mathf.Approximately(travelZ, travel.Z)
                || !Mathf.Approximately(pivotX, pivot.X)
                || !Mathf.Approximately(pivotZ, pivot.Z)
                || !Mathf.Approximately(angle, node.Props.animationAngle))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node));
                node.Props.animationTravel = new Vector3(travelX, 0f, travelZ).ToGame();
                node.Props.animationPivot = new Vector3(pivotX, 0f, pivotZ).ToGame();
                node.Props.animationAngle = angle;
                root.InvalidateTree();
            }
        }

        private void DrawCasingPortEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            ModularWeaponCasingPortProperties port = node.Props.casingPort;
            if (port == null)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 48f).ToGame(),
                    "This part has no <casingPort> data.");
                y += 54f;
                return;
            }

            float x = SliderRow(inner, ref y, "Ejection X",
                port.offset.ToNative().X, -1.5f, 1.5f);
            float z = SliderRow(inner, ref y, "Ejection Z",
                port.offset.ToNative().Z, -1.5f, 1.5f);
            float angle = SliderRow(inner, ref y, "Direction offset",
                port.ejectionAngleOffset, -180f, 180f);
            float phase = SliderRow(inner, ref y, "Cycle phase",
                port.ejectionCycleFraction, 0f, 1f);
            float scale = SliderRow(inner, ref y, "Casing scale",
                port.scale, 0.01f, 2f);
            if (!Mathf.Approximately(x, port.offset.ToNative().X)
                || !Mathf.Approximately(z, port.offset.ToNative().Z)
                || !Mathf.Approximately(angle, port.ejectionAngleOffset)
                || !Mathf.Approximately(phase, port.ejectionCycleFraction)
                || !Mathf.Approximately(scale, port.scale))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node));
                port.offset = new Vector3(x, 0f, z).ToGame();
                port.ejectionAngleOffset = angle;
                port.ejectionCycleFraction = phase;
                port.scale = scale;
                root.InvalidateTree();
            }
        }

        private void DrawLaserEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            ModularWeaponLaserProperties laser = node.Props.laser;
            if (laser == null)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 48f).ToGame(),
                    "This part has no <laser> data.");
                y += 54f;
                return;
            }

            float x = SliderRow(inner, ref y, "Emitter X",
                laser.emitterOffset.ToNative().X, -1.5f, 1.5f);
            float z = SliderRow(inner, ref y, "Emitter Z",
                laser.emitterOffset.ToNative().Z, -1.5f, 1.5f);
            float width = SliderRow(inner, ref y, "Beam width",
                laser.beamWidth, 0.005f, 0.15f);
            float dot = SliderRow(inner, ref y, "Dot size",
                laser.dotSize, 0.01f, 0.6f);
            float range = SliderRow(inner, ref y, "Maximum length",
                laser.maxLength, 1f, 100f);

            if (!Mathf.Approximately(x, laser.emitterOffset.ToNative().X)
                || !Mathf.Approximately(z, laser.emitterOffset.ToNative().Z)
                || !Mathf.Approximately(width, laser.beamWidth)
                || !Mathf.Approximately(dot, laser.dotSize)
                || !Mathf.Approximately(range, laser.maxLength))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node));
                laser.emitterOffset = new Vector3(x, 0f, z).ToGame();
                laser.beamWidth = width;
                laser.dotSize = dot;
                laser.maxLength = range;
                root.InvalidateTree();
            }
        }

        private void DrawSightEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            float occupancy = SliderRow(inner, ref y, "Vertical occupied",
                node.Props.verticalOccupancy, 0f, 0.5f);
            float occupancyOffset = SliderRow(inner, ref y, "Occupied centre Z",
                node.Props.verticalOccupancyOffset, -0.5f, 0.5f);
            if (!Mathf.Approximately(occupancy, node.Props.verticalOccupancy)
                || !Mathf.Approximately(
                    occupancyOffset,
                    node.Props.verticalOccupancyOffset))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node) + ":verticalOccupancy");
                node.Props.verticalOccupancy = occupancy;
                node.Props.verticalOccupancyOffset = occupancyOffset;
                root.InvalidateTree();
            }
            TooltipHandler.TipRegion(new Rect(inner.x, y - 56f, inner.width, 52f).ToGame(),
                "Visual obstruction only. This range never blocks attachment placement; "
                + "it reduces a sight's usable aiming window when it is in front.");

            if (node.Props.isAssemblyRoot)
            {
                float tolerance = SliderRow(inner, ref y, "Group height tolerance",
                    node.Props.sightGroupHeightTolerance, 0.001f, 0.1f);
                if (!Mathf.Approximately(
                    tolerance,
                    node.Props.sightGroupHeightTolerance))
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":sightTolerance");
                    node.Props.sightGroupHeightTolerance = tolerance;
                    root.InvalidateTree();
                }
            }

            ModularWeaponSightProperties sight = node.Props.sight;
            if (sight == null)
            {
                if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 28f).ToGame(),
                    "+ Add sight data"))
                {
                    PushUndoSnapshot();
                    node.Props.sight = new ModularWeaponSightProperties
                    {
                        kind = ModularSightKind.Holographic
                    };
                    root.InvalidateTree();
                }
                y += 34f;
                return;
            }

            Widgets.Label(new Rect(inner.x, y + 3f, 88f, 24f).ToGame(), "Sight type");
            if (Widgets.ButtonText(new Rect(inner.x + 92f, y,
                inner.width - 132f, 28f).ToGame(), sight.kind.ToString()))
            {
                PushUndoSnapshot();
                int count = Enum.GetValues(typeof(ModularSightKind)).Length;
                sight.kind = (ModularSightKind)(((int)sight.kind + 1) % count);
                root.InvalidateTree();
            }
            if (Widgets.ButtonText(new Rect(inner.xMax - 34f, y, 34f, 28f).ToGame(), "X"))
            {
                PushUndoSnapshot();
                node.Props.sight = null;
                root.InvalidateTree();
                y += 34f;
                return;
            }
            y += 34f;

            float axisX = SliderRow(inner, ref y, "Optical axis X",
                sight.axisOffset.ToNative().X, -0.5f, 0.5f);
            float axisZ = SliderRow(inner, ref y, "Optical axis Z",
                sight.axisOffset.ToNative().Z, -0.5f, 0.5f);
            float radius = SliderRow(inner, ref y, "Aim radius",
                sight.sightClearanceRadius, 0.001f, 0.15f);
            float priority = SliderRow(inner, ref y, "Selection priority",
                sight.selectionPriority, 0f, 100f);
            if (!Mathf.Approximately(axisX, sight.axisOffset.ToNative().X)
                || !Mathf.Approximately(axisZ, sight.axisOffset.ToNative().Z)
                || !Mathf.Approximately(radius, sight.sightClearanceRadius)
                || !Mathf.Approximately(priority, sight.selectionPriority))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node) + ":sightData");
                sight.axisOffset = new Vector3(axisX, 0f, axisZ).ToGame();
                sight.sightClearanceRadius = radius;
                sight.selectionPriority = priority;
                root.InvalidateTree();
            }
        }

        private void DrawFirePerformanceEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            Widgets.Label(new Rect(inner.x, y + 3f, 96f, 24f).ToGame(), "Part category");
            if (Widgets.ButtonText(new Rect(inner.x + 100f, y,
                inner.width - 100f, 28f).ToGame(), node.Props.partCategory.Label()))
            {
                PushUndoSnapshot();
                int count = Enum.GetValues(typeof(ModularWeaponPartCategory)).Length;
                node.Props.partCategory = (ModularWeaponPartCategory)(
                    ((int)node.Props.partCategory + 1) % count);
                if (node.Props.partCategory != ModularWeaponPartCategory.Functional)
                    node.Props.missingFunctions.Clear();
                root.InvalidateTree();
            }
            y += 34f;

            if (node.Props.partCategory == ModularWeaponPartCategory.Functional)
            {
                DrawMissingFunctionToggle(
                    inner,
                    node,
                    ref y,
                    "Missing: semi-auto only",
                    ModularWeaponMissingFunction.SemiAutomaticOnly);
                DrawMissingFunctionToggle(
                    inner,
                    node,
                    ref y,
                    "Missing: one-round capacity",
                    ModularWeaponMissingFunction.SingleRoundCapacity);
            }

            if (node.Props.isAssemblyRoot)
            {
                float rpm = SliderRow(inner, ref y, "Real RPM",
                    node.Props.realisticRoundsPerMinute, 60f, 2000f);
                if (!Mathf.Approximately(rpm, node.Props.realisticRoundsPerMinute))
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":realRpm");
                    node.Props.realisticRoundsPerMinute = rpm;
                    root.InvalidateTree();
                }

                float baseDelay = SliderRow(inner, ref y, "Base delay",
                    node.Props.baseFireDelayFactor, 1f, 3f);
                if (!Mathf.Approximately(baseDelay, node.Props.baseFireDelayFactor))
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":baseFireDelay");
                    node.Props.baseFireDelayFactor = baseDelay;
                    root.InvalidateTree();
                }
            }

            float partDelay = SliderRow(inner, ref y, "Part delay",
                node.Props.fireDelayMultiplier, 0.25f, 3f);
            if (!Mathf.Approximately(partDelay, node.Props.fireDelayMultiplier))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node) + ":partFireDelay");
                node.Props.fireDelayMultiplier = partDelay;
                root.InvalidateTree();
            }
            TooltipHandler.TipRegion(new Rect(inner.x, y - 28f, inner.width, 24f).ToGame(),
                "Multiplies fire delay. Values below 1 fire faster, but the root weapon's "
                    + "real RPM always remains the mechanical lower-delay limit.");

            float burstCountOffset = SliderRow(inner, ref y, "Burst count offset",
                node.Props.burstShotCountOffset, -10f, 10f);
            int roundedBurstOffset = Mathf.RoundToInt(burstCountOffset);
            float burstCountFactor = SliderRow(inner, ref y, "Burst count factor",
                node.Props.burstShotCountMultiplier, 0.1f, 4f);
            float burstSpeedFactor = SliderRow(inner, ref y, "Burst speed factor",
                node.Props.burstShotSpeedMultiplier, 0.1f, 4f);
            if (roundedBurstOffset != node.Props.burstShotCountOffset
                || !Mathf.Approximately(burstCountFactor,
                    node.Props.burstShotCountMultiplier)
                || !Mathf.Approximately(burstSpeedFactor,
                    node.Props.burstShotSpeedMultiplier))
            {
                EnsureUndoCheckpoint(CurrentEditKey(node) + ":burstPerformance");
                node.Props.burstShotCountOffset = roundedBurstOffset;
                node.Props.burstShotCountMultiplier = burstCountFactor;
                node.Props.burstShotSpeedMultiplier = burstSpeedFactor;
                root.InvalidateTree();
            }
            DrawInternalStatsEditor(inner, node, ref y);
            DrawStockEditor(inner, node, ref y);
            y += 4f;
        }

        private void DrawMissingFunctionToggle(
            Rect inner,
            ModularRenderNode node,
            ref float y,
            string label,
            ModularWeaponMissingFunction function)
        {
            bool enabled = node.Props.missingFunctions.Contains(function);
            bool edited = enabled;
            Widgets.CheckboxLabeled(
                new Rect(inner.x, y, inner.width, 24f).ToGame(), label, ref edited);
            if (edited != enabled)
            {
                PushUndoSnapshot();
                if (edited) node.Props.missingFunctions.Add(function);
                else node.Props.missingFunctions.Remove(function);
                root.InvalidateTree();
            }
            y += 28f;
        }

        private void DrawInternalStatsEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            ModularWeaponInternalStats stats = node.Props.internalStats;
            if (stats == null)
            {
                if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 28f).ToGame(),
                    "+ Add internal conversion stats"))
                {
                    PushUndoSnapshot();
                    node.Props.internalStats = new ModularWeaponInternalStats();
                    root.InvalidateTree();
                }
                y += 34f;
                return;
            }

            Widgets.Label(new Rect(inner.x, y + 3f, inner.width - 40f, 24f).ToGame(),
                "Internal conversion inputs (hidden from player UI)");
            if (Widgets.ButtonText(new Rect(inner.xMax - 34f, y, 34f, 28f).ToGame(), "X"))
            {
                PushUndoSnapshot();
                node.Props.internalStats = null;
                root.InvalidateTree();
                y += 34f;
                return;
            }
            y += 31f;

            foreach (var field in typeof(ModularWeaponInternalStats).GetFields())
            {
                float before = (float)field.GetValue(stats);
                float min = -50f, max = 100f;
                string name = field.Name;
                if (name == "massKg") { min = -1f; max = 10f; }
                else if (name == "recoilImpulse" || name == "muzzleRise") { min = -5f; max = 12f; }
                else if (name == "massPositionMeters") { min = -1f; max = 1f; }
                else if (name == "momentOfInertia" || name == "receiverLengthMeters"
                    || name == "lengthExtensionMeters" || name == "naturalSupport"
                    || name.StartsWith("precision", StringComparison.Ordinal)) { min = 0f; max = 1f; }
                else if (name.EndsWith("ReferenceCost", StringComparison.Ordinal)) { min = 0.1f; max = 3f; }
                else if (name == "ammunitionPrecisionFactor") { min = 0f; max = 1.35f; }
                else if (name == "operatingReliability" || name == "gasFlow") { min = -0.5f; max = 1.5f; }
                else if (name == "barrelLengthInches") { min = 0f; max = 30f; }
                else if (name == "identificationDistanceMeters") { min = 0f; max = 600f; }
                else if (name == "muzzleVelocityOffsetMetersPerSecond") { min = -300f; max = 300f; }
                float value = SliderRow(inner, ref y, name, before, min, max);
                if (Mathf.Approximately(value, before)) continue;
                EnsureUndoCheckpoint(CurrentEditKey(node) + ":internalStats");
                field.SetValue(stats, value);
                root.InvalidateTree();
            }
        }

        private void DrawStockEditor(Rect inner, ModularRenderNode node, ref float y)
        {
            y += 12f;
            var stock = node.Props.stock;
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 27f).ToGame(),
                stock == null ? "Add physical stock profile" : "Remove physical stock profile"))
            {
                PushUndoSnapshot();
                node.Props.stock = stock == null ? new ModularWeaponStockProperties() : null;
                root.InvalidateTree();
                stock = node.Props.stock;
            }
            y += 34f;
            if (stock == null) return;
            bool adjustable = stock.adjustable;
            Widgets.CheckboxLabeled(new Rect(inner.x, y, inner.width, 24f).ToGame(), "Rail-driven stock extension", ref adjustable);
            y += 30f;
            if (adjustable != stock.adjustable)
            {
                PushUndoSnapshot();
                stock.adjustable = adjustable;
                root.InvalidateTree();
            }
            foreach (var field in typeof(ModularWeaponStockProperties).GetFields())
            {
                if (field.FieldType != typeof(float)) continue;
                float before = (float)field.GetValue(stock);
                bool offset = field.Name.EndsWith("Offset", StringComparison.Ordinal);
                float value = SliderRow(inner, ref y, field.Name, before, offset ? -1f : 0f, 1f);
                if (Mathf.Approximately(value, before)) continue;
                EnsureUndoCheckpoint(CurrentEditKey(node) + ":stock");
                field.SetValue(stock, value);
                root.InvalidateTree();
            }
        }

        private void DrawAttachedRailPlacement(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
            if (node.parentComp == null || node.parentChildIndex < 0) return;
            ModularAttachmentSocket socket = node.parentComp.SocketNamed(node.parentSocketId);
            if (socket == null || !socket.isRail) return;

            ModularAttachmentMount mount = node.Props.MountNamed(node.mountId);
            float contact = Mathf.Max(0f,
                mount?.EffectiveRailContactLength ?? 0f);
            float body = Mathf.Max(0f,
                mount?.EffectivePhysicalLength ?? 0f);
            float limit = Mathf.Max(0f, (socket.railLength - contact) * 0.5f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f).ToGame(),
                "Rail: " + socket.Label + "   contact "
                + contact.ToString("0.###", CultureInfo.InvariantCulture)
                + "   body " + body.ToString("0.###",
                    CultureInfo.InvariantCulture));
            y += 26f;
            float value = limit <= 0.0001f
                ? 0f
                : SliderRow(inner, ref y, "Rail position", node.railOffset, -limit, limit);
            if (Mathf.Approximately(value, node.railOffset)) return;

            EnsureUndoCheckpoint(node.path + ":railPosition");
            string reason;
            if (!node.parentComp.TrySetRailOffset(node.parentChildIndex, value, out reason))
            {
                string key = selectedPath + ":" + target + ":" + selectedMountIndex + ":"
                    + selectedSocketIndex + ":Rail position";
                numericBuffers[key] = node.railOffset.ToString(
                    "0.###", CultureInfo.InvariantCulture);
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            }
        }

        private void DrawMountSelector(Rect inner, ModularRenderNode node, ref float y)
        {
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f).ToGame(), "Mount points");
            y += 24f;
            for (int i = 0; i < node.Props.mounts.Count; i++)
            {
                Rect row = new Rect(inner.x, y, inner.width, 25f);
                if (i == selectedMountIndex) Widgets.DrawHighlightSelected(row.ToGame());
                if (Widgets.ButtonText(row.ToGame(), node.Props.mounts[i].Label))
                {
                    activeEditKey = null;
                    selectedMountIndex = i;
                }
                y += 28f;
            }

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 25f).ToGame(), "+ Add mount"))
            {
                PushUndoSnapshot();
                node.Props.mounts.Add(new ModularAttachmentMount
                {
                    id = UniquePointId("mount", node.Props.mounts.ConvertAll(x => x.id))
                });
                selectedMountIndex = node.Props.mounts.Count - 1;
                root.InvalidateTree();
            }
            y += 30f;
        }

        private void DrawSocketSelector(Rect inner, ModularRenderNode node, ref float y)
        {
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f).ToGame(), "Sockets");
            y += 24f;
            for (int i = 0; i < node.Props.sockets.Count; i++)
            {
                Rect row = new Rect(inner.x, y, inner.width, 25f);
                if (i == selectedSocketIndex) Widgets.DrawHighlightSelected(row.ToGame());
                if (Widgets.ButtonText(row.ToGame(), node.Props.sockets[i].Label))
                {
                    activeEditKey = null;
                    selectedSocketIndex = i;
                }
                y += 28f;
            }

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 25f).ToGame(), "+ Add socket"))
            {
                Find.WindowStack.Add(new Dialog_CreateModularSocket(
                    node.Props,
                    socket =>
                    {
                        PushUndoSnapshot();
                        node.Props.sockets.Add(socket);
                        selectedSocketIndex = node.Props.sockets.Count - 1;
                        activeEditKey = null;
                        root.InvalidateTree();
                    }));
            }
            y += 30f;
        }

        private ModularAttachmentTransform ActiveTransform(ModularRenderNode node)
        {
            if (target == EditTarget.Mount)
            {
                if (node.Props.mounts.NullOrEmpty()) return null;
                selectedMountIndex = Mathf.Clamp(selectedMountIndex, 0, node.Props.mounts.Count - 1);
                return node.Props.mounts[selectedMountIndex].transform;
            }

            if (node.Props.sockets.NullOrEmpty()) return null;
            selectedSocketIndex = Mathf.Clamp(selectedSocketIndex, 0, node.Props.sockets.Count - 1);
            return node.Props.sockets[selectedSocketIndex].transform;
        }

        private void HandlePreviewNavigation(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition.ToNative())) return;

            if (current.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * (1f - current.delta.ToNative().Y * 0.07f), 0.2f, 8f);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 2)
            {
                pan += current.delta.ToNative();
                current.Use();
            }
        }

        private void HandleMarkerDrag(Rect rect, ModularRenderNode node, float pixelsPerCell)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDrag || current.button != 0
                || !rect.Contains(current.mousePosition.ToNative())) return;

            Vector2 rootDelta = new Vector2(
                current.delta.ToNative().X / pixelsPerCell,
                -current.delta.ToNative().Y / pixelsPerCell);

            if (current.shift && movementConstraint == MovementConstraint.Free)
            {
                if (Mathf.Abs(rootDelta.X) >= Mathf.Abs(rootDelta.Y)) rootDelta.Y = 0f;
                else rootDelta.X = 0f;
            }
            else if (movementConstraint == MovementConstraint.Horizontal)
            {
                rootDelta.Y = 0f;
            }
            else if (movementConstraint == MovementConstraint.Vertical)
            {
                rootDelta.X = 0f;
            }

            var localDelta = ModularTransform2D.Rotate(rootDelta, -node.transform.angle);
            localDelta = new Vector2(
                localDelta.X / SafeSignedScale(node.transform.scale.X),
                localDelta.Y / SafeSignedScale(node.transform.scale.Y));
            Vector3 delta = new Vector3(localDelta.X, 0f, localDelta.Y);
            EnsureUndoCheckpoint(CurrentEditKey(node) + ":drag");
            if (target == EditTarget.Graphic)
            {
                if (selectedPaths.Count > 1)
                {
                    var moving = new List<ModularRenderNode> {node};
                    foreach (ModularRenderNode candidate in root.RenderSnapshot())
                        if (candidate != node && selectedPaths.Contains(candidate.path)) moving.Add(candidate);
                    MoveGraphics(moving, rootDelta);
                }
                else node.Props.graphicOffset = (node.Props.graphicOffset.ToNative() + delta).ToGame();
            }
            else if (target == EditTarget.Flashlight)
            {
                if (node.Props.flashlight == null) return;
                node.Props.flashlight.emitterOffset = (node.Props.flashlight.emitterOffset.ToNative() + delta).ToGame();
            }
            else if (target == EditTarget.Laser)
            {
                if (node.Props.laser == null) return;
                node.Props.laser.emitterOffset = (node.Props.laser.emitterOffset.ToNative() + delta).ToGame();
            }
            else if (target == EditTarget.Sight)
            {
                if (node.Props.sight == null) return;
                node.Props.sight.axisOffset = (node.Props.sight.axisOffset.ToNative() + delta).ToGame();
            }
            else if (target == EditTarget.AnimationPart)
            {
                if (node.Props.animatedPart == ModularWeaponAnimatedPartKind.None)
                    return;
                node.Props.animationTravel = (node.Props.animationTravel.ToNative() + delta).ToGame();
            }
            else if (target == EditTarget.CasingPort)
            {
                if (node.Props.casingPort == null) return;
                node.Props.casingPort.offset = (node.Props.casingPort.offset.ToNative() + delta).ToGame();
            }
            else if (target == EditTarget.FeedStart)
            {
                if (node.Props.casingPort == null) return;
                node.Props.casingPort.feedStartOffset = (node.Props.casingPort.feedStartOffset.ToNative() + delta).ToGame();
            }
            else if (target == EditTarget.FeedEnd)
            {
                if (node.Props.casingPort == null) return;
                node.Props.casingPort.feedEndOffset = (node.Props.casingPort.feedEndOffset.ToNative() + delta).ToGame();
            }
            else
            {
                ModularAttachmentTransform transform = ActiveTransform(node);
                if (transform == null) return;
                transform.position = (transform.position.ToNative() + delta).ToGame();
            }
            root.InvalidateTree();
            current.Use();
        }

        private void SetTarget(EditTarget value)
        {
            if (target == value) return;
            activeEditKey = null;
            target = value;
        }

        private void SetSection(EditorSection value, ModularRenderNode selected)
        {
            if (section == value) return;
            activeEditKey = null;
            section = value;
            if (value == EditorSection.Placement)
            {
                target = EditTarget.Graphic;
                return;
            }

            if (selected?.Props.animatedPart != ModularWeaponAnimatedPartKind.None)
                target = EditTarget.AnimationPart;
            else if (selected?.Props.casingPort != null)
                target = EditTarget.CasingPort;
            else
                target = EditTarget.AnimationPart;
        }

        private void SetMovementConstraint(MovementConstraint value)
        {
            if (movementConstraint == value) return;
            activeEditKey = null;
            movementConstraint = value;
        }

        private string AxisLabel()
        {
            if (movementConstraint == MovementConstraint.Horizontal) return "Axis: horizontal";
            if (movementConstraint == MovementConstraint.Vertical) return "Axis: vertical";
            return "Axis: free";
        }

        private string CurrentEditKey(ModularRenderNode node)
        {
            return node.path + ":" + target + ":" + selectedMountIndex + ":"
                + selectedSocketIndex;
        }

        private void HandleUndoInput()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown) return;
            if ((current.control || current.command) && current.keyCode == KeyCode.Z)
            {
                UndoLastEdit();
                current.Use();
            }
        }

        private void EnsureUndoCheckpoint(string key)
        {
            if (activeEditKey == key) return;
            PushUndoSnapshot();
            activeEditKey = key;
        }

        private void PushUndoSnapshot()
        {
            undoHistory.Add(EditorSnapshot.Capture(root.RenderSnapshot(), root));
            if (undoHistory.Count > 100) undoHistory.RemoveAt(0);
        }

        private void UndoLastEdit()
        {
            if (undoHistory.Count == 0) return;
            int index = undoHistory.Count - 1;
            EditorSnapshot snapshot = undoHistory[index];
            undoHistory.RemoveAt(index);
            if (snapshot.Owner != root && ModularPlacementWorkspace.IsLive(snapshot.Owner)) SwitchWeapon(snapshot.Owner);
            snapshot.Restore();
            activeEditKey = null;
            numericBuffers.Clear();
            textBuffers.Clear();
            root.InvalidateTree();
            workspace.Refresh();
        }

        private ModularRenderNode FindSelected(List<ModularRenderNode> snapshot)
        {
            selectedPaths.RemoveWhere(path => !snapshot.Exists(node => node.path == path));
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].path == selectedPath)
                {
                    selectedPaths.Add(selectedPath);
                    return snapshot[i];
                }
            }

            ModularRenderNode fallback = snapshot.Count > 0 ? snapshot[0] : null;
            selectedPath = fallback?.path;
            if (selectedPath != null) selectedPaths.Add(selectedPath);
            return fallback;
        }

        private static bool ModeButton(Rect rect, string label, bool selected)
        {
            if (selected) Widgets.DrawHighlightSelected(rect.ToGame());
            return Widgets.ButtonText(rect.ToGame(), label);
        }

        private void DrawRailSurfaceRow(
            Rect inner,
            ref float y,
            string label,
            ref ModularRailSurface surface,
            string undoKey)
        {
            Widgets.Label(new Rect(inner.x, y + 3f, 112f, 24f).ToGame(), label);
            if (Widgets.ButtonText(new Rect(inner.x + 116f, y,
                inner.width - 116f, 26f).ToGame(), RailSurfaceLabel(surface)))
            {
                EnsureUndoCheckpoint(undoKey);
                surface = NextRailSurface(surface);
                root.InvalidateTree();
            }
            y += 32f;
        }

        internal static ModularRailSurface NextRailSurface(ModularRailSurface value)
        {
            switch (value)
            {
                case ModularRailSurface.Top: return ModularRailSurface.Bottom;
                case ModularRailSurface.Bottom: return ModularRailSurface.Side;
                case ModularRailSurface.Side: return ModularRailSurface.Top;
                default: return ModularRailSurface.Top;
            }
        }

        internal static string RailSurfaceLabel(ModularRailSurface value)
        {
            switch (value)
            {
                case ModularRailSurface.Top: return "Top";
                case ModularRailSurface.Bottom: return "Bottom";
                case ModularRailSurface.Side: return "Side only";
                default: return "Unspecified";
            }
        }

        private float SliderRow(
            Rect inner,
            ref float y,
            string label,
            float value,
            float min,
            float max)
        {
            Widgets.Label(new Rect(inner.x, y, 86f, 24f).ToGame(), label);
            float result = Widgets.HorizontalSlider(
                new Rect(inner.x + 90f, y + 3f, inner.width - 180f, 18f).ToGame(),
                value, min, max, true);
            string key = selectedPath + ":" + target + ":" + selectedMountIndex + ":"
                + selectedSocketIndex + ":" + label;
            string buffer;
            if (!numericBuffers.TryGetValue(key, out buffer))
                buffer = result.ToString("0.###", CultureInfo.InvariantCulture);
            Widgets.TextFieldNumeric(new Rect(inner.xMax - 82f, y, 82f, 24f).ToGame(),
                ref result, ref buffer, min, max);
            numericBuffers[key] = buffer;
            y += 28f;
            return result;
        }

        private static Vector2 WorldToGui(Vector2 point, Vector2 origin, float scale)
            => origin + new Vector2(point.X, -point.Y) * scale;

        private static Vector2 WorldToGuiAxes(Vector2 point, Vector2 origin, float pixelsPerCell)
        {
            return new Vector2(
                origin.X + point.X * pixelsPerCell,
                origin.Y - point.Y * pixelsPerCell);
        }

        private static Rect CenteredRect(Vector2 center, Vector2 size)
        {
            return new Rect(center.X - size.X * 0.5f, center.Y - size.Y * 0.5f,
                size.X, size.Y);
        }

        private static void DrawTexture(Rect rect, Texture texture, bool flipVertical)
        {
            Rect uv = flipVertical
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
            GUI.DrawTextureWithTexCoords(rect.ToGame(), texture, uv.ToGame(), true);
        }

        private static float SafeSignedScale(float value)
        {
            if (Mathf.Abs(value) >= 0.0001f) return value;
            return value < 0f ? -0.0001f : 0.0001f;
        }

        private static void DrawCross(Vector2 point, Color color, float size)
        {
            Widgets.DrawLine((new Vector2(point.X - size, point.Y)).ToGame(),(
                new Vector2(point.X + size, point.Y)).ToGame(),color.ToGame(), 2f);
            Widgets.DrawLine((new Vector2(point.X, point.Y - size)).ToGame(),(
                new Vector2(point.X, point.Y + size)).ToGame(),color.ToGame(), 2f);
        }

        private static string UniquePointId(string prefix, List<string> existing)
        {
            int index = 1;
            string value;
            do value = prefix + "_" + index++.ToString("00", CultureInfo.InvariantCulture);
            while (existing.Contains(value));
            return value;
        }

        internal static List<string> ParseTags(string value)
        {
            List<string> result = new List<string>();
            if (value.NullOrEmpty()) return result;

            string[] pieces = value.Split(new[] { ',', ';' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pieces.Length; i++)
            {
                string tag = pieces[i].Trim();
                if (!tag.NullOrEmpty() && !result.Contains(tag)) result.Add(tag);
            }
            return result;
        }

        internal static string BuildXml(List<ModularRenderNode> snapshot)
        {
            StringBuilder builder = new StringBuilder();
            HashSet<ThingDef> exported = new HashSet<ThingDef>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                ModularRenderNode node = snapshot[i];
                if (!exported.Add(node.thing.def)) continue;
                CompProperties_ModularWeaponNode props = node.Props;

                builder.AppendLine("<!-- " + node.thing.def.defName + " -->");
                if (props.partCategory != ModularWeaponPartCategory.Optional)
                    builder.AppendLine("<partCategory>" + props.partCategory
                        + "</partCategory>");
                if (props.performanceRole != ModularWeaponPerformanceRole.None)
                    builder.AppendLine("<performanceRole>" + props.performanceRole
                        + "</performanceRole>");
                if (!props.incompatibleParts.NullOrEmpty())
                {
                    builder.AppendLine("<incompatibleParts>");
                    foreach (ThingDef part in props.incompatibleParts)
                        if (part != null)
                            builder.AppendLine("  <li>" + Xml(part.defName) + "</li>");
                    builder.AppendLine("</incompatibleParts>");
                }
                if (!props.conditionalIncompatibleParts.NullOrEmpty())
                {
                    builder.AppendLine("<conditionalIncompatibleParts>");
                    foreach (ModularWeaponConditionalPartConflict rule in props.conditionalIncompatibleParts)
                    {
                        if (rule?.part == null) continue;
                        builder.AppendLine("  <li><part>" + Xml(rule.part.defName) + "</part><whenAnyParts>");
                        if (rule.whenAnyParts != null)
                            foreach (ThingDef condition in rule.whenAnyParts)
                                if (condition != null)
                                    builder.AppendLine("    <li>" + Xml(condition.defName) + "</li>");
                        builder.AppendLine("  </whenAnyParts></li>");
                    }
                    builder.AppendLine("</conditionalIncompatibleParts>");
                }
                if (!props.missingFunctions.NullOrEmpty())
                {
                    builder.AppendLine("<missingFunctions>");
                    for (int j = 0; j < props.missingFunctions.Count; j++)
                        builder.AppendLine("  <li>" + props.missingFunctions[j]
                            + "</li>");
                    builder.AppendLine("</missingFunctions>");
                }
                if (props.isAssemblyRoot)
                {
                    builder.AppendLine("<realisticRoundsPerMinute>"
                        + Number(props.realisticRoundsPerMinute)
                        + "</realisticRoundsPerMinute>");
                    builder.AppendLine("<baseFireDelayFactor>"
                        + Number(props.baseFireDelayFactor)
                        + "</baseFireDelayFactor>");
                    builder.AppendLine("<assemblyScale>" + Number(props.assemblyScale)
                        + "</assemblyScale>");
                    builder.AppendLine("<animationSpeed>" + Number(props.animationSpeed)
                        + "</animationSpeed>");
                    builder.AppendLine("<sightGroupHeightTolerance>"
                        + Number(props.sightGroupHeightTolerance)
                        + "</sightGroupHeightTolerance>");
                }
                if (!Mathf.Approximately(props.fireDelayMultiplier, 1f))
                    builder.AppendLine("<fireDelayMultiplier>"
                        + Number(props.fireDelayMultiplier) + "</fireDelayMultiplier>");
                AppendInternalStats(builder, props.internalStats);
                AppendStock(builder, props.stock);
                AppendStatModifiers(builder, "statOffsets", props.statOffsets);
                AppendStatModifiers(builder, "statFactors", props.statFactors);
                if (props.burstShotCountOffset != 0)
                    builder.AppendLine("<burstShotCountOffset>"
                        + props.burstShotCountOffset + "</burstShotCountOffset>");
                if (!Mathf.Approximately(props.burstShotCountMultiplier, 1f))
                    builder.AppendLine("<burstShotCountMultiplier>"
                        + Number(props.burstShotCountMultiplier)
                        + "</burstShotCountMultiplier>");
                if (!Mathf.Approximately(props.burstShotSpeedMultiplier, 1f))
                    builder.AppendLine("<burstShotSpeedMultiplier>"
                        + Number(props.burstShotSpeedMultiplier)
                        + "</burstShotSpeedMultiplier>");
                if (props.projectileOverride != null)
                    builder.AppendLine("<projectileOverride>"
                        + props.projectileOverride.defName + "</projectileOverride>");
                if (props.casingMoteDef != null)
                    builder.AppendLine("<casingMoteDef>"
                        + props.casingMoteDef.defName + "</casingMoteDef>");
                if (!props.feedingRoundTexPath.NullOrEmpty())
                    builder.AppendLine("<feedingRoundTexPath>"
                        + Xml(props.feedingRoundTexPath) + "</feedingRoundTexPath>");
                if (props.soundCastOverride != null)
                    builder.AppendLine("<soundCastOverride>"
                        + props.soundCastOverride.defName + "</soundCastOverride>");
                if (props.soundCastTailOverride != null)
                    builder.AppendLine("<soundCastTailOverride>"
                        + props.soundCastTailOverride.defName + "</soundCastTailOverride>");
                if (props.overridePriority != 0)
                    builder.AppendLine("<overridePriority>" + props.overridePriority
                        + "</overridePriority>");
                if (props.isAssemblyRoot && props.muzzleFlashEffecter != null)
                    builder.AppendLine("<muzzleFlashEffecter>"
                        + props.muzzleFlashEffecter.defName + "</muzzleFlashEffecter>");
                if (props.isAssemblyRoot
                    && !Mathf.Approximately(props.muzzleFlashDistance, 1.6f))
                    builder.AppendLine("<muzzleFlashDistance>"
                        + Number(props.muzzleFlashDistance) + "</muzzleFlashDistance>");
                if (props.isAssemblyRoot
                    && !Mathf.Approximately(props.muzzleFlashScale, 1f))
                    builder.AppendLine("<muzzleFlashScale>"
                        + Number(props.muzzleFlashScale) + "</muzzleFlashScale>");
                if (props.muzzleFlashEffecterOverride != null)
                    builder.AppendLine("<muzzleFlashEffecterOverride>"
                        + props.muzzleFlashEffecterOverride.defName
                        + "</muzzleFlashEffecterOverride>");
                if (!Mathf.Approximately(props.muzzleFlashDistanceOffset, 0f))
                    builder.AppendLine("<muzzleFlashDistanceOffset>"
                        + Number(props.muzzleFlashDistanceOffset)
                        + "</muzzleFlashDistanceOffset>");
                if (!Mathf.Approximately(props.muzzleFlashScaleFactor, 1f))
                    builder.AppendLine("<muzzleFlashScaleFactor>"
                        + Number(props.muzzleFlashScaleFactor)
                        + "</muzzleFlashScaleFactor>");
                if (props.suppressMuzzleFlash)
                    builder.AppendLine("<suppressMuzzleFlash>true</suppressMuzzleFlash>");
                if (props.muzzleEffectKind != ModularMuzzleEffectKind.Auto)
                    builder.AppendLine("<muzzleEffectKind>" + props.muzzleEffectKind
                        + "</muzzleEffectKind>");
                builder.AppendLine("<graphicOffset>" + Vector(props.graphicOffset.ToNative()) + "</graphicOffset>");
                if (!Mathf.Approximately(props.graphicAngle, 0f))
                    builder.AppendLine("<graphicAngle>" + Number(props.graphicAngle) + "</graphicAngle>");
                if (!IsAuthoredUnitScale(props.graphicScale.ToNative()))
                    builder.AppendLine("<graphicScale>(" + Number(props.graphicScale.ToNative().X) + ","
                        + Number(props.graphicScale.ToNative().Y) + ")</graphicScale>");
                if (!Mathf.Approximately(props.graphicLayer, 0f))
                    builder.AppendLine("<graphicLayer>" + Number(props.graphicLayer) + "</graphicLayer>");
                if (props.additionalGraphics != null && props.additionalGraphics.Count > 0)
                {
                    builder.AppendLine("<additionalGraphics>");
                    foreach (var layer in props.additionalGraphics)
                    {
                        if (layer?.graphicData == null) continue;
                        builder.AppendLine("  <li><graphicLayer>" + Number(layer.graphicLayer)
                            + "</graphicLayer><graphicData><texPath>"
                            + System.Security.SecurityElement.Escape(layer.graphicData.texPath)
                            + "</texPath><graphicClass>Graphic_Single</graphicClass><drawSize>("
                            + Number(layer.graphicData.drawSize.x) + ","
                            + Number(layer.graphicData.drawSize.y)
                            + ")</drawSize></graphicData></li>");
                    }
                    builder.AppendLine("</additionalGraphics>");
                }
                if (props.outlinePriority != 0)
                    builder.AppendLine("<outlinePriority>" + props.outlinePriority
                        + "</outlinePriority>");
                if (props.animatedPart != ModularWeaponAnimatedPartKind.None)
                {
                    builder.AppendLine("<animatedPart>" + props.animatedPart
                        + "</animatedPart>");
                    builder.AppendLine("<animationTravel>"
                        + Vector(props.animationTravel.ToNative()) + "</animationTravel>");
                    builder.AppendLine("<animationPivot>"
                        + Vector(props.animationPivot.ToNative()) + "</animationPivot>");
                    builder.AppendLine("<animationAngle>"
                        + Number(props.animationAngle) + "</animationAngle>");
                }
                if (props.hideWhenAttached)
                    builder.AppendLine("<hideWhenAttached>true</hideWhenAttached>");
                if (props.magazineCapacity > 0)
                    builder.AppendLine("<magazineCapacity>" + props.magazineCapacity
                        + "</magazineCapacity>");
                if (!props.ammunitionType.NullOrEmpty())
                    builder.AppendLine("<ammunitionType>" + Xml(props.ammunitionType)
                        + "</ammunitionType>");
                if (!props.ceAmmoDefName.NullOrEmpty())
                    builder.AppendLine("<ceAmmoDefName>" + props.ceAmmoDefName
                        + "</ceAmmoDefName>");
                if (!props.chamberCaliber.NullOrEmpty())
                    builder.AppendLine("<chamberCaliber>" + Xml(props.chamberCaliber)
                        + "</chamberCaliber>");
                if (!props.ceAmmoSetDefName.NullOrEmpty())
                    builder.AppendLine("<ceAmmoSetDefName>" + props.ceAmmoSetDefName
                        + "</ceAmmoSetDefName>");
                if (props.boreDiameterMm > 0f)
                    builder.AppendLine("<boreDiameterMm>" + Number(props.boreDiameterMm)
                        + "</boreDiameterMm>");
                if (!props.ammunitionCaliber.NullOrEmpty())
                    builder.AppendLine("<ammunitionCaliber>" + Xml(props.ammunitionCaliber)
                        + "</ammunitionCaliber>");
                if (props.projectileDiameterMm > 0f)
                    builder.AppendLine("<projectileDiameterMm>"
                        + Number(props.projectileDiameterMm)
                        + "</projectileDiameterMm>");
                if (props.verticalOccupancy > 0f)
                    builder.AppendLine("<verticalOccupancy>"
                        + Number(props.verticalOccupancy) + "</verticalOccupancy>");
                if (!Mathf.Approximately(props.verticalOccupancyOffset, 0f))
                    builder.AppendLine("<verticalOccupancyOffset>"
                        + Number(props.verticalOccupancyOffset)
                        + "</verticalOccupancyOffset>");

                AppendSight(builder, props.sight);
                AppendFlashlight(builder, props.flashlight);
                AppendLaser(builder, props.laser);
                AppendCasingPort(builder, props.casingPort);
                AppendMounts(builder, props.mounts);
                AppendSockets(builder, props.sockets);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static void AppendStatModifiers(
            StringBuilder builder,
            string elementName,
            List<StatModifier> modifiers)
        {
            if (modifiers.NullOrEmpty()) return;
            builder.AppendLine("<" + elementName + ">");
            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];
                if (modifier?.stat == null) continue;
                builder.AppendLine("  <" + modifier.stat.defName + ">"
                    + Number(modifier.value) + "</" + modifier.stat.defName + ">");
            }
            builder.AppendLine("</" + elementName + ">");
        }

        private static void AppendInternalStats(
            StringBuilder builder,
            ModularWeaponInternalStats stats)
        {
            if (stats == null) return;
            builder.AppendLine("<internalStats>");
            foreach (var field in typeof(ModularWeaponInternalStats).GetFields())
                builder.AppendLine("  <" + field.Name + ">" + Number((float)field.GetValue(stats))
                    + "</" + field.Name + ">");
            builder.AppendLine("</internalStats>");
        }

        private static void AppendStock(StringBuilder builder, ModularWeaponStockProperties stock)
        {
            if (stock == null) return;
            builder.AppendLine("<stock>");
            foreach (var field in typeof(ModularWeaponStockProperties).GetFields())
                builder.AppendLine("  <" + field.Name + ">"
                    + (field.FieldType == typeof(bool) ? ((bool)field.GetValue(stock) ? "true" : "false")
                        : Number((float)field.GetValue(stock))) + "</" + field.Name + ">");
            builder.AppendLine("</stock>");
        }

        private static void AppendSight(
            StringBuilder builder,
            ModularWeaponSightProperties sight)
        {
            if (sight == null) return;
            builder.AppendLine("<sight>");
            builder.AppendLine("  <kind>" + sight.kind + "</kind>");
            builder.AppendLine("  <integrated>" + sight.integrated.ToString().ToLowerInvariant() + "</integrated>");
            builder.AppendLine("  <axisOffset>" + Vector(sight.axisOffset.ToNative())
                + "</axisOffset>");
            builder.AppendLine("  <sightClearanceRadius>" + Number(sight.sightClearanceRadius)
                + "</sightClearanceRadius>");
            builder.AppendLine("  <selectionPriority>"
                + Number(sight.selectionPriority) + "</selectionPriority>");
            builder.AppendLine("</sight>");
        }

        private static void AppendFlashlight(
            StringBuilder builder,
            ModularWeaponFlashlightProperties light)
        {
            if (light == null) return;
            builder.AppendLine("<flashlight>");
            builder.AppendLine("  <emitterOffset>" + Vector(light.emitterOffset.ToNative())
                + "</emitterOffset>");
            builder.AppendLine("  <color>(" + Number(light.color.r) + ","
                + Number(light.color.g) + "," + Number(light.color.b) + ","
                + Number(light.color.a) + ")</color>");
            if (!light.coneTexPath.NullOrEmpty())
                builder.AppendLine("  <coneTexPath>" + Xml(light.coneTexPath)
                    + "</coneTexPath>");
            builder.AppendLine("  <nearWidth>" + Number(light.nearWidth)
                + "</nearWidth>");
            builder.AppendLine("  <farWidth>" + Number(light.farWidth)
                + "</farWidth>");
            if (!Mathf.Approximately(light.lengthOvershoot, 0f))
                builder.AppendLine("  <lengthOvershoot>"
                    + Number(light.lengthOvershoot) + "</lengthOvershoot>");
            if (!light.circleTexPath.NullOrEmpty())
                builder.AppendLine("  <circleTexPath>" + Xml(light.circleTexPath)
                    + "</circleTexPath>");
            builder.AppendLine("  <circleRadius>" + Number(light.circleRadius)
                + "</circleRadius>");
            if (!light.drawCircle)
                builder.AppendLine("  <drawCircle>false</drawCircle>");
            builder.AppendLine("  <closeRange>" + Number(light.closeRange)
                + "</closeRange>");
            builder.AppendLine("  <closeRangeMinScale>"
                + Number(light.closeRangeMinScale) + "</closeRangeMinScale>");
            builder.AppendLine("  <maxRange>" + Number(light.maxRange)
                + "</maxRange>");
            if (light.hediff != null)
                builder.AppendLine("  <hediff>" + light.hediff.defName + "</hediff>");
            builder.AppendLine("  <intensity>" + Number(light.intensity)
                + "</intensity>");
            if (light.affectsMechanoids)
                builder.AppendLine("  <affectsMechanoids>true</affectsMechanoids>");
            if (!light.affectsAnimals)
                builder.AppendLine("  <affectsAnimals>false</affectsAnimals>");
            builder.AppendLine("</flashlight>");
        }

        private static void AppendLaser(
            StringBuilder builder,
            ModularWeaponLaserProperties laser)
        {
            if (laser == null) return;
            builder.AppendLine("<laser>");
            builder.AppendLine("  <emitterOffset>" + Vector(laser.emitterOffset.ToNative())
                + "</emitterOffset>");
            builder.AppendLine("  <color>(" + Number(laser.color.r) + ","
                + Number(laser.color.g) + "," + Number(laser.color.b) + ","
                + Number(laser.color.a) + ")</color>");
            builder.AppendLine("  <beamWidth>" + Number(laser.beamWidth)
                + "</beamWidth>");
            builder.AppendLine("  <dotSize>" + Number(laser.dotSize)
                + "</dotSize>");
            if (!laser.drawDot)
                builder.AppendLine("  <drawDot>false</drawDot>");
            builder.AppendLine("  <maxLength>" + Number(laser.maxLength)
                + "</maxLength>");
            builder.AppendLine("</laser>");
        }

        private static void AppendCasingPort(
            StringBuilder builder,
            ModularWeaponCasingPortProperties port)
        {
            if (port == null) return;
            builder.AppendLine("<casingPort>");
            builder.AppendLine("  <offset>" + Vector(port.offset.ToNative()) + "</offset>");
            builder.AppendLine("  <ejectionAngleOffset>"
                + Number(port.ejectionAngleOffset) + "</ejectionAngleOffset>");
            builder.AppendLine("  <speedMin>" + Number(port.speedMin)
                + "</speedMin>");
            builder.AppendLine("  <speedMax>" + Number(port.speedMax)
                + "</speedMax>");
            builder.AppendLine("  <scale>" + Number(port.scale) + "</scale>");
            builder.AppendLine("  <ejectionCycleFraction>"
                + Number(port.ejectionCycleFraction)
                + "</ejectionCycleFraction>");
            builder.AppendLine("  <feedStartCycleFraction>"
                + Number(port.feedStartCycleFraction)
                + "</feedStartCycleFraction>");
            builder.AppendLine("  <feedEndCycleFraction>"
                + Number(port.feedEndCycleFraction)
                + "</feedEndCycleFraction>");
            builder.AppendLine("  <feedStartOffset>" + Vector(port.feedStartOffset.ToNative())
                + "</feedStartOffset>");
            builder.AppendLine("  <feedEndOffset>" + Vector(port.feedEndOffset.ToNative())
                + "</feedEndOffset>");
            builder.AppendLine("  <feedingRoundDrawSize>"
                + Number(port.feedingRoundDrawSize)
                + "</feedingRoundDrawSize>");
            builder.AppendLine("</casingPort>");
        }

        private static void AppendMounts(
            StringBuilder builder,
            List<ModularAttachmentMount> mounts)
        {
            if (mounts.Count == 0) return;
            builder.AppendLine("<mounts>");
            for (int i = 0; i < mounts.Count; i++)
            {
                ModularAttachmentMount mount = mounts[i];
                builder.AppendLine("  <li>");
                builder.AppendLine("    <id>" + Xml(mount.id) + "</id>");
                if (!mount.label.NullOrEmpty())
                    builder.AppendLine("    <label>" + Xml(mount.label) + "</label>");
                AppendStringList(builder, "socketTags", mount.socketTags, 4);
                if (mount.railContactLength > 0f)
                    builder.AppendLine("    <railContactLength>"
                        + Number(mount.railContactLength)
                        + "</railContactLength>");
                if (mount.physicalLength > 0f)
                    builder.AppendLine("    <physicalLength>"
                        + Number(mount.physicalLength) + "</physicalLength>");
                if (!Mathf.Approximately(mount.physicalCenterOffset, 0f))
                    builder.AppendLine("    <physicalCenterOffset>"
                        + Number(mount.physicalCenterOffset)
                        + "</physicalCenterOffset>");
                if (mount.railSurface != ModularRailSurface.Unspecified)
                    builder.AppendLine("    <railSurface>" + mount.railSurface
                        + "</railSurface>");
                if (!IsAuthoredZeroOffset(mount.oppositeSurfaceOffset.ToNative()))
                    builder.AppendLine("    <oppositeSurfaceOffset>"
                        + Vector(mount.oppositeSurfaceOffset.ToNative())
                        + "</oppositeSurfaceOffset>");
                AppendTransform(builder, mount.transform, 4);
                builder.AppendLine("  </li>");
            }
            builder.AppendLine("</mounts>");
        }

        private static void AppendSockets(
            StringBuilder builder,
            List<ModularAttachmentSocket> sockets)
        {
            if (sockets.Count == 0) return;
            builder.AppendLine("<sockets>");
            for (int i = 0; i < sockets.Count; i++)
            {
                ModularAttachmentSocket socket = sockets[i];
                builder.AppendLine("  <li>");
                builder.AppendLine("    <id>" + Xml(socket.id) + "</id>");
                if (!socket.label.NullOrEmpty())
                    builder.AppendLine("    <label>" + Xml(socket.label) + "</label>");
                AppendStringList(builder, "tags", socket.tags, 4);
                if (socket.required) builder.AppendLine("    <required>true</required>");
                if (socket.expectedPart != null)
                    builder.AppendLine("    <expectedPart>" + socket.expectedPart.defName + "</expectedPart>");
                if (socket.isRail)
                {
                    builder.AppendLine("    <isRail>true</isRail>");
                    builder.AppendLine("    <railLength>" + Number(socket.railLength)
                        + "</railLength>");
                    if (socket.maxAttachments > 0)
                        builder.AppendLine("    <maxAttachments>"
                            + socket.maxAttachments.ToString(CultureInfo.InvariantCulture)
                            + "</maxAttachments>");
                    if (socket.railSurface != ModularRailSurface.Unspecified)
                        builder.AppendLine("    <railSurface>" + socket.railSurface
                            + "</railSurface>");
                }
                AppendTransform(builder, socket.transform, 4);
                builder.AppendLine("  </li>");
            }
            builder.AppendLine("</sockets>");
        }

        private static void AppendStringList(
            StringBuilder builder,
            string name,
            List<string> values,
            int indent)
        {
            if (values.NullOrEmpty()) return;
            string pad = new string(' ', indent);
            builder.AppendLine(pad + "<" + name + ">");
            for (int i = 0; i < values.Count; i++)
                builder.AppendLine(pad + "  <li>" + Xml(values[i]) + "</li>");
            builder.AppendLine(pad + "</" + name + ">");
        }

        private static void AppendTransform(
            StringBuilder builder,
            ModularAttachmentTransform transform,
            int indent)
        {
            string pad = new string(' ', indent);
            builder.AppendLine(pad + "<transform>");
            builder.AppendLine(pad + "  <position>" + Vector(transform.position.ToNative()) + "</position>");
            if (!Mathf.Approximately(transform.angle, 0f))
                builder.AppendLine(pad + "  <angle>" + Number(transform.angle) + "</angle>");
            if (!IsAuthoredUnitScale(transform.scale.ToNative()))
                builder.AppendLine(pad + "  <scale>(" + Number(transform.scale.ToNative().X) + ","
                    + Number(transform.scale.ToNative().Y) + ")</scale>");
            if (!Mathf.Approximately(transform.layer, 0f))
                builder.AppendLine(pad + "  <layer>" + Number(transform.layer) + "</layer>");
            builder.AppendLine(pad + "</transform>");
        }

        // Current 1.7 compatibility vector operators use exact component Equals.
        private static bool IsAuthoredUnitScale(Vector2 value) => value == Vector2.One;

        private static bool IsAuthoredZeroOffset(Vector3 value) => value == Vector3.Zero;

        private static string Vector(Vector3 value)
        {
            return "(" + Number(value.X) + ",0," + Number(value.Z) + ")";
        }

        private static string Number(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Xml(string value) => System.Security.SecurityElement.Escape(value ?? string.Empty);

        private sealed class EditorSnapshot
        {
            public CompModularWeaponNode Owner;
            private readonly List<NodePropertiesState> states =
                new List<NodePropertiesState>();
            private readonly List<NodeRailState> railStates = new List<NodeRailState>();

            public static EditorSnapshot Capture(List<ModularRenderNode> nodes, CompModularWeaponNode owner)
            {
                EditorSnapshot snapshot = new EditorSnapshot {Owner=owner};
                HashSet<CompProperties_ModularWeaponNode> captured =
                    new HashSet<CompProperties_ModularWeaponNode>();
                HashSet<CompModularWeaponNode> capturedComps =
                    new HashSet<CompModularWeaponNode>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    CompProperties_ModularWeaponNode props = nodes[i].Props;
                    if (props != null && captured.Add(props))
                        snapshot.states.Add(new NodePropertiesState(props));
                    CompModularWeaponNode comp = nodes[i].comp;
                    if (comp != null && capturedComps.Add(comp))
                        snapshot.railStates.Add(new NodeRailState(comp));
                }

                return snapshot;
            }

            public void Restore()
            {
                for (int i = 0; i < states.Count; i++) states[i].Restore();
                for (int i = 0; i < railStates.Count; i++) railStates[i].Restore();
            }
        }

        private sealed class NodeRailState
        {
            private readonly CompModularWeaponNode comp;
            private readonly List<float> offsets;

            public NodeRailState(CompModularWeaponNode comp)
            {
                this.comp = comp;
                offsets = comp.RailOffsetsSnapshot();
            }

            public void Restore()
            {
                comp.RestoreRailOffsets(offsets);
            }
        }

        private sealed class NodePropertiesState
        {
            private readonly CompProperties_ModularWeaponNode targetProps;
            private readonly Vector3 graphicOffset;
            private readonly float graphicAngle;
            private readonly Vector2 graphicScale;
            private readonly float graphicLayer;
            private readonly ModularWeaponPartCategory partCategory;
            private readonly ModularWeaponPerformanceRole performanceRole;
            private readonly List<ModularWeaponMissingFunction> missingFunctions;
            private readonly float realisticRoundsPerMinute;
            private readonly float baseFireDelayFactor;
            private readonly float fireDelayMultiplier;
            private readonly float sightGroupHeightTolerance;
            private readonly ModularWeaponInternalStats internalStats;
            private readonly ModularWeaponStockProperties stock;
            private readonly float verticalOccupancy;
            private readonly float verticalOccupancyOffset;
            private readonly int burstShotCountOffset;
            private readonly float burstShotCountMultiplier;
            private readonly float burstShotSpeedMultiplier;
            private readonly ModularWeaponAnimatedPartKind animatedPart;
            private readonly Vector3 animationTravel;
            private readonly Vector3 animationPivot;
            private readonly float animationAngle;
            private readonly ModularWeaponFlashlightProperties flashlight;
            private readonly ModularWeaponLaserProperties laser;
            private readonly ModularWeaponSightProperties sight;
            private readonly ModularWeaponCasingPortProperties casingPort;
            private readonly List<ModularAttachmentMount> mounts;
            private readonly List<ModularAttachmentSocket> sockets;

            public NodePropertiesState(CompProperties_ModularWeaponNode props)
            {
                targetProps = props;
                graphicOffset = props.graphicOffset.ToNative();
                graphicAngle = props.graphicAngle;
                graphicScale = props.graphicScale.ToNative();
                graphicLayer = props.graphicLayer;
                partCategory = props.partCategory;
                performanceRole = props.performanceRole;
                missingFunctions = new List<ModularWeaponMissingFunction>(
                    props.missingFunctions);
                realisticRoundsPerMinute = props.realisticRoundsPerMinute;
                baseFireDelayFactor = props.baseFireDelayFactor;
                fireDelayMultiplier = props.fireDelayMultiplier;
                sightGroupHeightTolerance = props.sightGroupHeightTolerance;
                internalStats = CloneInternalStats(props.internalStats);
                stock = props.stock?.Clone();
                verticalOccupancy = props.verticalOccupancy;
                verticalOccupancyOffset = props.verticalOccupancyOffset;
                burstShotCountOffset = props.burstShotCountOffset;
                burstShotCountMultiplier = props.burstShotCountMultiplier;
                burstShotSpeedMultiplier = props.burstShotSpeedMultiplier;
                animatedPart = props.animatedPart;
                animationTravel = props.animationTravel.ToNative();
                animationPivot = props.animationPivot.ToNative();
                animationAngle = props.animationAngle;
                flashlight = CloneFlashlight(props.flashlight);
                laser = CloneLaser(props.laser);
                sight = CloneSight(props.sight);
                casingPort = CloneCasingPort(props.casingPort);
                mounts = CloneMounts(props.mounts);
                sockets = CloneSockets(props.sockets);
            }

            public void Restore()
            {
                targetProps.graphicOffset = graphicOffset.ToGame();
                targetProps.graphicAngle = graphicAngle;
                targetProps.graphicScale = graphicScale.ToGame();
                targetProps.graphicLayer = graphicLayer;
                targetProps.partCategory = partCategory;
                targetProps.performanceRole = performanceRole;
                targetProps.missingFunctions =
                    new List<ModularWeaponMissingFunction>(missingFunctions);
                targetProps.realisticRoundsPerMinute = realisticRoundsPerMinute;
                targetProps.baseFireDelayFactor = baseFireDelayFactor;
                targetProps.fireDelayMultiplier = fireDelayMultiplier;
                targetProps.sightGroupHeightTolerance = sightGroupHeightTolerance;
                targetProps.internalStats = CloneInternalStats(internalStats);
                targetProps.stock = stock?.Clone();
                targetProps.verticalOccupancy = verticalOccupancy;
                targetProps.verticalOccupancyOffset = verticalOccupancyOffset;
                targetProps.burstShotCountOffset = burstShotCountOffset;
                targetProps.burstShotCountMultiplier = burstShotCountMultiplier;
                targetProps.burstShotSpeedMultiplier = burstShotSpeedMultiplier;
                targetProps.animatedPart = animatedPart;
                targetProps.animationTravel = animationTravel.ToGame();
                targetProps.animationPivot = animationPivot.ToGame();
                targetProps.animationAngle = animationAngle;
                targetProps.flashlight = CloneFlashlight(flashlight);
                targetProps.laser = CloneLaser(laser);
                targetProps.sight = CloneSight(sight);
                targetProps.casingPort = CloneCasingPort(casingPort);
                targetProps.mounts = CloneMounts(mounts);
                targetProps.sockets = CloneSockets(sockets);
            }

            private static ModularWeaponInternalStats CloneInternalStats(
                ModularWeaponInternalStats source)
            {
                return source?.Clone();
            }

            private static ModularWeaponFlashlightProperties CloneFlashlight(
                ModularWeaponFlashlightProperties source)
            {
                if (source == null) return null;
                List<ModularFlashlightTargetOverride> overrides = null;
                if (source.targetOverrides != null)
                {
                    overrides = new List<ModularFlashlightTargetOverride>();
                    for (int i = 0; i < source.targetOverrides.Count; i++)
                    {
                        ModularFlashlightTargetOverride item = source.targetOverrides[i];
                        if (item == null) continue;
                        overrides.Add(new ModularFlashlightTargetOverride
                        {
                            pawnKinds = item.pawnKinds == null
                                ? null
                                : new List<PawnKindDef>(item.pawnKinds),
                            hediff = item.hediff,
                            intensity = item.intensity,
                            severityPerSecond = item.severityPerSecond,
                            requireExisting = item.requireExisting
                        });
                    }
                }

                return new ModularWeaponFlashlightProperties
                {
                    emitterOffset = source.emitterOffset,
                    color = source.color,
                    coneTexPath = source.coneTexPath,
                    nearWidth = source.nearWidth,
                    farWidth = source.farWidth,
                    lengthOvershoot = source.lengthOvershoot,
                    circleTexPath = source.circleTexPath,
                    circleRadius = source.circleRadius,
                    drawCircle = source.drawCircle,
                    closeRange = source.closeRange,
                    closeRangeMinScale = source.closeRangeMinScale,
                    maxRange = source.maxRange,
                    hediff = source.hediff,
                    intensity = source.intensity,
                    targetOverrides = overrides,
                    affectsMechanoids = source.affectsMechanoids,
                    affectsAnimals = source.affectsAnimals
                };
            }

            private static ModularWeaponLaserProperties CloneLaser(
                ModularWeaponLaserProperties source)
            {
                if (source == null) return null;
                return new ModularWeaponLaserProperties
                {
                    emitterOffset = source.emitterOffset,
                    color = source.color,
                    beamWidth = source.beamWidth,
                    dotSize = source.dotSize,
                    drawDot = source.drawDot,
                    maxLength = source.maxLength
                };
            }

            private static ModularWeaponSightProperties CloneSight(
                ModularWeaponSightProperties source)
            {
                if (source == null) return null;
                return new ModularWeaponSightProperties
                {
                    kind = source.kind,
                    integrated = source.integrated,
                    axisOffset = source.axisOffset,
                    sightClearanceRadius = source.sightClearanceRadius,
                    selectionPriority = source.selectionPriority
                };
            }

            private static ModularWeaponCasingPortProperties CloneCasingPort(
                ModularWeaponCasingPortProperties source)
            {
                if (source == null) return null;
                return new ModularWeaponCasingPortProperties
                {
                    offset = source.offset,
                    ejectionAngleOffset = source.ejectionAngleOffset,
                    speedMin = source.speedMin,
                    speedMax = source.speedMax,
                    scale = source.scale,
                    ejectionCycleFraction = source.ejectionCycleFraction,
                    feedStartCycleFraction = source.feedStartCycleFraction,
                    feedEndCycleFraction = source.feedEndCycleFraction,
                    feedStartOffset = source.feedStartOffset,
                    feedEndOffset = source.feedEndOffset,
                    feedingRoundDrawSize = source.feedingRoundDrawSize
                };
            }

            private static List<ModularAttachmentMount> CloneMounts(
                List<ModularAttachmentMount> source)
            {
                List<ModularAttachmentMount> result = new List<ModularAttachmentMount>();
                if (source == null) return result;
                for (int i = 0; i < source.Count; i++)
                {
                    ModularAttachmentMount item = source[i];
                    if (item == null) continue;
                    result.Add(new ModularAttachmentMount
                    {
                        id = item.id,
                        label = item.label,
                        socketTags = item.socketTags == null
                            ? new List<string>()
                            : new List<string>(item.socketTags),
                        transform = item.transform?.Clone()
                            ?? new ModularAttachmentTransform(),
                        railContactLength = item.railContactLength,
                        physicalLength = item.physicalLength,
                        physicalCenterOffset = item.physicalCenterOffset,
                        railSurface = item.railSurface,
                        oppositeSurfaceOffset = item.oppositeSurfaceOffset
                    });
                }

                return result;
            }

            private static List<ModularAttachmentSocket> CloneSockets(
                List<ModularAttachmentSocket> source)
            {
                List<ModularAttachmentSocket> result = new List<ModularAttachmentSocket>();
                if (source == null) return result;
                for (int i = 0; i < source.Count; i++)
                {
                    ModularAttachmentSocket item = source[i];
                    if (item == null) continue;
                    result.Add(new ModularAttachmentSocket
                    {
                        id = item.id,
                        label = item.label,
                        tags = item.tags == null
                            ? new List<string>()
                            : new List<string>(item.tags),
                        transform = item.transform?.Clone()
                            ?? new ModularAttachmentTransform(),
                        required = item.required,
                        expectedPart = item.expectedPart,
                        isRail = item.isRail,
                        railLength = item.railLength,
                        maxAttachments = item.maxAttachments,
                        railSurface = item.railSurface
                    });
                }

                return result;
            }

        }

    }

    public sealed class Dialog_CreateModularSocket : Window
    {
        private readonly CompProperties_ModularWeaponNode owner;
        private readonly Action<ModularAttachmentSocket> onCreated;
        private string socketId;
        private string socketLabel = string.Empty;
        private string socketTypes = "picatinny";
        private bool isRail = true;
        private string railLength = "0.1";
        private ModularRailSurface railSurface = ModularRailSurface.Top;

        public Dialog_CreateModularSocket(
            CompProperties_ModularWeaponNode owner,
            Action<ModularAttachmentSocket> onCreated)
        {
            this.owner = owner;
            this.onCreated = onCreated;
            socketId = NextSocketId(owner);
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(520f, 410f);

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f).ToGame(), "Create attachment socket");
            Text.Font = GameFont.Small;

            float y = 48f;
            DrawTextRow(inRect, ref y, "Socket ID", ref socketId);
            DrawTextRow(inRect, ref y, "Display label", ref socketLabel);
            DrawTextRow(inRect, ref y, "Socket types", ref socketTypes);
            TooltipHandler.TipRegion(new Rect(130f, y - 34f, inRect.width - 130f, 28f).ToGame(),
                "Comma-separated types. A mount is compatible when at least one type matches.");

            Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 28f).ToGame(),
                "Continuous horizontal rail", ref isRail);
            y += 34f;
            if (isRail)
            {
                DrawTextRow(inRect, ref y, "Rail length", ref railLength);
                Widgets.Label(new Rect(0f, y + 3f, 122f, 26f).ToGame(), "Rail surface");
                if (Widgets.ButtonText(new Rect(130f, y,
                    inRect.width - 130f, 28f).ToGame(),
                    Dialog_ModularAttachmentEditor.RailSurfaceLabel(railSurface)))
                    railSurface = Dialog_ModularAttachmentEditor
                        .NextRailSurface(railSurface);
                y += 38f;
            }

            Widgets.Label(new Rect(0f, y + 8f, inRect.width, 48f).ToGame(),
                "Examples: picatinny, top_rail, receiver_rail\n"
                + "The new socket starts at (0, 0) and can be moved immediately in the editor.");

            Rect buttons = new Rect(0f, inRect.height - 38f, inRect.width, 34f);
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, 120f, 32f).ToGame(), "Cancel"))
                Close();
            if (Widgets.ButtonText(new Rect(buttons.xMax - 140f, buttons.y, 140f, 32f).ToGame(),
                "Create socket"))
                Create();
        }

        private void Create()
        {
            socketId = socketId?.Trim();
            if (socketId.NullOrEmpty())
            {
                Messages.Message("Socket ID cannot be empty.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (owner.SocketNamed(socketId) != null)
            {
                Messages.Message("A socket named " + socketId + " already exists.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<string> types = Dialog_ModularAttachmentEditor.ParseTags(socketTypes);
            if (types.Count == 0)
            {
                Messages.Message("Enter at least one socket type.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            float parsedRailLength = 0f;
            if (isRail && (!float.TryParse(railLength, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsedRailLength)
                || parsedRailLength <= 0f))
            {
                Messages.Message("Rail length must be a positive number.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            onCreated?.Invoke(new ModularAttachmentSocket
            {
                id = socketId,
                label = socketLabel?.Trim(),
                tags = types,
                transform = new ModularAttachmentTransform(),
                isRail = isRail,
                railLength = parsedRailLength,
                railSurface = isRail
                    ? railSurface
                    : ModularRailSurface.Unspecified
            });
            Close();
        }

        private static void DrawTextRow(Rect inRect, ref float y, string label, ref string value)
        {
            Widgets.Label(new Rect(0f, y + 3f, 122f, 26f).ToGame(), label);
            value = Widgets.TextField(new Rect(130f, y, inRect.width - 130f, 28f).ToGame(), value ?? string.Empty);
            y += 38f;
        }

        private static string NextSocketId(CompProperties_ModularWeaponNode owner)
        {
            int index = 1;
            string id;
            do id = "socket_" + index++.ToString("00", CultureInfo.InvariantCulture);
            while (owner.SocketNamed(id) != null);
            return id;
        }
    }
}
