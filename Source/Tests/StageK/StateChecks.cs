using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private int statePhase,stateShots,stateToil,stateTicksLeft;
    private float stateFuel;
    private static int stateNewTargets;
    private IntVec3 stateClayCell;
    private Pawn statePawn,stateAnimal;
    private Building_ClayTrap stateTrap;
    private bool stateLoadRequested;private static bool stateClimatePatched;
    public override void ExposeData()
    {
        base.ExposeData();Scribe_Values.Look(ref statePhase,"statePhase");Scribe_Values.Look(ref stateShots,"stateShots");
        Scribe_Values.Look(ref stateToil,"stateToil");Scribe_Values.Look(ref stateTicksLeft,"stateTicksLeft");Scribe_Values.Look(ref stateClayCell,"stateClayCell");
        Scribe_Values.Look(ref stateFuel,"stateFuel");
        Scribe_References.Look(ref statePawn,"statePawn");Scribe_References.Look(ref stateAnimal,"stateAnimal");Scribe_References.Look(ref stateTrap,"stateTrap");
    }
    private void ResumeState()
    {
        DebugSettings.noAnimals=false;DebugSettings.enableStoryteller=false;DebugSettings.enableRandomMentalStates=false;
        if(!stateClimatePatched)
        {
            var harmony=new Harmony("helodrace.tests.stagek.state");
            harmony.Patch(AccessTools.PropertyGetter(typeof(MapTemperature),nameof(MapTemperature.OutdoorTemp)),prefix:new HarmonyMethod(typeof(Checks),nameof(Climate)));
            harmony.Patch(AccessTools.Method(typeof(Thing_ClayPigeon),nameof(Thing_ClayPigeon.Launch)),postfix:new HarmonyMethod(typeof(Checks),nameof(StateTargetLaunch)));
            stateClimatePatched=true;
        }
        stateNewTargets=0;
        if(statePawn!=null)fixtures.Add(statePawn);if(stateAnimal!=null)fixtures.Add(stateAnimal);
        steps=(statePhase==0?InitialState():AfterStateLoad()).GetEnumerator();
    }
    private void SaveState(int phase)
    {statePhase=phase;GameDataSaveLoader.SaveGame("StageKRecreationState");stateLoadRequested=true;}
    private static void StateTargetLaunch(){stateNewTargets++;}
    private IEnumerable<int> InitialState()
    {
        foreach(var pawn in playMap.mapPawns.AllPawnsSpawned.ToList())
        {pawn.DeSpawn();Find.WorldPawns.PassToWorld(pawn,PawnDiscardDecideMode.KeepForever);}
        foreach(var cell in CellRect.CenteredOn(playMap.Center,28))
        {
            foreach(var t in cell.GetThingList(playMap).ToList())if(t.Spawned&&t.def.destroyable)t.Destroy();
            playMap.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);playMap.roofGrid.SetRoof(cell,null);playMap.fogGrid.Unfog(cell);
        }
        stateTrap=(Building_ClayTrap)ThingMaker.MakeThing(Def<ThingDef>("HD_ClayTrap"));stateTrap.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(stateTrap,playMap.Center,playMap,Rot4.North);
        stateTrap.GetComp<CompRefuelable>().Refuel(25);statePawn=Player(stateTrap.InteractionCell);Gun(statePawn,"HD_Gun_M1897_Weapon");
        var job=JobMaker.MakeJob(Def<JobDef>("HD_ClayShooting"),stateTrap,stateTrap.InteractionCell,statePawn.equipment.Primary);
        Check(statePawn.jobs.TryTakeOrderedJob(job),"State fixture starts real clay recreation");
        for(int tick=0;tick<2000;tick++)
        {
            if(statePawn.jobs.curDriver is JobDriver_ClayShooting driver&&driver.Round.fired&&!driver.Round.Resolved)
            {
                stateShots=(int)statePawn.records.GetValue(RecordDefOf.ShotsFired);stateClayCell=driver.Round.target.Position;stateFuel=stateTrap.GetComp<CompRefuelable>().Fuel;
                Check(driver.Round.target is Thing_ClayPigeon&&driver.Round.target.Spawned,"Save point contains a flying clay target and launched live round");
                SaveState(1);yield return 0;yield break;
            }
            yield return 1;
        }
        throw new Exception("No in-flight clay save point");
    }
    private IEnumerable<int> AfterStateLoad()
    {
        if(statePhase==1)
        {
            Check(statePawn.jobs.curDriver is JobDriver_ClayShooting,"Native save/load restores the active clay job");
            var driver=(JobDriver_ClayShooting)statePawn.jobs.curDriver;
            Check(driver.Round.active&&driver.Round.fired&&!driver.Round.Resolved&&driver.Round.target?.Spawned==true,"Round ownership and pending projectile references survive native load");
            Check(driver.Round.target.Position==stateClayCell&&statePawn.records.GetValue(RecordDefOf.ShotsFired)==stateShots,"Flying target location and pre-save shot count do not reset or duplicate");
            Check(Math.Abs(stateTrap.GetComp<CompRefuelable>().Fuel-stateFuel)<.01f,"Target stock is unchanged across native save/load");
            bool ended=false;driver.AddFinishAction(_=>ended=true);
            for(int elapsed=0;elapsed<3600&&!ended;elapsed+=60)yield return 60;
            Check(ended,"Loaded clay session completes normally");
            float spent=stateFuel-stateTrap.GetComp<CompRefuelable>().Fuel;
            // Targets are consumed on launch; a session may end before the final target is shot.
            Check(Math.Abs(spent-stateNewTargets)<.01f,"Loaded session consumes exactly one target per observed new launch; launches="+stateNewTargets+" spent="+spent);
            Check(statePawn.records.GetValue(RecordDefOf.ShotsFired)>=stateShots&&statePawn.records.GetValue(RecordDefOf.ShotsFired)<=25-stateTrap.GetComp<CompRefuelable>().Fuel,"Loaded session does not fire more cartridges than launched targets");
            // Keep a previously socialized test colonist deep-saved for relation references.
            statePawn.DeSpawn();Find.WorldPawns.PassToWorld(statePawn,PawnDiscardDecideMode.KeepForever);
            stateTrap.Destroy();stateTrap=null;
            statePawn=Player(playMap.Center+new IntVec3(-8,0,-8));Gun(statePawn,"HD_Gun_M1897_Weapon");
            stateAnimal=PawnGenerator.GeneratePawn(Def<PawnKindDef>("Deer"));GenSpawn.Spawn(stateAnimal,playMap.Center+new IntVec3(2,0,-1),playMap);fixtures.Add(stateAnimal);
            var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=99999;stateAnimal.jobs.StartJob(wait,JobCondition.InterruptForced);
            var hunt=JobMaker.MakeJob(Def<JobDef>("HD_RecreationalHunt"),stateAnimal);hunt.SetTarget(TargetIndex.C,statePawn.equipment.Primary);
            Check(statePawn.jobs.TryTakeOrderedJob(hunt),"State fixture starts the long hunting job");yield return 6500;
            Check(statePawn.jobs.curDriver is JobDriver_RecreationalHunt&&statePawn.records.GetValue(RecordDefOf.ShotsFired)==0,"Hunting save point is still tracking and has not fired early");
            stateToil=(int)AccessTools.Field(typeof(JobDriver),"curToilIndex").GetValue(statePawn.jobs.curDriver);stateTicksLeft=statePawn.jobs.curDriver.ticksLeftThisToil;
            Check(stateTicksLeft>0&&stateTicksLeft<RecreationalHunting.TrackingTicks,"Tracking has meaningful remaining work before save");
            SaveState(2);yield return 0;yield break;
        }
        Check(statePhase==2&&statePawn.jobs.curDriver is JobDriver_RecreationalHunt,"Native save/load restores the active hunting job");
        var loaded=statePawn.jobs.curDriver;
        Check((int)AccessTools.Field(typeof(JobDriver),"curToilIndex").GetValue(loaded)==stateToil&&Math.Abs(loaded.ticksLeftThisToil-stateTicksLeft)<=2,"Tracking stage and remaining delay are preserved, not restarted or skipped");
        yield return 1000;
        Check(statePawn.jobs.curDriver==loaded&&loaded.ticksLeftThisToil<stateTicksLeft-990&&statePawn.records.GetValue(RecordDefOf.ShotsFired)==0,"Loaded hunter resumes tracking without an early first shot");
        float joy=statePawn.needs.joy.CurLevel;statePawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        Check(statePawn.needs.joy.CurLevel<=joy+.001f,"Interrupted preparation grants no unearned outcome bonus");
    }
}
