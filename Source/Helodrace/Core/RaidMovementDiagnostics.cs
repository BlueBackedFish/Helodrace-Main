using Verse;

namespace Helodrace
{
    public enum RaidMoveController { Formation, ContactGuard, Reaction, ExteriorIngress }
    public enum RaidMoveBlockReason { None, ContactGuard, GridPreparing, DestinationReserved, InvalidDestination,
        Unreachable, RetryDelay, Busy, ProtectedJob, UnknownJoin, OpeningWait, OpeningQueue }

    // Runtime observations, not planning state. Tick arguments also allow the
    // request/start/wait lifecycle to be checked without a running game.
    public sealed class RaidMovementDiagnostics
    {
        public RaidOrderKind RequestedKind;
        public IntVec3 RequestedDestination = IntVec3.Invalid;
        public RaidMoveController Controller;
        public RaidMoveBlockReason BlockReason;
        public int RequestedTick = -1, StartedTick = -1, BlockedSince = -1;
        public int LastStartLatency = -1;

        public void Request(RaidOrderKind kind, IntVec3 cell, RaidMoveController controller, int tick, bool changed)
        {
            if (changed || kind != RequestedKind || cell != RequestedDestination) { RequestedTick = tick; StartedTick = -1; }
            RequestedKind = kind; RequestedDestination = cell; Controller = controller;
        }
        public void Block(RaidMoveBlockReason reason, int tick)
        {
            if (reason != BlockReason) BlockedSince = reason == RaidMoveBlockReason.None ? -1 : tick;
            BlockReason = reason;
        }
        public void Started(int tick)
        {
            if (RequestedTick >= 0 && StartedTick < 0)
            {
                StartedTick = tick;
                LastStartLatency = System.Math.Max(0, tick - RequestedTick);
            }
            Block(RaidMoveBlockReason.None, tick);
        }
    }
}
