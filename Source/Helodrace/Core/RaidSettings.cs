using Verse;

namespace Helodrace
{
    public sealed class RaidSettings : ModSettings
    {
        public bool limitToOnePlatoon = true;
        // The squad tactical engine is unstable; vanilla raid AI is the default.
        public bool tacticalAI;
        public bool bodyVariants;
        public bool unifiedBallistics = true;
        public bool ballisticImpactDebug;
        public bool ballisticSpallDebug;
        public float ballisticDebugSeconds = 6f;
        public static bool BodyVariants => HelodraceBase.Instance?.RaidOptions?.bodyVariants ?? false;
        public static bool LimitToOnePlatoon => HelodraceBase.Instance?.RaidOptions?.limitToOnePlatoon ?? true;
        /// <summary>Lobos Arsenal vehicles depend on the unified ballistic pipeline, so it cannot be turned off there.</summary>
        public const string LobosArsenalPackageId = "Arsenal.Helod.LobosArsenal";
        public static bool UnifiedBallisticsLocked => ModsConfig.IsActive(LobosArsenalPackageId);
        public static bool UnifiedBallistics => UnifiedBallisticsLocked
            || (HelodraceBase.Instance?.RaidOptions?.unifiedBallistics ?? true);
        public static bool TacticalAI => HelodraceBase.Instance?.RaidOptions?.tacticalAI ?? false;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref limitToOnePlatoon, "limitRaidToOnePlatoon", true);
            Scribe_Values.Look(ref tacticalAI, "tacticalAI", false);
            Scribe_Values.Look(ref bodyVariants, "bodyVariants", false);
            Scribe_Values.Look(ref unifiedBallistics, "unifiedBallistics", true);
            Scribe_Values.Look(ref ballisticImpactDebug, "ballisticImpactDebug", false);
            Scribe_Values.Look(ref ballisticSpallDebug, "ballisticSpallDebug", false);
            Scribe_Values.Look(ref ballisticDebugSeconds, "ballisticDebugSeconds", 6f);
        }
    }
}
