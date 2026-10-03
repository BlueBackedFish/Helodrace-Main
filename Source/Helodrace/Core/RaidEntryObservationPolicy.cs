namespace Helodrace
{
    public enum RaidEntrySupportKind { None, Smoke, Grenade }

    public static class RaidEntryObservationPolicy
    {
        public const int ObservationTicks = 90;
        public const int SmallRoomCells = 16;

        public static RaidEntrySupportKind Support(bool outdoors, int roomCells, bool enemyContact = false) => outdoors
            ? RaidEntrySupportKind.Smoke : enemyContact || roomCells > SmallRoomCells
                ? RaidEntrySupportKind.Grenade : RaidEntrySupportKind.None;
    }
}
