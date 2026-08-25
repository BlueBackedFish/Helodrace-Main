using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class PawnRenderNode_ModularArmorPart : PawnRenderNode_Apparel
    {
        public readonly CompModularArmor Comp;
        public readonly InstalledModularArmorPart Installed;
        public readonly bool NorthUnderlay;

        public GraphicData NodeGraphicData => NorthUnderlay
            ? Installed.part.northUnderGraphicData
            : Installed.part.graphicData;

        public PawnRenderNode_ModularArmorPart(
            Pawn pawn,
            PawnRenderTree tree,
            CompModularArmor comp,
            InstalledModularArmorPart installed,
            bool northUnderlay = false)
            : base(
                pawn,
                BuildProperties(installed, northUnderlay),
                tree,
                comp.Apparel)
        {
            Comp = comp;
            Installed = installed;
            NorthUnderlay = northUnderlay;
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            return NodeGraphicData.GraphicColoredFor(Comp.parent);
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            yield return GraphicFor(pawn);
        }

        public override Color ColorFor(Pawn pawn)
        {
            return Comp.parent.DrawColor;
        }

        private static PawnRenderNodeProperties BuildProperties(
            InstalledModularArmorPart installed,
            bool northUnderlay)
        {
            GraphicData graphicData = northUnderlay
                ? installed.part.northUnderGraphicData
                : installed.part.graphicData;
            ModularArmorPositionDef position = installed.EffectivePosition;
            return new PawnRenderNodeProperties
            {
                nodeClass = typeof(PawnRenderNode_ModularArmorPart),
                workerClass = typeof(PawnRenderNodeWorker_ModularArmorPart),
                parentTagDef = northUnderlay
                    ? installed.part.northUnderParentTagDef ?? position.parentTagDef
                    : position.parentTagDef,
                useGraphic = true,
                baseLayer = northUnderlay
                    ? installed.part.northUnderDrawLayer
                    : installed.part.drawLayer
                        + position.drawLayer
                        + (installed.palsPanel?.drawLayer ?? 0f),
                drawSize = graphicData.drawSize,
                debugLabel = installed.part.defName
                    + (northUnderlay ? " (north underlay)" : string.Empty)
            };
        }
    }

    [HarmonyPatch(
        typeof(DynamicPawnRenderNodeSetup_Apparel),
        nameof(DynamicPawnRenderNodeSetup_Apparel.GetDynamicNodes))]
    public static class Patch_DynamicPawnRenderNodeSetup_ModularArmor
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn pawn,
            PawnRenderTree tree,
            ref IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> __result)
        {
            __result = AppendModularArmorNodes(__result, pawn, tree);
        }

        private static IEnumerable<(PawnRenderNode node, PawnRenderNode parent)>
            AppendModularArmorNodes(
                IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> original,
                Pawn pawn,
                PawnRenderTree tree)
        {
            if (original != null)
            {
                foreach ((PawnRenderNode node, PawnRenderNode parent) pair in original)
                {
                    yield return pair;
                }
            }

            if (pawn?.apparel?.WornApparel == null || tree == null)
            {
                yield break;
            }

            for (int apparelIndex = 0;
                apparelIndex < pawn.apparel.WornApparel.Count;
                apparelIndex++)
            {
                CompModularArmor comp = pawn.apparel.WornApparel[apparelIndex]
                    ?.TryGetComp<CompModularArmor>();
                List<PawnRenderNode> nodes = comp?.RenderNodes(tree);
                if (nodes.NullOrEmpty())
                {
                    continue;
                }

                for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    PawnRenderNode node = nodes[nodeIndex];
                    PawnRenderNode parent = null;
                    PawnRenderNodeTagDef parentTag = node?.Props?.parentTagDef;
                    if (node != null
                        && parentTag != null
                        && tree.TryGetNodeByTag(parentTag, out parent))
                    {
                        yield return (node, parent);
                    }
                }
            }
        }
    }

    public sealed class PawnRenderNodeWorker_ModularArmorPart : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_ModularArmorPart modularNode =
                (PawnRenderNode_ModularArmorPart)node;
            return base.CanDrawNow(node, parms)
                && (!modularNode.NorthUnderlay || parms.facing == Rot4.North);
        }

        public override Vector3 OffsetFor(
            PawnRenderNode node,
            PawnDrawParms parms,
            out Vector3 pivot)
        {
            Vector3 result = base.OffsetFor(node, parms, out pivot);
            PawnRenderNode_ModularArmorPart modularNode =
                (PawnRenderNode_ModularArmorPart)node;
            ModularArmorPartDef part = modularNode.Installed.part;
            ModularArmorPositionDef position = modularNode.Installed.EffectivePosition;

            if (position.drawOffsets != null)
            {
                result += position.drawOffsets.For(parms.facing);
            }

            if (part.drawOffsets != null)
            {
                result += part.drawOffsets.For(parms.facing);
            }

            if (modularNode.Installed.IsPalsMounted)
            {
                ModularArmorPalsPanelDef panel = modularNode.Installed.palsPanel;
                if (panel.drawOrigin != null)
                {
                    result += panel.drawOrigin.For(parms.facing);
                }

                float gridX = modularNode.Installed.palsX
                    + part.palsWidth * 0.5f
                    - panel.columns * 0.5f;
                float gridY = panel.rows * 0.5f
                    - modularNode.Installed.palsY
                    - part.palsHeight * 0.5f;
                float sideCompression = parms.facing == Rot4.East
                    || parms.facing == Rot4.West
                    ? 0.35f
                    : 1f;
                result.x += gridX * panel.drawCellSize.x * sideCompression;
                result.z += gridY * panel.drawCellSize.y;
            }

            result += modularNode.NodeGraphicData.DrawOffsetForRot(parms.facing);
            return result;
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_ModularArmorPart modularNode =
                (PawnRenderNode_ModularArmorPart)node;
            if (modularNode.NorthUnderlay)
            {
                return modularNode.Installed.part.northUnderDrawLayer;
            }

            return modularNode.Installed.part.drawLayer
                + modularNode.Installed.EffectivePosition.drawLayer
                + (modularNode.Installed.palsPanel?.drawLayer ?? 0f);
        }
    }

    internal static class ModularArmorDamageContext
    {
        [ThreadStatic]
        private static Stack<DamageInfo> damageStack;

        public static DamageInfo? Current => damageStack != null && damageStack.Count > 0
            ? damageStack.Peek()
            : (DamageInfo?)null;

        public static void Push(DamageInfo damageInfo)
        {
            if (damageStack == null)
            {
                damageStack = new Stack<DamageInfo>();
            }

            damageStack.Push(damageInfo);
        }

        public static void Pop()
        {
            if (damageStack != null && damageStack.Count > 0)
            {
                damageStack.Pop();
            }
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "ApplyToPawn")]
    public static class Patch_DamageWorker_AddInjury_ModularArmorContext
    {
        [HarmonyPrefix]
        public static void Prefix(DamageInfo dinfo)
        {
            ModularArmorDamageContext.Push(dinfo);
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            ModularArmorDamageContext.Pop();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage))]
    public static class Patch_ArmorUtility_ModularArmor
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn pawn,
            float armorPenetration,
            BodyPartRecord part,
            ref DamageDef damageDef,
            ref bool deflectedByMetalArmor,
            ref bool diminishedByMetalArmor,
            ref float __result)
        {
            if (__result <= 0f
                || pawn?.apparel?.WornApparel == null
                || part == null
                || damageDef?.armorCategory == null)
            {
                return;
            }

            DamageInfo? context = ModularArmorDamageContext.Current;
            for (int apparelIndex = pawn.apparel.WornApparel.Count - 1;
                apparelIndex >= 0 && __result > 0f;
                apparelIndex--)
            {
                Apparel apparel = pawn.apparel.WornApparel[apparelIndex];
                CompModularArmor comp = apparel?.TryGetComp<CompModularArmor>();
                if (comp == null)
                {
                    continue;
                }

                IReadOnlyList<InstalledModularArmorPart> installed = comp.InstalledParts;
                for (int partIndex = 0; partIndex < installed.Count && __result > 0f; partIndex++)
                {
                    InstalledModularArmorPart record = installed[partIndex];
                    ModularArmorFacing protectedFacing;
                    if (record?.part == null
                        || record.EffectivePosition == null
                        || !record.EffectivePosition.Covers(part)
                        || !TryGetProtectedFacing(
                            record.EffectivePosition,
                            pawn,
                            context,
                            out protectedFacing))
                    {
                        continue;
                    }

                    float armorRating = record.part.ArmorFor(damageDef.armorCategory);
                    if (armorRating <= 0f)
                    {
                        continue;
                    }

                    float before = __result;
                    bool metalArmor = record.part.metallic;
                    if (record.part.plateThingDef != null)
                    {
                        Thing plate = record.PlateForFacing(protectedFacing);
                        if (plate == null)
                        {
                            continue;
                        }

                        ApplyPlateArmor(
                            ref __result,
                            armorPenetration,
                            armorRating,
                            record,
                            protectedFacing,
                            ref damageDef);
                    }
                    else
                    {
                        ArmorUtilityReversePatch.ApplyArmor(
                            ref __result,
                            armorPenetration,
                            armorRating,
                            apparel,
                            ref damageDef,
                            pawn,
                            ref metalArmor);
                    }
                    metalArmor |= record.part.metallic;

                    if (__result <= 0f)
                    {
                        deflectedByMetalArmor |= metalArmor;
                    }
                    else if (__result < before)
                    {
                        diminishedByMetalArmor |= metalArmor;
                    }
                }
            }
        }

        private static void ApplyPlateArmor(
            ref float damageAmount,
            float armorPenetration,
            float armorRating,
            InstalledModularArmorPart installedPlateSet,
            ModularArmorFacing protectedFacing,
            ref DamageDef damageDef)
        {
            // ArmorUtility.ApplyArmor assumes armorThing is wearable apparel and
            // dereferences armorThing.def.apparel. Plates are ordinary items, so
            // reproduce the vanilla roll here while storing wear on plate 1 or 2
            // inside the physical two-plate set item.
            bool guaranteedBlock = damageDef.armorCategory
                    == DamageArmorCategoryDefOf.Sharp
                && (damageDef == DamageDefOf.Bullet || damageDef.isRanged)
                && installedPlateSet.PlateHealthRatioForFacing(protectedFacing) >= 0.4f
                && installedPlateSet.part.guaranteedBlockPenetration > 0f
                && armorPenetration
                    <= installedPlateSet.part.guaranteedBlockPenetration;

            int plateWear = GenMath.RoundRandom(damageAmount * 0.35f);
            if (plateWear > 0)
            {
                installedPlateSet.DamagePlateForFacing(
                    protectedFacing,
                    plateWear,
                    damageDef);
            }

            if (guaranteedBlock)
            {
                damageAmount = 0f;
                return;
            }

            float effectiveArmor = Mathf.Max(armorRating - armorPenetration, 0f);
            float armorRoll = Rand.Value;
            if (armorRoll < effectiveArmor * 0.5f)
            {
                damageAmount = 0f;
                return;
            }

            if (armorRoll < effectiveArmor)
            {
                damageAmount = GenMath.RoundRandom(damageAmount * 0.5f);
                if (damageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
                {
                    damageDef = DamageDefOf.Blunt;
                }
            }
        }

        private static bool TryGetProtectedFacing(
            ModularArmorPositionDef position,
            Pawn pawn,
            DamageInfo? context,
            out ModularArmorFacing protectedFacing)
        {
            protectedFacing = ModularArmorFacing.Front;
            if (position.protectedDirections.NullOrEmpty())
            {
                return true;
            }

            if (!context.HasValue
                || !TryGetRelativeAttackerAngle(
                    pawn,
                    context.Value,
                    out float relativeAngle))
            {
                return false;
            }

            float halfArc = Mathf.Clamp(position.directionArcDegrees, 0f, 180f) * 0.5f;
            for (int i = 0; i < position.protectedDirections.Count; i++)
            {
                float center = DirectionCenter(position.protectedDirections[i]);
                if (Mathf.Abs(Mathf.DeltaAngle(center, relativeAngle)) <= halfArc)
                {
                    protectedFacing = position.protectedDirections[i];
                    return true;
                }
            }

            return false;
        }

        private static float DirectionCenter(ModularArmorFacing facing)
        {
            if (facing == ModularArmorFacing.Right) return 90f;
            if (facing == ModularArmorFacing.Back) return 180f;
            if (facing == ModularArmorFacing.Left) return -90f;
            return 0f;
        }

        private static bool TryGetRelativeAttackerAngle(
            Pawn pawn,
            DamageInfo dinfo,
            out float relativeAngle)
        {
            Thing source = dinfo.Instigator;
            if (source != null
                && source.PositionHeld.IsValid
                && source.PositionHeld != pawn.PositionHeld)
            {
                IntVec3 toAttacker = source.PositionHeld - pawn.PositionHeld;
                IntVec3 forward = pawn.Rotation.FacingCell;
                float forwardAmount = toAttacker.x * forward.x
                    + toAttacker.z * forward.z;
                float rightAmount = toAttacker.x * forward.z
                    - toAttacker.z * forward.x;
                relativeAngle = Mathf.Atan2(rightAmount, forwardAmount)
                    * Mathf.Rad2Deg;
                return true;
            }

            if (dinfo.Angle >= 0f)
            {
                float attackerDirection = Mathf.Repeat(dinfo.Angle + 180f, 360f);
                relativeAngle = Mathf.DeltaAngle(
                    pawn.Rotation.AsAngle,
                    attackerDirection);
                return true;
            }

            relativeAngle = 0f;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ArmorUtilityReversePatch
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(ArmorUtility), "ApplyArmor")]
        public static void ApplyArmor(
            ref float damAmount,
            float armorPenetration,
            float armorRating,
            Thing armorThing,
            ref DamageDef damageDef,
            Pawn pawn,
            ref bool metalArmor)
        {
            throw new NotImplementedException(
                "Harmony reverse patch for ArmorUtility.ApplyArmor was not applied.");
        }
    }

    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue))]
    public static class Patch_StatExtension_ModularArmorConversions
    {
        [HarmonyPostfix]
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (!(thing is Pawn pawn) || pawn.apparel?.WornApparel == null)
            {
                return;
            }

            for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
            {
                pawn.apparel.WornApparel[i]
                    ?.TryGetComp<CompModularArmor>()
                    ?.ApplyStatConversions(stat, ref __result);
            }
        }
    }
}
