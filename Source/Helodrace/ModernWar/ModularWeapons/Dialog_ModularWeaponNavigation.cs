using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularWeapon
    {
        private readonly HashSet<int> collapsedParts = new HashSet<int>();
        private List<ModularRenderNode> navigationSnapshot;
        private ModularWeaponNavigationTree navigationTree;
        private string navigationSearch = string.Empty;
        private bool revealNavigationSelection = true;
        private ModularRenderNode hoveredNode;

        private void EnsureNavigation(List<ModularRenderNode> snapshot)
        {
            if (ReferenceEquals(navigationSnapshot, snapshot)) return;
            navigationSnapshot = snapshot;
            navigationTree = new ModularWeaponNavigationTree(snapshot,
                ShouldHideCEAmmunitionPart, ShouldHideCEAmmunitionSocket);
            var ids = new HashSet<int>(snapshot.Select(node => node.thing.thingIDNumber));
            collapsedParts.RemoveWhere(id => !ids.Contains(id));
            navigationTree.Reveal(FindSelected(snapshot), collapsedParts);
            revealNavigationSelection = true;
        }

        private void DrawPartNavigator(Rect rect, ModularRenderNode selected)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 7f, rect.width - 18f, 24f),
                "HD_ModularWeapon_Tree".Translate());
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width, 30f),
                "HD_ModularWeapon_TreeHint".Translate());

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 9f, rect.y + 38f, 42f, 22f),
                "HD_ModularWeapon_TreeSearch".Translate());
            Text.Font = GameFont.Small;
            string edited = Widgets.TextField(new Rect(rect.x + 53f, rect.y + 34f,
                rect.width - 91f, 28f), navigationSearch);
            if (Widgets.ButtonText(new Rect(rect.xMax - 33f, rect.y + 34f, 25f, 28f), "×"))
                edited = string.Empty;
            if (edited != navigationSearch)
            {
                navigationSearch = edited;
                navigationScroll = Vector2.zero;
                revealNavigationSelection = false;
            }

            float buttonWidth = (rect.width - 24f) * 0.5f;
            if (Widgets.ButtonText(new Rect(rect.x + 8f, rect.y + 68f, buttonWidth, 26f),
                "HD_ModularWeapon_ExpandAll".Translate()))
            {
                collapsedParts.Clear();
                navigationSearch = string.Empty;
                revealNavigationSelection = true;
            }
            if (Widgets.ButtonText(new Rect(rect.xMax - 8f - buttonWidth, rect.y + 68f,
                buttonWidth, 26f), "HD_ModularWeapon_FocusSelection".Translate()))
            {
                navigationSearch = string.Empty;
                navigationTree.Focus(selected, collapsedParts);
                revealNavigationSelection = true;
            }

            string breadcrumb = navigationTree.Breadcrumb(selected);
            Rect breadcrumbRect = new Rect(rect.x + 9f, rect.y + 100f, rect.width - 18f, 22f);
            Text.Font = GameFont.Tiny;
            bool previousWrap = Text.WordWrap;
            Text.WordWrap = false;
            GUI.color = new Color(0.65f, 0.85f, 0.88f);
            // Keep the selected end of a deep path visible; the tooltip has the full path.
            string[] labels = breadcrumb.Split(new[] { "  ›  " }, System.StringSplitOptions.None);
            Widgets.Label(breadcrumbRect, labels.Length > 2
                ? "…  ›  " + string.Join("  ›  ", labels.Skip(labels.Length - 2).ToArray())
                : breadcrumb);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(breadcrumbRect, breadcrumb);

            List<ModularWeaponNavigationTree.Entry> rows =
                navigationTree.VisibleRows(collapsedParts, navigationSearch);
            Rect outRect = new Rect(rect.x + 4f, rect.y + 127f,
                rect.width - 8f, Mathf.Max(1f, rect.height - 131f));
            const float rowHeight = 44f;
            float width = Mathf.Max(outRect.width - 18f,
                260f + (rows.Count == 0 ? 0 : rows.Max(row => row.Depth)) * 14f);
            float visibleHeight = Mathf.Max(1f,
                outRect.height - (width > outRect.width - 18f ? 18f : 0f));
            if (revealNavigationSelection)
            {
                int index = rows.FindIndex(row => !row.IsEmpty && row.Node.path == selectedPath);
                if (index >= 0)
                {
                    float top = index * rowHeight + 4f;
                    if (top < navigationScroll.y) navigationScroll.y = top;
                    else if (top + rowHeight > navigationScroll.y + visibleHeight)
                        navigationScroll.y = Mathf.Max(0f, top + rowHeight - visibleHeight);
                    float left = rows[index].Depth * 14f;
                    if (left < navigationScroll.x) navigationScroll.x = left;
                    else if (left + 260f > navigationScroll.x + outRect.width - 18f)
                        navigationScroll.x = Mathf.Max(0f, left + 260f - outRect.width + 18f);
                }
                revealNavigationSelection = false;
            }
            Rect viewRect = new Rect(0f, 0f, width,
                Mathf.Max(visibleHeight, rows.Count * rowHeight + 8f));
            bool pointerInTree = Mouse.IsOver(outRect);
            Widgets.BeginScrollView(outRect, ref navigationScroll, viewRect);
            if (rows.Count == 0)
                Widgets.Label(new Rect(9f, 12f, width - 18f, 26f),
                    "HD_ModularWeapon_TreeNoResults".Translate());

            for (int i = 0; i < rows.Count; i++)
            {
                ModularWeaponNavigationTree.Entry row = rows[i];
                Rect card = new Rect(3f, 4f + i * rowHeight, width - 6f, rowHeight - 2f);
                bool hovered = pointerInTree && Mouse.IsOver(card);
                bool active = row.IsEmpty
                    ? row.Owner.path == selectedPath && row.Socket.id == selectedSocketId
                    : row.Node.path == selectedPath;
                bool linkedHover = !row.IsEmpty && hoveredNode?.path == row.Node.path;
                Widgets.DrawBoxSolidWithOutline(card,
                    active ? new Color(0.13f, 0.22f, 0.24f)
                        : hovered || linkedHover ? new Color(0.19f, 0.21f, 0.16f)
                        : new Color(0.085f, 0.10f, 0.11f),
                    hovered || linkedHover ? Color.yellow
                        : active ? new Color(0.42f, 0.78f, 0.82f)
                        : new Color(0.19f, 0.23f, 0.25f), 1);
                float indent = row.Depth * 14f;
                for (int depth = 0; depth < row.Depth; depth++)
                {
                    float x = card.x + 9f + depth * 14f;
                    Widgets.DrawLine(new Vector2(x, card.y), new Vector2(x, card.yMax),
                        new Color(0.24f, 0.33f, 0.35f), 1f);
                }
                Rect toggle = new Rect(card.x + indent, card.y + 10f, 20f, 22f);
                if (!row.IsEmpty && row.Children.Count > 0)
                {
                    Text.Font = GameFont.Small;
                    bool collapsed = navigationSearch.Trim().Length == 0
                        && collapsedParts.Contains(row.Node.thing.thingIDNumber);
                    if (Widgets.ButtonText(toggle, collapsed ? "+" : "−"))
                    {
                        if (collapsed) collapsedParts.Remove(row.Node.thing.thingIDNumber);
                        else collapsedParts.Add(row.Node.thing.thingIDNumber);
                        // Search results must retain their ancestor paths.
                        navigationSearch = string.Empty;
                    }
                }
                Rect imageRect = new Rect(toggle.xMax + 3f, card.y + 4f, 34f, 34f);
                if (!row.IsEmpty && card.yMax >= navigationScroll.y
                    && card.y <= navigationScroll.y + visibleHeight)
                {
                    Widgets.DrawBoxSolidWithOutline(imageRect, new Color(0.16f, 0.18f, 0.20f),
                        new Color(0.32f, 0.39f, 0.42f), 1);
                    ModularWeaponPartImage.For(row.Node.thing.def).Draw(imageRect.ContractedBy(2f));
                    if (pointerInTree && Mouse.IsOver(imageRect)) hoveredPart = row.Node.thing.def;
                    TooltipHandler.TipRegion(imageRect, "HD_ModularWeapon_OpenPartImage".Translate());
                    if (Widgets.ButtonInvisible(imageRect))
                    {
                        SelectNode(row.Node);
                        Find.WindowStack.Add(new Dialog_ModularWeaponPartImage(row.Node.thing.def));
                    }
                }
                float textX = imageRect.xMax + 5f;
                Rect selectRect = new Rect(textX, card.y, card.xMax - textX, card.height);
                Text.Font = GameFont.Tiny;
                GUI.color = row.IsEmpty ? new Color(1f, 0.65f, 0.3f)
                    : new Color(0.65f, 0.73f, 0.76f);
                string socketLabel = row.Socket?.Label
                    ?? "HD_ModularWeapon_AssemblyRoot".Translate().ToString();
                if (row.Socket?.isRail == true)
                    socketLabel += "  ·  " + SurfaceText(row.Socket.railSurface);
                Widgets.Label(new Rect(textX, card.y + 2f, selectRect.width - 4f, 16f), socketLabel);
                GUI.color = row.IsEmpty ? new Color(1f, 0.65f, 0.3f)
                    : row.MatchesSearch ? new Color(0.8f, 1f, 0.65f) : Color.white;
                string label = row.IsEmpty ? "HD_ModularWeapon_EmptyRequiredSocket".Translate().ToString()
                    : row.Node.thing.LabelCap.ToString();
                if (!row.IsEmpty && row.DescendantCount > 0)
                    label += "  (" + row.DescendantCount + ")";
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(textX, card.y + 18f, selectRect.width - 4f, 24f), label);
                GUI.color = Color.white;
                string tip = navigationTree.Breadcrumb(row.Owner);
                if (row.Socket != null) tip += "\n" + ConnectionText(row.Node, row.Socket);
                if (!row.IsEmpty && row.Node.Props.hideWhenAttached)
                    tip += "\n" + "HD_ModularWeapon_InternalPart".Translate();
                TooltipHandler.TipRegion(selectRect, tip);
                if (hovered) hoveredNode = row.Owner;
                if (Widgets.ButtonInvisible(selectRect))
                {
                    SelectNode(row.Owner);
                    if (row.IsEmpty && selectedSocketId != row.Socket.id)
                    {
                        selectedSocketId = row.Socket.id;
                        selectedAttachmentId = -1;
                        selectedCatalogGroup = null;
                        catalogScroll = Vector2.zero;
                    }
                }
            }
            Widgets.EndScrollView();
            Text.WordWrap = previousWrap;
            Text.Font = GameFont.Small;
        }

        private void SelectNode(ModularRenderNode node)
        {
            if (node == null) return;
            if (selectedPath != node.path)
            {
                selectedPath = node.path;
                selectedSocketId = null;
                selectedAttachmentId = -1;
                selectedCatalogGroup = null;
                socketScroll = Vector2.zero;
                catalogScroll = Vector2.zero;
            }
            navigationTree?.Reveal(node, collapsedParts);
            revealNavigationSelection = true;
        }

        private static string ConnectionText(ModularRenderNode node, ModularAttachmentSocket socket)
        {
            string text = "HD_ModularWeapon_ConnectedVia".Translate(socket.Label);
            if (socket.isRail)
            {
                text += "  ·  " + SurfaceText(socket.railSurface);
                if (node?.attachedToRail == true)
                    text += "  ·  " + "HD_ModularWeapon_RailOffset".Translate(
                        node.railOffset.ToString("0.###", CultureInfo.InvariantCulture));
            }
            if (node?.mountedOnOppositeSurface == true)
                text += "  ·  " + "HD_ModularWeapon_OppositeMounted".Translate();
            return text;
        }
    }
}
