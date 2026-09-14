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
        private readonly ModularWeaponWorkshopSession workshop;
        private string selectedPath = "root";
        private string selectedSocketId;
        private int selectedAttachmentId = -1;
        private Vector2 navigationScroll;
        private Vector2 socketScroll;
        private Vector2 catalogScroll;
        private ThingDef hoveredPart;
        private bool performanceExpanded;

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
            if (root?.parent == null || root.parent.Destroyed)
            {
                Widgets.Label(inRect, "HD_ModularWeapon_Missing".Translate());
                return;
            }

            List<ModularRenderNode> snapshot = root.RenderSnapshot();
            if (SynchronizeCEAmmunitionToChamber(snapshot))
                snapshot = root.RenderSnapshot();
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
                (workshop == null
                    ? "HD_ModularWeapon_ImmediateHintDev"
                    : "HD_ModularWeapon_WorkshopHint").Translate());
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
                    selected.Props.sockets?.Count ?? 0, directChildCount)
                    + "  ·  " + selected.Props.partCategory.Label());
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
                    socket?.Label ?? child.parentSocketId)
                    + "  ·  " + child.Props.partCategory.Label();
                if (socket?.isRail == true)
                    connection += "  ·  " + "HD_ModularWeapon_RailOffset".Translate(
                        child.railOffset.ToString("0.###", CultureInfo.InvariantCulture));
                if (child.mountedOnOppositeSurface)
                    connection += "  ·  "
                        + "HD_ModularWeapon_OppositeMounted".Translate();
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
                .Where(node => !ShouldHideCEAmmunitionPart(node))
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
            // Keep the normal compact overlays out of the weapon's drawing area.
            inner.yMin += 92f;
            Vector2 origin = inner.center;
            float pixelsPerCell = Mathf.Min(inner.width, inner.height) * 0.82f;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    ModularRenderNode node = snapshot[i];
                    if (!ModularWeaponAssemblyRenderer.ShouldDrawNode(node)) continue;
                    Graphic graphic = node.thing.Graphic;
                    string texturePath = node.thing.def.graphicData?.texPath;
                    Texture texture = pass == 0
                        ? texturePath.NullOrEmpty()
                            ? null
                            : ContentFinder<Texture2D>.Get(texturePath + "_Outline", false)
                        : graphic?.MatSingle?.mainTexture;
                    if (graphic == null || texture == null) continue;

                    Vector2 center = WorldToGui(node.GraphicCenter, origin, pixelsPerCell);
                    Vector2 graphicScale = node.GraphicScale;
                    Vector2 size = Vector2.Scale(graphic.drawSize,
                        new Vector2(Mathf.Abs(graphicScale.x),
                            Mathf.Abs(graphicScale.y))) * pixelsPerCell;
                    Rect drawRect = CenteredRect(center, size);
                    Color previous = GUI.color;
                    if (pass == 1 && selected != null && node.path != selected.path)
                        GUI.color = new Color(1f, 1f, 1f, 0.78f);
                    Matrix4x4 matrix = GUI.matrix;
                    if (!Mathf.Approximately(node.GraphicAngle, 0f))
                        UI.RotateAroundPivot(-node.GraphicAngle, drawRect.center);
                    DrawTexture(drawRect, texture, node.GraphicVerticallyFlipped);
                    GUI.matrix = matrix;
                    GUI.color = previous;
                }
            }

            DrawSelectedRailOverlay(selected, origin, pixelsPerCell);
            Rect performanceRect = performanceExpanded
                ? new Rect(rect.x + 10f, rect.y + 10f, 430f, 390f)
                : new Rect(rect.x + 10f, rect.y + 10f, 285f, 60f);
            DrawPerformanceBlock(performanceRect, performanceExpanded);
            DrawAmmunitionBadge(
                new Rect(rect.xMax - 245f, rect.y + 10f, 235f, 74f),
                snapshot);
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

        private void DrawPerformanceBlock(Rect rect, bool expanded)
        {
            Widgets.DrawBoxSolidWithOutline(rect,
                new Color(0.07f, 0.08f, 0.085f, 0.93f),
                new Color(0.32f, 0.38f, 0.40f), 1);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 5f,
                rect.width - 42f, 20f),
                "HD_ModularWeapon_StatsTitle".Translate());
            if (Widgets.ButtonText(new Rect(rect.xMax - 31f, rect.y + 4f, 25f, 22f),
                expanded ? "−" : "+"))
                performanceExpanded = !performanceExpanded;

            if (!expanded)
            {
                Widgets.Label(new Rect(rect.x + 8f, rect.y + 26f,
                    rect.width - 16f, 28f),
                    "HD_ModularWeapon_PerformanceCompact".Translate(
                        root.EffectiveRoundsPerMinute.ToString("0.#",
                            CultureInfo.InvariantCulture),
                        root.EffectiveFireDelaySeconds.ToString("0.###",
                            CultureInfo.InvariantCulture)));
                Text.Font = GameFont.Small;
                return;
            }

            string text = "HD_ModularWeapon_Performance".Translate(
                root.RealisticRoundsPerMinute.ToString("0.#", CultureInfo.InvariantCulture),
                root.EffectiveRoundsPerMinute.ToString("0.#", CultureInfo.InvariantCulture),
                root.MinimumFireDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture),
                root.EffectiveFireDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture),
                root.EffectiveBurstIntervalTicks);
            Thing weapon = root.parent;
            VerbProperties verb = weapon.def.Verbs?.FirstOrDefault(v => v.isPrimary)
                ?? weapon.def.Verbs?.FirstOrDefault();
            if (verb != null)
                text += "\n" + "Burst shots" + ": "
                    + root.ApplyBurstShotCount(verb.burstShotCount);
            ModularWeaponFunctionStatus functionStatus = root.FunctionStatus;
            text += "\n" + (functionStatus.CanFire
                ? "HD_ModularWeapon_FunctionOperational".Translate().ToString()
                : "HD_ModularWeapon_FunctionBlocked".Translate(
                    functionStatus.MissingRequiredLabels).ToString());
            if (functionStatus.missingFunctionalParts.Count > 0)
                text += "\n" + "HD_ModularWeapon_FunctionDegraded".Translate(
                    functionStatus.MissingFunctionalLabels);
            text += "\n" + StatDefOf.RangedWeapon_Cooldown.LabelCap + ": "
                + weapon.GetStatValue(StatDefOf.RangedWeapon_Cooldown)
                    .ToString("0.###", CultureInfo.InvariantCulture) + "s";
            text += "\n" + StatDefOf.RangedWeapon_WarmupMultiplier.LabelCap + ": ×"
                + weapon.GetStatValue(StatDefOf.RangedWeapon_WarmupMultiplier)
                    .ToString("0.###", CultureInfo.InvariantCulture);
            if (ModsConfig.IsActive("ceteam.combatextended"))
            {
                AppendCEStat(ref text, weapon, "SightsEfficiency", "P0");
                AppendCEStat(ref text, weapon, "ShotSpread", "0.###");
                AppendCEStat(ref text, weapon, "SwayFactor", "0.###");
                AppendCEStat(ref text, weapon, "Recoil", "0.###");
            }
            else
            {
                text += "\n" + StatDefOf.AccuracyShort.LabelCap + ": "
                    + weapon.GetStatValue(StatDefOf.AccuracyShort).ToString("P0");
                text += "  " + StatDefOf.AccuracyMedium.LabelCap + ": "
                    + weapon.GetStatValue(StatDefOf.AccuracyMedium).ToString("P0");
                text += "\n" + StatDefOf.AccuracyLong.LabelCap + ": "
                    + weapon.GetStatValue(StatDefOf.AccuracyLong).ToString("P0");
            }
            text += "  " + StatDefOf.Mass.LabelCap + ": "
                + weapon.GetStatValue(StatDefOf.Mass)
                    .ToString("0.##", CultureInfo.InvariantCulture) + "kg";
            ModularWeaponConvertedStats converted = root.ConvertedStats;
            text += "\n\n" + "HD_ModularWeapon_ConvertedStats".Translate();
            text += "\n" + "HD_ModularWeapon_Mass".Translate() + ": "
                + converted.MassKg.ToString("0.##", CultureInfo.InvariantCulture) + " kg";
            text += "  " + "HD_ModularWeapon_RecoilControl".Translate() + ": "
                + converted.RecoilControl.ToString("P0");
            text += "\n" + "HD_ModularWeapon_Ergonomics".Translate() + ": "
                + converted.Ergonomics.ToString("P0");
            text += "  " + "HD_ModularWeapon_MuzzleControl".Translate() + ": "
                + converted.MuzzleControl.ToString("P0");
            text += "\n" + "HD_ModularWeapon_Reliability".Translate() + ": "
                + converted.OperatingReliability.ToString("P1");
            text += "  " + "HD_ModularWeapon_GasEfficiency".Translate() + ": "
                + converted.GasEfficiency.ToString("P0");
            text += "\n" + "HD_ModularWeapon_Balance".Translate() + ": "
                + (converted.CenterOfMassMeters * 100f)
                    .ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " cm";
            text += "  " + "HD_ModularWeapon_Handling".Translate() + ": "
                + converted.Handling.ToString("P0");
            text += "\n" + "HD_ModularWeapon_TargetAcquisition".Translate() + ": "
                + converted.TargetAcquisition.ToString("P0");
            text += "  " + "HD_ModularWeapon_AimingPrecision".Translate() + ": "
                + converted.AimingPrecision.ToString("P0");
            text += "\n" + "HD_ModularWeapon_IdentificationDistance".Translate() + ": "
                + converted.IdentificationDistanceCells
                    .ToString("0.#", CultureInfo.InvariantCulture) + " "
                + "HD_ModularWeapon_Cells".Translate();
            IReadOnlyList<ModularSightGroupStatus> sightGroups = root.SightGroups;
            text += "\n\n" + "HD_ModularWeapon_SightGroups".Translate(
                sightGroups.Count);
            for (int i = 0; i < sightGroups.Count; i++)
            {
                ModularSightGroupStatus group = sightGroups[i];
                string primary = group.primary != null
                    ? group.primary.LabelCap.ToString()
                    : "HD_ModularWeapon_SightNoPrimary".Translate().ToString();
                if (group.hasCompleteIronSight)
                    primary += " / " + "HD_ModularWeapon_SightIronPair".Translate();
                if (group.magnifierBehindPrimary)
                    primary += " + " + "HD_ModularWeapon_SightMagnifierRear".Translate();
                else if (group.magnifierAheadPrimary)
                    primary += " / " + "HD_ModularWeapon_SightMagnifierFront".Translate();
                if (group.belowWeaponSightCount > 0)
                    primary += " / " + "HD_ModularWeapon_SightBelowWeapon".Translate(
                        group.belowWeaponSightCount);
                text += "\n" + "HD_ModularWeapon_SightGroupLine".Translate(
                    group.isActive ? "▶ " : "  ",
                    group.axisHeight.ToString("0.###", CultureInfo.InvariantCulture),
                    group.efficiency.ToString("P0"),
                    primary);
            }
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 29f,
                rect.width - 16f, rect.height - 36f), text);
            Text.Font = GameFont.Small;
        }

        private static void AppendCEStat(
            ref string text,
            Thing weapon,
            string defName,
            string format)
        {
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(defName);
            if (stat == null || weapon == null) return;

            text += "\n" + stat.LabelCap + ": "
                + weapon.GetStatValue(stat).ToString(
                    format,
                    CultureInfo.InvariantCulture);
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

        private void DrawSocketPanel(Rect rect, ModularRenderNode node)
        {
            Widgets.DrawMenuSection(rect);
            if (node == null) return;
            Rect inner = rect.ContractedBy(10f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 26f), node.thing.LabelCap);
            float socketTop = 30f;
            if (root.HasAdjustableGasSystem)
            {
                DrawGasTubeControl(new Rect(
                    inner.x,
                    inner.y + socketTop,
                    inner.width,
                    82f));
                socketTop += 88f;
            }
            Rect outRect = new Rect(inner.x, inner.y + socketTop, inner.width,
                inner.height - socketTop);
            float contentHeight = 16f;
            for (int i = 0; i < node.Props.sockets.Count; i++)
            {
                ModularAttachmentSocket candidate = node.Props.sockets[i];
                if (ShouldHideCEAmmunitionSocket(candidate)) continue;
                contentHeight += 78f + node.comp.CountOnSocket(candidate.id) * 50f;
            }
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f,
                Mathf.Max(outRect.height, contentHeight));
            Widgets.BeginScrollView(outRect, ref socketScroll, viewRect);

            float y = 2f;
            for (int socketIndex = 0; socketIndex < node.Props.sockets.Count; socketIndex++)
            {
                ModularAttachmentSocket socket = node.Props.sockets[socketIndex];
                if (ShouldHideCEAmmunitionSocket(socket)) continue;
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
                        SurfaceText(socket.railSurface) + "  ·  "
                            + TagText(socket.tags)).ToString()
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
                        ReturnOrDestroyAssembly(removed);
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
                int available = workshop?.AvailableCount(def) ?? int.MaxValue;
                bool unavailable = available <= 0;
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
                string occupancyLabel = mount == null
                    ? props.partCategory.Label()
                    : props.partCategory.Label() + "\n"
                    + "HD_ModularWeapon_SurfaceDetail".Translate(
                        SurfaceText(mount.railSurface)).ToString() + "\n"
                    + (mount.railOccupancy > 0f
                        ? "HD_ModularWeapon_Occupancy".Translate(
                        mount.railOccupancy.ToString("0.###",
                            CultureInfo.InvariantCulture)).ToString()
                        : string.Empty);
                Widgets.Label(new Rect(tile.x + 6f, tile.yMax - 42f,
                    tile.width - 12f, 38f), occupancyLabel);
                if (workshop != null
                    && CompModularWeaponPartsBox.StorageModeFor(def)
                        != ModularWeaponPartStorageMode.Internal)
                {
                    GUI.color = unavailable ? Color.red : new Color(0.65f, 0.9f, 0.65f);
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(new Rect(tile.x + 6f, tile.y + 5f,
                        tile.width - 12f, 22f),
                        "HD_ModularWeapon_PartAvailable".Translate(available));
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                Text.Font = GameFont.Small;
                if (hovered) hoveredPart = def;
                if (!unavailable && Widgets.ButtonInvisible(tile))
                    InstallPart(node, socket, def, mount);
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

            Thing child;
            if (workshop != null)
            {
                if (!workshop.TryTakePart(def, out child))
                {
                    RestoreDisplaced(node.comp, displaced);
                    Messages.Message(
                        "HD_ModularWeapon_PartUnavailable".Translate(def.LabelCap),
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }
            }
            else
            {
                child = ThingMaker.MakeThing(def);
            }
            string rejection;
            float offset;
            bool success = node.comp.TryFindAttachOffset(
                    child, socket.id, mount.id, out offset, out rejection)
                && node.comp.TryAttach(child, socket.id, mount.id, offset, out rejection);
            if (!success)
            {
                ReturnOrDestroyAssembly(child);
                RestoreDisplaced(node.comp, displaced);
                Messages.Message(rejection ?? "HD_ModularWeapon_AttachFailed".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            for (int i = 0; i < displaced.Count; i++)
                ReturnOrDestroyAssembly(displaced[i].thing);
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

        private void ReturnOrDestroyAssembly(Thing thing)
        {
            if (workshop != null) workshop.ReturnAssembly(thing);
            else DestroyAssembly(thing);
        }

        private void DrawHoveredPartPreview(Rect windowRect, ThingDef def)
        {
            PartPreviewData data = PartPreviewData.For(def);
            Vector2 mouse = Event.current.mousePosition;
            float width = 250f;
            float height = 340f;
            float x = Mathf.Min(windowRect.xMax - width - 4f, mouse.x + 20f);
            float y = Mathf.Clamp(mouse.y - height * 0.5f,
                windowRect.y + 4f, windowRect.yMax - height - 4f);
            Rect panel = new Rect(x, y, width, height);
            Widgets.DrawBoxSolidWithOutline(panel,
                new Color(0.035f, 0.04f, 0.045f, 0.98f), Color.yellow, 2);
            Widgets.Label(new Rect(panel.x + 10f, panel.y + 8f,
                panel.width - 20f, 26f), def.LabelCap);

            Rect imageArea = new Rect(panel.x + 12f, panel.y + 38f,
                panel.width - 24f, 140f);
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
            Widgets.Label(new Rect(panel.x + 10f, panel.y + 183f,
                panel.width - 20f, panel.height - 191f), details);
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
            ModularAttachmentSocket current = node?.Props.SocketNamed(selectedSocketId);
            if (current != null && !ShouldHideCEAmmunitionSocket(current)) return;
            selectedSocketId = node?.Props.sockets
                .FirstOrDefault(socket => !ShouldHideCEAmmunitionSocket(socket))
                ?.id;
            selectedAttachmentId = -1;
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

        private static string TagText(List<string> tags)
        {
            return tags.NullOrEmpty() ? "-" : string.Join(", ", tags.ToArray());
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
