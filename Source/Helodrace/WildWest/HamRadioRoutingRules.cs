namespace Helodrace
{
    public static class HamRadioRoutingRules
    {
        public static bool IsHamConsole(string defName, bool hamClass)
            => hamClass || defName == "CommsConsole";

        public static bool BlocksVanillaFaction(string defName)
            => defName == "HD_HelodCivilLowFaction";
    }
}
