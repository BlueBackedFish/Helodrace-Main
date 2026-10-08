using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helodrace.Squads;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalSquadLink
    {
        public RaidTacticalUnit Unit;
        public Pawn Liaison;
        public CompTacticalRadio Radio;
        public int SelectionCursor, PeerCursor, ExchangeAt, PartnerExchangeAt, PartnerProbeAt, OpportunityUntil;
        public TacticalSquadCommand Peer;
        public TacticalCooperationState Cooperation = new TacticalCooperationState();
        public HashSet<IntVec3> KnownPortals = new HashSet<IntVec3>();
        public Dictionary<string, int> Identified = new Dictionary<string, int>();
        public int IdentifyUntil, IdentifyStarted;
        public bool ResetPlan, IdentificationHolding;
        public string LastChannel = "none";
    }

    public sealed partial class TacticalMessage
    {
        public TacticalSquadCommand From, To;
        public int FromPawn, ToPawn, Sent, Due;
        public TacticalMessageKind Kind;
        public TacticalChannel Channel;
        public TacticalCooperationAgenda Agenda;
        public TacticalContact Contact;
        public bool Ready, Finished, GoalSecured, PortalUsable;
        public IntVec3 Opening = IntVec3.Invalid;
        public int StartAt = -1;
    }

    // Shared bounded service, not a per-map component or pawn-pair graph.
    public sealed partial class TacticalCommunications
    {
        private List<TacticalMessage> pending = new List<TacticalMessage>(TacticalCommunicationPolicy.QueueLimit);
        private int sourceCursor, deliveryCursor, nextPair;
        public long PairChecks, MessagesSent, MessagesDelivered, MessagesDropped, QueueRejected, ReportsReceived;
        public long OffersAccepted, AgreementsConfirmed, StartMessages, Identifications, OperatorChanges;
        public long EndpointDrops;
        public int LastInvalidSender = -1;
        public string LastDropReason;
        public int PendingCount => pending.Count;
        internal void Forget(TacticalSquadCommand command)
        {
            // The queue is globally capped at 64. Retirement happens once per
            // command, never in each pawn's tick or normal communication probe.
            MessagesDropped += pending.RemoveAll(message => message.From == command || message.To == command);
            if (command.Link.Peer?.Link.Peer == command)
            {
                command.Link.Peer.Link.Peer = null;
                command.Link.Peer.Link.Cooperation.Abort();
                command.Link.Peer.Due = Math.Min(command.Link.Peer.Due, GenTicks.TicksGame + 1);
            }
            command.Link.Peer = null; command.Link.Liaison = null; command.Link.Radio = null;
        }
        public void Pump(IList<TacticalSquadCommand> commands, TacticalWorkBudget budget, int tick)
        {
            int visited = 0, delivered = 0;
            while (pending.Count > 0 && visited++ < 4 && delivered < 2)
            {
                if (deliveryCursor >= pending.Count) deliveryCursor = 0;
                TacticalMessage message = pending[deliveryCursor];
                if (message.Due > tick) { deliveryCursor++; continue; }
                if (!budget.TryCommunication(tick)) return;
                pending.RemoveAt(deliveryCursor); delivered++;
                long started = Stopwatch.GetTimestamp();
                try { Deliver(message, tick); }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            if (commands.Count < 2 || tick < nextPair || !budget.TryCommunication(tick)) return;
            nextPair = tick + TacticalCommunicationPolicy.PairInterval;
            TacticalSquadCommand source = null;
            for (int i = 0; i < Math.Min(4, commands.Count); i++)
            {
                if (sourceCursor >= commands.Count) sourceCursor = 0;
                TacticalSquadCommand next = commands[sourceCursor++];
                if (Active(next)) { source = next; break; }
            }
            if (source == null) return;
            TacticalSquadLink link = source.Link;
            TacticalSquadCommand receiver;
            if (link.Peer != null && tick >= link.PartnerProbeAt)
            { receiver = link.Peer; link.PartnerProbeAt = tick + TacticalCommunicationPolicy.ExchangeInterval; }
            else
            {
                if (link.PeerCursor >= commands.Count) link.PeerCursor = 0;
                receiver = commands[link.PeerCursor++];
            }
            long pairStarted = Stopwatch.GetTimestamp();
            try { ProbePair(source, receiver, tick); }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - pairStarted); }
        }
        private static bool Address(TacticalSquadCommand command) => command != null && !command.Terminal && !command.Owner.map.Disposed;
        private static bool Active(TacticalSquadCommand command) => Address(command) && command.Phase != TacticalCommandPhase.Complete
            && command.Phase != TacticalCommandPhase.Returning;
        private static bool Person(TacticalSquadCommand command, Pawn pawn) => pawn?.Spawned == true && pawn.Map == command.Owner.map
            && !pawn.Dead && !pawn.Downed && !pawn.InMentalState;
        private void SelectLiaison(TacticalSquadCommand command)
        {
            TacticalSquadLink link = command.Link;
            Pawn previous = link.Liaison;
            bool high = link.Unit.Organization.doctrine?.tacticalRadio == true;
            if (Person(command, link.Liaison))
            {
                link.Radio = high ? RaidTacticalRadioUtility.Radios(link.Liaison).FirstOrDefault() : null;
                if (!high || link.Radio != null) return;
            }
            Pawn commander = link.Unit.Commander;
            if (Person(command, commander))
            {
                CompTacticalRadio radio = high ? RaidTacticalRadioUtility.Radios(commander).FirstOrDefault() : null;
                if (!high || radio != null) { link.Liaison = commander; link.Radio = radio; }
            }
            if (!Person(command, link.Liaison) || high && link.Radio == null)
            {
                for (int i = 0; i < Math.Min(2, command.Members.Count); i++)
                {
                    if (link.SelectionCursor >= command.Members.Count) link.SelectionCursor = 0;
                    Pawn candidate = command.Members[link.SelectionCursor++].Pawn;
                    if (!Person(command, candidate)) continue;
                    CompTacticalRadio radio = high ? RaidTacticalRadioUtility.Radios(candidate).FirstOrDefault() : null;
                    if (!Person(command, link.Liaison) || radio != null) { link.Liaison = candidate; link.Radio = radio; }
                    if (!high || radio != null) break;
                }
            }
            if (previous != null && previous != link.Liaison) OperatorChanges++;
        }
        private TacticalChannel Channel(TacticalSquadCommand a, TacticalSquadCommand b)
        {
            if (!Address(a) || !Address(b) || a == b || a.Owner != b.Owner
                || a.Link.Unit.Faction != b.Link.Unit.Faction) return TacticalChannel.None;
            SelectLiaison(a); SelectLiaison(b);
            if (!Person(a, a.Link.Liaison) || !Person(b, b.Link.Liaison)) return TacticalChannel.None;
            CompTacticalRadio x = a.Link.Radio, y = b.Link.Radio;
            if (x != null && y != null && TacticalCommunicationPolicy.Radio(a.Link.Unit.Organization.doctrine.tacticalRadio,
                b.Link.Unit.Organization.doctrine.tacticalRadio, x.RadioProperties.network, y.RadioProperties.network,
                x.RadioProperties.range, y.RadioProperties.range, a.Link.Liaison.Position.DistanceToSquared(b.Link.Liaison.Position)))
                return TacticalChannel.Radio;
            return Physical(a, b) ? TacticalChannel.Voice : TacticalChannel.None;
        }
        private static bool Physical(TacticalSquadCommand a, TacticalSquadCommand b)
        {
            int range = Math.Min(a.Link.Unit.Organization.doctrine?.voiceContactRange ?? 8,
                b.Link.Unit.Organization.doctrine?.voiceContactRange ?? 8);
            IntVec3 x = a.Link.Liaison.Position, y = b.Link.Liaison.Position;
            if (x.DistanceToSquared(y) > range * range || !GenSight.LineOfSight(x, y, a.Owner.map, true)) return false;
            foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(x, y))
                if (RaidSmokeUtility.CoveringSmokeAt(a.Owner.map, cell)) return false;
            return true;
        }
        private static bool Negotiate(TacticalSquadCommand command) => Active(command) && !command.Defensive && command.ContactResponse == null
            && (command.Phase == TacticalCommandPhase.Pending || command.Phase == TacticalCommandPhase.Stack
                || command.Phase == TacticalCommandPhase.Clear && command.RoomScan == null);
        private void ProbePair(TacticalSquadCommand source, TacticalSquadCommand receiver, int tick)
        {
            PairChecks++;
            TacticalChannel channel = Channel(source, receiver);
            if (channel == TacticalChannel.None) return;
            source.Link.LastChannel = receiver.Link.LastChannel = channel.ToString();
            if (source.Link.Liaison.Position.Roofed(source.Owner.map) && receiver.Link.Liaison.Position.Roofed(source.Owner.map)
                && Physical(source, receiver))
            { Identify(source, receiver, tick); Identify(receiver, source, tick); }
            TacticalCooperationState cooperation = source.Link.Cooperation;
            if (cooperation.Agenda != null && cooperation.Agenda.Peer(source.Id) == receiver.Id)
            {
                if (cooperation.Stage == TacticalAgreementStage.Offered) Send(source, receiver, TacticalMessageKind.Offer, tick, channel);
                else if (cooperation.Stage == TacticalAgreementStage.Accepted) Send(source, receiver, TacticalMessageKind.Accept, tick, channel);
                else if (cooperation.Active && source.Id == cooperation.Agenda.First && cooperation.PeerStatusAt < 0)
                    Send(source, receiver, TacticalMessageKind.Confirm, tick, channel);
            }
            else if (Negotiate(source) && Negotiate(receiver) && !cooperation.Active && !cooperation.Negotiating
                && (cooperation.Stage != TacticalAgreementStage.Aborted || tick >= cooperation.Agenda.Deadline)
                && !receiver.Link.Cooperation.Active && !receiver.Link.Cooperation.Negotiating
                && source.Goal.IsValid && source.Goal == receiver.Goal && string.CompareOrdinal(source.Id, receiver.Id) < 0)
            {
                IntVec3 delta = source.Goal - source.Link.Liaison.Position;
                IntVec3 forward = Math.Abs(delta.x) >= Math.Abs(delta.z) ? new IntVec3(Math.Sign(delta.x), 0, 0)
                    : new IntVec3(0, 0, Math.Sign(delta.z));
                if (forward == IntVec3.Zero) forward = IntVec3.East;
                cooperation.Agenda = new TacticalCooperationAgenda(source.Id + ":" + receiver.Id + ":" + tick,
                    source.Id, receiver.Id, source.Goal, forward, tick + 900, tick + 6000);
                cooperation.Stage = TacticalAgreementStage.Offered; cooperation.NegotiationStarted = tick; source.Link.Peer = receiver;
                Send(source, receiver, TacticalMessageKind.Offer, tick, channel);
            }
            bool partner = cooperation.Active && source.Link.Peer == receiver;
            if (tick < (partner ? source.Link.PartnerExchangeAt : source.Link.ExchangeAt)) return;
            if (partner) source.Link.PartnerExchangeAt = tick + TacticalCommunicationPolicy.ExchangeInterval;
            else source.Link.ExchangeAt = tick + TacticalCommunicationPolicy.ExchangeInterval;
            if (cooperation.Active && source.Link.Peer == receiver)
            {
                Send(source, receiver, TacticalMessageKind.Status, tick, channel);
                if (source.Id == cooperation.Agenda.First && cooperation.LocalReady && cooperation.PeerReady
                    && tick - cooperation.PeerStatusAt < 600 && cooperation.ConfirmedStart < 0)
                {
                    cooperation.ConfirmedStart = tick + 90;
                    Send(source, receiver, TacticalMessageKind.Start, tick, channel);
                }
            }
            int reports = 0;
            foreach (TacticalContact contact in source.Contacts.Memory.Entries)
                if (contact.Origin == source.Id && tick - contact.SeenTick < TacticalContactMemory.RetentionTicks)
                { Send(source, receiver, TacticalMessageKind.Contact, tick, channel, contact); if (++reports == 2) break; }
        }
        private void Identify(TacticalSquadCommand command, TacticalSquadCommand peer, int tick)
        {
            if (!Active(command) || command.Link.Identified.TryGetValue(peer.Id, out int until) && tick < until) return;
            if (command.Link.Identified.Count >= 8) command.Link.Identified.Remove(command.Link.Identified.Keys.First());
            command.Link.Identified[peer.Id] = tick + 600;
            command.Link.IdentifyStarted = tick; command.Link.IdentifyUntil = tick + TacticalCommunicationPolicy.IdentificationTicks;
            command.Due = Math.Min(command.Due, tick + 1); Identifications++;
        }
        private void Send(TacticalSquadCommand from, TacticalSquadCommand to, TacticalMessageKind kind, int tick,
            TacticalChannel channel, TacticalContact contact = null)
        {
            if (pending.Any(p => p.From == from && p.To == to && p.Kind == kind && p.Contact?.EnemyId == contact?.EnemyId)) return;
            if (pending.Count >= TacticalCommunicationPolicy.QueueLimit) { QueueRejected++; return; }
            var doctrine = from.Link.Unit.Organization.doctrine;
            TacticalCooperationState own = from.Link.Cooperation;
            pending.Add(new TacticalMessage { From = from, To = to, Kind = kind, Channel = channel,
                FromPawn = from.Link.Liaison.thingIDNumber, ToPawn = to.Link.Liaison.thingIDNumber,
                Sent = tick, Due = tick + (channel == TacticalChannel.Radio ? doctrine?.radioReportTicks ?? 20 : doctrine?.voiceReportTicks ?? 40),
                Agenda = own.Agenda, Contact = contact?.Copy(), Ready = own.LocalReady,
                Finished = own.Stage == TacticalAgreementStage.Finished, GoalSecured = from.GoalSecured,
                Opening = from.Plan?.Opening ?? IntVec3.Invalid,
                PortalUsable = from.Members.Any(m => m.Crossed), StartAt = own.ConfirmedStart });
            MessagesSent++;
        }
        private void Deliver(TacticalMessage message, int tick)
        {
            TacticalChannel channel = Channel(message.From, message.To);
            if (message.From.Link.Liaison?.thingIDNumber != message.FromPawn || message.To.Link.Liaison?.thingIDNumber != message.ToPawn)
            { MessagesDropped++; EndpointDrops++; LastInvalidSender = message.FromPawn; LastDropReason = "endpoint"; return; }
            if (tick - message.Sent >= TacticalCommunicationPolicy.PacketLife || channel != message.Channel)
            { MessagesDropped++; LastDropReason = channel != message.Channel ? "channel" : "expired"; return; }
            TacticalSquadCommand receiver = message.To;
            if (message.Kind == TacticalMessageKind.Contact)
            {
                if (receiver.Contacts.Memory.Receive(message.Contact, tick)) { ReportsReceived++; receiver.Due = Math.Min(receiver.Due, tick + 1); }
                MessagesDelivered++; return;
            }
            if (message.Kind == TacticalMessageKind.Offer && !Negotiate(receiver)) { MessagesDropped++; LastDropReason = "recipient-busy"; return; }
            TacticalCooperationState own = receiver.Link.Cooperation;
            bool activeBefore = own.Active;
            if (!own.Receive(message.Kind, message.Agenda, receiver.Id, receiver.Goal, tick,
                message.Ready, message.Finished, message.GoalSecured, message.Opening, message.StartAt, message.Sent))
            { MessagesDropped++; LastDropReason = "protocol"; return; }
            receiver.Link.Peer = message.From; MessagesDelivered++;
            receiver.Due = Math.Min(receiver.Due, tick + 1);
            if (!activeBefore && own.Active) { receiver.Link.ResetPlan = receiver.Phase == TacticalCommandPhase.Stack; AgreementsConfirmed++; }
            if (message.Kind == TacticalMessageKind.Offer)
            { OffersAccepted++; Send(receiver, message.From, TacticalMessageKind.Accept, tick, channel); }
            if (message.Kind == TacticalMessageKind.Accept) Send(receiver, message.From, TacticalMessageKind.Confirm, tick, channel);
            if (message.Kind == TacticalMessageKind.Start) StartMessages++;
            if (message.Kind == TacticalMessageKind.Status && message.PortalUsable && message.Opening.IsValid)
            {
                if (receiver.Link.KnownPortals.Count >= 8) receiver.Link.KnownPortals.Remove(receiver.Link.KnownPortals.First());
                receiver.Link.KnownPortals.Add(message.Opening);
            }
        }
        public void Announce(TacticalSquadCommand command, int tick, bool aborted)
        {
            TacticalCooperationState cooperation = command.Link.Cooperation;
            if (cooperation.Agenda == null || cooperation.Stage == TacticalAgreementStage.Aborted || cooperation.Stage == TacticalAgreementStage.Finished) return;
            if (aborted) cooperation.Abort(); else cooperation.Stage = TacticalAgreementStage.Finished;
            TacticalChannel channel = Channel(command, command.Link.Peer);
            if (channel != TacticalChannel.None) Send(command, command.Link.Peer,
                aborted ? TacticalMessageKind.Abort : TacticalMessageKind.Status, tick, channel);
        }
    }
}
