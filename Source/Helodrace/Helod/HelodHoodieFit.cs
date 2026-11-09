using HarmonyLib;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Body), nameof(PawnRenderNodeWorker_Apparel_Body.ScaleFor))]
    public static class Patch_HelodHoodieFit
    {
        public static void Postfix(PawnRenderNode n, PawnDrawParms parms, ref UnityEngine.Vector3 __result)
        {
            if (n?.apparel?.def.defName != "HD_Apparel_Hoodie" || !HelodRace.IsHelod(parms.pawn)
                || HelodBodyVariants.Active(parms.pawn)) return;
            // The original torso is broader than the supplied Huge variant in
            // all three authored views. Widen the garment without moving its hem.
            System.Numerics.Vector3 scale = __result.ToNative();
            scale.X *= 1.16f;
            __result = scale.ToGame();
        }
    }
}
