namespace Helodrace
{
    public enum RaidBreachProgress
    {
        Approach,
        Crossing,
        Clearing,
        Complete
    }

    public static class RaidBreachTraversal
    {
        // Crossing is latched: congestion, knockback, and a lateral path step
        // must not send an admitted pawn back to the outside staging cell.
        public static RaidBreachProgress Advance(RaidBreachProgress progress,
            bool atOutside, bool pastOpening, bool atDestination)
        {
            if (progress == RaidBreachProgress.Complete) return progress;
            if (pastOpening && atDestination) return RaidBreachProgress.Complete;
            if (pastOpening) return RaidBreachProgress.Clearing;
            if (progress == RaidBreachProgress.Approach && atOutside)
                return RaidBreachProgress.Crossing;
            return progress;
        }
    }
}
