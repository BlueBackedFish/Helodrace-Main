namespace Helodrace.Economy
{
    public static class LoanRules
    {
        public const int RequiredCombinedGoodwill = 70;
        public const int MaximumAmount = 2000;
        public const int MinimumDays = 1;
        public const int MaximumDays = 60;
        public static float CreditFor(int amount) => amount / 20f;
        public static bool CanBorrow(int diplomatic, float financial, int amount, int days)
            => !float.IsNaN(financial) && !float.IsInfinity(financial)
                && diplomatic + financial >= RequiredCombinedGoodwill
                && amount > 0 && amount <= MaximumAmount && financial >= CreditFor(amount)
                && days >= MinimumDays && days <= MaximumDays;
    }

    // State shared by live contracts and standalone transaction tests. Serialization
    // belongs to LoanContract so the accounting does not depend on game objects.
    public sealed class LoanAccount
    {
        public int ApprovedAmount;
        public int DurationDays;
        public int Disbursed;
        public int Repaid;
        public int FundedTick = -1;
        public bool DeliveryFinished;
        public int Outstanding => Disbursed - Repaid;
        public bool Closed => DeliveryFinished && Outstanding == 0;
        public int Undelivered => ApprovedAmount - Disbursed;
        public int DueTick => FundedTick < 0 ? -1 : FundedTick + DurationDays * 60000;
        public bool Overdue(int now) => DeliveryFinished && Outstanding > 0 && now >= DueTick;

        public bool TryDisburse(int amount)
        {
            if (DeliveryFinished || amount <= 0 || amount > Undelivered) return false;
            Disbursed += amount;
            return true;
        }

        public float FinishDelivery(int now)
        {
            if (DeliveryFinished) return 0f;
            DeliveryFinished = true;
            if (Disbursed > 0) FundedTick = now;
            return LoanRules.CreditFor(Undelivered);
        }

        public bool TryRepay(int amount, out float restoredCredit)
        {
            restoredCredit = 0f;
            if (!DeliveryFinished || amount <= 0 || amount > Outstanding) return false;
            Repaid += amount;
            restoredCredit = LoanRules.CreditFor(amount);
            return true;
        }
    }
}
