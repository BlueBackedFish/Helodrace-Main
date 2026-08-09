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
        private const string HelodRaceDefName = "Helod";
        private const float LayerGap = 0.01f;

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
            if (parms.pawn?.def?.defName != HelodRaceDefName)
            {
                return;
            }

            if (n?.apparel?.def?.defName == "Apparel_Pants")
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
            __result = Math.Min(__result, hairLayer - LayerGap);
        }

        internal static bool IsUnspecifiedVanillaUtility(ThingDef def)
        {
            if (!VanillaUtilityDefNames.Contains(def?.defName))
            {
                return false;
            }

            // The firefoam pack keeps its explicitly authored layer. The
            // smokepop belt uses its Helod art but is intentionally lowered.
            return def.defName != "Apparel_FirefoampopPack";
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
        private const string HelodRaceDefName = "Helod";
        private const float EastWestOffsetX = 0.24f;
        private const float AllDirectionsOffsetZ = 0.16f;

        [HarmonyPostfix]
        public static void Postfix(
            PawnRenderNode node,
            PawnDrawParms parms,
            ref Vector3 __result)
        {
            if (parms.pawn?.def?.defName != HelodRaceDefName
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
