using System;
using Verse;

namespace Helodrace.Tactics
{
    public enum TacticalMessageKind { Offer, Accept, Confirm, Status, Start, Abort, Contact }
    public enum TacticalAgreementStage { None, Offered, Accepted, Agreed, Started, Finished, Aborted }
    public enum TacticalChannel { None, Voice, Radio }

    // Immutable, pre-agreed values; neither party's changing state is shared.
    public sealed partial class TacticalCooperationAgenda
    {
        public string Id, First, Second;
        public IntVec3 Goal, Forward;
        public int StartAt, Deadline;
        public TacticalCooperationAgenda(string id, string first, string second, IntVec3 goal, IntVec3 forward,
            int startAt, int deadline)
        { Id = id; First = first; Second = second; Goal = goal; Forward = forward; StartAt = startAt; Deadline = deadline; }
        public string Peer(string own) => own == First ? Second : First;
        public int Side(string own) => own == First ? -1 : 1;
        public IntVec3 Area(string own) => Goal + new IntVec3(-Forward.z, 0, Forward.x) * (Side(own) * 12);
        // The shared centre line remains both parties' responsibility. This is
        // agreed geometry, not knowledge of the peer's current room progress.
        public bool OwnsSide(string own, IntVec3 cell) => Side(own)
            * ((cell.x - Goal.x) * -Forward.z + (cell.z - Goal.z) * Forward.x) >= 0;
    }

    public sealed partial class TacticalCooperationState
    {
        public TacticalCooperationAgenda Agenda;
        public TacticalAgreementStage Stage;
        public bool LocalReady, PeerReady, PeerFinished, PeerGoalSecured;
        public int PeerStatusAt = -1, ConfirmedStart = -1, NegotiationStarted;
        public IntVec3 PeerOpening = IntVec3.Invalid;
        public bool Active => Stage == TacticalAgreementStage.Agreed || Stage == TacticalAgreementStage.Started;
        public bool Negotiating => Stage == TacticalAgreementStage.Offered || Stage == TacticalAgreementStage.Accepted;
        public bool CanStart(int tick, bool ready) => ready && Active
            && tick >= (ConfirmedStart >= 0 ? ConfirmedStart : Agenda.StartAt);
        public bool Expired(int tick) => Agenda != null && (tick >= Agenda.Deadline
            || Negotiating && tick - NegotiationStarted >= 600);
        public void Abort() { Stage = TacticalAgreementStage.Aborted; PeerReady = false; ConfirmedStart = -1; }
        public bool Receive(TacticalMessageKind kind, TacticalCooperationAgenda agenda, string own, IntVec3 goal,
            int tick, bool ready = false, bool finished = false, bool goalSecured = false,
            IntVec3 opening = default, int start = -1, int sampleTick = -1)
        {
            if (agenda == null || (own != agenda.First && own != agenda.Second) || goal != agenda.Goal || tick >= agenda.Deadline) return false;
            if (kind == TacticalMessageKind.Offer)
            {
                if (own != agenda.Second || Agenda != null && Agenda.Id != agenda.Id && (Active || Negotiating)) return false;
                if (Stage == TacticalAgreementStage.Aborted && Agenda?.Id == agenda.Id) return false;
                if (Agenda?.Id == agenda.Id && (Stage == TacticalAgreementStage.Accepted || Active || Stage == TacticalAgreementStage.Finished)) return true;
                Agenda = agenda; Stage = TacticalAgreementStage.Accepted; NegotiationStarted = tick; return true;
            }
            if (Agenda?.Id != agenda.Id || Stage == TacticalAgreementStage.Aborted) return false;
            if (kind == TacticalMessageKind.Accept && own == agenda.First && Stage == TacticalAgreementStage.Offered)
            { Stage = TacticalAgreementStage.Agreed; return true; }
            if (kind == TacticalMessageKind.Accept && own == agenda.First && (Active || Stage == TacticalAgreementStage.Finished)) return true;
            if (kind == TacticalMessageKind.Confirm && own == agenda.Second && Stage == TacticalAgreementStage.Accepted)
            { Stage = TacticalAgreementStage.Agreed; return true; }
            if (kind == TacticalMessageKind.Confirm && own == agenda.Second && (Active || Stage == TacticalAgreementStage.Finished)) return true;
            if (kind == TacticalMessageKind.Abort) { Abort(); return true; }
            if (!Active && Stage != TacticalAgreementStage.Finished) return false;
            if (kind == TacticalMessageKind.Status)
            {
                int sampled = sampleTick < 0 ? tick : sampleTick;
                if (sampled > tick || sampled < PeerStatusAt) return false;
                PeerReady = ready; PeerFinished = finished; PeerGoalSecured = goalSecured;
                PeerOpening = opening; PeerStatusAt = sampled; return true;
            }
            if (kind == TacticalMessageKind.Start && LocalReady && PeerReady && tick - PeerStatusAt < 600 && start >= tick)
            { ConfirmedStart = start; return true; }
            return false;
        }
    }

    public static class TacticalCommunicationPolicy
    {
        public const int QueueLimit = 64, PacketLife = 600, PairInterval = 8, ExchangeInterval = 120, IdentificationTicks = 45;
        public static bool Radio(bool allowedA, bool allowedB, string a, string b, int rangeA, int rangeB, int distanceSquared) =>
            allowedA && allowedB && !string.IsNullOrEmpty(a) && a == b && rangeA > 0 && rangeB > 0
            && distanceSquared <= Math.Min(rangeA, rangeB) * Math.Min(rangeA, rangeB);
    }
}
