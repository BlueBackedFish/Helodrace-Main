namespace Helodrace.Economy
{
    public static class FinancialGoodwillRules
    {
        public static float Add(float current, float change)
            => (float)System.Math.Round(System.Math.Max(-100d, System.Math.Min(100d, (double)current + change)),
                2, System.MidpointRounding.AwayFromZero);
        public static float HalfReward(int goodwill) => System.Math.Max(0, goodwill) * 0.5f;
        public static float ServiceReward(int paid) => System.Math.Max(0, paid) / 20f;
        public static int CaravanAttackPenalty(float damage)
            => damage > 0f ? -System.Math.Max(1, (int)(System.Math.Min(100f, damage) * 1.3f)) : 0;
    }
}
