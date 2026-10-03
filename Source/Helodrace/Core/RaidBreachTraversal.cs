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
        public static bool CanAdmit(bool hasClearance, bool mouthBusy) => hasClearance && !mouthBusy;

        public static bool IsClearance(int inwardDepth, bool atInsideMouth, bool singleCellRoom = false) =>
            inwardDepth >= 1 && (!atInsideMouth || singleCellRoom);

        public static int AdmissionLimit(int members, int clearanceCells) =>
            System.Math.Min(members, System.Math.Max(0, clearanceCells));

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
