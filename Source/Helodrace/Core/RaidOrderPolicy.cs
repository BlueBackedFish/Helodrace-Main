namespace Helodrace
{
    public static class RaidOrderPolicy
    {
        public static bool Refresh(bool pending, bool currentOwned,
            bool equipmentOrEmergency, bool busy) => !equipmentOrEmergency && !busy
                && (pending || !currentOwned);

        public static bool ContinueMove(bool sameDestination, bool sameUrgency) =>
            sameDestination && sameUrgency;

        public static bool ReadyToEnter(bool ready, bool projectilePending, bool delayElapsed) =>
            ready && !projectilePending && delayElapsed;

        public static bool AutoAttack(bool controlled, bool pending, bool hold) =>
            !controlled || !pending || hold;
    }
}
