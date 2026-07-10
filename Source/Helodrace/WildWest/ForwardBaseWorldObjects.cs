using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using System.Collections.Generic;
using System.Text;

namespace Helodrace
{
    public class HelodForwardBaseConstruction : WorldObject
    {
        private int completeTick;
        private string contractInfo;
        private List<HelodForwardBaseService> contractServices = new List<HelodForwardBaseService>();
        private HelodForwardBaseCostKind contractCostKind = HelodForwardBaseCostKind.FFP;
        private HelodForwardBaseIdiqPricingKind idiqPricingKind = HelodForwardBaseIdiqPricingKind.FFP;
        private int contractDurationDays = HelodForwardBaseServiceUtility.ServiceBillingPeriodDays;
        private float contractMilitaryCredit;
        private int contractStartTick;

        public int CompleteTick => completeTick;
        public string ContractInfo => contractInfo;
        public List<HelodForwardBaseService> ContractServices => contractServices;

        public HelodForwardBaseCostKind ContractCostKind => contractCostKind;
        public HelodForwardBaseIdiqPricingKind IdiqPricingKind => idiqPricingKind;
        public int ContractDurationDays => contractDurationDays;
        public float ContractMilitaryCredit => contractMilitaryCredit;

        public void StartConstruction(int durationTicks)
        {
            completeTick = Find.TickManager.TicksGame + durationTicks;
        }

        public void SetContractInfo(string info)
        {
            contractInfo = info;
        }

        public void SetContractServices(IEnumerable<HelodForwardBaseService> services)
        {
            contractServices = services == null ? new List<HelodForwardBaseService>() : new List<HelodForwardBaseService>(services);
        }

        public void ConfigureContract(HelodForwardBaseCostKind costKind, HelodForwardBaseIdiqPricingKind pricingKind, int durationDays, float militaryCredit = 0f)
        {
            contractCostKind = costKind;
            idiqPricingKind = pricingKind;
            contractDurationDays = durationDays;
            contractMilitaryCredit = militaryCredit;
            contractStartTick = Find.TickManager?.TicksGame ?? 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref completeTick, "completeTick", 0);
            Scribe_Values.Look(ref contractInfo, "contractInfo");
            Scribe_Collections.Look(ref contractServices, "contractServices", LookMode.Value);
            Scribe_Values.Look(ref contractCostKind, "contractCostKind", HelodForwardBaseCostKind.FFP);
            Scribe_Values.Look(ref idiqPricingKind, "idiqPricingKind", HelodForwardBaseIdiqPricingKind.FFP);
            Scribe_Values.Look(ref contractDurationDays, "contractDurationDays", HelodForwardBaseServiceUtility.ServiceBillingPeriodDays);
            Scribe_Values.Look(ref contractMilitaryCredit, "contractMilitaryCredit", 0f);
            Scribe_Values.Look(ref contractStartTick, "contractStartTick", 0);
            if (contractServices == null)
            {
                contractServices = new List<HelodForwardBaseService>();
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (completeTick > 0 && Find.TickManager.TicksGame >= completeTick)
            {
                CompleteConstruction();
            }
        }

        public override string GetInspectString()
        {
            string inspect = base.GetInspectString();
            if (completeTick > Find.TickManager.TicksGame)
            {
                string timeLeft = (completeTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += "HD_ForwardBaseConstruction_TimeLeft".Translate(timeLeft);
            }

            if (!contractInfo.NullOrEmpty())
            {
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += contractInfo;
            }

            return inspect;
        }

        private void CompleteConstruction()
        {
            WorldObjectDef completeDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("HD_ForwardBase");
            if (completeDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_ForwardBase WorldObjectDef is missing.", 72851061);
                return;
            }

            int targetTile = Tile;
            Faction targetFaction = Faction;
            Destroy();

            WorldObject complete = WorldObjectMaker.MakeWorldObject(completeDef);
            complete.Tile = targetTile;
            complete.SetFaction(targetFaction);
            HelodForwardBase forwardBase = complete as HelodForwardBase;
            if (forwardBase != null)
            {
                forwardBase.SetContractInfo(contractInfo);
                forwardBase.SetContractServices(contractServices);
                forwardBase.ConfigureContract(contractCostKind, idiqPricingKind, contractDurationDays, contractMilitaryCredit, contractStartTick);
            }
            Find.WorldObjects.Add(complete);
            Messages.Message("HD_ForwardBaseConstruction_Completed".Translate(), complete, MessageTypeDefOf.PositiveEvent);
        }
    }

