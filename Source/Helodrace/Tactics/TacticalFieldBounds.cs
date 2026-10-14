using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public enum TacticalBoundStep { Offer, Accept, Start, Moving, Ready, Follow, Following, Complete, Done }
    public sealed class TacticalBoundPacket : IExposable
    {
        public TacticalBoundStep Step;
        public TacticalChannel Channel;
        public int Epoch, Leg, From, To, Sent, Due, Ready, Needed;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Step,"step"); Scribe_Values.Look(ref Channel,"channel");
            Scribe_Values.Look(ref Epoch,"epoch"); Scribe_Values.Look(ref Leg,"leg");
            Scribe_Values.Look(ref From,"from"); Scribe_Values.Look(ref To,"to");
            Scribe_Values.Look(ref Sent,"sent"); Scribe_Values.Look(ref Due,"due");
            Scribe_Values.Look(ref Ready,"ready"); Scribe_Values.Look(ref Needed,"needed");
        }
    }
    public sealed class TacticalFieldBound : IExposable
    {
        public int Epoch, Leg, Started, ProbeAt, ReportAt = -1, ReportReady;
        public bool Split;
        public TacticalBoundStep Step;
        public List<int> First = new List<int>();
        public TacticalBoundPacket Pending;
        public bool Deliver(TacticalBoundPacket packet, TacticalChannel channel, int from, int to, int tick)
        {
            if (packet == null || packet.Epoch != Epoch || packet.Leg != Leg || packet.Step != Step
                || packet.Channel != channel || channel == TacticalChannel.None || packet.From != from || packet.To != to
                || from == to || tick < packet.Due || packet.Sent > tick || tick-packet.Sent >= 180
                || packet.Needed < 1 || packet.Ready < packet.Needed) return false;
            switch (Step)
            {
                case TacticalBoundStep.Offer: Step=TacticalBoundStep.Accept; break;
                case TacticalBoundStep.Accept: Step=TacticalBoundStep.Start; break;
                case TacticalBoundStep.Start: Step=TacticalBoundStep.Moving; break;
                case TacticalBoundStep.Ready: ReportAt=tick; ReportReady=packet.Ready; Step=TacticalBoundStep.Follow; break;
                case TacticalBoundStep.Follow:
                    if (ReportAt < 0 || tick-ReportAt >= 180) return false;
                    Step=Split ? TacticalBoundStep.Following : TacticalBoundStep.Done; break;
                case TacticalBoundStep.Complete: Step=TacticalBoundStep.Done; break;
                default: return false;
            }
            return true;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Epoch,"epoch"); Scribe_Values.Look(ref Leg,"leg"); Scribe_Values.Look(ref Started,"started");
            Scribe_Values.Look(ref ProbeAt,"probeAt"); Scribe_Values.Look(ref ReportAt,"reportAt",-1);
            Scribe_Values.Look(ref ReportReady,"reportReady"); Scribe_Values.Look(ref Split,"split"); Scribe_Values.Look(ref Step,"step");
            Scribe_Collections.Look(ref First,"first",LookMode.Value); Scribe_Deep.Look(ref Pending,"pending");
        }
    }
    public static class TacticalFieldBoundPolicy
    {
        public static int ShotGrace(float warmup, float cooldown, int burstTicks) =>
            Math.Max(180, Math.Min(1200, (int)Math.Ceiling((warmup+cooldown)*60) + Math.Max(0,burstTicks) + 90));
        public static bool Moving(TacticalFieldBound bound, bool first) => bound != null &&
            (first ? bound.Step == TacticalBoundStep.Moving || bound.Step == TacticalBoundStep.Ready
                : bound.Split && (bound.Step == TacticalBoundStep.Following || bound.Step == TacticalBoundStep.Complete));
    }
    public sealed partial class TacticalMemberCommand
    {
        public int FieldSupportCheckAt, FieldSupportUntil = -1;
        public IntVec3 FieldArrivalCell = IntVec3.Invalid;
        public int FieldArrivalCheckAt;
        public bool FieldArrivalCovered;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool FieldArrivalCover(TacticalSquadCommand command,TacticalMemberCommand member,int tick)
        {
            if(member.FieldArrivalCell!=member.Pawn.Position || tick>=member.FieldArrivalCheckAt)
            {
                member.FieldArrivalCell=member.Pawn.Position; member.FieldArrivalCheckAt=tick+30;
                member.FieldArrivalCovered=FieldCoverAt(command,member.Pawn.Position,command.FieldResponse.Focus);
            }
            return member.FieldArrivalCovered;
        }
        internal bool FieldSupportReady(TacticalSquadCommand command, TacticalMemberCommand member, int tick)
        {
            Pawn pawn=member.Pawn;
            if(pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>()?.ReadyToFire==false) return false;
            if (pawn.CurJobDef != JobDefOf.Wait_Combat || pawn.CurJob != member.Job
                || pawn.Dead || pawn.Downed || pawn.InMentalState || PawnUtility.PlayerForcedJobNowOrSoon(pawn)) return false;
            if(tick<member.FieldSupportCheckAt) return tick<member.FieldSupportUntil;
            member.FieldSupportCheckAt=tick+30; member.FieldSupportUntil=-1;
            var field=command.FieldResponse; Verb verb=pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if(field==null || verb?.IsMeleeAttack != false || !FieldArrivalCover(command,member,tick)) return false;
            int grace=TacticalFieldBoundPolicy.ShotGrace(verb.WarmupTime,verb.verbProps.AdjustedCooldown(verb,pawn),
                Math.Max(0,verb.BurstShotCount-1)*verb.TicksBetweenBurstShots);
            bool useful=member.FieldShotAt>=0 && tick-member.FieldShotAt<grace
                || verb.CanHitTarget(new LocalTargetInfo(field.Focus))
                || field.LastEngaged<0 && field.Motion==TacticalObservedMotion.Approaching;
            if(useful) member.FieldSupportUntil=tick+45;
            return useful;
        }
        private void PrepareFieldBound(TacticalSquadCommand command, TacticalFieldResponse field, List<TacticalMemberCommand> active, int tick)
        {
            var m=field.Maneuver;
            var first=new List<int>();
            bool high=command.Link.Unit.Faction.def.defName=="HD_HelodCivilHighFaction";
            // Stable real fireteam membership for a whole HIGH squad; a LOW
            // temporary party uses contiguous groups, never ordinal parity.
            if(m.WholeSquad && high)
                first.AddRange(m.Party.Where(i=>command.Members[i].Fireteam==1));
            else if(!high && m.Party.Count>=4)
                first.AddRange(m.Party.Take((m.Party.Count+1)/2));
            bool split=first.Count>=2 && m.Party.Count-first.Count>=2;
            if(split && !m.WholeSquad && !high
                && active.Count(a=>m.Party.Contains(command.Members.IndexOf(a)) && !first.Contains(command.Members.IndexOf(a))
                    && FieldSupportReady(command,a,tick))<2) split=false;
            if(!split) { first.Clear(); first.AddRange(m.Party); }
            if(m.Bound!=null) { first=new List<int>(m.Bound.First); split=m.Bound.Split; }
            m.Bound=new TacticalFieldBound { Epoch=m.Started,Leg=m.Leg,Started=tick,First=first,Split=split };
        }
        private bool AdvanceFieldBound(TacticalSquadCommand command,List<TacticalMemberCommand> active,TacticalFieldResponse field,int tick)
        {
            var m=field.Maneuver; var bound=m.Bound;
            if(bound==null) { PrepareFieldBound(command,field,active,tick); bound=m.Bound; }
            var first=active.Where(a=>bound.First.Contains(command.Members.IndexOf(a))).ToList();
            var second=active.Where(a=>m.Party.Contains(command.Members.IndexOf(a)) && !bound.First.Contains(command.Members.IndexOf(a))).ToList();
            var support=bound.Split ? second : active.Where(a=>!m.Party.Contains(command.Members.IndexOf(a))).ToList();
            // The already delivered W2 lease and sector report supply an
            // external supporter for an unsplit whole squad; no remote roster.
            bool remote=m.WholeSquad && !bound.Split;
            var mover=first.FirstOrDefault(); var cover=support.FirstOrDefault();
            if(mover==null) return false;
            if(remote) cover=first.Skip(1).FirstOrDefault();
            if(cover==null) return false;
            int movingReady=first.Count(a=>FieldAtWaypoint(command,a) && FieldSupportReady(command,a,tick));
            int supportReady=remote ? RemoteFieldSupport(field,tick) : support.Count(a=>FieldSupportReady(command,a,tick));
            if(bound.Step==TacticalBoundStep.Moving)
            {
                if(!TacticalFieldPolicy.Ready(first.Count,movingReady)) return false;
                bound.Step=TacticalBoundStep.Ready;
            }
            if(bound.Step==TacticalBoundStep.Following)
            {
                int secondReady=second.Count(a=>FieldAtWaypoint(command,a) && FieldSupportReady(command,a,tick));
                if(!TacticalFieldPolicy.Ready(second.Count,secondReady)) return false;
                bound.Step=TacticalBoundStep.Complete;
            }
            if(bound.Step==TacticalBoundStep.Done) return true;
            if(tick<bound.ProbeAt) return false;
            var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if(!budget.TryCommunication(tick)) return false;
            bound.ProbeAt=tick+15;
            bool reverse=bound.Step==TacticalBoundStep.Accept || bound.Step==TacticalBoundStep.Ready;
            Pawn from=(reverse ? mover : cover).Pawn, to=(reverse ? cover : mover).Pawn;
            TacticalChannel channel=TacticalCommunications.TeamChannel(command,from,to);
            bool externalOK=!m.WholeSquad || RemoteFieldSupport(field,tick)>=2
                || bound.Step==TacticalBoundStep.Ready || bound.Step==TacticalBoundStep.Complete
                || !bound.Split && bound.Step==TacticalBoundStep.Follow;
            if(bound.Pending!=null)
            {
                if(tick<bound.Pending.Due) return false;
                var packet=bound.Pending; bound.Pending=null;
                // A lost channel or liaison invalidates delivery, not the
                // already agreed movement. Recontact retries are bounded.
                if(!externalOK || !bound.Deliver(packet,channel,from.thingIDNumber,to.thingIDNumber,tick)) return false;
                ResetHammerDecisions(command,field);
                return bound.Step==TacticalBoundStep.Done;
            }
            if(channel==TacticalChannel.None || !externalOK) return false;
            int ready=1, needed=1;
            if(bound.Step==TacticalBoundStep.Offer || bound.Step==TacticalBoundStep.Start) { ready=supportReady; needed=2; }
            if(bound.Step==TacticalBoundStep.Ready) { ready=movingReady; needed=(first.Count*3+3)/4; }
            if(bound.Step==TacticalBoundStep.Follow)
            {
                // Refreshed readiness is emitted by the covering group, not
                // guessed from a remote recipient's current pawn positions.
                if(bound.ReportAt<0 || tick-bound.ReportAt>=180 || !TacticalFieldPolicy.Ready(first.Count,movingReady))
                { bound.Step=TacticalBoundStep.Ready; return false; }
                ready=bound.ReportReady; needed=(first.Count*3+3)/4;
            }
            if(bound.Step==TacticalBoundStep.Complete)
            { ready=second.Count(a=>FieldAtWaypoint(command,a) && FieldSupportReady(command,a,tick)); needed=(second.Count*3+3)/4; }
            if(ready<needed || needed<1) return false;
            int delay=channel==TacticalChannel.Radio ? command.Link.Unit.Organization.doctrine?.radioReportTicks ?? 20
                : command.Link.Unit.Organization.doctrine?.voiceReportTicks ?? 40;
            bound.Pending=new TacticalBoundPacket { Step=bound.Step,Epoch=bound.Epoch,Leg=bound.Leg,Channel=channel,
                From=from.thingIDNumber,To=to.thingIDNumber,Sent=tick,Due=tick+Math.Max(1,delay),Ready=ready,Needed=needed };
            return false;
        }
        private static int RemoteFieldSupport(TacticalFieldResponse field,int tick)
        {
            var order=field.MissionOrder;
            if(order?.Fresh(tick)!=true || field.Formation==null || string.IsNullOrEmpty(order.Supporter)
                || !field.Formation.Sectors.TryGetValue(order.Supporter,out var sector)
                || tick<sector.At || tick-sector.At>=180 || sector.Busy) return 0;
            return sector.Ready;
        }
    }
}
