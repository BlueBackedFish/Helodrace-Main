using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private IEnumerator<int> steps;private int remaining;private Map playMap;private static int forcePattern;private readonly HashSet<Pawn> fixtures=new();private static int clayHits;private static bool safeProjectileFlags=true;
    private static void ClayHit(Bullet __instance,Thing hitThing,bool blockedByShield)
    {if(hitThing is Thing_ClayPigeon&&!blockedByShield)clayHits++;}
    private static void Flags(Thing launcher,ProjectileHitFlags hitFlags,bool preventFriendlyFire)
    {if(launcher is Pawn p&&p.jobs?.curDriver is JobDriver_ClayShooting)safeProjectileFlags &= preventFriendlyFire&&(hitFlags&ProjectileHitFlags.NonTargetPawns)==0;}
    private static bool Pattern(ref System.Numerics.Vector2 __result)
    {if(forcePattern==0)return true;__result=forcePattern<0?new(10,10):System.Numerics.Vector2.Zero;return false;}
    private static bool Aim(ref float __result){if(forcePattern==0)return true;__result=0;return false;}
    private static bool Climate(ref float __result){__result=21;return false;}
    public override void GameComponentUpdate()
    {
        if(stateLoadRequested&&Current.ProgramState==ProgramState.Playing)
        {stateLoadRequested=false;steps=null;GameDataSaveLoader.LoadGame("StageKRecreationState");return;}
        if(steps==null||Current.ProgramState!=ProgramState.Playing)return;
        Find.TickManager.Pause();
        try
        {
            if(remaining==0){if(!steps.MoveNext()){steps=null;Finish();return;}remaining=steps.Current;}
            for(int i=0;i<Math.Min(120,remaining);i++)
            {
                foreach(var other in playMap.mapPawns.AllPawnsSpawned.Where(p=>!fixtures.Contains(p)).ToList())other.DeSpawn();
                Find.TickManager.DoSingleTick();
                foreach(var pawn in playMap.mapPawns.FreeColonistsSpawned)
                    foreach(var need in pawn.needs.AllNeeds)if(need is not Need_Joy)need.CurLevel=need.MaxLevel;
            }
            remaining-=Math.Min(120,remaining);
        }
        catch(Exception e){steps=null;System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"FAIL: "+e+"\n");Log.Error(e.ToString());Waylith.EngineApplication.Instance.Quit(1);}
    }
    private Pawn Player(IntVec3 cell)
    {
        var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,mustBeCapableOfViolence:true,canGeneratePawnRelations:false,fixedBiologicalAge:25,fixedChronologicalAge:25,forcedXenotype:XenotypeDefOf.Baseliner){ForceNoBackstory=true});
        if(pawn.story?.bodyType==null&&pawn.story!=null)pawn.story.bodyType=BodyTypeDefOf.Male;
        pawn.apparel.DestroyAll();pawn.equipment.DestroyAllEquipment();pawn.health.RemoveAllHediffs();GenSpawn.Spawn(pawn,cell,playMap);
        pawn.workSettings.EnableAndInitialize();foreach(var work in DefDatabase<WorkTypeDef>.AllDefsListForReading)pawn.workSettings.SetPriority(work,0);
        pawn.skills.GetSkill(SkillDefOf.Shooting).Level=20;
        for(int hour=0;hour<24;hour++)pawn.timetable.SetAssignment(hour,TimeAssignmentDefOf.Joy);
        pawn.needs.joy.CurLevel=.05f;fixtures.Add(pawn);return pawn;
    }
    private void Gun(Pawn pawn,string name)
    {pawn.equipment.DestroyAllEquipment();var gun=(ThingWithComps)ThingMaker.MakeThing(Def<ThingDef>(name));gun.GetComp<CompQuality>()?.SetQuality(QualityCategory.Normal,ArtGenerationContext.Colony);pawn.equipment.AddEquipment(gun);}
    private IEnumerable<int> RecreationChecks()
    {
        playMap=Find.CurrentMap;DebugSettings.enableStoryteller=false;DebugSettings.noAnimals=false;DebugSettings.enableRandomMentalStates=false;
        foreach(var pawn in playMap.mapPawns.AllPawnsSpawned.ToList())pawn.DeSpawn();
        foreach(var cell in CellRect.CenteredOn(playMap.Center,28))
        {
            foreach(var thing in cell.GetThingList(playMap).ToList())if(thing.Spawned&&thing.def.destroyable)thing.Destroy();
            playMap.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);playMap.roofGrid.SetRoof(cell,null);playMap.fogGrid.Unfog(cell);
        }
        playMap.weatherManager.curWeather=playMap.weatherManager.lastWeather=WeatherDefOf.Clear;playMap.skyManager.ForceSetCurSkyGlow(1);
        var h=new Harmony("helodrace.tests.stagek.pattern");var ballistics=typeof(CompOilProducer).Assembly.GetType("Helodrace.ShotgunBallistics");
        h.Patch(AccessTools.Method(ballistics,"Sample"),prefix:new HarmonyMethod(typeof(Checks),nameof(Pattern)));
        h.Patch(AccessTools.Method(ballistics,"AimDeviation"),prefix:new HarmonyMethod(typeof(Checks),nameof(Aim)));
        h.Patch(AccessTools.PropertyGetter(typeof(MapTemperature),nameof(MapTemperature.OutdoorTemp)),prefix:new HarmonyMethod(typeof(Checks),nameof(Climate)));
        h.Patch(AccessTools.Method(typeof(Bullet),"Impact"),postfix:new HarmonyMethod(typeof(Checks),nameof(ClayHit)));
        h.Patch(AccessTools.GetDeclaredMethods(typeof(Projectile)).Single(m=>m.Name==nameof(Projectile.Launch)&&m.GetParameters().Length==8),postfix:new HarmonyMethod(typeof(Checks),nameof(Flags)));
        var trap=(Building_ClayTrap)ThingMaker.MakeThing(Def<ThingDef>("HD_ClayTrap"));trap.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(trap,playMap.Center,playMap,Rot4.North);
        var shooter=Player(trap.InteractionCell);Gun(shooter,"HD_Gun_M1897_Weapon");
        Check(ShootingRecreation.HasHuntingGun(shooter),"Loaded hunting classification accepts the M1897");
        Gun(shooter,"Gun_AssaultRifle");Check(!ShootingRecreation.HasHuntingGun(shooter),"Automatic assault rifle cannot start recreational hunting");Gun(shooter,"HD_Gun_M1897_Weapon");
        Check(trap.GetComp<CompRefuelable>().Fuel==0&&!trap.CanPlay(shooter),"Clay trap starts empty and refuses recreation without physical target fuel");trap.GetComp<CompRefuelable>().Refuel(25);
        Check(trap.CanPlay(shooter),"Loaded trap and actual equipped gun accept an unobstructed outdoor lane");
        var friend=Player(trap.Position+IntVec3.North*5);Check(!trap.CanPlay(shooter),"Friendly pawn in lane prevents clay shooting");friend.Destroy();
        var job=Def<JoyGiverDef>("HD_ClayShooting").Worker.TryGiveJob(shooter);Check(job!=null,"Native joy giver produces the clay shooting job");
        int before=(int)shooter.records.GetValue(RecordDefOf.ShotsFired);Check(shooter.jobs.TryTakeOrderedJob(job),"Pawn starts actual clay recreation");
        for(int elapsed=0;elapsed<4000&&shooter.CurJobDef==Def<JobDef>("HD_ClayShooting");elapsed+=60)yield return 60;
        Check(shooter.CurJobDef!=Def<JobDef>("HD_ClayShooting"),"Clay session completes within its duration");
        Check(shooter.records.GetValue(RecordDefOf.ShotsFired)>before,"Clay recreation fires actual native ammunition");
        Check(trap.GetComp<CompRefuelable>().Fuel<25&&shooter.needs.joy.CurLevel>.15f,"Clay recreation consumes targets and raises actual joy");
        Check(clayHits>0&&safeProjectileFlags,"Actual moving clay targets are hit and live rounds exclude non-target pawns; hits="+clayHits);
        shooter.Destroy();trap.Destroy();
        foreach(int forced in new[]{-1,1})
        {
            var hunter=Player(playMap.Center+new IntVec3(-8,0,-8));Gun(hunter,"HD_Gun_M1897_Weapon");
            var animal=PawnGenerator.GeneratePawn(Def<PawnKindDef>("Deer"));GenSpawn.Spawn(animal,playMap.Center+new IntVec3(2,0,-1),playMap);
            fixtures.Add(animal);var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=99999;animal.jobs.StartJob(wait,JobCondition.InterruptForced);
            Check(RecreationalHunting.CanHunt(hunter,animal),"Safe wild deer with hunting gun is eligible");
            var hunt=JobMaker.MakeJob(Def<JobDef>("HD_RecreationalHunt"),animal);hunt.SetTarget(TargetIndex.C,hunter.equipment.Primary);
            before=(int)hunter.records.GetValue(RecordDefOf.ShotsFired);forcePattern=forced;
            Check(hunter.jobs.TryTakeOrderedJob(hunt),"Pawn starts long recreational hunting job");
            bool huntEnded=false;
            hunter.jobs.curDriver.AddFinishAction(reason=>{huntEnded=true;System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"HUNT_END "+reason+" at="+Find.TickManager.TicksGame+" report="+hunter.jobs.curDriver.GetReport()+" canShoot="+ShootingRecreation.HasHuntingGun(hunter)+" targetAlive="+!animal.Dead+" spawned="+animal.Spawned+" faction="+animal.Faction?.def.defName+" targetC="+hunt.targetC.Thing?.def.defName+" gunSame="+ReferenceEquals(hunter.equipment.Primary,hunt.targetC.Thing)+" targetSame="+ReferenceEquals(animal,hunt.targetA.Thing)+"\n");});
            yield return RecreationalHunting.MinimumPreparationTicks-30;
            Check(!huntEnded&&hunter.records.GetValue(RecordDefOf.ShotsFired)==before,"Search, tracking and observation take at least five game hours before the first shot; current="+hunter.CurJobDef?.defName+" sameJob="+ReferenceEquals(hunter.CurJob,hunt)+" shots="+hunter.records.GetValue(RecordDefOf.ShotsFired));
            for(int elapsed=0;elapsed<10000&&!huntEnded;elapsed+=60)yield return 60;
            Check(huntEnded,"Recreational hunt terminates instead of farming another animal");
            if(forced<0)
            {
                Check(hunter.records.GetValue(RecordDefOf.ShotsFired)==before+1,"Forced physical first-shot miss ends after exactly one cartridge");
                Check(!animal.Dead&&hunter.needs.joy.CurLevel>.45f,"Miss leaves game alive and grants the slightly reduced recreation reward");
            }
            else Check(animal.Dead&&hunter.needs.joy.CurLevel>.55f,"A first-shot hit continues to a real kill and grants the large success reward");
            forcePattern=0;hunter.Destroy();if(animal.Corpse!=null)animal.Corpse.Destroy();else animal.Destroy();
        }
    }
}
