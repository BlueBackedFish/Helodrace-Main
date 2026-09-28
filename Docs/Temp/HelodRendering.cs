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

    // ==========================================
    // [USER EDIT] 아기 전용 얼굴 텍스처
    // ==========================================

    public sealed class PawnRenderNode_HelodHead : PawnRenderNode_Head
    {
        public PawnRenderNode_HelodHead(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            // 머리가 없으면 렌더링하지 않음
            if (!pawn.health.hediffSet.HasHead)
                return null;

            // 해골 상태는 기존 바닐라 처리 유지
            if (pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated)
                return HeadTypeDefOf.Skull.GetGraphic(pawn, Color.white);

            if (pawn.story?.headType == null)
                return null;

            // 아기가 아니면 기존 얼굴 그대로 사용
            if (!pawn.DevelopmentalStage.Baby())
                return pawn.story.headType.GetGraphic(pawn, ColorFor(pawn));

            // 기존 얼굴 경로에서 파일 이름만 가져옴
            // 예:
            // Helod/Heads/HelodHead010101
            //               ↓
            // HelodHead010101

            string normalPath = pawn.story.headType.graphicPath;

            int slash = normalPath.LastIndexOf('/');

            string headName = slash >= 0
                ? normalPath.Substring(slash + 1)
                : normalPath;

            // 아기용 폴더
            string babyPath = "Helod/Heads/Baby/" + headName;

            // 아직 아기용 텍스처가 없는 얼굴이면
            // 기존 성인 얼굴을 대신 사용
            if (ContentFinder<Texture2D>.Get(babyPath + "_south", false) == null)
                return pawn.story.headType.GetGraphic(pawn, ColorFor(pawn));

            Shader shader = pawn.Drawer.renderer.StatueColor.HasValue
                ? ShaderDatabase.Cutout
                : ShaderUtility.GetSkinShader(pawn);

            return GraphicDatabase.Get<Graphic_Multi>(
                babyPath,
                shader,
                Vector2.one,
                ColorFor(pawn));
        }
    }

    // [USER EDIT] 헤로드 아기도 머리카락을 렌더링하도록 함
    public sealed class PawnRenderNode_HelodHair : PawnRenderNode_Hair
    {
        public PawnRenderNode_HelodHair(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (pawn.story?.hairDef == null || pawn.story.hairDef.noGraphic)
                return null;

            return pawn.story.hairDef.GraphicFor(pawn, ColorFor(pawn));
        }
    } // 여기까지

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
            var props = (PawnRenderNodeProperties_HelodAppendage)Props; // 여기부터 애기랑 으린이 꼬리 넣기 시도
            bool tail = props.appendage == HelodAppendage.Tail;

            string texPath = props.texPath;

            if (tail && pawn.DevelopmentalStage.Baby())
                {
                    texPath = "Helod/Tails/HelodTail_Baby";
                }
                
                else if (tail && pawn.DevelopmentalStage.Child())
                
                {
                    texPath = "Helod/Tails/HelodTail_Child";
                }

            return GraphicDatabase.Get<Graphic_Multi>(
             texPath,
             tail ? ShaderDatabase.Cutout : ShaderDatabase.CutoutComplex,
             Vector2.one,
             pawn.story.HairColor,
             Color.clear); // 여기까지
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn) => MeshPool.GetMeshSetForSize(1f, 1f);
    }

    public sealed class PawnRenderNodeWorker_HelodAppendage : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms)) return false;
            var renderKind = ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage; // 포밍레인 더함. 이게 머시여
            var kind = AnatomicalKind(renderKind, parms);
            if (kind == HelodAppendage.Tail)
            {
                //if (!parms.Portrait && parms.pawn.InBed()) return false;
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

        // [포밍레인 더함] 머리가 반전될 때 귀의 해부학적 좌우도 교환한당
        private static HelodAppendage AnatomicalKind(
            HelodAppendage kind,
            PawnDrawParms parms)
        {
            if (parms.facing != Rot4.West)
             return kind;

            if (kind == HelodAppendage.LeftEar)
                return HelodAppendage.RightEar;

            if (kind == HelodAppendage.RightEar)
                return HelodAppendage.LeftEar;

            return kind;
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

    // 렌더 트리에 적혀 있는 귀 종류.
    // 화면에 그려지는 LeftEar / RightEar 노드 자체를 의미함.
    var renderKind =
        ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage;

    // 실제 신체 기준 왼쪽 귀 / 오른쪽 귀.
    // 머리가 좌우 반전되었을 경우 서로 교환됨.
    var anatomicalKind = AnatomicalKind(renderKind, parms);

    // 캐릭터가 실제로 바라보고 있는 방향.
    Rot4 actualFacing = parms.facing;

    // 그래픽을 그릴 때 사용하는 방향.
    // 귀는 flipHead 상태일 때 East/West가 반전될 수 있음.
    Rot4 facing =
        parms.flipHead && renderKind != HelodAppendage.Tail
            ? parms.facing.Opposite
            : parms.facing;

    // 기본 성인 귀/꼬리 위치
    Vector3 offset;

    if (renderKind == HelodAppendage.Tail)
    {
        offset =
            facing == Rot4.North
                ? new Vector3(0f, 0.3f, -0.075f)
            : facing == Rot4.South
                ? new Vector3(0f, -0.3f, -0.07f)
            : new Vector3(0.095f, -0.3f, 0.02f);
    }
    else if (renderKind == HelodAppendage.LeftEar)
    {
        offset =
            facing == Rot4.North
                ? new Vector3(-0.13f, -0.3f, 0.2f)
            : facing == Rot4.South
                ? new Vector3(0.13f, 0.3f, 0.18825f)
            : new Vector3(-0.115f, -0.7f, 0.145f);
    }
    else
    {
        offset =
            facing == Rot4.North
                ? new Vector3(0.13f, -0.3f, 0.2f)
            : facing == Rot4.South
                ? new Vector3(-0.13f, 0.3f, 0.18825f)
            : new Vector3(-0.03625f, 0.296f, 0.1475f);
    }

    // 기존 East 방향 좌우 반전
    if (facing == Rot4.East)
        offset.x = -offset.x;


    // ==========================================
    // [포밍레인 더함] 어린이 꼬리 위치 보정
    // ==========================================

    if (renderKind == HelodAppendage.Tail
        && parms.pawn.DevelopmentalStage.Child())
    {
        if (actualFacing == Rot4.South)
            offset += new Vector3(0f, 0f, 0.075f);

        else if (actualFacing == Rot4.North)
            offset += new Vector3(0f, 0f, 0.075f);

        else if (actualFacing == Rot4.East)
            offset += new Vector3(0.025f, 0f, 0.02f);

        else if (actualFacing == Rot4.West)
            offset += new Vector3(-0.025f, 0f, 0.02f);
    }

    // ==========================================
    // [포밍레인 더함] 아기 꼬리 위치 보정
    // ==========================================

    if (renderKind == HelodAppendage.Tail
        && parms.pawn.DevelopmentalStage.Baby())
    {
        if (actualFacing == Rot4.South)
            offset += new Vector3(0f, 0f, 0.025f);

        else if (actualFacing == Rot4.North)
            offset += new Vector3(0f, 0f, 0.025f);

        else if (actualFacing == Rot4.East)
            offset += new Vector3(0.07f, 0f, -0.05f);

        else if (actualFacing == Rot4.West)
            offset += new Vector3(-0.07f, 0f, -0.05f);
    }


    // ==========================================
    // [USER EDIT] 어린이 귀 위치 보정
    // anatomicalKind = 실제 신체 기준 좌/우 귀
    // actualFacing   = 실제로 바라보는 방향
    // ==========================================

    if (parms.pawn.DevelopmentalStage.Child())
    {
        // 실제 신체 기준 왼쪽 귀
        if (anatomicalKind == HelodAppendage.LeftEar)
        {
            if (actualFacing == Rot4.South)
                offset += new Vector3(-0.035f, 0f, -0.05f);

            else if (actualFacing == Rot4.North)
                offset += new Vector3(0.035f, 0f, -0.05f);

            else if (actualFacing == Rot4.East)
                offset += new Vector3(-0.025f, 0f, -0.03f);

            else if (actualFacing == Rot4.West)
                offset += new Vector3(0.0075f, 0f, -0.0375f);
        }

        // 실제 신체 기준 오른쪽 귀
        else if (anatomicalKind == HelodAppendage.RightEar)
        {
            if (actualFacing == Rot4.South)
                offset += new Vector3(0.035f, 0f, -0.05f);

            else if (actualFacing == Rot4.North)
                offset += new Vector3(-0.035f, 0f, -0.05f);

            else if (actualFacing == Rot4.East)
                offset += new Vector3(-0.0075f, 0f, -0.0375f);

            else if (actualFacing == Rot4.West)
                offset += new Vector3(0.025f, 0f, -0.03f);
        }
    }

    // ==========================================
    // [USER EDIT] 아기 귀 위치 보정
    // anatomicalKind = 실제 신체 기준 좌/우 귀
    // actualFacing   = 실제로 바라보는 방향
    // ==========================================

    if (parms.pawn.DevelopmentalStage.Baby())
    {
        // 실제 신체 기준 왼쪽 귀
        if (anatomicalKind == HelodAppendage.LeftEar)
        {
            if (actualFacing == Rot4.South)
                offset += new Vector3(-0.065f, 0f, -0.0975f);

            else if (actualFacing == Rot4.North)
                offset += new Vector3(0.065f, 0f, -0.0975f);

            else if (actualFacing == Rot4.East)
                offset += new Vector3(-0.055f, 0f, -0.07f);

            else if (actualFacing == Rot4.West)
                offset += new Vector3(0.0175f, 0f, -0.075f);
        }

        // 실제 신체 기준 오른쪽 귀
        else if (anatomicalKind == HelodAppendage.RightEar)
        {
            if (actualFacing == Rot4.South)
                offset += new Vector3(0.065f, 0f, -0.0975f);

            else if (actualFacing == Rot4.North)
                offset += new Vector3(-0.065f, 0f, -0.0975f);

            else if (actualFacing == Rot4.East)
                offset += new Vector3(-0.0175f, 0f, -0.075f);

            else if (actualFacing == Rot4.West)
                offset += new Vector3(0.055f, 0f, -0.07f);
        }
    }

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
                ? (age < 3f ? -0.5f : age < 13f ? -0.125f : -0.075f)
                : (age >= 3f && age < 13f ? -1.2f : -0.07f);
        }
    }

    // ==========================================
    // [USER EDIT] 아기가 침대에 있을 때 위치 보정
    // ==========================================

    [HarmonyPatch(typeof(PawnRenderer), "GetBodyPos")]
    public static class Patch_HelodBabyBedOffset
    {
        public static void Postfix(
            Pawn ___pawn,
            PawnPosture posture,
            ref Vector3 __result)
        {
            if (!HelodRace.IsHelod(___pawn))
                return;

            if (!___pawn.DevelopmentalStage.Baby())
                return;

            if (posture != PawnPosture.LayingInBed)
                return;

            // 침대에서 아기 전체를 화면 아래쪽으로 이동
            __result.z -= 0.3f;
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
