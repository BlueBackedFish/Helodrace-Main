using System;
using FacialAnimation;
using HarmonyLib;
using Verse;

namespace HelodraceFacial
{
    internal static class BabyFacialPathContext
    {
        [ThreadStatic]
        internal static Pawn CurrentPawn;

        internal static void Begin(ThingComp controller, out Pawn previousPawn)
        {
            previousPawn = CurrentPawn;
            CurrentPawn = controller?.parent as Pawn;
        }

        internal static Exception End(Exception exception, Pawn previousPawn)
        {
            CurrentPawn = previousPawn;
            return exception;
        }
    }

    [HarmonyPatch(typeof(EyeballControllerComp), "LoadTextures")]
    internal static class BabyEyeballLoadContextPatch
    {
        private static void Prefix(EyeballControllerComp __instance, out Pawn __state)
        {
            BabyFacialPathContext.Begin(__instance, out __state);
        }

        private static Exception Finalizer(Exception __exception, Pawn __state)
        {
            return BabyFacialPathContext.End(__exception, __state);
        }
    }

    [HarmonyPatch(typeof(MouthControllerComp), "LoadTextures")]
    internal static class BabyMouthLoadContextPatch
    {
        private static void Prefix(MouthControllerComp __instance, out Pawn __state)
        {
            BabyFacialPathContext.Begin(__instance, out __state);
        }

        private static Exception Finalizer(Exception __exception, Pawn __state)
        {
            return BabyFacialPathContext.End(__exception, __state);
        }
    }

    [HarmonyPatch(typeof(LidControllerComp), "LoadTextures")]
    internal static class BabyLidLoadContextPatch
    {
        private static void Prefix(LidControllerComp __instance, out Pawn __state)
        {
            BabyFacialPathContext.Begin(__instance, out __state);
        }

        private static Exception Finalizer(Exception __exception, Pawn __state)
        {
            return BabyFacialPathContext.End(__exception, __state);
        }
    }

    // LidOptionControllerComp inherits the generic base LoadTextures method.
    // Harmony cannot safely patch that closed generic method, so Helod uses this
    // concrete subclass through its ThingDef comp entry instead.
    public sealed class HelodBabyLidOptionControllerComp : LidOptionControllerComp
    {
        public override void LoadTextures()
        {
            BabyFacialPathContext.Begin(this, out Pawn previousPawn);
            try
            {
                base.LoadTextures();
            }
            finally
            {
                BabyFacialPathContext.End(null, previousPawn);
            }
        }
    }

    [HarmonyPatch(typeof(HeadControllerComp), "LoadTextures")]
    internal static class BabyHeadLoadContextPatch
    {
        private static void Prefix(HeadControllerComp __instance, out Pawn __state)
        {
            BabyFacialPathContext.Begin(__instance, out __state);
        }

        private static Exception Finalizer(Exception __exception, Pawn __state)
        {
            return BabyFacialPathContext.End(__exception, __state);
        }
    }

    [HarmonyPatch(typeof(GraphicHelper), nameof(GraphicHelper.GetTexPathPrefix))]
    internal static class BabyFacialPathPatch
    {
        private static void Postfix(FaceTypeDef __0, ref string __result)
        {
            Pawn pawn = BabyFacialPathContext.CurrentPawn;

            if (pawn?.def?.defName != "Helod" || !pawn.DevelopmentalStage.Baby())
            {
                return;
            }

            ReplacePath(__0, ref __result,
                "Things/Pawn/Helod/Eyes/Normal",
                "Things/Pawn/Helod/Eyes/Baby");
            ReplacePath(__0, ref __result,
                "Things/Pawn/Helod/Lids/Normal",
                "Things/Pawn/Helod/Lids/Baby");
            ReplacePath(__0, ref __result,
                "Things/Pawn/Helod/Mouth/Normal",
                "Things/Pawn/Helod/Mouth/Baby");
            ReplacePath(__0, ref __result,
                "Things/Pawn/Helod/LidOptions/Normal",
                "Things/Pawn/Helod/LidOptions/Baby");
            ReplacePath(__0, ref __result,
                "Things/Pawn/Helod/Heads_Blank/Helod",
                "Things/Pawn/Helod/Heads_Blank/Helod_Baby");
        }

        private static void ReplacePath(
            FaceTypeDef faceTypeDef,
            ref string result,
            string normalPrefix,
            string babyPrefix)
        {
            if (faceTypeDef?.texPath == null
                || !faceTypeDef.texPath.StartsWith(normalPrefix, StringComparison.Ordinal)
                || result == null
                || !result.StartsWith(normalPrefix, StringComparison.Ordinal))
            {
                return;
            }

            result = babyPrefix + result.Substring(normalPrefix.Length);
        }
    }
}
