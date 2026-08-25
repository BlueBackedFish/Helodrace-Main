using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class ModularWeaponThing : ThingWithComps
    {
        public override void Print(SectionLayer layer)
        {
            base.Print(layer);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            CompModularWeaponNode comp = GetComp<CompModularWeaponNode>();
            if (comp != null)
                ModularWeaponAssemblyRenderer.DrawRealtime(
                    comp,
                    this,
                    drawLoc,
                    ModularWeaponAssemblyRenderer.GroundExtraRotation(this),
                    flip);
        }
    }

    public sealed class Graphic_ModularWeaponAssembly : Graphic_Single
    {
        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            CompModularWeaponNode comp = (thing as ThingWithComps)
                ?.GetComp<CompModularWeaponNode>();

            if (comp != null)
                ModularWeaponAssemblyRenderer.PrintOutlines(
                    comp, layer, thing, extraRotation, this);

            if (comp != null)
                ModularWeaponAssemblyRenderer.Print(comp, layer, thing, extraRotation, this, true);

            base.Print(layer, thing, extraRotation);

            if (comp != null)
                ModularWeaponAssemblyRenderer.Print(comp, layer, thing, extraRotation, this, false);
        }

        public override Graphic GetColoredVersion(
            Shader newShader,
            Color newColor,
            Color newColorTwo)
        {
            return GraphicDatabase.Get<Graphic_ModularWeaponAssembly>(
                path,
                newShader,
                drawSize,
                newColor,
                newColorTwo,
                data);
        }
    }

    public static class ModularWeaponAssemblyRenderer
    {
        public const float LayerAltitudeStep = 0.0042f;
        private const float OutlineLayerGap = 1f;
        private static readonly Dictionary<ThingDef, Material> outlineMaterials =
            new Dictionary<ThingDef, Material>();

        public static void PrintOutlines(
            CompModularWeaponNode comp,
            SectionLayer layer,
            Thing root,
            float extraRotation,
            Graphic bodyGraphic)
        {
            List<ModularRenderNode> nodes = comp.RenderSnapshot();
            if (nodes.Count == 0) return;

            Rot4 rot = root.Rotation;
            Vector3 baseCenter = root.TrueCenter() + bodyGraphic.DrawOffset(rot);
            bool rotated = bodyGraphic.ShouldDrawRotated;
            bool flip = false;
            float bodyAngle;

            if (rotated)
            {
                bodyAngle = rot.AsAngle + bodyGraphic.DrawRotatedExtraAngleOffset;
                if ((rot == Rot4.West && bodyGraphic.WestFlipped)
                    || (rot == Rot4.East && bodyGraphic.EastFlipped))
                    bodyAngle += 180f;
            }
            else
            {
                bodyAngle = 0f;
                flip = (rot == Rot4.West && bodyGraphic.WestFlipped)
                    || (rot == Rot4.East && bodyGraphic.EastFlipped);
            }

            bodyAngle += extraRotation;
            float stackScale = root.MultipleItemsPerCellDrawn() ? 0.8f : 1f;
            float outlineLayer = LowestGraphicLayer(nodes) - OutlineLayerGap;

            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                Graphic graphic = node.thing.Graphic;
                Material material = OutlineMaterial(node);
                if (graphic == null || material == null) continue;

                Vector2 local = node.GraphicCenter * stackScale;
                if (flip) local.x = -local.x;
                Vector2 worldOffset = RotateForWorldYaw(local, bodyAngle);
                Vector3 center = baseCenter + new Vector3(worldOffset.x, 0f, worldOffset.y);
                center.y += outlineLayer * LayerAltitudeStep + i * 0.000001f;

                Vector2 size = Vector2.Scale(graphic.drawSize, node.GraphicScale) * stackScale;
                if (!rotated && rot.IsHorizontal) size = size.Rotated();
                float drawAngle = bodyAngle + LocalGraphAngleAsWorldYaw(
                    node.GraphicAngle, flip);
                if (flip && bodyGraphic.data != null)
                    drawAngle += bodyGraphic.data.flipExtraRotation;

                Material printMaterial = material;
                Vector2[] uvs = null;
                Color32 vertexColor = new Color32(255, 255, 255, 255);
                Graphic.TryGetTextureAtlasReplacementInfo(
                    material,
                    root.def.category.ToAtlasGroup(),
                    flip,
                    true,
                    out printMaterial,
                    out uvs,
                    out vertexColor);

                Printer_Plane.PrintPlane(
                    layer,
                    center,
                    size,
                    printMaterial,
                    drawAngle,
                    flip,
                    uvs,
                    new[] { vertexColor, vertexColor, vertexColor, vertexColor });
            }
        }

        public static void Print(
            CompModularWeaponNode comp,
            SectionLayer layer,
            Thing root,
            float extraRotation,
            Graphic bodyGraphic,
            bool under)
        {
            List<ModularRenderNode> nodes = comp.RenderSnapshot();
            if (nodes.Count <= 1) return;

            Rot4 rot = root.Rotation;
            Vector3 baseCenter = root.TrueCenter() + bodyGraphic.DrawOffset(rot);
            bool rotated = bodyGraphic.ShouldDrawRotated;
            bool flip = false;
            float bodyAngle;

            if (rotated)
            {
                bodyAngle = rot.AsAngle + bodyGraphic.DrawRotatedExtraAngleOffset;
                if ((rot == Rot4.West && bodyGraphic.WestFlipped)
                    || (rot == Rot4.East && bodyGraphic.EastFlipped))
                    bodyAngle += 180f;
            }
            else
            {
                bodyAngle = 0f;
                flip = (rot == Rot4.West && bodyGraphic.WestFlipped)
                    || (rot == Rot4.East && bodyGraphic.EastFlipped);
            }

            bodyAngle += extraRotation;
            float stackScale = root.MultipleItemsPerCellDrawn() ? 0.8f : 1f;

            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                if (node.depth == 0 || (node.GraphicLayer < 0f) != under) continue;
                Graphic graphic = node.thing.Graphic;
                Material material = graphic?.MatSingleFor(node.thing);
                if (material == null) continue;

                Vector2 local = node.GraphicCenter * stackScale;
                if (flip) local.x = -local.x;
                Vector2 worldOffset = RotateForWorldYaw(local, bodyAngle);
                Vector3 center = baseCenter + new Vector3(worldOffset.x, 0f, worldOffset.y);
                center.y += node.GraphicLayer * LayerAltitudeStep;

                Vector2 graphicScale = node.GraphicScale;
                Vector2 size = Vector2.Scale(graphic.drawSize, graphicScale) * stackScale;
                if (!rotated && rot.IsHorizontal) size = size.Rotated();

                float drawAngle = bodyAngle + LocalGraphAngleAsWorldYaw(
                    node.GraphicAngle, flip);
                if (flip && bodyGraphic.data != null)
                    drawAngle += bodyGraphic.data.flipExtraRotation;

                Material printMaterial = material;
                Vector2[] uvs = null;
                Color32 vertexColor = new Color32(255, 255, 255, 255);
                Graphic.TryGetTextureAtlasReplacementInfo(
                    material,
                    root.def.category.ToAtlasGroup(),
                    flip,
                    true,
                    out printMaterial,
                    out uvs,
                    out vertexColor);

                Printer_Plane.PrintPlane(
                    layer,
                    center,
                    size,
                    printMaterial,
                    drawAngle,
                    flip,
                    uvs,
                    new[] { vertexColor, vertexColor, vertexColor, vertexColor });
            }
        }

        public static void DrawRealtime(
            CompModularWeaponNode comp,
            Thing root,
            Vector3 drawLoc,
            float bodyAngle,
            bool flipped)
        {
            DrawRealtime(comp, root, drawLoc, bodyAngle, flipped,
                flipped ? MeshPool.plane10Flip : MeshPool.plane10);
        }

        private static void DrawRealtime(
            CompModularWeaponNode comp,
            Thing root,
            Vector3 drawLoc,
            float bodyAngle,
            bool flipped,
            Mesh mesh)
        {
            List<ModularRenderNode> nodes = comp.RenderSnapshot();
            DrawRealtimeOutlines(nodes, drawLoc, bodyAngle, flipped, mesh);
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                if (node.depth == 0) continue;

                Graphic graphic = node.thing.Graphic;
                Material material = graphic?.MatSingleFor(node.thing);
                if (material == null) continue;

                Vector2 local = node.GraphicCenter;
                if (flipped) local.x = -local.x;
                Vector2 worldOffset = RotateForWorldYaw(local, bodyAngle);
                Vector3 position = drawLoc + new Vector3(worldOffset.x, 0f, worldOffset.y);
                position.y += node.GraphicLayer * LayerAltitudeStep;

                Vector2 scale = node.GraphicScale;
                Vector3 size = new Vector3(
                    graphic.drawSize.x * scale.x,
                    0f,
                    graphic.drawSize.y * scale.y);
                float localAngle = LocalGraphAngleAsWorldYaw(
                    node.GraphicAngle, flipped);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    position,
                    Quaternion.AngleAxis(bodyAngle + localAngle, Vector3.up),
                    size);
                Graphics.DrawMesh(mesh, matrix, material, 0);
            }
        }

        private static void DrawRealtimeOutlines(
            List<ModularRenderNode> nodes,
            Vector3 drawLoc,
            float bodyAngle,
            bool flipped,
            Mesh mesh)
        {
            float outlineLayer = LowestGraphicLayer(nodes) - OutlineLayerGap;
            for (int i = 0; i < nodes.Count; i++)
            {
                ModularRenderNode node = nodes[i];
                Graphic graphic = node.thing.Graphic;
                Material material = OutlineMaterial(node);
                if (graphic == null || material == null) continue;

                Vector2 local = node.GraphicCenter;
                if (flipped) local.x = -local.x;
                Vector2 worldOffset = RotateForWorldYaw(local, bodyAngle);
                Vector3 position = drawLoc + new Vector3(worldOffset.x, 0f, worldOffset.y);
                position.y += outlineLayer * LayerAltitudeStep + i * 0.000001f;

                Vector2 scale = node.GraphicScale;
                Vector3 size = new Vector3(
                    graphic.drawSize.x * scale.x,
                    0f,
                    graphic.drawSize.y * scale.y);
                float localAngle = LocalGraphAngleAsWorldYaw(
                    node.GraphicAngle, flipped);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    position,
                    Quaternion.AngleAxis(bodyAngle + localAngle, Vector3.up),
                    size);
                Graphics.DrawMesh(mesh, matrix, material, 0);
            }
        }

        private static float LowestGraphicLayer(List<ModularRenderNode> nodes)
        {
            float result = 0f;
            for (int i = 0; i < nodes.Count; i++)
                result = Mathf.Min(result, nodes[i].GraphicLayer);
            return result;
        }

        // ModularTransform2D uses the editor's mathematical X/Z convention. Unity's
        // positive yaw turns the opposite way on the X/Z plane, so world placement must
        // use the same Quaternion that rotates the rendered mesh. Otherwise a part's
        // centre and its pixels orbit in opposite directions; the distant stock exposes
        // the error most clearly.
        private static Vector2 RotateForWorldYaw(Vector2 value, float degrees)
        {
            Vector3 rotated = Quaternion.AngleAxis(degrees, Vector3.up)
                * new Vector3(value.x, 0f, value.y);
            return new Vector2(rotated.x, rotated.z);
        }

        private static float LocalGraphAngleAsWorldYaw(float graphAngle, bool flipped)
        {
            return flipped ? graphAngle : -graphAngle;
        }

        private static Material OutlineMaterial(ModularRenderNode node)
        {
            ThingDef def = node?.thing?.def;
            if (def == null) return null;

            Material material;
            if (outlineMaterials.TryGetValue(def, out material)) return material;

            string basePath = def.graphicData?.texPath;
            string outlinePath = basePath.NullOrEmpty() ? null : basePath + "_Outline";
            Texture2D texture = outlinePath.NullOrEmpty()
                ? null
                : ContentFinder<Texture2D>.Get(outlinePath, false);
            material = texture == null
                ? null
                : MaterialPool.MatFrom(outlinePath, ShaderDatabase.Cutout, Color.white);
            outlineMaterials[def] = material;
            return material;
        }

        public static void DrawAiming(Thing equipment, Vector3 drawLoc, float aimAngle)
        {
            CompModularWeaponNode comp = (equipment as ThingWithComps)
                ?.GetComp<CompModularWeaponNode>();
            if (comp == null || !comp.Props.isAssemblyRoot) return;

            float angle = aimAngle - 90f;
            bool flipped = false;
            Mesh mesh = MeshPool.plane10;

            if (aimAngle > 20f && aimAngle < 160f)
            {
                angle += equipment.def.equippedAngleOffset;
            }
            else if (aimAngle > 200f && aimAngle < 340f)
            {
                mesh = MeshPool.plane10Flip;
                angle -= 180f;
                angle -= equipment.def.equippedAngleOffset;
                flipped = true;
            }
            else
            {
                angle += equipment.def.equippedAngleOffset;
            }

            CompEquippable equippable = equipment.TryGetComp<CompEquippable>();
            if (equippable != null)
            {
                Vector3 ignored;
                float recoilAngle;
                EquipmentUtility.Recoil(
                    equipment.def,
                    EquipmentUtility.GetRecoilVerb(equippable.AllVerbs),
                    out ignored,
                    out recoilAngle,
                    aimAngle);
                angle += recoilAngle;
            }

            DrawRealtime(comp, equipment, drawLoc, angle % 360f, flipped, mesh);
        }

        public static float GroundExtraRotation(Thing root)
        {
            Graphic_RandomRotated random = root?.Graphic as Graphic_RandomRotated;
            if (random == null) return 0f;

            float max = root.def.graphicData?.onGroundRandomRotateAngle ?? 0f;
            if (max <= 0.01f) return 0f;
            return -max + (float)(root.thingIDNumber * 542) % (max * 2f);
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Patch_DrawEquipmentAiming_ModularWeaponAssembly
    {
        [HarmonyPostfix]
        public static void Postfix(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            ModularWeaponAssemblyRenderer.DrawAiming(eq, drawLoc, aimAngle);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_PawnGizmos_ModularWeaponEditor
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            if (values != null)
            {
                foreach (Gizmo gizmo in values) yield return gizmo;
            }

            CompModularWeaponNode comp = __instance?.equipment?.Primary
                ?.GetComp<CompModularWeaponNode>();
            if (comp == null || !comp.Props.isAssemblyRoot
                || __instance.Faction != Faction.OfPlayer) yield break;

            if (comp.Props.allowPlayerConfiguration)
            {
                yield return new Command_Action
                {
                    defaultLabel = "HD_ModularWeapon_Command".Translate(),
                    defaultDesc = "HD_ModularWeapon_CommandDesc".Translate(),
                    icon = comp.parent.def.uiIcon,
                    action = () => Find.WindowStack.Add(new Dialog_ModularWeapon(comp))
                };
            }

            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "DEV: attachment editor",
                defaultDesc = "Edit the equipped modular weapon's pivots, mounts and sockets.",
                icon = TexCommand.DesirePower,
                action = () => Find.WindowStack.Add(new Dialog_ModularAttachmentEditor(comp))
            };
        }
    }
}
