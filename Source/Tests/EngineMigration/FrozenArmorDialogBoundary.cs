using System.Reflection;
using Helodrace.ModernWar;
using Verse;

namespace Helodrace.Tests.FrozenArmorUI
{
    // Keep the original internal product entry points in the frozen test body
    // without widening their production visibility.
    internal static class GeometryBoundary
    {
        private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static Waylith.Math.Rect ToNative(this UnityEngine.Rect r) => new(r.x,r.y,r.width,r.height);
        internal static UnityEngine.Rect ToGame(this Waylith.Math.Rect r) => new(r.x,r.y,r.width,r.height);
        internal static string ConfigurationSignature(this CompModularArmor armor)
            => (string)typeof(CompModularArmor).GetMethod("ConfigurationSignature",Internal).Invoke(armor,null);
        internal static CompModularArmor CreateEditorDraft(this CompModularArmor armor)
            => (CompModularArmor)typeof(CompModularArmor).GetMethod("CreateEditorDraft",Internal).Invoke(armor,null);
        internal static bool CanCustomize(this CompClothingWorkbench bench, CompModularArmor armor)
            => (bool)typeof(CompClothingWorkbench).GetMethod("CanCustomize",Internal).Invoke(bench,new object[] {armor});
        internal static bool TryStartCustomization(this CompModularArmor armor, CompModularArmor draft, string baseline, Thing bench, out string rejection)
        {
            object[] args={draft,baseline,bench,null};
            bool result=(bool)typeof(CompModularArmor).GetMethod("TryStartCustomization",Internal).Invoke(armor,args);
            rejection=(string)args[3]; return result;
        }
    }
}
