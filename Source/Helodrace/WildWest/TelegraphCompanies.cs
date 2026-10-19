using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace
{
    public enum TelegraphCompany
    {
        NationalIndustrialCredit,
        CentralHardwareSupply,
        PioneerOverseasService,
        HarringtonProtective
    }

    public static class TelegraphCompanies
    {
        public static readonly IReadOnlyList<TelegraphCompany> All = Array.AsReadOnly((TelegraphCompany[])Enum.GetValues(typeof(TelegraphCompany)));
        public const string FinancialTrader = "HD_WW_Caravan_HelodFinancial";
        public const string CommercialTrader = "HD_GW_Caravan_HelodCommercial";
        public const string EliteGuard = "HD_GW_HelodEliteCaravanGuard";
        public const string EliteCaptain = "HD_GW_HelodEliteCaravanGuardCaptain";
        public const int HarringtonCost = 500;
        public const int HarringtonGuardCount = 6;
        public const int HarringtonDurationTicks = 60000;

        public static string NameKey(TelegraphCompany company) => "HD_Company_Name_" + company;

        public static string Name(TelegraphCompany company) => NameKey(company).Translate();

        public static string ServiceKey(TelegraphCompany company)
        {
            switch (company)
            {
                case TelegraphCompany.NationalIndustrialCredit: return "HD_Company_Service_Financial";
                case TelegraphCompany.CentralHardwareSupply: return "HD_Company_Service_Commercial";
                case TelegraphCompany.PioneerOverseasService: return "HD_Company_Service_Pioneer";
                case TelegraphCompany.HarringtonProtective: return "HD_Company_Service_Security";
                default: return "HD_Company_Service_Unavailable";
            }
        }
    }

    public sealed class CompanyDispatchSchedule
    {
        public const int CooldownTicks = 240000;
        public const int MinimumTravelTicks = 60000;
        public const int MaximumTravelTicks = 120000;
        public int LastRequestTick = -CooldownTicks;
        public int ArrivalTick = -1;
        public int ExpiryTick = -1;
        public bool Pending => ArrivalTick >= 0;
        public bool CanRequest(int now) => !Pending && now >= LastRequestTick + CooldownTicks;

        internal void Schedule(int now, int travelTicks)
        {
            LastRequestTick = now;
            ArrivalTick = now + travelTicks;
            ExpiryTick = now + CooldownTicks;
        }

        public void Complete() => ArrivalTick = -1;

        public bool Retry(int now)
        {
            if (now >= ExpiryTick) { Complete(); return false; }
            ArrivalTick = System.Math.Min(ExpiryTick, now + 2500);
            return true;
        }
    }

    public sealed class CompanyDispatchBook
    {
        public readonly CompanyDispatchSchedule Financial = new CompanyDispatchSchedule();
        public readonly CompanyDispatchSchedule Commercial = new CompanyDispatchSchedule();

        public bool CanRequest(int now, bool financial, bool commercial)
            => (financial || commercial) && (!financial || Financial.CanRequest(now)) && (!commercial || Commercial.CanRequest(now));

        public bool TrySchedule(int now, bool financial, bool commercial, int financialTravel, int commercialTravel)
        {
            if (!CanRequest(now, financial, commercial)) return false;
            if (financial && (financialTravel < CompanyDispatchSchedule.MinimumTravelTicks || financialTravel > CompanyDispatchSchedule.MaximumTravelTicks)) return false;
            if (commercial && (commercialTravel < CompanyDispatchSchedule.MinimumTravelTicks || commercialTravel > CompanyDispatchSchedule.MaximumTravelTicks)) return false;
            if (financial) Financial.Schedule(now, financialTravel);
            if (commercial) Commercial.Schedule(now, commercialTravel);
            return true;
        }
    }
}
