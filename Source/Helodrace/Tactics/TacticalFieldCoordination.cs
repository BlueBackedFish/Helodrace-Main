using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    // Delivered values only. A coordinator never queries a peer's live roster.
    public sealed class TacticalFieldSector : IExposable
    {
        public IntVec3 Origin, Focus, Flank = IntVec3.Invalid;
        public int At, Available, Ready;
        public bool Busy;
        public TacticalFieldSector Copy() => (TacticalFieldSector)MemberwiseClone();
        public void ExposeData()
        {
            Scribe_Values.Look(ref Origin,"origin"); Scribe_Values.Look(ref Focus,"focus");
            Scribe_Values.Look(ref Flank,"flank",IntVec3.Invalid); Scribe_Values.Look(ref At,"at");
            Scribe_Values.Look(ref Available,"available"); Scribe_Values.Look(ref Ready,"ready"); Scribe_Values.Look(ref Busy,"busy");
        }
    }
    public enum TacticalFieldMissionStage { Offered, Accepted, Committed, Active, Finished, Declined }
    public sealed class TacticalFieldMission : IExposable
    {
        public string Coordinator, Assignee;
        public int Epoch, Number, Started, Until, Side;
        public IntVec3 Origin, Focus;
        public TacticalFieldMissionStage Stage;
        public TacticalFieldMission Copy() => (TacticalFieldMission)MemberwiseClone();
        public bool Fresh(int tick) => Started <= tick && tick < Until && Until - Started <= 2400
            && Number > 0 && !string.IsNullOrEmpty(Coordinator) && !string.IsNullOrEmpty(Assignee)
            && Coordinator != Assignee && (Side == 1 || Side == -1) && Origin.IsValid && Focus.IsValid;
        public bool Matches(TacticalFieldMission other) => other != null && Coordinator == other.Coordinator && Assignee == other.Assignee
            && Epoch == other.Epoch && Number == other.Number && Started == other.Started && Until == other.Until
            && Origin == other.Origin && Focus == other.Focus && Side == other.Side;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Coordinator,"coordinator"); Scribe_Values.Look(ref Assignee,"assignee");
            Scribe_Values.Look(ref Epoch,"epoch"); Scribe_Values.Look(ref Number,"number"); Scribe_Values.Look(ref Started,"started");
            Scribe_Values.Look(ref Until,"until"); Scribe_Values.Look(ref Side,"side");
            Scribe_Values.Look(ref Origin,"origin"); Scribe_Values.Look(ref Focus,"focus"); Scribe_Values.Look(ref Stage,"stage");
        }
    }
    public static class TacticalFieldCoordinationPolicy
    {
        public static string Assignee(TacticalFieldFormation formation, string own, int tick)
        {
            if (formation == null || formation.Coordinator != own) return null;
            TacticalFieldSector support = null; string supportId = null;
            foreach (string id in formation.Members)
                if (formation.Sectors.TryGetValue(id,out var observed) && observed.Ready >= 2 && !observed.Busy
                    && observed.At <= tick && tick - observed.At < TacticalFieldDoctrine.RadioLife
                    && (support == null || id == own))
                { support = observed; supportId = id; }
            if (support == null) return null;
            string best = null; float score = float.MinValue;
            foreach (var pair in formation.Sectors)
            {
                var sector = pair.Value;
                if (!formation.Members.Contains(pair.Key) || tick < sector.At || tick - sector.At >= TacticalFieldDoctrine.RadioLife) continue;
                if (sector.Busy) return null; // finish an already reported moving sortie first
                if (pair.Key == own || pair.Key == supportId || sector.Available < 3 || sector.Flank.IsValid) continue;
                float value = TacticalFieldSpacingPolicy.Penalty(sector.Origin, support.Origin, sector.Focus);
                if (value > score || value == score && string.CompareOrdinal(pair.Key,best) < 0) { best = pair.Key; score = value; }
            }
            return best;
        }
        public static bool Receive(ref TacticalFieldMission current, TacticalFieldMission packet, string sender, string receiver,
            TacticalFieldFormation formation, bool canAccept, int tick)
        {
            if (packet == null || !packet.Fresh(tick) || formation?.Coordinator != packet.Coordinator
                || !formation.Members.Contains(sender) || !formation.Members.Contains(receiver)) return false;
            bool recipient = receiver == packet.Assignee && sender == packet.Coordinator;
            bool coordinator = receiver == packet.Coordinator && sender == packet.Assignee;
            if (packet.Stage == TacticalFieldMissionStage.Offered && recipient)
            {
                if (current?.Matches(packet) == true) return current.Stage == TacticalFieldMissionStage.Accepted;
                if (!canAccept || current != null && (current.Fresh(tick) && current.Stage != TacticalFieldMissionStage.Finished
                    && current.Stage != TacticalFieldMissionStage.Declined
                    || current.Coordinator == packet.Coordinator && (current.Epoch > packet.Epoch
                        || current.Epoch == packet.Epoch && current.Number >= packet.Number))) return false;
                current = packet.Copy(); current.Stage = TacticalFieldMissionStage.Accepted; return true;
            }
            if (current?.Matches(packet) != true) return false;
            if (packet.Stage == TacticalFieldMissionStage.Accepted && coordinator && current.Stage == TacticalFieldMissionStage.Offered)
            { current.Stage = TacticalFieldMissionStage.Committed; return true; }
            if (packet.Stage == TacticalFieldMissionStage.Committed && recipient && current.Stage == TacticalFieldMissionStage.Accepted)
            { current.Stage = TacticalFieldMissionStage.Committed; return true; }
            if (coordinator && (packet.Stage == TacticalFieldMissionStage.Active || packet.Stage == TacticalFieldMissionStage.Finished
                || packet.Stage == TacticalFieldMissionStage.Declined)
                && (current.Stage == TacticalFieldMissionStage.Committed || current.Stage == TacticalFieldMissionStage.Active
                    || packet.Stage == TacticalFieldMissionStage.Declined && current.Stage == TacticalFieldMissionStage.Offered))
            { current.Stage = packet.Stage; return true; }
            return false;
        }
    }
    public sealed partial class TacticalFieldResponse
    {
        public TacticalFieldMission MissionOrder;
        public int MissionSequence, MissionRetryAt;
        public int FlankWidth => Maneuver?.WholeSquad == true ? 12 : TacticalFieldDoctrine.LowFlankRadius;
        public int FlankDepth => Maneuver?.WholeSquad == true ? 8 : TacticalFieldDoctrine.LowFlankDepth;
    }
    public sealed partial class TacticalMessage { public TacticalFieldMission FieldMission; }
    public sealed partial class TacticalCommunications
    {
        private static void PublishFieldSector(TacticalSquadCommand command, TacticalFieldFormation formation, int tick)
        {
            int live = 0, ready = 0;
            foreach (var member in command.Members)
            {
                Pawn pawn = member.Pawn;
                if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || pawn.InMentalState) continue;
                if (pawn.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack == false) live++;
                if (member.FieldPostState == TacticalFieldPostState.Fixed && member.FieldShotAt >= 0 && tick - member.FieldShotAt < 300) ready++;
            }
            var field = command.FieldResponse; var maneuver = field.Maneuver;
            bool mobile = !command.Defensive && command.MedicalCare == null && command.ContactResponse == null
                && field.Screen == null && command.SupportCaller == null;
            formation.Sectors[command.Id] = new TacticalFieldSector { Origin = field.Anchor, Focus = field.Focus, At = tick,
                Available = mobile ? live : 0, Ready = ready, Busy = maneuver != null && maneuver.Phase != TacticalManeuverPhase.Strike,
                Flank = maneuver?.Phase == TacticalManeuverPhase.Strike ? field.FirePoints[1] : IntVec3.Invalid };
        }
        private void ProbeFieldMission(TacticalSquadCommand from, TacticalSquadCommand to, TacticalChannel channel, int tick)
        {
            var field = from.FieldResponse; var formation = FieldFormation(from,tick);
            var order = field.MissionOrder;
            if (order == null || !order.Fresh(tick) || order.Stage == TacticalFieldMissionStage.Declined || order.Stage == TacticalFieldMissionStage.Finished)
            {
                if (formation.Coordinator == from.Id && tick >= field.MissionRetryAt
                    && TacticalFieldCoordinationPolicy.Assignee(formation,from.Id,tick) == to.Id
                    && formation.Sectors.TryGetValue(to.Id,out var sector))
                {
                    IntVec3 direction = field.Focus - field.Anchor, delta = sector.Origin - field.Anchor;
                    field.MissionOrder = order = new TacticalFieldMission { Coordinator = from.Id, Assignee = to.Id,
                        Epoch = field.Started, Number = ++field.MissionSequence, Started = tick, Until = tick + 2400,
                        Origin = sector.Origin, Focus = field.Focus,
                        Side = (long)direction.x * delta.z - (long)direction.z * delta.x > 0 ? 1 : -1 };
                    field.MissionRetryAt = tick + 1800;
                }
            }
            if (order == null || !order.Fresh(tick)) return;
            if (from.Id == order.Coordinator && to.Id == order.Assignee
                && (order.Stage == TacticalFieldMissionStage.Offered || order.Stage == TacticalFieldMissionStage.Committed)
                || from.Id == order.Assignee && to.Id == order.Coordinator)
                QueueFieldMission(from,to,order,channel,tick);
        }
        private void QueueFieldMission(TacticalSquadCommand from, TacticalSquadCommand to, TacticalFieldMission order, TacticalChannel channel, int tick)
        {
            if (pending.Exists(p => p.From == from && p.To == to && p.Kind == TacticalMessageKind.FieldMission)) return;
            if (pending.Count >= TacticalCommunicationPolicy.QueueLimit) { QueueRejected++; return; }
            pending.Add(new TacticalMessage { From = from, To = to, Kind = TacticalMessageKind.FieldMission,
                Channel = channel, Sent = tick, Due = tick + (channel == TacticalChannel.Radio
                    ? from.Link.Unit.Organization.doctrine?.radioReportTicks ?? 20 : from.Link.Unit.Organization.doctrine?.voiceReportTicks ?? 40),
                FromPawn = from.Link.Liaison.thingIDNumber, ToPawn = to.Link.Liaison.thingIDNumber, FieldMission = order.Copy() });
            MessagesSent++;
        }
        private void DeliverFieldMission(TacticalMessage message, TacticalChannel channel, int tick)
        {
            if (channel == TacticalChannel.None || !FieldPair(message.From,message.To)) { MessagesDropped++; return; }
            var field = message.To.FieldResponse;
            bool canAccept = field.Maneuver == null && !message.To.Defensive && message.To.MedicalCare == null
                && message.To.ContactResponse == null && message.To.SupportCaller == null && field.Screen == null && tick >= field.NextSortieAt;
            if (!TacticalFieldCoordinationPolicy.Receive(ref field.MissionOrder,message.FieldMission,message.From.Id,
                message.To.Id,FieldFormation(message.To,tick),canAccept,tick))
            {
                MessagesDropped++; LastDropReason = "field-mission";
                if (message.FieldMission?.Stage == TacticalFieldMissionStage.Offered && field.MissionOrder?.Matches(message.FieldMission) != true)
                { var declined = message.FieldMission.Copy(); declined.Stage = TacticalFieldMissionStage.Declined;
                    QueueFieldMission(message.To,message.From,declined,channel,tick); }
                return;
            }
            MessagesDelivered++; message.To.Due = Math.Min(message.To.Due,tick + 1);
            if (field.MissionOrder.Stage == TacticalFieldMissionStage.Accepted || message.FieldMission.Stage == TacticalFieldMissionStage.Accepted)
                QueueFieldMission(message.To,message.From,field.MissionOrder,channel,tick);
        }
    }
}
