using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.Economy
{
    public sealed class TradeCommitment : IExposable
    {
        public TelegraphCompany Company;
        public Faction Provider;
        public Pawn Merchant;
        public Lord CaravanLord;
        public float Minimum, Turnover;
        public int OpportunityTicks;
        public bool Arrived, Closed;
        public string Outcome;
        public float Penalty;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Company, "company"); Scribe_References.Look(ref Provider, "provider");
            Scribe_References.Look(ref Merchant, "merchant"); Scribe_References.Look(ref CaravanLord, "lord");
            Scribe_Values.Look(ref Minimum, "minimum"); Scribe_Values.Look(ref Turnover, "turnover");
            Scribe_Values.Look(ref OpportunityTicks, "opportunity"); Scribe_Values.Look(ref Arrived, "arrived");
            Scribe_Values.Look(ref Closed, "closed"); Scribe_Values.Look(ref Outcome, "outcome"); Scribe_Values.Look(ref Penalty, "penalty");
        }
        public void Settle(bool responsible)
        {
            if (Closed) return;
            Closed = true;
            Penalty = responsible && Minimum > 0 ? (float)Math.Round(10 * Math.Clamp(1 - Turnover / Minimum, 0, 1), 2) : 0;
            Outcome = !responsible ? "HD_Commitment_Waived" : Penalty > 0 ? "HD_Commitment_Defaulted" : "HD_Commitment_Fulfilled";
            if (Penalty > 0) GameComponent_FinancialGoodwill.Current?.Adjust(Provider, -Penalty);
            Messages.Message("HD_Commitment_Settled".Translate(TelegraphCompanies.Name(Company), Outcome.Translate(), Turnover.ToString("0.##"), Minimum.ToString("0.##"), Penalty.ToString("0.##")),
                Penalty > 0 ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent);
            Merchant = null; CaravanLord = null;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class Patch_CommitmentTrade
    {
        public readonly record struct Pending(TradeCommitment Contract, float Value);
        [HarmonyPrefix]
        public static void Prefix(TradeDeal __instance, out Pending __state)
        {
            __state = default;
            if (TradeSession.giftMode || TradeSession.trader is not Pawn merchant || merchant.Map == null) return;
            var contract = merchant.Map.GetComponent<MapComponent_TelegraphCompanies>().CommitmentFor(merchant.GetLord());
            if (contract == null || contract.Merchant != merchant) return;
            float value = 0;
            foreach (var item in __instance.AllTradeables)
                if (item.ThingDef != null && item.ThingDef != ThingDefOf.Silver && item.ThingDef.defName != "HD_Money" && item.ActionToDo != TradeAction.None)
                    value += Math.Abs(item.CountToTransfer) * item.GetPriceFor(item.ActionToDo);
            __state = new Pending(contract, value);
        }
        [HarmonyPostfix]
        public static void Postfix(bool __result, bool actuallyTraded, Pending __state)
        {
            if (__result && actuallyTraded && __state.Contract is { Closed: false }) __state.Contract.Turnover += __state.Value;
        }
    }

    [HarmonyPatch(typeof(LordJob_TradeWithColony), nameof(LordJob_TradeWithColony.CreateGraph))]
    public static class Patch_CommitmentDeparture
    {
        [HarmonyPostfix]
        public static void Postfix(LordJob_TradeWithColony __instance, StateGraph __result)
        {
            foreach (var transition in __result.transitions)
            {
                if (transition.target is not LordToil_ExitMapAndEscortCarriers && transition.target is not LordToil_ExitMap) continue;
                bool normal = transition.sources.Any(t => t.GetType().FullName == "RimWorld.LordToil_DefendTraderCaravan") && transition.triggers.Any(t => t is Trigger_TicksPassed);
                bool dismissal = transition.triggers.Any(t => t is Trigger_Custom);
                transition.AddPreAction(new TransitionAction_Custom(() =>
                {
                    var lord = __instance.lord;
                    var contract = lord?.Map.GetComponent<MapComponent_TelegraphCompanies>().CommitmentFor(lord);
                    if (contract != null) contract.Settle(dismissal || (normal && contract.OpportunityTicks >= 2500));
                }));
            }
        }
    }

    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnDamaged))]
    public static class Patch_CommitmentDamage
    {
        [HarmonyPrefix]
        public static void Prefix(Lord __instance, [HarmonyArgument(0)] Pawn victim, [HarmonyArgument(1)] DamageInfo dinfo)
        {
            if (dinfo.Amount <= 0 || dinfo.Def?.ExternalViolenceFor(victim) != true) return;
            __instance.Map.GetComponent<MapComponent_TelegraphCompanies>().CommitmentFor(__instance)?.Settle(dinfo.Instigator?.Faction == Faction.OfPlayer);
        }
    }
    [HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnAttemptArrested))]
    public static class Patch_CommitmentArrest
    {
        [HarmonyPrefix]
        public static void Prefix(Lord __instance) => __instance.Map.GetComponent<MapComponent_TelegraphCompanies>().CommitmentFor(__instance)?.Settle(true);
    }
}

