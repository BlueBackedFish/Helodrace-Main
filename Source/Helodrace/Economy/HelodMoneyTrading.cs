using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Helodrace.Economy
{
    internal static class HelodMoneyTrading
    {
        internal static ThingDef MoneyDef => DefDatabase<ThingDef>.GetNamedSilentFail("HD_Money");

        internal static bool IsHelodFaction(Faction faction)
        {
            return IsHelodFactionDef(faction?.def);
        }

        internal static bool IsHelodFactionDef(FactionDef faction)
        {
            string name = faction?.defName;
            return name == "HD_HelodCivilLowFaction" || name == "HD_HelodCivilHighFaction";
        }

        internal static bool IsHelodTrade => IsHelodFaction(TradeSession.trader?.Faction);

        // Derived generators such as Tomes consult a live storyteller. Startup
        // has no game yet, so only the plain single-def implementation is safe.
        internal static bool ExistingMoneyStock(StockGenerator existing, ThingDef money) =>
            existing is StockGenerator_HelodMoney || existing?.GetType() == typeof(StockGenerator_SingleDef)
                && existing.HandlesThingDef(money);

        internal static void AddMoneyStockToTraderKinds()
        {
            ThingDef money = MoneyDef;
            if (money == null)
            {
                return;
            }

            foreach (TraderKindDef kind in DefDatabase<TraderKindDef>.AllDefsListForReading)
            {
                if (kind.stockGenerators == null)
                {
                    kind.stockGenerators = new List<StockGenerator>();
                }

                if (kind.stockGenerators.Any(existing => ExistingMoneyStock(existing, money)))
                {
                    continue;
                }

                var generator = new StockGenerator_HelodMoney();
                generator.ResolveReferences(kind);
                kind.stockGenerators.Add(generator);
            }
        }
    }

    internal sealed class StockGenerator_HelodMoney : StockGenerator
    {
        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction)
        {
            if (faction != null ? !HelodMoneyTrading.IsHelodFaction(faction)
                : !HelodMoneyTrading.IsHelodFactionDef(trader?.faction))
            {
                yield break;
            }

            ThingDef money = HelodMoneyTrading.MoneyDef;
            if (money == null)
            {
                yield break;
            }

            Thing notes = ThingMaker.MakeThing(money);
            notes.stackCount = Rand.RangeInclusive(300, 900);
            yield return notes;
        }

        // Availability is restricted by the active trader's faction in the WillTrade patch.
        public override bool HandlesThingDef(ThingDef thingDef) => false;
    }

    [HarmonyPatch(typeof(TraderKindDef), nameof(TraderKindDef.WillTrade))]
    internal static class Patch_TraderKindDef_WillTrade_HelodMoney
    {
        private static bool Prefix(TraderKindDef __instance, ThingDef td, ref bool __result)
        {
            ThingDef money = HelodMoneyTrading.MoneyDef;
            if (!HelodMoneyTrading.IsHelodTrade || TradeSession.trader?.TraderKind != __instance ||
                money == null || td != money)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    internal static class Patch_Tradeable_GetPriceFor_HelodMoney
    {
        private static void Postfix(Tradeable __instance, TradeAction action, ref float __result)
        {
            ThingDef money = HelodMoneyTrading.MoneyDef;
            if (!HelodMoneyTrading.IsHelodTrade || money == null || __instance.ThingDef != money)
            {
                return;
            }

            if (action == TradeAction.PlayerBuys)
            {
                __result = 5.25f;
            }
            else if (action == TradeAction.PlayerSells)
            {
                __result = 4.75f;
            }
        }
    }
}
