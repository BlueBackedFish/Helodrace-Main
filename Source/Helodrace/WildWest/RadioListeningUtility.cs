using Verse;

namespace Helodrace
{
    public static class RadioListeningUtility
    {
        public static bool InRange(Thing radio, IntVec3 cell)
        {
            float radius = radio.def.specialDisplayRadius;
            return (cell - radio.Position).LengthHorizontalSquared <= radius * radius;
        }

        public static bool CanListenAt(Thing radio, IntVec3 cell)
            => InRange(radio, cell) && cell.InBounds(radio.Map) && cell.Standable(radio.Map)
                && cell.GetRoom(radio.Map) == radio.GetRoom();
    }
}
