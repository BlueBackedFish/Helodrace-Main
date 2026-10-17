using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularWeapon
    {
        private readonly ModularWeaponPreviewCamera previewCamera = new ModularWeaponPreviewCamera();

        private Rect PreviewArea(Rect rect)
        {
            Rect inner = rect.ContractedBy(18f);
            inner.yMin += 60f;
            inner.yMax -= 46f;
            return inner;
        }

        private void HandlePreviewNavigation(Rect rect)
        {
            Rect area = PreviewCameraArea(rect);
            if (!Mouse.IsOver(PreviewArea(rect))) return;
            Event current = Event.current;
            if (current.type == EventType.ScrollWheel)
            {
                previewCamera.ZoomAt(area, previewCamera.Zoom * Mathf.Pow(1.12f, -current.delta.y),
                    current.mousePosition);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 2)
            {
                previewCamera.Move(current.delta);
                current.Use();
            }
        }

        private void DrawPreviewToolbar(Rect rect)
        {
            Rect toolbar = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 27f);
            Rect area = PreviewCameraArea(rect);
            if (Widgets.ButtonText(new Rect(toolbar.x, toolbar.y, 28f, toolbar.height), "−"))
                previewCamera.ZoomAt(area, previewCamera.Zoom / 1.25f, area.center);
            if (Widgets.ButtonText(new Rect(toolbar.x + 34f, toolbar.y, 28f, toolbar.height), "+"))
                previewCamera.ZoomAt(area, previewCamera.Zoom * 1.25f, area.center);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(toolbar.x + 66f, toolbar.y, 52f, toolbar.height),
                previewCamera.Zoom.ToString("P0"));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(toolbar.x + 123f, toolbar.y, 100f, toolbar.height),
                "HD_ModularWeapon_ResetView".Translate())) previewCamera.Reset();
            TooltipHandler.TipRegion(toolbar, "HD_ModularWeapon_ZoomHint".Translate());
            DrawPreviewGroupSelector(new Rect(toolbar.x + 231f, toolbar.y,
                Mathf.Max(40f, toolbar.width - 231f), toolbar.height));
        }

        private void UpdatePreviewHover(Rect rect, List<ModularRenderNode> snapshot)
        {
            Rect inner = PreviewArea(rect);
            if (!Mouse.IsOver(inner)) return;
            Rect cameraArea = PreviewCameraArea(rect);
            Vector2 origin = previewCamera.OriginFor(cameraArea);
            float scale = previewCamera.ScaleFor(cameraArea);
            ModularRenderNode front = SelectedPreviewPart(FindSelected(snapshot));
            // Hit testing follows the selected part's foreground draw order.
            foreach (ModularRenderNode node in PreviewHitOrder(front))
            {
                if ((node.path != front?.path && !ModularWeaponAssemblyRenderer.ShouldDrawNode(node))
                    || ShouldHideCEAmmunitionPart(node)) continue;
                Texture2D texture = node.Graphic?.MatSingle?.mainTexture as Texture2D;
                if (texture == null) continue;
                float angle;
                Rect drawRect = PreviewPartRect(node, origin, scale, out angle);
                if (drawRect.width <= 0f || drawRect.height <= 0f) continue;
                Vector2 point = drawRect.center + ModularTransform2D.Rotate(
                    Event.current.mousePosition - drawRect.center, angle);
                if (!drawRect.Contains(point)) continue;
                float u = (point.x - drawRect.x) / drawRect.width;
                float v = 1f - (point.y - drawRect.y) / drawRect.height;
                if (node.GraphicVerticallyFlipped) v = 1f - v;
                if (!ModularWeaponTextureData.For(texture).Contains(u, v)) continue;
                // Visual layer copies resolve back to their real assembly node.
                hoveredNode = snapshot.Find(part => part.path == node.path);
                if (hoveredNode != null && Widgets.ButtonInvisible(inner))
                {
                    navigationSearch = string.Empty;
                    GUI.FocusControl(null);
                    SelectNode(hoveredNode);
                }
                break;
            }
        }

        private ModularRenderNode SelectedPreviewPart(ModularRenderNode selected)
        {
            ModularRenderNode attached = navigationSnapshot.Find(node =>
                node.thing.thingIDNumber == selectedAttachmentId && node.parentComp == selected?.comp);
            return IsPreviewPart(attached) ? attached : IsPreviewPart(selected) ? selected : null;
        }

        private IEnumerable<ModularRenderNode> PreviewHitOrder(ModularRenderNode front)
        {
            for (int priority = 0; priority < 2; priority++)
                for (int i = previewLayers.Count - 1; i >= 0; i--)
                    if ((previewLayers[i].path == front?.path) == (priority == 0))
                        yield return previewLayers[i];
        }

        private void DrawPreviewAssembly(Vector2 origin, float scale, ModularRenderNode front)
        {
            Color previous = GUI.color;
            // All authored outlines stay behind every body layer, including selection.
            // Only the selected body's layers move to the front and receive the tint.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (ModularRenderNode node in previewLayers)
                {
                    bool selected = node.path == front?.path;
                    bool hovered = node.path == hoveredNode?.path;
                    if ((pass == 1 && selected) || (pass == 2 && !selected)
                        || (!selected && !hovered && !ModularWeaponAssemblyRenderer.ShouldDrawNode(node))) continue;
                    GUI.color = pass == 0 ? Color.white
                        : selected ? new Color(1f, 0.95f, 0.65f)
                        : hovered ? new Color(0.65f, 0.9f, 1f) : Color.white;
                    string path = node.TexturePath;
                    Texture texture = pass == 0
                        ? path.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(path + "_Outline", false)
                        : node.Graphic?.MatSingle?.mainTexture;
                    if (texture == null || node.Graphic == null) continue;
                    float angle;
                    Rect drawRect = PreviewPartRect(node, origin, scale, out angle);
                    Matrix4x4 matrix = GUI.matrix;
                    UI.RotateAroundPivot(-angle, drawRect.center);
                    DrawTexture(drawRect, texture, node.GraphicVerticallyFlipped);
                    GUI.matrix = matrix;
                }
            }
            GUI.color = previous;
        }

        private Rect PreviewPartRect(ModularRenderNode node, Vector2 origin,
            float scale, out float angle)
        {
            Vector2 center;
            ModularWeaponAssemblyRenderer.RealtimeNodeGeometry(root, node, false, out center, out angle);
            Vector2 graphicScale = node.GraphicScale;
            Vector2 size = Vector2.Scale(node.Graphic?.drawSize ?? Vector2.zero,
                new Vector2(Mathf.Abs(graphicScale.x), Mathf.Abs(graphicScale.y))) * scale;
            return CenteredRect(WorldToGui(center, origin, scale), size);
        }

        private void DrawPreviewHighlight(Rect rect, ModularRenderNode node,
            Vector2 origin, float scale)
        {
            if (node == null) return;
            Rect area = PreviewArea(rect);
            GUI.BeginGroup(area);
            origin -= area.position;
            Vector2 marker = WorldToGui(node.GraphicCenter, origin, scale);
            float markerAngle;
            marker = PreviewPartRect(node, origin, scale, out markerAngle).center;

            Rect caption = new Rect(rect.x + 10f, rect.yMax - 47f, rect.width - 20f, 39f);
            Widgets.DrawLine(marker, new Vector2(area.width * 0.5f, area.height),
                new Color(0.95f, 0.8f, 0.3f), 1f);
            Widgets.DrawBoxSolidWithOutline(CenteredRect(marker, new Vector2(8f, 8f)),
                new Color(0.1f, 0.14f, 0.15f), Color.yellow, 1);
            GUI.EndGroup();
            Widgets.DrawBoxSolidWithOutline(caption, new Color(0.055f, 0.07f, 0.08f, 0.96f),
                new Color(0.6f, 0.55f, 0.3f), 1);
            Text.Font = GameFont.Tiny;
            bool wrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(new Rect(caption.x + 7f, caption.y + 2f, caption.width - 14f, 18f),
                node.thing.LabelCap + (node.Props.hideWhenAttached
                    ? "  ·  " + "HD_ModularWeapon_InternalPart".Translate().ToString() : string.Empty));
            ModularAttachmentSocket socket = node.parentComp?.Props.SocketNamed(node.parentSocketId);
            GUI.color = new Color(0.72f, 0.8f, 0.82f);
            Widgets.Label(new Rect(caption.x + 7f, caption.y + 20f, caption.width - 14f, 18f),
                socket == null ? "HD_ModularWeapon_AssemblyRoot".Translate().ToString()
                    : ConnectionText(node, socket));
            GUI.color = Color.white;
            Text.WordWrap = wrap;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(caption, navigationTree.Breadcrumb(node));
        }
    }
}
