using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace.Economy
{
    public sealed class GameComponent_FinancialGoodwill : GameComponent
    {
        public const float Minimum = -100f;
        public const float Maximum = 100f;
        private Dictionary<Faction, float> values = new Dictionary<Faction, float>();
        private List<Faction> factionKeys;
        private List<float> goodwillValues;

        public GameComponent_FinancialGoodwill(Game game) { }
        public static GameComponent_FinancialGoodwill Current
            => Verse.Current.Game?.GetComponent<GameComponent_FinancialGoodwill>();

        public float Get(Faction faction)
            => faction != null && !faction.IsPlayer && values != null && values.TryGetValue(faction, out float value) ? value : 0f;

        public bool TrySet(Faction faction, float goodwill)
        {
            if (faction == null || faction.IsPlayer || float.IsNaN(goodwill) || float.IsInfinity(goodwill)) return false;
            if (values == null) values = new Dictionary<Faction, float>();
            float value = FinancialGoodwillRules.Add(0f, goodwill);
            if (value == 0f) values.Remove(faction);
            else values[faction] = value;
            return true;
        }

        public float Adjust(Faction faction, float goodwill)
        {
            float previous = Get(faction);
            return TrySet(faction, previous + goodwill) ? Get(faction) - previous : 0f;
        }

        public bool TrySpend(Faction faction, float cost)
        {
            if (cost <= 0f || float.IsNaN(cost) || float.IsInfinity(cost) || Get(faction) < cost) return false;
            return TrySet(faction, Get(faction) - cost);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref values, "financialGoodwill", LookMode.Reference, LookMode.Value,
                ref factionKeys, ref goodwillValues);
        }
    }
}
