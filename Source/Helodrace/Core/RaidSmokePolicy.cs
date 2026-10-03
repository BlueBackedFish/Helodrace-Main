using System;

namespace Helodrace
{
    public static class RaidSmokePolicy
    {
        public const int GrenadesPerCarrier = 3;
        public static int CarrierCount(int personnel) => Math.Max(0, (personnel + 2) / 3);
        public static bool EntrySmoke(bool coordinatedEntry, bool outsideRoom) => coordinatedEntry && outsideRoom;
        public static bool NeedsScreen(bool observed, float range, float distance, bool obscured) =>
            observed && range >= 16f && distance <= range && !obscured;
        public static bool ScreenComplete(bool launched, bool liveProjectile, bool preparing,
            bool smokePresent, bool settled, bool timedOut) =>
            !preparing && !liveProjectile && (launched && (smokePresent || settled) || timedOut);
    }
}
