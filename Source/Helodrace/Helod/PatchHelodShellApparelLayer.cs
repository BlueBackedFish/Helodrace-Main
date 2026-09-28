using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Body), nameof(PawnRenderNodeWorker_Apparel_Body.LayerFor))]
    public static class PatchHelodShellApparelLayer
    {
        private const float LayerGap = 0.01f;
        // GraphicMeshSet uses backLift planes: their vertex Y span is five
        // altitude layers (0.0018292684). Head/body Z offsets shift the planes
        // against each other, so ordering their origins by one layer is not
        // enough. Separate the entire surfaces, with one layer of clearance.
        private const float NorthShellLayerGap = 6f;
        private static readonly HashSet<ThingDef> VanillaUtilities = new HashSet<ThingDef>();
        private static ThingDef pantsDef;
        private static ThingDef chestRigDef;
        private static ThingDef molleBeltDef;

        internal static void RebuildCache()
        {
            VanillaUtilities.Clear();
            pantsDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Pants");
            chestRigDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_Apparel_GreatWarStormFrontChestRig");
            molleBeltDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_Apparel_MOLLEBattleBelt");
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (VanillaUtilityDefNames.Contains(def.defName) && def.defName != "Apparel_FirefoampopPack")
                    VanillaUtilities.Add(def);
        }

        private static readonly HashSet<string> VanillaUtilityDefNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Apparel_ShieldBelt",
                "Apparel_SmokepopBelt",
                "Apparel_FirefoampopPack",
                "Apparel_PsychicShockLance",
                "Apparel_PsychicInsanityLance",
                "OrbitalTargeterBombardment",
                "OrbitalTargeterPowerBeam",
                "TornadoGenerator",
                "Apparel_PackJump",
                "Apparel_PackBroadshield",
                "Apparel_PackControl",
                "Apparel_PackBandwidth",
                "Apparel_PackTox",
                "Apparel_ShardPsychicShockLance",
                "Apparel_ShardPsychicInsanityLance",
                "Apparel_BiomutationLance",
                "Apparel_DisruptorFlarePack",
                "Apparel_PackTurret",
                "Apparel_DeadlifePack",
                "Apparel_PackHunter",
                "Apparel_CerebrexNode"
            };

        [HarmonyPostfix]
        public static void Postfix(PawnRenderNode n, PawnDrawParms parms, ref float __result)
        {
            if (!HelodRace.IsHelod(parms.pawn))
            {
                return;
            }

            if (IsMolleBelt(n?.apparel?.def))
            {
                __result = BelowShellLayer(n.tree?.rootNode, parms, __result);
                return;
            }

            if (chestRigDef != null && n?.apparel?.def == chestRigDef)
            {
                // Keep the rig above every worn coat, including north-facing
                // coats whose vanilla depth is overridden below.
                __result = Math.Max(__result,
                    HighestCoatLayer(n.tree?.rootNode, parms) + 0.5f);
                return;
            }

            if (pantsDef != null && n?.apparel?.def == pantsDef)
            {
                // Helod body art already contains underwear. Keep pants at the
                // first visible depth immediately above that baked body layer.
                __result = LayerGap;
                return;
            }

            if ((parms.facing == Rot4.East || parms.facing == Rot4.West)
                && IsUnspecifiedVanillaUtility(n?.apparel?.def))
            {
                // Utility graphics without Helod-specific art render beneath
                // every other apparel layer on either side facing.
                __result = -LayerGap;
                return;
            }

            if (parms.facing == Rot4.South)
            {
                // South-facing head/apparel ordering is defined in XML.
                return;
            }

            if (n?.apparel?.def?.apparel?.LastLayer != ApparelLayerDefOf.Shell)
            {
                return;
            }

            PawnRenderNode hairNode = FindNode<PawnRenderNode_Hair>(
                n.tree?.rootNode);
            if (hairNode == null)
            {
                return;
            }

            float hairLayer = hairNode.Worker.LayerFor(hairNode, parms);
            // North must account for mesh vertices as well as matrix origins.
            float gap = parms.facing == Rot4.North ? NorthShellLayerGap : LayerGap;
            __result = Math.Min(__result, hairLayer - gap);
        }

        private static float HighestCoatLayer(PawnRenderNode node, PawnDrawParms parms)
        {
            float result = 20f;
            if (node == null) return result;
            if (IsCoatLayer(node.apparel?.def)
                && node.Worker is PawnRenderNodeWorker_Apparel_Body)
                result = node.Worker.LayerFor(node, parms);
            if (node.children != null)
                foreach (PawnRenderNode child in node.children)
                    result = Math.Max(result, HighestCoatLayer(child, parms));
            return result;
        }

        private static bool IsCoatLayer(ThingDef def)
        {
            ApparelLayerDef layer = def?.apparel?.LastLayer;
            return layer == ApparelLayerDefOf.Middle || layer == ApparelLayerDefOf.Shell;
        }

        internal static bool IsMolleBelt(ThingDef def)
            => molleBeltDef != null && def == molleBeltDef;

        internal static float BelowShellLayer(PawnRenderNode root, PawnDrawParms parms, float layer)
        {
            if (!HelodRace.IsHelod(parms.pawn)) return layer;
            float lowest = LowestShellLayer(root, parms);
            return float.IsPositiveInfinity(lowest) ? layer : Math.Min(layer, lowest - 0.5f);
        }

        private static float LowestShellLayer(PawnRenderNode node, PawnDrawParms parms)
        {
            float result = float.PositiveInfinity;
            if (node == null) return result;
            if (node.apparel?.def?.apparel?.LastLayer == ApparelLayerDefOf.Shell
                && node.Worker is PawnRenderNodeWorker_Apparel_Body)
                result = node.Worker.LayerFor(node, parms);
            if (node.children != null)
                foreach (PawnRenderNode child in node.children)
                    result = Math.Min(result, LowestShellLayer(child, parms));
            return result;
        }

        internal static bool IsUnspecifiedVanillaUtility(ThingDef def)
        {
            // The firefoam pack keeps its explicitly authored layer. The
            // smokepop belt follows the common utility apparel rules.
            return def != null && VanillaUtilities.Contains(def);
        }

        private static PawnRenderNode FindNode<TNode>(PawnRenderNode node)
            where TNode : PawnRenderNode
        {
            if (node == null)
            {
                return null;
            }

            if (node is TNode)
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
                PawnRenderNode foundNode = FindNode<TNode>(children[i]);
                if (foundNode != null)
                {
                    return foundNode;
                }
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.OffsetFor))]
    public static class PatchHelodUnspecifiedUtilityOffset
    {
        private const float EastWestOffsetX = 0.24f;
        private const float AllDirectionsOffsetZ = 0.16f;

        [HarmonyPostfix]
        public static void Postfix(
            PawnRenderNode node,
            PawnDrawParms parms,
            ref Vector3 __result)
        {
            if (!HelodRace.IsHelod(parms.pawn)
                || !PatchHelodShellApparelLayer.IsUnspecifiedVanillaUtility(
                    node?.apparel?.def))
            {
                return;
            }

            __result.z += AllDirectionsOffsetZ;

            if (parms.facing == Rot4.East)
            {
                __result.x += EastWestOffsetX;
            }
            else if (parms.facing == Rot4.West)
            {
                __result.x -= EastWestOffsetX;
            }
        }
    }
}
