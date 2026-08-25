using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class Dialog_ModularAttachmentEditor : Window
    {
        private enum EditTarget
        {
            Graphic,
            Mount,
            Socket
        }

        private enum MovementConstraint
        {
            Free,
            Horizontal,
            Vertical
        }

        private readonly CompModularWeaponNode root;
        private string selectedPath = "root";
        private int selectedMountIndex;
        private int selectedSocketIndex;
        private EditTarget target = EditTarget.Socket;
        private MovementConstraint movementConstraint = MovementConstraint.Free;
        private Vector2 treeScroll;
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
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(1380f, 850f);

        public override void DoWindowContents(Rect inRect)
        {
            HandleUndoInput();
            if (Event.current.type == EventType.MouseUp) activeEditKey = null;

            if (root?.parent == null || root.parent.Destroyed)
            {
                Widgets.Label(inRect, "The edited weapon no longer exists.");
                return;
            }

            List<ModularRenderNode> snapshot = root.RenderSnapshot();
            ModularRenderNode selected = FindSelected(snapshot);
            float header = 38f;
            Rect treeRect = new Rect(0f, header, 290f, inRect.height - header - 46f);
            Rect previewRect = new Rect(302f, header, inRect.width - 682f,
                inRect.height - header - 46f);
            Rect inspectorRect = new Rect(inRect.width - 368f, header, 368f,
                inRect.height - header - 46f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f),
                "Modular attachment editor — " + root.parent.LabelCap);
            Text.Font = GameFont.Small;

            Rect liveRect = new Rect(inRect.width - 190f, 2f, 188f, 29f);
            GUI.color = new Color(0.55f, 1f, 0.58f);
            if (Widgets.ButtonText(liveRect, "LIVE APPLY • refresh now"))
                root.InvalidateTree();
            GUI.color = Color.white;

            DrawTree(treeRect, snapshot);
            DrawPreview(previewRect, snapshot, selected);
            DrawInspector(inspectorRect, selected);

            Rect footer = new Rect(0f, inRect.height - 38f, inRect.width, 38f);
            if (Widgets.ButtonText(new Rect(footer.x, footer.y + 4f, 125f, 30f),
                "Copy XML"))
            {
                string xml = BuildXml(snapshot);
                GUIUtility.systemCopyBuffer = xml;
                Log.Message("[Helodrace] Modular attachment editor export:\n" + xml);
                Messages.Message("Modular attachment XML copied to clipboard.",
                    MessageTypeDefOf.PositiveEvent, false);
            }

            if (Widgets.ButtonText(new Rect(footer.x + 133f, footer.y + 4f, 135f, 30f),
                "Save XML files"))
                SaveXmlFiles(snapshot);

            if (Widgets.ButtonText(new Rect(footer.x + 276f, footer.y + 4f, 105f, 30f),
                "Reset view"))
            {
                zoom = 1f;
                pan = Vector2.zero;
            }

            if (Widgets.ButtonText(new Rect(footer.x + 389f, footer.y + 4f, 145f, 30f),
                "Undo (Ctrl+Z) " + undoHistory.Count))
                UndoLastEdit();

            Widgets.Label(new Rect(footer.x + 546f, footer.y + 8f,
                footer.width - 690f, 26f),
                "Every edit is applied to the live weapon immediately.");

            if (Widgets.ButtonText(new Rect(footer.xMax - 120f, footer.y + 4f, 120f, 30f),
                "CloseButton".Translate()))
                Close();
        }

        private void DrawTree(Rect rect, List<ModularRenderNode> snapshot)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f),
                "Assembly tree");

            Rect outRect = new Rect(rect.x + 4f, rect.y + 34f, rect.width - 8f, rect.height - 38f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f,
                Mathf.Max(outRect.height, snapshot.Count * 30f + 8f));
            Widgets.BeginScrollView(outRect, ref treeScroll, viewRect);

            List<ModularRenderNode> treeOrder = new List<ModularRenderNode>(snapshot);
            treeOrder.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            float y = 4f;
            for (int i = 0; i < treeOrder.Count; i++)
            {
                ModularRenderNode node = treeOrder[i];
                Rect row = new Rect(4f + node.depth * 15f, y,
                    viewRect.width - 8f - node.depth * 15f, 25f);
                if (node.path == selectedPath) Widgets.DrawHighlightSelected(row);
                if (Widgets.ButtonInvisible(row))
                {
                    activeEditKey = null;
                    selectedPath = node.path;
                    selectedMountIndex = 0;
                    selectedSocketIndex = 0;
                }

                Widgets.Label(row.ContractedBy(4f, 2f),
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
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.055f, 0.06f, 0.07f, 1f),
                new Color(0.35f, 0.38f, 0.42f), 2);

            Rect inner = rect.ContractedBy(14f);
            HandlePreviewNavigation(inner);
            Vector2 origin = inner.center + pan;
            Graphic rootGraphic = root.parent.Graphic;
            Vector2 rootSize = rootGraphic?.drawSize ?? Vector2.one;
            float cells = Mathf.Max(rootSize.x, rootSize.y, 0.01f);
            float pixelsPerCell = Mathf.Min(inner.width, inner.height) * 0.86f * zoom / cells;

            // The production renderer places every _Outline texture below the complete
            // assembly. Mirror that ordering here before drawing any normal part texture.
            for (int i = 0; i < snapshot.Count; i++)
            {
                ModularRenderNode node = snapshot[i];
                Graphic graphic = node.thing.Graphic;
                string texturePath = node.thing.def.graphicData?.texPath;
                Texture2D outline = texturePath.NullOrEmpty()
                    ? null
                    : ContentFinder<Texture2D>.Get(texturePath + "_Outline", false);
                if (graphic == null || outline == null) continue;

                Vector2 center = WorldToGui(node.GraphicCenter, origin, pixelsPerCell);
                Vector2 size = Vector2.Scale(graphic.drawSize, node.GraphicScale)
                    * pixelsPerCell;
                Rect drawRect = CenteredRect(center, size);
                Matrix4x4 matrix = GUI.matrix;
                if (!Mathf.Approximately(node.GraphicAngle, 0f))
                    UI.RotateAroundPivot(-node.GraphicAngle, drawRect.center);
                GUI.DrawTexture(drawRect, outline, ScaleMode.StretchToFill);
                GUI.matrix = matrix;
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                ModularRenderNode node = snapshot[i];
                Graphic graphic = node.thing.Graphic;
                Texture texture = graphic?.MatSingle?.mainTexture;
                if (texture == null) continue;

                Vector2 center = WorldToGui(node.GraphicCenter, origin, pixelsPerCell);
                Vector2 size = Vector2.Scale(graphic.drawSize, node.GraphicScale)
                    * pixelsPerCell;
                Rect drawRect = CenteredRect(center, size);
                Color old = GUI.color;
                GUI.color = node.path == selectedPath
                    ? graphic.Color
                    : new Color(graphic.Color.r, graphic.Color.g, graphic.Color.b, 0.72f);
                Matrix4x4 matrix = GUI.matrix;
                if (!Mathf.Approximately(node.GraphicAngle, 0f))
                    UI.RotateAroundPivot(-node.GraphicAngle, drawRect.center);
                GUI.DrawTexture(drawRect, texture, ScaleMode.StretchToFill);
                GUI.matrix = matrix;
                GUI.color = old;
            }

            if (selected != null)
            {
                DrawMarkers(selected, origin, pixelsPerCell);
                HandleMarkerDrag(inner, selected, pixelsPerCell);
            }

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x + 4f, inner.y + 4f, inner.width - 8f, 22f),
                "Wheel: zoom   Middle drag: pan   Left drag: move   Shift: temporary axis lock   "
                + AxisLabel() + "   "
                + zoom.ToString("0.00", CultureInfo.InvariantCulture) + "x");
            Text.Font = GameFont.Small;
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
                Widgets.DrawLine(railStart, railEnd,
                    new Color(0.15f, 0.65f, 1f), 4f);
                Widgets.DrawLine(occupiedStart, occupiedEnd, Color.yellow, 7f);
                DrawCross(occupiedStart, Color.yellow, 4f);
                DrawCross(occupiedEnd, Color.yellow, 4f);
            }

            Vector2 logical = WorldToGui(node.transform.position, origin, pixelsPerCell);
            DrawCross(logical, Color.red, 7f);

            Vector2 graphic = WorldToGui(node.GraphicCenter, origin, pixelsPerCell);
            DrawCross(graphic, target == EditTarget.Graphic ? Color.yellow : Color.white, 6f);

            for (int i = 0; i < node.Props.mounts.Count; i++)
            {
                ModularAttachmentMount mount = node.Props.mounts[i];
                Vector2 point = WorldToGui(node.transform.TransformPoint(mount.transform.position),
                    origin, pixelsPerCell);
                Color color = target == EditTarget.Mount && i == selectedMountIndex
                    ? Color.yellow : new Color(1f, 0.45f, 0.08f);
                if (mount.railOccupancy > 0f)
                {
                    Vector3 leftLocal = mount.transform.position;
                    Vector3 rightLocal = mount.transform.position;
                    leftLocal.x -= mount.railOccupancy * 0.5f;
                    rightLocal.x += mount.railOccupancy * 0.5f;
                    Vector2 left = WorldToGui(node.transform.TransformPoint(leftLocal),
                        origin, pixelsPerCell);
                    Vector2 right = WorldToGui(node.transform.TransformPoint(rightLocal),
                        origin, pixelsPerCell);
                    Widgets.DrawLine(left, right, color,
                        target == EditTarget.Mount && i == selectedMountIndex ? 7f : 3f);
                    DrawCross(left, color, 3f);
                    DrawCross(right, color, 3f);
                }
                Widgets.DrawBox(new Rect(point.x - 5f, point.y - 5f, 10f, 10f), 2, null);
                GUI.color = color;
                Widgets.DrawBox(new Rect(point.x - 4f, point.y - 4f, 8f, 8f), 2, null);
                GUI.color = Color.white;
            }

            for (int i = 0; i < node.Props.sockets.Count; i++)
            {
                ModularAttachmentSocket socket = node.Props.sockets[i];
                Vector2 point = WorldToGui(node.transform.TransformPoint(socket.transform.position),
                    origin, pixelsPerCell);
                Color color = target == EditTarget.Socket && i == selectedSocketIndex
                    ? Color.yellow : new Color(0.15f, 0.65f, 1f);
                if (socket.isRail && socket.railLength > 0f)
                {
                    Vector3 leftLocal = socket.transform.position;
                    Vector3 rightLocal = socket.transform.position;
                    leftLocal.x -= socket.railLength * 0.5f;
                    rightLocal.x += socket.railLength * 0.5f;
                    Vector2 left = WorldToGui(node.transform.TransformPoint(leftLocal),
                        origin, pixelsPerCell);
                    Vector2 right = WorldToGui(node.transform.TransformPoint(rightLocal),
                        origin, pixelsPerCell);
                    Widgets.DrawLine(left, right, color, 3f);
                }
                DrawCross(point, color, 5f);
                Text.Font = GameFont.Tiny;
                GUI.color = color;
                Widgets.Label(new Rect(point.x + 6f, point.y - 9f, 130f, 18f), socket.id);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }

        private void DrawInspector(Rect rect, ModularRenderNode node)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            if (node == null)
            {
                Widgets.Label(inner, "Select a node.");
                return;
            }

            float y = inner.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f), node.thing.LabelCap);
            Text.Font = GameFont.Small;
            y += 34f;

            float buttonWidth = (inner.width - 12f) / 3f;
            if (ModeButton(new Rect(inner.x, y, buttonWidth, 28f), "Graphic",
                target == EditTarget.Graphic)) SetTarget(EditTarget.Graphic);
            if (ModeButton(new Rect(inner.x + buttonWidth + 6f, y, buttonWidth, 28f), "Mount",
                target == EditTarget.Mount)) SetTarget(EditTarget.Mount);
            if (ModeButton(new Rect(inner.x + (buttonWidth + 6f) * 2f, y, buttonWidth, 28f), "Socket",
                target == EditTarget.Socket)) SetTarget(EditTarget.Socket);
            y += 38f;

            Widgets.Label(new Rect(inner.x, y + 3f, 58f, 24f), "Move");
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

            DrawFirePerformanceEditor(inner, node, ref y);
            DrawAttachedRailPlacement(inner, node, ref y);

            if (target == EditTarget.Socket && !node.Props.sockets.NullOrEmpty())
            {
                selectedSocketIndex = Mathf.Clamp(
                    selectedSocketIndex, 0, node.Props.sockets.Count - 1);
                ModularAttachmentSocket socket = node.Props.sockets[selectedSocketIndex];
                Widgets.Label(new Rect(inner.x, y, 92f, 24f), "Socket types");
                string key = node.path + ":socketTypes:" + selectedSocketIndex;
                string value;
                if (!textBuffers.TryGetValue(key, out value))
                    value = socket.tags.NullOrEmpty()
                        ? string.Empty
                        : string.Join(", ", socket.tags.ToArray());
                string edited = Widgets.TextField(
                    new Rect(inner.x + 96f, y, inner.width - 96f, 24f), value);
                textBuffers[key] = edited;
                if (edited != value)
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":socketTypes");
                    socket.tags = ParseTags(edited);
                    root.InvalidateTree();
                }
                TooltipHandler.TipRegion(new Rect(inner.x + 96f, y, inner.width - 96f, 24f),
                    "Comma-separated compatibility types, for example: picatinny, top_rail");
                y += 32f;

                bool rail = socket.isRail;
                Widgets.CheckboxLabeled(new Rect(inner.x, y, inner.width, 24f),
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
                }
            }
            else if (target == EditTarget.Mount && !node.Props.mounts.NullOrEmpty())
            {
                selectedMountIndex = Mathf.Clamp(
                    selectedMountIndex, 0, node.Props.mounts.Count - 1);
                ModularAttachmentMount mount = node.Props.mounts[selectedMountIndex];
                float occupancy = SliderRow(inner, ref y, "Occupied",
                    mount.railOccupancy, 0f, 1.5f);
                if (!Mathf.Approximately(occupancy, mount.railOccupancy))
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node) + ":railOccupancy");
                    mount.railOccupancy = occupancy;
                    root.InvalidateTree();
                }
                TooltipHandler.TipRegion(new Rect(inner.x, y - 28f, inner.width, 24f),
                    "Occupied rail length. The orange/yellow segment in the preview "
                    + "shows the exact interval used by this part.");
            }

            ModularAttachmentTransform transform = target == EditTarget.Graphic
                ? new ModularAttachmentTransform
                {
                    position = node.Props.graphicOffset,
                    angle = node.Props.graphicAngle,
                    scale = node.Props.graphicScale,
                    layer = node.Props.graphicLayer
                }
                : ActiveTransform(node);
            if (transform == null)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 25f),
                    target == EditTarget.Mount ? "This node has no mount." : "This node has no socket.");
                y += 32f;
            }
            else
            {
                bool changed = false;
                float x = SliderRow(inner, ref y, "X", transform.position.x, -1.5f, 1.5f);
                float z = SliderRow(inner, ref y, "Z", transform.position.z, -1.5f, 1.5f);
                string angleLabel = target == EditTarget.Graphic
                    ? "Assembly angle"
                    : "Angle";
                float angle = SliderRow(inner, ref y, angleLabel, transform.angle, -180f, 180f);
                ModularAttachmentSocket activeSocket = target == EditTarget.Socket
                    && !node.Props.sockets.NullOrEmpty()
                    ? node.Props.sockets[selectedSocketIndex]
                    : null;
                if (activeSocket?.isRail == true) angle = 0f;
                float scaleX = SliderRow(inner, ref y, "Scale X", transform.scale.x, 0.1f, 3f);
                float scaleY = SliderRow(inner, ref y, "Scale Z", transform.scale.y, 0.1f, 3f);
                float layer = SliderRow(inner, ref y, "Layer", transform.layer, -20f, 20f);

                changed |= !Mathf.Approximately(x, transform.position.x)
                    || !Mathf.Approximately(z, transform.position.z)
                    || !Mathf.Approximately(angle, transform.angle)
                    || !Mathf.Approximately(scaleX, transform.scale.x)
                    || !Mathf.Approximately(scaleY, transform.scale.y)
                    || !Mathf.Approximately(layer, transform.layer);
                if (changed)
                {
                    EnsureUndoCheckpoint(CurrentEditKey(node));
                    transform.position = new Vector3(x, 0f, z);
                    transform.angle = angle;
                    transform.scale = new Vector2(scaleX, scaleY);
                    transform.layer = layer;
                    if (target == EditTarget.Graphic)
                    {
                        node.Props.graphicOffset = transform.position;
                        node.Props.graphicAngle = transform.angle;
                        node.Props.graphicScale = transform.scale;
                        node.Props.graphicLayer = transform.layer;
                    }
                    root.InvalidateTree();
                }
            }

            y += 8f;
            if (target == EditTarget.Mount)
                DrawMountSelector(inner, node, ref y);
            else if (target == EditTarget.Socket)
                DrawSocketSelector(inner, node, ref y);

            y += 10f;
            Widgets.Label(new Rect(inner.x, y, inner.width, 90f),
                "Red: logical origin\nWhite: texture centre\nOrange: incoming mount\nBlue: outgoing socket\nYellow: active edit target");
        }

        private void DrawFirePerformanceEditor(
            Rect inner,
            ModularRenderNode node,
            ref float y)
        {
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
            TooltipHandler.TipRegion(new Rect(inner.x, y - 28f, inner.width, 24f),
                "Multiplies fire delay. Values below 1 fire faster, but the root weapon's "
                + "real RPM always remains the mechanical lower-delay limit.");
            y += 4f;
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
            float occupancy = Mathf.Max(0f, mount?.railOccupancy ?? 0f);
            float limit = Mathf.Max(0f, (socket.railLength - occupancy) * 0.5f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f),
                "Rail: " + socket.Label + "   occupied "
                + occupancy.ToString("0.###", CultureInfo.InvariantCulture));
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
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Mount points");
            y += 24f;
            for (int i = 0; i < node.Props.mounts.Count; i++)
            {
                Rect row = new Rect(inner.x, y, inner.width, 25f);
                if (i == selectedMountIndex) Widgets.DrawHighlightSelected(row);
                if (Widgets.ButtonText(row, node.Props.mounts[i].Label))
                {
                    activeEditKey = null;
                    selectedMountIndex = i;
                }
                y += 28f;
            }

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 25f), "+ Add mount"))
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
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Sockets");
            y += 24f;
            for (int i = 0; i < node.Props.sockets.Count; i++)
            {
                Rect row = new Rect(inner.x, y, inner.width, 25f);
                if (i == selectedSocketIndex) Widgets.DrawHighlightSelected(row);
                if (Widgets.ButtonText(row, node.Props.sockets[i].Label))
                {
                    activeEditKey = null;
                    selectedSocketIndex = i;
                }
                y += 28f;
            }

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 25f), "+ Add socket"))
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
            if (!rect.Contains(current.mousePosition)) return;

            if (current.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * (1f - current.delta.y * 0.07f), 0.2f, 8f);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 2)
            {
                pan += current.delta;
                current.Use();
            }
        }

        private void HandleMarkerDrag(Rect rect, ModularRenderNode node, float pixelsPerCell)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDrag || current.button != 0
                || !rect.Contains(current.mousePosition)) return;

            Vector2 rootDelta = new Vector2(
                current.delta.x / pixelsPerCell,
                -current.delta.y / pixelsPerCell);

            if (current.shift && movementConstraint == MovementConstraint.Free)
            {
                if (Mathf.Abs(rootDelta.x) >= Mathf.Abs(rootDelta.y)) rootDelta.y = 0f;
                else rootDelta.x = 0f;
            }
            else if (movementConstraint == MovementConstraint.Horizontal)
            {
                rootDelta.y = 0f;
            }
            else if (movementConstraint == MovementConstraint.Vertical)
            {
                rootDelta.x = 0f;
            }

            Vector2 localDelta = ModularTransform2D.Rotate(rootDelta, -node.transform.angle);
            localDelta = new Vector2(
                localDelta.x / Mathf.Max(0.0001f, node.transform.scale.x),
                localDelta.y / Mathf.Max(0.0001f, node.transform.scale.y));
            Vector3 delta = new Vector3(localDelta.x, 0f, localDelta.y);
            EnsureUndoCheckpoint(CurrentEditKey(node) + ":drag");
            if (target == EditTarget.Graphic)
            {
                node.Props.graphicOffset += delta;
            }
            else
            {
                ModularAttachmentTransform transform = ActiveTransform(node);
                if (transform == null) return;
                transform.position += delta;
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
            undoHistory.Add(EditorSnapshot.Capture(root.RenderSnapshot()));
            if (undoHistory.Count > 100) undoHistory.RemoveAt(0);
        }

        private void UndoLastEdit()
        {
            if (undoHistory.Count == 0) return;
            int index = undoHistory.Count - 1;
            EditorSnapshot snapshot = undoHistory[index];
            undoHistory.RemoveAt(index);
            snapshot.Restore();
            activeEditKey = null;
            numericBuffers.Clear();
            textBuffers.Clear();
            root.InvalidateTree();
        }

        private ModularRenderNode FindSelected(List<ModularRenderNode> snapshot)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].path == selectedPath) return snapshot[i];
            }

            ModularRenderNode fallback = snapshot.Count > 0 ? snapshot[0] : null;
            selectedPath = fallback?.path;
            return fallback;
        }

        private static bool ModeButton(Rect rect, string label, bool selected)
        {
            if (selected) Widgets.DrawHighlightSelected(rect);
            return Widgets.ButtonText(rect, label);
        }

        private float SliderRow(
            Rect inner,
            ref float y,
            string label,
            float value,
            float min,
            float max)
        {
            Widgets.Label(new Rect(inner.x, y, 86f, 24f), label);
            float result = Widgets.HorizontalSlider(
                new Rect(inner.x + 90f, y + 3f, inner.width - 180f, 18f),
                value, min, max, true);
            string key = selectedPath + ":" + target + ":" + selectedMountIndex + ":"
                + selectedSocketIndex + ":" + label;
            string buffer;
            if (!numericBuffers.TryGetValue(key, out buffer))
                buffer = result.ToString("0.###", CultureInfo.InvariantCulture);
            Widgets.TextFieldNumeric(new Rect(inner.xMax - 82f, y, 82f, 24f),
                ref result, ref buffer, min, max);
            numericBuffers[key] = buffer;
            y += 28f;
            return result;
        }

        private static Vector2 WorldToGui(Vector2 point, Vector2 origin, float pixelsPerCell)
        {
            return new Vector2(
                origin.x + point.x * pixelsPerCell,
                origin.y - point.y * pixelsPerCell);
        }

        private static Rect CenteredRect(Vector2 center, Vector2 size)
        {
            return new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f,
                size.x, size.y);
        }

        private static void DrawCross(Vector2 point, Color color, float size)
        {
            Widgets.DrawLine(new Vector2(point.x - size, point.y),
                new Vector2(point.x + size, point.y), color, 2f);
            Widgets.DrawLine(new Vector2(point.x, point.y - size),
                new Vector2(point.x, point.y + size), color, 2f);
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

        private static void SaveXmlFiles(List<ModularRenderNode> snapshot)
        {
            try
            {
                string directory = Path.Combine(
                    GenFilePaths.SaveDataFolderPath,
                    "Helodrace",
                    "ModularWeaponExports",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);

                HashSet<ThingDef> exported = new HashSet<ThingDef>();
                int count = 0;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    ModularRenderNode node = snapshot[i];
                    ThingDef def = node.thing.def;
                    if (!exported.Add(def)) continue;

                    string fragment = BuildXml(new List<ModularRenderNode> { node });
                    string content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                        + "<ModularWeaponPlacementExport>\n"
                        + "  <thingDef>" + def.defName + "</thingDef>\n"
                        + Indent(fragment, "  ")
                        + "</ModularWeaponPlacementExport>\n";
                    string path = Path.Combine(directory,
                        SafeFileName(def.defName) + "_Placement.xml");
                    File.WriteAllText(path, content, new UTF8Encoding(false));
                    count++;
                }

                Messages.Message(
                    "Saved " + count + " modular placement XML files to " + directory,
                    MessageTypeDefOf.PositiveEvent,
                    false);
                Log.Message("[Helodrace] Modular placement files saved to " + directory);
            }
            catch (Exception exception)
            {
                Log.Error("[Helodrace] Failed to save modular placement XML files: " + exception);
                Messages.Message(
                    "Could not save modular placement files. See the log for details.",
                    MessageTypeDefOf.RejectInput,
                    false);
            }
        }

        private static string Indent(string value, string prefix)
        {
            string normalized = value.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == lines.Length - 1 && lines[i].Length == 0) continue;
                builder.Append(prefix).AppendLine(lines[i]);
            }
            return builder.ToString();
        }

        private static string SafeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value ?? "ModularPart");
            for (int i = 0; i < builder.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (builder[i] != invalid[j]) continue;
                    builder[i] = '_';
                    break;
                }
            }
            return builder.ToString();
        }

        private static string BuildXml(List<ModularRenderNode> snapshot)
        {
            StringBuilder builder = new StringBuilder();
            HashSet<ThingDef> exported = new HashSet<ThingDef>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                ModularRenderNode node = snapshot[i];
                if (!exported.Add(node.thing.def)) continue;
                CompProperties_ModularWeaponNode props = node.Props;

                builder.AppendLine("<!-- " + node.thing.def.defName + " -->");
                if (props.isAssemblyRoot)
                {
                    builder.AppendLine("<realisticRoundsPerMinute>"
                        + Number(props.realisticRoundsPerMinute)
                        + "</realisticRoundsPerMinute>");
                    builder.AppendLine("<baseFireDelayFactor>"
                        + Number(props.baseFireDelayFactor)
                        + "</baseFireDelayFactor>");
                }
                if (!Mathf.Approximately(props.fireDelayMultiplier, 1f))
                    builder.AppendLine("<fireDelayMultiplier>"
                        + Number(props.fireDelayMultiplier) + "</fireDelayMultiplier>");
                builder.AppendLine("<graphicOffset>" + Vector(props.graphicOffset) + "</graphicOffset>");
                if (!Mathf.Approximately(props.graphicAngle, 0f))
                    builder.AppendLine("<graphicAngle>" + Number(props.graphicAngle) + "</graphicAngle>");
                if (props.graphicScale != Vector2.one)
                    builder.AppendLine("<graphicScale>(" + Number(props.graphicScale.x) + ","
                        + Number(props.graphicScale.y) + ")</graphicScale>");
                if (!Mathf.Approximately(props.graphicLayer, 0f))
                    builder.AppendLine("<graphicLayer>" + Number(props.graphicLayer) + "</graphicLayer>");

                AppendMounts(builder, props.mounts);
                AppendSockets(builder, props.sockets);
                AppendDefaultAttachments(builder, props.defaultAttachments);
                builder.AppendLine();
            }

            return builder.ToString();
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
                builder.AppendLine("    <id>" + mount.id + "</id>");
                if (!mount.label.NullOrEmpty())
                    builder.AppendLine("    <label>" + mount.label + "</label>");
                AppendStringList(builder, "socketTags", mount.socketTags, 4);
                if (mount.railOccupancy > 0f)
                    builder.AppendLine("    <railOccupancy>" + Number(mount.railOccupancy)
                        + "</railOccupancy>");
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
                builder.AppendLine("    <id>" + socket.id + "</id>");
                if (!socket.label.NullOrEmpty())
                    builder.AppendLine("    <label>" + socket.label + "</label>");
                AppendStringList(builder, "tags", socket.tags, 4);
                if (socket.required) builder.AppendLine("    <required>true</required>");
                if (socket.isRail)
                {
                    builder.AppendLine("    <isRail>true</isRail>");
                    builder.AppendLine("    <railLength>" + Number(socket.railLength)
                        + "</railLength>");
                    if (socket.maxAttachments > 0)
                        builder.AppendLine("    <maxAttachments>"
                            + socket.maxAttachments.ToString(CultureInfo.InvariantCulture)
                            + "</maxAttachments>");
                }
                AppendTransform(builder, socket.transform, 4);
                builder.AppendLine("  </li>");
            }
            builder.AppendLine("</sockets>");
        }

        private static void AppendDefaultAttachments(
            StringBuilder builder,
            List<ModularDefaultAttachment> attachments)
        {
            if (attachments.NullOrEmpty()) return;
            builder.AppendLine("<defaultAttachments>");
            for (int i = 0; i < attachments.Count; i++)
            {
                ModularDefaultAttachment item = attachments[i];
                if (item?.part == null) continue;
                builder.AppendLine("  <li>");
                builder.AppendLine("    <socketId>" + item.socketId + "</socketId>");
                if (!item.mountId.NullOrEmpty())
                    builder.AppendLine("    <mountId>" + item.mountId + "</mountId>");
                builder.AppendLine("    <part>" + item.part.defName + "</part>");
                if (!Mathf.Approximately(item.railOffset, 0f))
                    builder.AppendLine("    <railOffset>" + Number(item.railOffset)
                        + "</railOffset>");
                builder.AppendLine("  </li>");
            }
            builder.AppendLine("</defaultAttachments>");
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
                builder.AppendLine(pad + "  <li>" + values[i] + "</li>");
            builder.AppendLine(pad + "</" + name + ">");
        }

        private static void AppendTransform(
            StringBuilder builder,
            ModularAttachmentTransform transform,
            int indent)
        {
            string pad = new string(' ', indent);
            builder.AppendLine(pad + "<transform>");
            builder.AppendLine(pad + "  <position>" + Vector(transform.position) + "</position>");
            if (!Mathf.Approximately(transform.angle, 0f))
                builder.AppendLine(pad + "  <angle>" + Number(transform.angle) + "</angle>");
            if (transform.scale != Vector2.one)
                builder.AppendLine(pad + "  <scale>(" + Number(transform.scale.x) + ","
                    + Number(transform.scale.y) + ")</scale>");
            if (!Mathf.Approximately(transform.layer, 0f))
                builder.AppendLine(pad + "  <layer>" + Number(transform.layer) + "</layer>");
            builder.AppendLine(pad + "</transform>");
        }

        private static string Vector(Vector3 value)
        {
            return "(" + Number(value.x) + ",0," + Number(value.z) + ")";
        }

        private static string Number(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private sealed class EditorSnapshot
        {
            private readonly List<NodePropertiesState> states =
                new List<NodePropertiesState>();
            private readonly List<NodeRailState> railStates = new List<NodeRailState>();

            public static EditorSnapshot Capture(List<ModularRenderNode> nodes)
            {
                EditorSnapshot snapshot = new EditorSnapshot();
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
            private readonly float realisticRoundsPerMinute;
            private readonly float baseFireDelayFactor;
            private readonly float fireDelayMultiplier;
            private readonly List<ModularAttachmentMount> mounts;
            private readonly List<ModularAttachmentSocket> sockets;
            private readonly List<ModularDefaultAttachment> defaultAttachments;

            public NodePropertiesState(CompProperties_ModularWeaponNode props)
            {
                targetProps = props;
                graphicOffset = props.graphicOffset;
                graphicAngle = props.graphicAngle;
                graphicScale = props.graphicScale;
                graphicLayer = props.graphicLayer;
                realisticRoundsPerMinute = props.realisticRoundsPerMinute;
                baseFireDelayFactor = props.baseFireDelayFactor;
                fireDelayMultiplier = props.fireDelayMultiplier;
                mounts = CloneMounts(props.mounts);
                sockets = CloneSockets(props.sockets);
                defaultAttachments = CloneDefaultAttachments(props.defaultAttachments);
            }

            public void Restore()
            {
                targetProps.graphicOffset = graphicOffset;
                targetProps.graphicAngle = graphicAngle;
                targetProps.graphicScale = graphicScale;
                targetProps.graphicLayer = graphicLayer;
                targetProps.realisticRoundsPerMinute = realisticRoundsPerMinute;
                targetProps.baseFireDelayFactor = baseFireDelayFactor;
                targetProps.fireDelayMultiplier = fireDelayMultiplier;
                targetProps.mounts = CloneMounts(mounts);
                targetProps.sockets = CloneSockets(sockets);
                targetProps.defaultAttachments = CloneDefaultAttachments(defaultAttachments);
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
                        railOccupancy = item.railOccupancy
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
                        isRail = item.isRail,
                        railLength = item.railLength,
                        maxAttachments = item.maxAttachments
                    });
                }

                return result;
            }

            private static List<ModularDefaultAttachment> CloneDefaultAttachments(
                List<ModularDefaultAttachment> source)
            {
                List<ModularDefaultAttachment> result = new List<ModularDefaultAttachment>();
                if (source == null) return result;
                for (int i = 0; i < source.Count; i++)
                {
                    ModularDefaultAttachment item = source[i];
                    if (item == null) continue;
                    result.Add(new ModularDefaultAttachment
                    {
                        socketId = item.socketId,
                        mountId = item.mountId,
                        part = item.part,
                        railOffset = item.railOffset
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

        public override Vector2 InitialSize => new Vector2(520f, 370f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "Create attachment socket");
            Text.Font = GameFont.Small;

            float y = 48f;
            DrawTextRow(inRect, ref y, "Socket ID", ref socketId);
            DrawTextRow(inRect, ref y, "Display label", ref socketLabel);
            DrawTextRow(inRect, ref y, "Socket types", ref socketTypes);
            TooltipHandler.TipRegion(new Rect(130f, y - 34f, inRect.width - 130f, 28f),
                "Comma-separated types. A mount is compatible when at least one type matches.");

            Widgets.CheckboxLabeled(new Rect(0f, y, inRect.width, 28f),
                "Continuous horizontal rail", ref isRail);
            y += 34f;
            if (isRail) DrawTextRow(inRect, ref y, "Rail length", ref railLength);

            Widgets.Label(new Rect(0f, y + 8f, inRect.width, 48f),
                "Examples: picatinny, top_rail, receiver_rail\n"
                + "The new socket starts at (0, 0) and can be moved immediately in the editor.");

            Rect buttons = new Rect(0f, inRect.height - 38f, inRect.width, 34f);
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, 120f, 32f), "Cancel"))
                Close();
            if (Widgets.ButtonText(new Rect(buttons.xMax - 140f, buttons.y, 140f, 32f),
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
                railLength = parsedRailLength
            });
            Close();
        }

        private static void DrawTextRow(Rect inRect, ref float y, string label, ref string value)
        {
            Widgets.Label(new Rect(0f, y + 3f, 122f, 26f), label);
            value = Widgets.TextField(new Rect(130f, y, inRect.width - 130f, 28f), value ?? string.Empty);
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
