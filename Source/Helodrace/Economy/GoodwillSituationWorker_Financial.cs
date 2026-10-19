using RimWorld;

namespace Helodrace.Economy
{
    public sealed class GoodwillSituationWorker_Financial : GoodwillSituationWorker
    {
        public override int GetNaturalGoodwillOffset(Faction other)
            => GameComponent_FinancialGoodwill.Current?.Get(other) ?? 0;
    }
}
