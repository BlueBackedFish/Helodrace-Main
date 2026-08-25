using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class Dialog_ModularWeapon : Window
    {
        private readonly CompModularWeaponNode root;
        private string selectedPath = "root";
        private string selectedSocketId;
        private int selectedAttachmentId = -1;
        private Vector2 navigationScroll;
        private Vector2 socketScroll;
        private Vector2 catalogScroll;
        private ThingDef hoveredPart;

        public Dialog_ModularWeapon(CompModularWeaponNode root)
        {
            this.root = root;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(1380f, 860f);

        public override void DoWindowContents(Rect inRect)
        {
            hoveredPart = null;
            if (root?.parent == null || root.parent.Destroyed)
            {
                Widgets.Label(inRect, "HD_ModularWeapon_Missing".Translate());
                return;
            }

            List<ModularRenderNode> snapshot = root.RenderSnapshot();
            ModularRenderNode selected = FindSelected(snapshot);
            EnsureSelectedSocket(selected);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 160f, 32f),
                "HD_ModularWeapon_Title".Translate(root.parent.LabelCap));
            Text.Font = GameFont.Small;

            float bodyTop = 40f;
            float catalogHeight = 184f;
            float footerHeight = 38f;
            float bodyHeight = inRect.height - bodyTop - catalogHeight - footerHeight - 8f;
            Rect treeRect = new Rect(0f, bodyTop, 278f, bodyHeight);
            Rect previewRect = new Rect(290f, bodyTop, inRect.width - 690f, bodyHeight);
            Rect detailsRect = new Rect(inRect.width - 388f, bodyTop, 388f, bodyHeight);
            Rect catalogRect = new Rect(0f, bodyTop + bodyHeight + 8f,
                inRect.width, catalogHeight);

            DrawPartNavigator(treeRect, snapshot, selected);
            DrawAssemblyPreview(previewRect, snapshot, selected);
            DrawSocketPanel(detailsRect, selected);
            DrawCatalog(catalogRect, selected);

            Rect footer = new Rect(0f, inRect.height - footerHeight,
                inRect.width, footerHeight);
            Widgets.Label(new Rect(4f, footer.y + 9f, footer.width - 150f, 24f),
                "HD_ModularWeapon_ImmediateHint".Translate());
            if (Widgets.ButtonText(new Rect(footer.xMax - 120f, footer.y + 4f,
                120f, 30f), "CloseButton".Translate()))
                Close();

            if (hoveredPart != null) DrawHoveredPartPreview(inRect, hoveredPart);
        }

        private void DrawPartNavigator(
            Rect rect,
            List<ModularRenderNode> snapshot,
            ModularRenderNode selected)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 7f, rect.width - 18f, 24f),
                "HD_ModularWeapon_Navigation".Translate());
            if (selected == null) return;

            ModularRenderNode parentNode = FindNodeForComp(snapshot, selected.parentComp);
            ModularRenderNode rootNode = snapshot.FirstOrDefault(node => node.depth == 0);
            Rect backRect = new Rect(rect.x + 8f, rect.y + 34f, rect.width - 88f, 29f);
            if (parentNode != null)
            {
                if (Widgets.ButtonText(backRect,
                    "HD_ModularWeapon_BackToParent".Translate()))
                    SelectNode(parentNode);
            }
            else
            {
                Text.Font = GameFont.Tiny;
                Widgets.Label(backRect.ContractedBy(5f, 4f),
                    "HD_ModularWeapon_AssemblyRoot".Translate());
                Text.Font = GameFont.Small;
            }

            Rect homeRect = new Rect(rect.xMax - 72f, rect.y + 34f, 64f, 29f);
            if (rootNode != null && selected != rootNode &&
                Widgets.ButtonText(homeRect, "HD_ModularWeapon_Home".Translate()))
                SelectNode(rootNode);

            string breadcrumb = BuildBreadcrumbText(snapshot, selected);
            Rect breadcrumbRect = new Rect(rect.x + 9f, rect.y + 68f,
                rect.width - 18f, 28f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.76f, 0.79f);
            Widgets.Label(breadcrumbRect, breadcrumb);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(breadcrumbRect, breadcrumb);
            Text.Font = GameFont.Small;

            Rect currentCard = new Rect(rect.x + 8f, rect.y + 98f,
                rect.width - 16f, 88f);
            Widgets.DrawBoxSolidWithOutline(currentCard,
                new Color(0.13f, 0.17f, 0.18f),
                new Color(0.42f, 0.72f, 0.76f), 2);
            Texture currentIcon = selected.thing.def.uiIcon;
            if (currentIcon != null)
                Widgets.DrawTextureFitted(new Rect(currentCard.x + 8f,
                    currentCard.y + 13f, 58f, 58f), currentIcon, 1f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.85f, 0.88f);
            Widgets.Label(new Rect(currentCard.x + 74f, currentCard.y + 8f,
                currentCard.width - 82f, 18f),
                "HD_ModularWeapon_CurrentPart".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(currentCard.x + 74f, currentCard.y + 27f,
                currentCard.width - 82f, 30f), selected.thing.LabelCap);
            int directChildCount = DirectChildren(snapshot, selected).Count;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(currentCard.x + 74f, currentCard.y + 60f,
                currentCard.width - 82f, 20f),
                "HD_ModularWeapon_PartSummary".Translate(
                    selected.Props.sockets?.Count ?? 0, directChildCount));
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(rect.x + 9f, rect.y + 194f,
                rect.width - 18f, 24f),
                "HD_ModularWeapon_AttachedParts".Translate());

            List<ModularRenderNode> children = DirectChildren(snapshot, selected);
            Rect outRect = new Rect(rect.x + 4f, rect.y + 220f,
                rect.width - 8f, rect.height - 224f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f,
                Mathf.Max(outRect.height, children.Count * 74f + 8f));
            Widgets.BeginScrollView(outRect, ref navigationScroll, viewRect);
            if (children.Count == 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.66f, 0.69f, 0.70f);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(10f, 12f, viewRect.width - 20f, 44f),
                    "HD_ModularWeapon_NoAttachedParts".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            for (int i = 0; i < children.Count; i++)
            {
                ModularRenderNode child = children[i];
                Rect card = new Rect(4f, 4f + i * 74f, viewRect.width - 8f, 66f);
                bool hovered = Mouse.IsOver(card);
                Widgets.DrawBoxSolidWithOutline(card,
                    hovered ? new Color(0.17f, 0.20f, 0.18f)
                        : new Color(0.095f, 0.11f, 0.105f),
                    hovered ? Color.yellow : new Color(0.32f, 0.38f, 0.35f),
                    hovered ? 2 : 1);
                Texture icon = child.thing.def.uiIcon;
                if (icon != null)
                    Widgets.DrawTextureFitted(new Rect(card.x + 7f,
                        card.y + 10f, 46f, 46f), icon, 1f);
                Widgets.Label(new Rect(card.x + 60f, card.y + 7f,
                    card.width - 82f, 25f), child.thing.LabelCap);
                ModularAttachmentSocket socket = selected.Props
                    .SocketNamed(child.parentSocketId);
                Text.Font = GameFont.Tiny;
                string connection = "HD_ModularWeapon_ConnectedVia".Translate(
                    socket?.Label ?? child.parentSocketId);
                if (socket?.isRail == true)
                    connection += "  ·  " + "HD_ModularWeapon_RailOffset".Translate(
                        child.railOffset.ToString("0.###", CultureInfo.InvariantCulture));
                GUI.color = new Color(0.70f, 0.74f, 0.71f);
                Widgets.Label(new Rect(card.x + 60f, card.y + 34f,
                    card.width - 76f, 24f), connection);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(card.xMax - 24f, card.y, 20f, card.height), "›");
                Text.Anchor = TextAnchor.UpperLeft;
                if (hovered) hoveredPart = child.thing.def;
                if (Widgets.ButtonInvisible(card)) SelectNode(child);
            }
            Widgets.EndScrollView();
        }

        private void SelectNode(ModularRenderNode node)
        {
            if (node == null) return;
            selectedPath = node.path;
            selectedSocketId = null;
            selectedAttachmentId = -1;
            socketScroll = Vector2.zero;
            catalogScroll = Vector2.zero;
        }

        private static ModularRenderNode FindNodeForComp(
            List<ModularRenderNode> snapshot,
            CompModularWeaponNode comp)
        {
            if (comp == null) return null;
            return snapshot.FirstOrDefault(node => node.comp == comp);
        }

        private static List<ModularRenderNode> DirectChildren(
            List<ModularRenderNode> snapshot,
            ModularRenderNode parent)
        {
            if (parent?.comp == null) return new List<ModularRenderNode>();
            return snapshot.Where(node => node.parentComp == parent.comp)
                .OrderBy(node => node.parentChildIndex)
                .ToList();
        }

        private static string BuildBreadcrumbText(
            List<ModularRenderNode> snapshot,
            ModularRenderNode selected)
        {
            List<string> labels = new List<string>();
            ModularRenderNode current = selected;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                labels.Insert(0, current.thing.LabelCap.ToString());
                current = FindNodeForComp(snapshot, current.parentComp);
            }
            return string.Join("  ›  ", labels.ToArray());
        }

        private void DrawAssemblyPreview(
            Rect rect,
            List<ModularRenderNode> snapshot,
            ModularRenderNode selected)
        {
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.055f, 0.06f, 0.07f, 1f),
                new Color(0.35f, 0.38f, 0.42f), 2);
            Rect inner = rect.ContractedBy(18f);
            Vector2 origin = inner.center;
            float pixelsPerCell = Mathf.Min(inner.width, inner.height) * 0.82f;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    ModularRenderNode node = snapshot[i];
                    Graphic graphic = node.thing.Graphic;
                    string texturePath = node.thing.def.graphicData?.texPath;
                    Texture texture = pass == 0
                        ? texturePath.NullOrEmpty()
                            ? null
                            : ContentFinder<Texture2D>.Get(texturePath + "_Outline", false)
                        : graphic?.MatSingle?.mainTexture;
                    if (graphic == null || texture == null) continue;

                    Vector2 center = WorldToGui(node.GraphicCenter, origin, pixelsPerCell);
                    Vector2 size = Vector2.Scale(graphic.drawSize, node.GraphicScale)
                        * pixelsPerCell;
                    Rect drawRect = CenteredRect(center, size);
                    Color previous = GUI.color;
                    if (pass == 1 && selected != null && node.path != selected.path)
                        GUI.color = new Color(1f, 1f, 1f, 0.78f);
                    Matrix4x4 matrix = GUI.matrix;
                    if (!Mathf.Approximately(node.GraphicAngle, 0f))
                        UI.RotateAroundPivot(-node.GraphicAngle, drawRect.center);
                    GUI.DrawTexture(drawRect, texture, ScaleMode.StretchToFill);
                    GUI.matrix = matrix;
                    GUI.color = previous;
                }
            }

            DrawSelectedRailOverlay(selected, origin, pixelsPerCell);
            DrawPerformanceBlock(new Rect(rect.x + 10f, rect.y + 10f, 280f, 104f));
        }

        private void DrawSelectedRailOverlay(
            ModularRenderNode selected,
            Vector2 origin,
            float pixelsPerCell)
        {
            if (selected?.attachedToRail != true) return;
            Vector2 railStart = WorldToGui(selected.railStart, origin, pixelsPerCell);
            Vector2 railEnd = WorldToGui(selected.railEnd, origin, pixelsPerCell);
            Vector2 occupiedStart = WorldToGui(
                selected.occupiedRailStart, origin, pixelsPerCell);
            Vector2 occupiedEnd = WorldToGui(
                selected.occupiedRailEnd, origin, pixelsPerCell);
            Widgets.DrawLine(railStart, railEnd, new Color(0.15f, 0.65f, 1f), 4f);
            Widgets.DrawLine(occupiedStart, occupiedEnd, Color.yellow, 7f);
        }

        private void DrawPerformanceBlock(Rect rect)
        {
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.07f, 0.08f, 0.085f, 0.93f),
                new Color(0.32f, 0.38f, 0.40f), 1);
            string text = "HD_ModularWeapon_Performance".Translate(
                root.RealisticRoundsPerMinute.ToString("0.#", CultureInfo.InvariantCulture),
                root.EffectiveRoundsPerMinute.ToString("0.#", CultureInfo.InvariantCulture),
                root.MinimumFireDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture),
                root.EffectiveFireDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture),
                root.EffectiveBurstIntervalTicks);
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect.ContractedBy(8f), text);
            Text.Font = GameFont.Small;
        }

        private void DrawSocketPanel(Rect rect, ModularRenderNode node)
        {
            Widgets.DrawMenuSection(rect);
            if (node == null) return;
            Rect inner = rect.ContractedBy(10f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 26f), node.thing.LabelCap);
            Rect outRect = new Rect(inner.x, inner.y + 30f, inner.width,
                inner.height - 30f);
            float contentHeight = 16f;
            for (int i = 0; i < node.Props.sockets.Count; i++)
                contentHeight += 78f + node.comp.CountOnSocket(node.Props.sockets[i].id) * 50f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f,
                Mathf.Max(outRect.height, contentHeight));
            Widgets.BeginScrollView(outRect, ref socketScroll, viewRect);

            float y = 2f;
            for (int socketIndex = 0; socketIndex < node.Props.sockets.Count; socketIndex++)
            {
                ModularAttachmentSocket socket = node.Props.sockets[socketIndex];
                int count = node.comp.CountOnSocket(socket.id);
                Rect socketRect = new Rect(2f, y, viewRect.width - 4f, 64f);
                bool active = socket.id == selectedSocketId;
                Widgets.DrawBoxSolidWithOutline(socketRect,
                    active ? new Color(0.16f, 0.22f, 0.25f)
                        : new Color(0.09f, 0.11f, 0.12f),
                    active ? Color.yellow : new Color(0.34f, 0.40f, 0.43f),
                    active ? 2 : 1);
                if (Widgets.ButtonInvisible(socketRect))
                {
                    selectedSocketId = socket.id;
                    selectedAttachmentId = -1;
                    catalogScroll = Vector2.zero;
                }

                string capacity = socket.isRail
                    ? socket.maxAttachments > 0
                        ? count + "/" + socket.maxAttachments
                        : count + "/∞"
                    : count + "/1";
                Widgets.Label(new Rect(socketRect.x + 8f, socketRect.y + 7f,
                    socketRect.width - 16f, 24f), socket.Label + "  " + capacity);
                Text.Font = GameFont.Tiny;
                string detail = socket.isRail
                    ? "HD_ModularWeapon_RailDetail".Translate(
                        socket.railLength.ToString("0.###", CultureInfo.InvariantCulture),
                        TagText(socket.tags)).ToString()
                    : TagText(socket.tags);
                Widgets.Label(new Rect(socketRect.x + 8f, socketRect.y + 33f,
                    socketRect.width - 16f, 22f), detail);
                Text.Font = GameFont.Small;
                y += 69f;

                for (int childIndex = 0; childIndex < node.comp.ChildCount; childIndex++)
                {
                    if (node.comp.SocketIdAt(childIndex) != socket.id) continue;
                    Thing child = node.comp.ChildAt(childIndex);
                    Rect childRect = new Rect(18f, y, viewRect.width - 22f, 43f);
                    bool childSelected = child?.thingIDNumber == selectedAttachmentId;
                    Widgets.DrawBoxSolidWithOutline(childRect,
                        childSelected ? new Color(0.22f, 0.25f, 0.19f)
                            : new Color(0.11f, 0.12f, 0.105f),
                        childSelected ? Color.yellow : new Color(0.38f, 0.42f, 0.35f),
                        childSelected ? 2 : 1);
                    if (child?.def.uiIcon != null)
                        Widgets.DrawTextureFitted(new Rect(childRect.x + 5f,
                            childRect.y + 5f, 32f, 32f), child.def.uiIcon, 1f);
                    Widgets.Label(new Rect(childRect.x + 43f, childRect.y + 4f,
                        childRect.width - 86f, 24f), child?.LabelCap ?? "-");
                    if (socket.isRail)
                    {
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(childRect.x + 43f, childRect.y + 24f,
                            childRect.width - 86f, 17f),
                            "HD_ModularWeapon_RailOffset".Translate(
                                node.comp.RailOffsetAt(childIndex).ToString(
                                    "0.###", CultureInfo.InvariantCulture)));
                        Text.Font = GameFont.Small;
                    }
                    Rect removeRect = new Rect(childRect.xMax - 36f,
                        childRect.y + 6f, 30f, 30f);
                    if (!socket.required && Widgets.ButtonText(removeRect, "×"))
                    {
                        Thing removed = node.comp.DetachChildAt(childIndex);
                        DestroyAssembly(removed);
                        selectedAttachmentId = -1;
                        root.InvalidateTree();
                        Widgets.EndScrollView();
                        return;
                    }
                    if (Widgets.ButtonInvisible(new Rect(childRect.x, childRect.y,
                        childRect.width - 42f, childRect.height)))
                        selectedAttachmentId = childSelected ? -1 : child.thingIDNumber;
                    y += 47f;

                    if (childSelected && socket.isRail)
                    {
                        ModularAttachmentMount mount = child
                            ?.TryGetComp<CompModularWeaponNode>()
                            ?.Props.MountNamed(node.comp.MountIdAt(childIndex));
                        float half = Mathf.Max(0f,
                            (socket.railLength - (mount?.railOccupancy ?? 0f)) * 0.5f);
                        float oldValue = node.comp.RailOffsetAt(childIndex);
                        float value = half <= 0f ? 0f : Widgets.HorizontalSlider(
                            new Rect(28f, y + 3f, viewRect.width - 118f, 18f),
                            oldValue, -half, half, true);
                        Widgets.Label(new Rect(viewRect.width - 84f, y,
                            80f, 22f), value.ToString("0.###", CultureInfo.InvariantCulture));
                        if (!Mathf.Approximately(value, oldValue))
                        {
                            string rejection;
                            if (!node.comp.TrySetRailOffset(childIndex, value, out rejection))
                                Messages.Message(rejection, MessageTypeDefOf.RejectInput, false);
                        }
                        y += 27f;
                    }
                }
                y += 9f;
            }
            Widgets.EndScrollView();
        }

        private void DrawCatalog(Rect rect, ModularRenderNode node)
        {
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.075f, 0.085f, 0.08f),
                new Color(0.32f, 0.37f, 0.34f), 1);
            ModularAttachmentSocket socket = node?.Props.SocketNamed(selectedSocketId);
            string title = socket == null
                ? "HD_ModularWeapon_SelectSocket".Translate()
                : "HD_ModularWeapon_Catalog".Translate(socket.Label);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f,
                rect.width - 20f, 25f), title);
            if (socket == null || node == null) return;

            List<ThingDef> candidates = CompatibleParts(socket);
            Rect outRect = new Rect(rect.x + 6f, rect.y + 34f,
                rect.width - 12f, rect.height - 40f);
            float tileWidth = 164f;
            Rect viewRect = new Rect(0f, 0f,
                Mathf.Max(outRect.width, candidates.Count * (tileWidth + 8f) + 8f),
                outRect.height - 18f);
            Widgets.BeginScrollView(outRect, ref catalogScroll, viewRect);
            for (int i = 0; i < candidates.Count; i++)
            {
                ThingDef def = candidates[i];
                Rect tile = new Rect(6f + i * (tileWidth + 8f), 3f,
                    tileWidth, viewRect.height - 8f);
                bool hovered = Mouse.IsOver(tile);
                Widgets.DrawBoxSolidWithOutline(tile,
                    hovered ? new Color(0.18f, 0.22f, 0.18f)
                        : new Color(0.105f, 0.12f, 0.11f),
                    hovered ? Color.yellow : new Color(0.36f, 0.42f, 0.37f),
                    hovered ? 2 : 1);
                if (def.uiIcon != null)
                    Widgets.DrawTextureFitted(new Rect(tile.center.x - 28f,
                        tile.y + 7f, 56f, 56f), def.uiIcon, 1f);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(tile.x + 5f, tile.y + 67f,
                    tile.width - 10f, 42f), def.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;
                CompProperties_ModularWeaponNode props =
                    def.GetCompProperties<CompProperties_ModularWeaponNode>();
                ModularAttachmentMount mount = CompatibleMount(props, socket);
                Text.Font = GameFont.Tiny;
                string occupancyLabel = mount?.railOccupancy > 0f
                    ? "HD_ModularWeapon_Occupancy".Translate(
                        mount.railOccupancy.ToString("0.###",
                            CultureInfo.InvariantCulture)).ToString()
                    : string.Empty;
                Widgets.Label(new Rect(tile.x + 6f, tile.yMax - 25f,
                    tile.width - 12f, 20f), occupancyLabel);
                Text.Font = GameFont.Small;
                if (hovered) hoveredPart = def;
                if (Widgets.ButtonInvisible(tile)) InstallPart(node, socket, def, mount);
            }
            Widgets.EndScrollView();
        }

        private void InstallPart(
            ModularRenderNode node,
            ModularAttachmentSocket socket,
            ThingDef def,
            ModularAttachmentMount mount)
        {
            if (node?.comp == null || socket == null || def == null || mount == null) return;
            List<DetachedPart> displaced = new List<DetachedPart>();
            int limit = socket.isRail ? socket.maxAttachments : 1;
            while (limit > 0 && node.comp.CountOnSocket(socket.id) >= limit)
            {
                int index = FirstChildIndexOnSocket(node.comp, socket.id);
                if (index < 0) break;
                displaced.Add(new DetachedPart
                {
                    thing = node.comp.ChildAt(index),
                    socketId = node.comp.SocketIdAt(index),
                    mountId = node.comp.MountIdAt(index),
                    railOffset = node.comp.RailOffsetAt(index)
                });
                node.comp.DetachChildAt(index);
            }

            Thing child = ThingMaker.MakeThing(def);
            string rejection;
            float offset;
            bool success = node.comp.TryFindAttachOffset(
                    child, socket.id, mount.id, out offset, out rejection)
                && node.comp.TryAttach(child, socket.id, mount.id, offset, out rejection);
            if (!success)
            {
                DestroyAssembly(child);
                RestoreDisplaced(node.comp, displaced);
                Messages.Message(rejection ?? "HD_ModularWeapon_AttachFailed".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            for (int i = 0; i < displaced.Count; i++)
                DestroyAssembly(displaced[i].thing);
            selectedAttachmentId = child.thingIDNumber;
            root.InvalidateTree();
        }

        private static void RestoreDisplaced(
            CompModularWeaponNode parent,
            List<DetachedPart> displaced)
        {
            for (int i = 0; i < displaced.Count; i++)
            {
                DetachedPart record = displaced[i];
                string ignored;
                if (!parent.TryAttach(record.thing, record.socketId,
                    record.mountId, record.railOffset, out ignored))
                    DestroyAssembly(record.thing);
            }
        }

        private static void DestroyAssembly(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            CompModularWeaponNode comp = thing.TryGetComp<CompModularWeaponNode>();
            while (comp != null && comp.ChildCount > 0)
                DestroyAssembly(comp.DetachChildAt(comp.ChildCount - 1));
            thing.Destroy(DestroyMode.Vanish);
        }

        private void DrawHoveredPartPreview(Rect windowRect, ThingDef def)
        {
            PartPreviewData data = PartPreviewData.For(def);
            Vector2 mouse = Event.current.mousePosition;
            float width = 250f;
            float height = 250f;
            float x = Mathf.Min(windowRect.xMax - width - 4f, mouse.x + 20f);
            float y = Mathf.Clamp(mouse.y - height * 0.5f,
                windowRect.y + 4f, windowRect.yMax - height - 4f);
            Rect panel = new Rect(x, y, width, height);
            Widgets.DrawBoxSolidWithOutline(panel,
                new Color(0.035f, 0.04f, 0.045f, 0.98f), Color.yellow, 2);
            Widgets.Label(new Rect(panel.x + 10f, panel.y + 8f,
                panel.width - 20f, 26f), def.LabelCap);

            Rect imageArea = new Rect(panel.x + 12f, panel.y + 38f,
                panel.width - 24f, 156f);
            if (data.texture != null)
            {
                float aspect = Mathf.Max(0.05f, data.aspect);
                Vector2 size = aspect >= 1f
                    ? new Vector2(imageArea.width, imageArea.width / aspect)
                    : new Vector2(imageArea.height * aspect, imageArea.height);
                size *= 0.94f;
                Rect drawRect = CenteredRect(imageArea.center, size);
                GUI.DrawTextureWithTexCoords(drawRect, data.texture, data.uv, true);
            }

            CompProperties_ModularWeaponNode props =
                def.GetCompProperties<CompProperties_ModularWeaponNode>();
            ModularAttachmentSocket selectedSocket = FindSelected(root.RenderSnapshot())
                ?.Props.SocketNamed(selectedSocketId);
            ModularAttachmentMount mount = CompatibleMount(props, selectedSocket);
            string details = def.defName;
            if (mount?.railOccupancy > 0f)
                details += "\n" + "HD_ModularWeapon_Occupancy".Translate(
                    mount.railOccupancy.ToString("0.###", CultureInfo.InvariantCulture));
            if (props != null && !Mathf.Approximately(props.fireDelayMultiplier, 1f))
                details += "\n" + "HD_ModularWeapon_FireDelayModifier".Translate(
                    props.fireDelayMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(panel.x + 10f, panel.y + 199f,
                panel.width - 20f, 45f), details);
            Text.Font = GameFont.Small;
        }

        private List<ThingDef> CompatibleParts(ModularAttachmentSocket socket)
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => CompatibleMount(
                    def.GetCompProperties<CompProperties_ModularWeaponNode>(), socket) != null)
                .OrderBy(def => def.label)
                .ToList();
        }

        private static ModularAttachmentMount CompatibleMount(
            CompProperties_ModularWeaponNode props,
            ModularAttachmentSocket socket)
        {
            if (props?.mounts == null || socket == null) return null;
            for (int i = 0; i < props.mounts.Count; i++)
                if (props.mounts[i]?.Accepts(socket) == true) return props.mounts[i];
            return null;
        }

        private ModularRenderNode FindSelected(List<ModularRenderNode> snapshot)
        {
            for (int i = 0; i < snapshot.Count; i++)
                if (snapshot[i].path == selectedPath) return snapshot[i];
            ModularRenderNode fallback = snapshot.Count > 0 ? snapshot[0] : null;
            selectedPath = fallback?.path;
            return fallback;
        }

        private void EnsureSelectedSocket(ModularRenderNode node)
        {
            if (node?.Props.SocketNamed(selectedSocketId) != null) return;
            selectedSocketId = node?.Props.sockets.FirstOrDefault()?.id;
            selectedAttachmentId = -1;
        }

        private static int FirstChildIndexOnSocket(
            CompModularWeaponNode comp,
            string socketId)
        {
            for (int i = 0; i < comp.ChildCount; i++)
                if (comp.SocketIdAt(i) == socketId) return i;
            return -1;
        }

        private static string TagText(List<string> tags)
        {
            return tags.NullOrEmpty() ? "-" : string.Join(", ", tags.ToArray());
        }

        private static Vector2 WorldToGui(Vector2 point, Vector2 origin, float scale)
        {
            return new Vector2(origin.x + point.x * scale, origin.y - point.y * scale);
        }

        private static Rect CenteredRect(Vector2 center, Vector2 size)
        {
            return new Rect(center.x - size.x * 0.5f,
                center.y - size.y * 0.5f, size.x, size.y);
        }

        private sealed class DetachedPart
        {
            public Thing thing;
            public string socketId;
            public string mountId;
            public float railOffset;
        }

        private sealed class PartPreviewData
        {
            private static readonly Dictionary<ThingDef, PartPreviewData> Cache =
                new Dictionary<ThingDef, PartPreviewData>();

            public Texture2D texture;
            public Rect uv = new Rect(0f, 0f, 1f, 1f);
            public float aspect = 1f;

            public static PartPreviewData For(ThingDef def)
            {
                PartPreviewData result;
                if (Cache.TryGetValue(def, out result)) return result;
                result = Build(def);
                Cache[def] = result;
                return result;
            }

            private static PartPreviewData Build(ThingDef def)
            {
                PartPreviewData result = new PartPreviewData();
                string path = def?.graphicData?.texPath;
                result.texture = path.NullOrEmpty()
                    ? null
                    : ContentFinder<Texture2D>.Get(path, false);
                Texture2D texture = result.texture;
                if (texture == null) return result;

                result.aspect = texture.width / (float)Mathf.Max(1, texture.height);
                try
                {
                    Color32[] pixels = ReadPixelsSafely(texture);
                    if (pixels == null || pixels.Length == 0) return result;
                    int minX = texture.width;
                    int minY = texture.height;
                    int maxX = -1;
                    int maxY = -1;
                    for (int y = 0; y < texture.height; y++)
                    {
                        int row = y * texture.width;
                        for (int x = 0; x < texture.width; x++)
                        {
                            if (pixels[row + x].a <= 8) continue;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }

                    if (maxX >= minX && maxY >= minY)
                    {
                        const int padding = 6;
                        minX = Mathf.Max(0, minX - padding);
                        minY = Mathf.Max(0, minY - padding);
                        maxX = Mathf.Min(texture.width - 1, maxX + padding);
                        maxY = Mathf.Min(texture.height - 1, maxY + padding);
                        float width = maxX - minX + 1f;
                        float height = maxY - minY + 1f;
                        result.uv = new Rect(minX / (float)texture.width,
                            minY / (float)texture.height,
                            width / texture.width,
                            height / texture.height);
                        result.aspect = width / Mathf.Max(1f, height);
                    }
                }
                catch (Exception)
                {
                    // A full-texture preview is still preferable to breaking Window.OnGUI.
                }
                return result;
            }

            private static Color32[] ReadPixelsSafely(Texture2D texture)
            {
                if (texture.isReadable) return texture.GetPixels32();

                RenderTexture previous = RenderTexture.active;
                RenderTexture temporary = null;
                Texture2D readableCopy = null;
                try
                {
                    temporary = RenderTexture.GetTemporary(texture.width, texture.height,
                        0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    Graphics.Blit(texture, temporary);
                    RenderTexture.active = temporary;
                    readableCopy = new Texture2D(texture.width, texture.height,
                        TextureFormat.RGBA32, false);
                    readableCopy.ReadPixels(new Rect(0f, 0f,
                        texture.width, texture.height), 0, 0, false);
                    readableCopy.Apply(false, false);
                    return readableCopy.GetPixels32();
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                    if (readableCopy != null) UnityEngine.Object.Destroy(readableCopy);
                }
            }
        }
    }
}
