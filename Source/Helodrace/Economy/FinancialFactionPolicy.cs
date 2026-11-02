namespace Helodrace.Economy
{
    // Both Helod civil factions use Sthaler and may earn financial reputation.
    // Company services and lending remain restricted to their LOW provider.
    public static class FinancialFactionPolicy
    {
        public static bool Includes(string defName)
            => defName == "HD_HelodCivilLowFaction" || defName == "HD_HelodCivilHighFaction";
    }
}
