using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    [Flags] public enum TacticalCqbNotice { None=0, Post=1, Passage=2, Opening=4, Observation=8, Launch=16, Return=32, Failure=64, Safety=128 }
    public sealed class TacticalCqbSignals : IExposable
    {
        public int Version=-1, Due=int.MaxValue;
        public TacticalCqbNotice Mask;
        public bool Record(int expected,int version,TacticalCqbNotice notice,int tick)
        {
            if(version!=expected || notice==TacticalCqbNotice.None) return false;
            if(Version!=version) { Version=version; Mask=TacticalCqbNotice.None; Due=int.MaxValue; }
            Mask|=notice; Due=Math.Min(Due,tick); return true;
        }
        public TacticalCqbNotice Take(int version,int tick)
        {
            if(Version!=version) { Mask=TacticalCqbNotice.None; Due=int.MaxValue; return TacticalCqbNotice.None; }
            if(tick<Due) return TacticalCqbNotice.None;
            var mask=Mask; Mask=TacticalCqbNotice.None; Due=int.MaxValue; return mask;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Version,"version",-1); Scribe_Values.Look(ref Due,"due",int.MaxValue); Scribe_Values.Look(ref Mask,"mask");
        }
    }
    public sealed partial class TacticalSquadCommand
    {
        public TacticalCqbSignals CqbSignals=new TacticalCqbSignals();
        internal bool CqbQueued, CqbUrgent;
    }
    public sealed partial class GameComponent_TacticalCommands
    {
        private readonly Queue<TacticalSquadCommand> cqbEvents=new Queue<TacticalSquadCommand>();
        public long CqbNotices, CqbCheapTransitions, CqbStaleNotices;
        internal void QueueCqb(TacticalSquadCommand command,TacticalCqbNotice notice,int version,int due)
        {
            if(command.Terminal || !command.CqbSignals.Record(command.PlanVersion,version,notice,due)) { CqbStaleNotices++; return; }
            CqbNotices++;
            if(!command.CqbQueued) { command.CqbQueued=true; cqbEvents.Enqueue(command); }
        }
        private void PumpCqbEvents(int tick)
        {
            int count=Math.Min(4,cqbEvents.Count);
            for(int i=0;i<count;i++)
            {
                if(!WorkBudget.CanTransition(tick)) break;
                var command=cqbEvents.Dequeue(); command.CqbQueued=false;
                if(command.Terminal || command.Owner?.map?.Disposed!=false) continue;
                if(command.CqbSignals.Due>tick)
                { command.CqbQueued=true; cqbEvents.Enqueue(command); continue; }
                var notice=command.CqbSignals.Take(command.PlanVersion,tick);
                if(notice==TacticalCqbNotice.None) { CqbStaleNotices++; continue; }
                long started=Stopwatch.GetTimestamp();
                CqbCheapTransitions+=command.Owner.ApplyCqbTransitions(command,notice,tick);
                WorkBudget.Account(tick,Stopwatch.GetTimestamp()-started);
            }
        }
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        internal void CqbNotice(TacticalSquadCommand command,TacticalCqbNotice notice,int version,int due=-1)
        {
            if(command.Plan==null || command.Defensive) return;
            if(due<0) due=GenTicks.TicksGame;
            Current.Game?.GetComponent<GameComponent_TacticalCommands>()?.QueueCqb(command,notice,version,due);
            command.Due=Math.Min(command.Due,Math.Max(GenTicks.TicksGame,due));
            if(command.CqbParent!=null) command.CqbParent.Due=Math.Min(command.CqbParent.Due,command.Due);
        }
        internal int ApplyCqbTransitions(TacticalSquadCommand command,TacticalCqbNotice notice,int tick)
        {
            if(command.Plan==null || command.Phase==TacticalCommandPhase.Returning || command.ContactResponse!=null
                || command.FieldResponse!=null || command.MedicalCare!=null || command.FocusFire!=null
                || command.CqbParent?.CqbCancelRequested==true || SiblingCqbHazard(command)) return 0;
            var active=new List<TacticalMemberCommand>(command.Members.Count);
            foreach(var m in command.Members)
                if(m.Pawn?.Spawned==true && m.Pawn.Map==map && !m.Pawn.Dead && !m.Pawn.Downed && !m.Pawn.InMentalState) active.Add(m);
            if(active.Count==0 || PauseForExplosiveEvasion(command,tick)) return 0;
            int changed=0;
            for(int step=0;step<3;step++)
            {
                var old=command.Phase;
                if((old==TacticalCommandPhase.Stack || old==TacticalCommandPhase.Breach) && command.ChargeAction==null)
                {
                    if(OpeningUsable(command.Plan) && (old!=TacticalCommandPhase.Stack || CooperationReady(command,tick)))
                    { RememberOpening(command.Plan); command.Phase=TacticalCommandPhase.Observe; command.PhaseStarted=tick; }
                    else if(old==TacticalCommandPhase.Stack && CqbBreachReady(command,active) && CooperationReady(command,tick))
                    { command.Phase=TacticalCommandPhase.Breach; command.PhaseStarted=tick; }
                }
                else if(old==TacticalCommandPhase.Observe && command.OpeningAction?.ObservationDone==true)
                {
                    var action=command.OpeningAction;
                    bool returned=action.Observer==null || action.Observer.Dead || action.Observer.Downed
                        || active.Exists(m=>m.Pawn==action.Observer && AtPost(m));
                    if(returned && OpeningUsable(command.Plan)) { command.Phase=TacticalCommandPhase.Support; action.SupportStarted=tick; }
                }
                else if(old==TacticalCommandPhase.Support && command.OpeningAction!=null && !command.OpeningAction.ThrowIssued)
                {
                    var action=command.OpeningAction;
                    if(!TacticalCqbDoctrine.Support(TacticalHighCqbPolicy.High(command),action.Outdoors,action.RoomCells,
                        action.Enemy.IsValid,action.KnownHelodPrisoner,TacticalEntryCombatPolicy.Engaged(command.EntryCombatUntil,tick)))
                        SkipOpeningSupport(command,TacticalSupportSkip.Policy);
                }
                else if(old==TacticalCommandPhase.BlastWait && command.OpeningAction?.Launched==true && !command.ReplanAfterSupport)
                {
                    if(!SupportEffectsPending(command,tick) && !ChargeEffectsPending(command,tick)) command.Phase=TacticalCommandPhase.Enter;
                    else if(command.OpeningAction.SettledAt>=0)
                        CqbNotice(command,TacticalCqbNotice.Safety,command.PlanVersion,command.OpeningAction.SettledAt+TacticalOpeningPolicy.SettleTicks);
                }
                if(command.Phase==old) break;
                changed++;
            }
            // A child notice is independent of the heavy parent rotation, but
            // still goes through the normal global Advance/job/plan budgets.
            command.CqbUrgent=true; command.Due=Math.Min(command.Due,tick);
            if(command.CqbParent!=null) command.CqbParent.Due=Math.Min(command.CqbParent.Due,tick);
            return changed;
        }
    }
}
