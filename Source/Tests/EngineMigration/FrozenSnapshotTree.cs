using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Vector2=System.Numerics.Vector2;
using Verse;
using Helodrace.ModernWar;
namespace Helodrace.Tests.FrozenSnapshot
{
internal static class Boundary
{
    internal static System.Numerics.Vector3 ToNative(this Vector3 value)=>new(value.x,value.y,value.z);
    internal static void TakeBest(this ModularWeaponSightPerformance value,ModularWeaponSightPerformance other)
        => typeof(ModularWeaponSightPerformance).GetMethod("TakeBest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,new object[]{other});
}
internal static class ModularWeaponPartPerformance
{
    internal static ModularRailSurface MountSurface(ModularRenderNode node,List<ModularRenderNode> nodes)
        => (ModularRailSurface)typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponPartPerformance",true).GetMethod("MountSurface",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,new object[]{node,nodes});
}
internal static class Tree
{
private const BindingFlags All=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

internal static List<ModularRenderNode> Snapshot(CompModularWeaponNode owner)
{
 var result=new List<ModularRenderNode>();var transform=ModularTransform2D.Identity;
 transform.scale=Vector2.One*Mathf.Max(.01f,owner.Props.assemblyScale);transform.angle=owner.Props.graphicAngle;
 BuildSnapshotRecursive(owner,result,new HashSet<int>(),transform,"root",null,null,0);
 result.Sort((a,b)=>{int layer=a.GraphicLayer.CompareTo(b.GraphicLayer);return layer!=0?layer:string.CompareOrdinal(a.path,b.path);});return result;
}
internal static ModularAttachmentTransform EffectiveTransform(ModularAttachmentMount mount,ModularAttachmentSocket socket)
{
 ModularAttachmentTransform result=mount.transform?.Clone()??new ModularAttachmentTransform();
 if(mount.UsesOppositeSurface(socket))result.scale.y*=-1f;return result;
}
private static void BuildSnapshotRecursive(CompModularWeaponNode owner,
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
            if (depth > 32 || owner.parent == null || !visited.Add(owner.parent.thingIDNumber))
            {
                Log.ErrorOnce("[Helodrace] Invalid or excessively deep modular weapon tree.",
                    0x48444D57);
                return;
            }

            result.Add(new ModularRenderNode
            {
                path = path,
                depth = depth,
                thing = owner.parent,
                comp = owner,
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

            typeof(CompModularWeaponNode).GetMethod("RepairAssignmentLists", All).Invoke(owner, null);
            var children = (ThingOwner<Thing>)typeof(CompModularWeaponNode).GetField("children", All).GetValue(owner);
            var childSocketIds = (List<string>)typeof(CompModularWeaponNode).GetField("childSocketIds", All).GetValue(owner);
            var childMountIds = (List<string>)typeof(CompModularWeaponNode).GetField("childMountIds", All).GetValue(owner);
            var childRailOffsets = (List<float>)typeof(CompModularWeaponNode).GetField("childRailOffsets", All).GetValue(owner);
            for (int i = 0; i < children.Count; i++)
            {
                Thing child = children[i];
                CompModularWeaponNode childComp = child?.TryGetComp<CompModularWeaponNode>();
                ModularAttachmentSocket socket = owner.Props.SocketNamed(childSocketIds[i]);
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
                float childPaletteRatio = (float)typeof(CompModularWeaponNode).GetMethod("PaletteRatioFor", All).Invoke(owner, new object[]{childComp});
                if (oppositeSurface)
                    effectiveSocket.position += mount.oppositeSurfaceOffset
                        * childPaletteRatio;
                bool childAttachedToRail = socket.isRail;
                Vector2 childRailStart = Vector2.Zero;
                Vector2 childRailEnd = Vector2.Zero;
                Vector2 childOccupiedStart = Vector2.Zero;
                Vector2 childOccupiedEnd = Vector2.Zero;
                if (childAttachedToRail)
                {
                    Vector3 left = socket.transform.position;
                    Vector3 right = socket.transform.position;
                    left.x -= socket.railLength * 0.5f;
                    right.x += socket.railLength * 0.5f;
                    childRailStart = transform.TransformPoint(left.ToNative());
                    childRailEnd = transform.TransformPoint(right.ToNative());

                    float occupiedHalf = Mathf.Max(0f,
                        mount.EffectivePhysicalLength * childPaletteRatio) * 0.5f;
                    left = socket.transform.position;
                    right = socket.transform.position;
                    left.x += childRailOffsets[i]
                        + mount.physicalCenterOffset * childPaletteRatio
                        - occupiedHalf;
                    right.x += childRailOffsets[i]
                        + mount.physicalCenterOffset * childPaletteRatio
                        + occupiedHalf;
                    childOccupiedStart = transform.TransformPoint(left.ToNative());
                    childOccupiedEnd = transform.TransformPoint(right.ToNative());
                }
                ModularTransform2D childTransform = transform.Attach(
                    effectiveSocket.ToNativePose(),
                    EffectiveTransform(mount, socket).ToNativePose(),
                    childComp.Props.graphicAngle,
                    childPaletteRatio);
                BuildSnapshotRecursive(childComp,
                    result,
                    visited,
                    childTransform,
                    path + "/" + socket.id + "[" + child.thingIDNumber + "]",
                    socket.id,
                    mount.id,
                    depth + 1,
                    owner,
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
}
}
