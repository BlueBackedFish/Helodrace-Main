using System;
using RimWorld;

namespace Helodrace.Economy
{
    // Only the current synchronous operation sees this faction exception.
    // Live diplomacy stays unchanged, including while a menu is left open.
    public sealed class FinancialCaravanScope : IDisposable
    {
        [ThreadStatic] private static Faction current;
        private readonly Faction previous;
        private FinancialCaravanScope(Faction faction) { previous = current; current = faction; }
        public static FinancialCaravanScope Enter(Faction faction)
            => new FinancialCaravanScope(FinancialFactionPolicy.Includes(faction?.def?.defName) ? faction : null);
        public static bool IsGenerating(Faction faction) => current != null && current == faction;
        public static bool Covers(Faction a, Faction b)
            => current != null && ((a == current && b?.IsPlayer == true) || (b == current && a?.IsPlayer == true));
        public void Dispose() { current = previous; }
    }
}
