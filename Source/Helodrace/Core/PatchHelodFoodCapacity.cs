using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(Need_Food), "get_MaxLevel")]
    public static class Patch_HelodFoodMaxLevel
    {
        private const float HelodFoodCapacityFactor = 1.2f;

        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn, ref float __result)
        {
            if (___pawn?.def?.defName == "Helod")
            {
                __result *= HelodFoodCapacityFactor;
            }
        }
    }
}
