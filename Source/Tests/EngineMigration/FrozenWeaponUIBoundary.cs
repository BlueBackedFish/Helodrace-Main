using System.Collections.Generic;
using System.Reflection;
using Helodrace.ModernWar;
using Verse;
namespace Helodrace.Tests.FrozenWeaponUI
{
    internal static class GeometryBoundary
    {
        private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static Thing BenchThing(CompModularWeaponWorkbench bench) => bench == null ? null : (Thing)typeof(CompModularWeaponWorkbench).GetProperty("BenchThing",Internal).GetValue(bench);
        internal static List<Pawn> AvailableWeaponOwners(this CompModularWeaponWorkbench bench) => (List<Pawn>)typeof(CompModularWeaponWorkbench).GetMethod("AvailableWeaponOwners",Internal).Invoke(bench,null);
        internal static bool QueueModificationJob(this CompModularWeaponWorkbench bench, Pawn pawn) => (bool)typeof(CompModularWeaponWorkbench).GetMethod("QueueModificationJob",Internal).Invoke(bench,new object[]{pawn});
        internal static float PaletteRatioFor(this CompModularWeaponNode owner, CompModularWeaponNode child) => (float)typeof(CompModularWeaponNode).GetMethod("PaletteRatioFor",Internal).Invoke(owner,new object[]{child});

        internal static System.Numerics.Vector2 ToNative(this UnityEngine.Vector2 v) => new(v.x,v.y);
        internal static System.Numerics.Vector3 ToNative(this UnityEngine.Vector3 v) => new(v.x,v.y,v.z);
        internal static Waylith.Math.Rect ToNative(this UnityEngine.Rect r) => new(r.x,r.y,r.width,r.height);
        internal static Waylith.Math.Color ToNative(this UnityEngine.Color c) => new(c.r,c.g,c.b,c.a);
        internal static UnityEngine.Vector2 ToGame(this System.Numerics.Vector2 v) => new(v.X,v.Y);
        internal static UnityEngine.Vector3 ToGame(this System.Numerics.Vector3 v) => new(v.X,v.Y,v.Z);
        internal static UnityEngine.Rect ToGame(this Waylith.Math.Rect r) => new(r.x,r.y,r.width,r.height);
        internal static UnityEngine.Color ToGame(this Waylith.Math.Color c) => new(c.r,c.g,c.b,c.a);
    }
    internal static class ModularWeaponGasSystemUtility
    {
        private static readonly System.Type Product = typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponGasSystemUtility",true);
        public static float MinimumSetting => (float)Product.GetField("MinimumSetting").GetRawConstantValue();
        public static float MaximumSetting => (float)Product.GetField("MaximumSetting").GetRawConstantValue();
        public static float DefaultSetting => (float)Product.GetField("DefaultSetting").GetRawConstantValue();
    }
    internal static class ModularWeaponChildTransfer
    {
        public static List<Thing> Move(CompModularWeaponNode previous,CompModularWeaponNode replacement)
            => (List<Thing>)typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponChildTransfer",true).GetMethod("Move").Invoke(null,new object[]{previous,replacement});
    }
    internal static class ModularWeaponVisualLayers
    {
        public static IEnumerable<ModularRenderNode> Expand(IEnumerable<ModularRenderNode> snapshot)
            => (IEnumerable<ModularRenderNode>)typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponVisualLayers",true).GetMethod("Expand").Invoke(null,new object[]{snapshot});
    }
}
