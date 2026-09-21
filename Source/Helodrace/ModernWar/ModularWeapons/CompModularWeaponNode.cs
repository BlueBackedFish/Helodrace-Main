using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class CompModularWeaponNode : ThingComp, IThingHolder
    {
        private const int MaxTreeDepth = 32;
        private const float ReceiverRearRailOffset = -0.0215f;
        private const float ReceiverFrontRailOffset = 0.0215f;
        private const float M16LowerGripRailOffset = -0.036f;
        private const float M16LowerPanelRailOffset = 0.059f;
        private const float M16UpperSidePanelRailOffset = -0.024f;

        private ThingOwner<Thing> children;
        private List<string> childSocketIds = new List<string>();
        private List<string> childMountIds = new List<string>();
        private List<float> childRailOffsets = new List<float>();
        private List<ModularRenderNode> renderCache;
        private bool renderCacheDirty = true;
        private readonly Dictionary<StatDef, float> statOffsetCache =
            new Dictionary<StatDef, float>();
        private readonly Dictionary<StatDef, float> statFactorCache =
            new Dictionary<StatDef, float>();
        private Dictionary<int, float> resolvedSightPerformanceWeights =
            new Dictionary<int, float>();
        private List<ModularSightGroupStatus> resolvedSightGroups =
            new List<ModularSightGroupStatus>();
        private bool performanceCacheDirty = true;
        private float resolvedFireDelayMultiplier = 1f;
        private int resolvedBurstShotCountOffset;
        private float resolvedBurstShotCountMultiplier = 1f;
        private float resolvedBurstShotSpeedMultiplier = 1f;
        private ModularWeaponConvertedStats resolvedConvertedStats =
            new ModularWeaponConvertedStats();
        private int resolvedMagazineCapacity;
        private int resolvedMagazinePartThingId;
        private string resolvedCEAmmoSetDefName;
        private string resolvedCEAmmoDefName;
        private ThingDef resolvedProjectileOverride;
        private int resolvedProjectilePriority = int.MinValue;
        private SoundDef resolvedSoundCastOverride;
        private SoundDef resolvedSoundCastTailOverride;
        private int resolvedSoundPriority = int.MinValue;
        private EffecterDef resolvedMuzzleFlashEffecter;
        private float resolvedMuzzleFlashDistance;
        private float resolvedMuzzleFlashScale = 1f;
        private bool resolvedMuzzleFlashSuppressed;
        private bool resolvedHasAdjustableGasSystem;
        private ModularWeaponMuzzleSignature resolvedMuzzleSignature =
            new ModularWeaponMuzzleSignature();
        private float gasTubeFlowSetting =
            ModularWeaponGasSystemUtility.DefaultSetting;
        private bool legacyDevelopmentTreeChecked;
        private bool requiredDefaultAttachmentsChecked;
        private bool fluxStockRailMigrationChecked;

        public CompProperties_ModularWeaponNode Props =>
            (CompProperties_ModularWeaponNode)props;

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureContainer();
            return children;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            EnsureContainer();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, children);
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureContainer();
            BuildDefaultAttachments();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            EnsureContainer();
            children.ExposeData();
            Scribe_Collections.Look(ref childSocketIds, "modularChildSocketIds", LookMode.Value);
            Scribe_Collections.Look(ref childMountIds, "modularChildMountIds", LookMode.Value);
            Scribe_Collections.Look(ref childRailOffsets, "modularChildRailOffsets", LookMode.Value);
            Scribe_Values.Look(ref fluxStockRailMigrationChecked,
                "modularFluxStockRailMigrationChecked", false);
            if (Props.isAssemblyRoot)
                Scribe_Values.Look(
                    ref gasTubeFlowSetting,
                    "modularGasTubeFlowSetting",
                    ModularWeaponGasSystemUtility.DefaultSetting);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (childSocketIds == null) childSocketIds = new List<string>();
                if (childMountIds == null) childMountIds = new List<string>();
                if (childRailOffsets == null) childRailOffsets = new List<float>();
                gasTubeFlowSetting = Mathf.Clamp(
                    gasTubeFlowSetting,
                    ModularWeaponGasSystemUtility.MinimumSetting,
                    ModularWeaponGasSystemUtility.MaximumSetting);
                RepairAssignmentLists();
                InvalidateTree();
            }
        }

        public int ChildCount
        {
            get
            {
                EnsureContainer();
                return children.Count;
            }
        }

        /// <summary>
        /// True when this node is not currently held by another modular node. Any modular
        /// part can therefore become the visible root of a partial assembly on the map.
        /// </summary>
        public bool IsTopLevelAssembly => RootComp() == this;

        public Thing ChildAt(int index)
        {
            EnsureContainer();
            return index >= 0 && index < children.Count ? children[index] : null;
        }

        public string SocketIdAt(int index)
        {
            RepairAssignmentLists();
            return index >= 0 && index < childSocketIds.Count ? childSocketIds[index] : null;
        }

        public string MountIdAt(int index)
        {
            RepairAssignmentLists();
            return index >= 0 && index < childMountIds.Count ? childMountIds[index] : null;
        }

        public float RailOffsetAt(int index)
        {
            RepairAssignmentLists();
            return index >= 0 && index < childRailOffsets.Count ? childRailOffsets[index] : 0f;
        }

        public ModularAttachmentSocket SocketNamed(string id) => Props.SocketNamed(id);

        public float RealisticRoundsPerMinute
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                return Mathf.Max(1f, root.Props.realisticRoundsPerMinute);
            }
        }

        public float MinimumFireDelaySeconds => 60f / RealisticRoundsPerMinute;

        public float InstalledFireDelayMultiplier
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedFireDelayMultiplier;
            }
        }

        public int BurstShotCountOffset
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedBurstShotCountOffset;
            }
        }

        public float BurstShotCountMultiplier
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedBurstShotCountMultiplier;
            }
        }

        public float BurstShotSpeedMultiplier
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedBurstShotSpeedMultiplier;
            }
        }

        public ModularWeaponConvertedStats ConvertedStats
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedConvertedStats;
            }
        }

        public float GasTubeFlowSetting
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                return Mathf.Clamp(
                    root.gasTubeFlowSetting,
                    ModularWeaponGasSystemUtility.MinimumSetting,
                    ModularWeaponGasSystemUtility.MaximumSetting);
            }
        }

        public bool HasAdjustableGasSystem
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedHasAdjustableGasSystem;
            }
        }

        public ModularWeaponMuzzleSignature MuzzleSignature
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedMuzzleSignature;
            }
        }

        /// <summary>
        /// Instance-level coefficient reserved for the muzzle-flash renderer. A value of
        /// one is the 16-inch reference; zero means no flash should be emitted.
        /// </summary>
        public float MuzzleFlashCoefficient => MuzzleSignature.Coefficient;

        public void SetGasTubeFlowSetting(float value)
        {
            CompModularWeaponNode root = RootComp();
            value = Mathf.Clamp(
                value,
                ModularWeaponGasSystemUtility.MinimumSetting,
                ModularWeaponGasSystemUtility.MaximumSetting);
            if (Mathf.Approximately(root.gasTubeFlowSetting, value)) return;
            root.gasTubeFlowSetting = value;
            root.InvalidateTree();
        }

        /// <summary>
        /// Capacity supplied by the installed magazine. This is not a converted combat
        /// stat: compatibility modules consume it as an instance-level magazine value.
        /// </summary>
        public int MagazineCapacity
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                if (root.FunctionStatus.singleRoundCapacity) return 1;
                return root.resolvedMagazineCapacity;
            }
        }

        /// <summary>Runtime identity of the installed magazine part, or zero.</summary>
        public int MagazinePartThingId
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedMagazinePartThingId;
            }
        }

        /// <summary>CE AmmoSet defName supplied independently by the active chamber.</summary>
        public string CEAmmoSetDefName
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedCEAmmoSetDefName;
            }
        }

        /// <summary>CE AmmoDef defName supplied independently by the ammo selector.</summary>
        public string CEAmmoDefName
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedCEAmmoDefName;
            }
        }

        public CompModularWeaponNode AssemblyRoot => RootComp();

        public ModularWeaponFunctionStatus FunctionStatus =>
            ModularWeaponFunctionResolver.Resolve(RootComp());

        public float EffectiveFireDelaySeconds
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                float configured = MinimumFireDelaySeconds
                    * Mathf.Max(1f, root.Props.baseFireDelayFactor)
                    * InstalledFireDelayMultiplier
                    / Mathf.Max(0.01f, BurstShotSpeedMultiplier);
                return Mathf.Max(MinimumFireDelaySeconds, configured);
            }
        }

        public float EffectiveRoundsPerMinute => 60f / EffectiveFireDelaySeconds;

        public int EffectiveBurstIntervalTicks => Mathf.Max(
            Mathf.CeilToInt(MinimumFireDelaySeconds * 60f),
            Mathf.CeilToInt(EffectiveFireDelaySeconds * 60f));

        public void ApplyFireDelayToCooldown(ref float cooldownSeconds)
        {
            cooldownSeconds = Mathf.Max(
                MinimumFireDelaySeconds,
                cooldownSeconds * InstalledFireDelayMultiplier);
        }

        public void ApplyMechanicalRpmFloor(ref float seconds)
        {
            seconds = Mathf.Max(MinimumFireDelaySeconds, seconds);
        }

        public int ApplyBurstShotCount(int baseCount)
        {
            if (FunctionStatus.semiAutomaticOnly) return 1;
            return Mathf.Max(1, Mathf.CeilToInt(
                (baseCount + BurstShotCountOffset) * BurstShotCountMultiplier));
        }

        public ThingDef ProjectileOverride
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedProjectileOverride;
            }
        }

        public SoundDef SoundCastOverride
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedSoundCastOverride;
            }
        }

        public SoundDef SoundCastTailOverride
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedSoundCastTailOverride;
            }
        }

        public EffecterDef MuzzleFlashEffecter
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedMuzzleFlashSuppressed
                    ? null
                    : root.resolvedMuzzleFlashEffecter;
            }
        }

        public float MuzzleFlashDistance
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedMuzzleFlashDistance;
            }
        }

        public float MuzzleFlashScale
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedMuzzleFlashScale;
            }
        }

        public IReadOnlyList<ModularSightGroupStatus> SightGroups
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                root.EnsurePerformanceCache();
                return root.resolvedSightGroups;
            }
        }

        public ModularSightGroupStatus ActiveSightGroup
        {
            get
            {
                IReadOnlyList<ModularSightGroupStatus> groups = SightGroups;
                for (int i = 0; i < groups.Count; i++)
                    if (groups[i].isActive) return groups[i];
                return null;
            }
        }

        public override float GetStatOffset(StatDef stat)
        {
            CompModularWeaponNode root = RootComp();
            root.EnsurePerformanceCache();
            float value;
            return stat != null && root.statOffsetCache.TryGetValue(stat, out value)
                ? value
                : 0f;
        }

        public override float GetStatFactor(StatDef stat)
        {
            CompModularWeaponNode root = RootComp();
            root.EnsurePerformanceCache();
            float value;
            return stat != null && root.statFactorCache.TryGetValue(stat, out value)
                ? value
                : 1f;
        }

        public override void GetStatsExplanation(
            StatDef stat,
            StringBuilder sb,
            string whitespace = "")
        {
            CompModularWeaponNode root = RootComp();
            root.EnsurePerformanceCache();
            List<ModularRenderNode> nodes = root.RenderSnapshot();
            StringBuilder partLines = new StringBuilder();

            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                CompProperties_ModularWeaponNode nodeProps = node.Props;
                if (nodeProps == null) continue;

                float performanceWeight = 1f;
                if (nodeProps.sight != null
                    && !root.resolvedSightPerformanceWeights.TryGetValue(
                        node.thing.thingIDNumber,
                        out performanceWeight))
                    continue;

                float offset = nodeProps.statOffsets.GetStatOffsetFromList(stat);
                if (!Mathf.Approximately(offset, 0f))
                {
                    partLines.AppendLine(whitespace + "    "
                        + node.thing.LabelCap + ": "
                        + stat.Worker.ValueToString(
                            offset * performanceWeight,
                            false,
                            ToStringNumberSense.Offset));
                }

                float factor = nodeProps.statFactors.GetStatFactorFromList(stat);
                if (!Mathf.Approximately(factor, 1f))
                {
                    partLines.AppendLine(whitespace + "    "
                        + node.thing.LabelCap + ": "
                        + stat.Worker.ValueToString(
                            Mathf.Lerp(1f, factor, performanceWeight),
                            false,
                            ToStringNumberSense.Factor));
                }
            }

            if (partLines.Length == 0) return;
            sb.AppendLine(whitespace
                + "HD_ModularWeapon_AttachedParts".Translate() + ":");
            sb.Append(partLines);
        }

        private void EnsurePerformanceCache()
        {
            CompModularWeaponNode root = RootComp();
            if (root != this)
            {
                root.EnsurePerformanceCache();
                return;
            }
            if (!performanceCacheDirty) return;

            statOffsetCache.Clear();
            statFactorCache.Clear();
            resolvedFireDelayMultiplier = 1f;
            resolvedBurstShotCountOffset = 0;
            resolvedBurstShotCountMultiplier = 1f;
            resolvedBurstShotSpeedMultiplier = 1f;
            resolvedMagazineCapacity = 0;
            resolvedMagazinePartThingId = 0;
            resolvedCEAmmoSetDefName = null;
            resolvedCEAmmoDefName = null;
            resolvedProjectileOverride = null;
            resolvedProjectilePriority = int.MinValue;
            resolvedSoundCastOverride = null;
            resolvedSoundCastTailOverride = null;
            resolvedSoundPriority = int.MinValue;
            resolvedMuzzleFlashEffecter = Props.muzzleFlashEffecter;
            resolvedMuzzleFlashDistance = Props.muzzleFlashDistance;
            resolvedMuzzleFlashScale = Props.muzzleFlashScale;
            resolvedMuzzleFlashSuppressed = false;

            List<ModularRenderNode> nodes = RenderSnapshot();
            resolvedHasAdjustableGasSystem =
                ModularWeaponGasSystemUtility.HasInstalledGasSystem(nodes);
            float activeGasSetting = resolvedHasAdjustableGasSystem
                ? GasTubeFlowSetting
                : ModularWeaponGasSystemUtility.DefaultSetting;
            ModularSightResolution sightResolution =
                ModularWeaponSightResolver.Resolve(this, nodes);
            resolvedSightGroups = sightResolution.groups;
            resolvedSightPerformanceWeights = sightResolution.performanceWeights;
            resolvedConvertedStats = ModularWeaponStatConverter.Resolve(
                this,
                nodes,
                resolvedSightPerformanceWeights,
                activeGasSetting);
            for (int i = 0; i < nodes.Count; i++)
            {
                CompProperties_ModularWeaponNode nodeProps = nodes[i].Props;
                if (nodeProps == null) continue;

                float performanceWeight = 1f;
                bool applyStatModifiers = nodeProps.sight == null
                    || resolvedSightPerformanceWeights.TryGetValue(
                        nodes[i].thing.thingIDNumber,
                        out performanceWeight);

                if (applyStatModifiers && !nodeProps.statOffsets.NullOrEmpty())
                {
                    for (int j = 0; j < nodeProps.statOffsets.Count; j++)
                    {
                        StatModifier modifier = nodeProps.statOffsets[j];
                        if (modifier?.stat == null) continue;
                        float current;
                        statOffsetCache.TryGetValue(modifier.stat, out current);
                        statOffsetCache[modifier.stat] = current
                            + modifier.value * performanceWeight;
                    }
                }

                if (applyStatModifiers && !nodeProps.statFactors.NullOrEmpty())
                {
                    for (int j = 0; j < nodeProps.statFactors.Count; j++)
                    {
                        StatModifier modifier = nodeProps.statFactors[j];
                        if (modifier?.stat == null) continue;
                        float current;
                        if (!statFactorCache.TryGetValue(modifier.stat, out current))
                            current = 1f;
                        statFactorCache[modifier.stat] = current
                            * Mathf.Lerp(1f, modifier.value, performanceWeight);
                    }
                }

                resolvedFireDelayMultiplier *= Mathf.Max(
                    0.01f,
                    nodeProps.fireDelayMultiplier);
                resolvedBurstShotCountOffset += nodeProps.burstShotCountOffset;
                resolvedBurstShotCountMultiplier *= Mathf.Max(
                    0.01f,
                    nodeProps.burstShotCountMultiplier);
                resolvedBurstShotSpeedMultiplier *= Mathf.Max(
                    0.01f,
                    nodeProps.burstShotSpeedMultiplier);

                if (nodeProps.magazineCapacity > 0)
                {
                    resolvedMagazineCapacity = nodeProps.magazineCapacity;
                    resolvedMagazinePartThingId = nodes[i].thing.thingIDNumber;
                }
                if (!nodeProps.ceAmmoSetDefName.NullOrEmpty())
                    resolvedCEAmmoSetDefName = nodeProps.ceAmmoSetDefName;
                if (!nodeProps.ceAmmoDefName.NullOrEmpty())
                    resolvedCEAmmoDefName = nodeProps.ceAmmoDefName;

                if (nodeProps.projectileOverride != null
                    && nodeProps.overridePriority >= resolvedProjectilePriority)
                {
                    resolvedProjectileOverride = nodeProps.projectileOverride;
                    resolvedProjectilePriority = nodeProps.overridePriority;
                }

                if ((nodeProps.soundCastOverride != null
                        || nodeProps.soundCastTailOverride != null)
                    && nodeProps.overridePriority >= resolvedSoundPriority)
                {
                    if (nodeProps.soundCastOverride != null)
                        resolvedSoundCastOverride = nodeProps.soundCastOverride;
                    if (nodeProps.soundCastTailOverride != null)
                        resolvedSoundCastTailOverride = nodeProps.soundCastTailOverride;
                    resolvedSoundPriority = nodeProps.overridePriority;
                }

                if (nodeProps.muzzleFlashEffecterOverride != null)
                    resolvedMuzzleFlashEffecter = nodeProps.muzzleFlashEffecterOverride;
                resolvedMuzzleFlashDistance += nodeProps.muzzleFlashDistanceOffset;
                resolvedMuzzleFlashScale *= Mathf.Max(
                    0f,
                    nodeProps.muzzleFlashScaleFactor);
                if (nodeProps.suppressMuzzleFlash)
                    resolvedMuzzleFlashSuppressed = true;
            }

            resolvedMuzzleSignature =
                ModularWeaponGasSystemUtility.ResolveMuzzleSignature(
                    nodes,
                    activeGasSetting,
                    resolvedMuzzleFlashSuppressed);

            ModularWeaponStatConverter.ApplyVanillaFactors(
                resolvedConvertedStats,
                statOffsetCache,
                statFactorCache,
                parent?.def);
            resolvedFireDelayMultiplier *= resolvedConvertedStats.VanillaCycleFactor;

            performanceCacheDirty = false;
        }

        public bool TryAttach(
            Thing child,
            string socketId,
            string mountId,
            out string reason)
        {
            return TryAttach(child, socketId, mountId, 0f, out reason);
        }

        public bool TryAttach(
            Thing child,
            string socketId,
            string mountId,
            float railOffset,
            out string reason)
        {
            reason = null;
            if (child == null || child.Destroyed)
            {
                reason = "No valid part was supplied.";
                return false;
            }

            CompModularWeaponNode prospectiveChildComp =
                child.TryGetComp<CompModularWeaponNode>();
            if (child == parent
                || ContainsThingRecursive(child, 0)
                || (prospectiveChildComp != null
                    && prospectiveChildComp.ContainsThingRecursive(parent, 0)))
            {
                reason = "Attaching this part would create a cycle.";
                return false;
            }

            ModularAttachmentSocket socket = Props.SocketNamed(socketId);
            if (socket == null)
            {
                reason = "Socket " + socketId + " does not exist on " + parent.LabelCap + ".";
                return false;
            }

            int attachmentLimit = socket.isRail ? socket.maxAttachments : 1;
            if (attachmentLimit > 0 && CountOnSocket(socketId) >= attachmentLimit)
            {
                reason = "Socket " + socket.Label + " has reached its attachment limit.";
                return false;
            }

            CompModularWeaponNode childComp = prospectiveChildComp;
            if (childComp == null)
            {
                reason = child.LabelCap + " is not a modular weapon part.";
                return false;
            }

            ModularAttachmentMount mount = childComp.Props.MountNamed(mountId);
            if (mount == null)
            {
                reason = child.LabelCap + " has no attachment mount.";
                return false;
            }

            if (!mount.Accepts(socket))
            {
                reason = mount.Label + " is not compatible with " + socket.Label + ".";
                return false;
            }

            if (!ValidateRailPlacement(socket, mount, railOffset, -1, out reason))
                return false;

            if (child.Spawned)
            {
                reason = "A spawned part must be despawned before it can be attached.";
                return false;
            }

            EnsureContainer();
            if (!children.TryAdd(child, false))
            {
                reason = "RimWorld refused to move the part into the assembly.";
                return false;
            }

            childSocketIds.Add(socketId);
            childMountIds.Add(mount.id);
            childRailOffsets.Add(socket.isRail ? railOffset : 0f);
            InvalidateTree();
            return true;
        }

        public bool TrySetRailOffset(int childIndex, float railOffset, out string reason)
        {
            RepairAssignmentLists();
            reason = null;
            if (childIndex < 0 || childIndex >= children.Count)
            {
                reason = "The attached part no longer exists.";
                return false;
            }

            ModularAttachmentSocket socket = Props.SocketNamed(childSocketIds[childIndex]);
            CompModularWeaponNode childComp = children[childIndex]
                ?.TryGetComp<CompModularWeaponNode>();
            ModularAttachmentMount mount = childComp?.Props.MountNamed(childMountIds[childIndex]);
            if (socket == null || !socket.isRail || mount == null)
            {
                reason = "This attachment is not mounted on a rail.";
                return false;
            }

            if (!ValidateRailPlacement(socket, mount, railOffset, childIndex, out reason))
                return false;

            childRailOffsets[childIndex] = railOffset;
            SyncDefaultRailOffset(childIndex, railOffset);
            InvalidateTree();
            return true;
        }

        public bool TryFindAttachOffset(
            Thing child,
            string socketId,
            string mountId,
            out float offset,
            out string reason)
        {
            offset = 0f;
            reason = null;
            ModularAttachmentSocket socket = Props.SocketNamed(socketId);
            CompModularWeaponNode childComp = child?.TryGetComp<CompModularWeaponNode>();
            ModularAttachmentMount mount = childComp?.Props.MountNamed(mountId);
            if (socket == null || mount == null || !mount.Accepts(socket))
            {
                reason = "The selected part is not compatible with this socket.";
                return false;
            }

            if (!socket.isRail) return true;

            float halfPart = Mathf.Max(0f, mount.railOccupancy) * 0.5f;
            float minimum = -socket.railLength * 0.5f + halfPart;
            float maximum = socket.railLength * 0.5f - halfPart;
            if (minimum > maximum)
            {
                reason = mount.Label + " is longer than rail " + socket.Label + ".";
                return false;
            }

            List<float> candidates = new List<float> { 0f, minimum, maximum };
            RepairAssignmentLists();
            for (int i = 0; i < children.Count; i++)
            {
                if (childSocketIds[i] != socketId) continue;
                CompModularWeaponNode otherComp = children[i]
                    ?.TryGetComp<CompModularWeaponNode>();
                ModularAttachmentMount otherMount = otherComp?.Props.MountNamed(childMountIds[i]);
                float otherHalf = Mathf.Max(0f, otherMount?.railOccupancy ?? 0f) * 0.5f;
                candidates.Add(childRailOffsets[i] + otherHalf + halfPart + 0.0002f);
                candidates.Add(childRailOffsets[i] - otherHalf - halfPart - 0.0002f);
            }

            candidates.Sort((a, b) =>
            {
                int distance = Mathf.Abs(a).CompareTo(Mathf.Abs(b));
                return distance != 0 ? distance : a.CompareTo(b);
            });
            for (int i = 0; i < candidates.Count; i++)
            {
                float candidate = Mathf.Clamp(candidates[i], minimum, maximum);
                string rejection;
                if (!ValidateRailPlacement(socket, mount, candidate, -1, out rejection))
                    continue;
                offset = candidate;
                return true;
            }

            reason = "There is no free span on rail " + socket.Label + ".";
            return false;
        }

        internal List<float> RailOffsetsSnapshot()
        {
            RepairAssignmentLists();
            return new List<float>(childRailOffsets);
        }

        internal void RestoreRailOffsets(List<float> values)
        {
            childRailOffsets = values == null ? new List<float>() : new List<float>(values);
            RepairAssignmentLists();
            InvalidateTree();
        }

        public Thing DetachFromSocket(string socketId)
        {
            EnsureContainer();
            int index = IndexOnSocket(socketId);
            return DetachChildAt(index);
        }

        public Thing DetachChild(Thing child)
        {
            EnsureContainer();
            return DetachChildAt(child == null ? -1 : children.IndexOf(child));
        }

        public Thing DetachChildAt(int index)
        {
            EnsureContainer();
            if (index < 0 || index >= children.Count) return null;

            Thing child = children[index];
            Thing taken = children.Take(child, child.stackCount);
            childSocketIds.RemoveAt(index);
            childMountIds.RemoveAt(index);
            childRailOffsets.RemoveAt(index);
            InvalidateTree();
            return taken;
        }

        /// <summary>
        /// Atomically replaces the descendant tree with a preset. A detached template is
        /// built and validated first, so a malformed Def cannot partially alter a weapon.
        /// </summary>
        public bool TryApplyPreset(ModularWeaponPresetDef preset, out string rejection)
        {
            rejection = null;
            if (preset == null || preset.weaponDef != parent.def || !Props.isAssemblyRoot)
            {
                rejection = "The weapon preset does not target this assembly root.";
                return false;
            }

            Thing templateThing = ThingMaker.MakeThing(
                parent.def,
                GenStuff.DefaultStuffFor(parent.def));
            CompModularWeaponNode template =
                templateThing?.TryGetComp<CompModularWeaponNode>();
            if (template == null)
            {
                templateThing?.Destroy(DestroyMode.Vanish);
                rejection = "The preset root could not be created.";
                return false;
            }

            if (!preset.useDefaultConfiguration)
            {
                template.DestroyAllChildren();
                if (!BuildPresetChildren(template, preset.parts, 0, out rejection))
                {
                    DestroyAssembly(templateThing);
                    return false;
                }
            }

            if (!template.ValidateRequiredSocketsRecursive(0, out rejection))
            {
                DestroyAssembly(templateThing);
                return false;
            }

            DestroyAllChildren();
            while (template.ChildCount > 0)
            {
                string socketId = template.SocketIdAt(0);
                string mountId = template.MountIdAt(0);
                float railOffset = template.RailOffsetAt(0);
                Thing child = template.DetachChildAt(0);
                if (!TryAttach(child, socketId, mountId, railOffset, out rejection))
                {
                    DestroyAssembly(child);
                    DestroyAssembly(templateThing);
                    return false;
                }
            }

            requiredDefaultAttachmentsChecked = true;
            gasTubeFlowSetting = Mathf.Clamp(
                preset.gasTubeFlowSetting,
                ModularWeaponGasSystemUtility.MinimumSetting,
                ModularWeaponGasSystemUtility.MaximumSetting);
            templateThing.Destroy(DestroyMode.Vanish);
            InvalidateTree();
            return true;
        }

        private static bool BuildPresetChildren(
            CompModularWeaponNode parentComp,
            List<ModularWeaponPresetPart> entries,
            int depth,
            out string rejection)
        {
            rejection = null;
            if (depth > MaxTreeDepth)
            {
                rejection = "The preset exceeds the maximum modular tree depth.";
                return false;
            }
            if (entries.NullOrEmpty()) return true;

            for (int i = 0; i < entries.Count; i++)
            {
                ModularWeaponPresetPart entry = entries[i];
                Thing child = entry?.part == null
                    ? null
                    : ThingMaker.MakeThing(
                        entry.part,
                        GenStuff.DefaultStuffFor(entry.part));
                CompModularWeaponNode childComp =
                    child?.TryGetComp<CompModularWeaponNode>();
                if (childComp == null)
                {
                    DestroyAssembly(child);
                    rejection = "Invalid modular part at preset index " + i + ".";
                    return false;
                }

                childComp.DestroyAllChildren();
                if (!BuildPresetChildren(
                    childComp, entry.children, depth + 1, out rejection)
                    || !parentComp.TryAttach(
                        child, entry.socketId, entry.mountId,
                        entry.railOffset, out rejection))
                {
                    DestroyAssembly(child);
                    return false;
                }
                childComp.requiredDefaultAttachmentsChecked = true;
            }
            return true;
        }

        private bool ValidateRequiredSocketsRecursive(
            int depth,
            out string rejection)
        {
            rejection = null;
            if (depth > MaxTreeDepth)
            {
                rejection = "The preset exceeds the maximum modular tree depth.";
                return false;
            }
            if (!Props.sockets.NullOrEmpty())
            {
                for (int i = 0; i < Props.sockets.Count; i++)
                {
                    ModularAttachmentSocket socket = Props.sockets[i];
                    if (socket?.required == true && CountOnSocket(socket.id) == 0)
                    {
                        rejection = parent.def.defName + " requires socket "
                            + socket.id + ".";
                        return false;
                    }
                }
            }

            for (int i = 0; i < ChildCount; i++)
            {
                CompModularWeaponNode child = ChildAt(i)
                    ?.TryGetComp<CompModularWeaponNode>();
                if (child != null
                    && !child.ValidateRequiredSocketsRecursive(depth + 1, out rejection))
                    return false;
            }
            return true;
        }

        private void DestroyAllChildren()
        {
            while (ChildCount > 0)
                DestroyAssembly(DetachChildAt(ChildCount - 1));
            requiredDefaultAttachmentsChecked = true;
            InvalidateTree();
        }

        private static void DestroyAssembly(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            CompModularWeaponNode comp = thing.TryGetComp<CompModularWeaponNode>();
            while (comp != null && comp.ChildCount > 0)
                DestroyAssembly(comp.DetachChildAt(comp.ChildCount - 1));
            thing.Destroy(DestroyMode.Vanish);
        }

        public int IndexOnSocket(string socketId)
        {
            RepairAssignmentLists();
            for (int i = 0; i < childSocketIds.Count; i++)
            {
                if (childSocketIds[i] == socketId) return i;
            }

            return -1;
        }

        public int CountOnSocket(string socketId)
        {
            RepairAssignmentLists();
            int count = 0;
            for (int i = 0; i < childSocketIds.Count; i++)
                if (childSocketIds[i] == socketId) count++;
            return count;
        }

        public List<ModularRenderNode> RenderSnapshot()
        {
            CompModularWeaponNode root = RootComp();
            if (root != this) return root.RenderSnapshot();
            // The initial M1911 import stored the lower slide image as a separate
            // child. It is now rendered by the upper slide's additionalGraphics.
            if (parent.def.defName == "HD_Gun_ModularM1911_Test_Weapon")
                for (int i = ChildCount - 1; i >= 0; i--)
                    if (ChildAt(i)?.def.defName == "HD_ModularPart_Slide_M1911A1_DOWN")
                        DetachChildAt(i)?.Destroy(DestroyMode.Vanish);
            root.MigrateLegacyDevelopmentOptics();
            root.EnsureRequiredDefaultAttachmentsRecursive();
            if (!renderCacheDirty && renderCache != null) return renderCache;

            List<ModularRenderNode> result = new List<ModularRenderNode>();
            HashSet<int> visited = new HashSet<int>();
            ModularTransform2D rootTransform = ModularTransform2D.Identity;
            rootTransform.scale = Vector2.one * Mathf.Max(0.01f, Props.assemblyScale);
            rootTransform.angle = Props.graphicAngle;
            BuildSnapshotRecursive(
                result,
                visited,
                rootTransform,
                "root",
                null,
                null,
                0);
            result.Sort((a, b) =>
            {
                int layer = a.GraphicLayer.CompareTo(b.GraphicLayer);
                return layer != 0 ? layer : string.CompareOrdinal(a.path, b.path);
            });
            renderCache = result;
            renderCacheDirty = false;
            return renderCache;
        }

        public void InvalidateTree()
        {
            CompModularWeaponNode root = RootComp();
            HashSet<StatDef> affectedStats = new HashSet<StatDef>();
            foreach (StatDef stat in root.statOffsetCache.Keys) affectedStats.Add(stat);
            foreach (StatDef stat in root.statFactorCache.Keys) affectedStats.Add(stat);
            root.CollectPerformanceStats(affectedStats, 0);
            affectedStats.Add(StatDefOf.RangedWeapon_Cooldown);
            affectedStats.Add(StatDefOf.RangedWeapon_WarmupMultiplier);
            affectedStats.Add(StatDefOf.Mass);
            affectedStats.Add(StatDefOf.AccuracyTouch);
            affectedStats.Add(StatDefOf.AccuracyShort);
            affectedStats.Add(StatDefOf.AccuracyMedium);
            affectedStats.Add(StatDefOf.AccuracyLong);

            root.renderCacheDirty = true;
            root.renderCache = null;
            root.performanceCacheDirty = true;

            foreach (StatDef stat in affectedStats)
                stat?.Worker?.ClearCacheForThing(root.parent);

            Thing rootThing = root.parent;
            if (rootThing != null && rootThing.Spawned && rootThing.Map != null)
            {
                rootThing.Map.mapDrawer.MapMeshDirty(
                    rootThing.Position,
                    MapMeshFlagDefOf.Things);
            }
        }

        private void CollectPerformanceStats(HashSet<StatDef> result, int depth)
        {
            if (result == null || depth > MaxTreeDepth) return;
            if (!Props.statOffsets.NullOrEmpty())
            {
                for (int i = 0; i < Props.statOffsets.Count; i++)
                    if (Props.statOffsets[i]?.stat != null)
                        result.Add(Props.statOffsets[i].stat);
            }
            if (!Props.statFactors.NullOrEmpty())
            {
                for (int i = 0; i < Props.statFactors.Count; i++)
                    if (Props.statFactors[i]?.stat != null)
                        result.Add(Props.statFactors[i].stat);
            }

            EnsureContainer();
            for (int i = 0; i < children.Count; i++)
                children[i]?.TryGetComp<CompModularWeaponNode>()
                    ?.CollectPerformanceStats(result, depth + 1);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (!IsTopLevelAssembly || !Props.isAssemblyRoot) yield break;

            // Normal configuration is deliberately exposed by the dedicated weapon bench,
            // where a linked parts box can account for every installed/removed part.
            if (Props.allowPlayerConfiguration && Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: " + "HD_ModularWeapon_Command".Translate(),
                    defaultDesc = "HD_ModularWeapon_CommandDescDev".Translate(),
                    icon = parent.def.uiIcon,
                    action = () => Find.WindowStack.Add(new Dialog_ModularWeapon(this))
                };
            }

            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "DEV: attachment editor",
                defaultDesc = "Edit this modular item tree's pivots, mounts and sockets using the live render tree.",
                icon = TexCommand.DesirePower,
                action = () => Find.WindowStack.Add(new Dialog_ModularAttachmentEditor(this))
            };
        }

        private void BuildDefaultAttachments()
        {
            // CE can query assembled stats while comps are still initializing, which may
            // materialize a required child before this callback. Reconcile each socket
            // below instead of treating any existing child as a completed assembly.
            if (Props.defaultAttachments.NullOrEmpty()) return;

            for (int i = 0; i < Props.defaultAttachments.Count; i++)
            {
                ModularDefaultAttachment entry = Props.defaultAttachments[i];
                if (entry?.part == null) continue;
                ModularAttachmentSocket socket = Props.SocketNamed(entry.socketId);
                int limit = socket?.isRail == true ? socket.maxAttachments : 1;
                if (socket == null || (limit > 0 && CountOnSocket(entry.socketId) >= limit))
                    continue;

                Thing child = ThingMaker.MakeThing(entry.part);
                string reason;
                if (!TryAttach(child, entry.socketId, entry.mountId, entry.railOffset, out reason))
                {
                    child.Destroy(DestroyMode.Vanish);
                    Log.Error("[Helodrace] Could not build default modular attachment "
                        + entry.part.defName + " on " + parent.def.defName + ": " + reason);
                }
            }
        }

        private void EnsureRequiredDefaultAttachmentsRecursive()
        {
            if (!requiredDefaultAttachmentsChecked)
            {
                requiredDefaultAttachmentsChecked = true;
                for (int i = 0; i < Props.defaultAttachments.Count; i++)
                {
                    ModularDefaultAttachment entry = Props.defaultAttachments[i];
                    ModularAttachmentSocket socket = Props.SocketNamed(entry?.socketId);
                    if (entry?.part == null || socket?.required != true
                        || CountOnSocket(socket.id) > 0)
                        continue;

                    // Part categories intentionally allow incomplete assemblies to persist.
                    // A missing Required part must block firing instead of silently being
                    // recreated when the weapon happens to render.
                    CompProperties_ModularWeaponNode expectedProps = entry.part
                        .GetCompProperties<CompProperties_ModularWeaponNode>();
                    if (expectedProps?.partCategory == ModularWeaponPartCategory.Required)
                        continue;

                    Thing child = ThingMaker.MakeThing(entry.part);
                    string reason;
                    if (!TryAttach(child, socket.id, entry.mountId,
                        entry.railOffset, out reason))
                    {
                        child.Destroy(DestroyMode.Vanish);
                        Log.Error("[Helodrace] Could not restore required modular attachment "
                            + entry.part.defName + " on " + parent.def.defName + ": " + reason);
                    }
                }
            }

            for (int i = 0; i < ChildCount; i++)
                ChildAt(i)?.TryGetComp<CompModularWeaponNode>()
                    ?.EnsureRequiredDefaultAttachmentsRecursive();
        }

        // Development saves made before receiver rail sockets existed stored the EXPS under
        // the handguard and the G33 under the EXPS. Move those same Thing instances instead
        // of recreating them, preserving hit points and the rest of each subtree state.
        private void MigrateLegacyDevelopmentOptics()
        {
            if (legacyDevelopmentTreeChecked) return;
            legacyDevelopmentTreeChecked = true;
            if (parent?.def?.defName != "HD_Gun_ModularM4_Test_Weapon") return;

            if (Props.SocketNamed("magazine") != null && IndexOnSocket("magazine") < 0)
            {
                ThingDef magazineDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                    "HD_ModularPart_Magazine_STANAG");
                if (magazineDef != null)
                {
                    Thing magazine = ThingMaker.MakeThing(magazineDef);
                    string magazineReason;
                    if (!TryAttach(magazine, "magazine", null, out magazineReason))
                    {
                        magazine.Destroy(DestroyMode.Vanish);
                        Log.Error("[Helodrace] Could not add the STANAG magazine to a saved "
                            + "M4 development assembly: " + magazineReason);
                    }
                }
            }

            CompModularWeaponNode upper = DirectChildComp("HD_ModularPart_UpperReceiver_M4A1");
            upper?.MigrateLegacyReceiverRailAssignments();
            CompModularWeaponNode handguard = upper
                ?.DirectChildComp("HD_ModularPart_Handguard_HACRISFDE");
            if (upper == null || handguard == null) return;

            int opticIndex = handguard.IndexOnSocket("rail_top_rear");
            if (opticIndex < 0) return;

            Thing optic = handguard.ChildAt(opticIndex);
            CompModularWeaponNode opticComp = optic?.TryGetComp<CompModularWeaponNode>();
            Thing magnifier = opticComp?.DetachFromSocket("magnifier");
            optic = handguard.DetachFromSocket("rail_top_rear");

            string reason;
            if (magnifier != null
                && !upper.TryAttach(magnifier, "rail_receiver_top", null,
                    ReceiverRearRailOffset, out reason))
            {
                Log.Error("[Helodrace] Could not migrate saved G33 to the upper receiver: "
                    + reason);
            }

            if (optic != null
                && !upper.TryAttach(optic, "rail_receiver_top", null,
                    ReceiverFrontRailOffset, out reason))
            {
                Log.Error("[Helodrace] Could not migrate saved EXPS to the upper receiver: "
                    + reason);
            }

            renderCacheDirty = true;
        }

        private CompModularWeaponNode DirectChildComp(string defName)
        {
            EnsureContainer();
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                if (child?.def?.defName == defName)
                    return child.TryGetComp<CompModularWeaponNode>();
            }

            return null;
        }

        private void BuildSnapshotRecursive(
            List<ModularRenderNode> result,
            HashSet<int> visited,
            ModularTransform2D transform,
            string path,
            string parentSocketId,
            string mountId,
            int depth,
            CompModularWeaponNode parentComp = null,
            int parentChildIndex = -1,
            float railOffset = 0f,
            bool attachedToRail = false,
            bool mountedOnOppositeSurface = false,
            Vector2 railStart = default(Vector2),
            Vector2 railEnd = default(Vector2),
            Vector2 occupiedRailStart = default(Vector2),
            Vector2 occupiedRailEnd = default(Vector2))
        {
            if (depth > MaxTreeDepth || parent == null || !visited.Add(parent.thingIDNumber))
            {
                Log.ErrorOnce("[Helodrace] Invalid or excessively deep modular weapon tree.",
                    0x48444D57);
                return;
            }

            result.Add(new ModularRenderNode
            {
                path = path,
                depth = depth,
                thing = parent,
                comp = this,
                transform = transform,
                parentSocketId = parentSocketId,
                mountId = mountId,
                parentComp = parentComp,
                parentChildIndex = parentChildIndex,
                railOffset = railOffset,
                attachedToRail = attachedToRail,
                mountedOnOppositeSurface = mountedOnOppositeSurface,
                railStart = railStart,
                railEnd = railEnd,
                occupiedRailStart = occupiedRailStart,
                occupiedRailEnd = occupiedRailEnd
            });

            RepairAssignmentLists();
            MigrateLegacyReceiverRailAssignments();
            MigrateLegacyM16LowerRailAssignments();
            MigrateLegacyFluxStockRailOffset();
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                CompModularWeaponNode childComp = child?.TryGetComp<CompModularWeaponNode>();
                ModularAttachmentSocket socket = Props.SocketNamed(childSocketIds[i]);
                ModularAttachmentMount mount = childComp?.Props.MountNamed(childMountIds[i]);
                if (childComp == null || socket == null || mount == null) continue;

                ModularAttachmentTransform effectiveSocket = socket.transform?.Clone()
                    ?? new ModularAttachmentTransform();
                if (socket.isRail)
                {
                    effectiveSocket.angle = 0f;
                    effectiveSocket.position.x += childRailOffsets[i];
                }
                bool oppositeSurface = mount.UsesOppositeSurface(socket);
                if (oppositeSurface)
                    effectiveSocket.position += mount.oppositeSurfaceOffset;
                bool childAttachedToRail = socket.isRail;
                Vector2 childRailStart = Vector2.zero;
                Vector2 childRailEnd = Vector2.zero;
                Vector2 childOccupiedStart = Vector2.zero;
                Vector2 childOccupiedEnd = Vector2.zero;
                if (childAttachedToRail)
                {
                    Vector3 left = socket.transform.position;
                    Vector3 right = socket.transform.position;
                    left.x -= socket.railLength * 0.5f;
                    right.x += socket.railLength * 0.5f;
                    childRailStart = transform.TransformPoint(left);
                    childRailEnd = transform.TransformPoint(right);

                    float occupiedHalf = Mathf.Max(0f, mount.railOccupancy) * 0.5f;
                    left = socket.transform.position;
                    right = socket.transform.position;
                    left.x += childRailOffsets[i] - occupiedHalf;
                    right.x += childRailOffsets[i] + occupiedHalf;
                    childOccupiedStart = transform.TransformPoint(left);
                    childOccupiedEnd = transform.TransformPoint(right);
                }
                ModularTransform2D childTransform = transform.Attach(
                    effectiveSocket,
                    mount.EffectiveTransform(socket),
                    childComp.Props.graphicAngle);
                childComp.BuildSnapshotRecursive(
                    result,
                    visited,
                    childTransform,
                    path + "/" + socket.id + "[" + child.thingIDNumber + "]",
                    socket.id,
                    mount.id,
                    depth + 1,
                    this,
                    i,
                    childRailOffsets[i],
                    childAttachedToRail,
                    oppositeSurface,
                    childRailStart,
                    childRailEnd,
                    childOccupiedStart,
                    childOccupiedEnd);
            }
        }

        private void MigrateLegacyReceiverRailAssignments()
        {
            if (Props.SocketNamed("rail_receiver_top") == null) return;
            RepairAssignmentLists();
            bool changed = false;
            for (int i = 0; i < childSocketIds.Count; i++)
            {
                if (childSocketIds[i] == "rail_receiver_rear")
                {
                    childSocketIds[i] = "rail_receiver_top";
                    childRailOffsets[i] = ReceiverRearRailOffset;
                    changed = true;
                }
                else if (childSocketIds[i] == "rail_receiver_front")
                {
                    childSocketIds[i] = "rail_receiver_top";
                    childRailOffsets[i] = ReceiverFrontRailOffset;
                    changed = true;
                }
            }

            if (changed) InvalidateTree();
        }

        private void MigrateLegacyM16LowerRailAssignments()
        {
            if (parent?.def?.defName != "HD_ModularPart_HandguardExt_HACURXDOWN"
                || Props.SocketNamed("rail_bottom") == null)
                return;

            RepairAssignmentLists();
            bool changed = false;
            for (int i = childSocketIds.Count - 1; i >= 0; i--)
            {
                string legacySocket = childSocketIds[i];
                if (legacySocket == "rail_bottom_grip")
                {
                    childSocketIds[i] = "rail_bottom";
                    childRailOffsets[i] = M16LowerGripRailOffset;
                    changed = true;
                }
                else if (legacySocket == "rail_bottom_panel")
                {
                    childSocketIds[i] = "rail_bottom";
                    childRailOffsets[i] = M16LowerPanelRailOffset;
                    changed = true;
                }
                else if (legacySocket == "rail_side_panel")
                {
                    string legacyMount = childMountIds[i];
                    float legacyOffset = childRailOffsets[i];
                    Thing sidePanel = DetachChildAt(i);
                    CompModularWeaponNode upperHandguard = parent?.ParentHolder
                        as CompModularWeaponNode;
                    string reason = null;
                    bool transferred = sidePanel != null
                        && upperHandguard?.Props.SocketNamed("rail_side_upper") != null
                        && upperHandguard.TryAttach(sidePanel, "rail_side_upper", null,
                            M16UpperSidePanelRailOffset, out reason);
                    if (!transferred && sidePanel != null)
                    {
                        float freeOffset;
                        transferred = upperHandguard != null
                            && upperHandguard.TryFindAttachOffset(sidePanel,
                                "rail_side_upper", null, out freeOffset, out reason)
                            && upperHandguard.TryAttach(sidePanel, "rail_side_upper",
                                null, freeOffset, out reason);
                    }

                    if (!transferred && sidePanel != null && children.TryAdd(sidePanel))
                    {
                        childSocketIds.Add(legacySocket);
                        childMountIds.Add(legacyMount);
                        childRailOffsets.Add(legacyOffset);
                        Log.Warning("[Helodrace] Could not move a saved M16 mid panel to "
                            + "the upper side rail; the part was preserved for a later retry. "
                            + reason);
                    }
                    changed = true;
                }
            }

            if (changed) InvalidateTree();
        }

        private void MigrateLegacyFluxStockRailOffset()
        {
            if (fluxStockRailMigrationChecked) return;
            fluxStockRailMigrationChecked = true;
            if (parent?.def?.defName != "HD_ModularPart_Receiver_FluxRaiderKit") return;

            RepairAssignmentLists();
            for (int i = 0; i < children.Count; i++)
            {
                if (childSocketIds[i] != "stock"
                    || children[i]?.def?.defName != "HD_ModularPart_Stock_FluxRaiderKit"
                    || !Mathf.Approximately(childRailOffsets[i], 0f))
                    continue;

                // Before the extension track existed, zero represented the authored
                // placement. Preserve the latest fully extended authoring position.
                childRailOffsets[i] = -0.121f;
                InvalidateTree();
                break;
            }
        }

        private bool ContainsThingRecursive(Thing candidate, int depth)
        {
            if (depth > MaxTreeDepth) return true;
            EnsureContainer();
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                if (child == candidate) return true;
                CompModularWeaponNode comp = child?.TryGetComp<CompModularWeaponNode>();
                if (comp != null && comp.ContainsThingRecursive(candidate, depth + 1)) return true;
            }

            return false;
        }

        private CompModularWeaponNode RootComp()
        {
            CompModularWeaponNode current = this;
            HashSet<CompModularWeaponNode> visited = new HashSet<CompModularWeaponNode>();
            while (current != null && visited.Add(current))
            {
                CompModularWeaponNode parentComp = current.parent?.ParentHolder
                    as CompModularWeaponNode;
                if (parentComp == null) return current;
                current = parentComp;
            }

            return this;
        }

        private void EnsureContainer()
        {
            if (children == null)
                children = new ThingOwner<Thing>(this, false, LookMode.Deep);
        }

        private void RepairAssignmentLists()
        {
            EnsureContainer();
            if (childSocketIds == null) childSocketIds = new List<string>();
            if (childMountIds == null) childMountIds = new List<string>();
            if (childRailOffsets == null) childRailOffsets = new List<float>();

            while (childSocketIds.Count < children.Count) childSocketIds.Add(string.Empty);
            while (childMountIds.Count < children.Count) childMountIds.Add(string.Empty);
            while (childRailOffsets.Count < children.Count) childRailOffsets.Add(0f);
            if (childSocketIds.Count > children.Count)
                childSocketIds.RemoveRange(children.Count, childSocketIds.Count - children.Count);
            if (childMountIds.Count > children.Count)
                childMountIds.RemoveRange(children.Count, childMountIds.Count - children.Count);
            if (childRailOffsets.Count > children.Count)
                childRailOffsets.RemoveRange(children.Count, childRailOffsets.Count - children.Count);
        }

        private bool ValidateRailPlacement(
            ModularAttachmentSocket socket,
            ModularAttachmentMount mount,
            float offset,
            int ignoredChildIndex,
            out string reason)
        {
            reason = null;
            if (!socket.isRail) return true;

            float railLength = Mathf.Max(0f, socket.railLength);
            float occupancy = Mathf.Max(0f, mount.railOccupancy);
            float halfRail = railLength * 0.5f;
            float halfPart = occupancy * 0.5f;
            if (railLength <= 0f || Mathf.Abs(offset) + halfPart > halfRail + 0.0001f)
            {
                reason = mount.Label + " does not fit within rail " + socket.Label + ".";
                return false;
            }

            RepairAssignmentLists();
            float left = offset - halfPart;
            float right = offset + halfPart;
            for (int i = 0; i < children.Count; i++)
            {
                if (i == ignoredChildIndex || childSocketIds[i] != socket.id) continue;
                CompModularWeaponNode otherComp = children[i]
                    ?.TryGetComp<CompModularWeaponNode>();
                ModularAttachmentMount otherMount = otherComp?.Props.MountNamed(childMountIds[i]);
                float otherHalf = Mathf.Max(0f, otherMount?.railOccupancy ?? 0f) * 0.5f;
                float otherLeft = childRailOffsets[i] - otherHalf;
                float otherRight = childRailOffsets[i] + otherHalf;
                if (right > otherLeft + 0.0001f && left < otherRight - 0.0001f)
                {
                    reason = mount.Label + " overlaps " + children[i].LabelCap
                        + " on rail " + socket.Label + ".";
                    return false;
                }
            }

            return true;
        }

        private void SyncDefaultRailOffset(int childIndex, float value)
        {
            if (Props.defaultAttachments.NullOrEmpty()) return;
            Thing child = children[childIndex];
            string socketId = childSocketIds[childIndex];
            int ordinal = 0;
            for (int i = 0; i < childIndex; i++)
                if (childSocketIds[i] == socketId && children[i]?.def == child?.def) ordinal++;

            for (int i = 0; i < Props.defaultAttachments.Count; i++)
            {
                ModularDefaultAttachment entry = Props.defaultAttachments[i];
                if (entry?.socketId != socketId || entry.part != child?.def) continue;
                if (ordinal-- > 0) continue;
                entry.railOffset = value;
                return;
            }
        }
    }
}
