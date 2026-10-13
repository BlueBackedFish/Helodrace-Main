using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class MapComponent_TacticalCommands
    {
        public int PlayerRadioJammedUntil;
        public bool PlayerRadioJammed => GenTicks.TicksGame < PlayerRadioJammedUntil;
    }
}
