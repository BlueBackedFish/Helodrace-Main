using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalFacingTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string reason) { checks++; if(!value) throw new Exception(reason); }
        var pawn=new Pawn {kindDef=(PawnKindDef)RuntimeHelpers.GetUninitializedObject(typeof(PawnKindDef))};
        pawn.Position=new IntVec3(20,0,20);
        pawn.jobs=(Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
        pawn.rotationTracker=new Pawn_RotationTracker(pawn);
        var job=new Job {targetA=pawn.Position+IntVec3.South*10,targetB=pawn.Position+IntVec3.West*10};
        AccessTools.Field(typeof(Pawn_JobTracker),"curJob").SetValue(pawn.jobs,job);
        var actor=new JobDriver_TacticalActor {pawn=pawn,job=job};
        pawn.jobs.curDriver=actor;
        var toils=((IEnumerable<Toil>)AccessTools.Method(actor.GetType(),"MakeNewToils").Invoke(actor,null)).ToList();
        AccessTools.Field(typeof(JobDriver),"toils").SetValue(actor,toils);
        AccessTools.Field(typeof(JobDriver),"curToilIndex").SetValue(actor,0);
        Check(toils.Count==1 && !pawn.jobs.HandlingFacing && actor.rotateToFace==TargetIndex.B,
            "A continuing CQB actor leaves rotation to native movement/aiming and keeps B as its stopped watch direction.");
        // Exercise the native path-facing operation without Unity's stun/DLC
        // bootstrap. Inspect UpdateRotation below to verify its dispatch order.
        var turn=AccessTools.Method(typeof(Pawn_RotationTracker),"FaceAdjacentCell");
        foreach(var direction in new[]{IntVec3.North,IntVec3.East,IntVec3.South,IntVec3.West})
        {
            turn.Invoke(pawn.rotationTracker,new object[]{pawn.Position+direction});
            Check(pawn.Rotation.FacingCell==direction,"Native path facing supports all four movement directions.");
        }
        var generator=new DynamicMethod("NativeFacingInstructions",typeof(void),Type.EmptyTypes).GetILGenerator();
        var code=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Pawn_RotationTracker),"UpdateRotation"),generator);
        int ownership=code.FindIndex(i=>i.Calls(AccessTools.PropertyGetter(typeof(Pawn_JobTracker),"HandlingFacing")));
        int stance=code.FindIndex(i=>i.opcode==OpCodes.Isinst && Equals(i.operand,typeof(Stance_Busy)));
        int moving=code.FindIndex(i=>i.Calls(AccessTools.PropertyGetter(typeof(Pawn_PathFollower),"Moving")));
        int pathTurn=code.FindIndex(i=>i.Calls(turn));
        int fallback=code.FindIndex(i=>i.Calls(AccessTools.Method(typeof(Pawn_RotationTracker),"FaceTarget")));
        Check(ownership>=0 && stance>ownership && moving>stance && pathTurn>moving && fallback>pathTurn,
            "Installed native rotation checks facing ownership, then firing stance, then path movement, then stationary job target.");

        // Field shelter/flank/return jobs use native Goto with only target A.
        // B is intentionally invalid: native Goto interprets B as a fixed look
        // target while walking, not tactical metadata.
        job.targetB=LocalTargetInfo.Invalid;
        var field=new JobDriver_Goto {pawn=pawn,job=job};
        pawn.jobs.curDriver=field;
        toils=((IEnumerable<Toil>)AccessTools.Method(field.GetType(),"MakeNewToils").Invoke(field,null)).ToList();
        AccessTools.Field(typeof(JobDriver),"toils").SetValue(field,toils);
        AccessTools.Field(typeof(JobDriver),"curToilIndex").SetValue(field,0);
        Check(!pawn.jobs.HandlingFacing,"Native field Goto does not own facing when no look target is supplied.");
        Console.WriteLine("PASS: "+checks+" CQB/field toil ownership, native cardinal turns and installed rotation-priority checks (not a live Unity simulation).");
    }
}
