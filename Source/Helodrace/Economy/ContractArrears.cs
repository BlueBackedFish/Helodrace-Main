using System;
using RimWorld;

namespace Helodrace.Economy
{
    public static class ContractArrears
    {
        public const int ExtraGoodwillPenalty = 30;
        public static float FinancialCharge(float current)
            => float.IsNaN(current) || float.IsInfinity(current) ? 0f
                : (float)Math.Round(Math.Abs((double)current) * 0.5d, 2, MidpointRounding.AwayFromZero);

        public static bool Process(Faction provider, CaravanLossKind kind, string contractKey, string label, string location, int unpaidNotes)
        {
            if (provider == null || provider.IsPlayer) return false;
            double conversion = HelodMoneyTrading.MoneyDef?.BaseMarketValue ?? HelodForwardBaseServiceUtility.SthalerSilverValue;
            CaravanLossEntry entry = GameComponent_CaravanLosses.Current?.RecordArrears(provider, kind, contractKey,
                label, location, unpaidNotes, conversion);
            if (entry == null) return false;
            int before = provider.PlayerGoodwill;
            provider.TryAffectGoodwillWith(Faction.OfPlayer, -ExtraGoodwillPenalty);
            entry.GoodwillPenalty = Math.Max(0, before - provider.PlayerGoodwill);
            var financial = GameComponent_FinancialGoodwill.Current;
            float change = financial?.Adjust(provider, -FinancialCharge(financial.Get(provider))) ?? 0f;
            entry.FinancialPenalty = change < 0f ? -change : 0f;
            return true;
        }
    }
}
