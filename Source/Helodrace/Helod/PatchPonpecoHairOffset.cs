using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.OffsetFor))]
    public static class PatchPonpecoHairOffset
    {
        private const string HelodRaceDefName = "Helod";
        private const string PonpecoFemaleDefPrefix = "PPHF";
        private const float NorthSouthOffset = 0.01f;

        [HarmonyPostfix]
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            string hairDefName = parms.pawn?.story?.hairDef?.defName;

            if (node is PawnRenderNode_Hair &&
                parms.pawn?.def?.defName == HelodRaceDefName &&
                hairDefName != null &&
                hairDefName.StartsWith(PonpecoFemaleDefPrefix, StringComparison.Ordinal) &&
                (parms.facing == Rot4.North || parms.facing == Rot4.South))
            {
                __result.z += NorthSouthOffset;
            }
        }
    }
}
