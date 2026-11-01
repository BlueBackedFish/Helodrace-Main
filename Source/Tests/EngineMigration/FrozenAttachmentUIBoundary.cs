using System.Collections.Generic;
using System.Reflection;
using Helodrace.ModernWar;

namespace Helodrace.Tests.FrozenAttachmentUI
{
    internal static class ModularWeaponVisualLayers
    {
        internal static List<ModularRenderNode> Expand(IEnumerable<ModularRenderNode> nodes)
            => (List<ModularRenderNode>)typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponVisualLayers",true).GetMethod("Expand").Invoke(null,new object[]{nodes});
    }

    internal static class GeometryBoundary
    {
        private const BindingFlags Internal=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static System.Numerics.Vector2 ToNative(this UnityEngine.Vector2 v)=>new(v.x,v.y);
        internal static System.Numerics.Vector3 ToNative(this UnityEngine.Vector3 v)=>new(v.x,v.y,v.z);
        internal static UnityEngine.Vector2 ToGame(this System.Numerics.Vector2 v)=>new(v.X,v.Y);
        internal static bool IsLive(CompModularWeaponNode root)=>(bool)typeof(ModularPlacementWorkspace).GetMethod("IsLive",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root});
        internal static List<float> RailOffsetsSnapshot(this CompModularWeaponNode root)=>(List<float>)typeof(CompModularWeaponNode).GetMethod("RailOffsetsSnapshot",Internal).Invoke(root,null);
        internal static void RestoreRailOffsets(this CompModularWeaponNode root,List<float> offsets)=>typeof(CompModularWeaponNode).GetMethod("RestoreRailOffsets",Internal).Invoke(root,new object[]{offsets});
    }
}
