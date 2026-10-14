using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;

internal static class TacticalOpeningTests
{
    internal static void Run()
    {
        if (TacticalOpeningPolicy.WantsSupport(false, 16, false)
            || !TacticalOpeningPolicy.WantsSupport(false, 16, true)
            || !TacticalOpeningPolicy.WantsSupport(false, 17, false)
            || !TacticalOpeningPolicy.WantsSupport(true, 1, false)) throw new Exception("Room/contact support policy failed.");
        var cells = new HashSet<IntVec3>();
        for (int x = 0; x < 4; x++) for (int z = 0; z < 4; z++) cells.Add(new IntVec3(x,0,z));
        if (TacticalOpeningPolicy.ClassifySize(IntVec3.Zero, cells.Contains) != 16) throw new Exception("Small room must not overflow.");
        cells.Add(new IntVec3(4,0,0)); cells.Add(new IntVec3(5,0,0));
        if (TacticalOpeningPolicy.ClassifySize(IntVec3.Zero, cells.Contains) != 17) throw new Exception("Large room classification must cap at 17.");
        int reads = 0;
        if (TacticalOpeningPolicy.ClassifySize(IntVec3.Zero, cell => { reads++; return true; }) != 17 || reads > 69)
            throw new Exception("An unbounded outdoor region must still use bounded probes.");
        if (TacticalOpeningPolicy.FarEnough(IntVec3.Zero, IntVec3.North)
            || !TacticalOpeningPolicy.FarEnough(IntVec3.Zero, IntVec3.North * 2)) throw new Exception("Opening throw distance failed.");
        if (!TacticalOpeningPolicy.EffectsPending(true, false, true, 0, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, true, true, 0, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, false, false, 0, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, false, true, -1, 100)
            || !TacticalOpeningPolicy.EffectsPending(false, false, true, 100, 129)
            || TacticalOpeningPolicy.EffectsPending(false, false, true, 100, 130))
            throw new Exception("Entry must wait for projectile, explosion damage, return and settling.");
        if (TacticalOpeningPolicy.ReleaseTicks != 18 || TacticalOpeningPolicy.ObservationTicks != 90)
            throw new Exception("Release/observation timings changed.");
        foreach(IntVec3 forward in GenAdj.CardinalDirections)
        {
            var plan=new TacticalLocalPlan {Opening=new IntVec3(20,0,20),Inward=forward,StackSide=1};
            IntVec3 side=new IntVec3(-forward.z,0,forward.x),post=plan.Outside+side*3;
            IntVec3 peek=plan.Outside+side,other=plan.Outside-side;
            if(TacticalOpeningPolicy.FindThrowPosition(plan,post,_=>true,c=>c==peek)!=peek)
                throw new Exception("A wall-covered stack unable to throw must use the adjacent outside peek slot, not skip the grenade.");
            if(TacticalOpeningPolicy.FindThrowPosition(plan,post,c=>c!=peek,c=>c==peek||c==other)!=other)
                throw new Exception("An occupied preferred peek slot must use the opposite available side.");
            if(TacticalOpeningPolicy.FindThrowPosition(plan,post,_=>true,c=>c==post)!=post)
                throw new Exception("An already valid stack throw must retain its original position.");
            if(TacticalOpeningPolicy.FindThrowPosition(plan,post,_=>false,_=>true).IsValid
                || TacticalOpeningPolicy.FindThrowPosition(plan,post,_=>true,c=>c==plan.Opening).IsValid)
                throw new Exception("Throw positioning must never force occupied cells or stand inside the breach channel.");
        }
        CheckTargetFallback();
        CheckContactOwnership();
        CheckSelectionSlices();
        Console.WriteLine("PASS: bounded room/target selection, unseen fallback, admitted-only markers, deferred candidates, preparation ownership and projectile/explosion/return safety.");
    }
    private static void CheckTargetFallback()
    {
        foreach(IntVec3 forward in GenAdj.CardinalDirections)
        {
            var plan=new TacticalLocalPlan {Opening=new IntVec3(20,0,20),Inward=forward,Depth=2};
            IntVec3 side=new IntVec3(-forward.z,0,forward.x);
            IntVec3 unreachable=plan.InnerOpening+forward*3+side*2;
            IntVec3 safe=plan.InnerOpening+forward*4;
            var targets=new List<IntVec3>();
            TacticalOpeningPolicy.CollectSupportTargets(plan,IntVec3.Invalid,_=>true,c=>c==unreachable,targets);
            if(targets.Count!=9||targets[0]!=unreachable||!targets.Contains(safe))
                throw new Exception("Unseen priority must retain all bounded alternatives, not commit to the first unthrowable corner.");
            IntVec3 selected=IntVec3.Invalid;
            foreach(var target in targets)
                if(TacticalOpeningPolicy.FindThrowPosition(plan,plan.Outside+side,_=>true,_=>target==safe).IsValid)
                {selected=target;break;}
            if(selected!=safe)throw new Exception("An unreachable preferred target must fall back to a throw-capable alternative.");
            TacticalOpeningPolicy.CollectSupportTargets(plan,safe,_=>true,_=>true,targets);
            if(targets.Count!=1||targets[0]!=safe)throw new Exception("Observed enemy location must retain sole target priority.");
            TacticalOpeningPolicy.CollectSupportTargets(plan,plan.InnerOpening+forward,_=>true,_=>false,targets);
            if(targets.Count!=1||targets[0]!=plan.InnerOpening+forward*2)
                throw new Exception("A sighted door/channel enemy must aim just beyond it at the minimum safe opening distance, not discard support.");
        }
        var action=new TacticalOpeningAction {Target=new IntVec3(10,0,10)};
        if(action.ShowThrow)throw new Exception("A proposed target without an admitted grenade job must not be shown as a committed throw.");
        action.ThrowIssued=true;
        if(!action.ShowThrow)throw new Exception("An admitted throw must display its target through preparation.");
        action.SupportSkip=TacticalSupportSkip.Interrupted;
        if(action.ShowThrow)throw new Exception("An interrupted prelaunch throw must not retain an active marker.");
        action.Launched=true;
        if(!action.ShowThrow)throw new Exception("A live grenade must retain its marker even if a routine was interrupted.");
        action.EffectsCleared=true;
        if(action.ShowThrow)throw new Exception("Resolved explosives must not retain an active throw marker during entry.");
    }
    private static void CheckContactOwnership()
    {
        var owner = new MapComponent_TacticalCommands(null);
        var pawn = new Pawn {Position=new IntVec3(19,0,20)};
        var member = new TacticalMemberCommand {Pawn=pawn};
        var command = new TacticalSquadCommand {Plan=new TacticalLocalPlan {Opening=new IntVec3(20,0,20),Inward=IntVec3.East},
            Phase=TacticalCommandPhase.Breach,OpeningAction=new TacticalOpeningAction {SupportCursor=10,Target=new IntVec3(23,0,22)}};
        command.Members.Add(member);
        command.OpeningAction.SupportTargets.Add(command.OpeningAction.Target);
        IntVec3 enemy=new IntVec3(24,0,20), other=new IntVec3(25,0,22), rear=new IntVec3(17,0,20);
        if(!TacticalOpeningPolicy.RememberContact(command,enemy,12,100)||!command.OpeningAction.ObservationDone
            ||command.OpeningAction.Enemy!=enemy||command.OpeningAction.SupportTargets.Count!=0||command.OpeningAction.SupportCursor!=0)
            throw new Exception("A genuine breach contact must become one completed opening observation and replace any blind selection.");
        if(TacticalOpeningPolicy.RememberContact(command,other,13,101)||command.OpeningAction.Enemy!=enemy
            ||TacticalOpeningPolicy.OwnsContact(command,rear)||!TacticalOpeningPolicy.OwnsContact(command,other))
            throw new Exception("The opening owns frontal room contacts without tracking later enemy positions or suppressing rear emergencies.");
        command.Phase=TacticalCommandPhase.Support;
        command.Contacts.Memory.Remember(12,enemy,enemy,false,true,100);
        var active=new List<TacticalMemberCommand>{member};
        bool Advance(string name) => (bool)AccessTools.Method(typeof(MapComponent_TacticalCommands),name).Invoke(owner,new object[]{command,active,101});
        if(Advance("AdvanceFocusFire")||Advance("AdvanceFieldResponse")||Advance("RespondToContacts")
            ||command.ContactResponse!=null||command.OpeningAction.Enemy!=enemy)
            throw new Exception("Focus, field or close-contact response must not capture a completed opening observation before its retreat/throw.");
        command.ContactResponse=new TacticalContactResponse {First=enemy,FirstId=12,Started=99,LastSeen=100};
        if(Advance("RespondToContacts")||command.ContactResponse!=null||command.OpeningAction.Enemy!=enemy)
            throw new Exception("A previous frontal firefight must yield to the retained opening contact without erasing it.");
        if((bool)AccessTools.Method(typeof(MapComponent_TacticalCommands),"RefreshSharedPassage").Invoke(owner,new object[]{command,101}))
            throw new Exception("A shared room-clear report cannot bypass a newly observed enemy's opening action.");
        // Synthetic pawns do not have native race/equality state.
        var byPawn=new Dictionary<Pawn,TacticalSquadCommand>(ReferenceEqualityComparer.Instance);
        AccessTools.Field(typeof(MapComponent_TacticalCommands),"byPawn").SetValue(owner,byPawn);
        byPawn[pawn]=command;
        bool OwnShot(IntVec3 cell)=>(bool)AccessTools.Method(typeof(MapComponent_TacticalCommands),"OpeningOwnsShot").Invoke(owner,new object[]{pawn,cell});
        if(!OwnShot(enemy)||OwnShot(rear))throw new Exception("The observed room's guard fire must stop while rear-directed security remains available.");
        command.ContactResponse=new TacticalContactResponse {DirectAttack=true};
        if(OwnShot(enemy))throw new Exception("An actual close-attack emergency must retain its immediate defense.");
        command.ContactResponse=null;command.Phase=TacticalCommandPhase.Enter;
        if(TacticalOpeningPolicy.OwnsContact(command,enemy)||OwnShot(enemy))
            throw new Exception("Opening withdrawal must not suppress combat after entry.");
    }
    private static void CheckSelectionSlices()
    {
        var owner=new MapComponent_TacticalCommands(null);
        var command=new TacticalSquadCommand {Phase=TacticalCommandPhase.Support,Plan=new TacticalLocalPlan(),
            OpeningAction=new TacticalOpeningAction {ObservationDone=true,RoomCells=17,
                SupportRoomReady=true,
                SupportTargets=new List<IntVec3>{new IntVec3(10,0,10),new IntVec3(11,0,10)},SupportActors=new List<int>{0,1,2,3}}};
        // Forced jobs must never be replaced. They also provide a genuine held
        // formation while probing deferred selection without a Unity map.
        for(int i=0;i<12;i++)
        {
            var pawn=new Pawn {Position=new IntVec3(i,0,0)};
            command.Plan.Stack.Add(pawn.Position);
            pawn.jobs=(Verse.AI.Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Verse.AI.Pawn_JobTracker));
            var def=(JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef));def.defName="HD_NewTacticalPost";
            var job=new Verse.AI.Job {def=def,playerForced=true,targetA=pawn.Position};
            AccessTools.Field(typeof(Verse.AI.Pawn_JobTracker),"curJob").SetValue(pawn.jobs,job);
            pawn.jobs.curDriver=new JobDriver_TacticalPost {AtPost=true,job=job};
            command.Members.Add(new TacticalMemberCommand {Pawn=pawn,Job=job});
        }
        var oldGame=Current.Game;
        try
        {
            var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            game.components=new List<GameComponent>{new GameComponent_TacticalCommands(game)}; Current.Game=game;
            var advance=AccessTools.Method(owner.GetType(),"AdvanceOpeningAction");
            advance.Invoke(owner,new object[]{command,command.Members,100});
            if(command.OpeningAction.SupportCursor!=4||command.Phase!=TacticalCommandPhase.Support||command.Due!=101)
                throw new Exception("Support selection must yield after four carriers and resume next tick, without entering early.");
            advance.Invoke(owner,new object[]{command,command.Members,100});
            if(command.OpeningAction.SupportCursor!=4)throw new Exception("Selection cannot consume a second plan slice in the same tick.");
            for(int tick=101;tick<=102;tick++)advance.Invoke(owner,new object[]{command,command.Members,tick});
            if(command.Phase!=TacticalCommandPhase.Support||command.OpeningAction.SupportCursor!=12)
                throw new Exception("An exhausted first preference pass must still test the alternative targets before entering.");
            advance.Invoke(owner,new object[]{command,command.Members,103});
            if(command.Phase!=TacticalCommandPhase.Enter||command.OpeningAction.SupportCursor!=16||command.OpeningAction.ThrowIssued
                ||command.OpeningAction.Target.IsValid||command.OpeningAction.ShowThrow
                ||command.Members.Exists(m=>m.Pawn.CurJob!=m.Job))
                throw new Exception("Both preference passes must finish once, preserving forced jobs and avoiding perpetual Support retries.");
            // Job identity owns preparation even before its driver initializes.
            var thrower=command.Members[0];
            var definition=(JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef));definition.defName="HD_NewTacticalThrow";
            var throwing=new Verse.AI.Job {def=definition,targetA=new IntVec3(10,0,10),playerForced=true};
            AccessTools.Field(typeof(Verse.AI.Pawn_JobTracker),"curJob").SetValue(thrower.Pawn.jobs,throwing);
            thrower.Pawn.jobs.curDriver=null;thrower.Job=throwing;
            command.Phase=TacticalCommandPhase.Support;
            command.OpeningAction=new TacticalOpeningAction {ThrowIssued=true,Thrower=thrower.Pawn,Target=throwing.targetA.Cell};
            advance.Invoke(owner,new object[]{command,command.Members,112});
            if(command.Phase!=TacticalCommandPhase.Support||thrower.Pawn.CurJob!=throwing)
                throw new Exception("An admitted throw with driver initialization pending must keep the squad out of entry.");
            var oldPost=command.Members[1].Job;
            var forced=new Verse.AI.Job {def=oldPost.def,playerForced=true,targetA=thrower.Pawn.Position};
            AccessTools.Field(typeof(Verse.AI.Pawn_JobTracker),"curJob").SetValue(thrower.Pawn.jobs,forced);
            thrower.Pawn.jobs.curDriver=null;thrower.Job=null;
            advance.Invoke(owner,new object[]{command,command.Members,113});
            if(command.Phase!=TacticalCommandPhase.Enter||command.OpeningAction.Target.IsValid
                ||command.OpeningAction.SupportSkip!=TacticalSupportSkip.Interrupted||command.OpeningAction.ShowThrow)
                throw new Exception("A genuine prelaunch interruption may enter, but must clear its target and record the reason.");
        }
        finally {Current.Game=oldGame;}
    }
}
