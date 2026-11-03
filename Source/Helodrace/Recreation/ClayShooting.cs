using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RimWorld;
using Verse;
using Verse.AI;
using GameVector3=UnityEngine.Vector3;

namespace Helodrace;

public sealed class Building_ClayTrap:Building
{
    public const int LaneLength=14;
    public bool SafeLane(Pawn user)
    {
        if(!Spawned||Faction!=Faction.OfPlayer)return false;
        var dir=Rotation.FacingCell;
        for(int step=1;step<=LaneLength;step++)
            foreach(var cell in GenRadial.RadialCellsAround(Position+dir*step,1.5f,true))
                if(!cell.InBounds(Map)||cell.Roofed(Map)||(cell.GetEdifice(Map)!=null&&cell.GetEdifice(Map)!=this)
                    ||cell.GetThingList(Map).OfType<Pawn>().Any(p=>p!=user&&!p.Dead))return false;
        return true;
    }
    public bool CanPlay(Pawn pawn)=>ShootingRecreation.CanShoot(pawn)&&SafeLane(pawn);
    public override string GetInspectString()=>base.GetInspectString()+"\n"+"HD_ClayTrap_Lane".Translate(LaneLength);
}
public sealed class Thing_ClayPigeon:Thing
{
    private IntVec3 origin,direction;
    private int age;
    public void Launch(IntVec3 start,IntVec3 facing){origin=start;direction=facing;age=0;}
    private Vector3 FlightPosition
    {
        get
        {
            var p=new Vector3(origin.x+.5f,0,origin.z+.5f)+new Vector3(direction.x,0,direction.z)*(age*.035f);
            p.Y=AltitudeLayer.Projectile.AltitudeFor()+.2f+MathF.Sin(MathF.PI*age/240f)*.35f;return p;
        }
    }
    // Actual Verse draw-position contract; all flight math stays in .NET vectors.
    public override GameVector3 DrawPos=>FlightPosition.ToGame();
    public override int UpdateRateTicks=>1;
    protected override void Tick()
    {
        base.Tick();age++;var p=FlightPosition;var cell=new IntVec3((int)MathF.Floor(p.X),0,(int)MathF.Floor(p.Z));
        if(age>=240||!cell.InBounds(Map)){Destroy();return;}Position=cell;
    }
    public override void ExposeData()
    {base.ExposeData();Scribe_Values.Look(ref origin,"flightOrigin");Scribe_Values.Look(ref direction,"flightDirection");Scribe_Values.Look(ref age,"flightAge");}
}
public sealed class JoyGiver_ClayShooting:JoyGiver_InteractBuildingInteractionCell
{
    protected override bool CanInteractWith(Pawn pawn,Thing thing,bool inBed)
        =>!inBed&&thing is Building_ClayTrap trap&&trap.CanPlay(pawn)&&base.CanInteractWith(pawn,thing,inBed);
    protected override Job TryGivePlayJob(Pawn pawn,Thing thing)
        =>JobMaker.MakeJob(def.jobDef,thing,thing.InteractionCell,pawn.equipment.Primary);
}
public sealed class JobDriver_ClayShooting:JobDriver,IRecreationRound
{
    private RecreationRound round=new();
    public RecreationRound Round=>round;
    private Thing_ClayPigeon pigeon;
    private int elapsed,nextLaunch;
    private bool castRequested;
    private Building_ClayTrap Trap=>job.targetA.Thing as Building_ClayTrap;
    public override bool TryMakePreToilReservations(bool errorOnFailed)
        =>pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed)&&pawn.Reserve(job.targetB,job,1,-1,null,errorOnFailed);
    public override void ExposeData()
    {
        base.ExposeData();Scribe_Deep.Look(ref round,"round");round??=new();Scribe_References.Look(ref pigeon,"pigeon");
        Scribe_Values.Look(ref elapsed,"elapsed");Scribe_Values.Look(ref nextLaunch,"nextLaunch");Scribe_Values.Look(ref castRequested,"castRequested");
    }
    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);
        AddFailCondition(()=>Trap==null||!ShootingRecreation.CanShoot(pawn)||pawn.equipment.Primary!=job.targetC.Thing||!Trap.SafeLane(pawn));
        AddFinishAction(_=>{round.Cancel();if(pigeon?.Spawned==true)pigeon.Destroy();pawn.stances?.CancelBusyStanceSoft();});
        yield return Toils_Goto.GotoCell(TargetIndex.B,PathEndMode.OnCell);
        var play=ToilMaker.MakeToil("ClayShooting");play.defaultCompleteMode=ToilCompleteMode.Never;
        play.tickAction=()=>
        {
            elapsed++;pawn.rotationTracker.FaceCell(Trap.Position+Trap.Rotation.FacingCell*8);
            pawn.needs.joy?.GainJoy(.00013f,DefDatabase<JoyKindDef>.GetNamed("HD_TargetShooting"));
            if(elapsed>=3000||pawn.needs.joy?.CurLevel>=.99f){EndJobWith(JobCondition.Succeeded);return;}
            if(!round.active&&Find.TickManager.TicksGame>=nextLaunch)
            {
                var start=Trap.Position+Trap.Rotation.FacingCell*2;
                pigeon=(Thing_ClayPigeon)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_ClayPigeonFlying"));
                pigeon.Launch(start,Trap.Rotation.FacingCell);GenSpawn.Spawn(pigeon,start,Map);
                round.Begin(pigeon);castRequested=false;
            }
            if(!round.active)return;
            var verb=ShootingRecreation.VerbFor(pawn);
            if(!castRequested&&pigeon?.Spawned==true&&!(pawn.stances.curStance is Stance_Busy))
                castRequested=verb.TryStartCastOn(pigeon,surpriseAttack:false,canHitNonTargetPawns:false,preventFriendlyFire:true);
            if(round.Resolved||round.TimedOut||pigeon==null||pigeon.Destroyed)
            {
                pawn.needs.joy?.GainJoy(round.hit ? .025f : .01f,DefDatabase<JoyKindDef>.GetNamed("HD_TargetShooting"));
                round.Cancel();if(pigeon?.Spawned==true)pigeon.Destroy();nextLaunch=Find.TickManager.TicksGame+360;
            }
        };
        yield return play;
    }
}
