using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class PawnRenderNode_HelodBody : PawnRenderNode_Body
    {
        public PawnRenderNode_HelodBody(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        public override Graphic GraphicFor(Pawn pawn)
        {
            string path = pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated
                ? "Things/Pawn/Humanlike/Bodies/Dessicated/Dessicated_Thin"
                : "Helod/Bodies/Naked_" + (pawn.DevelopmentalStage.Baby() ? "Baby"
                    : pawn.DevelopmentalStage.Child() ? "Child" : "Female");
            return GraphicDatabase.Get<Graphic_Multi>(path, ShaderFor(pawn), Vector2.one, ColorFor(pawn));
        }
    }

    public enum HelodAppendage { Tail, LeftEar, RightEar }

    public sealed class PawnRenderNodeProperties_HelodAppendage : PawnRenderNodeProperties
    {
        public HelodAppendage appendage;
        public PawnRenderNodeProperties_HelodAppendage()
        {
            nodeClass = typeof(PawnRenderNode_HelodAppendage);
            workerClass = typeof(PawnRenderNodeWorker_HelodAppendage);
            colorType = AttachmentColorType.Hair;
            rotDrawMode = RotDrawMode.Fresh | RotDrawMode.Rotting;
        }
    }

    public sealed class PawnRenderNode_HelodAppendage : PawnRenderNode
    {
        public PawnRenderNode_HelodAppendage(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        public override Graphic GraphicFor(Pawn pawn)
        {
            var props = (PawnRenderNodeProperties_HelodAppendage)Props;
            bool tail = props.appendage == HelodAppendage.Tail;
            return GraphicDatabase.Get<Graphic_Multi>(props.texPath,
                tail ? ShaderDatabase.Cutout : ShaderDatabase.CutoutComplex,
                Vector2.one, pawn.story.HairColor, Color.clear);
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn) => MeshPool.GetMeshSetForSize(1f, 1f);
    }

    public sealed class PawnRenderNodeWorker_HelodAppendage : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms)) return false;
            var kind = ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage;
            if (kind == HelodAppendage.Tail)
            {
                if (!parms.Portrait && parms.pawn.InBed()) return false;
                return HasPart(parms.pawn, HelodRace.TailParts);
            }
            if (parms.flags.FlagSet(PawnRenderFlags.HeadStump)
                || HelodCoveredEarsUtility.IsWearingEarCoveringApparel(parms.pawn)) return false;
            return HasPart(parms.pawn, kind == HelodAppendage.LeftEar
                ? HelodRace.LeftEarParts : HelodRace.RightEarParts);
        }

        private static bool HasPart(Pawn pawn, List<BodyPartRecord> candidates)
        {
            if (pawn?.health?.hediffSet == null) return false;
            for (int i = 0; i < candidates.Count; i++)
                if (!pawn.health.hediffSet.PartIsMissing(candidates[i])) return true;
            return false;
        }

        internal static bool MatchesEar(BodyPartRecord part, string label)
        {
            // customLabel is translated; keep anatomical identity language-independent.
            return part.def.defName == "Ear"
                && (string.IsNullOrEmpty(part.untranslatedCustomLabel)
                    ? part.customLabel : part.untranslatedCustomLabel) == label;
        }

        protected override Material GetMaterial(PawnRenderNode node, PawnDrawParms parms)
        {
            if (parms.flipHead && ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage != HelodAppendage.Tail)
                parms.facing = parms.facing.Opposite;
            return base.GetMaterial(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 result = base.OffsetFor(node, parms, out pivot);
            var kind = ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage;
            Rot4 facing = parms.flipHead && kind != HelodAppendage.Tail ? parms.facing.Opposite : parms.facing;
            // Final offsets include the former framework's default Head/Tail offsets.
            Vector3 offset;
            if (kind == HelodAppendage.Tail)
                offset = facing == Rot4.North ? new Vector3(0f, 0.3f, -0.075f)
                    : facing == Rot4.South ? new Vector3(0.0075f, -0.3f, -0.07f)
                    : new Vector3(0.095f, -0.3f, 0.02f);
            else if (kind == HelodAppendage.LeftEar)
                offset = facing == Rot4.North ? new Vector3(-0.13f, -0.3f, 0.2f)
                    : facing == Rot4.South ? new Vector3(0.13f, 0.3f, 0.18825f)
                    : new Vector3(-0.115f, -0.7f, 0.145f);
            else
                offset = facing == Rot4.North ? new Vector3(0.13f, -0.3f, 0.2f)
                    : facing == Rot4.South ? new Vector3(-0.13f, 0.3f, 0.18825f)
                    : new Vector3(-0.0025f, 0.296f, 0.145f);
            if (facing == Rot4.East) offset.x = -offset.x;
            return result + offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            var kind = ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage;
            float width = kind == HelodAppendage.Tail
                ? HumanlikeMeshPoolUtility.HumanlikeBodyWidthForPawn(parms.pawn)
                : HumanlikeMeshPoolUtility.HumanlikeHeadWidthForPawn(parms.pawn);
            float scale = width * HelodRace.DrawScale;
            return new Vector3(scale, 1f, scale);
        }
    }

    [HarmonyPatch]
    public static class Patch_HelodMeshScale
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(HumanlikeMeshPoolUtility), "GetHumanlikeBodySetForPawn");
            yield return AccessTools.Method(typeof(HumanlikeMeshPoolUtility), "GetHumanlikeHeadSetForPawn");
            yield return AccessTools.Method(typeof(HumanlikeMeshPoolUtility), "GetHumanlikeHairSetForPawn");
            yield return AccessTools.Method(typeof(HumanlikeMeshPoolUtility), "GetHumanlikeBeardSetForPawn");
        }
        public static void Prefix(Pawn pawn, ref float wFactor, ref float hFactor)
        {
            if (!HelodRace.IsHelod(pawn)) return;
            wFactor *= HelodRace.DrawScale;
            hFactor *= HelodRace.DrawScale;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BaseHeadOffsetAt))]
    public static class Patch_HelodHeadOffset
    {
        public static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (!HelodRace.IsHelod(___pawn)) return;
            float age = ___pawn.ageTracker.AgeBiologicalYearsFloat;
            float offset = OffsetForAge(___pawn.gender, age);
            __result.z += offset * Mathf.Sqrt(___pawn.ageTracker.CurLifeStage.bodySizeFactor);
        }

        internal static float OffsetForAge(Gender gender, float age)
        {
            // Preserve the pre-removal lifeStageAges values, including the male overrides.
            return gender == Gender.Female
                ? (age < 3f ? -0.25f : age < 13f ? -0.125f : -0.075f)
                : (age >= 3f && age < 13f ? -1.2f : -0.07f);
        }
    }

    [HarmonyPatch(typeof(Apparel), "get_WornGraphicPath")]
    public static class Patch_HelodApparelPath
    {
        public static void Postfix(Apparel __instance, ref string __result)
        {
            string path = HelodRace.ApparelPath(__instance);
            if (path != null) __result = path;
        }
    }
}
