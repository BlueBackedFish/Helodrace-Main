using FacialAnimation;
using HarmonyLib;
using Verse;

namespace HelodraceFacial
{
    [StaticConstructorOnStartup]
    internal static class DeadLidPatchBootstrap
    {
        static DeadLidPatchBootstrap()
        {
            new Harmony("BlueBackedFish.Helodrace.Facial.DeadLid").PatchAll();
        }
    }

    [HarmonyPatch(typeof(NLFacialAnimationPartNode), nameof(NLFacialAnimationPartNode.GraphicFor))]
    internal static class DeadLidGraphicPatch
    {
        private const string HelodRaceDefName = "Helod";
        private const string DeadLidShapeDefName = "dead";

        private static LidShapeDef deadLidShape;

        private static void Prefix(Pawn pawn)
        {
            if (pawn == null || !pawn.Dead || pawn.def?.defName != HelodRaceDefName)
            {
                return;
            }

            LidControllerComp lid = pawn.GetComp<LidControllerComp>();
            if (lid == null)
            {
                return;
            }

            if (deadLidShape == null)
            {
                deadLidShape = DefDatabase<LidShapeDef>.GetNamedSilentFail(DeadLidShapeDefName);
            }

            if (deadLidShape == null)
            {
                return;
            }

            lid.SetShape(deadLidShape);
            HideEyeballs(pawn.GetComp<EyeballControllerComp>());
        }

        private static void HideEyeballs(EyeballControllerComp eyeballs)
        {
            if (eyeballs == null)
            {
                return;
            }

            eyeballs.SetVisible(NLFacialAnimationLayerType.Basic, false);
            eyeballs.SetVisible(NLFacialAnimationLayerType.Cover, false);
            eyeballs.SetVisible(NLFacialAnimationLayerType.L_Basic, false);
            eyeballs.SetVisible(NLFacialAnimationLayerType.L_Option, false);
            eyeballs.SetVisible(NLFacialAnimationLayerType.R_Basic, false);
            eyeballs.SetVisible(NLFacialAnimationLayerType.R_Option, false);
        }
    }
}
