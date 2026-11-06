using Verse;

namespace Helodrace
{
    public sealed class RaidSettings : ModSettings
    {
        public bool limitToOnePlatoon = true;
        // The squad tactical engine is unstable; vanilla raid AI is the default.
        public bool tacticalAI;
        public static bool LimitToOnePlatoon => HelodraceBase.Instance?.RaidOptions?.limitToOnePlatoon ?? true;
        public static bool TacticalAI => HelodraceBase.Instance?.RaidOptions?.tacticalAI ?? false;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref limitToOnePlatoon, "limitRaidToOnePlatoon", true);
            Scribe_Values.Look(ref tacticalAI, "tacticalAI", false);
        }
    }
}
