using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace.Economy
{
    // Gifts and trading fund this separate relationship. It shifts natural goodwill without
    // granting an immediate diplomatic reward; recovery happens one point/day.
    public sealed class GameComponent_FinancialGoodwill : GameComponent
    {
        public const int Minimum = -100;
        public const int Maximum = 100;
        private Dictionary<Faction, int> values = new Dictionary<Faction, int>();
        private List<Faction> factionKeys;
        private List<int> goodwillValues;
        private Dictionary<Faction, int> nextRecoveryTicks = new Dictionary<Faction, int>();
        private List<Faction> recoveryFactions;
        private List<int> recoveryTicks;

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
            int previous = Get(faction);
            if (value == 0) values.Remove(faction);
            else values[faction] = value;
            if (nextRecoveryTicks == null) nextRecoveryTicks = new Dictionary<Faction, int>();
            if (value <= 0) nextRecoveryTicks.Remove(faction);
            else if (!nextRecoveryTicks.ContainsKey(faction))
                nextRecoveryTicks[faction] = Find.TickManager.TicksGame + FinancialGoodwillRules.RecoveryIntervalTicks;
            if (previous != value) Find.GoodwillSituationManager?.RecalculateAll(false);
            return true;
        }

        public int AddReward(Faction faction, int goodwill)
        {
            if (faction == null || faction == Faction.OfPlayer || goodwill <= 0) return 0;
            int previous = Get(faction);
            TrySet(faction, FinancialGoodwillRules.Add(previous, goodwill));
            return Get(faction) - previous;
        }

        public bool UsesFinancialRecovery(Faction faction)
            => faction != null && !faction.IsPlayer && faction.HasGoodwill && !faction.defeated
                && !faction.temporary && !faction.def.permanentEnemy && Get(faction) > 0;

        public int RecoveryTarget(Faction faction)
            => System.Math.Max(Minimum, System.Math.Min(Maximum, System.Math.Min(faction.NaturalGoodwill,
                Find.GoodwillSituationManager?.GetMaxGoodwill(faction) ?? Maximum)));

        public bool ReplacesVanillaRecovery(Faction faction)
            => UsesFinancialRecovery(faction) && faction.BaseGoodwillWith(Faction.OfPlayer) < RecoveryTarget(faction);

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 0 || values == null) return;
            if (nextRecoveryTicks == null) nextRecoveryTicks = new Dictionary<Faction, int>();
            foreach (Faction faction in new List<Faction>(values.Keys))
            {
                if (!UsesFinancialRecovery(faction)) continue;
                if (!nextRecoveryTicks.TryGetValue(faction, out int next))
                {
                    nextRecoveryTicks[faction] = now + FinancialGoodwillRules.RecoveryIntervalTicks;
                    continue;
                }
                if (now < next) continue;
                int amount = FinancialGoodwillRules.RecoveryAmount(faction.BaseGoodwillWith(Faction.OfPlayer), RecoveryTarget(faction));
                if (amount > 0)
                    faction.TryAffectGoodwillWith(Faction.OfPlayer, amount, reason: HistoryEventDefOf.ReachNaturalGoodwill);
                nextRecoveryTicks[faction] = now + FinancialGoodwillRules.RecoveryIntervalTicks;
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Find.GoodwillSituationManager?.RecalculateAll(false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref values, "financialGoodwill", LookMode.Reference, LookMode.Value,
                ref factionKeys, ref goodwillValues);
            Scribe_Collections.Look(ref nextRecoveryTicks, "financialGoodwillNextRecovery", LookMode.Reference, LookMode.Value,
                ref recoveryFactions, ref recoveryTicks);
        }
    }
}
