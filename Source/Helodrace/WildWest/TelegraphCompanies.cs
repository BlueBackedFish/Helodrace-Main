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
        HarringtonProtective,
        WhitmoreArms,
        ContinentalPetroleum
    }

    public static class TelegraphCompanies
    {
        public static readonly IReadOnlyList<TelegraphCompany> All = Array.AsReadOnly((TelegraphCompany[])Enum.GetValues(typeof(TelegraphCompany)));
        public static readonly IReadOnlyList<TelegraphCompany> CaravanCompanies = Array.AsReadOnly(new[]
            { TelegraphCompany.NationalIndustrialCredit, TelegraphCompany.CentralHardwareSupply, TelegraphCompany.WhitmoreArms, TelegraphCompany.ContinentalPetroleum });
        public const string FinancialTrader = "HD_WW_Caravan_HelodFinancial";
        public const string CommercialTrader = "HD_GW_Caravan_HelodCommercial";
        public const string ArmsTrader = "HD_WW_Caravan_HelodArms";
        public const string PetroleumTrader = "HD_WW_Caravan_HelodPetroleum";
        public const string EliteGuard = "HD_GW_HelodEliteCaravanGuard";
        public const string EliteCaptain = "HD_GW_HelodEliteCaravanGuardCaptain";
        public const int HarringtonCost = 500;
        public const int HarringtonGuardCount = 6;
        public const int HarringtonDurationTicks = 60000;

        public static string NameKey(TelegraphCompany company) => "HD_Company_Name_" + company;

        public static string Name(TelegraphCompany company) => NameKey(company).Translate();

        public static string TraderKind(TelegraphCompany company)
        {
            switch (company)
            {
                case TelegraphCompany.NationalIndustrialCredit: return FinancialTrader;
                case TelegraphCompany.CentralHardwareSupply: return CommercialTrader;
                case TelegraphCompany.WhitmoreArms: return ArmsTrader;
                case TelegraphCompany.ContinentalPetroleum: return PetroleumTrader;
                default: return null;
            }
        }

        public static string DescriptionKey(TelegraphCompany company)
        {
            switch (company)
            {
                case TelegraphCompany.NationalIndustrialCredit: return "HD_Company_FinancialDesc";
                case TelegraphCompany.CentralHardwareSupply: return "HD_Company_CommercialDesc";
                case TelegraphCompany.PioneerOverseasService: return "HD_Company_PioneerDesc";
                case TelegraphCompany.HarringtonProtective: return "HD_Company_HarringtonDesc";
                case TelegraphCompany.WhitmoreArms: return "HD_Company_ArmsDesc";
                case TelegraphCompany.ContinentalPetroleum: return "HD_Company_PetroleumDesc";
                default: return "HD_Company_Service_Unavailable";
            }
        }

        public static string ServiceKey(TelegraphCompany company)
        {
            switch (company)
            {
                case TelegraphCompany.NationalIndustrialCredit: return "HD_Company_Service_Financial";
                case TelegraphCompany.CentralHardwareSupply: return "HD_Company_Service_Commercial";
                case TelegraphCompany.PioneerOverseasService: return "HD_Company_Service_Pioneer";
                case TelegraphCompany.HarringtonProtective: return "HD_Company_Service_Security";
                case TelegraphCompany.WhitmoreArms: return "HD_Company_Service_Arms";
                case TelegraphCompany.ContinentalPetroleum: return "HD_Company_Service_Petroleum";
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
        public bool SafeConduct;
        public bool Pending => ArrivalTick >= 0;
        public bool CanRequest(int now) => !Pending && now >= LastRequestTick + CooldownTicks;

        internal void Schedule(int now, int travelTicks, bool safeConduct)
        {
            LastRequestTick = now;
            ArrivalTick = now + travelTicks;
            ExpiryTick = now + CooldownTicks;
            SafeConduct = safeConduct;
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
        private readonly Dictionary<TelegraphCompany, CompanyDispatchSchedule> schedules = new Dictionary<TelegraphCompany, CompanyDispatchSchedule>();

        public CompanyDispatchBook()
        {
            foreach (TelegraphCompany company in TelegraphCompanies.CaravanCompanies) schedules.Add(company, new CompanyDispatchSchedule());
        }

        public CompanyDispatchSchedule Schedule(TelegraphCompany company) => schedules[company];

        public bool CanRequest(int now, IReadOnlyList<TelegraphCompany> companies)
        {
            if (companies == null || companies.Count == 0) return false;
            var unique = new HashSet<TelegraphCompany>();
            foreach (TelegraphCompany company in companies)
                if (!unique.Add(company) || !schedules.TryGetValue(company, out CompanyDispatchSchedule schedule) || !schedule.CanRequest(now)) return false;
            return true;
        }

        public bool TrySchedule(int now, IReadOnlyList<TelegraphCompany> companies, int[] travelTicks, bool safeConduct)
        {
            if (!CanRequest(now, companies) || travelTicks == null || travelTicks.Length != companies.Count) return false;
            foreach (int travel in travelTicks)
                if (travel < CompanyDispatchSchedule.MinimumTravelTicks || travel > CompanyDispatchSchedule.MaximumTravelTicks) return false;
            for (int i = 0; i < companies.Count; i++) schedules[companies[i]].Schedule(now, travelTicks[i], safeConduct);
            return true;
        }
    }

    public static class TelegraphTradeRules
    {
        public const int MinimumFinancial = 50;
        public const int HostileMinimumFinancial = 80;
        public const int HostileCaravanCost = 20;
        public static int Cost(bool hostile, int count) => hostile ? HostileCaravanCost * count : 0;
        // Every caravan in a combined request must satisfy the 80-point rule
        // before its own fee. Combining requests cannot bypass that threshold.
        public static int RequiredFinancial(bool hostile, int count)
            => hostile ? HostileMinimumFinancial + HostileCaravanCost * System.Math.Max(0, count - 1) : MinimumFinancial;
        public static bool CanRequest(float finance, bool hostile, int count)
            => count > 0 && finance >= RequiredFinancial(hostile, count);
    }
}
