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

    public sealed class PawnRenderNode_HelodHead : PawnRenderNode_Head
    {
        public PawnRenderNode_HelodHead(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (!pawn.health.hediffSet.HasHead) return null;
            if (pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated)
                return HeadTypeDefOf.Skull.GetGraphic(pawn, Color.white);
            if (pawn.story?.headType == null) return null;
            if (!pawn.DevelopmentalStage.Baby())
                return pawn.story.headType.GetGraphic(pawn, ColorFor(pawn));

            string normalPath = pawn.story.headType.graphicPath;
            int slash = normalPath.LastIndexOf('/');
            string headName = slash >= 0 ? normalPath.Substring(slash + 1) : normalPath;
            string babyPath = "Helod/Heads/Baby/" + headName;
            if (ContentFinder<Texture2D>.Get(babyPath + "_south", false) == null)
                return pawn.story.headType.GetGraphic(pawn, ColorFor(pawn));

            Shader shader = pawn.Drawer.renderer.StatueColor.HasValue
                ? ShaderDatabase.Cutout : ShaderUtility.GetSkinShader(pawn);
            return GraphicDatabase.Get<Graphic_Multi>(babyPath, shader, Vector2.one, ColorFor(pawn));
        }
    }

    public sealed class PawnRenderNode_HelodHair : PawnRenderNode_Hair
    {
        public PawnRenderNode_HelodHair(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree) { }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (pawn.story?.hairDef == null || pawn.story.hairDef.noGraphic) return null;
            return pawn.story.hairDef.GraphicFor(pawn, ColorFor(pawn));
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
            string texPath = props.texPath;
            if (tail && pawn.DevelopmentalStage.Baby())
                texPath = "Helod/Tails/HelodTail_Baby";
            else if (tail && pawn.DevelopmentalStage.Child())
                texPath = "Helod/Tails/HelodTail_Child";
            return GraphicDatabase.Get<Graphic_Multi>(texPath,
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
            var kind = AnatomicalKind(
                ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage, parms.facing);
            if (kind == HelodAppendage.Tail)
            {
                // Sleeping spots are beds internally, but their Def explicitly asks the
                // renderer to show the sleeper. Only suppress the tail when the bed hides
                // the pawn body under its own graphic.
                if (!parms.Portrait && parms.pawn.InBed())
                {
                    Building_Bed bed = parms.pawn.CurrentBed();
                    if (bed == null || bed.def?.building == null
                        || !bed.def.building.bed_showSleeperBody) return false;
                }
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

        private static HelodAppendage AnatomicalKind(HelodAppendage kind, Rot4 facing)
        {
            if (facing != Rot4.West) return kind;
            if (kind == HelodAppendage.LeftEar) return HelodAppendage.RightEar;
            if (kind == HelodAppendage.RightEar) return HelodAppendage.LeftEar;
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
            var renderKind = ((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage;
            Rot4 facing = parms.flipHead && renderKind != HelodAppendage.Tail
                ? parms.facing.Opposite : parms.facing;
            HelodAppendageOffset entry = ConfiguredOffsetFor(renderKind);
            if (entry == null) return result;
            Vector3 offset = DirectionalOffsetFor(entry, facing);
            HelodAppendage anatomicalKind = AnatomicalKind(renderKind, parms.facing);
            HelodAppendageOffset developmentalEntry = ConfiguredOffsetFor(anatomicalKind);
            if (developmentalEntry != null)
                offset += DevelopmentalOffsetFor(developmentalEntry,
                    parms.pawn.DevelopmentalStage, parms.facing);
            return result + offset;
        }

        internal static bool TryGetConfiguredOffset(
            HelodAppendage kind, Rot4 facing, out Vector3 offset)
        {
            offset = Vector3.zero;
            HelodAppendageOffset entry = ConfiguredOffsetFor(kind);
            if (entry == null) return false;
            offset = DirectionalOffsetFor(entry, facing);
            return true;
        }

        private static HelodAppendageOffset ConfiguredOffsetFor(HelodAppendage kind)
        {
            var entries = HelodRace.Settings?.appendageOffsets;
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                HelodAppendageOffset entry = entries[i];
                if (entry == null || entry.appendage != kind) continue;
                return entry;
            }
            return null;
        }

        private static Vector3 DirectionalOffsetFor(HelodAppendageOffset entry, Rot4 facing)
        {
            return facing == Rot4.North ? entry.north
                : facing == Rot4.South ? entry.south
                : facing == Rot4.East ? entry.east : entry.west;
        }

        internal static Vector3 DevelopmentalOffsetFor(
            HelodAppendageOffset entry, DevelopmentalStage stage, Rot4 facing)
        {
            if (stage.Baby())
                return facing == Rot4.North ? entry.babyNorth
                    : facing == Rot4.South ? entry.babySouth
                    : facing == Rot4.East ? entry.babyEast : entry.babyWest;
            if (stage.Child())
                return facing == Rot4.North ? entry.childNorth
                    : facing == Rot4.South ? entry.childSouth
                    : facing == Rot4.East ? entry.childEast : entry.childWest;
            return Vector3.zero;
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
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (!HelodRace.IsHelod(___pawn)) return;
            float age = ___pawn.ageTracker.AgeBiologicalYearsFloat;
            HelodHeadOffset entry = OffsetForAge(age);
            if (entry == null) return;
            float value = ___pawn.gender == Gender.Female ? entry.female : entry.male;
            if (entry.absolute)
                __result.z = value;
            else
                __result.z += value * Mathf.Sqrt(___pawn.ageTracker.CurLifeStage.bodySizeFactor);
        }

        internal static HelodHeadOffset OffsetForAge(float age)
        {
            var entries = HelodRace.Settings?.headOffsets;
            HelodHeadOffset selected = null;
            if (entries != null)
                foreach (var entry in entries)
                    if (entry != null && age >= entry.minAge
                        && (selected == null || entry.minAge >= selected.minAge))
                        selected = entry;
            // Missing settings leave the vanilla head offset unchanged.
            return selected;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetBodyPos")]
    public static class Patch_HelodBabyBedOffset
    {
        public static void Postfix(Pawn ___pawn, PawnPosture posture, ref Vector3 __result)
        {
            if (HelodRace.IsHelod(___pawn) && ___pawn.DevelopmentalStage.Baby()
                && posture == PawnPosture.LayingInBed)
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
