using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Body), nameof(PawnRenderNodeWorker_Apparel_Body.LayerFor))]
    public static class PatchHelodShellApparelLayer
    {
        private const string HelodRaceDefName = "Helod";
        private const float LayerGap = 0.01f;

        [HarmonyPostfix]
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref float __result)
        {
            if (parms.pawn?.def?.defName != HelodRaceDefName ||
                node?.apparel?.def?.apparel?.LastLayer != ApparelLayerDefOf.Shell)
            {
                return;
            }

            PawnRenderNode hairNode = FindHairNode(node.tree?.rootNode);
            if (hairNode == null)
            {
                return;
            }

            float hairLayer = hairNode.Worker.LayerFor(hairNode, parms);
            __result = Math.Min(__result, hairLayer - LayerGap);
        }

        private static PawnRenderNode FindHairNode(PawnRenderNode node)
        {
            if (node == null)
            {
                return null;
            }

            if (node is PawnRenderNode_Hair)
            {
                return node;
            }

            PawnRenderNode[] children = node.children;
            if (children == null)
            {
                return null;
            }

            for (int i = 0; i < children.Length; i++)
            {
                PawnRenderNode hairNode = FindHairNode(children[i]);
                if (hairNode != null)
                {
                    return hairNode;
                }
            }

            return null;
        }
    }
}
