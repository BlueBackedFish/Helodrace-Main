using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Helodrace;
using Helodrace.Tactics;
using Verse;

internal static class TacticalFieldBoundTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string reason) { checks++; if(!value) throw new Exception(reason); }
        var bound=new TacticalFieldBound { Epoch=100,Leg=1,Started=200,Split=true,First=new(){2,3} };
        TacticalBoundPacket Packet(int tick) => new() { Epoch=100,Leg=1,Step=bound.Step,Channel=TacticalChannel.Voice,
            From=10,To=20,Sent=tick,Due=tick+40,Ready=2,Needed=2 };
        bool Deliver(int tick) => bound.Deliver(Packet(tick-40),TacticalChannel.Voice,10,20,tick);
        var offered=Packet(200);
        Check(!bound.Deliver(offered,TacticalChannel.Voice,10,20,239),"A queued order cannot start before actual delivery delay.");
        Check(!bound.Deliver(offered,TacticalChannel.None,10,20,240),"Lost voice contact does not deliver an offer.");
        Check(!bound.Deliver(offered,TacticalChannel.Radio,10,20,240),"Changing channels invalidates an in-flight packet.");
        Check(!bound.Deliver(offered,TacticalChannel.Voice,11,20,240),"A changed liaison cannot receive the previous operator's packet.");
        Check(bound.Deliver(offered,TacticalChannel.Voice,10,20,240) && bound.Step==TacticalBoundStep.Accept,"Offer delivery requests acceptance, not movement.");
        Check(!TacticalFieldBoundPolicy.Moving(bound,true) && !TacticalFieldBoundPolicy.Moving(bound,false),"Neither group starts from the offer alone.");
        Check(!bound.Deliver(offered,TacticalChannel.Voice,10,20,250),"Duplicate old stages cannot replay a transition.");
        Check(Deliver(290) && bound.Step==TacticalBoundStep.Start,"Delivered acceptance still needs the start command.");
        Check(!TacticalFieldBoundPolicy.Moving(bound,true),"Acknowledgement alone does not release the mover.");
        Check(Deliver(340) && TacticalFieldBoundPolicy.Moving(bound,true) && !TacticalFieldBoundPolicy.Moving(bound,false),"Only the agreed first group moves after start delivery.");
        Check(!Deliver(390),"An arbitrary packet cannot progress a moving team without its local readiness event.");
        bound.Step=TacticalBoundStep.Ready;
        var wrong=Packet(400); wrong.Leg=0;
        Check(!bound.Deliver(wrong,TacticalChannel.Voice,10,20,440),"Previous leg readiness cannot authorize this leg.");
        wrong=Packet(400); wrong.Epoch=99;
        Check(!bound.Deliver(wrong,TacticalChannel.Voice,10,20,440),"Previous mission readiness cannot authorize this mission.");
        wrong=Packet(400); wrong.Ready=1;
        Check(!bound.Deliver(wrong,TacticalChannel.Voice,10,20,440),"A covered minority cannot release the support group.");
        wrong=Packet(400); wrong.Needed=0;
        Check(!bound.Deliver(wrong,TacticalChannel.Voice,10,20,440),"Zero-sized readiness must not fabricate support.");
        wrong=Packet(400);
        Check(!bound.Deliver(wrong,TacticalChannel.Voice,10,20,580),"Expired snapshots cannot hand over support.");
        Check(Deliver(450) && bound.Step==TacticalBoundStep.Follow && bound.ReportReady==2,"Delivered readiness records a bounded value snapshot.");
        Check(!TacticalFieldBoundPolicy.Moving(bound,false),"The second group still waits for its follow command.");
        Check(!Deliver(630),"Stale ready evidence cannot release a later follow command.");
        bound.Step=TacticalBoundStep.Ready;
        Check(Deliver(640) && Deliver(690) && TacticalFieldBoundPolicy.Moving(bound,false) && !TacticalFieldBoundPolicy.Moving(bound,true),
            "Refreshed report and delivered follow exchange movement and cover ownership.");
        bound.Step=TacticalBoundStep.Complete;
        Check(Deliver(740) && bound.Step==TacticalBoundStep.Done,"Completion also requires a delivered report.");
        var solo=new TacticalFieldBound { Epoch=100,Leg=1,Step=TacticalBoundStep.Follow,ReportAt=700,Split=false };
        var ack=Packet(700); ack.Step=TacticalBoundStep.Follow;
        Check(solo.Deliver(ack,TacticalChannel.Voice,10,20,740) && solo.Step==TacticalBoundStep.Done,
            "An undersized unsplit party finishes under main-body cover without a fabricated second group.");
        Check(TacticalFieldBoundPolicy.ShotGrace(.5f,1,0)==180 && TacticalFieldBoundPolicy.ShotGrace(4,3,90)==600,
            "Slow single-shot/long burst support receives weapon-aware grace beyond the old 300 ticks.");
        Check(TacticalFieldBoundPolicy.ShotGrace(30,30,10000)==1200,"Weapon readiness grace stays bounded.");
        Check(TacticalFieldPolicy.Ready(4,3) && !TacticalFieldPolicy.Ready(4,2),"A bounded straggler does not require a full-party barrier.");
        Check(AccessTools.Method(typeof(CoverUtility),"CalculateOverallBlockChance",new[]{typeof(LocalTargetInfo),typeof(IntVec3),typeof(Map)})!=null,
            "Native preparation geometry accepts a cell target and observed shooter position, without a synthetic pawn.");
        CheckMissionHandover(Check);
        CheckActorOwnership(Check);
        RoundTrip(bound,Check);
        Console.WriteLine("PASS: "+checks+" W4 delayed cover handover, channel loss, stale/replayed snapshots, weapon cadence and native persistence checks.");
    }
    private static void CheckActorOwnership(Action<bool,string> check)
    {
        var first=new TacticalMemberCommand { Pawn=new Pawn { thingIDNumber=10 } };
        var second=new TacticalMemberCommand { Pawn=new Pawn { thingIDNumber=20 } };
        var bound=new TacticalFieldBound { Split=true,First=new(){0} };
        var field=new TacticalFieldResponse { Maneuver=new TacticalFieldManeuver { Phase=TacticalManeuverPhase.Moving,Bound=bound } };
        field.FireGroup.Add(0); field.FireGroup.Add(1);
        var command=new TacticalSquadCommand { FieldResponse=field }; command.Members.Add(first); command.Members.Add(second);
        var service=new MapComponent_TacticalCommands(null);
        var moving=AccessTools.Method(typeof(MapComponent_TacticalCommands),"FieldHammerMoving");
        bool Moves(TacticalMemberCommand member)=>(bool)moving.Invoke(service,new object[]{command,member});
        check(!Moves(first) && !Moves(second),"Production actor jobs obey the offer hold.");
        bound.Step=TacticalBoundStep.Moving;
        check(Moves(first) && !Moves(second),"Production actor jobs release only the first committed group.");
        bound.Step=TacticalBoundStep.Ready;
        check(Moves(first) && !Moves(second),"Unarrived first-group stragglers can finish while their ready report is queued.");
        bound.Step=TacticalBoundStep.Following;
        check(!Moves(first) && Moves(second),"Production actor ownership exchanges only after the follow delivery.");
        bound.Step=TacticalBoundStep.Done;
        check(!Moves(first) && !Moves(second),"Completed bounds hold both groups until a new agreement.");
    }
    private static void CheckMissionHandover(Action<bool,string> check)
    {
        var group=new TacticalFieldFormation { Coordinator="A",Members=new(){"A","B"} };
        group.Sectors["A"]=new(){At=1000,Available=12,Ready=2};
        group.Sectors["B"]=new(){At=1000,Available=12,Ready=2};
        var old=new TacticalFieldMission { Coordinator="A",Assignee="B",Supporter="A",Stage=TacticalFieldMissionStage.Finished };
        check(TacticalFieldCoordinationPolicy.Handover(group,old,1000),"Delivered useful flank allows the old support squad to become the mover.");
        group.Sectors["B"].At=820;
        check(!TacticalFieldCoordinationPolicy.Handover(group,old,1000),"An old remote ready report cannot authorize another squad's movement.");
        group.Sectors["B"].At=1000;
        var offer=new TacticalFieldMission { Coordinator="A",Assignee="A",Supporter="B",Epoch=100,Number=2,Started=1000,Until=2000,
            Origin=new IntVec3(20,0,20),Focus=new IntVec3(40,0,40),Side=1 };
        var own=offer.Copy(); TacticalFieldMission peer=null;
        check(offer.Fresh(1000),"A coordinator can move only when a distinct explicit supporter exists.");
        check(TacticalFieldCoordinationPolicy.Receive(ref peer,offer,"A","B",group,true,1010),"The new support squad must accept the coordinator's movement.");
        check(TacticalFieldCoordinationPolicy.Receive(ref own,peer,"B","A",group,true,1020)
            && own.Stage==TacticalFieldMissionStage.Committed,"Support acceptance alone does not start the coordinator's move.");
        check(TacticalFieldCoordinationPolicy.Receive(ref peer,own,"A","B",group,true,1030)
            && peer.Stage==TacticalFieldMissionStage.Active,"The supporter receives the start commitment and confirms it.");
        check(TacticalFieldCoordinationPolicy.Receive(ref own,peer,"B","A",group,true,1040)
            && own.Stage==TacticalFieldMissionStage.Active,"Only delivered confirmation releases self-coordinator movement.");
        own.Stage=TacticalFieldMissionStage.Finished;
        check(!TacticalFieldCoordinationPolicy.Handover(group,own,1050),"A completed self-coordinator move waits for the supporter's final acknowledgement.");
        check(TacticalFieldCoordinationPolicy.Receive(ref peer,own,"A","B",group,true,1060),"Support receives movement completion.");
        check(TacticalFieldCoordinationPolicy.Receive(ref own,peer,"B","A",group,true,1070) && own.FinishAcknowledged,
            "Delivered completion acknowledgement allows the next alternating squad assignment.");
        check(TacticalFieldCoordinationPolicy.Handover(group,own,1070),"The new supporter and previous mover can exchange roles again.");
        var altered=offer.Copy(); altered.Supporter="C";
        check(!own.Matches(altered),"Support identity belongs to the immutable mission tuple.");
    }
    private static void RoundTrip(TacticalFieldBound bound,Action<bool,string> check)
    {
        var cache=(IDictionary)AccessTools.Field(typeof(GenTypes),"typeCache").GetValue(null);
        var key=typeof(GenTypes).GetNestedType("TypeCacheKey",BindingFlags.NonPublic);
        foreach(var t in new[]{typeof(TacticalFieldBound),typeof(TacticalBoundPacket),typeof(CompSharpshooterWeapon),typeof(SharpshooterPreparationClock)})
            cache[Activator.CreateInstance(key,new object[]{t.FullName,null})]=t;
        string path=Path.Combine(Path.GetTempPath(),"Helodrace-field-bound-"+Guid.NewGuid()+".xml");
        bool old=ScribeMetaHeaderUtility.modListChanged; ScribeMetaHeaderUtility.modListChanged=true;
        try
        {
            bound.Pending=new TacticalBoundPacket { Epoch=100,Leg=2,Step=TacticalBoundStep.Start,From=10,To=20,Channel=TacticalChannel.Radio,Due=999 };
            var mode=new CompSharpshooterWeapon { altModeActive=true,aiModeOwned=true };
            mode.Preparation.Update(true,false,new IntVec3(10,0,10),900);
            Scribe.saver.InitSaving(path,"bound"); Scribe_Deep.Look(ref bound,"state");
            Scribe.EnterNode("mode"); mode.PostExposeData(); Scribe.ExitNode(); Scribe.saver.FinalizeSaving();
            TacticalFieldBound loaded=null; var loadedMode=new CompSharpshooterWeapon();
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded,"state");
            Scribe.EnterNode("mode"); loadedMode.PostExposeData(); Scribe.ExitNode(); Scribe.loader.ForceStop();
            check(loaded.Step==bound.Step && loaded.Split && loaded.First.Count==2 && loaded.ReportAt==bound.ReportAt,"Save restores the committed leg and delivered readiness.");
            check(loaded.Pending.Due==999 && loaded.Pending.Channel==TacticalChannel.Radio && loaded.Pending.Leg==2,"Save restores the bounded pending packet without pawn/job objects.");
            check(loadedMode.altModeActive && loadedMode.aiModeOwned,"Save retains AI mode ownership for safe movement cleanup.");
            check(loadedMode.Preparation.StartedAt==900 && loadedMode.Preparation.Remaining(1000,120)==20,
                "Save retains elapsed stationary preparation without a waiting job.");
        }
        finally { Scribe.ForceStop(); ScribeMetaHeaderUtility.modListChanged=old; if(File.Exists(path)) File.Delete(path); }
    }
}
