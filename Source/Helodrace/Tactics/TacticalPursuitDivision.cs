using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    public enum TacticalPursuitRole { Independent, Pursue, Clear }
    public sealed class TacticalPursuitAssignment : IExposable
    {
        public const int MemberLimit = 16;
        public string Id, Coordinator, Tracker;
        public IntVec3 Goal;
        public IntVec3 Lead = IntVec3.Invalid;
        public int EnemyId = -1, LeadTick = -1;
        public int Until;
        public List<string> Members = new List<string>();
        public TacticalPursuitAssignment Copy() => new TacticalPursuitAssignment { Id=Id, Coordinator=Coordinator,
            Tracker=Tracker, Goal=Goal, Until=Until, Lead=Lead,EnemyId=EnemyId,LeadTick=LeadTick,Members=new List<string>(Members) };
        public bool Valid(IntVec3 goal, int tick) => !string.IsNullOrEmpty(Id) && goal==Goal && tick<Until
            && Members.Count>=2 && Members.Count<=MemberLimit && Members.Contains(Coordinator) && Members.Contains(Tracker);
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id,"id"); Scribe_Values.Look(ref Coordinator,"coordinator");
            Scribe_Values.Look(ref Tracker,"tracker"); Scribe_Values.Look(ref Goal,"goal"); Scribe_Values.Look(ref Until,"until");
            Scribe_Values.Look(ref Lead,"lead",IntVec3.Invalid); Scribe_Values.Look(ref EnemyId,"enemyId",-1); Scribe_Values.Look(ref LeadTick,"leadTick",-1);
            Scribe_Collections.Look(ref Members,"members",LookMode.Value);
        }
    }
    public sealed class TacticalPursuitProposal : IExposable
    {
        public string Id, From, To;
        public TacticalPursuitAssignment Assignment;
        public TacticalPursuitProposal Copy() => new TacticalPursuitProposal { Id=Id,From=From,To=To,Assignment=Assignment.Copy() };
        public void ExposeData()
        { Scribe_Values.Look(ref Id,"id"); Scribe_Values.Look(ref From,"from"); Scribe_Values.Look(ref To,"to"); Scribe_Deep.Look(ref Assignment,"assignment"); }
    }
    // Only delivered messages activate roles. It contains no Pawn or live peer
    // state and is independent of the existing frontage/breach agreement.
    public sealed class TacticalPursuitDivision : IExposable
    {
        public TacticalPursuitAssignment Assignment;
        public TacticalPursuitProposal Proposal;
        public TacticalAgreementStage Stage;
        public string LastConfirmed;
        public int Started;
        public TacticalPursuitRole Role(string own, IntVec3 goal, int tick) => Assignment?.Valid(goal,tick)==true
            && Assignment.Members.Contains(own) ? own==Assignment.Tracker ? TacticalPursuitRole.Pursue : TacticalPursuitRole.Clear
                : TacticalPursuitRole.Independent;
        public bool Expire(IntVec3 goal, int tick)
        {
            bool changed=false;
            if (Assignment!=null && !Assignment.Valid(goal,tick)) { Assignment=null; changed=true; }
            if (Proposal!=null && (!Proposal.Assignment.Valid(goal,tick) || tick-Started>=TacticalCommunicationPolicy.PacketLife)) CancelProposal();
            return changed;
        }
        public void CancelProposal() { Proposal=null; Stage=TacticalAgreementStage.None; }
        public bool Begin(TacticalPursuitProposal proposal, string own, IntVec3 goal, int tick)
        {
            Expire(goal,tick);
            if (Proposal!=null || proposal?.From!=own || !Adoptable(proposal.Assignment,own,goal,tick)) return false;
            Proposal=proposal.Copy(); Stage=TacticalAgreementStage.Offered; Started=tick; return true;
        }
        private bool Adoptable(TacticalPursuitAssignment agenda, string own, IntVec3 goal, int tick) =>
            agenda?.Valid(goal,tick)==true && agenda.Members.Contains(own)
            && (Assignment?.Valid(goal,tick)!=true || string.CompareOrdinal(agenda.Coordinator,Assignment.Coordinator)<=0)
            && (Assignment?.Id!=agenda.Id || Assignment.Tracker==agenda.Tracker);
        public bool Receive(TacticalMessageKind kind, TacticalPursuitProposal proposal, string sender, string own, IntVec3 goal, int tick)
        {
            Expire(goal,tick);
            if (proposal?.Assignment==null || string.IsNullOrEmpty(proposal.Id) || proposal.From==proposal.To
                || !proposal.Assignment.Members.Contains(proposal.From) || !proposal.Assignment.Members.Contains(proposal.To)
                || !Adoptable(proposal.Assignment,own,goal,tick)) return false;
            if (kind==TacticalMessageKind.PursuitOffer)
            {
                if (own!=proposal.To || sender!=proposal.From || Proposal!=null && Proposal.Id!=proposal.Id) return false;
                if (LastConfirmed==proposal.Id) return true;
                if (Proposal?.Id==proposal.Id) return true;
                Proposal=proposal.Copy(); Stage=TacticalAgreementStage.Accepted; Started=tick; return true;
            }
            if (kind==TacticalMessageKind.PursuitConfirm && own==proposal.To && sender==proposal.From
                && LastConfirmed==proposal.Id && Assignment?.Id==proposal.Assignment.Id && Proposal==null) return true;
            if (Proposal?.Id!=proposal.Id) return false;
            if (kind==TacticalMessageKind.PursuitAccept && own==proposal.From && sender==proposal.To
                && (Stage==TacticalAgreementStage.Offered || Stage==TacticalAgreementStage.Agreed))
            { Assignment=proposal.Assignment.Copy(); LastConfirmed=proposal.Id; Stage=TacticalAgreementStage.Agreed; return true; }
            if (kind==TacticalMessageKind.PursuitConfirm && own==proposal.To && sender==proposal.From && Stage==TacticalAgreementStage.Accepted)
            { Assignment=proposal.Assignment.Copy(); LastConfirmed=proposal.Id; CancelProposal(); return true; }
            if (kind==TacticalMessageKind.PursuitReceipt && own==proposal.From && sender==proposal.To && Stage==TacticalAgreementStage.Agreed)
            { CancelProposal(); return true; }
            return false;
        }
        public static TacticalPursuitProposal Build(string from, string to, IntVec3 goal, int tick,
            TacticalPursuitAssignment a, TacticalPursuitAssignment b, int leadA, int leadB)
        {
            if (a?.Valid(goal,tick)!=true) a=null;
            if (b?.Valid(goal,tick)!=true) b=null;
            if (a==null && b==null && leadA<0 && leadB<0) return null;
            var winner=a==null ? b : b==null ? a : string.CompareOrdinal(a.Coordinator,b.Coordinator)<=0 ? a : b;
            var agenda=winner?.Copy() ?? new TacticalPursuitAssignment { Id=from+":"+to+":"+tick,
                Coordinator=string.CompareOrdinal(from,to)<0 ? from : to,
                Tracker=leadA>=leadB ? from : to, Goal=goal, Until=tick+TacticalContactMemory.RetentionTicks };
            foreach (var previous in new[]{a,b})
                if (previous!=null) foreach (string member in previous.Members)
                    if (!agenda.Members.Contains(member) && agenda.Members.Count<TacticalPursuitAssignment.MemberLimit) agenda.Members.Add(member);
            foreach (string member in new[]{from,to})
            {
                if (agenda.Members.Contains(member)) continue;
                if (agenda.Members.Count==TacticalPursuitAssignment.MemberLimit) return null;
                agenda.Members.Add(member);
            }
            // Clearing units may relay the assignment but cannot indefinitely
            // renew an absent/dead tracker. Renewal requires speaking to it.
            if ((agenda.Tracker==from || agenda.Tracker==to) && Math.Max(leadA,leadB)>=0)
                agenda.Until=tick+TacticalContactMemory.RetentionTicks;
            return new TacticalPursuitProposal {Id=agenda.Id+":"+from+":"+to+":"+tick,From=from,To=to,Assignment=agenda};
        }
        public void ExposeData()
        {
            Scribe_Deep.Look(ref Assignment,"assignment"); Scribe_Deep.Look(ref Proposal,"proposal");
            Scribe_Values.Look(ref Stage,"stage"); Scribe_Values.Look(ref Started,"started");
            Scribe_Values.Look(ref LastConfirmed,"lastConfirmed");
        }
    }
}
