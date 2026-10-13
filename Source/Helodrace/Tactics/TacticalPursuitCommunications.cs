using System;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalCommunications
    {
        internal static void RefreshPursuitDivision(TacticalSquadCommand command, int tick)
        {
            if (command.Link.PursuitDivision.Expire(command.Goal,tick)) InvalidatePursuitOrder(command);
            if (command.Link.PursuitDivision.Proposal==null) command.Link.PursuitPeer=null;
        }
        private static void InvalidatePursuitOrder(TacticalSquadCommand command)
        {
            command.Contacts.Pursuit.Version++;
            command.CancelEntryPreview();
            foreach (var child in command.CqbTeams) child.CancelEntryPreview();
            // No live plan/job cancellation or remote state write.
        }
        private static void IndoorLead(TacticalSquadCommand command, int tick, out int seen, out int id, out IntVec3 cell)
        {
            seen=-1; id=-1; cell=IntVec3.Invalid;
            foreach (var contact in command.Contacts.Memory.Entries)
                if (contact.SeenTick>seen && contact.SeenTick<=tick && tick-contact.SeenTick<TacticalContactMemory.RetentionTicks
                    && contact.Area.InBounds(command.Owner.map) && contact.Area.Roofed(command.Owner.map))
                { seen=contact.SeenTick; id=contact.EnemyId; cell=contact.Area; }
            foreach (var threat in command.Contacts.Melee.Entries)
                if (threat.Urgency>0 && threat.SeenTick>seen && threat.SeenTick<=tick && tick-threat.SeenTick<TacticalContactMemory.RetentionTicks
                    && threat.Position.InBounds(command.Owner.map) && threat.Position.Roofed(command.Owner.map))
                { seen=threat.SeenTick; id=threat.EnemyId; cell=threat.Position; }
        }
        private void ProbePursuitPair(TacticalSquadCommand source, TacticalSquadCommand receiver, TacticalChannel channel, int tick)
        {
            if (!Active(source) || !Active(receiver) || source.Defensive || receiver.Defensive
                || !source.Goal.IsValid || source.Goal!=receiver.Goal) return;
            RefreshPursuitDivision(source,tick); RefreshPursuitDivision(receiver,tick);
            var own=source.Link.PursuitDivision;
            var other=receiver.Link.PursuitDivision;
            if (own.Proposal!=null)
            {
                if (source.Link.PursuitPeer!=receiver) return;
                TacticalMessageKind kind=own.Stage==TacticalAgreementStage.Offered ? TacticalMessageKind.PursuitOffer
                    : own.Stage==TacticalAgreementStage.Accepted ? TacticalMessageKind.PursuitAccept : TacticalMessageKind.PursuitConfirm;
                QueuePursuit(source,receiver,kind,own.Proposal,channel,tick); return;
            }
            if (other.Proposal!=null || string.CompareOrdinal(source.Id,receiver.Id)>=0) return;
            var a=own.Assignment; var b=other.Assignment;
            if (a!=null && b!=null && a.Id==b.Id && a.Members.Count==b.Members.Count
                && a.Members.All(b.Members.Contains) && Math.Min(a.Until,b.Until)-tick>240) return;
            IndoorLead(source,tick,out int leadA,out int idA,out IntVec3 cellA);
            IndoorLead(receiver,tick,out int leadB,out int idB,out IntVec3 cellB);
            // A relay between clearing units cannot refresh an old assignment.
            if (a!=null && b!=null && a.Id==b.Id && a.Tracker!=source.Id && a.Tracker!=receiver.Id
                && a.Members.Count==b.Members.Count && a.Members.All(b.Members.Contains)) return;
            var proposal=TacticalPursuitDivision.Build(source.Id,receiver.Id,source.Goal,tick,a,b,leadA,leadB);
            if (proposal==null) return;
            if (proposal.Assignment.Tracker==source.Id && leadA>=0)
            { proposal.Assignment.Lead=cellA; proposal.Assignment.EnemyId=idA; proposal.Assignment.LeadTick=leadA; }
            else if (proposal.Assignment.Tracker==receiver.Id && leadB>=0)
            { proposal.Assignment.Lead=cellB; proposal.Assignment.EnemyId=idB; proposal.Assignment.LeadTick=leadB; }
            if (!own.Begin(proposal,source.Id,source.Goal,tick)) return;
            source.Link.PursuitPeer=receiver;
            QueuePursuit(source,receiver,TacticalMessageKind.PursuitOffer,proposal,channel,tick);
        }
        private void QueuePursuit(TacticalSquadCommand from, TacticalSquadCommand to, TacticalMessageKind kind,
            TacticalPursuitProposal proposal, TacticalChannel channel, int tick)
        {
            if (channel==TacticalChannel.None || proposal==null || pending.Any(p=>p.From==from && p.To==to
                && p.Kind==kind && p.PursuitProposal?.Id==proposal.Id)) return;
            if (pending.Count>=TacticalCommunicationPolicy.QueueLimit) { QueueRejected++; return; }
            var doctrine=from.Link.Unit.Organization.doctrine;
            pending.Add(new TacticalMessage {From=from,To=to,Kind=kind,Channel=channel,Sent=tick,
                Due=tick+(channel==TacticalChannel.Radio ? doctrine?.radioReportTicks ?? 20 : doctrine?.voiceReportTicks ?? 40),
                FromPawn=from.Link.Liaison.thingIDNumber,ToPawn=to.Link.Liaison.thingIDNumber,PursuitProposal=proposal.Copy()});
            MessagesSent++;
        }
        private void DeliverPursuit(TacticalMessage message, TacticalChannel channel, int tick)
        {
            var receiver=message.To;
            if (!Active(receiver) || !Active(message.From) || receiver.Defensive || message.From.Defensive || message.From.Goal!=receiver.Goal)
            { MessagesDropped++; LastDropReason="pursuit-mission"; return; }
            var division=receiver.Link.PursuitDivision;
            var before=division.Role(receiver.Id,receiver.Goal,tick);
            IntVec3 oldLead=division.Assignment?.Lead ?? IntVec3.Invalid;
            string oldTracker=division.Assignment?.Tracker;
            if (!division.Receive(message.Kind,message.PursuitProposal,message.From.Id,receiver.Id,receiver.Goal,tick))
            { MessagesDropped++; LastDropReason="pursuit-protocol"; return; }
            if (before!=division.Role(receiver.Id,receiver.Goal,tick) || oldLead!=(division.Assignment?.Lead ?? IntVec3.Invalid)
                || oldTracker!=division.Assignment?.Tracker) InvalidatePursuitOrder(receiver);
            receiver.Link.PursuitPeer=division.Proposal==null ? null : message.From;
            receiver.Due=Math.Min(receiver.Due,tick+1); MessagesDelivered++;
            if (message.Kind==TacticalMessageKind.PursuitOffer)
                QueuePursuit(receiver,message.From,TacticalMessageKind.PursuitAccept,message.PursuitProposal,channel,tick);
            else if (message.Kind==TacticalMessageKind.PursuitAccept)
                QueuePursuit(receiver,message.From,TacticalMessageKind.PursuitConfirm,message.PursuitProposal,channel,tick);
            else if (message.Kind==TacticalMessageKind.PursuitConfirm)
                QueuePursuit(receiver,message.From,TacticalMessageKind.PursuitReceipt,message.PursuitProposal,channel,tick);
        }
    }
}
