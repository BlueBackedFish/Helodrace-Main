using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalFieldResponse
    {
        public int LastEngaged = -1, RetreatStep, MovingGroups, RadioAt, AppliedStep = -1;
        public string AppliedGroup;
        public List<IntVec3> RetreatBases = new List<IntVec3>(3);
        public TacticalFieldFormation Formation;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private void AdvanceHighFieldRetreat(TacticalSquadCommand command, TacticalFieldResponse field, int tick)
        {
            if (field.BoundPending || field.Assigned < field.Order.Count || field.Stage == TacticalFieldStage.Forming) return;
            var formation = field.Formation;
            if (formation?.Members.Count > 1 && (!RaidTacticalRadioUtility.SignalAvailable(map)
                || formation.Expire(command.Id, tick)))
            {
                // Keep the current line on radio loss instead of advancing
                // back into the enemy just to restore a solo template.
                field.RetreatStep = Math.Min(TacticalFieldDoctrine.SoloSteps, formation.Step);
                field.RetreatBases.Clear();
                for (int point = 0; point < 3; point++) field.RetreatBases.Add(field.FirePoints[point]
                    + TacticalFieldDoctrine.Offset(field.Forward, 0, TacticalFieldDoctrine.RetreatDepth(field.RetreatStep, point, 3)));
                field.Formation = formation = null; field.AppliedGroup = null; field.AppliedStep = field.RetreatStep;
            }
            bool grouped = formation?.Members.Count > 1;
            if (grouped) formation.LastEngaged = Math.Max(formation.LastEngaged, field.LastEngaged);
            int step = grouped ? formation.Step : field.RetreatStep;
            int started = grouped ? formation.Started : field.Started;
            int engaged = grouped ? formation.LastEngaged : field.LastEngaged;
            if ((!grouped || formation.Coordinator == command.Id) && field.Stage == TacticalFieldStage.Defending
                && TacticalFieldDoctrine.RetreatDue(tick, started, field.NextMove, engaged, step, grouped))
            {
                step++; field.NextMove = tick + TacticalFieldDoctrine.RetreatInterval;
                if (grouped) formation.Step = step; else field.RetreatStep = step;
            }
            string group = grouped ? string.Join("|", formation.Members) : null;
            if (step == field.AppliedStep && group == field.AppliedGroup) return;
            field.AppliedGroup = group; field.AppliedStep = step; field.MovingGroups = 0;
            for (int point = 0; point < 3; point++)
            {
                IntVec3 next = grouped ? formation.Point(command.Id, point)
                    : field.RetreatBases[point] - TacticalFieldDoctrine.Offset(field.Forward, 0, TacticalFieldDoctrine.RetreatDepth(step, point, 3));
                // Membership changes may spread laterally but cannot order a
                // pawn forward merely because it inherited a different slot.
                next = TacticalFieldDoctrine.WithoutAdvance(field.FirePoints[point], next, field.Forward);
                if (next == field.FirePoints[point]) continue;
                field.FirePoints[point] = next; field.Areas[point] = new TacticalFireArea { Started = tick };
                field.MovingGroups |= 1 << point;
            }
            if (field.MovingGroups == 0) return;
            field.MovingTeam = field.MovingGroups == 2 ? 1 : -1;
            field.Stage = TacticalFieldStage.Moving; field.BoundPending = true; field.BoundCursor = 0;
            field.MoveStarted = tick; FieldBounds++;
        }
    }
    public sealed partial class TacticalCommunications
    {
        private static TacticalFieldFormation FieldFormation(TacticalSquadCommand command, int tick)
        {
            var field = command.FieldResponse;
            if (field.Formation == null)
                field.Formation = new TacticalFieldFormation { Coordinator = command.Id, Members = new List<string> { command.Id },
                    Seen = new Dictionary<string, int> { [command.Id] = tick }, Origin = field.Anchor, Forward = field.Forward,
                    Started = field.Started, Step = field.RetreatStep, LastEngaged = field.LastEngaged };
            field.Formation.Seen[command.Id] = tick;
            if (field.Formation.Members.Count == 1) field.Formation.Step = field.RetreatStep;
            field.Formation.LastEngaged = Math.Max(field.Formation.LastEngaged, field.LastEngaged);
            return field.Formation;
        }
        private void ProbeFieldPair(TacticalSquadCommand source, TacticalSquadCommand receiver, TacticalChannel channel, int tick)
        {
            if (channel != TacticalChannel.Radio || !FieldPair(source, receiver) || tick < source.FieldResponse.RadioAt) return;
            source.FieldResponse.RadioAt = tick + 120;
            QueueFieldFormation(source, receiver, FieldFormation(source, tick), false, tick);
        }
        private static bool FieldPair(TacticalSquadCommand a, TacticalSquadCommand b) => Active(a) && Active(b)
            && a.FieldResponse?.High == true && b.FieldResponse?.High == true && a.Goal == b.Goal
            && TacticalFieldDoctrine.Within(a.FieldResponse.Anchor, b.FieldResponse.Anchor, 60)
            && TacticalFieldDoctrine.Within(a.FieldResponse.Focus, b.FieldResponse.Focus, 25)
            && a.FieldResponse.Forward.x * b.FieldResponse.Forward.x + a.FieldResponse.Forward.z * b.FieldResponse.Forward.z > 0;
        private void QueueFieldFormation(TacticalSquadCommand from, TacticalSquadCommand to, TacticalFieldFormation report, bool reply, int tick)
        {
            if (pending.Exists(p => p.From == from && p.To == to && p.Kind == TacticalMessageKind.FieldFormation)) return;
            if (pending.Count >= TacticalCommunicationPolicy.QueueLimit) { QueueRejected++; return; }
            pending.Add(new TacticalMessage { From = from, To = to, Kind = TacticalMessageKind.FieldFormation,
                Channel = TacticalChannel.Radio, Sent = tick, Due = tick + (from.Link.Unit.Organization.doctrine?.radioReportTicks ?? 20),
                FromPawn = from.Link.Liaison.thingIDNumber, ToPawn = to.Link.Liaison.thingIDNumber,
                FieldFormation = report.Copy(), FieldReply = reply });
            MessagesSent++;
        }
        private void DeliverFieldFormation(TacticalMessage message, TacticalChannel channel, int tick)
        {
            if (channel != TacticalChannel.Radio || !FieldPair(message.From, message.To)
                || !FieldFormation(message.To, tick).Receive(message.FieldFormation, message.To.Id, message.From.Id, message.Sent, tick))
            { MessagesDropped++; LastDropReason = "field-formation"; return; }
            message.To.Due = Math.Min(message.To.Due, tick + 1); MessagesDelivered++;
            if (!message.FieldReply) QueueFieldFormation(message.To, message.From, FieldFormation(message.To, tick), true, tick);
        }
    }
}
