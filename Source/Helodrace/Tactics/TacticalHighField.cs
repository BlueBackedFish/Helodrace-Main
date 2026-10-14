using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalFieldResponse
    {
        public int LastEngaged = -1, RadioAt;
        public TacticalFieldFormation Formation;
    }
    public sealed partial class TacticalCommunications
    {
        private static TacticalFieldFormation FieldFormation(TacticalSquadCommand command, int tick)
        {
            var field = command.FieldResponse;
            if (field.Formation == null)
                field.Formation = new TacticalFieldFormation { Coordinator = command.Id, Members = new List<string> { command.Id },
                    Seen = new Dictionary<string, int> { [command.Id] = tick }, Origin = field.Anchor, Forward = field.Forward,
                    Started = field.Started, LastEngaged = field.LastEngaged };
            field.Formation.Seen[command.Id] = tick;
            PublishFieldSector(command,field.Formation,tick);
            return field.Formation;
        }
        private void ProbeFieldPair(TacticalSquadCommand source, TacticalSquadCommand receiver, TacticalChannel channel, int tick)
        {
            if (channel == TacticalChannel.None || !FieldPair(source, receiver) || tick < source.FieldResponse.RadioAt) return;
            source.FieldResponse.RadioAt = tick + 120;
            QueueFieldFormation(source, receiver, FieldFormation(source, tick), false, channel, tick);
            ProbeFieldMission(source,receiver,channel,tick);
        }
        private static bool FieldPair(TacticalSquadCommand a, TacticalSquadCommand b) => Active(a) && Active(b)
            && a.FieldResponse != null && b.FieldResponse != null && a.Goal == b.Goal && a.Owner == b.Owner
            && a.Link.Unit.Faction == b.Link.Unit.Faction
            && TacticalFieldDoctrine.Within(a.FieldResponse.Anchor, b.FieldResponse.Anchor, 60)
            && TacticalFieldDoctrine.Within(a.FieldResponse.Focus, b.FieldResponse.Focus, 25)
            && (long)a.FieldResponse.Forward.x * b.FieldResponse.Forward.x + (long)a.FieldResponse.Forward.z * b.FieldResponse.Forward.z > 0;
        private void QueueFieldFormation(TacticalSquadCommand from, TacticalSquadCommand to, TacticalFieldFormation report, bool reply,
            TacticalChannel channel, int tick)
        {
            if (pending.Exists(p => p.From == from && p.To == to && p.Kind == TacticalMessageKind.FieldFormation)) return;
            if (pending.Count >= TacticalCommunicationPolicy.QueueLimit) { QueueRejected++; return; }
            pending.Add(new TacticalMessage { From = from, To = to, Kind = TacticalMessageKind.FieldFormation,
                Channel = channel, Sent = tick, Due = tick + (channel == TacticalChannel.Radio
                    ? from.Link.Unit.Organization.doctrine?.radioReportTicks ?? 20 : from.Link.Unit.Organization.doctrine?.voiceReportTicks ?? 40),
                FromPawn = from.Link.Liaison.thingIDNumber, ToPawn = to.Link.Liaison.thingIDNumber,
                FieldFormation = report.Copy(), FieldReply = reply });
            MessagesSent++;
        }
        private void DeliverFieldFormation(TacticalMessage message, TacticalChannel channel, int tick)
        {
            if (channel == TacticalChannel.None || !FieldPair(message.From, message.To)
                || !FieldFormation(message.To, tick).Receive(message.FieldFormation, message.To.Id, message.From.Id, message.Sent, tick))
            { MessagesDropped++; LastDropReason = "field-formation"; return; }
            message.To.Due = System.Math.Min(message.To.Due, tick + 1); MessagesDelivered++;
            if (!message.FieldReply) QueueFieldFormation(message.To, message.From, FieldFormation(message.To, tick), true, channel, tick);
        }
    }
}
