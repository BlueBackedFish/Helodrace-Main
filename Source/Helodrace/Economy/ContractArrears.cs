using System;
using RimWorld;

namespace Helodrace.Economy
{
    public static class ContractArrears
    {
        public const int LoanGoodwillPenalty = 30;
        public const int ForwardBaseGoodwillPenalty = 15;
        public const float ForwardBaseFinancialPenalty = 30f;
        public static float LoanFinancialCharge(float creditUsed)
            => float.IsNaN(creditUsed) || float.IsInfinity(creditUsed) ? 0f
                : (float)Math.Round(Math.Max(0d, creditUsed) * 0.5d, 2, MidpointRounding.AwayFromZero);

        public static bool Process(Faction provider, CaravanLossKind kind, string contractKey, string label, string location,
            int unpaidNotes, float loanCreditUsed)
        {
            if (!GameComponent_FinancialGoodwill.Supports(provider)) return false;
            bool loan = kind == CaravanLossKind.LoanOverdue;
            if (loan && (loanCreditUsed <= 0f || float.IsNaN(loanCreditUsed) || float.IsInfinity(loanCreditUsed))) return false;
            double conversion = HelodMoneyTrading.MoneyDef?.BaseMarketValue ?? HelodForwardBaseServiceUtility.SthalerSilverValue;
            CaravanLossEntry entry = GameComponent_CaravanLosses.Current?.RecordArrears(provider, kind, contractKey,
                label, location, unpaidNotes, conversion);
            if (entry == null) return false;
            int before = provider.PlayerGoodwill;
            provider.TryAffectGoodwillWith(Faction.OfPlayer, -(loan ? LoanGoodwillPenalty : ForwardBaseGoodwillPenalty));
            entry.GoodwillPenalty = Math.Max(0, before - provider.PlayerGoodwill);
            var financial = GameComponent_FinancialGoodwill.Current;
            float charge = loan ? LoanFinancialCharge(loanCreditUsed) : ForwardBaseFinancialPenalty;
            float change = financial?.Adjust(provider, -charge) ?? 0f;
            entry.FinancialPenalty = change < 0f ? -change : 0f;
            return true;
        }
    }
}
