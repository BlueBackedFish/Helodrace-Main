using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    public sealed class MapComponent_TelegraphNetwork : MapComponent
    {
        private int readyTick = -1;
        private Faction provider;
        private bool activationNotified;

        public MapComponent_TelegraphNetwork(Map map) : base(map) { }

        public bool InstallationRequested => readyTick >= 0;
        public Faction Provider => TelegraphNetworkRules.IsProvider(provider?.def?.defName) ? provider : null;
        public int TicksUntilReady => System.Math.Max(0, readyTick - Find.TickManager.TicksGame);
        public int LocalHour => GenDate.HourOfDay(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile).x);

        public TelegraphConnectionStatus Status(bool hasPrimaryCell = true)
        {
            bool available = Provider != null && !Provider.defeated;
            return TelegraphNetworkRules.Status(readyTick, Find.TickManager.TicksGame, LocalHour, available, hasPrimaryCell);
        }

        public string StatusText(TelegraphConnectionStatus status)
        {
            if (status == TelegraphConnectionStatus.Installing)
                return "HD_Telegraph_Installing".Translate(TicksUntilReady.ToStringTicksToPeriod());
            if (status == TelegraphConnectionStatus.RelayClosed)
                return "HD_Telegraph_RelayClosed".Translate(TelegraphNetworkRules.OpeningHour, TelegraphNetworkRules.ClosingHour);
            return ("HD_Telegraph_Status_" + status).Translate();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref readyTick, "telegraphReadyTick", -1);
            Scribe_References.Look(ref provider, "telegraphProvider");
            Scribe_Values.Look(ref activationNotified, "telegraphActivationNotified", false);
        }

        public override void MapComponentTick()
        {
            if (!InstallationRequested || activationNotified || Find.TickManager.TicksGame < readyTick) return;
            activationNotified = true;
            Find.LetterStack.ReceiveLetter("HD_Telegraph_ActivatedTitle".Translate(),
                "HD_Telegraph_Activated".Translate(map.Parent.LabelCap, TelegraphNetworkRules.OpeningHour, TelegraphNetworkRules.ClosingHour),
                LetterDefOf.PositiveEvent, new GlobalTargetInfo(map.Center, map));
        }

        public static bool IsInstallationTrader(Pawn trader, Map map)
        {
            return trader != null && trader.Spawned && trader.Map == map && !trader.Dead && !trader.Downed
                && TelegraphNetworkRules.IsProvider(trader.Faction?.def?.defName)
                && !trader.Faction.defeated && !trader.HostileTo(Faction.OfPlayer)
                && trader.trader?.traderKind != null;
        }

        public static Pawn FindInstallationTrader(Pawn caravanMember, Map map)
        {
            if (caravanMember == null || !caravanMember.Spawned || caravanMember.Map != map
                || !TelegraphNetworkRules.IsProvider(caravanMember.Faction?.def?.defName)) return null;
            if (IsInstallationTrader(caravanMember, map)) return caravanMember;
            return caravanMember.GetLord()?.ownedPawns.FirstOrDefault(p => p.Faction == caravanMember.Faction
                && IsInstallationTrader(p, map));
        }

        public string InstallationRejection(Pawn trader)
        {
            if (!IsInstallationTrader(trader, map)) return "HD_Telegraph_TraderUnavailable".Translate();
            if (InstallationRequested) return "HD_Telegraph_AlreadyRequested".Translate();
            if (!map.IsPlayerHome || !map.listerThings.AllThings.Any(t => t.Faction == Faction.OfPlayer
                && t.TryGetComp<CompTelegraphTable>() is CompTelegraphTable terminal && !terminal.IsWireless))
                return "HD_Telegraph_NeedsTerminal".Translate();
            return null;
        }

        public bool TryRequestInstallation(Pawn trader, Pawn negotiator, out string reason)
        {
            reason = InstallationRejection(trader);
            if (reason != null) return false;
            if (negotiator == null || !negotiator.Spawned || negotiator.Dead || negotiator.Downed
                || negotiator.Map != map || negotiator.Faction != Faction.OfPlayer
                || !negotiator.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                || negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled
                || !negotiator.CanReach(trader, PathEndMode.Touch, Danger.Deadly))
            {
                reason = "HD_Telegraph_OperatorUnavailable".Translate();
                return false;
            }

            if (!TryPay(negotiator, TelegraphNetworkRules.InstallationCost, out reason)) return false;
            provider = trader.Faction;
            Economy.GameComponent_FinancialGoodwill.Current?.RecordServicePayment(provider, TelegraphNetworkRules.InstallationCost);
            readyTick = Find.TickManager.TicksGame + Rand.RangeInclusive(
                TelegraphNetworkRules.MinimumInstallationTicks, TelegraphNetworkRules.MaximumInstallationTicks);
            activationNotified = false;
            Messages.Message("HD_Telegraph_InstallationOrdered".Translate(TelegraphNetworkRules.InstallationCost),
                negotiator, MessageTypeDefOf.PositiveEvent);
            return true;
        }
        public bool TryPay(Pawn negotiator, int amount, out string reason)
        {
            reason = null;
            if (amount <= 0) return false;
            if (negotiator == null || !negotiator.Spawned || negotiator.Map != map || negotiator.Faction != Faction.OfPlayer)
            {
                reason = "HD_Telegraph_OperatorUnavailable".Translate();
                return false;
            }
            ThingDef money = DefDatabase<ThingDef>.GetNamed("HD_Money");
            List<Thing> notes = map.listerThings.ThingsOfDef(money)
                .Where(t => !t.IsForbidden(negotiator) && !t.Position.Fogged(map)
                    && negotiator.CanReach(t, PathEndMode.Touch, Danger.Deadly)).ToList();
            if (notes.Sum(t => t.stackCount) < amount)
            {
                reason = "HD_Telegraph_NotEnoughMoney".Translate(amount);
                return false;
            }

            int remaining = amount;
            foreach (Thing note in notes)
            {
                int count = System.Math.Min(remaining, note.stackCount);
                note.SplitOff(count).Destroy();
                remaining -= count;
                if (remaining == 0) break;
            }
            return true;
        }
    }
}
