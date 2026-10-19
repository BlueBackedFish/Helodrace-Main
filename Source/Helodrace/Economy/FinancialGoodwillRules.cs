namespace Helodrace.Economy
{
    public static class FinancialGoodwillRules
    {
        public static float Add(float current, float change)
            => System.Math.Max(-100f, System.Math.Min(100f, current + change));
        public static float HalfReward(int goodwill) => System.Math.Max(0, goodwill) * 0.5f;
        public static int CaravanAttackPenalty(float damage)
            => damage > 0f ? -System.Math.Max(1, (int)(System.Math.Min(100f, damage) * 1.3f)) : 0;
    }
}
