using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Economy
{
    internal static class HelodCashTrade
    {
        private const string MoneyDefName = "HD_Money";

        internal static bool Active
        {
            get
            {
                if (TradeSession.giftMode)
                {
                    return false;
                }

                FactionDef playerFaction = TradeSession.playerNegotiator?.Faction?.def;
                FactionDef traderFaction = TradeSession.trader?.Faction?.def;
                return playerFaction?.defName == "HD_HelodPlayerColony"
                    && (traderFaction?.defName == "HD_HelodCivilLowFaction"
                        || traderFaction?.defName == "HD_HelodCivilHighFaction");
            }
        }

        internal static ThingDef MoneyDef => DefDatabase<ThingDef>.GetNamedSilentFail(MoneyDefName);

        internal static float SilverPerSthaler => Mathf.Max(0.01f,
            HelodMarketState.Current?.SthalerSilverValue ?? 5f);
    }

    [HarmonyPatch(typeof(TraderKindDef), nameof(TraderKindDef.WillTrade))]
    internal static class Patch_TraderKindDef_WillTrade_HelodCash
    {
        private static bool Prefix(TraderKindDef __instance, ThingDef td, ref bool __result)
        {
            if (!HelodCashTrade.Active || TradeSession.trader?.TraderKind != __instance
                || td != HelodCashTrade.MoneyDef)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Tradeable), "get_IsCurrency")]
    internal static class Patch_Tradeable_IsCurrency_HelodCash
    {
        private static bool Prefix(Tradeable __instance, ref bool __result)
        {
            if (!HelodCashTrade.Active)
            {
                return true;
            }

            __result = __instance.ThingDef == HelodCashTrade.MoneyDef;
            return false;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), "get_CurrencyTradeable")]
    internal static class Patch_TradeDeal_CurrencyTradeable_HelodCash
    {
        private static bool Prefix(TradeDeal __instance, ref Tradeable __result)
        {
            if (!HelodCashTrade.Active)
            {
                return true;
            }

            ThingDef money = HelodCashTrade.MoneyDef;
            __result = money == null ? null : __instance.AllTradeables.FirstOrDefault(t => t.ThingDef == money);
            return false;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), "AddAllTradeables")]
    internal static class Patch_TradeDeal_AddAllTradeables_HelodCash
    {
        private static void Postfix(TradeDeal __instance)
        {
            if (!HelodCashTrade.Active || __instance.CurrencyTradeable != null)
            {
                return;
            }

            ThingDef money = HelodCashTrade.MoneyDef;
            if (money == null)
            {
                return;
            }

            Thing placeholder = ThingMaker.MakeThing(money);
            placeholder.stackCount = 0;
            Tradeable currency = new Tradeable();
            currency.AddThing(placeholder, Transactor.Trader);
            __instance.AllTradeables.Add(currency);
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    internal static class Patch_Tradeable_GetPriceFor_HelodCash
    {
        private static void Postfix(ref float __result)
        {
            if (HelodCashTrade.Active)
            {
                __result /= HelodCashTrade.SilverPerSthaler;
            }
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    internal static class Patch_TradeDeal_TryExecute_HelodCash
    {
        private static bool Prefix(TradeDeal __instance, ref bool actuallyTraded, ref bool __result)
        {
            if (!HelodCashTrade.Active)
            {
                return true;
            }

            bool buys = __instance.AllTradeables.Any(t => !t.IsCurrency && t.ActionToDo == TradeAction.PlayerBuys);
            bool sells = __instance.AllTradeables.Any(t => !t.IsCurrency && t.ActionToDo == TradeAction.PlayerSells);
            if (buys && sells)
            {
                Messages.Message("HD_HelodCashTrade_NoBarter".Translate(), MessageTypeDefOf.RejectInput, false);
                actuallyTraded = false;
                __result = false;
                return false;
            }

            float cashTotal = __instance.AllTradeables
                .Where(t => !t.IsCurrency)
                .Sum(t => t.CurTotalCurrencyCostForSource);
            if (!buys && !sells || Mathf.Abs(cashTotal) >= 1f)
            {
                return true;
            }

            Messages.Message("HD_HelodCashTrade_Minimum".Translate(), MessageTypeDefOf.RejectInput, false);
            actuallyTraded = false;
            __result = false;
            return false;
        }
    }
}
