using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalCqbDoctrine
    {
        // Entry type dominates only the already bounded local candidate set.
        public static int EntryPenalty(bool high, bool gap, bool openDoor, bool door, bool cleared)
        {
            if (cleared) return -40000;
            if (high) return gap ? 0 : openDoor ? 10000 : door ? 20000 : 30000;
            return gap || openDoor ? 1200 : door ? 600 : 0;
        }
        public static bool Support(bool high, bool outdoors, int cells, bool contact, bool prisoner, bool combat)
        {
            if (outdoors) return true;
            if (prisoner) return false;
            // Preserve the user's explicit small-room conservation rule.
            return contact || high && combat || cells > 16;
        }
        public static bool Lethal(bool high, bool observedThreat, bool combat, bool prisoner) =>
            !prisoner && (!high || observedThreat || combat);
        public static int GrenadePreference(bool high, bool lethal, string name)
        {
            if (name == "HD_Grenade_M84_Item") return high && !lethal ? 3 : 1;
            if (name == "HD_Grenade_MKII" || name == "HD_Grenade_MKIII"
                || name == "HD_Grenade_M67_Item" || name == "HD_Grenade_M111_Item") return lethal ? 2 : -1;
            if (name == "HD_Grenade_M7A2_Item") return high ? -1 : 0;
            return -1;
        }
    }
}
