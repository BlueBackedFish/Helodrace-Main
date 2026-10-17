using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularWeapon
    {
        private sealed class SocketCard
        {
            public ModularAttachmentSocket Socket;
            public ModularRenderNode Child;
            public Rect Rect;
            public Vector2 Anchor;
            public bool Add => Socket.isRail && Child == null;
        }

        private readonly List<SocketCard> socketCards = new List<SocketCard>();
        private ModularRenderNode socketOwner;
        private int socketPage;
        private int socketPages = 1;
        private int socketPageStart;
        private int revealSocketAttachment = -1;
        private float? preferredRailOffset;
        private string preferredRailOwner, preferredRailSocket;
        private string lastRailRejection;
        private float lastRailRejectionAt;

        private Rect PreviewCameraArea(Rect rect)
        {
            Rect area = PreviewArea(rect);
            if (socketCards.Count > 0 && area.height >= 200f)
            {
                area.yMin += ModularWeaponSocketLayout.CardHeight + 8f;
                area.yMax -= ModularWeaponSocketLayout.CardHeight + 8f;
            }
            return area;
        }

        private Rect SocketPageControls(Rect rect) => new Rect(rect.xMax - 130f, rect.y + 43f, 118f, 25f);

        private void PrepareSocketCards(Rect rect, ModularRenderNode owner, List<ModularRenderNode> snapshot)
        {
            if (socketOwner?.path != owner?.path) socketPage = 0;
            socketOwner = owner;
            socketCards.Clear();
            if (owner == null || !IsPreviewPart(owner)) return;
            foreach (ModularAttachmentSocket socket in owner.Props.sockets)
            {
                if (ShouldHideCEAmmunitionSocket(socket)) continue;
                var children = snapshot.Where(node => node.parentComp == owner.comp
                    && node.parentSocketId == socket.id && !node.thing.Destroyed)
                    .OrderBy(node => node.parentChildIndex).ToList();
                if (socket.isRail || children.Count == 0)
                    socketCards.Add(new SocketCard { Socket = socket });
                foreach (var child in children)
                    socketCards.Add(new SocketCard { Socket = socket, Child = child });
            }
            Rect area = PreviewArea(rect);
            socketPages = ModularWeaponSocketLayout.PageCount(area, socketCards.Count);
            if (revealSocketAttachment >= 0)
            {
                int index = socketCards.FindIndex(card => card.Child?.thing.thingIDNumber == revealSocketAttachment);
                if (index >= 0)
                {
                    socketPage = ModularWeaponSocketLayout.PageFor(area, index);
                    revealSocketAttachment = -1;
                }
            }
            socketPage = Mathf.Clamp(socketPage, 0, socketPages - 1);
            socketPageStart = socketPage * ModularWeaponSocketLayout.Capacity(area);
            List<Rect> positions = ModularWeaponSocketLayout.Arrange(area, socketCards.Count, socketPage);
            Rect cameraArea = PreviewCameraArea(rect);
            Vector2 origin = previewCamera.OriginFor(cameraArea);
            float scale = previewCamera.ScaleFor(cameraArea);
            for (int i = 0; i < socketCards.Count; i++)
            {
                var card = socketCards[i];
                Vector3 local = card.Socket.transform?.position ?? Vector3.zero;
                if (card.Child?.attachedToRail == true) local.x += card.Child.railOffset;
                card.Anchor = WorldToGui(RealtimeSocketPoint(owner, local), origin, scale);
                card.Rect = i >= socketPageStart && i < socketPageStart + positions.Count
                    ? positions[i - socketPageStart] : default(Rect);
                if (card.Rect.width > 0f && Mouse.IsOver(card.Rect)) hoveredNode = card.Child ?? owner;
            }
        }

        private Vector2 RealtimeSocketPoint(ModularRenderNode owner, Vector3 local)
        {
            Vector2 center;
            float angle;
            ModularWeaponAssemblyRenderer.RealtimeNodeGeometry(root, owner, false, out center, out angle);
            Vector2 offset = owner.transform.TransformPoint(local) - owner.GraphicCenter;
            return center + ModularTransform2D.Rotate(offset, angle - owner.GraphicAngle);
        }

        private void RailScreenPoints(Rect rect, ModularAttachmentSocket socket, out Vector2 start, out Vector2 end)
        {
            Vector3 a = socket.transform?.position ?? Vector3.zero, b = a;
            a.x -= socket.railLength * 0.5f;
            b.x += socket.railLength * 0.5f;
            Rect area = PreviewCameraArea(rect);
            Vector2 origin = previewCamera.OriginFor(area);
            float scale = previewCamera.ScaleFor(area);
            start = WorldToGui(RealtimeSocketPoint(socketOwner, a), origin, scale);
            end = WorldToGui(RealtimeSocketPoint(socketOwner, b), origin, scale);
        }

        private bool PointerOverSocketCard(Rect rect)
        {
            return socketCards.Any(card => card.Rect.width > 0f && Mouse.IsOver(card.Rect))
                || (socketPages > 1 && Mouse.IsOver(SocketPageControls(rect)));
        }

        private void SelectPreviewSocket(ModularRenderNode owner, ModularAttachmentSocket socket,
            float? railOffset = null)
        {
            SelectNode(owner);
            selectedSocketId = socket.id;
            selectedAttachmentId = -1;
            selectedCatalogGroup = null;
            catalogScroll = Vector2.zero;
            preferredRailOffset = railOffset;
            preferredRailOwner = owner.path;
            preferredRailSocket = socket.id;
        }

        private void HandleRailClick(Rect rect)
        {
            if (socketOwner == null || !Mouse.IsOver(PreviewArea(rect)) || PointerOverSocketCard(rect)) return;
            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0) return;
            ModularAttachmentSocket closest = null;
            float distance = 9f;
            float fraction = 0.5f;
            foreach (var socket in socketOwner.Props.sockets)
            {
                if (!socket.isRail || ShouldHideCEAmmunitionSocket(socket)) continue;
                Vector2 start, end;
                RailScreenPoints(rect, socket, out start, out end);
                float candidate = ModularWeaponSocketLayout.Distance(current.mousePosition, start, end);
                if (candidate > distance || (closest != null && Mathf.Approximately(candidate, distance)
                    && socket.id != selectedSocketId)) continue;
                closest = socket;
                distance = candidate;
                fraction = ModularWeaponSocketLayout.Fraction(current.mousePosition, start, end);
            }
            if (closest == null) return;
            SelectPreviewSocket(socketOwner, closest, (fraction - 0.5f) * closest.railLength);
            int index = socketCards.FindIndex(card => card.Socket == closest && card.Add);
            if (index >= 0) socketPage = ModularWeaponSocketLayout.PageFor(PreviewArea(rect), index);
            current.Use();
        }

        private void DrawSocketOverlay(Rect rect)
        {
            if (socketOwner == null) return;
            Rect area = PreviewArea(rect);
            GUI.BeginGroup(area);
            foreach (var socket in socketOwner.Props.sockets)
            {
                if (!socket.isRail || ShouldHideCEAmmunitionSocket(socket)) continue;
                Vector2 start, end;
                RailScreenPoints(rect, socket, out start, out end);
                bool active = selectedSocketId == socket.id;
                Color color = active ? new Color(1f, 0.82f, 0.25f) : new Color(0.25f, 0.8f, 0.95f);
                Widgets.DrawLine(start - area.position, end - area.position, color, active ? 6f : 4f);
                foreach (var card in socketCards.Where(card => card.Socket == socket && card.Child != null))
                {
                    float fraction = socket.railLength <= 0f ? 0.5f
                        : card.Child.railOffset / socket.railLength + 0.5f;
                    Vector2 marker = start + (end - start) * fraction - area.position;
                    Widgets.DrawBoxSolidWithOutline(CenteredRect(marker, new Vector2(9f, 9f)),
                        new Color(0.08f, 0.12f, 0.14f),
                        card.Child.thing.thingIDNumber == selectedAttachmentId ? Color.yellow : Color.white, 1);
                }
                if (active && preferredRailOffset.HasValue && preferredRailOwner == socketOwner.path
                    && preferredRailSocket == socket.id && socket.railLength > 0f)
                {
                    Vector2 marker = start + (end - start)
                        * (preferredRailOffset.Value / socket.railLength + 0.5f) - area.position;
                    Widgets.DrawBoxSolid(CenteredRect(marker, new Vector2(4f, 15f)), Color.yellow);
                }
            }
            foreach (SocketCard card in socketCards)
            {
                if (card.Rect.width <= 0f) continue;
                Vector2 target = new Vector2(card.Rect.center.x,
                    card.Rect.center.y < area.center.y ? card.Rect.yMax : card.Rect.y);
                bool active = card.Child == null ? card.Socket.id == selectedSocketId
                    : card.Child.thing.thingIDNumber == selectedAttachmentId;
                Color color = active ? new Color(1f, 0.85f, 0.35f) : new Color(0.48f, 0.65f, 0.72f);
                Widgets.DrawLine(card.Anchor - area.position, target - area.position, color, active ? 2f : 1f);
                if (!card.Socket.isRail)
                    Widgets.DrawBoxSolidWithOutline(CenteredRect(card.Anchor - area.position, new Vector2(8f, 8f)),
                        new Color(0.08f, 0.12f, 0.14f), color, 1);
            }
            GUI.EndGroup();
            bool wrap = Text.WordWrap;
            Text.WordWrap = false;
            foreach (var card in socketCards)
            {
                if (card.Rect.width <= 0f || card.Child?.thing.Destroyed == true) continue;
                if (DrawSocketCard(card)) break;
            }
            Text.WordWrap = wrap;
            Text.Font = GameFont.Small;
            if (socketPages > 1)
            {
                Rect controls = SocketPageControls(rect);
                if (Widgets.ButtonText(new Rect(controls.x, controls.y, 24f, controls.height), "‹"))
                    socketPage = (socketPage + socketPages - 1) % socketPages;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(controls.x + 27f, controls.y, 62f, controls.height),
                    (socketPage + 1) + " / " + socketPages);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(controls.xMax - 24f, controls.y, 24f, controls.height), "›"))
                    socketPage = (socketPage + 1) % socketPages;
            }
        }

        // Returns true after a mutation; stop drawing records from the old snapshot.
        private bool DrawSocketCard(SocketCard card)
        {
            bool active = card.Child == null ? card.Socket.id == selectedSocketId && selectedAttachmentId < 0
                : card.Child.thing.thingIDNumber == selectedAttachmentId;
            Rect box = card.Rect;
            Widgets.DrawBoxSolidWithOutline(box, active ? new Color(0.13f, 0.22f, 0.24f)
                : new Color(0.065f, 0.09f, 0.11f), active ? Color.yellow : new Color(0.37f, 0.52f, 0.59f), 1);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(box.x + 6f, box.y + 2f, box.width - 34f, 18f), card.Socket.Label);
            string label = card.Child?.thing.LabelCap.ToString()
                ?? (card.Add ? "HD_ModularWeapon_AddRailPart" : "HD_ModularWeapon_AddSocketPart").Translate().ToString();
            float textX = box.x + 7f;
            if (card.Child != null)
            {
                Rect image = new Rect(box.x + 5f, box.y + 20f, 25f, 25f);
                ModularWeaponPartImage.For(card.Child.thing.def).Draw(image);
                TooltipHandler.TipRegion(image, "HD_ModularWeapon_InspectSocketPart".Translate());
                if (Widgets.ButtonInvisible(image))
                {
                    SelectNode(card.Child);
                    return true;
                }
                textX = image.xMax + 4f;
            }
            Widgets.Label(new Rect(textX, box.y + 21f, box.xMax - textX - 5f, 19f), label);
            if (card.Child != null && (!card.Socket.required || card.Socket.isRail))
            {
                Rect remove = new Rect(box.xMax - 25f, box.y + 3f, 21f, 21f);
                TooltipHandler.TipRegion(remove, "HD_ModularWeapon_RemoveSocketPart".Translate());
                if (Widgets.ButtonText(remove, "×"))
                {
                    int index = ChildIndex(card.Child.thing.thingIDNumber);
                    if (index >= 0)
                    {
                        Thing removed = socketOwner.comp.DetachChildAt(index);
                        ReturnOrDestroyAssembly(removed);
                        selectedAttachmentId = -1;
                        preferredRailOffset = null;
                        root.InvalidateTree();
                    }
                    return true;
                }
            }
            if (card.Child?.attachedToRail == true)
            {
                if (active) DrawRailPositionSlider(card);
                else
                    Widgets.Label(new Rect(textX, box.y + 38f, box.xMax - textX - 5f, 17f),
                        card.Child.railOffset.ToString("0.###", CultureInfo.InvariantCulture));
            }
            else if (card.Add)
                Widgets.Label(new Rect(box.x + 7f, box.y + 38f, box.width - 14f, 17f),
                    "HD_ModularWeapon_RailPartCount".Translate(socketOwner.comp.CountOnSocket(card.Socket.id)));
            // Child/image/remove/slider controls receive input before the card body.
            if (Widgets.ButtonInvisible(box) && (card.Child == null || !active))
            {
                SelectPreviewSocket(socketOwner, card.Socket);
                if (card.Child != null) selectedAttachmentId = card.Child.thing.thingIDNumber;
            }
            TooltipHandler.TipRegion(box, card.Socket.Label + "\n" + label + "\n"
                + "HD_ModularWeapon_SocketOverlayHint".Translate());
            return false;
        }

        private int ChildIndex(int id)
        {
            for (int i = 0; i < socketOwner.comp.ChildCount; i++)
                if (socketOwner.comp.ChildAt(i)?.thingIDNumber == id) return i;
            return -1;
        }

        private void DrawRailPositionSlider(SocketCard card)
        {
            int index = ChildIndex(card.Child.thing.thingIDNumber);
            if (index < 0) return;
            var mount = card.Child.Props.MountNamed(card.Child.mountId);
            float ratio = socketOwner.comp.PaletteRatioFor(card.Child.comp);
            float half = Mathf.Max(0f, (card.Socket.railLength
                - (mount?.EffectiveRailContactLength ?? 0f) * ratio) * 0.5f);
            float old = socketOwner.comp.RailOffsetAt(index);
            float value = half <= 0f ? 0f : Widgets.HorizontalSlider(
                new Rect(card.Rect.x + 7f, card.Rect.y + 39f, card.Rect.width - 65f, 13f), old, -half, half, true);
            Widgets.Label(new Rect(card.Rect.xMax - 54f, card.Rect.y + 37f, 49f, 18f),
                old.ToString("0.###", CultureInfo.InvariantCulture));
            if (Mathf.Approximately(old, value)) return;
            string reason;
            if (socketOwner.comp.TrySetRailOffset(index, value, out reason)) lastRailRejection = null;
            else if (reason != lastRailRejection || Time.realtimeSinceStartup - lastRailRejectionAt > 1f)
            {
                lastRailRejection = reason;
                lastRailRejectionAt = Time.realtimeSinceStartup;
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            }
        }

        private bool TryInstallAtPreviewSocket(ModularRenderNode owner, ModularAttachmentSocket socket,
            Thing child, ModularAttachmentMount mount, out string reason, float? replacementOffset = null)
        {
            float offset;
            float? preferred = replacementOffset ?? (preferredRailOwner == owner.path
                && preferredRailSocket == socket.id ? preferredRailOffset : null);
            if (socket.isRail && preferred.HasValue)
            {
                var childComp = child.TryGetComp<CompModularWeaponNode>();
                float half = (socket.railLength - mount.EffectiveRailContactLength
                    * owner.comp.PaletteRatioFor(childComp)) * 0.5f;
                if (half >= 0f)
                    return owner.comp.TryAttach(child, socket.id, mount.id,
                        Mathf.Clamp(preferred.Value, -half, half), out reason);
            }
            return owner.comp.TryFindAttachOffset(child, socket.id, mount.id, out offset, out reason)
                && owner.comp.TryAttach(child, socket.id, mount.id, offset, out reason);
        }
    }
}
