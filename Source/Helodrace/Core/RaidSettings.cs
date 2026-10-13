using Verse;

namespace Helodrace
{
    public sealed class RaidSettings : ModSettings
    {
        public bool limitToOnePlatoon = true;
        public static bool LimitToOnePlatoon => HelodraceBase.Instance?.RaidOptions?.limitToOnePlatoon ?? true;
        public override void ExposeData() => Scribe_Values.Look(ref limitToOnePlatoon, "limitRaidToOnePlatoon", true);
    }
}
