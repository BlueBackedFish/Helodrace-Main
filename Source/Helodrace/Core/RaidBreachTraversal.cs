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
        public static bool OpeningAvailable(bool atOpening, bool entered,
            bool occupied, bool reservedByOther) => atOpening || entered || !occupied && !reservedByOther;

        public static bool IsClearance(int inwardDepth, bool atInsideMouth, bool singleCellRoom = false) =>
            inwardDepth >= 1 && (!atInsideMouth || singleCellRoom);

        public static int AdmissionLimit(int members, int clearanceCells) =>
            System.Math.Min(members, System.Math.Max(0, clearanceCells));

        public static bool PlacementDepthAllowed(int depth, bool hasCover) =>
            depth >= 1 && depth <= (hasCover ? 6 : 3);

        public static float PlacementScore(int depth, int backWallDistance,
            int lateral, int preferredLateral, float cover) =>
            depth * 5f + backWallDistance * 4f + System.Math.Abs(lateral - preferredLateral)
                + (lateral == 0 ? 20f : 0f) - (cover >= 0.1f ? 100f + cover * 20f : 0f);

        public static bool CanUsePortal(bool selectedOpening, bool wallLine, bool exteriorAccess) =>
            selectedOpening || !wallLine && !exteriorAccess;

        public static bool AllowsCommittedIngressStep(bool selectedOpening, bool entered,
            int currentRoom, int nextRoom, int insideRoom, bool wallLine, bool exteriorAccess) =>
            selectedOpening || (nextRoom == 0 || nextRoom == insideRoom)
                && (entered || nextRoom != insideRoom || currentRoom == insideRoom)
                && CanUsePortal(false, wallLine, exteriorAccess);

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
