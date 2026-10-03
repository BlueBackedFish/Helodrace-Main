using System.Collections.Generic;

namespace Helodrace
{
    public enum RaidCqbIntent { None, ClearCurrentRoom, EnterRoom, RemoveInteriorObstacle }

    internal static class RaidCqbPolicy
    {
        internal static RaidCqbIntent Intent(int occupied, int target, bool locallyReachable) => occupied <= 0 || target <= 0
            ? RaidCqbIntent.None : occupied != target ? RaidCqbIntent.EnterRoom
            : locallyReachable ? RaidCqbIntent.ClearCurrentRoom : RaidCqbIntent.RemoveInteriorObstacle;

        internal static bool BreachDestination(int occupied, int destination, bool sourceReachable,
            bool destinationReachable, ISet<int> cleared)
        {
            if (!sourceReachable || destination <= 0) return false;
            if (destination == occupied) return !destinationReachable;
            return !cleared.Contains(destination) && !destinationReachable;
        }
    }
}
