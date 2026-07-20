using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using HarmonyLib;

namespace Helodrace
{
    [HarmonyPatch(typeof(WealthWatcher), "CalculateWealthItems")]
    public static class Patch_WealthWatcher_CalculateWealthItems
    {
        [HarmonyPostfix]
        public static void Postfix(WealthWatcher __instance, ref float __result, Map ___map)
        {
            if (___map != null)
            {
                // Find all Government Bond things on the map
                ThingDef bondDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_GovernmentBond");
                if (bondDef != null)
                {
                    List<Thing> bonds = ___map.listerThings.ThingsOfDef(bondDef);
                    if (bonds != null && bonds.Count > 0)
                    {
                        float bondWealth = 0f;
                        foreach (Thing b in bonds)
                        {
                            if (!b.Position.Fogged(___map))
                            {
                                bondWealth += b.MarketValue * b.stackCount;
                            }
                        }

                        // Subtract the value of the government bonds from the total item wealth
                        __result = UnityEngine.Mathf.Max(0f, __result - bondWealth);
                    }
                }
            }
        }
    }
}