    public class HelodForwardBase : WorldObject
    {
        private string contractInfo;
        private List<HelodForwardBaseService> contractServices = new List<HelodForwardBaseService>();
        private HelodForwardBaseCostKind contractCostKind = HelodForwardBaseCostKind.FFP;
        private HelodForwardBaseIdiqPricingKind idiqPricingKind = HelodForwardBaseIdiqPricingKind.FFP;
        private int contractDurationDays = HelodForwardBaseServiceUtility.ServiceBillingPeriodDays;
        private float contractMilitaryCredit;
        private int contractStartTick;
        private int usagePeriodStartTick;
        private List<HelodForwardBaseService> usageServices = new List<HelodForwardBaseService>();
        private List<int> usageCounts = new List<int>();
        private List<int> totalUsageCounts = new List<int>();
        private const int WithdrawalTicks = 3 * GenDate.TicksPerDay;
        private const float PaymentFailureCreditPenalty = 80f;
        private const int PaymentFailureGoodwillPenalty = -12;
        private const int ContractCompleteGoodwillBonus = 6;

        public string ContractInfo => contractInfo;
        public List<HelodForwardBaseService> ContractServices => contractServices;
        public HelodForwardBaseCostKind ContractCostKind => contractCostKind;
        public HelodForwardBaseIdiqPricingKind IdiqPricingKind => idiqPricingKind;
        public int ContractDurationDays => contractDurationDays;
        public float ContractMilitaryCredit => contractMilitaryCredit;

        public void SetContractInfo(string info)
        {
            contractInfo = info;
        }

        public void SetContractServices(IEnumerable<HelodForwardBaseService> services)
        {
            contractServices = services == null ? new List<HelodForwardBaseService>() : new List<HelodForwardBaseService>(services);
        }

        public bool HasService(HelodForwardBaseService service)
        {
            return contractServices != null && contractServices.Contains(service);
        }

        public void ConfigureContract(HelodForwardBaseCostKind costKind, HelodForwardBaseIdiqPricingKind pricingKind, int durationDays, float militaryCredit = 0f, int startTick = 0)
        {
            contractCostKind = costKind;
            idiqPricingKind = pricingKind;
            contractDurationDays = durationDays;
            contractMilitaryCredit = militaryCredit;
            contractStartTick = startTick > 0 ? startTick : Find.TickManager?.TicksGame ?? 0;
            EnsureUsagePeriod();
        }

        public bool HasServiceCapacity(HelodForwardBaseService service)
        {
            if (!HasService(service))
            {
                return false;
            }

            EnsureUsagePeriod();
            if (UsesCreditBudget && IsCreditBudgetExhausted())
            {
                return false;
            }

            if (!UsesLimitedPeriodQuota)
            {
                return true;
            }

            int limit = ServiceUseLimitPerBillingPeriod(service);
            return limit <= 0 || CurrentUsageCount(service) < limit;
        }

        public bool ShouldRecordServiceUseOnCall(HelodForwardBaseService service)
        {
            return HasService(service) && UsesFixedPriceUsageCost;
        }

        public bool ShouldRecordServiceUseOnExecution(HelodForwardBaseService service)
        {
            return HasService(service) && UsesReimbursableUsageCost;
        }

