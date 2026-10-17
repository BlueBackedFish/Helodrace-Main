using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularWeapon
    {
        private List<ModularRenderNode> groupSnapshot;
        private List<ModularWeaponPreviewGroup> previewGroups;
        private ModularWeaponPreviewGroup activePreviewGroup;
        private List<ModularRenderNode> previewLayers;
        private bool previewNeedsFit;
        private HashSet<int> PreviewScope => activePreviewGroup == null
            || activePreviewGroup.Kind == ModularWeaponPreviewGroupKind.Assembly
                ? null : activePreviewGroup.PartIds;

        private bool IsPreviewPart(ModularRenderNode node)
            => node != null && (activePreviewGroup == null || activePreviewGroup.Contains(node));

        private void EnsurePreviewGroups(List<ModularRenderNode> snapshot)
        {
            if (ReferenceEquals(groupSnapshot, snapshot)) return;
            groupSnapshot = snapshot;
            string key = activePreviewGroup?.Key ?? "all";
            previewGroups = ModularWeaponPreviewGroups.Build(snapshot, root.SightGroups);
            activePreviewGroup = previewGroups.FirstOrDefault(group => group.Key == key) ?? previewGroups[0];
            if (activePreviewGroup.Key != key) previewCamera.Reset();
            RefreshPreviewLayers();
            if (!activePreviewGroup.Contains(FindSelected(snapshot)))
                SelectNode(activePreviewGroup.Root);
        }

        private void RefreshPreviewLayers()
        {
            previewLayers = ModularWeaponVisualLayers.Expand(groupSnapshot)
                .Where(node => IsPreviewPart(node) && !ShouldHideCEAmmunitionPart(node)).ToList();
            previewNeedsFit = true;
        }

        private void SelectPreviewGroup(ModularWeaponPreviewGroup group, bool selectRoot = true)
        {
            activePreviewGroup = previewGroups.FirstOrDefault(current => current.Key == group.Key)
                ?? previewGroups[0];
            RefreshPreviewLayers();
            previewCamera.Reset();
            navigationSearch = string.Empty;
            collapsedParts.Clear();
            navigationScroll = Vector2.zero;
            if (selectRoot) SelectNode(activePreviewGroup.Root);
            revealNavigationSelection = true;
        }

        private void DrawPreviewGroupSelector(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            string label = PreviewGroupLabel(activePreviewGroup);
            if (Widgets.ButtonText(rect, "HD_ModularWeapon_ViewGroup".Translate(label)))
            {
                var options = new List<FloatMenuOption>();
                foreach (var group in previewGroups)
                {
                    ModularWeaponPreviewGroup option = group;
                    options.Add(new FloatMenuOption((option.Key == activePreviewGroup.Key ? "• " : string.Empty)
                        + PreviewGroupLabel(option), () => SelectPreviewGroup(option)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            TooltipHandler.TipRegion(rect, label + "\n" + "HD_ModularWeapon_GroupMenuHint".Translate());
            Text.Font = GameFont.Small;
        }

        private static string PreviewGroupLabel(ModularWeaponPreviewGroup group)
        {
            switch (group.Kind)
            {
                case ModularWeaponPreviewGroupKind.MainWeapon:
                    return "HD_ModularWeapon_GroupMain".Translate();
                case ModularWeaponPreviewGroupKind.AllSights:
                    return "HD_ModularWeapon_GroupAllSights".Translate();
                case ModularWeaponPreviewGroupKind.Sight:
                    return "HD_ModularWeapon_GroupSight".Translate(group.SightIndex + 1,
                        group.Root.thing.LabelCap).ToString()
                        + (group.WeaponRoot?.depth > 0 ? "  ·  " + group.WeaponRoot.thing.LabelCap.ToString() : string.Empty);
                case ModularWeaponPreviewGroupKind.AttachedWeapon:
                    return "HD_ModularWeapon_GroupAttached".Translate(group.Root.thing.LabelCap);
                default:
                    return "HD_ModularWeapon_GroupAll".Translate();
            }
        }

        private void UpdatePreviewBounds()
        {
            if (!previewNeedsFit) return;
            bool found = false;
            Rect bounds = default(Rect);
            foreach (var node in previewLayers)
            {
                if (!ModularWeaponAssemblyRenderer.ShouldDrawNode(node)) continue;
                Graphic graphic = node.Graphic;
                if (graphic == null) continue;
                Texture2D texture = graphic.MatSingle?.mainTexture as Texture2D;
                if (texture == null) continue;
                IncludePreviewBounds(node, texture, ref bounds, ref found);
                string path = node.TexturePath;
                Texture2D outline = path.NullOrEmpty() ? null
                    : ContentFinder<Texture2D>.Get(path + "_Outline", false);
                if (outline != null) IncludePreviewBounds(node, outline, ref bounds, ref found);
            }
            previewCamera.SetBounds(found ? bounds : new Rect(-0.5f, -0.5f, 1f, 1f));
            previewNeedsFit = false;
        }

        private void IncludePreviewBounds(ModularRenderNode node, Texture2D texture,
            ref Rect bounds, ref bool found)
        {
            var data = ModularWeaponTextureData.For(texture);
            if (!data.HasPixels) return;
            Rect uv = data.VisibleUv;
            if (node.GraphicVerticallyFlipped) uv.y = 1f - uv.yMax;
            Vector2 graphicScale = node.GraphicScale;
            Rect local = ModularWeaponImageBounds.OnCanvas(uv, Vector2.Scale(node.Graphic.drawSize,
                new Vector2(Mathf.Abs(graphicScale.x), Mathf.Abs(graphicScale.y))));
            Vector2 center;
            float angle;
            ModularWeaponAssemblyRenderer.RealtimeNodeGeometry(root, node, false, out center, out angle);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                {
                    Vector2 point = center + ModularTransform2D.Rotate(
                        new Vector2(x == 0 ? local.xMin : local.xMax, y == 0 ? local.yMin : local.yMax), angle);
                    var corner = new Rect(point.x, point.y, 0f, 0f);
                    bounds = found ? ModularWeaponImageBounds.Union(bounds, corner) : corner;
                    found = true;
                }
        }
    }
}
