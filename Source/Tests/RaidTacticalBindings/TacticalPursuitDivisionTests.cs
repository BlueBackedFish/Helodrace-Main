using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactics;
using Verse;

internal static class TacticalPursuitDivisionTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string reason) { checks++; if(!value) throw new Exception(reason); }
        var goal=new IntVec3(50,0,50);
        var a=new TacticalPursuitDivision(); var b=new TacticalPursuitDivision();
        var proposal=TacticalPursuitDivision.Build("A","B",goal,100,null,null,90,95);
        Check(proposal.Assignment.Tracker=="B" && a.Begin(proposal,"A",goal,100),"The actual recent observer is selected as the single tracker.");
        Check(a.Role("A",goal,110)==TacticalPursuitRole.Independent && b.Role("B",goal,110)==TacticalPursuitRole.Independent,
            "Creating a proposal or only having the same enemy memory cannot silently split roles.");
        Check(b.Receive(TacticalMessageKind.PursuitOffer,proposal,"A","B",goal,140)
            && b.Role("B",goal,140)==TacticalPursuitRole.Independent,"An offer is not delivered confirmation.");
        Check(a.Receive(TacticalMessageKind.PursuitAccept,proposal,"B","A",goal,180)
            && a.Role("A",goal,180)==TacticalPursuitRole.Clear && b.Role("B",goal,180)==TacticalPursuitRole.Independent,
            "Accept only changes its actual recipient; the other unit remains isolated.");
        Check(b.Receive(TacticalMessageKind.PursuitConfirm,proposal,"A","B",goal,220)
            && b.Role("B",goal,220)==TacticalPursuitRole.Pursue,"The tracker requires its own confirmed packet.");
        Check(a.Receive(TacticalMessageKind.PursuitReceipt,proposal,"B","A",goal,260) && a.Proposal==null,
            "Receipt closes the retry conversation without discarding the agreed task.");
        Check(b.Receive(TacticalMessageKind.PursuitOffer,proposal,"A","B",goal,265) && b.Proposal==null
            && b.Role("B",goal,265)==TacticalPursuitRole.Pursue,"A delayed duplicate offer must not reopen and block a completed conversation.");
        var wrongMission=proposal.Copy();wrongMission.Assignment.Goal+=IntVec3.North;
        Check(!b.Receive(TacticalMessageKind.PursuitOffer,proposal,"X","B",goal,270)
            && !b.Receive(TacticalMessageKind.PursuitConfirm,wrongMission,"A","B",goal,270),
            "Wrong senders and other building missions cannot adopt the assignment.");
        var c=new TacticalPursuitDivision();
        var join=TacticalPursuitDivision.Build("A","C",goal,300,a.Assignment,null,90,299);
        Check(join.Assignment.Tracker=="B" && join.Assignment.Until==a.Assignment.Until && a.Begin(join,"A",goal,300),
            "A clearing unit can relay the original tracker, but cannot renew it without contact with that tracker.");
        Handshake(a,c,join,goal,300,Check);
        Check(new[]{a.Role("A",goal,500),b.Role("B",goal,500),c.Role("C",goal,500)}.Count(r=>r==TacticalPursuitRole.Pursue)==1,
            "Three communicating squads keep one tracker and two building-clearing squads: "
                +a.Role("A",goal,500)+"/"+b.Role("B",goal,500)+"/"+c.Role("C",goal,500));
        var d=new TacticalPursuitDivision(); var e=new TacticalPursuitDivision();
        var independent=TacticalPursuitDivision.Build("D","E",goal,400,null,null,390,-1);
        Check(d.Begin(independent,"D",goal,400),"A disconnected communication cluster forms its own local agreement.");
        Handshake(d,e,independent,goal,400,Check);
        var merge=TacticalPursuitDivision.Build("C","D",goal,550,c.Assignment,d.Assignment,299,390);
        Check(merge.Assignment.Tracker=="B" && c.Begin(merge,"C",goal,550),"Connected clusters preserve the winning known tracker.");
        Handshake(c,d,merge,goal,550,Check);
        Check(d.Role("D",goal,700)==TacticalPursuitRole.Clear && b.Role("B",goal,700)==TacticalPursuitRole.Pursue,
            "The second group's tracker stops pursuing only after its own delivered role conversation.");
        Check(!d.Receive(TacticalMessageKind.PursuitConfirm,independent,"D","E",goal,710),
            "An old losing-group confirmation cannot resurrect a replaced role.");
        var pending=new TacticalPursuitDivision();
        Check(pending.Receive(TacticalMessageKind.PursuitOffer,proposal,"A","B",goal,150),"A lost-confirmation fixture accepts the proposal.");
        pending.Expire(goal,750);
        Check(pending.Proposal==null && pending.Role("B",goal,750)==TacticalPursuitRole.Independent,
            "Dropped confirmations time out to independent behavior rather than create an invisible assignment.");
        Check(a.Expire(goal,1900) && a.Role("A",goal,1900)==TacticalPursuitRole.Independent,
            "Broken contact eventually expires the pre-agreed role; it never silently extends a dead tracker.");
        Check(TacticalPursuitDivision.Build("A","B",goal,0,null,null,-1,-1)==null,
            "No observed indoor lead means no new pursuit split.");
        var full=proposal.Assignment.Copy();full.Members=Enumerable.Range(0,TacticalPursuitAssignment.MemberLimit-2)
            .Select(i=>"member"+i).Concat(new[]{"A","B"}).ToList();
        Check(TacticalPursuitDivision.Build("A","extra",goal,700,full,null,90,-1)==null,
            "A full bounded group cannot fabricate membership for an uncommunicated seventeenth squad.");
        var pursuit=new TacticalCqbPursuit(); pursuit.Observe(17,new IntVec3(11,0,10),100);
        var target=new TacticalRoomFrontier {Opening=new IntVec3(10,0,10),Inward=IntVec3.East};
        var other=new TacticalRoomFrontier {Opening=new IntVec3(20,0,20),Inward=IntVec3.North};
        Check(pursuit.Priority(target,110)==0 && pursuit.Priority(target,110,follow:false)==3
            && pursuit.Priority(other,110,follow:false)==2,"Clearing units prefer other rooms while trackers prefer the lead room.");
        pursuit.RecordEntered(new HashSet<IntVec3>{pursuit.Target},new[]{other},120);
        Check(pursuit.Priority(other,130)==1 && pursuit.Priority(other,130,follow:false)==3,
            "Only the assigned tracker prioritizes the sighted room's connected exits.");
        CheckQueue(goal,proposal,Check);
        CheckAssignedLead(goal,Check);
        Console.WriteLine($"PASS: {checks} communication-confirmed split, third-unit relay, group merge, packet loss, task expiry and queue checks.");
    }
    private static void Handshake(TacticalPursuitDivision sender,TacticalPursuitDivision recipient,TacticalPursuitProposal p,
        IntVec3 goal,int start,Action<bool,string> check)
    {
        int tick=start+40;
        check(recipient.Receive(TacticalMessageKind.PursuitOffer,p,p.From,p.To,goal,tick),"Delivered relay offer must be accepted.");
        check(sender.Receive(TacticalMessageKind.PursuitAccept,p,p.To,p.From,goal,tick+40),"Delivered relay acceptance must reach its proposer.");
        check(recipient.Receive(TacticalMessageKind.PursuitConfirm,p,p.From,p.To,goal,tick+80),"Delivered relay confirmation must activate its own role.");
        check(sender.Receive(TacticalMessageKind.PursuitReceipt,p,p.To,p.From,goal,tick+120),"Delivered relay receipt must close retries.");
    }
    private static void CheckQueue(IntVec3 goal,TacticalPursuitProposal proposal,Action<bool,string> check)
    {
        var doctrine=(DoctrineDef)RuntimeHelpers.GetUninitializedObject(typeof(DoctrineDef));doctrine.voiceReportTicks=40;
        var group=new CombatGroup {id="A"};var org=new CombatOrganization {id="org",doctrine=doctrine,rootGroups=new List<CombatGroup>{group}};
        org.RestoreTreeLinks();
        var from=new TacticalSquadCommand {Id="A",Goal=goal};from.Link.Unit=RaidTacticalUnit.ForGroup(group);from.Link.Liaison=new Pawn {thingIDNumber=1};
        var to=new TacticalSquadCommand {Id="B",Goal=goal};to.Link.Liaison=new Pawn {thingIDNumber=2};
        var network=new TacticalCommunications();var queue=AccessTools.Method(typeof(TacticalCommunications),"QueuePursuit");
        void Send(TacticalChannel channel)=>queue.Invoke(network,new object[]{from,to,TacticalMessageKind.PursuitOffer,proposal,channel,100});
        Send(TacticalChannel.None);check(network.PendingCount==0,"No real communication channel means no role packet.");
        Send(TacticalChannel.Voice);Send(TacticalChannel.Voice);
        var packet=((List<TacticalMessage>)AccessTools.Field(typeof(TacticalCommunications),"pending").GetValue(network)).Single();
        check(network.PendingCount==1 && packet.Due==140 && packet.FromPawn==1 && packet.ToPawn==2
            && packet.PursuitProposal!=proposal && packet.PursuitProposal.Assignment!=proposal.Assignment,
            "Role messages use bounded, coalesced, detached delayed packets with the real liaison endpoints.");
        int count=packet.PursuitProposal.Assignment.Members.Count;proposal.Assignment.Members.Add("uncommunicated");
        check(packet.PursuitProposal.Assignment.Members.Count==count,"An in-flight role report cannot mutate with its source.");
        var command=new TacticalSquadCommand {Id="B",Goal=goal,Phase=TacticalCommandPhase.Enter,Plan=new TacticalLocalPlan(),
            OpeningAction=new TacticalOpeningAction {Launched=true,EffectsCleared=false}};
        command.Link.PursuitDivision.Assignment=packet.PursuitProposal.Assignment.Copy();
        var plan=command.Plan;var action=command.OpeningAction;
        AccessTools.Method(typeof(TacticalCommunications),"RefreshPursuitDivision").Invoke(null,new object[]{command,goal.x+3000});
        check(command.Plan==plan && command.OpeningAction==action && command.Phase==TacticalCommandPhase.Enter,
            "Expiry must only invalidate future priorities, never cancel ingress or explosive safety.");
    }
    private static void CheckAssignedLead(IntVec3 goal,Action<bool,string> check)
    {
        var lead=new IntVec3(11,0,10);var local=new IntVec3(30,0,30);
        var command=new TacticalSquadCommand {Id="clearer",Goal=goal};
        command.Link.PursuitDivision.Assignment=new TacticalPursuitAssignment {Id="group",Coordinator="tracker",Tracker="tracker",
            Goal=goal,Until=1900,Lead=lead,EnemyId=17,LeadTick=100,Members=new List<string>{"tracker","clearer"}};
        command.Contacts.Pursuit.Observe(88,lead,200);command.Contacts.Pursuit.RoomReady=true;command.Contacts.Pursuit.Room.Add(lead);
        command.Contacts.Memory.Remember(88,local,local,false,true,500,command.Id);
        var owner=new MapComponent_TacticalCommands(null);
        command.Owner=owner;
        check((bool)AccessTools.Method(owner.GetType(),"PrepareCqbPursuit").Invoke(owner,new object[]{command,600,true})
            && command.Contacts.Pursuit.Target==lead && command.Contacts.Pursuit.EnemyId==17
            && command.Contacts.Memory.Entries.Single().Position==local,
            "Actual clearer preparation must use the communicated tracker lead while preserving newer local combat memory.");
        var exit=new TacticalRoomFrontier {Opening=new IntVec3(12,0,10),Inward=IntVec3.East};
        owner.SharedSecured.Record(new TacticalSecuredReport {Origin="tracker",Sequence=1,SeenTick=650,Goal=lead,
            Opening=new IntVec3(10,0,10),Inward=IntVec3.East,Depth=1,PortalUsable=true,
            Cells=new List<IntVec3>{lead},Frontiers=new List<TacticalRoomFrontier>{exit}});
        check((bool)AccessTools.Method(owner.GetType(),"PrepareCqbPursuit").Invoke(owner,new object[]{command,650,true})
            && command.Contacts.Pursuit.Entered && command.Contacts.Pursuit.Priority(exit,650,follow:false)==3
            && command.SecuredCells.Count==0,
            "After the tracker clears its room, a communicated clearer avoids its shared known exits without faking its own physical entry.");
        var own=AccessTools.Method(typeof(TacticalCommunications),"OwnObservation");
        bool Own(string origin)=>(bool)own.Invoke(null,new object[]{"A",origin});
        check(Own("A") && Own("A::cqb:1") && !Own("AB::cqb:1") && !Own("B") && !Own(null),
            "A HIGH fireteam's own observation can be reported by its squad without forwarding another squad's hidden information.");
    }
}
