using System;
using RimWorld;
using Verse;

namespace UnityEngine { public static class Mathf { public static int CeilToInt(float value) => (int)Math.Ceiling(value); } }
namespace Helodrace
{
    public static class HelodForwardBaseServiceUtility { public const float SthalerSilverValue = 5f; }
    // Native world-object/payment boundaries. The default handler and its saved
    // guard are compiled from the production partial class.
    public partial class HelodForwardBase
    {
        public Faction Faction;
        public string Id = "Base_1", LabelCap = "Forward base";
        public int WithdrawalCount;
        public float Bill = 80.25f;
        private const int PaymentFailureGoodwillPenalty = -12;
        private int usagePeriodStartTick = 60000;
        public string GetUniqueLoadID() => Id;
        private float PeriodUsageCost() => Bill;
        private void BeginWithdrawal() { WithdrawalCount++; }
        public void Default() => HandlePaymentFailure();
        public void ExposeDefault() => ExposePaymentDefaultState();
    }
}
