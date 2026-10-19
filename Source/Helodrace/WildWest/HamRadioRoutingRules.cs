namespace Helodrace
{
    public static class HamRadioRoutingRules
    {
        public static bool IsHamFaction(string defName)
            => defName == "HD_HelodCivilLowFaction" || defName == "HD_HelodCivilHighFaction";
    }
}
