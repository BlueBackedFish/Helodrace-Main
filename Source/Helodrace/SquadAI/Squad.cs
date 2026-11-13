using System.Collections.Generic;
using Helodrace.Squads;
using Verse;
using Verse.AI.Group;

namespace Helodrace.SquadAI
{
    // Vanilla: the lord's own duties run the squad. Brace: hold cover facing an
    // approaching enemy. Engage: base of fire plus an optional flank team.
    // Advance: radio squads bound toward an out-of-range enemy, one team moving.
    public enum SquadStage { Vanilla, Brace, Engage, Advance }
    public enum TeamTask { Hold, Move }

    public sealed class SquadTeam
    {
        public readonly List<Pawn> Members = new List<Pawn>();
        public readonly Dictionary<Pawn, IntVec3> Posts = new Dictionary<Pawn, IntVec3>();
        public CombatGroup Group;
        public TeamTask Task;
        public bool Flank;
        public int TaskStarted;
        public IntVec3 Zone = IntVec3.Invalid;
    }

    // Plain runtime state. Nothing here is saved: after a load every squad
    // starts in Vanilla and replans from what its members can see.
    public sealed class Squad
    {
        public readonly RaidTacticalUnit Unit;
        public readonly bool Radio;
        public readonly List<Pawn> Live = new List<Pawn>();
        public readonly List<SquadTeam> Teams = new List<SquadTeam>();
        public readonly SquadContacts Contacts = new SquadContacts();
        public Lord Lord;
        public SquadStage Stage;
        public int StageStarted, NextThink, NextPlan;
        public IntVec3 Focus = IntVec3.Invalid;
        public float Range = 25f;
        public int MemberSignature;

        public Squad(RaidTacticalUnit unit, int tick)
        {
            Unit = unit;
            Radio = unit.Organization.doctrine?.tacticalRadio == true;
            NextThink = tick + (int)((uint)unit.Id.GetHashCode() % SquadPolicy.ThinkTicks);
        }
        public string Id => Unit.Id;

        public IntVec3 Centroid(IEnumerable<Pawn> pawns)
        {
            long x = 0, z = 0; int n = 0;
            foreach (Pawn pawn in pawns) { x += pawn.Position.x; z += pawn.Position.z; n++; }
            return n == 0 ? IntVec3.Invalid : new IntVec3((int)(x / n), 0, (int)(z / n));
        }
        public IntVec3 Centroid() => Centroid(Live);

        public SquadTeam TeamOf(Pawn pawn)
        {
            foreach (SquadTeam team in Teams) if (team.Members.Contains(pawn)) return team;
            return null;
        }
    }

    // Every tuning value of the squad AI in one place.
    public static class SquadPolicy
    {
        public const int ThinkTicks = 30, DiscoverTicks = 600, ReplanTicks = 900;
        public const int ContactLifeTicks = 300, HarmMemoryTicks = 240, ApproachSampleTicks = 240;
        public const int ObserversPerThink = 3, LineChecksPerThink = 12, ObservationRadius = 70, CloseThreatRadius = 12;
        public const float EngageRangeFactor = 1.05f, EngageHoldFactor = 1.3f, BraceRangeFactor = 2f;
        public const float BaseStandoffFactor = .75f, AdvanceFactor = .85f;
        public const int PostRadius = 6, PostSpacing = 3, TeamLateralSpacing = 7, FocusShift = 10;
        public const float LineOverlapLateral = 1.6f;
        public const int FlankMinimumLive = 6, FlankTimeoutTicks = 1500, MoveTimeoutTicks = 1500;
        public const int BoundMinimum = 8, BoundMaximum = 22;
        public const float HoldRadius = 2.9f, MoveArrivalRadius = 2f, ArrivedShare = .75f;
        public const float DefaultRange = 25f;
        public static int FlankSize(int live) => live < FlankMinimumLive ? 0 : System.Math.Min(4, System.Math.Max(3, live / 3));
    }
}
