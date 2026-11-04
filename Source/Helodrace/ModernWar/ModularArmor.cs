using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.ModernWar
{
    public enum ModularArmorInstallMode
    {
        Fixed,
        Positionable
    }

    public enum ModularArmorFacing
    {
        Front,
        Right,
        Back,
        Left
    }

    public enum ModularArmorMetric
    {
        Ergonomics,
        Ventilation,
        LoadDistribution
    }

    public enum ModularArmorConversionMode
    {
        Offset,
        Factor
    }

    public sealed class ModularArmorSlotDef : Def
    {
        public bool required;
        public int uiOrder;
    }

    public sealed class ModularArmorDrawOffsets
    {
        public Vector3 north;
        public Vector3 east;
        public Vector3 south;
        public Vector3 west;

        public System.Numerics.Vector3 For(Rot4 facing)
        {
            if (facing == Rot4.North) return north.ToNative();
            if (facing == Rot4.East) return east.ToNative();
            if (facing == Rot4.West) return west.ToNative();
            return south.ToNative();
        }
    }

    public sealed class ModularArmorPositionDef : Def
    {
        public List<BodyPartGroupDef> bodyPartGroups;
        public List<BodyPartDef> bodyParts;
        public List<ModularArmorFacing> protectedDirections;
        public float directionArcDegrees = 90f;
        public PawnRenderNodeTagDef parentTagDef;
        public ModularArmorDrawOffsets drawOffsets;
        // Absolute layer within the vanilla body/head apparel render band.
        public float drawLayer;

        public bool Covers(BodyPartRecord part)
        {
            if (part == null)
            {
                return false;
            }

            if (!bodyParts.NullOrEmpty() && bodyParts.Contains(part.def))
            {
                return true;
            }

            if (!bodyPartGroups.NullOrEmpty())
            {
                for (int i = 0; i < bodyPartGroups.Count; i++)
                {
                    if (part.IsInGroup(bodyPartGroups[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public sealed class ModularArmorPalsPanelDef : Def
    {
        public int columns = 6;
        public int rows = 4;
        public ModularArmorPositionDef armorPosition;
        public ModularArmorPartDef requiredPart;
        public float uiX = 0.2f;
        public float uiY = 0.45f;
        public float uiWidth = 0.6f;
        public float uiHeight = 0.35f;
        public ModularArmorDrawOffsets drawOrigin;
        public Vector2 drawCellSize = new Vector2(0.08f, 0.08f);
        // Added to armorPosition.drawLayer for the normal panel graphic.
        public float drawLayer;
        // Absolute layer used by authored side/back graphics behind body apparel.
        public float sideBackDrawLayer = 19.99f;
        public float positionDrawLayerStep;
        public int flatNormalPositionCount;
        public string authoredTexturePrefix;
        public string sideBackTexturePrefix;
        public string northTexturePrefix;
        public string northBackTexturePrefix;
        public string southTexturePrefix;
        public string southBackTexturePrefix;
        public List<ModularArmorFacing> visibleDirections;
        public List<ModularArmorFacing> renderDirections;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (columns <= 0 || rows <= 0)
            {
                yield return defName + " must have a positive PALS grid size.";
            }

            if (armorPosition == null)
            {
                yield return defName + " has no armorPosition.";
            }

            if (requiredPart != null
                && requiredPart.installMode != ModularArmorInstallMode.Fixed)
            {
                yield return defName + " requires a part that is not fixed.";
            }
        }
    }

    public sealed class ModularArmorPartDef : Def
    {
        public ModularArmorSlotDef slot;
        public ModularArmorInstallMode installMode;
        public ModularArmorPositionDef fixedPosition;
        public List<ModularArmorPalsPanelDef> allowedPalsPanels;
        public List<ModularArmorPalsPanelDef> blockedPalsPanels;
        public int palsWidth = 1;
        public int palsHeight = 1;
        public bool fillNarrowPalsPanel;
        public string authoredPalsTextureKey;
        public List<string> compatibleArmorTags;
        public List<string> conflictTags;
        public ThingDef plateThingDef;
        public ThingDef partThingDef;
        public bool canInstantCraft = true;
        public int installWorkTicks = 240;
        public bool playerSelectable = true;
        public float guaranteedBlockPenetration;

        public float armorRatingSharp;
        public float armorRatingBlunt;
        public float armorRatingHeat;
        public bool metallic = true;

        public float ergonomics;
        public float ventilation;
        public float loadDistribution;
        public List<ModularArmorPartStatModifier> statModifiers;

        public GraphicData graphicData;
        public bool drawBehindArmor;
        public List<ModularArmorFacing> renderDirections;
        public List<ModularArmorFacing> previewDirections;
        public GraphicData northUnderGraphicData;
        public PawnRenderNodeTagDef northUnderParentTagDef;
        // Immediately below the vanilla head-apparel root (70).
        public float northUnderDrawLayer = 69.99f;
        public string uiIconPath;
        public Color uiColor = new Color(0.32f, 0.38f, 0.30f);
        public ModularArmorDrawOffsets drawOffsets;
        public float drawLayer;

        public ModularArmorPositionDef DefaultPosition
        {
            get
            {
                if (installMode == ModularArmorInstallMode.Fixed)
                {
                    return fixedPosition;
                }

                return null;
            }
        }

        public ThingDef RequiredThingDef => plateThingDef ?? partThingDef;

        public bool CanInstantCraft => playerSelectable && canInstantCraft
            && partThingDef != null && plateThingDef == null
            && partThingDef.GetCompProperties<CompProperties_ArmorPlateSet>() == null;

        public int PalsWidthFor(ModularArmorPalsPanelDef panel)
        {
            if (panel == null || !fillNarrowPalsPanel)
            {
                return palsWidth;
            }

            return Mathf.Min(palsWidth, panel.columns);
        }

        public bool AllowsPosition(ModularArmorPositionDef position)
        {
            if (installMode == ModularArmorInstallMode.Fixed)
            {
                return position != null && position == fixedPosition;
            }

            return false;
        }

        public float ArmorFor(DamageArmorCategoryDef category)
        {
            if (category == DamageArmorCategoryDefOf.Sharp) return armorRatingSharp;
            if (category?.defName == "Blunt") return armorRatingBlunt;
            if (category?.defName == "Heat") return armorRatingHeat;
            return 0f;
        }

        public float MetricFor(ModularArmorMetric metric)
        {
            if (metric == ModularArmorMetric.Ergonomics) return ergonomics;
            if (metric == ModularArmorMetric.Ventilation) return ventilation;
            return loadDistribution;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (installMode == ModularArmorInstallMode.Fixed && slot == null)
            {
                yield return defName + " has no modular armor slot.";
            }

            if (installMode == ModularArmorInstallMode.Fixed && fixedPosition == null)
            {
                yield return defName + " is fixed but has no fixedPosition.";
            }

            if (installMode == ModularArmorInstallMode.Positionable
                && allowedPalsPanels.NullOrEmpty())
            {
                yield return defName + " is positionable but has no allowedPalsPanels.";
            }

            if (installMode == ModularArmorInstallMode.Positionable
                && (palsWidth <= 0 || palsHeight <= 0))
            {
                yield return defName + " has an invalid PALS footprint.";
            }

            if (!statModifiers.NullOrEmpty())
            {
                for (int i = 0; i < statModifiers.Count; i++)
                {
                    if (statModifiers[i]?.stat == null)
                    {
                        yield return defName + " has a stat modifier without a stat.";
                    }
                }
            }
        }
    }

    public sealed class ModularArmorPartStatModifier
    {
        public StatDef stat;
        public ModularArmorConversionMode mode;
        public float value;
    }

    public sealed class ModularArmorDefaultPart
    {
        public ModularArmorSlotDef slot;
        public ModularArmorPartDef part;
        public ModularArmorPositionDef position;
    }

    public sealed class ModularArmorStatConversion
    {
        public ModularArmorMetric source;
        public StatDef targetStat;
        public ModularArmorConversionMode mode;
        public float factor = 1f;
        public float referenceValue;
    }

    public sealed class CompProperties_ModularArmor : CompProperties
    {
        public List<ModularArmorSlotDef> slots;
        public List<ModularArmorDefaultPart> defaultParts;
        public List<ModularArmorPalsPanelDef> palsPanels;
        public List<string> armorTags;
        public List<ModularArmorStatConversion> statConversions;
        public float baseErgonomics;
        public float baseVentilation;
        public float baseLoadDistribution;
        public ModularArmorPreviewCrop previewCrop;
        public bool allowPlayerConfiguration = true;

        public CompProperties_ModularArmor()
        {
            compClass = typeof(CompModularArmor);
        }
    }

    public sealed class ModularArmorPreviewCrop
    {
        public float x;
        public float y;
        public float width = 1f;
        public float height = 1f;

        public Waylith.Math.Rect Rect => new Waylith.Math.Rect(x, y, width, height);
    }

    public sealed class CompProperties_ArmorPlateSet : CompProperties
    {
        public BallisticPlateMaterial material = BallisticPlateMaterial.Ceramic;
        // Composite plates specify their own coefficient in the physical item's XML.
        public float compositeDurabilityCoefficient = 1f;

        public CompProperties_ArmorPlateSet()
        {
            compClass = typeof(CompArmorPlateSet);
        }
    }

    public sealed class CompArmorPlateSet : ThingComp
    {
        private int plateOneHitPoints = -1;
        private int plateTwoHitPoints = -1;

        public int MaxHitPointsPerPlate => parent.MaxHitPoints;

        public float DurabilityMaterialCoefficient
        {
            get
            {
                CompProperties_ArmorPlateSet plateProps = (CompProperties_ArmorPlateSet)props;
                return BallisticArmorRules.MaterialCoefficient(
                    plateProps.material,
                    plateProps.compositeDurabilityCoefficient);
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureInitialized();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref plateOneHitPoints, "plateOneHitPoints", -1);
            Scribe_Values.Look(ref plateTwoHitPoints, "plateTwoHitPoints", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
            }
        }

        public int HitPointsFor(int plateNumber)
        {
            EnsureInitialized();
            return plateNumber == 2 ? plateTwoHitPoints : plateOneHitPoints;
        }

        public bool IsFunctional(int plateNumber)
        {
            return HitPointsFor(plateNumber) > 0;
        }

        public bool AnyFunctional
        {
            get
            {
                EnsureInitialized();
                return plateOneHitPoints > 0 || plateTwoHitPoints > 0;
            }
        }

        public void ApplyDamage(int plateNumber, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            EnsureInitialized();
            if (plateNumber == 2)
            {
                plateTwoHitPoints = Mathf.Max(0, plateTwoHitPoints - amount);
            }
            else
            {
                plateOneHitPoints = Mathf.Max(0, plateOneHitPoints - amount);
            }
        }

        public override string CompInspectStringExtra()
        {
            EnsureInitialized();
            return "HD_ArmorPlateSet_Inspect".Translate(
                plateOneHitPoints,
                MaxHitPointsPerPlate,
                plateTwoHitPoints,
                MaxHitPointsPerPlate);
        }

        private void EnsureInitialized()
        {
            if (plateOneHitPoints < 0)
            {
                plateOneHitPoints = MaxHitPointsPerPlate;
            }

            if (plateTwoHitPoints < 0)
            {
                plateTwoHitPoints = MaxHitPointsPerPlate;
            }

            plateOneHitPoints = Mathf.Clamp(
                plateOneHitPoints,
                0,
                MaxHitPointsPerPlate);
            plateTwoHitPoints = Mathf.Clamp(
                plateTwoHitPoints,
                0,
                MaxHitPointsPerPlate);
        }
    }

    public sealed class InstalledModularArmorPart : IExposable, IThingHolder
    {
        public ModularArmorSlotDef slot;
        public ModularArmorPartDef part;
        public ModularArmorPositionDef position;
        public ModularArmorPalsPanelDef palsPanel;
        public int palsX;
        public int palsY;
        private ThingOwner<Thing> plateSetContainer;
        public bool plateOrderSwapped;
        // Editor-only reference: preview health without transferring the real item.
        internal InstalledModularArmorPart previewSource;
        internal int previewSourceIndex = -1;

        public IThingHolder ParentHolder => null;

        public bool IsPalsMounted => part?.installMode == ModularArmorInstallMode.Positionable;

        public ModularArmorPositionDef EffectivePosition => IsPalsMounted
            ? palsPanel?.armorPosition
            : position;

        public InstalledModularArmorPart()
        {
        }

        public InstalledModularArmorPart(
            ModularArmorSlotDef slot,
            ModularArmorPartDef part,
            ModularArmorPositionDef position)
        {
            this.slot = slot;
            this.part = part;
            this.position = position;
            EnsurePlateSetContainer();
        }

        public InstalledModularArmorPart(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef palsPanel,
            int palsX,
            int palsY)
        {
            this.part = part;
            this.palsPanel = palsPanel;
            this.palsX = palsX;
            this.palsY = palsY;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref slot, "slot");
            Scribe_Defs.Look(ref part, "part");
            Scribe_Defs.Look(ref position, "position");
            Scribe_Defs.Look(ref palsPanel, "palsPanel");
            Scribe_Values.Look(ref palsX, "palsX", 0);
            Scribe_Values.Look(ref palsY, "palsY", 0);
            Scribe_Values.Look(ref plateOrderSwapped, "plateOrderSwapped", false);
            EnsurePlateSetContainer();
            plateSetContainer.ExposeData();
        }

        public Thing PlateSet
        {
            get
            {
                if (previewSource != null) return previewSource.PlateSet;
                EnsurePlateSetContainer();
                return plateSetContainer.Count > 0 ? plateSetContainer[0] : null;
            }
        }

        public Thing InstalledItem => PlateSet;

        public Thing PlateForFacing(ModularArmorFacing facing)
        {
            Thing plateSet = PlateSet;
            if (plateSet == null || plateSet.Destroyed)
            {
                return null;
            }

            CompArmorPlateSet plateComp = plateSet.TryGetComp<CompArmorPlateSet>();
            return plateComp == null
                ? plateSet.HitPoints > 0 ? plateSet : null
                : plateComp.IsFunctional(PlateNumberForFacing(facing))
                    ? plateSet
                    : null;
        }

        public int PlateNumberForFacing(ModularArmorFacing facing)
        {
            List<ModularArmorFacing> directions = EffectivePosition?
                .protectedDirections?
                .Distinct()
                .ToList();
            int directionIndex = directions?.IndexOf(facing) ?? -1;
            if (directionIndex < 0)
            {
                return 1;
            }

            int normalNumber = directionIndex == 0 ? 1 : 2;
            return plateOrderSwapped ? 3 - normalNumber : normalNumber;
        }

        public bool CanSwapFrontRearPlates
        {
            get
            {
                List<ModularArmorFacing> directions = EffectivePosition?
                    .protectedDirections;
                return PlateSet != null
                    && directions?.Contains(ModularArmorFacing.Front) == true
                    && directions.Contains(ModularArmorFacing.Back);
            }
        }

        public bool SwapFrontRearPlates()
        {
            if (!CanSwapFrontRearPlates)
            {
                return false;
            }

            plateOrderSwapped = !plateOrderSwapped;
            return true;
        }

        public int PlateHitPointsForFacing(ModularArmorFacing facing)
        {
            Thing plateSet = PlateSet;
            if (plateSet == null || plateSet.Destroyed)
            {
                return 0;
            }

            CompArmorPlateSet plateComp = plateSet.TryGetComp<CompArmorPlateSet>();
            return plateComp?.HitPointsFor(PlateNumberForFacing(facing))
                ?? plateSet.HitPoints;
        }

        public int PlateMaxHitPoints
        {
            get
            {
                Thing plateSet = PlateSet;
                return plateSet?.TryGetComp<CompArmorPlateSet>()?.MaxHitPointsPerPlate
                    ?? plateSet?.MaxHitPoints
                    ?? 0;
            }
        }

        public float PlateHealthRatioForFacing(ModularArmorFacing facing)
        {
            int maxHitPoints = PlateMaxHitPoints;
            return maxHitPoints > 0
                ? Mathf.Clamp01((float)PlateHitPointsForFacing(facing) / maxHitPoints)
                : 0f;
        }

        public void DamagePlateForFacing(
            ModularArmorFacing facing,
            int amount,
            DamageDef damageDef)
        {
            Thing plateSet = PlateSet;
            if (plateSet == null || plateSet.Destroyed || amount <= 0)
            {
                return;
            }

            CompArmorPlateSet plateComp = plateSet.TryGetComp<CompArmorPlateSet>();
            if (plateComp != null)
            {
                plateComp.ApplyDamage(PlateNumberForFacing(facing), amount);
            }
            else
            {
                plateSet.TakeDamage(new DamageInfo(damageDef, amount, 0f));
            }
        }

        public bool HasFunctionalPlate
        {
            get
            {
                if (part?.plateThingDef == null)
                {
                    return true;
                }

                Thing plateSet = PlateSet;
                if (plateSet == null || plateSet.Destroyed)
                {
                    return false;
                }

                CompArmorPlateSet plateComp = plateSet.TryGetComp<CompArmorPlateSet>();
                return plateComp?.AnyFunctional ?? plateSet.HitPoints > 0;
            }
        }

        public bool TryInstallPlateSet(Thing plateSet)
        {
            EnsurePlateSetContainer();
            return plateSet != null
                && PlateSet == null
                && plateSetContainer.TryAdd(plateSet, false);
        }

        public bool TryInstallRequiredItem(Thing item)
        {
            return item != null
                && part?.RequiredThingDef == item.def
                && TryInstallPlateSet(item);
        }

        public Thing RemovePlateSet()
        {
            EnsurePlateSetContainer();
            Thing plateSet = PlateSet;
            return plateSet == null
                ? null
                : plateSetContainer.Take(plateSet, plateSet.stackCount);
        }

        public Thing RemoveInstalledItem()
        {
            return RemovePlateSet();
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsurePlateSetContainer();
            return plateSetContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsurePlateSetContainer();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, plateSetContainer);
        }

        private void EnsurePlateSetContainer()
        {
            if (plateSetContainer == null)
            {
                plateSetContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
            }
        }
    }

    public sealed partial class CompModularArmor : ThingComp
    {
        private List<InstalledModularArmorPart> installedParts;

        public CompProperties_ModularArmor Props => (CompProperties_ModularArmor)props;

        public Apparel Apparel => parent as Apparel;

        public Pawn Wearer => Apparel?.Wearer;

        public IReadOnlyList<InstalledModularArmorPart> InstalledParts
        {
            get
            {
                EnsureConfiguration();
                return installedParts;
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureConfiguration();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(
                ref installedParts,
                "modularArmorParts",
                LookMode.Deep);
            Scribe_Deep.Look(ref pendingCustomization, "pendingArmorCustomization");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureConfiguration();
                // Loading runs in a long-event worker. PALS validation checks
                // authored textures, which ContentFinder only permits on the
                // main thread. A worker lookup returns null and would remove
                // every valid pouch/radio from the loaded armor.
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (parent.Destroyed) return;
                    ValidateConfiguration();
                    NotifyConfigurationChanged();
                });
            }
        }

        public InstalledModularArmorPart InstalledIn(ModularArmorSlotDef slot)
        {
            EnsureConfiguration();
            return installedParts.FirstOrDefault(record => record?.slot == slot);
        }

        public InstalledModularArmorPart ConflictingInstalledPart(
            ModularArmorPartDef part,
            InstalledModularArmorPart ignore = null)
        {
            EnsureConfiguration();
            if (part?.conflictTags.NullOrEmpty() != false)
            {
                return null;
            }

            return installedParts.FirstOrDefault(record => record != null
                && record != ignore
                && record.part != null
                && !record.part.conflictTags.NullOrEmpty()
                && record.part.conflictTags.Any(part.conflictTags.Contains));
        }

        public bool IsPartCompatible(ModularArmorPartDef part)
        {
            if (part == null)
            {
                return false;
            }

            if (part.installMode == ModularArmorInstallMode.Fixed
                && (part.slot == null
                    || Props.slots.NullOrEmpty()
                    || !Props.slots.Contains(part.slot)))
            {
                return false;
            }

            if (part.installMode == ModularArmorInstallMode.Positionable
                && (part.allowedPalsPanels.NullOrEmpty()
                    || Props.palsPanels.NullOrEmpty()
                    || !part.allowedPalsPanels.Any(Props.palsPanels.Contains)))
            {
                return false;
            }

            return part.compatibleArmorTags.NullOrEmpty()
                || !Props.armorTags.NullOrEmpty()
                    && part.compatibleArmorTags.Any(Props.armorTags.Contains);
        }

        public bool SetPart(
            ModularArmorSlotDef slot,
            ModularArmorPartDef part,
            ModularArmorPositionDef position = null,
            Thing suppliedItem = null)
        {
            EnsureConfiguration();
            if (slot == null
                || Props.slots.NullOrEmpty()
                || !Props.slots.Contains(slot))
            {
                return false;
            }

            InstalledModularArmorPart existing = InstalledIn(slot);
            if (part == null)
            {
                if (slot.required)
                {
                    return false;
                }

                if (existing != null)
                {
                    if (!RemovePalsPartsRequiring(existing.part))
                    {
                        return false;
                    }

                    if (!TryReturnInstalledItem(existing))
                    {
                        return false;
                    }

                    installedParts.Remove(existing);
                    NotifyConfigurationChanged();
                }

                return true;
            }

            if (part.installMode != ModularArmorInstallMode.Fixed
                || part.slot != slot
                || !IsPartCompatible(part))
            {
                return false;
            }

            if (ConflictingInstalledPart(part, existing) != null)
            {
                return false;
            }

            position = position ?? part.DefaultPosition;
            if (!part.AllowsPosition(position))
            {
                return false;
            }

            ThingDef requiredThingDef = part.RequiredThingDef;
            bool canReuseInstalledItem = requiredThingDef != null
                && existing?.part?.RequiredThingDef == requiredThingDef
                && existing.InstalledItem != null;
            if (requiredThingDef != null
                && !canReuseInstalledItem
                && !isPreviewDraft
                && suppliedItem?.def != requiredThingDef)
            {
                return false;
            }

            if (!RemovePalsPartsBlockedBy(part))
            {
                return false;
            }

            if (existing?.part != part
                && !RemovePalsPartsRequiring(existing?.part))
            {
                return false;
            }

            if (existing != null && !canReuseInstalledItem)
            {
                if (!TryReturnInstalledItem(existing))
                {
                    return false;
                }

                installedParts.Remove(existing);
                existing = null;
            }

            if (existing == null)
            {
                existing = new InstalledModularArmorPart(slot, part, position);
                installedParts.Add(existing);
            }
            else
            {
                existing.part = part;
                existing.position = position;
            }

            if (suppliedItem != null
                && !existing.TryInstallRequiredItem(suppliedItem))
            {
                return false;
            }

            NotifyConfigurationChanged();
            return true;
        }

        /// <summary>
        /// Replaces the complete modular configuration with a validated Def preset.
        /// Candidate records (including physical plate/module Things) are built first;
        /// the old configuration is only destroyed after every entry succeeds.
        /// </summary>
        public bool TryApplyPreset(ModularArmorPresetDef preset, out string rejection)
        {
            rejection = null;
            if (preset == null || preset.armorDef != parent.def)
            {
                rejection = "The armor preset does not target this apparel.";
                return false;
            }

            EnsureConfiguration();
            List<InstalledModularArmorPart> previous = installedParts;
            installedParts = new List<InstalledModularArmorPart>();
            bool success = TryBuildPresetConfiguration(preset, out rejection);
            if (!success)
            {
                DestroyInstalledItems(installedParts);
                installedParts = previous;
                NotifyConfigurationChanged();
                return false;
            }

            DestroyInstalledItems(previous);
            NotifyConfigurationChanged();
            return true;
        }

        private bool TryBuildPresetConfiguration(
            ModularArmorPresetDef preset,
            out string rejection)
        {
            rejection = null;
            List<ModularArmorPresetPart> fixedEntries;
            if (preset.useDefaultConfiguration)
            {
                fixedEntries = Props.defaultParts.NullOrEmpty()
                    ? new List<ModularArmorPresetPart>()
                    : Props.defaultParts
                        .Where(entry => entry?.slot != null && entry.part != null)
                        .Select(entry => new ModularArmorPresetPart
                        {
                            slot = entry.slot,
                            part = entry.part,
                            position = entry.position
                        }).ToList();
            }
            else
            {
                fixedEntries = preset.fixedParts
                    ?? new List<ModularArmorPresetPart>();
            }

            for (int i = 0; i < fixedEntries.Count; i++)
            {
                ModularArmorPresetPart entry = fixedEntries[i];
                Thing suppliedItem = MakeRequiredItem(entry?.part);
                if (entry?.slot == null || entry.part == null
                    || !SetPart(entry.slot, entry.part, entry.position, suppliedItem))
                {
                    suppliedItem?.Destroy(DestroyMode.Vanish);
                    rejection = "Invalid fixed armor entry at index " + i + ".";
                    return false;
                }

                InstalledModularArmorPart installed = InstalledIn(entry.slot);
                if (installed != null)
                    installed.plateOrderSwapped = entry.plateOrderSwapped;
            }

            List<ModularArmorPresetPalsPart> palsEntries =
                preset.useDefaultConfiguration
                    ? null
                    : preset.palsParts;
            if (!palsEntries.NullOrEmpty())
            {
                for (int i = 0; i < palsEntries.Count; i++)
                {
                    ModularArmorPresetPalsPart entry = palsEntries[i];
                    Thing suppliedItem = MakeRequiredItem(entry?.part);
                    if (entry?.part == null || entry.panel == null
                        || InstallPalsPart(
                            entry.part, entry.panel, entry.x, entry.y,
                            suppliedItem) == null)
                    {
                        suppliedItem?.Destroy(DestroyMode.Vanish);
                        rejection = "Invalid PALS armor entry at index " + i + ".";
                        return false;
                    }
                }
            }

            if (!Props.slots.NullOrEmpty())
            {
                for (int i = 0; i < Props.slots.Count; i++)
                {
                    ModularArmorSlotDef slot = Props.slots[i];
                    if (slot?.required == true && InstalledIn(slot) == null)
                    {
                        rejection = "Required armor slot " + slot.defName
                            + " is empty.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static Thing MakeRequiredItem(ModularArmorPartDef part)
        {
            ThingDef itemDef = part?.RequiredThingDef;
            return itemDef == null
                ? null
                : ThingMaker.MakeThing(itemDef, GenStuff.DefaultStuffFor(itemDef));
        }

        private static void DestroyInstalledItems(
            List<InstalledModularArmorPart> records)
        {
            if (records == null) return;
            for (int i = 0; i < records.Count; i++)
            {
                Thing item = records[i]?.RemoveInstalledItem();
                if (item != null && !item.Destroyed)
                    item.Destroy(DestroyMode.Vanish);
            }
        }

        public int RequiredItemsNeededFor(
            ModularArmorSlotDef slot,
            ModularArmorPartDef part)
        {
            ThingDef requiredThingDef = part?.RequiredThingDef;
            if (requiredThingDef == null)
            {
                return 0;
            }

            InstalledModularArmorPart existing = InstalledIn(slot);
            return existing?.part?.RequiredThingDef == requiredThingDef
                && existing.InstalledItem != null
                ? 0
                : 1;
        }

        public int AvailableRequiredItemCount(ThingDef itemDef)
        {
            if (itemDef == null)
            {
                return 0;
            }

            int result = 0;
            ThingOwner<Thing> inventory = Wearer?.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    if (inventory[i]?.def == itemDef)
                    {
                        result += inventory[i].stackCount;
                    }
                }
            }

            Map map = EffectiveMap;
            if (map != null)
            {
                List<Thing> mapPlates = map.listerThings.ThingsOfDef(itemDef);
                for (int i = 0; i < mapPlates.Count; i++)
                {
                    Thing plate = mapPlates[i];
                    if (plate?.Spawned == true
                        && !plate.Destroyed
                        && !plate.IsForbidden(Faction.OfPlayer))
                    {
                        result += plate.stackCount;
                    }
                }
            }

            return result;
        }

        public bool TryStartFixedPartInstall(
            ModularArmorSlotDef slot,
            ModularArmorPartDef part,
            ModularArmorPositionDef position,
            out string rejection, Thing workbench = null)
        {
            rejection = null;
            if (!CompClothingWorkbench.CanUse(workbench, this))
            {
                rejection = "HD_ClothingBench_Required".Translate();
                return false;
            }
            InstalledModularArmorPart existing = InstalledIn(slot);
            if (part == null)
            {
                return SetPart(slot, null);
            }

            if (existing?.part == part)
            {
                return true;
            }

            if (part.installMode != ModularArmorInstallMode.Fixed
                || part.slot != slot
                || !IsPartCompatible(part)
                || ConflictingInstalledPart(part, existing) != null)
            {
                rejection = "HD_ModularArmor_InvalidInstall".Translate();
                return false;
            }

            if (DebugSettings.godMode)
            {
                Thing suppliedItem = RequiredItemsNeededFor(slot, part) > 0
                    ? MakeRequiredItem(part)
                    : null;
                bool installed = SetPart(slot, part, position, suppliedItem);
                if (!installed && suppliedItem != null && !suppliedItem.Destroyed)
                {
                    suppliedItem.Destroy(DestroyMode.Vanish);
                }

                return installed;
            }

            if (part.RequiredThingDef == null)
            {
                return SetPart(slot, part, position);
            }

            return TryStartInstallJob(part, position, null, 0, 0, out rejection, workbench);
        }

        public bool TryStartPalsPartInstall(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x,
            int y,
            out string rejection, Thing workbench = null)
        {
            rejection = null;
            if (!CompClothingWorkbench.CanUse(workbench, this))
            {
                rejection = "HD_ClothingBench_Required".Translate();
                return false;
            }
            if (!CanPlacePalsPart(part, panel, x, y))
            {
                rejection = "HD_ModularArmor_InvalidInstall".Translate();
                return false;
            }

            if (DebugSettings.godMode)
            {
                Thing suppliedItem = MakeRequiredItem(part);
                bool installed = InstallPalsPart(
                    part,
                    panel,
                    x,
                    y,
                    suppliedItem) != null;
                if (!installed && suppliedItem != null && !suppliedItem.Destroyed)
                {
                    suppliedItem.Destroy(DestroyMode.Vanish);
                }

                return installed;
            }

            if (part.RequiredThingDef == null)
            {
                return InstallPalsPart(part, panel, x, y) != null;
            }

            return TryStartInstallJob(part, null, panel, x, y, out rejection, workbench);
        }

        private bool TryStartInstallJob(
            ModularArmorPartDef part,
            ModularArmorPositionDef position,
            ModularArmorPalsPanelDef panel,
            int x,
            int y,
            out string rejection, Thing workbench)
        {
            rejection = null;
            Pawn worker = Wearer;
            if (worker?.Spawned != true || worker.Faction != Faction.OfPlayer)
            {
                rejection = "HD_ModularArmor_MustBeWorn".Translate();
                return false;
            }

            if (!CompClothingWorkbench.CanWorkAt(worker, workbench, this))
            {
                rejection = "HD_ClothingBench_Unreachable".Translate();
                return false;
            }

            Thing item = FindAvailableRequiredItem(worker, part.RequiredThingDef);
            if (item == null)
            {
                rejection = "HD_ModularArmor_NoReachablePart".Translate(
                    part.RequiredThingDef.LabelCap);
                return false;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                "HD_InstallModularArmorPart");
            if (jobDef == null)
            {
                rejection = "HD_ModularArmor_JobMissing".Translate();
                return false;
            }

            int panelIndex = panel == null ? -1 : Props.palsPanels.IndexOf(panel);
            int packedZ = panelIndex < 0 ? -1 : panelIndex * 100 + y;
            LocalTargetInfo installData = new LocalTargetInfo(
                new IntVec3(panel == null ? -1 : x, part.shortHash, packedZ));
            Job job = JobMaker.MakeJob(jobDef, item, parent, installData);
            job.targetQueueB = new List<LocalTargetInfo> { workbench };
            job.count = 1;
            if (!worker.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                rejection = "HD_ModularArmor_CannotStartJob".Translate();
                return false;
            }

            return true;
        }

        private Thing FindAvailableRequiredItem(Pawn worker, ThingDef itemDef)
        {
            Thing inventoryItem = worker.inventory?.innerContainer
                ?.FirstOrDefault(thing => thing?.def == itemDef);
            if (inventoryItem != null)
            {
                return inventoryItem;
            }

            Map map = worker.Map;
            return map?.listerThings.ThingsOfDef(itemDef)
                ?.Where(thing => thing?.Spawned == true
                    && !thing.Destroyed
                    && !thing.IsForbidden(worker)
                    && worker.CanReserveAndReach(
                        thing,
                        PathEndMode.ClosestTouch,
                        Danger.Some))
                .OrderBy(thing => worker.Position.DistanceToSquared(thing.Position))
                .FirstOrDefault();
        }

        public ModularArmorPartDef PartForInstallJob(LocalTargetInfo installData)
        {
            ushort shortHash = (ushort)Mathf.Clamp(installData.Cell.y, 0, ushort.MaxValue);
            return DefDatabase<ModularArmorPartDef>.AllDefsListForReading
                .FirstOrDefault(part => part.shortHash == shortHash
                    && IsPartCompatible(part));
        }

        public bool CompleteInstallJob(Thing suppliedItem, LocalTargetInfo installData)
        {
            ModularArmorPartDef part = PartForInstallJob(installData);
            if (part == null || suppliedItem?.def != part.RequiredThingDef)
            {
                return false;
            }

            int x = installData.Cell.x;
            int packedZ = installData.Cell.z;
            if (x < 0 || packedZ < 0)
            {
                return SetPart(part.slot, part, part.DefaultPosition, suppliedItem);
            }

            int panelIndex = packedZ / 100;
            int y = packedZ % 100;
            if (Props.palsPanels.NullOrEmpty()
                || panelIndex < 0
                || panelIndex >= Props.palsPanels.Count)
            {
                return false;
            }

            return InstallPalsPart(
                part,
                Props.palsPanels[panelIndex],
                x,
                y,
                suppliedItem) != null;
        }

        public int InstallWorkTicks(LocalTargetInfo installData)
        {
            return Mathf.Max(1, PartForInstallJob(installData)?.installWorkTicks ?? 240);
        }

        public bool TryStartFrontRearSwap(out string rejection, Thing workbench = null)
        {
            rejection = null;
            if (!CompClothingWorkbench.CanUse(workbench, this))
            {
                rejection = "HD_ClothingBench_Required".Translate();
                return false;
            }
            if (!InstalledParts.Any(record => record?.CanSwapFrontRearPlates == true))
            {
                rejection = "HD_ModularArmor_NoSwappablePlate".Translate();
                return false;
            }

            if (DebugSettings.godMode)
            {
                return CompleteFrontRearSwap();
            }

            Pawn worker = Wearer;
            if (worker?.Spawned != true || worker.Faction != Faction.OfPlayer)
            {
                rejection = "HD_ModularArmor_MustBeWorn".Translate();
                return false;
            }

            if (!CompClothingWorkbench.CanWorkAt(worker, workbench, this))
            {
                rejection = "HD_ClothingBench_Unreachable".Translate();
                return false;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(
                "HD_SwapModularArmorFrontRear");
            if (jobDef == null)
            {
                rejection = "HD_ModularArmor_JobMissing".Translate();
                return false;
            }

            return worker.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(jobDef, parent, workbench),
                JobTag.Misc);
        }

        public bool CompleteFrontRearSwap()
        {
            InstalledModularArmorPart record = InstalledParts
                .FirstOrDefault(installed => installed?.CanSwapFrontRearPlates == true);
            if (record?.SwapFrontRearPlates() != true)
            {
                return false;
            }

            NotifyConfigurationChanged();
            return true;
        }

        private Map EffectiveMap => Wearer?.Map ?? parent.MapHeld;

        private bool TryReturnInstalledItem(InstalledModularArmorPart record)
        {
            if (isPreviewDraft) return true;
            Thing installedItem = record?.InstalledItem;
            if (installedItem == null)
            {
                return true;
            }

            if (Wearer?.inventory?.innerContainer == null && EffectiveMap == null)
            {
                return false;
            }

            ReturnLooseItems(new[] { record.RemoveInstalledItem() });
            return true;
        }

        private void ReturnLooseItems(IEnumerable<Thing> items)
        {
            if (items == null)
            {
                return;
            }

            ThingOwner<Thing> inventory = Wearer?.inventory?.innerContainer;
            Map map = EffectiveMap;
            IntVec3 dropCell = Wearer?.PositionHeld ?? parent.PositionHeld;
            foreach (Thing item in items)
            {
                if (item == null || item.Destroyed)
                {
                    continue;
                }

                if (inventory != null && inventory.TryAdd(item))
                {
                    continue;
                }

                if (map != null)
                {
                    GenPlace.TryPlaceThing(item, dropCell, map, ThingPlaceMode.Near);
                }
            }
        }

        public IEnumerable<InstalledModularArmorPart> PartsOnPanel(
            ModularArmorPalsPanelDef panel)
        {
            EnsureConfiguration();
            return installedParts.Where(record => record?.IsPalsMounted == true
                && record.palsPanel == panel);
        }

        public InstalledModularArmorPart PalsPanelBlocker(
            ModularArmorPalsPanelDef panel,
            InstalledModularArmorPart ignore = null)
        {
            EnsureConfiguration();
            return installedParts.FirstOrDefault(record => record != null
                && record != ignore
                && !record.IsPalsMounted
                && record.part?.blockedPalsPanels?.Contains(panel) == true);
        }

        public bool IsPalsPanelAvailable(ModularArmorPalsPanelDef panel)
        {
            if (panel == null
                || Props.palsPanels.NullOrEmpty()
                || !Props.palsPanels.Contains(panel))
            {
                return false;
            }

            if (panel.requiredPart == null)
            {
                return true;
            }

            EnsureConfiguration();
            return installedParts.Any(record => record != null
                && !record.IsPalsMounted
                && record.part == panel.requiredPart);
        }

        private bool RemovePalsPartsBlockedBy(ModularArmorPartDef part)
        {
            if (part?.blockedPalsPanels.NullOrEmpty() != false)
            {
                return true;
            }

            List<InstalledModularArmorPart> blocked = installedParts
                .Where(record => record?.IsPalsMounted == true
                    && part.blockedPalsPanels.Contains(record.palsPanel))
                .ToList();
            for (int i = 0; i < blocked.Count; i++)
            {
                if (!TryReturnInstalledItem(blocked[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < blocked.Count; i++)
            {
                installedParts.Remove(blocked[i]);
            }

            return true;
        }

        private bool RemovePalsPartsRequiring(ModularArmorPartDef part)
        {
            if (part == null || Props.palsPanels.NullOrEmpty())
            {
                return true;
            }

            HashSet<ModularArmorPalsPanelDef> dependentPanels =
                new HashSet<ModularArmorPalsPanelDef>(Props.palsPanels
                    .Where(panel => panel?.requiredPart == part));
            if (dependentPanels.Count == 0)
            {
                return true;
            }

            List<InstalledModularArmorPart> dependentParts = installedParts
                .Where(record => record?.IsPalsMounted == true
                    && dependentPanels.Contains(record.palsPanel))
                .ToList();
            for (int i = 0; i < dependentParts.Count; i++)
            {
                if (!TryReturnInstalledItem(dependentParts[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < dependentParts.Count; i++)
            {
                installedParts.Remove(dependentParts[i]);
            }

            return true;
        }

        public bool CanPlacePalsPart(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x,
            int y,
            InstalledModularArmorPart ignore = null)
        {
            int partWidth = part?.PalsWidthFor(panel) ?? 0;
            if (part?.installMode != ModularArmorInstallMode.Positionable
                || panel == null
                || !IsPartCompatible(part)
                || !IsPalsPanelAvailable(panel)
                || ConflictingInstalledPart(part, ignore) != null
                || PalsPanelBlocker(panel, ignore) != null
                || !part.allowedPalsPanels.Contains(panel)
                || x < 0
                || y < 0
                || x + partWidth > panel.columns
                || y + part.palsHeight > panel.rows
                || !HasAuthoredPalsTexture(part, panel, x))
            {
                return false;
            }

            EnsureConfiguration();
            for (int i = 0; i < installedParts.Count; i++)
            {
                InstalledModularArmorPart other = installedParts[i];
                if (other == null
                    || other == ignore
                    || !other.IsPalsMounted
                    || other.palsPanel != panel)
                {
                    continue;
                }

                if (RectanglesOverlap(
                    x,
                    y,
                    partWidth,
                    part.palsHeight,
                    other.palsX,
                    other.palsY,
                    other.part.PalsWidthFor(other.palsPanel),
                    other.part.palsHeight))
                {
                    return false;
                }
            }

            return true;
        }

        public InstalledModularArmorPart InstallPalsPart(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x,
            int y,
            Thing suppliedItem = null)
        {
            if (!CanPlacePalsPart(part, panel, x, y)
                || !isPreviewDraft && part.RequiredThingDef != null
                    && suppliedItem?.def != part.RequiredThingDef)
            {
                return null;
            }

            InstalledModularArmorPart record = new InstalledModularArmorPart(
                part,
                panel,
                x,
                y);
            if (suppliedItem != null && !record.TryInstallRequiredItem(suppliedItem))
            {
                return null;
            }

            installedParts.Add(record);
            NotifyConfigurationChanged();
            return record;
        }

        public bool MovePalsPart(
            InstalledModularArmorPart record,
            ModularArmorPalsPanelDef panel,
            int x,
            int y)
        {
            EnsureConfiguration();
            if (record == null
                || !installedParts.Contains(record)
                || !record.IsPalsMounted
                || !CanPlacePalsPart(record.part, panel, x, y, record))
            {
                return false;
            }

            record.palsPanel = panel;
            record.palsX = x;
            record.palsY = y;
            NotifyConfigurationChanged();
            return true;
        }

        public bool RemovePalsPart(InstalledModularArmorPart record)
        {
            EnsureConfiguration();
            if (record == null
                || !record.IsPalsMounted
                || !installedParts.Contains(record)
                || !TryReturnInstalledItem(record))
            {
                return false;
            }

            installedParts.Remove(record);
            NotifyConfigurationChanged();
            return true;
        }

        public float MetricFor(ModularArmorMetric metric)
        {
            float value = metric == ModularArmorMetric.Ergonomics
                ? Props.baseErgonomics
                : metric == ModularArmorMetric.Ventilation
                    ? Props.baseVentilation
                    : Props.baseLoadDistribution;

            EnsureConfiguration();
            for (int i = 0; i < installedParts.Count; i++)
            {
                if (installedParts[i]?.part != null)
                {
                    value += installedParts[i].part.MetricFor(metric);
                }
            }

            return value;
        }

        public void ApplyStatConversions(StatDef targetStat, ref float value)
        {
            if (targetStat == null)
            {
                return;
            }

            if (!Props.statConversions.NullOrEmpty())
            {
                for (int i = 0; i < Props.statConversions.Count; i++)
                {
                    ModularArmorStatConversion conversion = Props.statConversions[i];
                    if (conversion?.targetStat != targetStat)
                    {
                        continue;
                    }

                    float converted = (MetricFor(conversion.source) - conversion.referenceValue)
                        * conversion.factor;
                    if (conversion.mode == ModularArmorConversionMode.Factor)
                    {
                        value *= Mathf.Max(0f, 1f + converted);
                    }
                    else
                    {
                        value += converted;
                    }
                }
            }

            EnsureConfiguration();
            for (int partIndex = 0; partIndex < installedParts.Count; partIndex++)
            {
                List<ModularArmorPartStatModifier> modifiers =
                    installedParts[partIndex]?.part?.statModifiers;
                if (modifiers.NullOrEmpty())
                {
                    continue;
                }

                for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
                {
                    ModularArmorPartStatModifier modifier = modifiers[modifierIndex];
                    if (modifier?.stat != targetStat)
                    {
                        continue;
                    }

                    if (modifier.mode == ModularArmorConversionMode.Factor)
                    {
                        value *= Mathf.Max(0f, modifier.value);
                    }
                    else
                    {
                        value += modifier.value;
                    }
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            return null;
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            EnsureConfiguration();
            var protectionGroups = installedParts
                .Where(record => record?.part != null
                    && record.EffectivePosition != null
                    && record.HasFunctionalPlate
                    && (record.part.armorRatingSharp > 0f
                        || record.part.armorRatingBlunt > 0f
                        || record.part.armorRatingHeat > 0f))
                .GroupBy(record => new
                {
                    Sharp = record.part.armorRatingSharp,
                    Blunt = record.part.armorRatingBlunt,
                    Heat = record.part.armorRatingHeat,
                    GuaranteedBlock = record.part.guaranteedBlockPenetration
                })
                .OrderByDescending(group => group.Key.Sharp)
                .ThenByDescending(group => group.Key.Blunt)
                .ThenByDescending(group => group.Key.Heat)
                .ToList();

            int priority = 5600;
            for (int i = 0; i < protectionGroups.Count; i++)
            {
                var group = protectionGroups[i];
                string coverage = string.Join(", ", group
                    .Select(ProtectionCoverage)
                    .Where(text => !text.NullOrEmpty())
                    .Distinct());
                if (coverage.NullOrEmpty())
                {
                    continue;
                }
                string armorValues = "HD_ModularArmor_ProtectionValues".Translate(
                    group.Key.Sharp.ToStringPercent(),
                    group.Key.Blunt.ToStringPercent(),
                    group.Key.Heat.ToStringPercent());

                string protectionDescription = "HD_ModularArmor_ProtectionDesc".Translate(
                    coverage,
                    armorValues).Resolve();
                if (group.Key.GuaranteedBlock > 0f)
                {
                    protectionDescription += "\n" +
                        "HD_ModularArmor_GuaranteedBlock".Translate(
                            group.Key.GuaranteedBlock.ToStringPercent(),
                            0.4f.ToStringPercent()).Resolve();
                }

                yield return new StatDrawEntry(
                    StatCategoryDefOf.Apparel,
                    "HD_ModularArmor_ProtectionArea".Translate(coverage).Resolve(),
                    armorValues,
                    protectionDescription,
                    priority--);
            }
        }

        private static string ProtectionCoverage(InstalledModularArmorPart record)
        {
            ModularArmorPositionDef position = record.EffectivePosition;
            List<string> bodyLabels = new List<string>();
            if (!position.bodyPartGroups.NullOrEmpty())
            {
                bodyLabels.AddRange(position.bodyPartGroups
                    .Where(group => group != null)
                    .Select(group => group.LabelCap.ToString()));
            }
            if (!position.bodyParts.NullOrEmpty())
            {
                bodyLabels.AddRange(position.bodyParts
                    .Where(part => part != null)
                    .Select(part => part.LabelCap.ToString()));
            }

            string bodies = bodyLabels.Count > 0
                ? string.Join("·", bodyLabels.Distinct())
                : position.LabelCap.ToString();
            IEnumerable<ModularArmorFacing> activeDirections =
                position.protectedDirections;
            if (record.part.plateThingDef != null
                && !position.protectedDirections.NullOrEmpty())
            {
                activeDirections = position.protectedDirections
                    .Where(facing => record.PlateForFacing(facing) != null);
            }

            List<ModularArmorFacing> directionList = activeDirections?
                .Distinct()
                .ToList();
            if (record.part.plateThingDef != null && directionList.NullOrEmpty())
            {
                return null;
            }

            string directions = directionList.NullOrEmpty()
                ? "HD_ModularArmor_AllDirections".Translate().ToString()
                : string.Join("·", directionList
                    .OrderBy(DirectionDisplayOrder)
                    .Select(DirectionDisplayLabel));
            return "HD_ModularArmor_CoverageWithDirection".Translate(
                bodies,
                directions);
        }

        private static int DirectionDisplayOrder(ModularArmorFacing facing)
        {
            if (facing == ModularArmorFacing.Front) return 0;
            if (facing == ModularArmorFacing.Back) return 1;
            if (facing == ModularArmorFacing.Left) return 2;
            return 3;
        }

        private static string DirectionDisplayLabel(ModularArmorFacing facing)
        {
            if (facing == ModularArmorFacing.Front)
                return "HD_ModularArmor_Facing_Front".Translate();
            if (facing == ModularArmorFacing.Back)
                return "HD_ModularArmor_Facing_Back".Translate();
            if (facing == ModularArmorFacing.Left)
                return "HD_ModularArmor_Facing_Left".Translate();
            return "HD_ModularArmor_Facing_Right".Translate();
        }

        public override List<PawnRenderNode> CompRenderNodes()
        {
            return RenderNodes(Wearer?.Drawer?.renderer?.renderTree);
        }

        public string AuthoredPalsTexturePath(
            InstalledModularArmorPart record,
            bool sideBack,
            bool west = false)
        {
            return AuthoredPalsTexturePath(
                record,
                sideBack,
                west ? Rot4.West : Rot4.East);
        }

        public string AuthoredPalsTexturePath(
            InstalledModularArmorPart record,
            bool backLayer,
            Rot4 facing)
        {
            ModularArmorPartDef part = record?.part;
            ModularArmorPalsPanelDef panel = record?.palsPanel;
            if (part?.authoredPalsTextureKey.NullOrEmpty() != false || panel == null)
            {
                return null;
            }

            if (facing == Rot4.East || facing == Rot4.West)
            {
                if (backLayer)
                {
                    if (panel.sideBackTexturePrefix.NullOrEmpty()
                        || !IsFrontmostSideRecord(record))
                    {
                        return null;
                    }

                    return AuthoredPalsTexturePrefix(part, panel.sideBackTexturePrefix)
                        + "_" + part.authoredPalsTextureKey + "_east";
                }

                int textureX = record.palsX;
                if (facing == Rot4.West)
                {
                    textureX = panel.columns
                        - textureX
                        - part.PalsWidthFor(panel);
                }

                return AuthoredPalsTexturePath(
                    part,
                    panel,
                    textureX,
                    panel.authoredTexturePrefix,
                    "east");
            }

            string normalPrefix = facing == Rot4.North
                ? panel.northTexturePrefix
                : panel.southTexturePrefix;
            string backPrefix = facing == Rot4.North
                ? panel.northBackTexturePrefix
                : panel.southBackTexturePrefix;
            string suffix = facing == Rot4.North ? "north" : "south";
            return AuthoredSegmentedPalsTexturePath(
                part,
                panel,
                record.palsX,
                normalPrefix,
                backPrefix,
                suffix,
                backLayer);
        }

        private static string AuthoredSegmentedPalsTexturePath(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x,
            string normalPrefix,
            string backPrefix,
            string suffix,
            bool backLayer)
        {
            if (normalPrefix.NullOrEmpty())
            {
                return null;
            }

            if (backPrefix.NullOrEmpty())
            {
                return backLayer
                    ? null
                    : AuthoredPalsTexturePath(
                        part,
                        panel,
                        x,
                        normalPrefix,
                        suffix);
            }

            int width = part.PalsWidthFor(panel);
            int totalPositions = panel.columns - width + 1;
            int normalPositions = Mathf.Min(
                totalPositions,
                panel.flatNormalPositionCount > 0
                    ? panel.flatNormalPositionCount
                    : Mathf.Max(1, totalPositions / 2));
            bool usesBack = x >= normalPositions;
            if (usesBack != backLayer)
            {
                return null;
            }

            int localX = usesBack ? x - normalPositions : x;
            int segmentPositions = usesBack
                ? totalPositions - normalPositions
                : normalPositions;
            string prefix = usesBack ? backPrefix : normalPrefix;
            return AuthoredPalsTexturePrefix(part, prefix)
                + "_" + part.authoredPalsTextureKey
                + (segmentPositions > 1 ? "_" + (localX + 1) : string.Empty)
                + "_" + suffix;
        }

        private static string AuthoredPalsTexturePath(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x,
            string prefix = null,
            string suffix = "east")
        {
            if (part?.authoredPalsTextureKey.NullOrEmpty() != false
                || panel == null
                || (prefix ?? panel.authoredTexturePrefix).NullOrEmpty()
                || x < 0)
            {
                return null;
            }

            int width = part.PalsWidthFor(panel);
            bool usesPositionSuffix = panel.columns > width;
            return AuthoredPalsTexturePrefix(part, prefix ?? panel.authoredTexturePrefix)
                + "_" + part.authoredPalsTextureKey
                + (usesPositionSuffix ? "_" + (x + 1) : string.Empty)
                + "_" + suffix;
        }

        private static string AuthoredPalsTexturePrefix(ModularArmorPartDef part, string prefix)
        {
            // Keep each individual PNG under part/mounting-position folders
            // within the existing armor/panel directory.
            if (prefix.NullOrEmpty() || part?.authoredPalsTextureKey.NullOrEmpty() != false)
                return prefix;
            int separator = prefix.LastIndexOf('/');
            string filePrefix = prefix.Substring(separator + 1);
            string position = filePrefix.Substring(filePrefix.LastIndexOf('_') + 1);
            return prefix.Substring(0, separator + 1)
                + part.authoredPalsTextureKey + "/" + position + "/" + filePrefix;
        }

        private static bool HasAuthoredPalsTexture(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x)
        {
            if (panel == null)
            {
                return false;
            }

            bool configured = false;
            if (!panel.authoredTexturePrefix.NullOrEmpty())
            {
                configured = true;
                string eastPath = AuthoredPalsTexturePath(part, panel, x);
                if (eastPath.NullOrEmpty()
                    || ContentFinder<Texture2D>.Get(eastPath, false) == null)
                {
                    return false;
                }
            }

            Rot4[] flatFacings = { Rot4.North, Rot4.South };
            for (int i = 0; i < flatFacings.Length; i++)
            {
                Rot4 facing = flatFacings[i];
                string normalPrefix = facing == Rot4.North
                    ? panel.northTexturePrefix
                    : panel.southTexturePrefix;
                if (normalPrefix.NullOrEmpty())
                {
                    continue;
                }

                configured = true;
                string backPrefix = facing == Rot4.North
                    ? panel.northBackTexturePrefix
                    : panel.southBackTexturePrefix;
                string path = AuthoredSegmentedPalsTexturePath(
                    part,
                    panel,
                    x,
                    normalPrefix,
                    backPrefix,
                    facing == Rot4.North ? "north" : "south",
                    !backPrefix.NullOrEmpty()
                        && x >= Mathf.Min(
                            panel.columns - part.PalsWidthFor(panel) + 1,
                            panel.flatNormalPositionCount > 0
                                ? panel.flatNormalPositionCount
                                : Mathf.Max(1,
                                    (panel.columns - part.PalsWidthFor(panel) + 1) / 2)));
                if (path.NullOrEmpty()
                    || ContentFinder<Texture2D>.Get(path, false) == null)
                {
                    return false;
                }
            }

            return !configured || part.authoredPalsTextureKey.NullOrEmpty() == false;
        }

        private bool IsFrontmostSideRecord(InstalledModularArmorPart record)
        {
            if (record?.palsPanel?.sideBackTexturePrefix.NullOrEmpty() != false)
            {
                return false;
            }

            return PartsOnPanel(record.palsPanel)
                .Where(other => other?.part?.authoredPalsTextureKey.NullOrEmpty() == false)
                .OrderBy(other => other.palsX)
                .ThenBy(other => other.palsY)
                .FirstOrDefault() == record;
        }

        private Graphic AuthoredPalsGraphic(
            InstalledModularArmorPart record,
            bool sideBack,
            Rot4 facing)
        {
            string path = AuthoredPalsTexturePath(record, sideBack, facing);
            if (path.NullOrEmpty()
                || ContentFinder<Texture2D>.Get(path, false) == null)
            {
                return null;
            }

            return GraphicDatabase.Get<Graphic_Single>(
                path,
                ShaderDatabase.Cutout,
                Vector2.one,
                Color.white);
        }

        public bool PalsPartVisibleForFacing(
            InstalledModularArmorPart record,
            Rot4 facing)
        {
            if (record?.part == null)
            {
                return false;
            }

            ModularArmorFacing modularFacing;
            if (facing == Rot4.North) modularFacing = ModularArmorFacing.Back;
            else if (facing == Rot4.East) modularFacing = ModularArmorFacing.Right;
            else if (facing == Rot4.West) modularFacing = ModularArmorFacing.Left;
            else modularFacing = ModularArmorFacing.Front;

            if (!record.part.renderDirections.NullOrEmpty()
                && !record.part.renderDirections.Contains(modularFacing))
            {
                return false;
            }

            if (record.IsPalsMounted != true)
            {
                return true;
            }

            return IsPalsPanelAvailable(record.palsPanel)
                && (record.palsPanel.renderDirections.NullOrEmpty()
                    || record.palsPanel.renderDirections.Contains(modularFacing));
        }

        public float DrawLayerFor(InstalledModularArmorPart record)
        {
            if (record?.part == null || record.EffectivePosition == null)
            {
                return 0f;
            }

            if (record.part.drawBehindArmor)
            {
                return -1f;
            }

            float result = record.part.drawLayer + record.EffectivePosition.drawLayer;
            if (record.IsPalsMounted && record.palsPanel != null)
            {
                result += record.palsPanel.drawLayer
                    + record.palsPanel.positionDrawLayerStep * record.palsX;
            }

            return result;
        }

        public List<PawnRenderNode> RenderNodes(PawnRenderTree tree)
        {
            Pawn wearer = Wearer;
            if (wearer == null || tree == null)
            {
                return null;
            }

            EnsureConfiguration();
            List<PawnRenderNode> nodes = new List<PawnRenderNode>();
            for (int i = 0; i < installedParts.Count; i++)
            {
                InstalledModularArmorPart record = installedParts[i];
                if (record?.part == null || record.EffectivePosition == null)
                {
                    continue;
                }

                Graphic authoredEastGraphic = AuthoredPalsGraphic(
                    record, false, Rot4.East);
                Graphic authoredWestGraphic = AuthoredPalsGraphic(
                    record, false, Rot4.West);
                Graphic authoredNorthGraphic = AuthoredPalsGraphic(
                    record, false, Rot4.North);
                Graphic authoredSouthGraphic = AuthoredPalsGraphic(
                    record, false, Rot4.South);
                if (authoredEastGraphic != null
                    || authoredWestGraphic != null
                    || authoredNorthGraphic != null
                    || authoredSouthGraphic != null)
                {
                    Graphic sideBackEast = AuthoredPalsGraphic(
                        record, true, Rot4.East);
                    Graphic sideBackWest = AuthoredPalsGraphic(
                        record, true, Rot4.West);
                    Graphic sideBackNorth = AuthoredPalsGraphic(
                        record, true, Rot4.North);
                    Graphic sideBackSouth = AuthoredPalsGraphic(
                        record, true, Rot4.South);
                    if (sideBackEast != null
                        || sideBackWest != null
                        || sideBackNorth != null
                        || sideBackSouth != null)
                    {
                        nodes.Add(new PawnRenderNode_ModularArmorPart(
                            wearer,
                            tree,
                            this,
                            record,
                            false,
                            sideBackEast,
                            sideBackWest,
                            sideBackNorth,
                            sideBackSouth,
                            true));
                    }

                    nodes.Add(new PawnRenderNode_ModularArmorPart(
                        wearer,
                        tree,
                        this,
                        record,
                        false,
                        authoredEastGraphic,
                        authoredWestGraphic,
                        authoredNorthGraphic,
                        authoredSouthGraphic));
                    continue;
                }

                if (record.part.graphicData == null
                    || record.part.graphicData.texPath.NullOrEmpty())
                {
                    continue;
                }

                if (record.part.northUnderGraphicData != null
                    && !record.part.northUnderGraphicData.texPath.NullOrEmpty())
                {
                    nodes.Add(new PawnRenderNode_ModularArmorPart(
                        wearer,
                        tree,
                        this,
                        record,
                        true));
                }

                nodes.Add(new PawnRenderNode_ModularArmorPart(
                    wearer,
                    tree,
                    this,
                    record));
            }

            return nodes;
        }

        private void EnsureConfiguration()
        {
            if (installedParts != null)
            {
                return;
            }

            installedParts = new List<InstalledModularArmorPart>();
            if (Props.defaultParts.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < Props.defaultParts.Count; i++)
            {
                ModularArmorDefaultPart entry = Props.defaultParts[i];
                if (entry?.part == null || entry.slot == null)
                {
                    continue;
                }

                ModularArmorPositionDef position = entry.position ?? entry.part.DefaultPosition;
                if (entry.part.slot == entry.slot
                    && IsPartCompatible(entry.part)
                    && ConflictingInstalledPart(entry.part) == null
                    && entry.part.AllowsPosition(position)
                    && installedParts.All(record => record.slot != entry.slot))
                {
                    installedParts.Add(new InstalledModularArmorPart(
                        entry.slot,
                        entry.part,
                        position));
                }
            }
        }

        private void ValidateConfiguration()
        {
            installedParts = installedParts ?? new List<InstalledModularArmorPart>();
            NormalizeSingleRowPalsPlacements();
            HashSet<ModularArmorSlotDef> occupied = new HashSet<ModularArmorSlotDef>();
            installedParts.RemoveAll(record => record == null
                || record.part == null
                || !IsPartCompatible(record.part)
                || (record.IsPalsMounted
                    ? record.palsPanel == null
                        || !Props.palsPanels.Contains(record.palsPanel)
                        || !CanPlacePalsPart(
                            record.part,
                            record.palsPanel,
                            record.palsX,
                            record.palsY,
                            record)
                    : record.slot == null
                        || occupied.Contains(record.slot)
                        || record.part.slot != record.slot
                        || !Props.slots.Contains(record.slot)
                        || !record.part.AllowsPosition(record.position)
                        || !occupied.Add(record.slot)));

            HashSet<string> activeConflictTags = new HashSet<string>();
            installedParts.RemoveAll(record =>
            {
                if (record?.part?.conflictTags.NullOrEmpty() != false)
                {
                    return false;
                }

                if (record.part.conflictTags.Any(activeConflictTags.Contains))
                {
                    return true;
                }

                activeConflictTags.UnionWith(record.part.conflictTags);
                return false;
            });

            occupied.Clear();
            for (int i = 0; i < installedParts.Count; i++)
            {
                if (!installedParts[i].IsPalsMounted)
                {
                    occupied.Add(installedParts[i].slot);
                }

            }

            if (!Props.defaultParts.NullOrEmpty())
            {
                for (int i = 0; i < Props.defaultParts.Count; i++)
                {
                    ModularArmorDefaultPart entry = Props.defaultParts[i];
                    if (entry?.slot?.required == true && !occupied.Contains(entry.slot))
                    {
                        ModularArmorPositionDef position = entry.position ?? entry.part?.DefaultPosition;
                        if (entry.part != null
                            && entry.part.slot == entry.slot
                            && IsPartCompatible(entry.part)
                            && ConflictingInstalledPart(entry.part) == null
                            && entry.part.AllowsPosition(position))
                        {
                            installedParts.Add(new InstalledModularArmorPart(
                                entry.slot,
                                entry.part,
                                position));
                            occupied.Add(entry.slot);
                        }
                    }
                }
            }
        }

        private void NormalizeSingleRowPalsPlacements()
        {
            if (Props.palsPanels.NullOrEmpty() || installedParts.NullOrEmpty())
            {
                return;
            }

            List<InstalledModularArmorPart> rejected = new List<InstalledModularArmorPart>();
            for (int panelIndex = 0; panelIndex < Props.palsPanels.Count; panelIndex++)
            {
                ModularArmorPalsPanelDef panel = Props.palsPanels[panelIndex];
                if (panel?.rows != 1)
                {
                    continue;
                }

                List<InstalledModularArmorPart> accepted = new List<InstalledModularArmorPart>();
                List<InstalledModularArmorPart> records = installedParts
                    .Where(record => record?.IsPalsMounted == true
                        && record.palsPanel == panel)
                    .OrderBy(record => record.palsX)
                    .ThenBy(record => record.palsY)
                    .ToList();
                for (int i = 0; i < records.Count; i++)
                {
                    InstalledModularArmorPart record = records[i];
                    if (record.part?.allowedPalsPanels?.Contains(panel) != true)
                    {
                        rejected.Add(record);
                        continue;
                    }

                    int width = record.part.PalsWidthFor(panel);
                    int maximumX = panel.columns - width;
                    int preferredX = Mathf.Clamp(record.palsX, 0, Mathf.Max(0, maximumX));
                    int chosenX = -1;
                    for (int offset = 0; offset <= Mathf.Max(0, maximumX) + 1; offset++)
                    {
                        int candidateX = offset == 0 ? preferredX : offset - 1;
                        if (candidateX < 0 || candidateX > maximumX)
                        {
                            continue;
                        }

                        if (!HasAuthoredPalsTexture(record.part, panel, candidateX))
                        {
                            continue;
                        }

                        bool overlaps = accepted.Any(other => RectanglesOverlap(
                            candidateX,
                            0,
                            width,
                            1,
                            other.palsX,
                            0,
                            other.part.PalsWidthFor(panel),
                            1));
                        if (!overlaps)
                        {
                            chosenX = candidateX;
                            break;
                        }
                    }

                    if (chosenX < 0)
                    {
                        rejected.Add(record);
                        continue;
                    }

                    record.palsX = chosenX;
                    record.palsY = 0;
                    accepted.Add(record);
                }
            }

            for (int i = 0; i < rejected.Count; i++)
            {
                InstalledModularArmorPart record = rejected[i];
                if (TryReturnInstalledItem(record))
                {
                    installedParts.Remove(record);
                }
            }
        }

        public void NotifyConfigurationChanged()
        {
            if (isPreviewDraft) return;
            Wearer?.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        private static bool RectanglesOverlap(
            int x1,
            int y1,
            int width1,
            int height1,
            int x2,
            int y2,
            int width2,
            int height2)
        {
            return x1 < x2 + width2
                && x1 + width1 > x2
                && y1 < y2 + height2
                && y1 + height1 > y2;
        }
    }

    public sealed class StatWorker_ModularArmorMetric : StatWorker
    {
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CompModularArmor comp = req.Thing?.TryGetComp<CompModularArmor>();
            if (comp == null)
            {
                return base.GetValueUnfinalized(req, applyPostProcess);
            }

            if (stat.defName == "HD_ArmorErgonomics")
            {
                return comp.MetricFor(ModularArmorMetric.Ergonomics);
            }

            if (stat.defName == "HD_ArmorVentilation")
            {
                return comp.MetricFor(ModularArmorMetric.Ventilation);
            }

            return comp.MetricFor(ModularArmorMetric.LoadDistribution);
        }
    }

}