        public bool TryConsumeServiceUse(HelodForwardBaseService service, out string failReason)
        {
            failReason = null;
            if (!HasService(service))
            {
                failReason = "HD_ForwardBase_ServiceUnavailable".Translate().ToString();
                return false;
            }

            EnsureUsagePeriod();
            if (UsesCreditBudget && IsCreditBudgetExhausted())
            {
                failReason = "HD_ForwardBase_ServiceCreditExhausted".Translate(HelodForwardBaseServiceUtility.ServiceBillingPeriodDays, contractMilitaryCredit.ToString("F0")).ToString();
                return false;
            }

            int limit = ServiceUseLimitPerBillingPeriod(service);
            if (UsesLimitedPeriodQuota && limit > 0 && CurrentUsageCount(service) >= limit)
            {
                failReason = "HD_ForwardBase_ServiceQuotaExhausted".Translate(ServiceLabel(service), limit, HelodForwardBaseServiceUtility.ServiceBillingPeriodDays, UsageUnitLabel(service)).ToString();
                return false;
            }

            int index = UsageIndex(service);
            usageCounts[index]++;
            totalUsageCounts[index]++;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contractInfo, "contractInfo");
            Scribe_Collections.Look(ref contractServices, "contractServices", LookMode.Value);
            Scribe_Values.Look(ref contractCostKind, "contractCostKind", HelodForwardBaseCostKind.FFP);
            Scribe_Values.Look(ref idiqPricingKind, "idiqPricingKind", HelodForwardBaseIdiqPricingKind.FFP);
            Scribe_Values.Look(ref contractDurationDays, "contractDurationDays", HelodForwardBaseServiceUtility.ServiceBillingPeriodDays);
            Scribe_Values.Look(ref contractMilitaryCredit, "contractMilitaryCredit", 0f);
            Scribe_Values.Look(ref contractStartTick, "contractStartTick", 0);
            Scribe_Values.Look(ref usagePeriodStartTick, "usagePeriodStartTick", 0);
            Scribe_Collections.Look(ref usageServices, "usageServices", LookMode.Value);
            Scribe_Collections.Look(ref usageCounts, "usageCounts", LookMode.Value);
            Scribe_Collections.Look(ref totalUsageCounts, "totalUsageCounts", LookMode.Value);
            if (contractServices == null)
            {
                contractServices = new List<HelodForwardBaseService>();
            }
            if (usageServices == null)
            {
                usageServices = new List<HelodForwardBaseService>();
            }
            if (usageCounts == null)
            {
                usageCounts = new List<int>();
            }
            if (totalUsageCounts == null)
            {
                totalUsageCounts = new List<int>();
            }
            NormalizeUsageLists();
            if (contractStartTick <= 0)
            {
                contractStartTick = usagePeriodStartTick > 0 ? usagePeriodStartTick : Find.TickManager?.TicksGame ?? 0;
            }
        }

        protected override void Tick()
        {
            base.Tick();
            EnsureUsagePeriod();
            CheckContractEnd();
        }

        public override string GetInspectString()
        {
            string inspect = base.GetInspectString();
            if (!contractInfo.NullOrEmpty())
            {
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += contractInfo;
            }

            string usageInfo = UsageInspectString();
            if (!usageInfo.NullOrEmpty())
            {
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += usageInfo;
            }

            int ticksLeft = ContractEndTick - (Find.TickManager?.TicksGame ?? 0);
            if (ticksLeft > 0)
            {
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += "HD_ForwardBase_ContractTimeLeft".Translate(ticksLeft.ToStringTicksToPeriod());
            }

            return inspect;
        }

        private int ContractEndTick => contractStartTick + contractDurationDays * GenDate.TicksPerDay;

        private bool UsesLimitedPeriodQuota => contractCostKind != HelodForwardBaseCostKind.IDIQ;

        private bool UsesCreditBudget => contractCostKind != HelodForwardBaseCostKind.FFP;

        private bool UsesFixedPriceUsageCost => contractCostKind == HelodForwardBaseCostKind.FFP
            || (contractCostKind == HelodForwardBaseCostKind.IDIQ && idiqPricingKind == HelodForwardBaseIdiqPricingKind.FFP);

