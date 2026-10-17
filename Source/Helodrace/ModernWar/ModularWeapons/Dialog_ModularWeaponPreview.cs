using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularWeapon
    {
        private readonly ModularWeaponPreviewCamera previewCamera = new ModularWeaponPreviewCamera();

        private static Rect PreviewArea(Rect rect)
        {
            Rect inner = rect.ContractedBy(18f);
            inner.yMin += 122f;
            inner.yMax -= 46f;
            return inner;
        }

        private bool PreviewCoveredByStats(Rect rect)
        {
            return performanceExpanded && Mouse.IsOver(
                new Rect(rect.x + 10f, rect.y + 44f, 430f, 390f));
        }

        private void HandlePreviewNavigation(Rect rect)
        {
            Rect area = PreviewArea(rect);
            if (!Mouse.IsOver(area) || PreviewCoveredByStats(rect)) return;
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
            Rect area = PreviewArea(rect);
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
        }

        private void UpdatePreviewHover(Rect rect, List<ModularRenderNode> snapshot)
        {
            Rect inner = PreviewArea(rect);
            if (!Mouse.IsOver(inner)) return;
            if (PreviewCoveredByStats(rect)) return;
            Vector2 origin = previewCamera.OriginFor(inner);
            float scale = previewCamera.ScaleFor(inner);
            List<ModularRenderNode> layers = ModularWeaponVisualLayers.Expand(snapshot);
            // Test the visible pixels in reverse draw order, including additional layers.
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                ModularRenderNode node = layers[i];
                if (!ModularWeaponAssemblyRenderer.ShouldDrawNode(node)
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
            foreach (ModularRenderNode layer in ModularWeaponVisualLayers.Expand(navigationSnapshot))
            {
                if (layer.path != node.path) continue;
                Texture texture = layer.Graphic?.MatSingle?.mainTexture;
                if (texture == null) continue;
                float angle;
                Rect drawRect = PreviewPartRect(layer, origin, scale, out angle);
                if (layer.additionalGraphic == null) marker = drawRect.center;
                Matrix4x4 matrix = GUI.matrix;
                Color color = GUI.color;
                UI.RotateAroundPivot(-angle, drawRect.center);
                // Draw over the assembly so internal/covered parts can also be located.
                GUI.color = new Color(1f, 0.95f, 0.65f);
                DrawTexture(drawRect, texture, layer.GraphicVerticallyFlipped);
                GUI.color = color;
                GUI.matrix = matrix;
            }

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
