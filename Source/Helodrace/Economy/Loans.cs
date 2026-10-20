using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace.Economy
{
    public sealed class LoanContract : IExposable
    {
        public LoanAccount Account = new LoanAccount();
        public Faction Lender;
        public Map Destination;
        public Pawn Courier;
        public bool Dispatched;
        public int DispatchTick;
        public int ExpiryTick;
        public bool OverdueNotified;

        public void ExposeData()
        {
            Scribe_References.Look(ref Lender, "lender");
            Scribe_References.Look(ref Destination, "destination");
            Scribe_References.Look(ref Courier, "courier");
            Scribe_Values.Look(ref Dispatched, "dispatched");
            Scribe_Values.Look(ref DispatchTick, "dispatchTick");
            Scribe_Values.Look(ref ExpiryTick, "expiryTick");
            Scribe_Values.Look(ref OverdueNotified, "overdueNotified");
            Scribe_Values.Look(ref Account.ApprovedAmount, "approvedAmount");
            Scribe_Values.Look(ref Account.DurationDays, "durationDays");
            Scribe_Values.Look(ref Account.Disbursed, "disbursed");
            Scribe_Values.Look(ref Account.Repaid, "repaid");
            Scribe_Values.Look(ref Account.FundedTick, "fundedTick", -1);
            Scribe_Values.Look(ref Account.DeliveryFinished, "deliveryFinished");
        }
    }

    public sealed class GameComponent_Loans : GameComponent
    {
        private static readonly System.Type SettledCaravanToil = typeof(LordJob_TradeWithColony).Assembly.GetType("RimWorld.LordToil_DefendTraderCaravan", true);
        private static readonly System.Reflection.FieldInfo MeetingSpot = HarmonyLib.AccessTools.Field(typeof(LordJob_TradeWithColony), "chillSpot");
        private List<LoanContract> contracts = new List<LoanContract>();
        public GameComponent_Loans(Game game) { }
        public static GameComponent_Loans Current => Verse.Current.Game?.GetComponent<GameComponent_Loans>();
        public LoanContract OpenContract(Faction lender) => contracts.FirstOrDefault(c => c.Lender == lender && !c.Account.Closed);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref contracts, "loans", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && contracts == null) contracts = new List<LoanContract>();
        }

        private static string ConnectionFailure(Pawn actor, CompTelegraphTable terminal)
        {
            string failure = terminal?.TransmissionFailure(terminal.Network?.Provider, true);
            if (terminal == null) return "HD_Telegraph_Status_ProviderUnavailable".Translate();
            if (failure != null) return failure;
            if (actor == null || !actor.Spawned || actor.Map != terminal.parent.Map || actor.Dead || actor.Downed
                || actor.Faction != Faction.OfPlayer || !actor.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                || actor.skills == null || actor.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
                return "HD_Telegraph_OperatorUnavailable".Translate();
            return null;
        }

        public string BorrowFailure(Faction lender, int amount, int days)
        {
            if (lender == null || lender.defeated || !TelegraphNetworkRules.IsProvider(lender.def.defName))
                return "HD_Telegraph_Status_ProviderUnavailable".Translate();
            if (contracts.Any(c => c.Account.Overdue(Find.TickManager.TicksGame))) return "HD_Loan_OverdueBlock".Translate();
            if (OpenContract(lender) != null) return "HD_Loan_AlreadyOpen".Translate();
            float financial = GameComponent_FinancialGoodwill.Current?.Get(lender) ?? 0f;
            if (lender.PlayerGoodwill + financial < LoanRules.RequiredCombinedGoodwill)
                return "HD_Loan_NeedsCombined".Translate(LoanRules.RequiredCombinedGoodwill);
            if (!LoanRules.CanBorrow(lender.PlayerGoodwill, financial, amount, days))
                return "HD_Loan_InvalidTerms".Translate(LoanRules.MaximumDays, financial.ToString("0.##"));
            return null;
        }

        public bool TryBorrow(Pawn actor, CompTelegraphTable terminal, int amount, int days, out string reason)
        {
            reason = ConnectionFailure(actor, terminal) ?? BorrowFailure(terminal.Network.Provider, amount, days);
            if (reason != null) return false;
            var ledger = GameComponent_FinancialGoodwill.Current;
            Faction lender = terminal.Network.Provider;
            float cost = LoanRules.CreditFor(amount);
            if (ledger == null || !ledger.TrySpend(lender, cost))
            {
                reason = "HD_Loan_InvalidTerms".Translate(LoanRules.MaximumDays, ledger?.Get(lender) ?? 0f);
                return false;
            }
            if (!terminal.ConsumePrimaryCell())
            {
                ledger.Adjust(lender, cost);
                reason = "HD_Company_NeedsCells".Translate(1);
                return false;
            }
            int now = Find.TickManager.TicksGame;
            contracts.Add(new LoanContract
            {
                Lender = lender, Destination = terminal.parent.Map,
                DispatchTick = now + Rand.RangeInclusive(60000, 120000), ExpiryTick = now + 240000,
                Account = new LoanAccount { ApprovedAmount = amount, DurationDays = days }
            });
            Messages.Message("HD_Loan_Signed".Translate(amount, days, cost.ToString("0.##")), actor, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public bool TryRepay(Pawn actor, CompTelegraphTable terminal, LoanContract contract, int amount, out string reason)
        {
            reason = ConnectionFailure(actor, terminal);
            if (reason != null) return false;
            if (contract == null || !contracts.Contains(contract) || contract.Lender != terminal.Network.Provider
                || !contract.Account.DeliveryFinished || amount <= 0 || amount > contract.Account.Outstanding)
            {
                reason = "HD_Loan_InvalidRepayment".Translate();
                return false;
            }
            if (!terminal.Network.TryPay(actor, amount, out reason)) return false;
            contract.Account.TryRepay(amount, out float restore);
            GameComponent_FinancialGoodwill.Current.Adjust(contract.Lender, restore);
            Messages.Message("HD_Loan_Repaid".Translate(amount, restore.ToString("0.##"), contract.Account.Outstanding),
                actor, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 0) return;
            foreach (LoanContract contract in contracts)
            {
                if (!contract.Account.DeliveryFinished) TickDelivery(contract, now);
                if (!contract.OverdueNotified && contract.Account.Overdue(now))
                {
                    contract.OverdueNotified = true;
                    Find.LetterStack.ReceiveLetter("HD_Loan_OverdueTitle".Translate(),
                        "HD_Loan_Overdue".Translate(contract.Lender?.Name ?? "?", contract.Account.Outstanding), LetterDefOf.NegativeEvent);
                }
            }
        }

        private void TickDelivery(LoanContract contract, int now)
        {
            Map map = contract.Destination;
            if (map == null || !Find.Maps.Contains(map) || contract.Lender == null || contract.Lender.defeated || now >= contract.ExpiryTick)
            {
                FinishDelivery(contract, now);
                return;
            }
            if (!contract.Dispatched)
            {
                if (now < contract.DispatchTick) return;
                contract.DispatchTick = now + 2500;
                var existing = new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category, map);
                parms.faction = contract.Lender;
                parms.traderKind = DefDatabase<TraderKindDef>.GetNamed(TelegraphCompanies.FinancialTrader);
                parms.forced = true;
                bool arrived;
                using (FinancialCaravanScope.Enter(contract.Lender))
                    arrived = IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms);
                if (!arrived) return;
                contract.Dispatched = true;
                contract.Courier = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !existing.Contains(p)
                    && p.Faction == contract.Lender && p.trader?.traderKind == parms.traderKind);
                if (contract.Courier == null) FinishDelivery(contract, now);
                return;
            }
            Pawn courier = contract.Courier;
            Lord lord = courier?.Spawned == true ? courier.GetLord() : null;
            if (courier == null || courier.Dead || !courier.Spawned || courier.Map != map
                || !(lord?.LordJob is LordJob_FinancialCaravan job) || !job.Protected
                || lord.CurLordToil is LordToil_ExitMap || lord.CurLordToil is LordToil_ExitMapAndEscortCarriers)
            {
                FinishDelivery(contract, now);
                return;
            }
            // The native caravan travels to its meeting spot before defending/trading there.
            if (courier.Downed || courier.InMentalState || !SettledCaravanToil.IsInstanceOfType(lord.CurLordToil)
                || courier.Position.DistanceTo((IntVec3)MeetingSpot.GetValue(job)) > 8f) return;
            ThingDef moneyDef = HelodMoneyTrading.MoneyDef;
            while (contract.Account.Undelivered > 0)
            {
                Thing notes = ThingMaker.MakeThing(moneyDef);
                notes.stackCount = System.Math.Min(moneyDef.stackLimit, contract.Account.Undelivered);
                if (!GenPlace.TryPlaceThing(notes, courier.Position, map, ThingPlaceMode.Near,
                    (placed, count) => contract.Account.TryDisburse(count)))
                {
                    if (!notes.Destroyed) notes.Destroy();
                    return;
                }
            }
            FinishDelivery(contract, now);
        }

        private static void FinishDelivery(LoanContract contract, int now)
        {
            float refund = contract.Account.FinishDelivery(now);
            if (refund > 0f) GameComponent_FinancialGoodwill.Current?.Adjust(contract.Lender, refund);
            Messages.Message((contract.Account.Disbursed > 0 ? "HD_Loan_Delivered" : "HD_Loan_DeliveryCancelled")
                .Translate(contract.Account.Disbursed, contract.Account.DurationDays, refund.ToString("0.##")),
                MessageTypeDefOf.NeutralEvent);
        }
    }
}