        private bool UsesReimbursableUsageCost => contractCostKind == HelodForwardBaseCostKind.CostReimbursement
            || (contractCostKind == HelodForwardBaseCostKind.IDIQ && idiqPricingKind == HelodForwardBaseIdiqPricingKind.CostReimbursement);

        private bool UsesPeriodSettlement => UsesFixedPriceUsageCost || UsesReimbursableUsageCost;

        private void EnsureUsagePeriod()
        {
            NormalizeUsageLists();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (usagePeriodStartTick <= 0)
            {
                usagePeriodStartTick = now;
            }

            while (now >= usagePeriodStartTick + HelodForwardBaseServiceUtility.ServiceBillingPeriodTicks)
            {
                if (!TrySettleUsagePeriod())
                {
                    return;
                }

                usagePeriodStartTick += HelodForwardBaseServiceUtility.ServiceBillingPeriodTicks;
                for (int i = 0; i < usageCounts.Count; i++)
                {
                    usageCounts[i] = 0;
                }
            }
        }

        private int UsageIndex(HelodForwardBaseService service)
        {
            NormalizeUsageLists();
            int index = usageServices.IndexOf(service);
            if (index >= 0)
            {
                return index;
            }

            usageServices.Add(service);
            usageCounts.Add(0);
            totalUsageCounts.Add(0);
            return usageServices.Count - 1;
        }

        private int CurrentUsageCount(HelodForwardBaseService service)
        {
            int index = UsageIndex(service);
            return index >= 0 && index < usageCounts.Count ? usageCounts[index] : 0;
        }

        private int TotalUsageCount(HelodForwardBaseService service)
        {
            int index = UsageIndex(service);
            return index >= 0 && index < totalUsageCounts.Count ? totalUsageCounts[index] : 0;
        }

        private string UsageInspectString()
        {
            if (contractServices == null || contractServices.Count == 0)
            {
                return null;
            }

            EnsureUsagePeriod();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("HD_ForwardBase_ServiceUsageHeader".Translate(HelodForwardBaseServiceUtility.ServiceBillingPeriodDays).ToString());
            float reimbursableTotal = 0f;
            for (int i = 0; i < contractServices.Count; i++)
            {
                HelodForwardBaseService service = contractServices[i];
                int limit = ServiceUseLimitPerBillingPeriod(service);
                int current = CurrentUsageCount(service);
                int total = TotalUsageCount(service);
                if (limit > 0 && UsesLimitedPeriodQuota)
                {
                    builder.AppendLine("HD_ForwardBase_ServiceUsageLineLimited".Translate(ServiceLabel(service), current, limit, total, UsageUnitLabel(service)).ToString());
                }
                else
                {
                    builder.AppendLine("HD_ForwardBase_ServiceUsageLineUnlimited".Translate(ServiceLabel(service), current, total, UsageUnitLabel(service)).ToString());
                }

                if (UsesReimbursableUsageCost)
                {
                    reimbursableTotal += total * HelodForwardBaseServiceUtility.ServiceUseCostGoldStandard(service);
                }
            }

            if (UsesCreditBudget && contractMilitaryCredit > 0f)
            {
                builder.AppendLine("HD_ForwardBase_ServiceUsageCredit".Translate(PeriodUsageCostGoldStandard().ToString("F0"), contractMilitaryCredit.ToString("F0")).ToString());
            }

            if (UsesReimbursableUsageCost)
            {
                builder.Append("HD_ForwardBase_ServiceUsageReimbursable".Translate(HelodForwardBaseServiceUtility.FormatSthalerValue(reimbursableTotal)).ToString());
            }

            return builder.ToString().TrimEndNewlines();
        }

        private bool IsCreditBudgetExhausted()
        {
            return contractMilitaryCredit > 0f && PeriodUsageCostGoldStandard() >= contractMilitaryCredit;
        }

        private int ServiceUseLimitPerBillingPeriod(HelodForwardBaseService service)
        {
            if (contractCostKind == HelodForwardBaseCostKind.CostReimbursement)
            {
                return 50;
            }

            return HelodForwardBaseServiceUtility.ServiceUseLimitPerBillingPeriod(service);
        }