namespace Helodrace
{
    public sealed partial class MapComponent_TelegraphCompanies
    {
        private List<TradeCommitment> commitments = new();
        public const float CommitmentWealthRequired = 20000;
        public float CommitmentMinimum => (float)Math.Ceiling(Math.Clamp(map.wealthWatcher.WealthTotal * .05f, 1000, 5000));
        public IReadOnlyList<TradeCommitment> Commitments => commitments;
        public TradeCommitment ActiveCommitment(TelegraphCompany company) => commitments.FirstOrDefault(c => c.Company == company && !c.Closed);
        public TradeCommitment CommitmentFor(Lord lord) => lord == null ? null : commitments.FirstOrDefault(c => !c.Closed && c.CaravanLord == lord);
        private void ExposeCommitments()
        {
            Scribe_Collections.Look(ref commitments, "tradeCommitments", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) commitments ??= new();
        }
        public string CommitmentFailure(CompTelegraphTable comp, TelegraphCompany company)
        {
            if (company is not (TelegraphCompany.CentralHardwareSupply or TelegraphCompany.WhitmoreArms or TelegraphCompany.ContinentalPetroleum)) return "HD_Commitment_GoodsOnly".Translate();
            if (comp?.Provider == null || comp.Provider.defeated || comp.Provider.HostileTo(Faction.OfPlayer)) return "HD_Commitment_PeaceRequired".Translate();
            if (!dispatches.CanRequest(Find.TickManager.TicksGame, new[] { company }) || ActiveCommitment(company) != null) return "HD_Company_RequestUnavailable".Translate();
            if (map.wealthWatcher.WealthTotal < CommitmentWealthRequired) return "HD_Commitment_WealthRequired".Translate(CommitmentWealthRequired);
            if (!comp.HasTransmissionCapacity(1)) return "HD_Company_NeedsCells".Translate(1);
            return comp.TransmissionFailure(comp.Provider);
        }
        public bool TryRequestCommittedCaravan(Pawn actor, CompTelegraphTable comp, TelegraphCompany company, out string reason)
        {
            reason = ConnectionFailure(actor, comp) ?? CommitmentFailure(comp, company);
            if (reason != null) return false;
            if (!comp.Provider.def.allowedArrivalTemperatureRange.ExpandedBy(-4).Includes(map.mapTemperature.SeasonalTemp)) { reason = "BadTemperature".Translate(); return false; }
            if (DefDatabase<TraderKindDef>.GetNamedSilentFail(TelegraphCompanies.TraderKind(company)) == null) { reason = "HD_Company_RequestUnavailable".Translate(); return false; }
            if (!dispatches.TrySchedule(Find.TickManager.TicksGame, new[] { company }, new[] { Rand.RangeInclusive(CompanyDispatchSchedule.MinimumTravelTicks, CompanyDispatchSchedule.MaximumTravelTicks) }, false)) return false;
            commitments.RemoveAll(c => c.Closed && c.Company == company);
            commitments.Add(new TradeCommitment { Company = company, Provider = comp.Provider, Minimum = CommitmentMinimum });
            caravanProviders[company] = comp.Provider; comp.TryTransmit();
            Messages.Message("HD_Company_CaravansRequested".Translate(1), actor, MessageTypeDefOf.PositiveEvent); return true;
        }
        public bool CancelCommitment(TelegraphCompany company)
        {
            var contract = ActiveCommitment(company);
            if (contract == null || contract.Arrived) return false;
            Schedule(company).Complete(); contract.Settle(true); return true;
        }
        private void BindCommitment(TelegraphCompany company, HashSet<Lord> prior)
        {
            var contract = ActiveCommitment(company); if (contract == null) return;
            var lord = map.lordManager.lords.FirstOrDefault(l => !prior.Contains(l) && l.ownedPawns.Any(p => p.TraderKind?.defName == TelegraphCompanies.TraderKind(company)));
            if (lord == null) { contract.Settle(false); return; }
            contract.Arrived = true; contract.CaravanLord = lord; contract.Merchant = lord.ownedPawns.Single(p => p.trader != null);
        }
        private void TickCommitments()
        {
            foreach (var c in commitments.Where(c => !c.Closed && c.Arrived))
            {
                if (c.Merchant == null || !c.Merchant.Spawned || c.Merchant.Dead || c.CaravanLord == null) { c.Settle(false); continue; }
                if (c.Merchant.CanTradeNow && !c.Provider.HostileTo(Faction.OfPlayer) && map.mapPawns.FreeColonistsSpawned.Any(p =>
                    !p.Downed && p.skills != null && !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                    && p.CanReach(c.Merchant, PathEndMode.Touch, Danger.Some))) c.OpportunityTicks += 250;
            }
        }
    }
}
