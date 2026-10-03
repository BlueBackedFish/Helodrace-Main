namespace Helodrace
{
    public static class RaidOrderPolicy
    {
        public static bool Refresh(bool pending, bool currentOwned,
            bool equipmentOrEmergency, bool busy) => !equipmentOrEmergency && !busy
                && (pending || !currentOwned);

        public static bool ContinueMove(bool sameDestination, bool sameUrgency) =>
            sameDestination && sameUrgency;
    }
}
