using System;

namespace Helodrace
{
    public static class RaidReactivePolicy
    {
        public static bool DefenseActive(int tick, ref int until, bool supportPending, bool enemyEngaging)
        {
            if (supportPending || enemyEngaging) until = Math.Max(until, tick + 300);
            return until > tick;
        }

        public static bool Outranged(bool observed, bool aimingAtTeam, bool obscured,
            float enemyRange, float ownRange, float distance) => observed && aimingAtTeam && !obscured
                && enemyRange >= distance && distance > ownRange && enemyRange > ownRange + 3f;
    }
}
