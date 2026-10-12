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
        CheckSelectionSlices();
        Console.WriteLine("PASS: bounded room classification, small-room/contact exception, throw distance, projectile/explosion/return safety.");
    }
    private static void CheckSelectionSlices()
    {
        var owner=new MapComponent_TacticalCommands(null);
        var command=new TacticalSquadCommand {Phase=TacticalCommandPhase.Support,Plan=new TacticalLocalPlan(),
            OpeningAction=new TacticalOpeningAction {ObservationDone=true,RoomCells=17,Target=new IntVec3(10,0,10)}};
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
            pawn.jobs.curDriver=new JobDriver_TacticalPost {AtPost=true};
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
            for(int tick=101;tick<=105;tick++)advance.Invoke(owner,new object[]{command,command.Members,tick});
            if(command.Phase!=TacticalCommandPhase.Enter||command.OpeningAction.SupportCursor!=24||command.OpeningAction.ThrowIssued
                ||command.Members.Exists(m=>m.Pawn.CurJob!=m.Job))
                throw new Exception("Both preference passes must finish once, preserving forced jobs and avoiding perpetual Support retries.");
        }
        finally {Current.Game=oldGame;}
    }
}