        private float PeriodUsageCostGoldStandard()
        {
            float cost = 0f;
            if (usageServices == null || usageCounts == null)
            {
                return cost;
            }

            for (int i = 0; i < usageServices.Count && i < usageCounts.Count; i++)
            {
                cost += usageCounts[i] * HelodForwardBaseServiceUtility.ServiceUseCostGoldStandard(usageServices[i]);
            }

            return cost;
        }

        private bool TrySettleUsagePeriod()
        {
            if (!UsesPeriodSettlement)
            {
                return true;
            }

            string failReason;
            if (!TryCollectPeriodPayment(null, out failReason))
            {
                HandlePaymentFailure();
                return false;
            }

            return true;
        }

        private void CheckContractEnd()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (contractStartTick <= 0 || contractDurationDays <= 0 || now < ContractEndTick)
            {
                return;
            }

            if (!TrySettleUsagePeriod())
            {
                return;
            }

            AwardContractCompletionGoodwill();
            Messages.Message("HD_ForwardBase_ContractCompleted".Translate(), this, MessageTypeDefOf.PositiveEvent);
            BeginWithdrawal();
        }

        private bool TryCollectPeriodPayment(Map preferredMap, out string failReason)
        {
            failReason = null;
            int moneyNeeded = Mathf.CeilToInt(PeriodUsageCostGoldStandard());
            ThingDef moneyDef = DefDatabase<ThingDef>.GetNamedSilentFail("HD_Money");
            if (moneyNeeded <= 0)
            {
                return true;
            }

            if (moneyDef == null)
            {
                failReason = "HD_ForwardBase_PeriodPaymentFailed".Translate(moneyNeeded).ToString();
                return false;
            }

            if (CountAvailableThing(preferredMap, moneyDef) < moneyNeeded)
            {
                failReason = "HD_ForwardBase_PeriodPaymentFailed".Translate(moneyNeeded).ToString();
                return false;
            }

            ConsumeAvailableThing(preferredMap, moneyDef, moneyNeeded);
            return true;
        }

        private void HandlePaymentFailure()
        {
            contractMilitaryCredit = Mathf.Max(0f, contractMilitaryCredit - PaymentFailureCreditPenalty);
            if (Faction != null && Faction != Faction.OfPlayer)
            {
                Faction.TryAffectGoodwillWith(Faction.OfPlayer, PaymentFailureGoodwillPenalty);
            }

            Messages.Message("HD_ForwardBase_PeriodPaymentDefaulted".Translate(), this, MessageTypeDefOf.NegativeEvent);
            BeginWithdrawal();
        }

        private void AwardContractCompletionGoodwill()
        {
            if (Faction != null && Faction != Faction.OfPlayer)
            {
                Faction.TryAffectGoodwillWith(Faction.OfPlayer, ContractCompleteGoodwillBonus);
            }
        }

        private void BeginWithdrawal()
        {
            WorldObjectDef withdrawalDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("HD_ForwardBaseWithdrawal");
            if (withdrawalDef == null)
            {
                Destroy();
                return;
            }

            int targetTile = Tile;
            Faction targetFaction = Faction;
            string info = contractInfo;
            Destroy();

            HelodForwardBaseWithdrawal withdrawal = WorldObjectMaker.MakeWorldObject(withdrawalDef) as HelodForwardBaseWithdrawal;
            if (withdrawal == null)
            {
                return;
            }

            withdrawal.Tile = targetTile;
            withdrawal.SetFaction(targetFaction);
            withdrawal.StartWithdrawal(WithdrawalTicks, info);
            Find.WorldObjects.Add(withdrawal);
        }

        private static int CountAvailableThing(Map preferredMap, ThingDef def)
        {
            int count = CountThing(preferredMap, def);
            if (Find.Maps == null)
            {
                return count;
            }

            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map == preferredMap || !map.IsPlayerHome)
                {
                    continue;
                }

