namespace Helodrace
{
    public enum TelegraphConnectionStatus
    {
        NotInstalled,
        Installing,
        ProviderUnavailable,
        RelayClosed,
        NoPrimaryCells,
        Online
    }

    public static class TelegraphNetworkRules
    {
        public const string ProviderDefName = "HD_HelodCivilLowFaction";
        public const int InstallationCost = 500;
        public const int OpeningHour = 8;
        public const int ClosingHour = 18;
        public const int MinimumInstallationTicks = 120000;
        public const int MaximumInstallationTicks = 180000;

        public static bool IsProvider(string defName) => defName == ProviderDefName;

        public static TelegraphConnectionStatus Status(int readyTick, int now, int localHour,
            bool providerAvailable, bool hasPrimaryCell)
        {
            if (readyTick < 0) return TelegraphConnectionStatus.NotInstalled;
            if (now < readyTick) return TelegraphConnectionStatus.Installing;
            if (!providerAvailable) return TelegraphConnectionStatus.ProviderUnavailable;
            if (localHour < OpeningHour || localHour >= ClosingHour) return TelegraphConnectionStatus.RelayClosed;
            if (!hasPrimaryCell) return TelegraphConnectionStatus.NoPrimaryCells;
            return TelegraphConnectionStatus.Online;
        }
    }
}
