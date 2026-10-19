namespace Helodrace.Economy
{
    public static class FinancialGoodwillRules
    {
        public const int RecoveryIntervalTicks = 60000;

        public static int Add(int current, int gain)
            => (int)System.Math.Max(-100L, System.Math.Min(100L, (long)current + gain));

        public static int RecoveryAmount(int current, int target)
            => current < System.Math.Max(-100, System.Math.Min(100, target)) ? 1 : 0;
    }
}
