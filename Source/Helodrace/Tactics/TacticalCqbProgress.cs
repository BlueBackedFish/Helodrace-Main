using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalCqbWait { None, Plan, Roles, Cooperation, Tool, Observation, ObserverReturn, SupportCandidates,
        ThrowPreparation, FriendlySafety, BlastSafety, EntryTransit, CombatQuiet, JobBudget, PathBudget }
    public sealed partial class TacticalMemberCommand
    {
        public int CqbExecutionVersion, EntryVersion;
        public bool LaneCleared, PostReady;
    }
    public sealed partial class TacticalSquadCommand
    {
        private TacticalLocalPlan plan;
        public int PlanVersion;
        public TacticalLocalPlan Plan
        {
            get => plan;
            set { if (!ReferenceEquals(plan,value)) { plan=value; PlanVersion++; } }
        }
        public TacticalCqbWait CqbWait;
        public int CqbRequiredReady, CqbRequiredCount, CqbCrossedCount, CqbPostCount;
    }
    public static class TacticalCqbProgressPolicy
    {
        public static List<int> Core(TacticalLocalPlan plan,int breacher,IEnumerable<int> live)
        {
            var available=new List<int>();
            foreach(int i in live) if(i>=0 && i<plan.Stack.Count && TacticalStackSecurity.Participant(plan,i)) available.Add(i);
            var result=new List<int>(3);
            if(available.Contains(breacher)) result.Add(breacher);
            while(result.Count<3 && result.Count<available.Count)
            {
                int best=-1,distance=int.MaxValue;
                foreach(int i in available)
                    if(!result.Contains(i) && plan.Stack[i].DistanceToSquared(plan.Opening)<distance)
                    { best=i; distance=plan.Stack[i].DistanceToSquared(plan.Opening); }
                if(best<0) break; result.Add(best);
            }
            return result;
        }
        public static bool Physical(TacticalMemberCommand member,int version) => member.EntryVersion==version && member.LaneCleared;
        public static bool RealFailure(JobCondition condition,bool safety) => !safety
            && (condition==JobCondition.Incompletable || condition==JobCondition.Errored);
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        internal int CqbVersion(Pawn pawn,Job job) => byPawn.TryGetValue(pawn,out var command)
            && command.Members.Find(m=>m.Pawn==pawn)?.Job==job ? command.PlanVersion : -1;
        internal void PostArrived(Pawn pawn,Job job,int version)
        {
            if(!byPawn.TryGetValue(pawn,out var command) || command.Plan==null || command.PlanVersion!=version) return;
            var member=command.Members.Find(m=>m.Pawn==pawn);
            if(member?.Job!=job) return;
            member.PostReady=true;
            int index=command.Members.IndexOf(member);
            TacticalStackSecurity.Arrived(command.Plan,index,pawn.Position);
            if(!command.Plan.RetainedOutside.Contains(index) && (command.Plan.Direct || member.Passed && member.Crossed))
                EntryLaneCleared(pawn,job,version);
            Wake(pawn);
        }
        internal void EntryLaneCleared(Pawn pawn,Job job,int version)
        {
            if(!byPawn.TryGetValue(pawn,out var command) || command.PlanVersion!=version || command.Plan==null) return;
            var member=command.Members.Find(m=>m.Pawn==pawn);
            if(member?.Job!=job) return;
            member.EntryVersion=version; member.LaneCleared=true; member.Entered=member.EverEntered=true;
            Wake(pawn);
        }
        private void RefreshCqbProgress(TacticalSquadCommand command,List<TacticalMemberCommand> active)
        {
            command.CqbCrossedCount=command.CqbPostCount=0;
            foreach(var member in active)
            {
                member.PostReady=AtPost(member);
                if(member.PostReady) command.CqbPostCount++;
                if(TacticalCqbProgressPolicy.Physical(member,command.PlanVersion))
                { member.Entered=member.EverEntered=true; command.CqbCrossedCount++; }
            }
        }
        private bool CqbBreachReady(TacticalSquadCommand command,List<TacticalMemberCommand> active)
        {
            var live=new List<int>(active.Count); foreach(var member in active) live.Add(command.Members.IndexOf(member));
            int worker=command.Members.FindIndex(m=>m.Pawn==command.Breacher);
            var core=TacticalCqbProgressPolicy.Core(command.Plan,worker,live);
            command.CqbRequiredCount=core.Count; command.CqbRequiredReady=0;
            foreach(int index in core)
                if(AtPost(command.Members[index]) && command.Members[index].Pawn.Position==command.Plan.Stack[index]) command.CqbRequiredReady++;
            bool ready=core.Count>0 && command.CqbRequiredReady==core.Count;
            if(!ready) command.CqbWait=TacticalCqbWait.Roles;
            return ready;
        }
        private bool CqbEntryReady(TacticalSquadCommand command,List<TacticalMemberCommand> active,int tick)
        {
            int participants=0;
            foreach(var member in active)
            {
                int index=command.Members.IndexOf(member);
                if(command.Plan.RetainedOutside.Contains(index)) continue;
                // Unstarted stragglers become explicit trailing guards after a
                // bounded wait. A committed portal transit is never abandoned.
                if(!TacticalStackSecurity.ReadyToEnter(command.Plan,index) && tick-command.PhaseStarted>=1200)
                {
                    command.Plan.RetainedOutside.Add(index);
                    command.Plan.Positions[index]=command.Plan.Stack[index];
                    continue;
                }
                participants++;
                IntVec3 delta=member.Pawn.Position-command.Plan.Opening;
                bool clear=TacticalCqbProgressPolicy.Physical(member,command.PlanVersion)
                    && (command.Plan.Direct || delta.x*command.Plan.Inward.x+delta.z*command.Plan.Inward.z>=command.Plan.Depth)
                    && TacticalPostFirePolicy.CanStop(command.Plan,member.Pawn.Position);
                if(!clear) { command.CqbWait=TacticalCqbWait.EntryTransit; return false; }
            }
            return participants>0;
        }
    }
}
