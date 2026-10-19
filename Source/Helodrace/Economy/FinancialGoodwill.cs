using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace.Economy
{
    // Separate from Faction goodwill. Economic systems may use this ledger later;
    // no trade, payment, contract, or diplomacy event currently changes it.
    public sealed class GameComponent_FinancialGoodwill : GameComponent
    {
        public const int Minimum = -100;
        public const int Maximum = 100;
        private Dictionary<Faction, int> values = new Dictionary<Faction, int>();
        private List<Faction> factionKeys;
        private List<int> goodwillValues;

        public GameComponent_FinancialGoodwill(Game game) { }

        public static GameComponent_FinancialGoodwill Current
            => Verse.Current.Game?.GetComponent<GameComponent_FinancialGoodwill>();

        public int Get(Faction faction)
        {
            return faction != null && faction != Faction.OfPlayer && values != null && values.TryGetValue(faction, out int value) ? value : 0;
        }

        public bool TrySet(Faction faction, int goodwill)
        {
            if (faction == null || faction == Faction.OfPlayer) return false;
            if (values == null) values = new Dictionary<Faction, int>();
            int value = System.Math.Max(Minimum, System.Math.Min(Maximum, goodwill));
            if (value == 0) values.Remove(faction);
            else values[faction] = value;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref values, "financialGoodwill", LookMode.Reference, LookMode.Value,
                ref factionKeys, ref goodwillValues);
        }
    }
}
