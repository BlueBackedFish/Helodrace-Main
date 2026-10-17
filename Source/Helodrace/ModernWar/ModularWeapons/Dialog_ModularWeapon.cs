using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularWeapon : Window
    {
        private readonly CompModularWeaponNode root;
        private readonly ModularWeaponWorkshopSession workshop;
        private string selectedPath = "root";
        private string selectedSocketId;
        private int selectedAttachmentId = -1;
        private Vector2 navigationScroll;
        private Vector2 catalogScroll;
        private string selectedCatalogGroup;
        private ThingDef hoveredPart;

        public Dialog_ModularWeapon(CompModularWeaponNode root)
            : this(root, null)
        {
        }

        public Dialog_ModularWeapon(
            CompModularWeaponNode root,
            ModularWeaponWorkshopSession workshop)
        {
            this.root = root;
            this.workshop = workshop;
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
            hoveredNode = null;
            if (workshop != null && !workshop.IsValidFor(root))
            {
                Close();
                return;
            }
            if (root?.parent == null || root.parent.Destroyed)
            {
                Widgets.Label(inRect, "HD_ModularWeapon_Missing".Translate());
                return;
            }

            List<ModularRenderNode> snapshot = root.RenderSnapshot();
            if (SynchronizeCEAmmunitionToChamber(snapshot))
                snapshot = root.RenderSnapshot();
            EnsureNavigation(snapshot);
            EnsurePreviewGroups(snapshot);
            ModularRenderNode selected = FindSelected(snapshot);
            if (ShouldHideCEAmmunitionPart(selected))
            {
                selected = snapshot.FirstOrDefault(node => node.depth == 0);
                if (selected != null) SelectNode(selected);
            }
            EnsureSelectedSocket(selected);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 205f, 32f),
                "HD_ModularWeapon_Title".Translate(root.parent.LabelCap));
            Text.Font = GameFont.Small;
            Rect exportRect = new Rect(inRect.width - 190f, 0f, 170f, 30f);
            if (Widgets.ButtonText(exportRect,
                "HD_ModularWeapon_ExportPreset".Translate()))
            {
                GUIUtility.systemCopyBuffer = ModularPresetXmlExporter.ExportWeapon(root);
                Messages.Message(
                    "HD_ModularWeapon_ExportPresetCopied".Translate(),
                    MessageTypeDefOf.PositiveEvent,
                    false);
            }
            TooltipHandler.TipRegion(
                exportRect,
                "HD_ModularWeapon_ExportPresetDesc".Translate());

            float bodyTop = 40f;
            float catalogHeight = 206f;
            float footerHeight = 38f;
            float bodyHeight = inRect.height - bodyTop - catalogHeight - footerHeight - 8f;
            float treeWidth = Mathf.Clamp(inRect.width * 0.23f, 260f, 320f);
            float statsWidth = Mathf.Clamp(inRect.width * 0.21f, 250f, 300f);
            Rect treeRect = new Rect(0f, bodyTop, treeWidth, bodyHeight);
            Rect previewRect = new Rect(treeRect.xMax + 12f, bodyTop,
                inRect.width - treeWidth - statsWidth - 24f, bodyHeight);
            Rect detailsRect = new Rect(inRect.width - statsWidth, bodyTop,
                statsWidth, bodyHeight);
            Rect catalogRect = new Rect(0f, bodyTop + bodyHeight + 8f,
                inRect.width, catalogHeight);

            UpdatePreviewBounds();
            PrepareSocketCards(previewRect, selected, snapshot);
            HandleRailClick(previewRect);
            if (!PointerOverSocketCard(previewRect))
            {
                HandlePreviewNavigation(previewRect);
                UpdatePreviewHover(previewRect, snapshot);
            }
            selected = FindSelected(snapshot);
            EnsureSelectedSocket(selected);
            DrawPartNavigator(treeRect, selected);
            selected = FindSelected(snapshot);
            EnsureSelectedSocket(selected);
            UpdatePreviewBounds();
            PrepareSocketCards(previewRect, selected, snapshot);
            DrawAssemblyPreview(previewRect, selected);
            DrawWeaponStatsPanel(detailsRect, snapshot);
            selected = FindSelected(snapshot);
            EnsureSelectedSocket(selected);
            DrawCatalog(catalogRect, selected);

            Rect footer = new Rect(0f, inRect.height - footerHeight,
                inRect.width, footerHeight);
            Widgets.Label(new Rect(4f, footer.y + 9f, footer.width - 150f, 24f),
                (workshop == null
                    ? "HD_ModularWeapon_ImmediateHintDev"
                    : "HD_ModularWeapon_WorkshopHint").Translate());
            if (Widgets.ButtonText(new Rect(footer.xMax - 120f, footer.y + 4f,
                120f, 30f), "CloseButton".Translate()))
                Close();

            if (hoveredPart != null) DrawHoveredPartPreview(inRect, hoveredPart);
        }

        private void DrawAssemblyPreview(
            Rect rect,
            ModularRenderNode selected)
        {
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.055f, 0.06f, 0.07f, 1f),
                new Color(0.35f, 0.38f, 0.42f), 2);
            Rect inner = PreviewArea(rect);
            Rect cameraArea = PreviewCameraArea(rect);
            Vector2 origin = previewCamera.OriginFor(cameraArea);
            float pixelsPerCell = previewCamera.ScaleFor(cameraArea);
            ModularRenderNode front = SelectedPreviewPart(selected);
            ModularRenderNode focus = IsPreviewPart(hoveredNode) ? hoveredNode : front;
            GUI.BeginGroup(inner);
            Vector2 localOrigin = origin - inner.position;
            DrawPreviewAssembly(localOrigin, pixelsPerCell, front);

            GUI.EndGroup();
            DrawPreviewHighlight(rect, focus, origin, pixelsPerCell);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 43f, rect.width - 150f, 25f),
                "HD_ModularWeapon_SocketOverlayTitle".Translate(selected?.thing.LabelCap.ToString() ?? string.Empty));
            Text.Font = GameFont.Small;
            DrawSocketOverlay(rect);
            DrawPreviewToolbar(rect);
        }

        private static void DrawAmmunitionBadge(
            Rect rect,
            List<ModularRenderNode> snapshot)
        {
            ModularRenderNode ammunition = snapshot.FirstOrDefault(node =>
                !node.Props.ammunitionType.NullOrEmpty());
            string label = ammunition != null
                ? ammunition.Props.ammunitionType
                : "HD_ModularWeapon_NoAmmunition".Translate().ToString();
            ModularAmmunitionCompatibilityResult compatibility =
                ModularWeaponAmmunitionUtility.Resolve(snapshot);
            string status;
            Color border;
            switch (compatibility.kind)
            {
                case ModularAmmunitionCompatibilityKind.Compatible:
                    status = "HD_ModularWeapon_AmmoCompatible".Translate(
                        compatibility.ChamberLabel);
                    border = new Color(0.45f, 0.67f, 0.35f);
                    break;
                case ModularAmmunitionCompatibilityKind.FailureToChamber:
                    status = "HD_ModularWeapon_AmmoFailureToChamber".Translate(
                        compatibility.ChamberLabel);
                    border = new Color(1f, 0.62f, 0.12f);
                    break;
                case ModularAmmunitionCompatibilityKind.Catastrophic:
                    status = "HD_ModularWeapon_AmmoCatastrophic".Translate(
                        compatibility.ChamberLabel);
                    border = new Color(1f, 0.16f, 0.12f);
                    break;
                default:
                    status = "HD_ModularWeapon_AmmoUnspecified".Translate();
                    border = new Color(0.50f, 0.32f, 0.28f);
                    break;
            }
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.07f, 0.08f, 0.085f, 0.93f),
                border,
                compatibility.kind == ModularAmmunitionCompatibilityKind.Catastrophic
                    ? 2
                    : 1);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(7f),
                "HD_ModularWeapon_SelectedAmmunition".Translate(label)
                + "\n" + status);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawGasTubeControl(Rect rect)
        {
            bool available = root.HasAdjustableGasSystem;
            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(0.085f, 0.105f, 0.10f),
                available
                    ? new Color(0.34f, 0.48f, 0.38f)
                    : new Color(0.36f, 0.36f, 0.36f),
                1);

            float setting = root.GasTubeFlowSetting;
            Widgets.Label(
                new Rect(rect.x + 8f, rect.y + 5f, rect.width - 75f, 22f),
                "HD_ModularWeapon_GasTubeSetting".Translate(
                    setting.ToString("P0", CultureInfo.InvariantCulture)));
            Rect resetRect = new Rect(rect.xMax - 60f, rect.y + 3f, 53f, 23f);
            if (available && Widgets.ButtonText(
                resetRect,
                "HD_ModularWeapon_GasTubeReset".Translate()))
                root.SetGasTubeFlowSetting(ModularWeaponGasSystemUtility.DefaultSetting);

            if (available)
            {
                float edited = Widgets.HorizontalSlider(
                    new Rect(rect.x + 9f, rect.y + 30f, rect.width - 18f, 18f),
                    setting,
                    ModularWeaponGasSystemUtility.MinimumSetting,
                    ModularWeaponGasSystemUtility.MaximumSetting,
                    true);
                edited = Mathf.Round(edited * 100f) / 100f;
                if (!Mathf.Approximately(edited, setting))
                    root.SetGasTubeFlowSetting(edited);

                ModularWeaponConvertedStats converted = root.ConvertedStats;
                ModularWeaponMuzzleSignature muzzle = root.MuzzleSignature;
                Text.Font = GameFont.Tiny;
                Widgets.Label(
                    new Rect(rect.x + 8f, rect.y + 55f, rect.width - 16f, 20f),
                    "HD_ModularWeapon_GasTubeEffects".Translate(
                        root.EffectiveRoundsPerMinute.ToString(
                            "0.#", CultureInfo.InvariantCulture),
                        converted.GasRecoilMultiplier.ToString(
                            "0.###", CultureInfo.InvariantCulture),
                        muzzle.Coefficient.ToString(
                            "0.###", CultureInfo.InvariantCulture)));
                Text.Font = GameFont.Small;
            }
            else
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.65f, 0.65f, 0.65f);
                Widgets.Label(
                    new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, 38f),
                    "HD_ModularWeapon_GasTubeUnavailable".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            TooltipHandler.TipRegion(
                rect,
                "HD_ModularWeapon_GasTubeDesc".Translate());
        }

        private void DrawCatalog(Rect rect, ModularRenderNode node)
        {
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.075f, 0.085f, 0.08f),
                new Color(0.32f, 0.37f, 0.34f), 1);
            ModularAttachmentSocket socket = node?.Props.SocketNamed(selectedSocketId);
            if (ShouldHideCEAmmunitionSocket(socket)) socket = null;
            string title = socket == null
                ? "HD_ModularWeapon_SelectSocket".Translate()
                : "HD_ModularWeapon_Catalog".Translate(socket.Label);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f,
                rect.width - 20f, 25f), title);
            if (socket == null || node == null) return;

            List<ThingDef> candidates = CompatibleParts(socket);
            const float tileWidth = 148f;
            const float tileHeight = 122f;
            const float gap = 8f;
            const float buttonWidth = 132f;
            const float buttonHeight = 24f;
            string[] groupOrder =
            {
                "Discouraged", "Core", "Barrel", "Furniture", "Magazine",
                "Sights", "Electronics", "Other"
            };
            Dictionary<string, List<ThingDef>> groups = candidates
                .GroupBy(def => CatalogGroup(def, socket))
                .ToDictionary(group => group.Key,
                    group => group.OrderBy(def => def.label).ToList());
            List<string> availableGroups = groupOrder
                .Where(key => groups.ContainsKey(key)).ToList();
            if (availableGroups.Count == 0) return;
            if (!availableGroups.Contains(selectedCatalogGroup))
                selectedCatalogGroup = availableGroups
                    .FirstOrDefault(key => key != "Discouraged")
                    ?? availableGroups[0];

            int buttonsPerRow = Mathf.Max(1,
                Mathf.FloorToInt((rect.width - 12f) / (buttonWidth + 6f)));
            int buttonRows = Mathf.CeilToInt(
                availableGroups.Count / (float)buttonsPerRow);
            for (int i = 0; i < availableGroups.Count; i++)
            {
                string key = availableGroups[i];
                Rect button = new Rect(rect.x + 7f
                        + (i % buttonsPerRow) * (buttonWidth + 6f),
                    rect.y + 34f + (i / buttonsPerRow) * (buttonHeight + 4f),
                    buttonWidth, buttonHeight);
                GUI.color = key == "Discouraged"
                    ? new Color(1f, 0.72f, 0.42f)
                    : key == selectedCatalogGroup
                        ? new Color(0.68f, 0.92f, 0.76f) : Color.white;
                if (Widgets.ButtonText(button,
                    ("HD_ModularWeapon_CatalogGroup_" + key).Translate()))
                {
                    selectedCatalogGroup = key;
                    catalogScroll = Vector2.zero;
                }
                GUI.color = Color.white;
            }

            Rect outRect = new Rect(rect.x + 6f,
                rect.y + 34f + buttonRows * (buttonHeight + 4f) + 4f,
                rect.width - 12f,
                rect.height - 40f - buttonRows * (buttonHeight + 4f) - 4f);
            List<ThingDef> entries = groups[selectedCatalogGroup];
            int columns = Mathf.Max(1,
                Mathf.FloorToInt((outRect.width - 18f) / (tileWidth + gap)));
            float contentHeight = 12f - gap
                + Mathf.CeilToInt(entries.Count / (float)columns)
                    * (tileHeight + gap);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f,
                Mathf.Max(outRect.height, contentHeight));
            Widgets.BeginScrollView(outRect, ref catalogScroll, viewRect);
            for (int i = 0; i < entries.Count; i++)
            {
                    ThingDef def = entries[i];
                    CompProperties_ModularWeaponNode props =
                        def.GetCompProperties<CompProperties_ModularWeaponNode>();
                    int available = workshop?.AvailableCount(def) ?? int.MaxValue;
                    bool unavailable = available <= 0;
                    ResearchProjectDef missingResearch = workshop == null
                        ? null
                        : ModularWeaponWorkshopSession.MissingResearch(def);
                    Rect tile = new Rect(6f + (i % columns) * (tileWidth + gap),
                        6f + (i / columns) * (tileHeight + gap),
                        tileWidth, tileHeight);
                    bool hovered = Mouse.IsOver(tile);
                    Widgets.DrawBoxSolidWithOutline(tile,
                        hovered ? new Color(0.18f, 0.22f, 0.18f)
                            : new Color(0.105f, 0.12f, 0.11f),
                        hovered ? Color.yellow : new Color(0.36f, 0.42f, 0.37f),
                        hovered ? 2 : 1);
                    Text.Font = GameFont.Tiny;
                    GUI.color = unavailable ? Color.red : new Color(0.65f, 0.9f, 0.65f);
                    string status = workshop == null
                        ? "HD_ModularWeapon_InstantCraft".Translate()
                        : missingResearch != null
                            ? "HD_ModularWeapon_ResearchRequired".Translate(
                                missingResearch.LabelCap)
                        : available > 0 && available != int.MaxValue
                            ? "HD_ModularWeapon_PartAvailable".Translate(available)
                        : workshop.CanCraftNow(def)
                            ? "HD_ModularWeapon_InstantCraft".Translate()
                        : ModularWeaponWorkshopSession.CanInstantCraft(def)
                            ? "HD_ModularWeapon_MaterialUnavailable".Translate()
                            : "HD_ModularWeapon_MustObtain".Translate();
                    Widgets.Label(new Rect(tile.x + 6f, tile.y + 4f,
                        tile.width - 12f, 18f), status);
                    GUI.color = Color.white;
                    ModularWeaponPartImage.For(def).Draw(new Rect(tile.x + 6f,
                        tile.y + 22f, tile.width - 12f, 61f));
                    Text.Anchor = TextAnchor.UpperCenter;
                    Widgets.Label(new Rect(tile.x + 5f, tile.y + 85f,
                        tile.width - 10f, 34f), def.LabelCap);
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                    if (hovered) hoveredPart = def;
                    string discouragedReason = DiscouragedReason(def, socket);
                    if (discouragedReason != null)
                        TooltipHandler.TipRegion(tile, discouragedReason.Translate());
                    else if (socket.railSurface == ModularRailSurface.Side
                        && props.performanceRole
                            == ModularWeaponPerformanceRole.Foregrip)
                        TooltipHandler.TipRegion(tile,
                            "HD_ModularWeapon_SideGripEfficiency".Translate());
                    else if (props.performanceRole
                        == ModularWeaponPerformanceRole.RailPanel)
                        TooltipHandler.TipRegion(tile,
                            "HD_ModularWeapon_RailPanelEfficiency".Translate());
                    if (!unavailable && Widgets.ButtonInvisible(tile))
                        InstallPart(node, socket, def, CompatibleMount(props, socket));
            }
            Widgets.EndScrollView();
        }

        private static string CatalogGroup(ThingDef def, ModularAttachmentSocket socket)
        {
            if (DiscouragedReason(def, socket) != null) return "Discouraged";
            string[] name = def.defName.Split('_');
            string kind = name.Length > 2 ? name[2] : string.Empty;
            switch (kind)
            {
                case "Receiver": case "UpperReceiver": case "Slide":
                case "Bolt": case "OperatingRod": case "CockingTube":
                case "Trigger": case "TriggerGroup": case "TriggerHousing":
                case "Hammer": case "Safety": case "GasBlock":
                case "GasTube": case "BufferTube": return "Core";
                case "Barrel": case "Muzzle": case "Suppressor":
                    return "Barrel";
                case "Handguard": case "HandguardExt": case "Stock":
                case "StockExt": case "Grip": case "PistolGrip":
                case "RailPanel": return "Furniture";
                case "Magazine": case "MagazineExt": case "MagazineWrap":
                case "Ammunition": return "Magazine";
                case "FrontSight": case "RearSight": case "IronSight":
                case "Optic": case "Scope": case "Magnifier":
                case "OpticRail": case "OpticMount": case "Mount":
                    return "Sights";
                case "Laser": case "Light": return "Electronics";
                default: return "Other";
            }
        }

        private static string DiscouragedReason(
            ThingDef def, ModularAttachmentSocket socket)
        {
            if (socket?.isRail != true) return null;
            CompProperties_ModularWeaponNode props =
                def.GetCompProperties<CompProperties_ModularWeaponNode>();
            ModularAttachmentMount mount = CompatibleMount(props, socket);
            if (mount?.UsesOppositeSurface(socket) != true) return null;

            string[] name = def.defName.Split('_');
            string kind = name.Length > 2 ? name[2] : string.Empty;
            if (socket.railSurface == ModularRailSurface.Top
                && props.performanceRole == ModularWeaponPerformanceRole.Foregrip)
                return "HD_ModularWeapon_DiscouragedTopGrip";
            if (socket.railSurface == ModularRailSurface.Bottom
                && (props.sight != null || kind == "Optic"
                    || kind == "Scope" || kind == "Magnifier"
                    || kind == "FrontSight" || kind == "RearSight"
                    || kind == "IronSight" || kind == "OpticMount"
                    || kind == "OpticRail"))
                return "HD_ModularWeapon_DiscouragedBottomSight";
            return null;
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
            if (socket.isRail && limit > 0 && node.comp.CountOnSocket(socket.id) >= limit)
            {
                Messages.Message("HD_ModularWeapon_RejectSocketFull".Translate(socket.Label),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
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

            Thing child;
            ModularWeaponWorkshopSession.PartAcquisition acquisition = null;
            if (workshop != null)
            {
                if (!workshop.TryAcquirePart(def, out acquisition,
                    out string acquisitionRejection))
                {
                    RestoreDisplaced(node.comp, displaced);
                    Messages.Message(
                        acquisitionRejection
                            ?? "HD_ModularWeapon_PartUnavailable".Translate(def.LabelCap),
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }
                child = acquisition.Part;
            }
            else
            {
                child = ThingMaker.MakeThing(def);
            }
            string rejection;
            bool success = TryInstallAtPreviewSocket(node, socket, child, mount, out rejection);
            if (!success)
            {
                if (acquisition != null) acquisition.Rollback();
                else DestroyAssembly(child);
                RestoreDisplaced(node.comp, displaced);
                Messages.Message(rejection ?? "HD_ModularWeapon_AttachFailed".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            acquisition?.Commit();
            for (int i = 0; i < displaced.Count; i++)
                ReturnOrDestroyAssembly(displaced[i].thing);
            selectedAttachmentId = child.thingIDNumber;
            revealSocketAttachment = child.thingIDNumber;
            preferredRailOffset = null;
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

        private void ReturnOrDestroyAssembly(Thing thing)
        {
            if (workshop != null) workshop.ReturnAssembly(thing);
            else DestroyAssembly(thing);
        }

        private void DrawHoveredPartPreview(Rect windowRect, ThingDef def)
        {
            ModularWeaponPartImage data = ModularWeaponPartImage.For(def);
            Vector2 mouse = Event.current.mousePosition;
            float width = Mathf.Min(360f, windowRect.width - 8f);
            float height = Mathf.Min(480f, windowRect.height - 8f);
            float x = Mathf.Min(windowRect.xMax - width - 4f, mouse.x + 20f);
            float y = Mathf.Clamp(mouse.y - height * 0.5f,
                windowRect.y + 4f, windowRect.yMax - height - 4f);
            Rect panel = new Rect(x, y, width, height);
            Widgets.DrawBoxSolidWithOutline(panel,
                new Color(0.035f, 0.04f, 0.045f, 0.98f), Color.yellow, 2);
            Widgets.Label(new Rect(panel.x + 10f, panel.y + 8f,
                panel.width - 20f, 26f), def.LabelCap);

            Rect imageArea = new Rect(panel.x + 12f, panel.y + 38f,
                panel.width - 24f, Mathf.Min(250f, height * 0.52f));
            Widgets.DrawBoxSolid(imageArea, new Color(0.16f, 0.18f, 0.20f));
            data.Draw(imageArea.ContractedBy(4f));

            CompProperties_ModularWeaponNode props =
                def.GetCompProperties<CompProperties_ModularWeaponNode>();
            ModularAttachmentSocket selectedSocket = FindSelected(root.RenderSnapshot())
                ?.Props.SocketNamed(selectedSocketId);
            ModularAttachmentMount mount = CompatibleMount(props, selectedSocket);
            string details = def.defName;
            float mass = props?.internalStats != null
                && props.internalStats.massKg > 0f
                ? props.internalStats.massKg
                : def.GetStatValueAbstract(StatDefOf.Mass);
            details += "\n" + StatDefOf.Mass.LabelCap + ": "
                + mass.ToString("0.##", CultureInfo.InvariantCulture) + " kg";
            if (props?.craftCost != null && props.craftCost.Count > 0)
                details += "\n" + "HD_ModularWeapon_CraftCost".Translate(
                    string.Join(", ", props.craftCost
                        .Where(entry => entry?.thingDef != null)
                        .Select(entry => entry.thingDef.LabelCap + " ×" + entry.count)));
            if (mount != null && selectedSocket?.isRail == true)
            {
                details += "\n" + "HD_ModularWeapon_RailContactLength".Translate(
                    mount.EffectiveRailContactLength.ToString(
                        "0.###", CultureInfo.InvariantCulture));
                details += "\n" + "HD_ModularWeapon_PhysicalLength".Translate(
                    mount.EffectivePhysicalLength.ToString(
                        "0.###", CultureInfo.InvariantCulture));
                if (!Mathf.Approximately(mount.physicalCenterOffset, 0f))
                    details += "\n" + "HD_ModularWeapon_BodyOffset".Translate(
                        mount.physicalCenterOffset.ToString("+0.###;-0.###;0",
                            CultureInfo.InvariantCulture));
            }
            if (mount != null)
            {
                details += "\n" + "HD_ModularWeapon_SurfaceDetail".Translate(
                    SurfaceText(mount.railSurface));
                if (mount.UsesOppositeSurface(selectedSocket))
                    details += "  ·  " + "HD_ModularWeapon_OppositeMounted".Translate();
            }
            if (props != null && !Mathf.Approximately(props.fireDelayMultiplier, 1f))
                details += "\n" + "HD_ModularWeapon_FireDelayModifier".Translate(
                    props.fireDelayMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            if (props?.verticalOccupancy > 0f)
                details += "\n" + "HD_ModularWeapon_VerticalOccupancy".Translate(
                    props.verticalOccupancy.ToString("0.###", CultureInfo.InvariantCulture));
            if (props?.sight != null)
                details += "\n" + "HD_ModularWeapon_SightPartDetail".Translate(
                    props.sight.kind.ToString(),
                    props.sight.aimRadius.ToString("0.###", CultureInfo.InvariantCulture));
            if (props != null && !props.ammunitionType.NullOrEmpty())
                details += "\n" + "HD_ModularWeapon_AmmunitionPartDetail".Translate(
                    props.ammunitionType);
            if (props != null && !props.chamberCaliber.NullOrEmpty())
                details += "\n" + "HD_ModularWeapon_ChamberPartDetail".Translate(
                    ModularWeaponAmmunitionUtility.DisplayCaliber(props.chamberCaliber));
            if (props != null && !props.statOffsets.NullOrEmpty())
            {
                for (int i = 0; i < props.statOffsets.Count; i++)
                {
                    StatModifier modifier = props.statOffsets[i];
                    if (modifier?.stat == null) continue;
                    details += "\n" + modifier.stat.LabelCap + " "
                        + modifier.value.ToStringWithSign();
                }
            }
            if (props != null && !props.statFactors.NullOrEmpty())
            {
                for (int i = 0; i < props.statFactors.Count; i++)
                {
                    StatModifier modifier = props.statFactors[i];
                    if (modifier?.stat == null) continue;
                    details += "\n" + modifier.stat.LabelCap + " ×"
                        + modifier.value.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(panel.x + 10f, imageArea.yMax + 8f,
                panel.width - 20f, panel.yMax - imageArea.yMax - 16f), details);
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
            ModularRenderNode fallback = snapshot.FirstOrDefault(node => node.depth == 0);
            selectedPath = fallback?.path;
            selectedSocketId = null;
            selectedAttachmentId = -1;
            revealNavigationSelection = true;
            return fallback;
        }

        private void EnsureSelectedSocket(ModularRenderNode node)
        {
            ModularAttachmentSocket current = node?.Props.SocketNamed(selectedSocketId);
            if (current != null && !ShouldHideCEAmmunitionSocket(current)) return;
            selectedSocketId = null;
            selectedAttachmentId = -1;
            preferredRailOffset = null;
            selectedCatalogGroup = null;
            catalogScroll = Vector2.zero;
        }

        private bool SynchronizeCEAmmunitionToChamber(
            List<ModularRenderNode> snapshot)
        {
            if (!CombatExtendedActive || snapshot.NullOrEmpty()) return false;

            ModularRenderNode chamber = snapshot.FirstOrDefault(node =>
                !node.Props.chamberCaliber.NullOrEmpty()
                && !node.Props.ceAmmoSetDefName.NullOrEmpty());
            if (chamber == null) return false;

            ModularRenderNode currentAmmo = snapshot.FirstOrDefault(node =>
                !node.Props.ammunitionType.NullOrEmpty());
            if (currentAmmo != null
                && string.Equals(
                    currentAmmo.Props.ammunitionCaliber,
                    chamber.Props.chamberCaliber,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            ThingDef desired = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def =>
                {
                    CompProperties_ModularWeaponNode props = def
                        .GetCompProperties<CompProperties_ModularWeaponNode>();
                    return props != null
                        && !props.ceAmmoDefName.NullOrEmpty()
                        && string.Equals(
                            props.ammunitionCaliber,
                            chamber.Props.chamberCaliber,
                            StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(def => def.defName)
                .FirstOrDefault();
            if (desired == null) return false;

            CompModularWeaponNode magazine = currentAmmo?.parentComp;
            ModularAttachmentSocket socket = magazine?.Props
                .SocketNamed(currentAmmo?.parentSocketId);
            if (magazine == null || socket == null)
            {
                ModularRenderNode magazineNode = snapshot.FirstOrDefault(node =>
                    node.Props.sockets.Any(ShouldHideCEAmmunitionSocket));
                magazine = magazineNode?.comp;
                socket = magazineNode?.Props.sockets
                    .FirstOrDefault(ShouldHideCEAmmunitionSocket);
            }
            if (magazine == null || socket == null) return false;

            int oldIndex = magazine.IndexOnSocket(socket.id);
            Thing oldPart = oldIndex >= 0 ? magazine.ChildAt(oldIndex) : null;
            string oldMount = oldIndex >= 0 ? magazine.MountIdAt(oldIndex) : null;
            float oldOffset = oldIndex >= 0 ? magazine.RailOffsetAt(oldIndex) : 0f;
            if (oldIndex >= 0) magazine.DetachChildAt(oldIndex);

            Thing replacement = ThingMaker.MakeThing(desired);
            CompProperties_ModularWeaponNode replacementProps = replacement
                .TryGetComp<CompModularWeaponNode>()?.Props;
            ModularAttachmentMount mount = CompatibleMount(replacementProps, socket);
            string rejection;
            float offset;
            bool attached = mount != null
                && magazine.TryFindAttachOffset(
                    replacement,
                    socket.id,
                    mount.id,
                    out offset,
                    out rejection)
                && magazine.TryAttach(
                    replacement,
                    socket.id,
                    mount.id,
                    offset,
                    out rejection);
            if (!attached)
            {
                DestroyAssembly(replacement);
                if (oldPart != null)
                {
                    string ignored;
                    magazine.TryAttach(
                        oldPart,
                        socket.id,
                        oldMount,
                        oldOffset,
                        out ignored);
                }
                return false;
            }

            DestroyAssembly(oldPart);
            selectedAttachmentId = -1;
            return true;
        }

        private static bool CombatExtendedActive =>
            ModsConfig.IsActive("ceteam.combatextended");

        private static bool ShouldHideCEAmmunitionPart(ModularRenderNode node)
        {
            return CombatExtendedActive
                && node != null
                && !node.Props.ammunitionType.NullOrEmpty();
        }

        private static bool ShouldHideCEAmmunitionSocket(
            ModularAttachmentSocket socket)
        {
            if (!CombatExtendedActive || socket == null) return false;
            if (string.Equals(
                socket.id,
                "ammunition",
                StringComparison.OrdinalIgnoreCase))
                return true;
            return socket.tags?.Any(tag => tag?.IndexOf(
                "ammunition",
                StringComparison.OrdinalIgnoreCase) >= 0) == true;
        }

        private static int FirstChildIndexOnSocket(
            CompModularWeaponNode comp,
            string socketId)
        {
            for (int i = 0; i < comp.ChildCount; i++)
                if (comp.SocketIdAt(i) == socketId) return i;
            return -1;
        }

        private static string SurfaceText(ModularRailSurface surface)
        {
            switch (surface)
            {
                case ModularRailSurface.Top:
                    return "HD_ModularWeapon_SurfaceTop".Translate();
                case ModularRailSurface.Bottom:
                    return "HD_ModularWeapon_SurfaceBottom".Translate();
                case ModularRailSurface.Side:
                    return "HD_ModularWeapon_SurfaceSide".Translate();
                default:
                    return "HD_ModularWeapon_SurfaceUnspecified".Translate();
            }
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

        private static void DrawTexture(Rect rect, Texture texture, bool flipVertical)
        {
            Rect uv = flipVertical
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
        }

        private sealed class DetachedPart
        {
            public Thing thing;
            public string socketId;
            public string mountId;
            public float railOffset;
        }

    }
}
