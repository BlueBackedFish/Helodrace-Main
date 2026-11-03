using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Economy;

public static class CurrencyWealth
{
    public const float SthalerFactor = .25f;

    [HarmonyPatch(typeof(WealthWatcher), "CalculateWealthItems")]
    private static class MapItemWealth
    {
        [HarmonyPostfix]
        private static void Postfix(Map ___map, ref float __result)
        {
            ThingDef money = HelodMoneyTrading.MoneyDef;
            if (___map == null || money == null) return;
            // Match the native recursive holders and ownership/visibility filter.
            // Currency in a colonist's inventory or carry tracker is counted too.
            var notes = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(___map, ThingRequest.ForDef(money), notes,
                allowUnreal: false, WealthWatcher.WealthItemsFilter);
            float discount = 0;
            foreach (Thing note in notes)
                if (note.SpawnedOrAnyParentSpawned && !note.PositionHeld.Fogged(___map))
                    discount += note.MarketValue * note.stackCount * (1 - SthalerFactor);
            __result = System.MathF.Max(0, __result - discount);
        }
    }
}
