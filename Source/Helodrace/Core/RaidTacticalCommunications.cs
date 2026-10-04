using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Helodrace.Squads;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed class RaidReportTransmission : IExposable
    {
        public string FromUnit, ToUnit;
        public int FromPawn, ToPawn, DueTick, StartedTick, CommandDelay;
        public bool AwaitingAck;
        public RaidCommunicationMode Mode;
        public RaidTacticalReport Report;
        public void ExposeData()
        {
            Scribe_Values.Look(ref FromUnit, "fromUnit"); Scribe_Values.Look(ref ToUnit, "toUnit");
            Scribe_Values.Look(ref FromPawn, "fromPawn"); Scribe_Values.Look(ref ToPawn, "toPawn");
            Scribe_Values.Look(ref DueTick, "due"); Scribe_Values.Look(ref StartedTick, "started");
            Scribe_Values.Look(ref CommandDelay, "commandDelay");
            Scribe_Values.Look(ref AwaitingAck, "awaitingAck"); Scribe_Values.Look(ref Mode, "mode");
            Scribe_Deep.Look(ref Report, "report");
        }
    }

    internal sealed class RaidCommunicationFrame
    {
        public RaidTacticalUnit Unit;
        public MapComponent_RaidTacticalExecution.ExecutionState State;
        public Pawn Commander;
        public Dictionary<int, Pawn> Members;
        public Dictionary<int, int> CommandDelays;
        public Dictionary<int, List<CompTacticalRadio>> Radios;
        public DoctrineDef Doctrine => Unit.Organization.doctrine;
        private readonly Dictionary<long, int> edges = new Dictionary<long, int>();

        public bool RadioTo(RaidCommunicationFrame other, Pawn a, Pawn b, bool blackout) =>
            Radios.TryGetValue(a.thingIDNumber, out List<CompTacticalRadio> radiosA)
            && other.Radios.TryGetValue(b.thingIDNumber, out List<CompTacticalRadio> radiosB)
            && radiosA.Any(first => radiosB.Any(second => RaidCommunicationPolicy.RadioCompatible(
                Doctrine?.tacticalRadio == true, other.Doctrine?.tacticalRadio == true,
                first.Operational, second.Operational, blackout, first.RadioProperties.network, second.RadioProperties.network,
                a.Position.DistanceToSquared(b.Position), Math.Min(first.RadioProperties.range, second.RadioProperties.range))));

        public static bool VoiceTo(Pawn a, Pawn b, int range) => a.Position.DistanceToSquared(b.Position) <= range * range
            && GenSight.LineOfSight(a.Position, b.Position, a.Map, true)
            && !GenSight.PointsOnLineOfSight(a.Position, b.Position).Any(cell => RaidSmokeUtility.CoveringSmokeAt(a.Map, cell));

        public int Edge(int from, int to, bool blackout)
        {
            long key = ((long)Math.Min(from, to) << 32) | (uint)Math.Max(from, to);
            if (edges.TryGetValue(key, out int result)) return result;
            Pawn a = Members[from], b = Members[to];
            result = RadioTo(this, a, b, blackout) ? Doctrine?.radioReportTicks ?? 20
                : VoiceTo(a, b, Doctrine?.voiceContactRange ?? 8) ? Doctrine?.voiceReportTicks ?? 40 : -1;
            edges[key] = result;
            return result;
        }
    }

    public sealed class MapComponent_RaidTacticalCommunications : MapComponent
    {
        private List<RaidReportTransmission> pending = new List<RaidReportTransmission>();
        private Dictionary<string, RaidCommunicationFrame> frames = new Dictionary<string, RaidCommunicationFrame>();
        private int frameTick = -1, pairCursor, deliveryCursor, queued;
        private bool blackout;
        public MapComponent_RaidTacticalCommunications(Map map) : base(map) { }

        internal RaidCommunicationFrame Frame(string unitId, int tick)
        {
            RefreshFrames(tick);
            return unitId != null && frames.TryGetValue(unitId, out RaidCommunicationFrame frame) ? frame : null;
        }

        private void RefreshFrames(int tick)
        {
            if (frameTick >= 0 && tick - frameTick < RaidCommunicationPolicy.TickInterval) return;
            frameTick = tick; blackout = SCR300RadioUtility.IsBlackout(map);
            frames.Clear();
            var execution = map.GetComponent<MapComponent_RaidTacticalExecution>();
            if (execution == null) return;
            foreach (RaidTacticalUnit unit in RaidTacticalUnit.All)
            {
                var state = execution.StateFor(unit.Id);
                if (state?.ActivePlan?.Success != true) continue;
                List<Pawn> members = unit.Members.Where(pawn => pawn?.Spawned == true && pawn.Map == map
                    && !pawn.Dead && !pawn.Downed && !pawn.InMentalState && execution.ControlsPawn(pawn)
                    && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)).ToList();
                if (members.Count == 0) continue;
                var frame = new RaidCommunicationFrame { Unit = unit, State = state,
                    Commander = members.Contains(unit.Commander) ? unit.Commander : null,
                    Members = members.ToDictionary(pawn => pawn.thingIDNumber),
                    Radios = members.ToDictionary(pawn => pawn.thingIDNumber, pawn => RaidTacticalRadioUtility.Radios(pawn).ToList()) };
                frame.CommandDelays = frame.Commander != null
                    ? RaidCommunicationPolicy.Delays(frame.Members.Keys.ToList(), frame.Commander.thingIDNumber,
                        (a, b) => frame.Edge(a, b, blackout)) : new Dictionary<int, int>();
                frames[unit.Id] = frame;
                state.Communication.Status = $"Command={(frame.Commander?.LabelShort ?? "none")}; connected {frame.CommandDelays.Count}/{members.Count}; "
                    + $"radio operators={frame.Radios.Count(value => value.Value.Count > 0)}; blackout={blackout}";
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pending, "raidReportTransmissions", LookMode.Deep);
            Scribe_Values.Look(ref pairCursor, "raidCommunicationPairCursor");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pending = pending ?? new List<RaidReportTransmission>();
                frameTick = -1; frames.Clear();
            }
        }

        public override void MapComponentTick()
        {
            int tick = GenTicks.TicksGame;
            if (tick % RaidCommunicationPolicy.TickInterval != 0) return;
            // Observation can populate a frame between communication ticks. Never let
            // that cache defer operator/equipment revalidation at a delivery deadline.
            frameTick = -1;
            ProcessTick(tick);
        }

        private void ProcessTick(int tick)
        {
            RefreshFrames(tick); queued = 0;
            // Every resumed transmission revalidates the current people, equipment and contact.
            for (int attempts = 0, count = Math.Min(16, pending.Count); attempts < count && pending.Count > 0; attempts++)
            {
                deliveryCursor %= pending.Count;
                var packet = pending[deliveryCursor];
                bool connected = Validate(packet, out RaidCommunicationFrame source, out RaidCommunicationFrame receiver);
                RaidTransmissionAction action = RaidCommunicationPolicy.Transmission(connected, packet.Report?.ObservedTick ?? -1,
                    packet.StartedTick, packet.DueTick, packet.AwaitingAck, tick);
                if (action == RaidTransmissionAction.Interrupt)
                {
                    if (packet.Report != null) source?.State.Communication.Receipt(packet.ToUnit, packet.Report, "Interrupted", tick);
                    pending.RemoveAt(deliveryCursor); continue;
                }
                if (action == RaidTransmissionAction.Wait) { deliveryCursor++; continue; }
                if (action == RaidTransmissionAction.Acknowledge)
                {
                    source.State.Communication.Receipt(packet.ToUnit, packet.Report, "Acknowledged", tick);
                    pending.RemoveAt(deliveryCursor); continue;
                }
                bool own = packet.FromUnit == packet.ToUnit;
                RaidReportLedger inbox = receiver.Commander?.thingIDNumber == packet.ToPawn
                    ? receiver.State.Communication.Knowledge : receiver.State.Communication.For(packet.ToPawn).Reports;
                if (inbox.Receive(packet.Report, receiver.Unit.Id, tick, own))
                {
                    RaidTacticalReport received = inbox.Reports.First(value => value.Id == packet.Report.Id);
                    if (receiver.Commander?.thingIDNumber == packet.ToPawn)
                        map.GetComponent<MapComponent_RaidTacticalExecution>().AcceptReport(receiver.State, received, tick);
                    receiver.State.Communication.Receipt(packet.FromUnit, received, "Received via " + packet.Mode, tick);
                }
                // Receipt is permanent even if the later acknowledgement cannot get back.
                packet.AwaitingAck = true;
                packet.DueTick = tick + Math.Max(source.Doctrine?.communicationAckTicks ?? 20, own
                    ? source.CommandDelays[packet.FromPawn] : 0);
                deliveryCursor++;
            }
            List<RaidCommunicationFrame> available = frames.Values.OrderBy(frame => frame.Unit.Id).ToList();
            for (int frameIndex = 0; frameIndex < available.Count; frameIndex++)
            {
                RaidCommunicationFrame frame = available[(frameIndex + tick / RaidCommunicationPolicy.TickInterval) % available.Count];
                var comm = frame.State.Communication;
                comm.Knowledge.Prune(tick);
                comm.Observers.RemoveAll(observer => !frame.Members.ContainsKey(observer.PawnId));
                for (int observerIndex = 0; observerIndex < comm.Observers.Count; observerIndex++)
                {
                    RaidObserverMemory observer = comm.Observers[(observerIndex + tick / RaidCommunicationPolicy.TickInterval) % comm.Observers.Count];
                    observer.Reports.Prune(tick);
                    if (frame.Commander == null || observer.PawnId == frame.Commander.thingIDNumber
                        || !frame.CommandDelays.TryGetValue(observer.PawnId, out int delay)) continue;
                    foreach (RaidTacticalReport report in observer.Reports.Reports.Where(report => !comm.Knowledge.Knows(report)
                        && !InFlight(frame.Unit.Id, frame.Unit.Id, report.Id)).Take(2))
                        Queue(frame, frame, observer.PawnId, frame.Commander.thingIDNumber, report,
                            frame.RadioTo(frame, frame.Members[observer.PawnId], frame.Commander, blackout)
                                ? RaidCommunicationMode.Radio : RaidCommunicationMode.Voice, delay, tick);
                }
            }
            int pairCount = available.Count * Math.Max(0, available.Count - 1);
            for (int checkedPairs = 0; checkedPairs < Math.Min(RaidCommunicationPolicy.PairBudget, pairCount); checkedPairs++)
            {
                int pair = pairCursor++ % pairCount;
                int first = pair / (available.Count - 1), second = pair % (available.Count - 1);
                if (second >= first) second++;
                Share(available[first], available[second], tick);
            }
            if (pairCount > 0) pairCursor %= pairCount;
        }

        private bool Validate(RaidReportTransmission packet, out RaidCommunicationFrame source, out RaidCommunicationFrame receiver)
        {
            source = Frame(packet.FromUnit, frameTick); receiver = Frame(packet.ToUnit, frameTick);
            if (source == null || receiver == null || packet.Report == null
                || !source.Members.TryGetValue(packet.FromPawn, out Pawn a)
                || !receiver.Members.TryGetValue(packet.ToPawn, out Pawn b)) return false;
            // A newly slower relay must start its own delay. In particular, losing radio
            // cannot complete a 20-tick report through a replacement 40-tick voice route.
            if (!source.CommandDelays.TryGetValue(packet.FromPawn, out int delay) || delay > packet.CommandDelay) return false;
            if (source == receiver) return receiver.Commander == b;
            if (!Friendly(source, receiver)) return false;
            return packet.Mode == RaidCommunicationMode.Radio ? source.RadioTo(receiver, a, b, blackout)
                : RaidCommunicationFrame.VoiceTo(a, b, Math.Min(source.Doctrine?.voiceContactRange ?? 8, receiver.Doctrine?.voiceContactRange ?? 8));
        }

        private void Share(RaidCommunicationFrame source, RaidCommunicationFrame receiver, int tick)
        {
            if (!Friendly(source, receiver) || source.Commander == null) return;
            List<RaidTacticalReport> reports = source.State.Communication.Knowledge.Reports.Where(report =>
                RaidCommunicationPolicy.CanRelay(report.Route, receiver.Unit.Id, false)
                && !InFlight(source.Unit.Id, receiver.Unit.Id, report.Id)
                && !source.State.Communication.Receipts.Any(receipt => receipt.Peer == receiver.Unit.Id
                    && receipt.ReportId == report.Id && receipt.Revision >= report.Revision && receipt.Status == "Acknowledged"))
                .OrderBy(report => source.State.Communication.Receipts.FirstOrDefault(receipt => receipt.Peer == receiver.Unit.Id
                    && receipt.ReportId == report.Id)?.Tick ?? -1).ThenByDescending(report => report.ObservedTick).Take(2).ToList();
            if (reports.Count == 0) return;
            foreach (Pawn a in source.Members.Values.Where(pawn => source.CommandDelays.ContainsKey(pawn.thingIDNumber))
                .OrderBy(pawn => source.CommandDelays[pawn.thingIDNumber]))
                foreach (Pawn b in receiver.Members.Values.OrderBy(pawn => pawn == receiver.Commander ? 0 : 1))
                {
                    bool radio = source.RadioTo(receiver, a, b, blackout);
                    if (!radio && !RaidCommunicationFrame.VoiceTo(a, b,
                        Math.Min(source.Doctrine?.voiceContactRange ?? 8, receiver.Doctrine?.voiceContactRange ?? 8))) continue;
                    int delay = source.CommandDelays[a.thingIDNumber] + (radio
                        ? Math.Max(source.Doctrine?.radioReportTicks ?? 20, receiver.Doctrine?.radioReportTicks ?? 20)
                        : Math.Max(source.Doctrine?.voiceReportTicks ?? 40, receiver.Doctrine?.voiceReportTicks ?? 40));
                    foreach (RaidTacticalReport report in reports) Queue(source, receiver, a.thingIDNumber, b.thingIDNumber, report,
                        radio ? RaidCommunicationMode.Radio : RaidCommunicationMode.Voice, delay, tick);
                    return;
                }
        }

        private bool InFlight(string source, string receiver, string id) =>
            pending.Any(packet => packet.FromUnit == source && packet.ToUnit == receiver && packet.Report?.Id == id);

        private static bool Friendly(RaidCommunicationFrame source, RaidCommunicationFrame receiver) =>
            source.Unit.Faction != null && receiver.Unit.Faction != null && (source.Unit.Faction == receiver.Unit.Faction
                || source.Unit.Faction.RelationKindWith(receiver.Unit.Faction) == FactionRelationKind.Ally);

        private void Queue(RaidCommunicationFrame source, RaidCommunicationFrame receiver, int fromPawn, int toPawn,
            RaidTacticalReport report, RaidCommunicationMode mode, int delay, int tick)
        {
            if (queued >= RaidCommunicationPolicy.QueueBudget || pending.Count >= RaidCommunicationPolicy.PendingCapacity
                || InFlight(source.Unit.Id, receiver.Unit.Id, report.Id)
                || source.State.Communication.Receipts.Any(receipt => receipt.Peer == receiver.Unit.Id && receipt.ReportId == report.Id
                    && receipt.Revision >= report.Revision && (receipt.Status == "Acknowledged"
                        || receipt.Status == "Interrupted" && tick - receipt.Tick < 120))) return;
            queued++;
            pending.Add(new RaidReportTransmission { FromUnit = source.Unit.Id, ToUnit = receiver.Unit.Id,
                FromPawn = fromPawn, ToPawn = toPawn, DueTick = tick + Math.Max(20, delay), StartedTick = tick,
                CommandDelay = source.CommandDelays[fromPawn], Mode = mode, Report = report.Copy() });
            source.State.Communication.Receipt(receiver.Unit.Id, report, "Sending via " + mode, tick);
        }

        internal string ReportFor(string unitId)
        {
            var text = new StringBuilder();
            foreach (RaidReportTransmission packet in pending.Where(packet => packet.FromUnit == unitId || packet.ToUnit == unitId))
                text.AppendLine($"  {packet.Mode} {packet.FromUnit}/#{packet.FromPawn} → {packet.ToUnit}/#{packet.ToPawn}: "
                    + $"{packet.Report.Kind} {packet.Report.Id}@{packet.Report.Revision}; {(packet.AwaitingAck ? "ACK wait" : "report wait")} until {packet.DueTick}");
            return text.ToString();
        }
    }
}
