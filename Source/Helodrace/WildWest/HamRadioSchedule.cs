namespace Helodrace
{
    public sealed class HamRadioSchedule
    {
        public const float DailyMissionChance = 0.15f;
        public int PreparedDay = -1;
        public bool HasRumor;
        public bool RumorConsumed;

        public void PrepareDay(int day, int worldSeed, int mapId)
        {
            if (day <= PreparedDay) return;
            PreparedDay = day;
            HasRumor = IsRumorDay(day, worldSeed, mapId);
            RumorConsumed = false;
        }

        public bool TryConsumeRumor(int day)
        {
            if (day != PreparedDay || !HasRumor || RumorConsumed) return false;
            RumorConsumed = true;
            return true;
        }

        // A day's outcome is independent of conversations and the global RNG.
        // Reloading even an earlier save cannot change which days have rumors.
        public static bool IsRumorDay(int day, int worldSeed, int mapId)
        {
            unchecked
            {
                uint hash = Mix((uint)worldSeed ^ ((uint)mapId + 0x9E3779B9u));
                hash = Mix(hash ^ ((uint)day + 0x85EBCA6Bu));
                return hash / 4294967296d < DailyMissionChance;
            }
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }
    }
}