                count += CountThing(map, def);
            }

            return count;
        }

        private static bool ConsumeAvailableThing(Map preferredMap, ThingDef def, int count)
        {
            int remaining = count;
            remaining -= ConsumeThing(preferredMap, def, remaining);
            if (remaining <= 0 || Find.Maps == null)
            {
                return remaining <= 0;
            }

            for (int i = 0; i < Find.Maps.Count && remaining > 0; i++)
            {
                Map map = Find.Maps[i];
                if (map == preferredMap || !map.IsPlayerHome)
                {
                    continue;
                }

                remaining -= ConsumeThing(map, def, remaining);
            }

            return remaining <= 0;
        }

        private static int CountThing(Map map, ThingDef def)
        {
            if (map == null || def == null)
            {
                return 0;
            }

            int count = 0;
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (!thing.Destroyed && !thing.Position.Fogged(map))
                {
                    count += thing.stackCount;
                }
            }

            return count;
        }

        private static int ConsumeThing(Map map, ThingDef def, int count)
        {
            if (map == null || def == null || count <= 0)
            {
                return 0;
            }

            int consumed = 0;
            List<Thing> things = new List<Thing>(map.listerThings.ThingsOfDef(def));
            for (int i = 0; i < things.Count && consumed < count; i++)
            {
                Thing thing = things[i];
                if (thing.Destroyed || thing.Position.Fogged(map))
                {
                    continue;
                }

                int taken = Mathf.Min(count - consumed, thing.stackCount);
                consumed += taken;
                if (taken >= thing.stackCount)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    thing.stackCount -= taken;
                }
            }

            return consumed;
        }

        private void NormalizeUsageLists()
        {
            if (usageServices == null)
            {
                usageServices = new List<HelodForwardBaseService>();
            }
            if (usageCounts == null)
            {
                usageCounts = new List<int>();
            }
            if (totalUsageCounts == null)
            {
                totalUsageCounts = new List<int>();
            }

            while (usageCounts.Count < usageServices.Count)
            {
                usageCounts.Add(0);
            }
            while (totalUsageCounts.Count < usageServices.Count)
            {
                totalUsageCounts.Add(0);
            }
            while (usageCounts.Count > usageServices.Count)
            {
                usageCounts.RemoveAt(usageCounts.Count - 1);
            }
            while (totalUsageCounts.Count > usageServices.Count)
            {
                totalUsageCounts.RemoveAt(totalUsageCounts.Count - 1);
            }
        }

        private static string ServiceLabel(HelodForwardBaseService service)
        {
            return ("HD_TelegraphTable_ForwardBase_Service_" + service).Translate().ToString();
        }

        private string UsageUnitLabel(HelodForwardBaseService service)
        {
            if (ShouldRecordServiceUseOnExecution(service))
            {
                return "HD_ForwardBase_ServiceUsageUnit_Shot".Translate().ToString();
            }

            return "HD_ForwardBase_ServiceUsageUnit_Call".Translate().ToString();
        }
    }

    public class HelodForwardBaseWithdrawal : WorldObject
    {
        private int withdrawTick;
        private string contractInfo;

        public void StartWithdrawal(int durationTicks, string info)
        {
            withdrawTick = Find.TickManager.TicksGame + durationTicks;
            contractInfo = info;
        }

        protected override void Tick()
        {
            base.Tick();
            if (withdrawTick > 0 && Find.TickManager.TicksGame >= withdrawTick)
            {
                Messages.Message("HD_ForwardBase_WithdrawalCompleted".Translate(), this, MessageTypeDefOf.NeutralEvent);
                Destroy();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref withdrawTick, "withdrawTick", 0);
            Scribe_Values.Look(ref contractInfo, "contractInfo");
        }

        public override string GetInspectString()
        {
            string inspect = base.GetInspectString();
            if (withdrawTick > Find.TickManager.TicksGame)
            {
                string timeLeft = (withdrawTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += "HD_ForwardBase_WithdrawalTimeLeft".Translate(timeLeft);
            }

            if (!contractInfo.NullOrEmpty())
            {
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += contractInfo;
            }

            return inspect;
        }
    }
}
