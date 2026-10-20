using Helodrace.Economy;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public partial class HelodForwardBase
    {
        private bool paymentDefaultProcessed;
        private void ExposePaymentDefaultState()
            => Scribe_Values.Look(ref paymentDefaultProcessed, "paymentDefaultProcessed");

        private void HandlePaymentFailure()
        {
            if (paymentDefaultProcessed) return;
            paymentDefaultProcessed = true;
            ContractArrears.Process(Faction, CaravanLossKind.ForwardBaseOverdue,
                GetUniqueLoadID() + "/" + usagePeriodStartTick, TelegraphCompanies.Name(TelegraphCompany.PioneerOverseasService),
                LabelCap, Mathf.CeilToInt(PeriodUsageCost()), 0f);
            Messages.Message("HD_ForwardBase_PeriodPaymentDefaulted".Translate(), this, MessageTypeDefOf.NegativeEvent);
            BeginWithdrawal();
        }
    }
}
