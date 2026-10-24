using System.Collections.Generic;
using Helodrace.Economy;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Helodrace
{
    public sealed class MapComponent_TelegraphCompanies : MapComponent
    {
        private readonly CompanyDispatchBook dispatches = new CompanyDispatchBook();
        private Faction caravanProvider;
        private int harringtonNextRequestTick;
        public MapComponent_TelegraphCompanies(Map map) : base(map) { }
        public CompanyDispatchSchedule Schedule(TelegraphCompany company) => dispatches.Schedule(company);
        public int HarringtonWait => Mathf.Max(0, harringtonNextRequestTick - Find.TickManager.TicksGame);

        public override void ExposeData()
        {
            base.ExposeData();
            foreach (TelegraphCompany company in TelegraphCompanies.CaravanCompanies) ExposeSchedule(company.ToString(), dispatches.Schedule(company));
            Scribe_References.Look(ref caravanProvider, "companyCaravanProvider");
            Scribe_Values.Look(ref harringtonNextRequestTick, "harringtonNextRequestTick", 0);
        }

        private static void ExposeSchedule(string key, CompanyDispatchSchedule schedule)
        {
            Scribe_Values.Look(ref schedule.LastRequestTick, key + "LastRequest", -CompanyDispatchSchedule.CooldownTicks);
            Scribe_Values.Look(ref schedule.ArrivalTick, key + "Arrival", -1);
            Scribe_Values.Look(ref schedule.ExpiryTick, key + "Expiry", -1);
            Scribe_Values.Look(ref schedule.SafeConduct, key + "SafeConduct", false);
        }

        private string ConnectionFailure(Pawn actor, CompTelegraphTable comp, bool allowHostileTrade = false)
        {
            if (comp == null || comp.parent.Map != map) return "HD_Telegraph_Status_ProviderUnavailable".Translate();
            string reason = comp.TransmissionFailure(comp.Provider, allowHostileTrade);
            if (reason != null) return reason;
            if (actor == null || !actor.Spawned || actor.Map != map || actor.Dead || actor.Downed
                || actor.Faction != Faction.OfPlayer || !actor.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                || actor.skills == null || actor.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
                return "HD_Telegraph_OperatorUnavailable".Translate();
            return null;
        }

        public string CaravanRequestFailure(CompTelegraphTable comp, IReadOnlyList<TelegraphCompany> requested)
        {
            if (comp?.Provider == null) return "HD_Telegraph_Status_ProviderUnavailable".Translate();
            if (!dispatches.CanRequest(Find.TickManager.TicksGame, requested)) return "HD_Company_RequestUnavailable".Translate();
            Faction provider = comp.Provider;
            bool hostile = provider.HostileTo(Faction.OfPlayer);
            float finance = GameComponent_FinancialGoodwill.Current?.Get(provider) ?? 0f;
            if (!TelegraphTradeRules.CanRequest(finance, hostile, requested.Count))
                return "HD_Company_NeedsFinancial".Translate(TelegraphTradeRules.RequiredFinancial(hostile, requested.Count), finance.ToString("0.##"));
            if (!comp.HasTransmissionCapacity(requested.Count)) return "HD_Company_NeedsCells".Translate(requested.Count);
            return null;
        }

        public bool TryRequestCaravans(Pawn actor, CompTelegraphTable comp, IReadOnlyList<TelegraphCompany> requested, out string reason)
        {
            reason = ConnectionFailure(actor, comp, true) ?? CaravanRequestFailure(comp, requested);
            if (reason != null) return false;
            Faction provider = comp.Provider;
            if (!provider.def.allowedArrivalTemperatureRange.ExpandedBy(-4f).Includes(map.mapTemperature.SeasonalTemp))
            {
                reason = "BadTemperature".Translate(); return false;
            }
            foreach (TelegraphCompany company in requested)
                if (DefDatabase<TraderKindDef>.GetNamedSilentFail(TelegraphCompanies.TraderKind(company)) == null)
                {
                    reason = "HD_Company_RequestUnavailable".Translate(); return false;
                }
            bool hostile = provider.HostileTo(Faction.OfPlayer);
            int cost = TelegraphTradeRules.Cost(hostile, requested.Count);
            var ledger = GameComponent_FinancialGoodwill.Current;
            if (cost > 0 && (ledger == null || !ledger.TrySpend(provider, cost)))
            {
                reason = "HD_Company_NeedsFinancial".Translate(TelegraphTradeRules.RequiredFinancial(true, requested.Count), ledger?.Get(provider) ?? 0f);
                return false;
            }
            int[] travel = new int[requested.Count];
            for (int i = 0; i < travel.Length; i++) travel[i] = Rand.RangeInclusive(CompanyDispatchSchedule.MinimumTravelTicks, CompanyDispatchSchedule.MaximumTravelTicks);
            if (!dispatches.TrySchedule(Find.TickManager.TicksGame, requested, travel, hostile))
            {
                if (cost > 0) ledger.Adjust(provider, cost);
                reason = "HD_Company_RequestUnavailable".Translate(); return false;
            }
            for (int i = 0; i < requested.Count; i++) comp.TryTransmit();
            caravanProvider = provider;
            Messages.Message("HD_Company_CaravansRequested".Translate(requested.Count), actor, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 0) return;
            foreach (TelegraphCompany company in TelegraphCompanies.CaravanCompanies) TryArrival(dispatches.Schedule(company), company, now);
        }

        private void TryArrival(CompanyDispatchSchedule schedule, TelegraphCompany company, int now)
        {
            if (!schedule.Pending || now < schedule.ArrivalTick) return;
            if (caravanProvider == null || caravanProvider.defeated || (caravanProvider.HostileTo(Faction.OfPlayer) && !schedule.SafeConduct))
            {
                schedule.Complete();
                Messages.Message("HD_Company_DispatchCancelled".Translate(TelegraphCompanies.Name(company)), MessageTypeDefOf.NegativeEvent);
                return;
            }
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category, map);
            parms.faction = caravanProvider;
            parms.traderKind = DefDatabase<TraderKindDef>.GetNamed(TelegraphCompanies.TraderKind(company));
            parms.forced = true;
            bool arrived;
            using (FinancialCaravanScope.Enter(schedule.SafeConduct ? caravanProvider : null))
                arrived = IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms);
            if (arrived) schedule.Complete();
            else if (!schedule.Retry(now))
                Messages.Message("HD_Company_DispatchCancelled".Translate(TelegraphCompanies.Name(company)), MessageTypeDefOf.NegativeEvent);
        }

        public bool TryRequestHarrington(Pawn actor, CompTelegraphTable comp, out string reason)
        {
            reason = ConnectionFailure(actor, comp);
            if (reason != null) return false;
            if (HarringtonWait > 0)
            {
                reason = "HD_Company_Cooldown".Translate(HarringtonWait.ToStringTicksToPeriod());
                return false;
            }
            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && !c.Fogged(map)
                && map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Neutral, out IntVec3 entry))
            {
                reason = "HD_Company_NoEntry".Translate();
                return false;
            }
            var squad = new List<Pawn>();
            Faction provider = comp.Provider;
            for (int i = 0; i < TelegraphCompanies.HarringtonGuardCount; i++)
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed(i == 0 ? TelegraphCompanies.EliteCaptain : TelegraphCompanies.EliteGuard);
                squad.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, provider, PawnGenerationContext.NonPlayer,
                    map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false)));
            }
            if (!comp.Network.TryPay(actor, TelegraphCompanies.HarringtonCost, out reason))
            {
                foreach (Pawn pawn in squad) pawn.Destroy();
                return false;
            }
            foreach (Pawn pawn in squad)
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
            LordMaker.MakeNewLord(provider, new LordJob_Harrington(Faction.OfPlayer, actor.Position), map, squad);
            GameComponent_FinancialGoodwill.Current?.RecordServicePayment(provider, TelegraphCompanies.HarringtonCost);
            comp.TryTransmit();
            harringtonNextRequestTick = Find.TickManager.TicksGame + TelegraphCompanies.HarringtonDurationTicks;
            Messages.Message("HD_Company_HarringtonArrived".Translate(squad.Count), actor, MessageTypeDefOf.PositiveEvent);
            return true;
        }
    }

    public sealed class LordJob_Harrington : LordJob_AssistColony
    {
        public LordJob_Harrington() { }
        public LordJob_Harrington(Faction defendedFaction, IntVec3 rally) : base(defendedFaction, rally) { }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = base.CreateGraph();
            // Keep vanilla assistance, combat, danger, and retreat transitions;
            // replace its normal departure timer with the purchased one-day term.
            foreach (Transition transition in graph.transitions)
                for (int i = 0; i < transition.triggers.Count; i++)
                    if (transition.triggers[i] is Trigger_TicksPassed)
                        transition.triggers[i] = new Trigger_TicksPassed(TelegraphCompanies.HarringtonDurationTicks);
            return graph;
        }
    }
}
