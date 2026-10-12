using HarmonyLib;
using Verse;

namespace Helodrace
{
    // Native pack graphics recognise only Belt. Keep the existing medibag
    // texture path/pack rendering when its independent wear layer is used.
    [HarmonyPatch(typeof(ApparelLayerDef), nameof(ApparelLayerDef.IsUtilityLayer), MethodType.Getter)]
    public static class Patch_MedicalBagUtilityLayer
    {
        public static void Postfix(ApparelLayerDef __instance, ref bool __result)
        {
            if (__instance.defName == "HD_MedicalBag") __result = true;
        }
    }
}
