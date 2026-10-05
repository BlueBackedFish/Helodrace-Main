using Verse;

namespace Helodrace
{
    public enum RaidCommandOwner { Approach, Security, Breach, Assault, Guard, Reaction, OpeningQueue }

    // The task's intent survives a temporary guard/ingress hold. Destination on
    // RaidPawnOrder is only the currently executable leg, never the task itself.
    public sealed class RaidPawnCommand : IExposable
    {
        public RaidCommandOwner Owner;
        public RaidOrderKind Kind;
        public IntVec3 Destination = IntVec3.Invalid;
        public bool Sprint, FightOnArrival, Reactive;
        public float Radius = 10f;
        public int Revision;
        public RaidMovementNode Connection;
        public bool IndependentJoin;
        internal int JoinSearchAfter;

        internal bool Assign(RaidCommandOwner owner, RaidOrderKind kind, IntVec3 destination,
            bool sprint, bool fightOnArrival, float radius, bool reactive, RaidMovementNode connection, bool independentJoin = false)
        {
            bool changed = Owner != owner || Kind != kind || Destination != destination || Sprint != sprint
                || FightOnArrival != fightOnArrival || Radius != radius || Reactive != reactive || Connection != connection
                || IndependentJoin != independentJoin;
            if (!changed) return false;
            Owner = owner; Kind = kind; Destination = destination; Sprint = sprint;
            FightOnArrival = fightOnArrival; Radius = radius; Reactive = reactive; Connection = connection;
            IndependentJoin = independentJoin; JoinSearchAfter = 0;
            Revision++;
            return true;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Owner, "owner"); Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Destination, "destination", IntVec3.Invalid);
            Scribe_Values.Look(ref Sprint, "sprint"); Scribe_Values.Look(ref FightOnArrival, "fightOnArrival");
            Scribe_Values.Look(ref Reactive, "reactive"); Scribe_Values.Look(ref Radius, "radius", 10f);
            Scribe_Values.Look(ref Revision, "revision");
            Scribe_Deep.Look(ref Connection, "connection");
            Scribe_Values.Look(ref IndependentJoin, "independentJoin");
        }
    }
}
