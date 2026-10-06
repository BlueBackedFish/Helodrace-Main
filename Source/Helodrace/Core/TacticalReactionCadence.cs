namespace Helodrace
{
    // Runtime-only cadence. Contact revisions and phase changes bypass a quiet
    // interval; urgent casualty callbacks invalidate it before execution.
    public sealed class TacticalReactionCadence
    {
        private int lastTick = -1000, revision, phase;
        public bool Result { get; private set; }
        public bool Due(int tick, int contactRevision, int currentPhase, bool active) =>
            tick < lastTick || contactRevision != revision || currentPhase != phase
            || tick != lastTick && (active || Result || tick - lastTick >= 30);
        public void Record(int tick, int contactRevision, int currentPhase, bool result)
        { lastTick = tick; revision = contactRevision; phase = currentPhase; Result = result; }
        public void Invalidate() { lastTick = -1000; Result = false; }
    }
}
