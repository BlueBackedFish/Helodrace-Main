namespace Helodrace
{
    public sealed class HamRadioSchedule
    {
        public const float DailyMissionChance = 0.15f;
        public int LastCheckedDay = -1;

        public bool CanCheck(int day) => day > LastCheckedDay;

        public bool CheckDay(int day, float roll)
        {
            if (!CanCheck(day)) return false;
            LastCheckedDay = day;
            return roll < DailyMissionChance;
        }
    }
}
