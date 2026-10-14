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
        public string Coordinator, Assignee, Supporter;
        public int Epoch, Number, Started, Until, Side;
        public IntVec3 Origin, Focus;
        public TacticalFieldMissionStage Stage;
        public bool FinishAcknowledged;
        public TacticalFieldMission Copy() => (TacticalFieldMission)MemberwiseClone();
        public bool Fresh(int tick) => Started <= tick && tick < Until && Until - Started <= 2400
            && Number > 0 && !string.IsNullOrEmpty(Coordinator) && !string.IsNullOrEmpty(Assignee)
            && (Coordinator != Assignee || !string.IsNullOrEmpty(Supporter) && Supporter != Assignee)
            && (Side == 1 || Side == -1) && Origin.IsValid && Focus.IsValid;
        public bool Matches(TacticalFieldMission other) => other != null && Coordinator == other.Coordinator && Assignee == other.Assignee
            && Supporter == other.Supporter && Epoch == other.Epoch && Number == other.Number && Started == other.Started && Until == other.Until
            && Origin == other.Origin && Focus == other.Focus && Side == other.Side;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Coordinator,"coordinator"); Scribe_Values.Look(ref Assignee,"assignee");
            Scribe_Values.Look(ref Supporter,"supporter");
            Scribe_Values.Look(ref Epoch,"epoch"); Scribe_Values.Look(ref Number,"number"); Scribe_Values.Look(ref Started,"started");
            Scribe_Values.Look(ref Until,"until"); Scribe_Values.Look(ref Side,"side");
            Scribe_Values.Look(ref Origin,"origin"); Scribe_Values.Look(ref Focus,"focus"); Scribe_Values.Look(ref Stage,"stage");
            Scribe_Values.Look(ref FinishAcknowledged,"finishAcknowledged");
        }
    }
    public static class TacticalFieldCoordinationPolicy
    {
        public static string Supporter(TacticalFieldFormation formation,string own,int tick)
        {
            string best=null;
            if(formation==null) return null;
            foreach(string id in formation.Members)
                if(formation.Sectors.TryGetValue(id,out var s) && s.Ready>=2 && !s.Busy
                    && s.At<=tick && tick-s.At<TacticalFieldDoctrine.RadioLife && (best==null || id==own)) best=id;
            return best;
        }
        public static bool Handover(TacticalFieldFormation formation,TacticalFieldMission previous,int tick)
        {
            return previous?.Stage==TacticalFieldMissionStage.Finished
                && (previous.Assignee!=previous.Coordinator || previous.FinishAcknowledged) && !string.IsNullOrEmpty(previous.Supporter)
                && previous.Assignee!=previous.Supporter && formation?.Coordinator==previous.Coordinator
                && formation.Members.Contains(previous.Assignee) && formation.Members.Contains(previous.Supporter)
                && formation.Sectors.TryGetValue(previous.Assignee,out var s) && s.Ready>=2 && !s.Busy
                && s.At<=tick && tick-s.At<180
                && formation.Sectors.TryGetValue(previous.Supporter,out var next) && next.Available>=3 && !next.Busy
                && next.At<=tick && tick-next.At<180;
        }
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
            string partner=packet.Assignee==packet.Coordinator ? packet.Supporter : packet.Assignee;
            bool recipient = receiver == partner && sender == packet.Coordinator;
            bool coordinator = receiver == packet.Coordinator && sender == partner;
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
            if(packet.Stage==TacticalFieldMissionStage.Finished && packet.Assignee==packet.Coordinator)
            {
                if(recipient && (current.Stage==TacticalFieldMissionStage.Active || current.Stage==TacticalFieldMissionStage.Finished))
                { current.Stage=TacticalFieldMissionStage.Finished; return true; }
                if(coordinator && current.Stage==TacticalFieldMissionStage.Finished)
                { current.FinishAcknowledged=true; return true; }
                return false;
            }
            if (packet.Stage == TacticalFieldMissionStage.Accepted && coordinator && current.Stage == TacticalFieldMissionStage.Offered)
            { current.Stage = TacticalFieldMissionStage.Committed; return true; }
            if (packet.Stage == TacticalFieldMissionStage.Committed && recipient && current.Stage == TacticalFieldMissionStage.Accepted)
            { current.Stage = packet.Assignee==packet.Coordinator ? TacticalFieldMissionStage.Active : TacticalFieldMissionStage.Committed; return true; }
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
                if (command.Owner.FieldSupportReady(command,member,tick)) ready++;
            }
            var field = command.FieldResponse; var maneuver = field.Maneuver;
            bool mobile = !command.Defensive && command.MedicalCare == null && command.ContactResponse == null
                && field.Screen == null && command.SupportCaller == null;
            formation.Sectors[command.Id] = new TacticalFieldSector { Origin = maneuver?.Phase == TacticalManeuverPhase.Strike ? field.FirePoints[1] : field.Anchor, Focus = field.Focus, At = tick,
                Available = mobile ? live : 0, Ready = ready, Busy = maneuver != null && maneuver.Phase != TacticalManeuverPhase.Strike,
                Flank = maneuver?.Phase == TacticalManeuverPhase.Strike ? field.FirePoints[1] : IntVec3.Invalid };
        }
        private void ProbeFieldMission(TacticalSquadCommand from, TacticalSquadCommand to, TacticalChannel channel, int tick)
        {
            var field = from.FieldResponse; var formation = FieldFormation(from,tick);
            var order = field.MissionOrder;
            bool waitingFinish=order?.Assignee==from.Id && order.Coordinator==from.Id
                && order.Stage==TacticalFieldMissionStage.Finished && !order.FinishAcknowledged;
            if (order == null || !order.Fresh(tick) || order.Stage == TacticalFieldMissionStage.Declined
                || order.Stage == TacticalFieldMissionStage.Finished && !waitingFinish)
            {
                bool handover=TacticalFieldCoordinationPolicy.Handover(formation,order,tick);
                string assignee=handover ? order.Supporter : TacticalFieldCoordinationPolicy.Assignee(formation,from.Id,tick);
                string supporter=handover ? order.Assignee : TacticalFieldCoordinationPolicy.Supporter(formation,from.Id,tick);
                string partner=assignee==from.Id ? supporter : assignee;
                if (formation.Coordinator == from.Id && (tick >= field.MissionRetryAt || handover)
                    && partner == to.Id && supporter!=null && formation.Sectors.TryGetValue(assignee,out var sector))
                {
                    IntVec3 direction = field.Focus - field.Anchor, delta = sector.Origin - field.Anchor;
                    field.MissionOrder = order = new TacticalFieldMission { Coordinator = from.Id, Assignee = assignee, Supporter=supporter,
                        Epoch = field.Started, Number = ++field.MissionSequence, Started = tick, Until = tick + 2400,
                        Origin = sector.Origin, Focus = field.Focus,
                        Side = (long)direction.x * delta.z - (long)direction.z * delta.x > 0 ? 1 : -1 };
                    field.MissionRetryAt = tick + 1800;
                }
            }
            if (order == null || !order.Fresh(tick)) return;
            string endpoint=order.Assignee==order.Coordinator ? order.Supporter : order.Assignee;
            if (from.Id == order.Coordinator && to.Id == endpoint
                && (order.Stage == TacticalFieldMissionStage.Offered || order.Stage == TacticalFieldMissionStage.Committed
                    || order.Assignee==order.Coordinator && order.Stage==TacticalFieldMissionStage.Finished)
                || from.Id == endpoint && to.Id == order.Coordinator)
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
            bool supportingSelf=message.FieldMission?.Assignee==message.FieldMission?.Coordinator && message.FieldMission?.Supporter==message.To.Id;
            bool canAccept = (field.Maneuver == null || field.Maneuver.Phase==TacticalManeuverPhase.Strike
                    && (supportingSelf || field.MissionOrder?.Stage==TacticalFieldMissionStage.Finished))
                && !message.To.Defensive && message.To.MedicalCare == null
                && message.To.ContactResponse == null && message.To.SupportCaller == null && field.Screen == null && tick >= field.NextSortieAt;
            if(supportingSelf) canAccept &= message.To.Members.Count(m=>message.To.Owner.FieldSupportReady(message.To,m,tick))>=2;
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
            if (field.MissionOrder.Stage == TacticalFieldMissionStage.Accepted || message.FieldMission.Stage == TacticalFieldMissionStage.Accepted
                || supportingSelf && message.FieldMission.Stage==TacticalFieldMissionStage.Committed)
                QueueFieldMission(message.To,message.From,field.MissionOrder,channel,tick);
            else if(supportingSelf && message.FieldMission.Stage==TacticalFieldMissionStage.Finished)
                QueueFieldMission(message.To,message.From,field.MissionOrder,channel,tick);
        }
    }
}
