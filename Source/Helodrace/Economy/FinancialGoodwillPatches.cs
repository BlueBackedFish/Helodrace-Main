using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Economy
{
    // Ordinary trade/gifts retain vanilla diplomacy. Safe-conduct trade only
    // grants financial credit; it must not repair the underlying hostile relation.
    // Financial credit is a separate half-sized reward, including at the normal
    // diplomatic cap. Do not replace or recalculate gift valuation.
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_FinancialEconomicReward
    {
        [HarmonyPrefix]
        public static bool Prefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, ref float __state, ref bool __result)
        {
            __state = 0f;
            if (GameComponent_FinancialGoodwill.Current == null || other == null
                || (reason != HistoryEventDefOf.GaveGift && reason != HistoryEventDefOf.Traded)
                || goodwillChange <= 0 || __instance.IsPlayer == other.IsPlayer
                || !__instance.CanChangeGoodwillFor(other, goodwillChange)) return true;
            __state = FinancialGoodwillRules.HalfReward(__instance.CalculateAdjustedGoodwillChange(other, goodwillChange));
            Faction recipient = __instance.IsPlayer ? other : __instance;
            if (reason == HistoryEventDefOf.Traded && TradeSession.trader is Pawn trader
                && trader.Faction == recipient && FinancialCaravanUtility.IsProtected(trader))
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(Faction __instance, Faction other, float __state)
        {
            if (__state <= 0f) return;
            Faction recipient = __instance.IsPlayer ? other : __instance;
            var ledger = GameComponent_FinancialGoodwill.Current;
            float gained = ledger?.Adjust(recipient, __state) ?? 0f;
            if (gained > 0f)
                Messages.Message("HD_FinancialGoodwill_Received".Translate(recipient.Name, gained.ToString("0.##"),
                    ledger.Get(recipient).ToString("0.##")), MessageTypeDefOf.PositiveEvent);
        }
    }
}
