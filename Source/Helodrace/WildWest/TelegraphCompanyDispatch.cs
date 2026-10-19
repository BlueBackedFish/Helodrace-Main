using System.Collections.Generic;
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
        public CompanyDispatchSchedule Schedule(bool financial) => financial ? dispatches.Financial : dispatches.Commercial;
        public int HarringtonWait => Mathf.Max(0, harringtonNextRequestTick - Find.TickManager.TicksGame);

        public override void ExposeData()
        {
            base.ExposeData();
            ExposeSchedule("nationalIndustrialCredit", dispatches.Financial);
            ExposeSchedule("centralHardwareSupply", dispatches.Commercial);
            Scribe_References.Look(ref caravanProvider, "companyCaravanProvider");
            Scribe_Values.Look(ref harringtonNextRequestTick, "harringtonNextRequestTick", 0);
        }

        private static void ExposeSchedule(string key, CompanyDispatchSchedule schedule)
        {
            Scribe_Values.Look(ref schedule.LastRequestTick, key + "LastRequest", -CompanyDispatchSchedule.CooldownTicks);
            Scribe_Values.Look(ref schedule.ArrivalTick, key + "Arrival", -1);
            Scribe_Values.Look(ref schedule.ExpiryTick, key + "Expiry", -1);
        }

        private string ConnectionFailure(Pawn actor, CompTelegraphTable comp)
        {
            if (comp == null || comp.parent.Map != map) return "HD_Telegraph_Status_ProviderUnavailable".Translate();
            string reason = comp.TransmissionFailure(comp.Network?.Provider);
            if (reason != null) return reason;
            if (actor == null || !actor.Spawned || actor.Map != map || actor.Dead || actor.Downed
                || actor.Faction != Faction.OfPlayer || !actor.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                || actor.skills == null || actor.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
                return "HD_Telegraph_OperatorUnavailable".Translate();
            return null;
        }

        public bool TryRequestCaravans(Pawn actor, CompTelegraphTable comp, bool financial, bool commercial, out string reason)
        {
            reason = ConnectionFailure(actor, comp);
            if (reason != null) return false;
            int now = Find.TickManager.TicksGame;
            if (!dispatches.CanRequest(now, financial, commercial))
            {
                reason = "HD_Company_RequestUnavailable".Translate();
                return false;
            }
            int count = (financial ? 1 : 0) + (commercial ? 1 : 0);
            Faction provider = comp.Network.Provider;
            if (provider.PlayerRelationKind != FactionRelationKind.Ally)
            {
                reason = "MustBeAlly".Translate();
                return false;
            }
            if (!provider.def.allowedArrivalTemperatureRange.ExpandedBy(-4f).Includes(map.mapTemperature.SeasonalTemp))
            {
                reason = "BadTemperature".Translate();
                return false;
            }
            if (comp.StoredPrimaryCells < count)
            {
                reason = "HD_Company_NeedsCells".Translate(count);
                return false;
            }
            // Resolve exact trader kinds before consuming anything. The two requests
            // have separate schedules and never touch Faction.lastTraderRequestTick.
            if ((financial && DefDatabase<TraderKindDef>.GetNamedSilentFail(TelegraphCompanies.FinancialTrader) == null)
                || (commercial && DefDatabase<TraderKindDef>.GetNamedSilentFail(TelegraphCompanies.CommercialTrader) == null))
            {
                reason = "HD_Company_RequestUnavailable".Translate();
                return false;
            }
            if (!dispatches.TrySchedule(now, financial, commercial,
                Rand.RangeInclusive(CompanyDispatchSchedule.MinimumTravelTicks, CompanyDispatchSchedule.MaximumTravelTicks),
                Rand.RangeInclusive(CompanyDispatchSchedule.MinimumTravelTicks, CompanyDispatchSchedule.MaximumTravelTicks)))
            {
                reason = "HD_Company_RequestUnavailable".Translate();
                return false;
            }
            for (int i = 0; i < count; i++) comp.ConsumePrimaryCell();
            caravanProvider = provider;
            provider.TryAffectGoodwillWith(Faction.OfPlayer, Faction.OfPlayer.CalculateAdjustedGoodwillChange(provider, -15) * count);
            Messages.Message("HD_Company_CaravansRequested".Translate(count), actor, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 0) return;
            TryArrival(dispatches.Financial, TelegraphCompanies.FinancialTrader, TelegraphCompany.NationalIndustrialCredit, now);
            TryArrival(dispatches.Commercial, TelegraphCompanies.CommercialTrader, TelegraphCompany.CentralHardwareSupply, now);
        }

        private void TryArrival(CompanyDispatchSchedule schedule, string kindName, TelegraphCompany company, int now)
        {
            if (!schedule.Pending || now < schedule.ArrivalTick) return;
            if (caravanProvider == null || caravanProvider.defeated || caravanProvider.HostileTo(Faction.OfPlayer))
            {
                schedule.Complete();
                Messages.Message("HD_Company_DispatchCancelled".Translate(TelegraphCompanies.Name(company)), MessageTypeDefOf.NegativeEvent);
                return;
            }
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category, map);
            parms.faction = caravanProvider;
            parms.traderKind = DefDatabase<TraderKindDef>.GetNamed(kindName);
            parms.forced = true;
            if (IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms)) schedule.Complete();
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
            Faction provider = comp.Network.Provider;
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
            comp.ConsumePrimaryCell();
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
