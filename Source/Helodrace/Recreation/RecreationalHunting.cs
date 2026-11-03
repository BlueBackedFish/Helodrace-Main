using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace;

public static class RecreationalHunting
{
    public const int SearchTicks=5000,TrackingTicks=6000,StalkingTicks=1500;
    public const int MinimumPreparationTicks=SearchTicks+TrackingTicks+StalkingTicks;
    public const int FinishHuntTicks=6000;
    public const float SuccessJoy=.65f,FailureJoy=.55f;
    public static bool CanHunt(Pawn pawn,Pawn animal)
        =>ShootingRecreation.HasHuntingGun(pawn)&&animal?.Spawned==true&&animal.RaceProps.Animal
            &&animal.Faction==null&&!animal.Dead&&!animal.Downed&&!animal.InMentalState
            &&!animal.RaceProps.predator&&animal.RaceProps.manhunterOnDamageChance<=.01f
            &&animal.BodySize>=.15f&&animal.BodySize<=1.5f&&!animal.IsForbidden(pawn)
            &&!animal.Position.IsForbidden(pawn)&&!animal.Position.Fogged(pawn.Map)
            &&pawn.Map.designationManager.DesignationOn(animal,DesignationDefOf.Hunt)==null
            &&pawn.CanReserve(animal)&&pawn.CanReach(animal,PathEndMode.Touch,Danger.None);
}
public sealed class JoyGiver_RecreationalHunt:JoyGiver
{
    public override Job TryGiveJob(Pawn pawn)
    {
        if(pawn.needs?.joy==null||!ShootingRecreation.HasHuntingGun(pawn)||!JoyUtility.EnjoyableOutsideNow(pawn))return null;
        var animals=pawn.Map.mapPawns.AllPawnsSpawned.Where(a=>RecreationalHunting.CanHunt(pawn,a)
            &&a.Position.DistanceToSquared(pawn.Position)<=3600).OrderByDescending(a=>a.Position.DistanceToSquared(pawn.Position)).Take(8).ToList();
        if(!animals.TryRandomElement(out Pawn animal))return null;
        var job=JobMaker.MakeJob(def.jobDef,animal);job.SetTarget(TargetIndex.C,pawn.equipment.Primary);return job;
    }
}
public sealed class JobDriver_RecreationalHunt:JobDriver,IRecreationRound
{
    private RecreationRound round=new();
    public RecreationRound Round=>round;
    private bool firstHit,success,castRequested;
    private int stage,firstCastTick;
    private Pawn Animal=>job.targetA.Thing as Pawn;
    public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed);
    public override void ExposeData()
    {
        base.ExposeData();Scribe_Deep.Look(ref round,"round");round??=new();
        Scribe_Values.Look(ref firstHit,"firstHit");Scribe_Values.Look(ref success,"success");
        Scribe_Values.Look(ref castRequested,"castRequested");Scribe_Values.Look(ref stage,"stage");Scribe_Values.Look(ref firstCastTick,"firstCastTick");
    }
    public override string GetReport()=> (stage switch {0=>"HD_Hunt_Search",1=>"HD_Hunt_Track",2=>"HD_Hunt_Stalk",_=>"HD_Hunt_Shoot"}).Translate();
    protected override IEnumerable<Toil> MakeNewToils()
    {
        AddFailCondition(()=>Animal==null||pawn.equipment.Primary!=job.targetC.Thing||!ShootingRecreation.HasHuntingGun(pawn)
            ||(!Animal.Spawned&&!Animal.Dead)||Animal.Faction!=null||(!firstHit&&!round.fired&&Animal.Dead));
        AddFinishAction(_=>{round.Cancel();pawn.stances?.CancelBusyStanceSoft();});
        var search=Toils_General.Wait(RecreationalHunting.SearchTicks,TargetIndex.A);search.AddPreInitAction(()=>stage=0);yield return search;
        job.verbToUse=ShootingRecreation.VerbFor(pawn);
        yield return Toils_Combat.GotoCastPosition(TargetIndex.A,TargetIndex.None,true,.85f);
        var track=Toils_General.Wait(RecreationalHunting.TrackingTicks,TargetIndex.A);track.AddPreInitAction(()=>stage=1);yield return track;
        yield return Toils_Combat.GotoCastPosition(TargetIndex.A,TargetIndex.None,true,.85f);
        var stalk=Toils_General.Wait(RecreationalHunting.StalkingTicks,TargetIndex.A);stalk.AddPreInitAction(()=>stage=2);yield return stalk;
        var finish=ToilMaker.MakeToil("FinishRecreationalHunt");finish.defaultCompleteMode=ToilCompleteMode.Instant;
        finish.initAction=()=>
        {
            pawn.needs.joy?.GainJoy(success?RecreationalHunting.SuccessJoy:RecreationalHunting.FailureJoy,DefDatabase<JoyKindDef>.GetNamed("HD_OutdoorHunting"));
            if(success&&Animal?.Corpse!=null)Animal.Corpse.SetForbidden(false,false);
            Messages.Message((success?"HD_Hunt_Success":"HD_Hunt_Failure").Translate(pawn.LabelShort),pawn,MessageTypeDefOf.NeutralEvent,false);
        };
        var position=Toils_Combat.GotoCastPosition(TargetIndex.A,TargetIndex.None,true,.9f);
        position.AddPreInitAction(()=>{stage=3;if(firstHit&&Animal.Dead){success=true;JumpToToil(finish);}});
        yield return position;
        var fire=ToilMaker.MakeToil("RecreationalHuntOneRound");fire.defaultCompleteMode=ToilCompleteMode.Never;
        fire.initAction=()=>{stage=3;round.Begin(Animal);castRequested=false;if(firstCastTick==0)firstCastTick=Find.TickManager.TicksGame;};
        fire.tickAction=()=>
        {
            if(firstHit&&Animal.Dead){success=true;JumpToToil(finish);return;}
            if(Find.TickManager.TicksGame-firstCastTick>RecreationalHunting.FinishHuntTicks){success=false;JumpToToil(finish);return;}
            var verb=ShootingRecreation.VerbFor(pawn);job.verbToUse=verb;pawn.rotationTracker.FaceTarget(Animal);
            if(!castRequested&&!(pawn.stances.curStance is Stance_Busy))
                castRequested=verb.TryStartCastOn(Animal,surpriseAttack:false,canHitNonTargetPawns:false,preventFriendlyFire:true);
            if(!round.Resolved&&!round.TimedOut)return;
            if(!firstHit&&!round.hit){success=false;JumpToToil(finish);return;}
            firstHit=true;
            if(Animal.Dead){success=true;JumpToToil(finish);return;}
            round.Cancel();JumpToToil(position);
        };
        yield return fire;
        yield return finish;
    }
}
