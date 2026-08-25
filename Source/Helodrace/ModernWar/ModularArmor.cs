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

    public enum ModularArmorSlotSide
    {
        Auto,
        Left,
        Right,
        Top,
        Bottom
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
        public ModularArmorSlotSide uiSide;
    }

    public sealed class ModularArmorDrawOffsets
    {
        public Vector3 north;
        public Vector3 east;
        public Vector3 south;
        public Vector3 west;

        public Vector3 For(Rot4 facing)
        {
            if (facing == Rot4.North) return north;
            if (facing == Rot4.East) return east;
            if (facing == Rot4.West) return west;
            return south;
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
        public float uiX = 0.2f;
        public float uiY = 0.45f;
        public float uiWidth = 0.6f;
        public float uiHeight = 0.35f;
        public ModularArmorDrawOffsets drawOrigin;
        public Vector2 drawCellSize = new Vector2(0.08f, 0.08f);
        public float drawLayer;

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
        }
    }

    public sealed class ModularArmorPartDef : Def
    {
        public ModularArmorSlotDef slot;
        public ModularArmorInstallMode installMode;
        public ModularArmorPositionDef fixedPosition;
        public List<ModularArmorPalsPanelDef> allowedPalsPanels;
        public int palsWidth = 1;
        public int palsHeight = 1;
        public List<string> compatibleArmorTags;
        public List<string> conflictTags;
        public ThingDef plateThingDef;
        public ThingDef partThingDef;
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
        public GraphicData northUnderGraphicData;
        public PawnRenderNodeTagDef northUnderParentTagDef;
        public float northUnderDrawLayer = 79f;
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

        public Rect Rect => new Rect(x, y, width, height);
    }

    public sealed class CompProperties_ArmorPlateSet : CompProperties
    {
        public CompProperties_ArmorPlateSet()
        {
            compClass = typeof(CompArmorPlateSet);
        }
    }

    public sealed class CompArmorPlateSet : ThingComp
    {
        private const int CurrentDurabilityVersion = 1;
        private int plateOneHitPoints = -1;
        private int plateTwoHitPoints = -1;
        private int durabilityVersion;

        public int MaxHitPointsPerPlate => parent.MaxHitPoints;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureInitialized();
            durabilityVersion = CurrentDurabilityVersion;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref plateOneHitPoints, "plateOneHitPoints", -1);
            Scribe_Values.Look(ref plateTwoHitPoints, "plateTwoHitPoints", -1);
            Scribe_Values.Look(ref durabilityVersion, "durabilityVersion", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                UpgradeDurabilityIfNeeded();
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

        public void SetLegacyHitPoints(int plateOne, int plateTwo)
        {
            plateOneHitPoints = Mathf.Clamp(plateOne, 0, MaxHitPointsPerPlate);
            plateTwoHitPoints = Mathf.Clamp(plateTwo, 0, MaxHitPointsPerPlate);
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

        private void UpgradeDurabilityIfNeeded()
        {
            if (durabilityVersion >= CurrentDurabilityVersion)
            {
                return;
            }

            if (plateOneHitPoints >= 0)
            {
                plateOneHitPoints = Mathf.CeilToInt(plateOneHitPoints / 5f);
            }

            if (plateTwoHitPoints >= 0)
            {
                plateTwoHitPoints = Mathf.CeilToInt(plateTwoHitPoints / 5f);
            }

            durabilityVersion = CurrentDurabilityVersion;
        }
    }

    // Kept only to migrate saves made by the earlier one-item-per-direction model.
    public sealed class DirectionalArmorPlateState : IExposable, IThingHolder
    {
        public ModularArmorFacing facing;
        private ThingOwner<Thing> plateContainer;

        public IThingHolder ParentHolder => null;

        public Thing Plate
        {
            get
            {
                EnsureContainer();
                return plateContainer.Count > 0 ? plateContainer[0] : null;
            }
        }

        public DirectionalArmorPlateState()
        {
            EnsureContainer();
        }

        public DirectionalArmorPlateState(ModularArmorFacing facing)
        {
            this.facing = facing;
            EnsureContainer();
        }

        public bool TryInstallPlate(Thing plate)
        {
            EnsureContainer();
            return plate != null
                && Plate == null
                && plateContainer.TryAdd(plate, false);
        }

        public Thing RemovePlate()
        {
            EnsureContainer();
            Thing plate = Plate;
            return plate == null
                ? null
                : plateContainer.Take(plate, plate.stackCount);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref facing, "facing", ModularArmorFacing.Front);
            EnsureContainer();
            plateContainer.ExposeData();
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureContainer();
            return plateContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsureContainer();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, plateContainer);
        }

        private void EnsureContainer()
        {
            if (plateContainer == null)
            {
                plateContainer = new ThingOwner<Thing>(this, false, LookMode.Deep);
            }
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
        public List<DirectionalArmorPlateState> directionalPlates;
        private ThingOwner<Thing> plateSetContainer;
        public bool plateOrderSwapped;

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
            Scribe_Collections.Look(
                ref directionalPlates,
                "directionalPlates",
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MigrateDirectionalPlates();
            }
        }

        public Thing PlateSet
        {
            get
            {
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

        private void MigrateDirectionalPlates()
        {
            EnsurePlateSetContainer();
            if (PlateSet != null || directionalPlates.NullOrEmpty())
            {
                return;
            }

            List<ModularArmorFacing> directions = EffectivePosition?
                .protectedDirections?
                .Distinct()
                .ToList() ?? new List<ModularArmorFacing>();
            Thing firstPlate = null;
            Thing secondPlate = null;
            if (directions.Count > 0)
            {
                firstPlate = directionalPlates
                    .FirstOrDefault(state => state?.facing == directions[0])
                    ?.RemovePlate();
            }

            if (directions.Count > 1)
            {
                secondPlate = directionalPlates
                    .FirstOrDefault(state => state?.facing == directions[1])
                    ?.RemovePlate();
            }

            Thing setItem = firstPlate ?? secondPlate;
            if (setItem != null && TryInstallPlateSet(setItem))
            {
                CompArmorPlateSet plateComp = setItem.TryGetComp<CompArmorPlateSet>();
                plateComp?.SetLegacyHitPoints(
                    firstPlate?.HitPoints ?? 0,
                    secondPlate?.HitPoints ?? 0);
            }

            Thing duplicate = setItem == firstPlate ? secondPlate : firstPlate;
            duplicate?.Destroy(DestroyMode.Vanish);
            for (int i = 0; i < directionalPlates.Count; i++)
            {
                directionalPlates[i]?.GetDirectlyHeldThings()
                    ?.ClearAndDestroyContents();
            }

            directionalPlates = null;
        }
    }

    public sealed class CompModularArmor : ThingComp
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureConfiguration();
                ValidateConfiguration();
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
                && suppliedItem?.def != requiredThingDef)
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
            out string rejection)
        {
            rejection = null;
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

            if (part.RequiredThingDef == null)
            {
                return SetPart(slot, part, position);
            }

            return TryStartInstallJob(part, position, null, 0, 0, out rejection);
        }

        public bool TryStartPalsPartInstall(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x,
            int y,
            out string rejection)
        {
            rejection = null;
            if (!CanPlacePalsPart(part, panel, x, y))
            {
                rejection = "HD_ModularArmor_InvalidInstall".Translate();
                return false;
            }

            if (part.RequiredThingDef == null)
            {
                return InstallPalsPart(part, panel, x, y) != null;
            }

            return TryStartInstallJob(part, null, panel, x, y, out rejection);
        }

        private bool TryStartInstallJob(
            ModularArmorPartDef part,
            ModularArmorPositionDef position,
            ModularArmorPalsPanelDef panel,
            int x,
            int y,
            out string rejection)
        {
            rejection = null;
            Pawn worker = Wearer;
            if (worker?.Spawned != true || worker.Faction != Faction.OfPlayer)
            {
                rejection = "HD_ModularArmor_MustBeWorn".Translate();
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

        public bool TryStartFrontRearSwap(out string rejection)
        {
            rejection = null;
            Pawn worker = Wearer;
            if (worker?.Spawned != true || worker.Faction != Faction.OfPlayer)
            {
                rejection = "HD_ModularArmor_MustBeWorn".Translate();
                return false;
            }

            if (!InstalledParts.Any(record => record?.CanSwapFrontRearPlates == true))
            {
                rejection = "HD_ModularArmor_NoSwappablePlate".Translate();
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
                JobMaker.MakeJob(jobDef, parent),
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

        public bool CanPlacePalsPart(
            ModularArmorPartDef part,
            ModularArmorPalsPanelDef panel,
            int x,
            int y,
            InstalledModularArmorPart ignore = null)
        {
            if (part?.installMode != ModularArmorInstallMode.Positionable
                || panel == null
                || !IsPartCompatible(part)
                || ConflictingInstalledPart(part, ignore) != null
                || !part.allowedPalsPanels.Contains(panel)
                || x < 0
                || y < 0
                || x + part.palsWidth > panel.columns
                || y + part.palsHeight > panel.rows)
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
                    part.palsWidth,
                    part.palsHeight,
                    other.palsX,
                    other.palsY,
                    other.part.palsWidth,
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
                || part.RequiredThingDef != null
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

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Command_Action command = ConfigurationCommand(false);
            if (command != null)
            {
                yield return command;
            }
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            Command_Action command = ConfigurationCommand(true);
            if (command != null)
            {
                yield return command;
            }

            Command_Action swapCommand = FrontRearSwapCommand();
            if (swapCommand != null)
            {
                yield return swapCommand;
            }
        }

        public override List<PawnRenderNode> CompRenderNodes()
        {
            return RenderNodes(Wearer?.Drawer?.renderer?.renderTree);
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
                if (record?.part?.graphicData == null
                    || record.EffectivePosition == null
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

        private Command_Action ConfigurationCommand(bool worn)
        {
            if (!Props.allowPlayerConfiguration
                || Props.slots.NullOrEmpty() && Props.palsPanels.NullOrEmpty())
            {
                return null;
            }

            if (worn && Wearer?.Faction != Faction.OfPlayer)
            {
                return null;
            }

            return new Command_Action
            {
                defaultLabel = "HD_ModularArmor_Configure_Label".Translate(),
                defaultDesc = "HD_ModularArmor_Configure_Desc".Translate(),
                icon = parent.def.uiIcon,
                action = () => Find.WindowStack.Add(new Dialog_ModularArmor(this))
            };
        }

        private Command_Action FrontRearSwapCommand()
        {
            Pawn wearer = Wearer;
            if (wearer?.Faction != Faction.OfPlayer)
            {
                return null;
            }

            InstalledModularArmorPart record = InstalledParts
                .FirstOrDefault(installed => installed?.CanSwapFrontRearPlates == true);
            if (record == null)
            {
                return null;
            }

            return new Command_Action
            {
                defaultLabel = "HD_ModularArmor_SwapFrontRear".Translate(),
                defaultDesc = "HD_ModularArmor_SwapFrontRearTip".Translate(
                    record.PlateNumberForFacing(ModularArmorFacing.Front),
                    record.PlateNumberForFacing(ModularArmorFacing.Back)),
                icon = parent.def.uiIcon,
                action = delegate
                {
                    if (!TryStartFrontRearSwap(out string rejection)
                        && !rejection.NullOrEmpty())
                    {
                        Messages.Message(rejection, MessageTypeDefOf.RejectInput);
                    }
                }
            };
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

        public void NotifyConfigurationChanged()
        {
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

    public sealed class Dialog_ModularArmor : Window
    {
        private readonly CompModularArmor comp;
        private ModularArmorPartDef selectedPalsPart;
        private InstalledModularArmorPart selectedInstalledPart;
        private Rot4 previewFacing;

        public Dialog_ModularArmor(CompModularArmor comp)
        {
            this.comp = comp;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            previewFacing = comp.Wearer != null
                ? comp.Wearer.Rotation
                : Rot4.South;
        }

        public override Vector2 InitialSize => new Vector2(1280f, 820f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f),
                "HD_ModularArmor_WindowTitle".Translate(comp.parent.LabelCap));
            Text.Font = GameFont.Small;

            Rect previewRect = new Rect(
                inRect.width * 0.5f - 220f,
                120f,
                440f,
                400f);
            List<ModularArmorSlotDef> visibleSlots = VisibleFixedSlots();
            DrawFixedSlotConnections(visibleSlots, previewRect);
            DrawArmorPreview(previewRect, visibleSlots);
            DrawFixedSlots(visibleSlots, previewRect);
            DrawMetrics(new Rect(20f, 500f, 270f, 110f));
            bool hasPals = !comp.Props.palsPanels.NullOrEmpty();
            if (hasPals)
            {
                DrawPalsCatalog(new Rect(0f, 620f, inRect.width, 112f));
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
            Widgets.Label(new Rect(10f, inRect.height - 35f, inRect.width - 150f, 30f),
                instruction);
            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 34f, 120f, 32f),
                "CloseButton".Translate()))
            {
                Close();
            }
        }

        private void DrawArmorPreview(
            Rect rect,
            List<ModularArmorSlotDef> visibleSlots)
        {
            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(0.08f, 0.09f, 0.10f, 0.96f),
                new Color(0.40f, 0.45f, 0.42f),
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
                Widgets.Label(rect.ContractedBy(20f), comp.parent.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            if (!comp.Props.palsPanels.NullOrEmpty())
            {
                for (int i = 0; i < comp.Props.palsPanels.Count; i++)
                {
                    ModularArmorPalsPanelDef panel = comp.Props.palsPanels[i];
                    if (PositionVisibleForFacing(panel.armorPosition))
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

            Texture2D apparelTexture = ApparelPreviewTexture();
            if (apparelTexture != null)
            {
                textures.Add(apparelTexture);
            }

            for (int i = 0; i < installed.Count; i++)
            {
                GraphicData graphicData = installed[i]?.part?.graphicData;
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
            Rect uv = comp.Props.previewCrop?.Rect ?? new Rect(0f, 0f, 1f, 1f);

            float sourceAspect = layers[0].width * uv.width
                / Mathf.Max(1f, layers[0].height * uv.height);
            Rect destination = availableRect;
            float availableAspect = availableRect.width / availableRect.height;
            if (availableAspect > sourceAspect)
            {
                destination.width = availableRect.height * sourceAspect;
                destination.x = availableRect.center.x - destination.width * 0.5f;
            }
            else
            {
                destination.height = availableRect.width / sourceAspect;
                destination.y = availableRect.center.y - destination.height * 0.5f;
            }

            Rect drawUv = previewFacing == Rot4.West
                ? new Rect(uv.xMax, uv.y, -uv.width, uv.height)
                : uv;
            for (int i = 0; i < layers.Count; i++)
            {
                GUI.DrawTextureWithTexCoords(destination, layers[i], drawUv, true);
            }
        }

        private void DrawPreviewFacingControls(
            Rect previewRect,
            List<ModularArmorSlotDef> visibleSlots)
        {
            Rect leftButton = new Rect(previewRect.x + 12f, previewRect.y + 10f, 42f, 30f);
            Rect rightButton = new Rect(previewRect.xMax - 54f, previewRect.y + 10f, 42f, 30f);
            if (Widgets.ButtonText(leftButton, "◀"))
            {
                RotatePreview(1);
            }
            if (Widgets.ButtonText(rightButton, "▶"))
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
                facingLabelRect,
                "HD_ModularArmor_Facing".Translate(FacingLabel()));

            Text.Font = GameFont.Tiny;
            string slotSummary = visibleSlots.Count == 0
                ? "HD_ModularArmor_None".Translate().ToString()
                : string.Join(" · ", visibleSlots.Select(slot => slot.LabelCap.ToString()));
            Widgets.Label(
                new Rect(previewRect.x + 12f, previewRect.yMax - 38f,
                    previewRect.width - 24f, 30f),
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

        private void DrawFixedSlotConnections(
            List<ModularArmorSlotDef> slots,
            Rect previewRect)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                Rect socket = FixedSlotRect(slots, i, previewRect);
                Vector2 start;
                Vector2 end;
                if (socket.xMax <= previewRect.xMin)
                {
                    start = new Vector2(socket.xMax, socket.center.y);
                    end = new Vector2(previewRect.xMin, socket.center.y);
                }
                else if (socket.xMin >= previewRect.xMax)
                {
                    start = new Vector2(socket.xMin, socket.center.y);
                    end = new Vector2(previewRect.xMax, socket.center.y);
                }
                else if (socket.yMax <= previewRect.yMin)
                {
                    start = new Vector2(socket.center.x, socket.yMax);
                    end = new Vector2(socket.center.x, previewRect.yMin);
                }
                else
                {
                    start = new Vector2(socket.center.x, socket.yMin);
                    end = new Vector2(socket.center.x, previewRect.yMax);
                }
                Widgets.DrawLine(start, end, new Color(0.45f, 0.52f, 0.48f), 2f);
            }
        }

        private void DrawFixedSlots(
            List<ModularArmorSlotDef> slots,
            Rect previewRect)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                ModularArmorSlotDef slot = slots[i];
                Rect socket = FixedSlotRect(slots, i, previewRect);
                InstalledModularArmorPart installed = comp.InstalledIn(slot);
                bool hovered = Mouse.IsOver(socket);
                Widgets.DrawBoxSolidWithOutline(
                    socket,
                    hovered
                        ? new Color(0.20f, 0.24f, 0.22f)
                        : new Color(0.12f, 0.14f, 0.13f),
                    slot.required
                        ? new Color(0.72f, 0.62f, 0.28f)
                        : new Color(0.38f, 0.45f, 0.41f),
                    2);

                Text.Anchor = TextAnchor.UpperCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(socket.x + 4f, socket.y + 3f, socket.width - 8f, 20f),
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
                            new Rect(socket.x + 8f, socket.y + 27f, 38f, 38f),
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
                    new Rect(labelX, socket.y + 22f, labelWidth, 44f),
                    installedLabel);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (Widgets.ButtonInvisible(socket))
                {
                    OpenPartMenu(slot);
                }
            }
        }

        private void DrawMetrics(Rect rect)
        {
            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(0.10f, 0.12f, 0.11f),
                new Color(0.34f, 0.40f, 0.36f),
                1);
            Widgets.Label(rect.ContractedBy(10f), "HD_ModularArmor_MetricBlock".Translate(
                comp.MetricFor(ModularArmorMetric.Ergonomics).ToString("0.##"),
                comp.MetricFor(ModularArmorMetric.Ventilation).ToString("0.##"),
                comp.MetricFor(ModularArmorMetric.LoadDistribution).ToString("0.##")));
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

            Widgets.DrawBoxSolid(gridRect, new Color(0.04f, 0.05f, 0.04f, 0.55f));
            for (int x = 0; x <= panel.columns; x++)
            {
                float lineX = gridRect.x + x * cellWidth;
                Widgets.DrawLine(
                    new Vector2(lineX, gridRect.y),
                    new Vector2(lineX, gridRect.yMax),
                    new Color(0.48f, 0.55f, 0.45f, 0.72f),
                    1f);
            }

            for (int y = 0; y <= panel.rows; y++)
            {
                float lineY = gridRect.y + y * cellHeight;
                Widgets.DrawLine(
                    new Vector2(gridRect.x, lineY),
                    new Vector2(gridRect.xMax, lineY),
                    new Color(0.48f, 0.55f, 0.45f, 0.72f),
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
            Widgets.Label(new Rect(gridRect.x, gridRect.y - 20f, gridRect.width, 20f), panel.LabelCap);
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
                partRect.ContractedBy(2f),
                record.part.uiColor,
                selected ? Color.yellow : new Color(0.72f, 0.76f, 0.68f),
                selected ? 3 : 1);
            DrawPartIconAndLabel(partRect.ContractedBy(5f), record.part);
            TooltipHandler.TipRegion(partRect,
                "HD_ModularArmor_PalsInstalledTip".Translate(
                    record.part.LabelCap,
                    record.palsX + 1,
                    record.palsY + 1));
        }

        private void DrawPlacementPreview(
            Rect gridRect,
            float cellWidth,
            float cellHeight,
            ModularArmorPalsPanelDef panel)
        {
            ModularArmorPartDef part = selectedInstalledPart?.part ?? selectedPalsPart;
            if (part == null || !Mouse.IsOver(gridRect))
            {
                return;
            }

            int x = Mathf.FloorToInt((Event.current.mousePosition.x - gridRect.x) / cellWidth);
            int y = Mathf.FloorToInt((Event.current.mousePosition.y - gridRect.y) / cellHeight);
            Rect preview = new Rect(
                gridRect.x + x * cellWidth,
                gridRect.y + y * cellHeight,
                part.palsWidth * cellWidth,
                part.palsHeight * cellHeight);
            bool valid = comp.CanPlacePalsPart(part, panel, x, y, selectedInstalledPart);
            Widgets.DrawBoxSolid(preview.ContractedBy(2f), valid
                ? new Color(0.25f, 0.75f, 0.35f, 0.35f)
                : new Color(0.85f, 0.20f, 0.20f, 0.40f));
        }

        private void HandlePalsInput(
            Rect gridRect,
            float cellWidth,
            float cellHeight,
            ModularArmorPalsPanelDef panel,
            List<InstalledModularArmorPart> mounted)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown || !gridRect.Contains(current.mousePosition))
            {
                return;
            }

            InstalledModularArmorPart clicked = mounted.LastOrDefault(record =>
                GridPartRect(gridRect, cellWidth, cellHeight, record)
                    .Contains(current.mousePosition));
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

            int x = Mathf.FloorToInt((current.mousePosition.x - gridRect.x) / cellWidth);
            int y = Mathf.FloorToInt((current.mousePosition.y - gridRect.y) / cellHeight);
            if (selectedInstalledPart != null)
            {
                if (comp.MovePalsPart(selectedInstalledPart, panel, x, y))
                {
                    selectedInstalledPart = null;
                }
            }
            else if (selectedPalsPart != null)
            {
                if (comp.TryStartPalsPartInstall(
                    selectedPalsPart,
                    panel,
                    x,
                    y,
                    out string rejection))
                {
                    selectedPalsPart = null;
                    Close();
                }
                else if (!rejection.NullOrEmpty())
                {
                    Messages.Message(rejection, MessageTypeDefOf.RejectInput);
                }
            }
            current.Use();
        }

        private void DrawPalsCatalog(Rect rect)
        {
            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(0.08f, 0.09f, 0.085f),
                new Color(0.32f, 0.36f, 0.33f),
                1);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, 25f),
                "HD_ModularArmor_PalsCatalog".Translate());

            List<ModularArmorPartDef> parts = DefDatabase<ModularArmorPartDef>
                .AllDefsListForReading
                .Where(part => part.installMode == ModularArmorInstallMode.Positionable
                    && comp.IsPartCompatible(part))
                .OrderBy(part => part.label)
                .ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                ModularArmorPartDef part = parts[i];
                Rect tile = new Rect(rect.x + 10f + i * 175f, rect.y + 34f, 165f, 78f);
                bool selected = selectedPalsPart == part;
                int available = comp.AvailableRequiredItemCount(part.RequiredThingDef);
                bool hasItem = part.RequiredThingDef == null || available > 0;
                Widgets.DrawBoxSolidWithOutline(
                    tile,
                    selected ? new Color(0.22f, 0.28f, 0.23f)
                        : hasItem ? new Color(0.12f, 0.14f, 0.13f)
                        : new Color(0.08f, 0.08f, 0.08f),
                    selected ? Color.yellow : new Color(0.35f, 0.42f, 0.37f),
                    selected ? 2 : 1);
                DrawPartIconAndLabel(tile.ContractedBy(8f), part);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.LowerCenter;
                string footprint = part.palsWidth + " x " + part.palsHeight;
                if (part.RequiredThingDef != null)
                {
                    footprint += "  " + "HD_ModularArmor_PartStock".Translate(available);
                }
                Widgets.Label(tile.ContractedBy(5f), footprint);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(tile))
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
        }

        private static void DrawPartIconAndLabel(Rect rect, ModularArmorPartDef part)
        {
            Texture icon = part.uiIconPath.NullOrEmpty()
                ? part.RequiredThingDef?.uiIcon
                : ContentFinder<Texture2D>.Get(part.uiIconPath, false);
            if (icon != null)
            {
                Rect iconRect = new Rect(rect.center.x - 20f, rect.y, 40f, 40f);
                Widgets.DrawTextureFitted(iconRect, icon, 1f);
                rect.yMin += 42f;
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, part.LabelCap);
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

        private ModularArmorFacing PreviewArmorFacing()
        {
            if (previewFacing == Rot4.North) return ModularArmorFacing.Back;
            if (previewFacing == Rot4.East) return ModularArmorFacing.Right;
            if (previewFacing == Rot4.West) return ModularArmorFacing.Left;
            return ModularArmorFacing.Front;
        }

        private static Rect FixedSlotRect(
            List<ModularArmorSlotDef> slots,
            int index,
            Rect previewRect)
        {
            int side = FixedSlotSide(slots[index], index);
            int ring = 0;
            for (int i = 0; i < index; i++)
            {
                if (FixedSlotSide(slots[i], i) == side)
                {
                    ring++;
                }
            }

            if (side == 0)
            {
                return new Rect(
                    previewRect.x - 285f - ring * 12f,
                    previewRect.center.y - 36f + ring * 82f,
                    260f,
                    72f);
            }

            if (side == 1)
            {
                return new Rect(
                    previewRect.xMax + 25f + ring * 12f,
                    previewRect.center.y - 36f + ring * 82f,
                    260f,
                    72f);
            }

            if (side == 2)
            {
                return new Rect(
                    previewRect.center.x - 130f + ring * 270f,
                    previewRect.y - 82f,
                    260f,
                    72f);
            }

            return new Rect(
                previewRect.center.x - 130f + ring * 270f,
                previewRect.yMax + 12f,
                260f,
                72f);
        }

        private static int FixedSlotSide(ModularArmorSlotDef slot, int index)
        {
            if (slot.uiSide == ModularArmorSlotSide.Auto) return index % 4;
            if (slot.uiSide == ModularArmorSlotSide.Left) return 0;
            if (slot.uiSide == ModularArmorSlotSide.Right) return 1;
            if (slot.uiSide == ModularArmorSlotSide.Top) return 2;
            return 3;
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
                record.part.palsWidth * cellWidth,
                record.part.palsHeight * cellHeight);
        }

        private void OpenPartMenu(ModularArmorSlotDef slot)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (!slot.required)
            {
                options.Add(new FloatMenuOption(
                    "HD_ModularArmor_None".Translate(),
                    () => comp.SetPart(slot, null)));
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
                int needed = comp.RequiredItemsNeededFor(slot, part);
                int available = comp.AvailableRequiredItemCount(part.RequiredThingDef);
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
                    options.Add(available >= needed
                    ? new FloatMenuOption(
                        label,
                        delegate
                        {
                            if (comp.TryStartFixedPartInstall(
                                slot,
                                part,
                                part.DefaultPosition,
                                out string rejection))
                            {
                                Close();
                            }
                            else if (!rejection.NullOrEmpty())
                            {
                                Messages.Message(
                                    rejection,
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

    }
}
