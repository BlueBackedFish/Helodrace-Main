namespace Helodrace.Tactics
{
    public static class TacticalCadence
    {
        public const int DecisionTicks = 10, MovementBatch = 2;
        public static int DecisionInterval(TacticalCommandPhase phase) => phase == TacticalCommandPhase.Complete ? 600 : DecisionTicks;
    }
}
