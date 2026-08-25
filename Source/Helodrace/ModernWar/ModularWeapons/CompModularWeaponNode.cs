using System;
using System.Collections.Generic;
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

        private ThingOwner<Thing> children;
        private List<string> childSocketIds = new List<string>();
        private List<string> childMountIds = new List<string>();
        private List<float> childRailOffsets = new List<float>();
        private List<ModularRenderNode> renderCache;
        private bool renderCacheDirty = true;
        private bool legacyDevelopmentTreeChecked;

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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (childSocketIds == null) childSocketIds = new List<string>();
                if (childMountIds == null) childMountIds = new List<string>();
                if (childRailOffsets == null) childRailOffsets = new List<float>();
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
                List<ModularRenderNode> nodes = RootComp().RenderSnapshot();
                float result = 1f;
                for (int i = 0; i < nodes.Count; i++)
                    result *= Mathf.Max(0.01f, nodes[i].Props.fireDelayMultiplier);
                return result;
            }
        }

        public float EffectiveFireDelaySeconds
        {
            get
            {
                CompModularWeaponNode root = RootComp();
                float configured = MinimumFireDelaySeconds
                    * Mathf.Max(1f, root.Props.baseFireDelayFactor)
                    * InstalledFireDelayMultiplier;
                return Mathf.Max(MinimumFireDelaySeconds, configured);
            }
        }

        public float EffectiveRoundsPerMinute => 60f / EffectiveFireDelaySeconds;

        public int EffectiveBurstIntervalTicks => Mathf.Max(
            Mathf.CeilToInt(MinimumFireDelaySeconds * 60f),
            Mathf.CeilToInt(EffectiveFireDelaySeconds * 60f));

        public void ApplyFireDelayToCooldown(ref float cooldownSeconds)
        {
            cooldownSeconds = EffectiveFireDelaySeconds;
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
            root.MigrateLegacyDevelopmentOptics();
            if (!renderCacheDirty && renderCache != null) return renderCache;

            List<ModularRenderNode> result = new List<ModularRenderNode>();
            HashSet<int> visited = new HashSet<int>();
            ModularTransform2D rootTransform = ModularTransform2D.Identity;
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
            root.renderCacheDirty = true;
            root.renderCache = null;

            Thing rootThing = root.parent;
            if (rootThing != null && rootThing.Spawned && rootThing.Map != null)
            {
                rootThing.Map.mapDrawer.MapMeshDirty(
                    rootThing.Position,
                    MapMeshFlagDefOf.Things);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (!IsTopLevelAssembly || !Props.isAssemblyRoot) yield break;

            if (Props.allowPlayerConfiguration)
            {
                yield return new Command_Action
                {
                    defaultLabel = "HD_ModularWeapon_Command".Translate(),
                    defaultDesc = "HD_ModularWeapon_CommandDesc".Translate(),
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
            if (Props.defaultAttachments.NullOrEmpty() || ChildCount > 0) return;

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
                railStart = railStart,
                railEnd = railEnd,
                occupiedRailStart = occupiedRailStart,
                occupiedRailEnd = occupiedRailEnd
            });

            RepairAssignmentLists();
            MigrateLegacyReceiverRailAssignments();
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
                    mount.transform,
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
