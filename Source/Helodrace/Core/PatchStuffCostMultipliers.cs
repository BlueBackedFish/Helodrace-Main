using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(CostListCalculator), nameof(CostListCalculator.CostListAdjusted), new[] { typeof(BuildableDef), typeof(ThingDef), typeof(bool) })]
    public static class Patch_StuffCostMultipliers
    {
        public static void Postfix(BuildableDef entDef, ThingDef stuff, ref List<ThingDefCountClass> __result)
        {
            if (stuff?.defName != "HD_UniformRolledSteelPlate" || __result == null)
            {
                return;
            }

            if (!(entDef is ThingDef thingDef) || thingDef.CostStuffCount <= 0)
            {
                return;
            }

            foreach (ThingDefCountClass cost in __result)
            {
                if (cost.thingDef == stuff)
                {
                    int explicitCost = Mathf.Max(0, cost.count - thingDef.CostStuffCount);
                    int adjustedStuffCost = Mathf.Max(1, Mathf.CeilToInt(thingDef.CostStuffCount * 0.1f));
                    cost.count = explicitCost + adjustedStuffCost;
                }
            }
        }
    }
}
