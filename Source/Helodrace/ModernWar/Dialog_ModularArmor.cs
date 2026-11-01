using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Rect = Waylith.Math.Rect;
using Vector2 = System.Numerics.Vector2;
using Color = Waylith.Math.Color;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public sealed partial class Dialog_ModularArmor : Window
    {
        private readonly CompModularArmor comp;
        private readonly CompModularArmor original;
        private readonly string baseline;
        private readonly CompClothingWorkbench bench;
        private ModularArmorPartDef selectedPalsPart;
        private InstalledModularArmorPart selectedInstalledPart;
        private Rot4 previewFacing;
        private readonly ModularArmorPreviewBounds previewBounds = new ModularArmorPreviewBounds();
        private Vector2 slotsScroll, catalogScroll;

        public Dialog_ModularArmor(CompModularArmor comp, CompClothingWorkbench bench)
        {
            original = comp;
            baseline = comp.ConfigurationSignature();
            this.comp = comp.CreateEditorDraft();
            this.bench = bench;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            previewFacing = comp.Wearer != null
                ? comp.Wearer.Rotation
                : Rot4.South;
        }

        public override UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(1280f, 820f);

        public override void DoWindowContents(UnityEngine.Rect gameRect)
        {
            Rect inRect = gameRect.ToNative();
            if (bench?.CanCustomize(original) != true)
            {
                Close();
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 205f, 32f).ToGame(),
                "HD_ModularArmor_WindowTitle".Translate(comp.parent.LabelCap));
            Text.Font = GameFont.Small;
            Rect exportRect = new Rect(inRect.width - 190f, 0f, 170f, 30f);
            if (Widgets.ButtonText(exportRect.ToGame(),
                "HD_ModularArmor_ExportPreset".Translate()))
            {
                GUIUtility.systemCopyBuffer = ModularPresetXmlExporter.ExportArmor(comp);
                Messages.Message(
                    "HD_ModularArmor_ExportPresetCopied".Translate(),
                    MessageTypeDefOf.PositiveEvent,
                    false);
            }
            TooltipHandler.TipRegion(
                exportRect.ToGame(),
                "HD_ModularArmor_ExportPresetDesc".Translate());

            Widgets.Label(new Rect(10f, 44f, inRect.width - 390f, 30f).ToGame(),
                "HD_ClothingBench_DraftHint".Translate());

            if (comp.InstalledParts.Any(record => record?.CanSwapFrontRearPlates == true)
                && Widgets.ButtonText(new Rect(inRect.width - 360f, 44f, 360f, 30f).ToGame(),
                    "HD_ModularArmor_SwapFrontRear".Translate()))
            {
                comp.CompleteFrontRearSwap();
            }

            bool hasPals = !comp.Props.palsPanels.NullOrEmpty();
            const float bodyTop = 82f;
            const float footerHeight = 66f;
            float catalogHeight = hasPals ? 170f : 0f;
            float bodyHeight = inRect.height - bodyTop - footerHeight - catalogHeight
                - (hasPals ? 8f : 0f);
            float slotsWidth = Mathf.Clamp(inRect.width * 0.23f, 250f, 300f);
            float statsWidth = Mathf.Clamp(inRect.width * 0.23f, 270f, 310f);
            Rect slotsRect = new Rect(0f, bodyTop, slotsWidth, bodyHeight);
            Rect previewRect = new Rect(slotsRect.xMax + 12f, bodyTop,
                inRect.width - slotsWidth - statsWidth - 24f, bodyHeight);
            Rect statsRect = new Rect(inRect.width - statsWidth, bodyTop, statsWidth, bodyHeight);
            List<ModularArmorSlotDef> visibleSlots = VisibleFixedSlots();
            DrawArmorPreview(previewRect, visibleSlots);
            DrawFixedSlots(visibleSlots, slotsRect);
            DrawArmorStatsPanel(statsRect);
            if (hasPals)
            {
                DrawPalsCatalog(new Rect(0f, previewRect.yMax + 8f, inRect.width, catalogHeight));
            }

            string instruction = selectedInstalledPart != null
                ? "HD_ModularArmor_Pals_MoveHint".Translate(
                    selectedInstalledPart.part.LabelCap)
                : selectedPalsPart != null
                    ? "HD_ModularArmor_Pals_PlaceHint".Translate(
                        selectedPalsPart.LabelCap,
                        selectedPalsPart.palsWidth,
                        selectedPalsPart.palsHeight)
                    : hasPals
                        ? "HD_ModularArmor_Pals_Hint".Translate()
                        : "HD_ModularArmor_FixedOnlyHint".Translate();
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(10f, inRect.height - footerHeight + 8f,
                inRect.width - 310f, footerHeight - 12f).ToGame(),
                instruction);
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inRect.width - 280f, inRect.height - 34f, 150f, 32f).ToGame(),
                "HD_ClothingBench_Apply".Translate()))
            {
                if (original.TryStartCustomization(comp, baseline, bench.parent, out string rejection)) Close();
                else if (!rejection.NullOrEmpty()) Messages.Message(rejection, MessageTypeDefOf.RejectInput);
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 34f, 120f, 32f).ToGame(),
                "CancelButton".Translate()))
            {
                Close();
            }
        }

        private void DrawArmorPreview(
            Rect rect,
            List<ModularArmorSlotDef> visibleSlots)
        {
            Widgets.DrawBoxSolidWithOutline(
                rect.ToGame(), new Color(0.08f, 0.09f, 0.10f, 0.96f).ToGame(), new Color(0.40f, 0.45f, 0.42f).ToGame(),
                2);

            List<Texture2D> layers = PreviewTextures();
            if (!layers.NullOrEmpty())
            {
                DrawPreviewTextureLayers(
                    new Rect(rect.x + 38f, rect.y + 48f, rect.width - 76f, rect.height - 98f),
                    layers);
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(UiMath.Inset(rect, 20f).ToGame(), comp.parent.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            if (!comp.Props.palsPanels.NullOrEmpty())
            {
                for (int i = 0; i < comp.Props.palsPanels.Count; i++)
                {
                    ModularArmorPalsPanelDef panel = comp.Props.palsPanels[i];
                    if (PanelVisibleForFacing(panel))
                    {
                        DrawPalsPanel(rect, panel);
                    }
                }
            }

            DrawPreviewFacingControls(rect, visibleSlots);
        }

        private List<Texture2D> PreviewTextures()
        {
            List<Texture2D> textures = new List<Texture2D>();
            IReadOnlyList<InstalledModularArmorPart> installed = comp.InstalledParts;

            if (previewFacing == Rot4.North)
            {
                for (int i = 0; i < installed.Count; i++)
                {
                    GraphicData underGraphic = installed[i]?.part?.northUnderGraphicData;
                    if (underGraphic == null || underGraphic.texPath.NullOrEmpty())
                    {
                        continue;
                    }

                    Texture2D underTexture = ContentFinder<Texture2D>.Get(
                        underGraphic.texPath,
                        false);
                    if (underTexture != null)
                    {
                        textures.Add(underTexture);
                    }
                }
            }

            for (int i = 0; i < installed.Count; i++)
            {
                if (!PartVisibleInPreview(installed[i]))
                {
                    continue;
                }

                string sideBackPath = comp.AuthoredPalsTexturePath(
                    installed[i],
                    true,
                    previewFacing);
                Texture2D sideBackTexture = sideBackPath.NullOrEmpty()
                    ? null
                    : ContentFinder<Texture2D>.Get(sideBackPath, false);
                if (sideBackTexture != null)
                {
                    textures.Add(sideBackTexture);
                }
            }

            List<InstalledModularArmorPart> orderedInstalled = installed
                .Where(record => record != null)
                .OrderBy(comp.DrawLayerFor)
                .ToList();
            for (int i = 0; i < orderedInstalled.Count; i++)
            {
                InstalledModularArmorPart installedPart = orderedInstalled[i];
                if (installedPart.part?.drawBehindArmor != true
                    || !PartVisibleInPreview(installedPart))
                {
                    continue;
                }

                GraphicData graphicData = installedPart.part.graphicData;
                if (graphicData == null || graphicData.texPath.NullOrEmpty())
                {
                    continue;
                }

                Texture2D partTexture = DirectionalTexture(graphicData.texPath, false);
                if (partTexture != null)
                {
                    textures.Add(partTexture);
                }
            }

            Texture2D apparelTexture = ApparelPreviewTexture();
            if (apparelTexture != null)
            {
                textures.Add(apparelTexture);
            }

            for (int i = 0; i < orderedInstalled.Count; i++)
            {
                InstalledModularArmorPart installedPart = orderedInstalled[i];
                if (installedPart.part?.drawBehindArmor == true
                    || !PartVisibleInPreview(installedPart))
                {
                    continue;
                }

                string authoredPath = comp.AuthoredPalsTexturePath(
                    installedPart,
                    false,
                    previewFacing);
                Texture2D authoredTexture = authoredPath.NullOrEmpty()
                    ? null
                    : ContentFinder<Texture2D>.Get(authoredPath, false);
                if (authoredTexture != null)
                {
                    textures.Add(authoredTexture);
                    continue;
                }

                GraphicData graphicData = installedPart.part?.graphicData;
                if (graphicData == null || graphicData.texPath.NullOrEmpty())
                {
                    continue;
                }

                Texture2D partTexture = DirectionalTexture(graphicData.texPath, false);
                if (partTexture != null)
                {
                    textures.Add(partTexture);
                }
            }

            return textures;
        }

        private bool PartVisibleInPreview(InstalledModularArmorPart record)
        {
            if (record?.part == null)
            {
                return false;
            }

            return (record.part.previewDirections.NullOrEmpty()
                    || record.part.previewDirections.Contains(PreviewArmorFacing()))
                && comp.PalsPartVisibleForFacing(record, previewFacing);
        }

        private Texture2D ApparelPreviewTexture()
        {
            string wornPath = comp.parent.def.apparel?.wornGraphicPath;
            if (!wornPath.NullOrEmpty())
            {
                Texture2D texture = DirectionalTexture(wornPath, true);
                if (texture != null)
                {
                    return texture;
                }
            }

            string fallback = comp.parent.def.graphicData?.texPath;
            return fallback.NullOrEmpty()
                ? null
                : ContentFinder<Texture2D>.Get(fallback, false);
        }

        private Texture2D DirectionalTexture(string basePath, bool useBodyType)
        {
            string suffix = FacingTextureSuffix();
            if (useBodyType)
            {
                string bodyType = comp.Wearer?.story?.bodyType?.defName;
                if (!bodyType.NullOrEmpty())
                {
                    Texture2D bodyTexture = ContentFinder<Texture2D>.Get(
                        basePath + "_" + bodyType + "_" + suffix,
                        false);
                    if (bodyTexture != null)
                    {
                        return bodyTexture;
                    }
                }

                Texture2D femaleTexture = ContentFinder<Texture2D>.Get(
                    basePath + "_Female_" + suffix,
                    false);
                if (femaleTexture != null)
                {
                    return femaleTexture;
                }
            }

            if (!useBodyType)
            {
                Texture2D exactTexture = ContentFinder<Texture2D>.Get(
                    basePath,
                    false);
                if (exactTexture != null)
                {
                    return exactTexture;
                }
            }

            return ContentFinder<Texture2D>.Get(basePath + "_" + suffix, false);
        }

        private string FacingTextureSuffix()
        {
            if (previewFacing == Rot4.North) return "north";
            if (previewFacing == Rot4.South) return "south";
            return "east";
        }

        private void DrawPreviewTextureLayers(Rect availableRect, List<Texture2D> layers)
        {
            Rect uv = previewBounds.CombinedBounds(layers);
            Rect minimumCrop = comp.Props.previewCrop?.Rect.ToNative() ?? new Rect(0f, 0f, 1f, 1f);
            Rect destination = ModularArmorPreviewBounds.DestinationFor(availableRect, uv,
                layers[0].width, layers[0].height, minimumCrop);

            Rect drawUv = previewFacing == Rot4.West
                ? new Rect(uv.xMax, uv.y, -uv.width, uv.height)
                : uv;
            UnityEngine.Rect gameDestination = destination.ToGame();
            UnityEngine.Rect gameUv = drawUv.ToGame();
            for (int i = 0; i < layers.Count; i++)
            {
                GUI.DrawTextureWithTexCoords(gameDestination, layers[i], gameUv, true);
            }
        }

        private void DrawPreviewFacingControls(
            Rect previewRect,
            List<ModularArmorSlotDef> visibleSlots)
        {
            Rect leftButton = new Rect(previewRect.x + 12f, previewRect.y + 10f, 42f, 30f);
            Rect rightButton = new Rect(previewRect.xMax - 54f, previewRect.y + 10f, 42f, 30f);
            if (Widgets.ButtonText(leftButton.ToGame(), "◀"))
            {
                RotatePreview(1);
            }
            if (Widgets.ButtonText(rightButton.ToGame(), "▶"))
            {
                RotatePreview(-1);
            }

            Rect facingLabelRect = new Rect(
                leftButton.xMax + 6f,
                previewRect.y + 10f,
                rightButton.xMin - leftButton.xMax - 12f,
                30f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(
                facingLabelRect.ToGame(),
                "HD_ModularArmor_Facing".Translate(FacingLabel()));

            Text.Font = GameFont.Tiny;
            string slotSummary = visibleSlots.Count == 0
                ? "HD_ModularArmor_None".Translate().ToString()
                : string.Join(" · ", visibleSlots.Select(slot => slot.LabelCap.ToString()));
            Widgets.Label(
                new Rect(previewRect.x + 12f, previewRect.yMax - 38f,
                    previewRect.width - 24f, 30f).ToGame(),
                "HD_ModularArmor_VisibleSlots".Translate(slotSummary));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void RotatePreview(int direction)
        {
            int facing = (previewFacing.AsInt + direction) % 4;
            if (facing < 0)
            {
                facing += 4;
            }
            previewFacing = new Rot4(facing);
            slotsScroll = Vector2.Zero;
        }

        private TaggedString FacingLabel()
        {
            if (previewFacing == Rot4.North)
                return "HD_ModularArmor_Facing_Back".Translate();
            if (previewFacing == Rot4.East)
                return "HD_ModularArmor_Facing_Right".Translate();
            if (previewFacing == Rot4.West)
                return "HD_ModularArmor_Facing_Left".Translate();
            return "HD_ModularArmor_Facing_Front".Translate();
        }

        private void DrawFixedSlots(
            List<ModularArmorSlotDef> slots,
            Rect rect)
        {
            Widgets.DrawMenuSection(rect.ToGame());
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 26f).ToGame(),
                "HD_ModularArmor_FixedSlots".Translate());
            Rect outRect = new Rect(rect.x + 8f, rect.y + 40f, rect.width - 16f, rect.height - 48f);
            Rect view = new Rect(0f, 0f, outRect.width - 18f, slots.Count * 88f);
            UnityEngine.Vector2 gameScroll = slotsScroll.ToGame();
            Widgets.BeginScrollView(outRect.ToGame(), ref gameScroll, view.ToGame());
            slotsScroll = gameScroll.ToNative();
            for (int i = 0; i < slots.Count; i++)
            {
                ModularArmorSlotDef slot = slots[i];
                Rect socket = new Rect(0f, i * 88f, view.width, 80f);
                InstalledModularArmorPart installed = comp.InstalledIn(slot);
                bool hovered = Mouse.IsOver(socket.ToGame());
                Widgets.DrawBoxSolidWithOutline(
                    socket.ToGame(), (
                    hovered
                        ? new Color(0.20f, 0.24f, 0.22f)
                        : new Color(0.12f, 0.14f, 0.13f)).ToGame(), (
                    slot.required
                        ? new Color(0.72f, 0.62f, 0.28f)
                        : new Color(0.38f, 0.45f, 0.41f)).ToGame(),
                    2);

                Text.Anchor = TextAnchor.UpperCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(socket.x + 4f, socket.y + 3f, socket.width - 8f, 20f).ToGame(),
                    slot.LabelCap);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                ModularArmorFacing armorFacing = PreviewArmorFacing();
                Thing directionalPlate = installed?.PlateForFacing(armorFacing);
                Thing plateSet = installed?.PlateSet;
                ThingDef plateDef = installed?.part?.plateThingDef;
                float labelX = socket.x + 5f;
                float labelWidth = socket.width - 10f;
                if (plateDef != null)
                {
                    Texture plateIcon = directionalPlate?.def?.uiIcon ?? plateDef.uiIcon;
                    if (plateIcon != null)
                    {
                        Widgets.DrawTextureFitted(
                            new Rect(socket.x + 8f, socket.y + 27f, 38f, 38f).ToGame(),
                            plateIcon,
                            1f);
                        labelX += 44f;
                        labelWidth -= 44f;
                    }
                }

                string installedLabel = installed?.part?.LabelCap.ToString()
                    ?? "HD_ModularArmor_None".Translate().ToString();
                if (plateDef != null)
                {
                    int plateNumber = installed?.PlateNumberForFacing(armorFacing) ?? 1;
                    string durability;
                    if (plateSet == null)
                    {
                        durability = "HD_ModularArmor_PlateSetMissing".Translate();
                    }
                    else if (directionalPlate != null)
                    {
                        durability = "HD_ModularArmor_NumberedPlateDurability".Translate(
                            plateNumber,
                            installed.PlateHitPointsForFacing(armorFacing),
                            installed.PlateMaxHitPoints);
                    }
                    else
                    {
                        durability = "HD_ModularArmor_NumberedPlateDestroyed".Translate(
                            plateNumber);
                    }

                    installedLabel += "\n" + durability;
                    Text.Font = GameFont.Tiny;
                }
                Widgets.Label(
                    new Rect(labelX, socket.y + 22f, labelWidth, socket.height - 26f).ToGame(),
                    installedLabel);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (Widgets.ButtonInvisible(socket.ToGame()))
                {
                    OpenPartMenu(slot);
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawPalsPanel(Rect previewRect, ModularArmorPalsPanelDef panel)
        {
            Rect gridRect = new Rect(
                previewRect.x + previewRect.width * panel.uiX,
                previewRect.y + previewRect.height * panel.uiY,
                previewRect.width * panel.uiWidth,
                previewRect.height * panel.uiHeight);
            float cellWidth = gridRect.width / panel.columns;
            float cellHeight = gridRect.height / panel.rows;

            Widgets.DrawBoxSolid(gridRect.ToGame(), new Color(0.04f, 0.05f, 0.04f, 0.55f).ToGame());
            for (int x = 0; x <= panel.columns; x++)
            {
                float lineX = gridRect.x + x * cellWidth;
                Widgets.DrawLine(new Vector2(lineX, gridRect.y).ToGame(), new Vector2(lineX, gridRect.yMax).ToGame(), new Color(0.48f, 0.55f, 0.45f, 0.72f).ToGame(),
                    1f);
            }

            for (int y = 0; y <= panel.rows; y++)
            {
                float lineY = gridRect.y + y * cellHeight;
                Widgets.DrawLine(new Vector2(gridRect.x, lineY).ToGame(), new Vector2(gridRect.xMax, lineY).ToGame(), new Color(0.48f, 0.55f, 0.45f, 0.72f).ToGame(),
                    1f);
            }

            List<InstalledModularArmorPart> mounted = comp.PartsOnPanel(panel).ToList();
            for (int i = 0; i < mounted.Count; i++)
            {
                DrawMountedPalsPart(gridRect, cellWidth, cellHeight, mounted[i]);
            }

            DrawPlacementPreview(gridRect, cellWidth, cellHeight, panel);
            HandlePalsInput(gridRect, cellWidth, cellHeight, panel, mounted);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(gridRect.x, gridRect.y - 20f, gridRect.width, 20f).ToGame(), panel.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawMountedPalsPart(
            Rect gridRect,
            float cellWidth,
            float cellHeight,
            InstalledModularArmorPart record)
        {
            Rect partRect = GridPartRect(gridRect, cellWidth, cellHeight, record);
            bool selected = selectedInstalledPart == record;
            Widgets.DrawBoxSolidWithOutline(
                UiMath.Inset(partRect, 2f).ToGame(),
                record.part.uiColor, (
                selected ? Color.yellow : new Color(0.72f, 0.76f, 0.68f)).ToGame(),
                selected ? 3 : 1);
            DrawPartIconAndLabel(UiMath.Inset(partRect, 5f), record.part);
            string tooltip = "HD_ModularArmor_PalsInstalledTip".Translate(
                record.part.LabelCap,
                record.palsX + 1,
                record.palsY + 1);
            CompAmmoPouch pouch = record.InstalledItem?.TryGetComp<CompAmmoPouch>();
            if (pouch != null)
            {
                int capacity = AmmoPouchUtility.CapacityFor(original.Wearer?.equipment?.Primary);
                tooltip += "\n" + (capacity > 0
                    ? "HD_AmmoPouch_Rounds".Translate(pouch.RoundsFor(capacity), capacity).ToString()
                    : "HD_AmmoPouch_StoredPercent".Translate(
                        pouch.RemainingFraction.ToStringPercent()).ToString());
            }
            TooltipHandler.TipRegion(partRect.ToGame(), tooltip);
        }

        private void DrawPlacementPreview(
            Rect gridRect,
            float cellWidth,
            float cellHeight,
            ModularArmorPalsPanelDef panel)
        {
            ModularArmorPartDef part = selectedInstalledPart?.part ?? selectedPalsPart;
            if (part == null || !Mouse.IsOver(gridRect.ToGame()))
            {
                return;
            }

            Vector2 mousePosition = Event.current.mousePosition.ToNative();
            int x = Mathf.FloorToInt((mousePosition.X - gridRect.x) / cellWidth);
            int y = Mathf.FloorToInt((mousePosition.Y - gridRect.y) / cellHeight);
            Rect preview = new Rect(
                gridRect.x + x * cellWidth,
                gridRect.y + y * cellHeight,
                part.PalsWidthFor(panel) * cellWidth,
                part.palsHeight * cellHeight);
            bool valid = comp.CanPlacePalsPart(part, panel, x, y, selectedInstalledPart);
            Widgets.DrawBoxSolid(UiMath.Inset(preview, 2f).ToGame(), (valid
                ? new Color(0.25f, 0.75f, 0.35f, 0.35f)
                : new Color(0.85f, 0.20f, 0.20f, 0.40f)).ToGame());
        }

        private void HandlePalsInput(
            Rect gridRect,
            float cellWidth,
            float cellHeight,
            ModularArmorPalsPanelDef panel,
            List<InstalledModularArmorPart> mounted)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown) return;
            Vector2 mousePosition = current.mousePosition.ToNative();
            if (!gridRect.Contains(mousePosition))
            {
                return;
            }

            InstalledModularArmorPart clicked = mounted.LastOrDefault(record =>
                GridPartRect(gridRect, cellWidth, cellHeight, record)
                    .Contains(mousePosition));
            if (current.button == 1 && clicked != null)
            {
                comp.RemovePalsPart(clicked);
                if (selectedInstalledPart == clicked)
                {
                    selectedInstalledPart = null;
                }
                current.Use();
                return;
            }

            if (current.button != 0)
            {
                return;
            }

            if (clicked != null)
            {
                selectedInstalledPart = clicked;
                selectedPalsPart = null;
                current.Use();
                return;
            }

            int x = Mathf.FloorToInt((mousePosition.X - gridRect.x) / cellWidth);
            int y = Mathf.FloorToInt((mousePosition.Y - gridRect.y) / cellHeight);
            if (selectedInstalledPart != null)
            {
                if (comp.MovePalsPart(selectedInstalledPart, panel, x, y))
                {
                    selectedInstalledPart = null;
                }
            }
            else if (selectedPalsPart != null)
            {
                if (comp.InstallPalsPart(
                    selectedPalsPart,
                    panel,
                    x,
                    y) != null)
                {
                    selectedPalsPart = null;
                }
            }
            current.Use();
        }

        private void DrawPalsCatalog(Rect rect)
        {
            Widgets.DrawBoxSolidWithOutline(
                rect.ToGame(), new Color(0.08f, 0.09f, 0.085f).ToGame(), new Color(0.32f, 0.36f, 0.33f).ToGame(),
                1);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, 25f).ToGame(),
                "HD_ModularArmor_PalsCatalog".Translate());

            List<ModularArmorPartDef> parts = DefDatabase<ModularArmorPartDef>
                .AllDefsListForReading
                .Where(part => part.installMode == ModularArmorInstallMode.Positionable
                    && comp.IsPartCompatible(part))
                .OrderBy(part => part.label)
                .ToList();
            Rect outRect = new Rect(rect.x + 8f, rect.y + 36f,
                rect.width - 16f, rect.height - 44f);
            const float tileWidth = 165f, tileHeight = 116f, gap = 8f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((outRect.width - 18f + gap) / (tileWidth + gap)));
            int rows = Mathf.CeilToInt(parts.Count / (float)columns);
            Rect view = new Rect(0f, 0f, outRect.width - 18f, rows * (tileHeight + gap));
            UnityEngine.Vector2 gameScroll = catalogScroll.ToGame();
            Widgets.BeginScrollView(outRect.ToGame(), ref gameScroll, view.ToGame());
            catalogScroll = gameScroll.ToNative();
            for (int i = 0; i < parts.Count; i++)
            {
                ModularArmorPartDef part = parts[i];
                Rect tile = new Rect((i % columns) * (tileWidth + gap),
                    (i / columns) * (tileHeight + gap), tileWidth, tileHeight);
                bool selected = selectedPalsPart == part;
                int available = EditorStock(part.RequiredThingDef);
                bool hasItem = DebugSettings.godMode
                    || part.RequiredThingDef == null
                    || available > 0;
                Widgets.DrawBoxSolidWithOutline(
                    tile.ToGame(), (
                    selected ? new Color(0.22f, 0.28f, 0.23f)
                        : hasItem ? new Color(0.12f, 0.14f, 0.13f)
                        : new Color(0.08f, 0.08f, 0.08f)).ToGame(), (
                    selected ? Color.yellow : new Color(0.35f, 0.42f, 0.37f)).ToGame(),
                    selected ? 2 : 1);
                Rect content = UiMath.Inset(tile, 8f);
                content.height -= 22f;
                Text.Font = GameFont.Tiny;
                DrawPartIconAndLabel(content, part);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.LowerCenter;
                string footprint = part.palsWidth + " x " + part.palsHeight;
                if (part.RequiredThingDef != null)
                {
                    footprint += "  " + "HD_ModularArmor_PartStock".Translate(available);
                }
                Widgets.Label(UiMath.Inset(tile, 5f).ToGame(), footprint);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(tile.ToGame()))
                {
                    if (hasItem)
                    {
                        selectedPalsPart = selected ? null : part;
                        selectedInstalledPart = null;
                    }
                    else
                    {
                        Messages.Message(
                            "HD_ModularArmor_NoReachablePart".Translate(
                                part.RequiredThingDef.LabelCap),
                            MessageTypeDefOf.RejectInput);
                    }
                }
            }
            Widgets.EndScrollView();
        }

        private static void DrawPartIconAndLabel(Rect rect, ModularArmorPartDef part)
        {
            Texture icon = part.uiIconPath.NullOrEmpty()
                ? part.RequiredThingDef?.uiIcon
                : ContentFinder<Texture2D>.Get(part.uiIconPath, false);
            if (icon != null)
            {
                Rect iconRect = new Rect(rect.center.X - 20f, rect.y, 40f, 40f);
                Widgets.DrawTextureFitted(iconRect.ToGame(), icon, 1f);
                rect.yMin += 42f;
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ToGame(), part.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private List<ModularArmorSlotDef> OrderedSlots()
        {
            return comp.Props.slots.NullOrEmpty()
                ? new List<ModularArmorSlotDef>()
                : comp.Props.slots
                    .OrderBy(slot => slot.uiOrder)
                    .ThenBy(slot => slot.label)
                    .ToList();
        }

        private List<ModularArmorSlotDef> VisibleFixedSlots()
        {
            return OrderedSlots()
                .Where(SlotVisibleForFacing)
                .ToList();
        }

        private bool SlotVisibleForFacing(ModularArmorSlotDef slot)
        {
            InstalledModularArmorPart installed = comp.InstalledIn(slot);
            ModularArmorPositionDef position = installed?.EffectivePosition;
            if (position == null)
            {
                position = DefDatabase<ModularArmorPartDef>.AllDefsListForReading
                    .Where(part => part.slot == slot && comp.IsPartCompatible(part))
                    .Select(part => part.DefaultPosition)
                    .FirstOrDefault(candidate => candidate != null);
            }

            return PositionVisibleForFacing(position);
        }

        private bool PositionVisibleForFacing(ModularArmorPositionDef position)
        {
            if (position?.protectedDirections.NullOrEmpty() != false)
            {
                return true;
            }

            return position.protectedDirections.Contains(PreviewArmorFacing());
        }

        private bool PanelVisibleForFacing(ModularArmorPalsPanelDef panel)
        {
            if (!comp.IsPalsPanelAvailable(panel))
            {
                return false;
            }

            return panel.visibleDirections.NullOrEmpty()
                ? PositionVisibleForFacing(panel.armorPosition)
                : panel.visibleDirections.Contains(PreviewArmorFacing());
        }

        private ModularArmorFacing PreviewArmorFacing()
        {
            if (previewFacing == Rot4.North) return ModularArmorFacing.Back;
            if (previewFacing == Rot4.East) return ModularArmorFacing.Right;
            if (previewFacing == Rot4.West) return ModularArmorFacing.Left;
            return ModularArmorFacing.Front;
        }

        private static Rect GridPartRect(
            Rect gridRect,
            float cellWidth,
            float cellHeight,
            InstalledModularArmorPart record)
        {
            return new Rect(
                gridRect.x + record.palsX * cellWidth,
                gridRect.y + record.palsY * cellHeight,
                record.part.PalsWidthFor(record.palsPanel) * cellWidth,
                record.part.palsHeight * cellHeight);
        }

        private void OpenPartMenu(ModularArmorSlotDef slot)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (!slot.required)
            {
                options.Add(new FloatMenuOption(
                    "HD_ModularArmor_None".Translate(),
                    () => { if (bench.CanCustomize(comp)) comp.SetPart(slot, null); }));
            }

            List<ModularArmorPartDef> parts = DefDatabase<ModularArmorPartDef>
                .AllDefsListForReading
                .Where(part => part.playerSelectable
                    && part.slot == slot
                    && comp.IsPartCompatible(part))
                .OrderBy(part => part.label)
                .ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                ModularArmorPartDef part = parts[i];
                int needed = part.RequiredThingDef == null ? 0 : 1;
                int available = EditorStock(part.RequiredThingDef, slot);
                InstalledModularArmorPart conflict = comp.ConflictingInstalledPart(
                    part,
                    comp.InstalledIn(slot));
                string label = part.LabelCap;
                if (part.RequiredThingDef != null)
                {
                    label += " " + "HD_ModularArmor_PartStock".Translate(available);
                }

                if (conflict != null)
                {
                    options.Add(new FloatMenuOption(
                        label + ": " + "HD_ModularArmor_PartConflict".Translate(
                            conflict.part.LabelCap),
                        null));
                }
                else
                {
                    options.Add(DebugSettings.godMode || available >= needed
                    ? new FloatMenuOption(
                        label,
                        delegate
                        {
                            if (!bench.CanCustomize(comp)) return;
                            if (!comp.SetPart(
                                slot,
                                part,
                                part.DefaultPosition))
                            {
                                Messages.Message(
                                    "HD_ModularArmor_InvalidInstall".Translate(),
                                    MessageTypeDefOf.RejectInput);
                            }
                        })
                    : new FloatMenuOption(
                        label + ": " + "HD_ModularArmor_PartRequired".Translate(),
                        null));
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private int EditorStock(ThingDef itemDef, ModularArmorSlotDef replacing = null)
        {
            if (itemDef == null) return 0;
            return Math.Max(0, original.AvailableRequiredItemCount(itemDef)
                + original.InstalledParts.Count(record => record.InstalledItem?.def == itemDef)
                - comp.InstalledParts.Count(record => record.part.RequiredThingDef == itemDef
                    && (replacing == null || record.slot != replacing)));
        }

    }
}
