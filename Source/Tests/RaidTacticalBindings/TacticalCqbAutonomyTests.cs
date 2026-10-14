using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalCqbAutonomyTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string reason) { checks++; if(!value) throw new Exception(reason); }
        var plan=new TacticalLocalPlan { Opening=new IntVec3(20,0,20) };
        for(int i=0;i<13;i++) plan.Stack.Add(plan.Opening+IntVec3.West*(i+1));
        plan.StackMembers.AddRange(new[]{0,1,2,3,4,5});
        var core=TacticalCqbProgressPolicy.Core(plan,5,Enumerable.Range(0,13));
        Check(core.SequenceEqual(new[]{5,0,1}),"The engineer and two nearby participants, not distant watches, gate breach readiness.");
        core=TacticalCqbProgressPolicy.Core(plan,5,new[]{0,1,2,3,4,8,9});
        Check(core.SequenceEqual(new[]{0,1,2}),"Unavailable personnel do not remain required roles.");
        var command=new TacticalSquadCommand { Plan=plan }; int version=command.PlanVersion;
        command.Plan=plan; Check(command.PlanVersion==version,"A held opening does not change generation on repeated use.");
        command.Plan=new TacticalLocalPlan(); Check(command.PlanVersion==version+1,"A new plan invalidates earlier readiness notices.");
        var member=new TacticalMemberCommand { EntryVersion=version,LaneCleared=true,Entered=true };
        Check(!TacticalCqbProgressPolicy.Physical(member,command.PlanVersion),"Old-room crossing cannot authorize the next opening.");
        member.EntryVersion=command.PlanVersion; member.Entered=member.EntryAssignmentDone=false;
        Check(TacticalCqbProgressPolicy.Physical(member,command.PlanVersion),"Cover displacement does not erase actual entry history.");
        member.EntryOwner="root";
        Check(!TacticalCqbProgressPolicy.Physical(member,command.PlanVersion,"child"),"Equal generation numbers in different HIGH execution owners cannot alias entry history.");
        member.EntryOwner=null;
        var job=new Job { targetA=new IntVec3(1,0,1),targetC=new IntVec3(3,0,3),targetQueueA=new(){new IntVec3(4,0,4)} };
        Check(new JobDriver_TacticalObserve {job=job}.PostCell==job.targetC.Cell,"Observer readiness uses its covered return, not its peek tile.");
        Check(new JobDriver_TacticalThrow {job=job}.PostCell==job.targetQueueA[0].Cell,"Thrower readiness uses the actual return tile.");
        Check(new JobDriver_TacticalIngress {job=job}.PostCell==job.targetC.Cell,"Ingress final readiness is distinct from the outside approach.");
        Check(!TacticalCqbProgressPolicy.RealFailure(JobCondition.InterruptForced,false)
            && !TacticalCqbProgressPolicy.RealFailure(JobCondition.InterruptOptional,false),"Normal role handover does not incur path-failure backoff.");
        Check(TacticalCqbProgressPolicy.RealFailure(JobCondition.Incompletable,false)
            && !TacticalCqbProgressPolicy.RealFailure(JobCondition.Incompletable,true),"Actual failure and explicit safety cancellation are distinct.");
        var service=new MapComponent_TacticalCommands(null);
        var pawn=new Pawn { thingIDNumber=123 }; member.Pawn=pawn; member.Job=job;
        pawn.health=(Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        AccessTools.Field(typeof(Pawn_HealthTracker),"healthState").SetValue(pawn.health,PawnHealthState.Mobile);
        command.Members.Add(member);
        AccessTools.Field(service.GetType(),"byPawn").SetValue(service,new Dictionary<Pawn,TacticalSquadCommand>(ReferenceEqualityComparer.Instance){[pawn]=command});
        var lane=AccessTools.Method(service.GetType(),"EntryLaneCleared");
        member.LaneCleared=false;
        lane.Invoke(service,new object[]{pawn,job,version,null});
        Check(!member.LaneCleared,"An old driver version cannot report the current room as entered.");
        lane.Invoke(service,new object[]{pawn,job,command.PlanVersion,null});
        Check(member.LaneCleared && member.Entered && member.EverEntered,"The owned actual lane event latches room entry independently of final-slot arrival.");
        var signals=new TacticalCqbSignals();
        Check(!signals.Record(4,3,TacticalCqbNotice.Post,100),"Late events from replaced plans are rejected before queuing.");
        Check(signals.Record(4,4,TacticalCqbNotice.Post,100) && signals.Record(4,4,TacticalCqbNotice.Passage,100),"Readiness notices merge for one plan.");
        Check(signals.Take(4,99)==TacticalCqbNotice.None,"Deadline events cannot advance early.");
        Check(signals.Take(4,100)==(TacticalCqbNotice.Post|TacticalCqbNotice.Passage)
            && signals.Take(4,100)==TacticalCqbNotice.None,"Merged events are consumed once without recursive replay.");
        signals.Record(4,4,TacticalCqbNotice.Safety,130);
        Check(signals.Take(5,130)==TacticalCqbNotice.None && signals.Mask==TacticalCqbNotice.None,"Replacing a plan clears its future safety notice.");
        Console.WriteLine("PASS: "+checks+" CQB role barriers, generation-bound progress and actual return-post checks.");
    }
}
