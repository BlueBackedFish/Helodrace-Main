// Lightweight game surfaces for control-flow checks.
// Actual firing and native rendering are covered by the RimWorld 1.7 host.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse.AI;
using UnityEngine;
namespace UnityEngine
{
    public class Texture2D { }
    public static class Mathf { public static float Clamp01(float v)=>Math.Max(0f,Math.Min(1f,v)); }
    public struct Color { public static Color white => new Color(); }
    public struct Vector2 { }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new Vector3(0,1,0); public static Vector3 forward=>new Vector3(0,0,1);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float n)=>new Vector3(a.x*n,a.y*n,a.z*n);
    }
    public struct Quaternion
    {
        float angle;
        public static Quaternion AngleAxis(float angle,Vector3 axis)=>new Quaternion{angle=angle};
        public static Vector3 operator *(Quaternion q,Vector3 v){double a=q.angle*Math.PI/180;return new Vector3((float)(v.x*Math.Cos(a)+v.z*Math.Sin(a)),v.y,(float)(v.z*Math.Cos(a)-v.x*Math.Sin(a)));}
    }
    public struct Matrix4x4 { public Vector3 pos,scale; public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>new Matrix4x4{pos=p,scale=s}; }
    public class Material { public string Path; }
    public static class Graphics
    {
        public static List<(Matrix4x4 matrix,Material material)> Draws=new List<(Matrix4x4,Material)>();
        public static void DrawMesh(object mesh,Matrix4x4 matrix,Material mat,int layer)=>Draws.Add((matrix,mat));
    }
}
namespace RimWorld
{
    public class DefOfAttribute:Attribute { }
    public static class DefOfHelper { public static void EnsureInitializedInCtor(Type type){ } }
    public class Faction { public static Faction OfPlayer=new Faction(); }
    public enum WorkTags { Violent }
    public static class PawnCapacityDefOf { public static object Manipulation=new object(),Sight=new object(); }
    public class CompPowerTrader { public bool PowerOn=true; }
    public class CompFlickable { public bool SwitchIsOn=true; }
    public class CompMannable { public bool MannedNow; public Verse.Pawn ManningPawn; }
    public class TurretTop { public float CurRotation; }
    public class Building_TurretGun:Verse.Building
    {
        protected int burstWarmupTicksLeft; protected Verse.LocalTargetInfo currentTargetInt;
        public bool Active=true,IsStunned; public TurretTop Top=new TurretTop();
        protected virtual bool CanSetForcedTarget=>false;
        public bool CanSetForcedTargetForTest=>CanSetForcedTarget;
        public Verse.Thing gun;
        public Verse.CompEquippable GunCompEq=>gun?.GetComp<Verse.CompEquippable>();
        public Verse.Verb TestVerb; public Verse.Verb AttackVerb=>GunCompEq?.AllVerbs.FirstOrDefault()??TestVerb; public int BaseTicks;
        protected int burstCooldownTicksLeft;
        protected virtual void BurstComplete(){ }
        public Verse.LocalTargetInfo CurrentTarget=>currentTargetInt;
        protected override void Tick(){BaseTicks++;}
        public void DoTickForTest()=>Tick(); public void SetWarmupForTest(){burstWarmupTicksLeft=40;currentTargetInt=new Verse.LocalTargetInfo(this);}
        public int WarmupForTest=>burstWarmupTicksLeft;
    }
}
namespace Verse
{
    public class JobDef { }
    public class DefModExtension { }
    public class ThingDef:Def
    {
        public GraphicData graphicData=new GraphicData(); public BuildingProperties building=new BuildingProperties();
        public DefModExtension Extension;
        public int stackLimit=75;
        public T GetModExtension<T>() where T:DefModExtension=>Extension as T;
    }
    public class BuildingProperties { public float turretTopDrawSize=3.2f; public ThingDef turretGunDef; }
    public class GraphicData { public Vector2 drawSize; }
    public class Graphic { public string Path="original desk"; public GraphicData data; }
    public class Graphic_Multi:Graphic { }
    public static class GraphicDatabase { public static T Get<T>(string path,object shader,Vector2 size,Color color,Color colorTwo,GraphicData data) where T:Graphic,new()=>new T{Path=path,data=data}; }
    public static class ShaderDatabase { public static object Cutout=new object(); }
    public static class MaterialPool { public static Material MatFrom(string p,object s)=>new Material{Path=p}; }
    public static class MeshPool { public static object plane10=new object(); }
    public enum AltitudeLayer { BuildingOnTop }
    public static partial class Extensions
    {
        public static float AltitudeFor(this AltitudeLayer a)=>1f;
        public static string Translate(this string s,params object[] args)=>s;
        public static bool IsForbidden(this Thing t,Pawn p)=>t.Forbidden;
        public static T TryGetComp<T>(this Thing t) where T:class=>t.GetComp<T>();
        public static bool CanReach(this Pawn p,Thing t,PathEndMode mode,Danger danger)=>true;
        public static bool CanReserve(this Pawn p,object t)=>true;
        public static bool Reserve(this Pawn p,object t,Job job,int count,int stack,object layer,bool error)=>true;
        public static void GainComfortFromCellIfPossible(this Pawn p,int ticks){ }
    }
    public struct Rot4 { public float AsAngle=>0; }
    public struct IntVec3:IEquatable<IntVec3>
    {
        int n; public IntVec3(int n){this.n=n;}
        public bool InBounds(Map map)=>map!=null;
        public List<Thing> GetThingList(Map map)=>map.AtControls;
        public bool Equals(IntVec3 b)=>n==b.n;
        public override bool Equals(object b)=>b is IntVec3 v&&Equals(v);
        public override int GetHashCode()=>n;
        public static bool operator ==(IntVec3 a,IntVec3 b)=>a.Equals(b);
        public static bool operator !=(IntVec3 a,IntVec3 b)=>!a.Equals(b);
        public static implicit operator LocalTargetInfo(IntVec3 p)=>new LocalTargetInfo();
    }
    public struct LocalTargetInfo
    {
        public Thing Thing;
        public LocalTargetInfo(Thing t){Thing=t;}
        public bool IsValid=>Thing!=null; public static LocalTargetInfo Invalid=>new LocalTargetInfo();
        public static implicit operator LocalTargetInfo(Thing t)=>new LocalTargetInfo(t);
    }
    public class ConditionManager { public bool Disabled; public bool ElectricityDisabled(Map m)=>Disabled; }
    public partial class Map { public ConditionManager gameConditionManager=new ConditionManager(); public List<Thing> AtControls=new List<Thing>(); }
    public enum DestroyMode { Vanish }
    public partial class Thing
    {
        public bool Spawned=true,Forbidden; public Map Map; public Faction Faction; public IntVec3 Position;
        public ThingDef def=new ThingDef(); public Rot4 Rotation;
        public Vector3 DrawPos=>new Vector3(); public string LabelShort=>"building";
        public CompPowerTrader Power=new CompPowerTrader(); public CompFlickable Flick=new CompFlickable();
        public CompMannable Manning;
        public Helodrace.CompCrowsGraphic GraphicComp;
        public CompEquippable Equippable; public Helodrace.CompCrowsAmmo Ammo;
        public T GetComp<T>() where T:class=>(Power as T)??(Flick as T)??(GraphicComp as T)??(Manning as T)??(Equippable as T)??(Ammo as T);
        public virtual Graphic Graphic=>new Graphic();
        public virtual void ExposeData(){ } public virtual void DeSpawn(DestroyMode m=DestroyMode.Vanish){Spawned=false;}
        public virtual void SetFaction(Faction f,Pawn recruiter=null){Faction=f;}
        public virtual IEnumerable<Gizmo> GetGizmos(){yield break;}
        public virtual IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn p){yield break;}
        public virtual string GetInspectString()=>"";
        public virtual void DrawExtraSelectionOverlays(){ }
        public virtual void SpawnSetup(Map map,bool respawningAfterLoad=false){Map=map;Spawned=true;}
    }
    public class ThingWithComps:Thing { protected virtual void Tick(){ } }
    public class Building:ThingWithComps { public IntVec3 InteractionCell=>new IntVec3(1); }
    public partial class Pawn:Thing
    {
        public float ShootingAccuracy=.98f;
        public bool Dead,Downed,InMentalState; public RaceProperties RaceProps=new RaceProperties();
        public Health health=new Health(); public PawnJobs jobs=new PawnJobs(); public RotationTracker rotationTracker=new RotationTracker();
        public Job CurJob=>jobs.CurJob; public bool WorkTagIsDisabled(WorkTags w)=>false;
    }
    public class RaceProperties { public bool Humanlike=true; }
    public class Health { public Capacities capacities=new Capacities(); }
    public class Capacities { public bool Enabled=true; public bool CapableOf(object capacity)=>Enabled; }
    public class RotationTracker { public void FaceTarget(Thing t){ } }
    public class PawnJobs { public Job CurJob; public JobDriver curDriver; public void TryTakeOrderedJob(Job job,JobTag tag){CurJob=job;} }
    public class Gizmo { }
    public class Command_Action:Gizmo { public string defaultLabel,defaultDesc; public Texture2D icon; public Action action; }
    public class FloatMenuOption { public string Label; public Action action; public FloatMenuOption(string s,Action a){Label=s;action=a;} }
    public static class ContentFinder<T> where T:new() { public static T Get(string p)=>new T(); }
    public class TargetingParameters { public bool canTargetBuildings,canTargetPawns,canTargetItems,canTargetLocations; public Predicate<LocalTargetInfo> validator; }
    public class Targeter { public void BeginTargeting(TargetingParameters p,Action<LocalTargetInfo> action){ } }
    public class TickManager { public int TicksGame; }
    public static class Find { public static Targeter Targeter=new Targeter(); public static TickManager TickManager=new TickManager(); public static WindowStack WindowStack=new WindowStack(); }
    public static class GenDraw { public static void DrawLineBetween(Vector3 a,Vector3 b){ } }
    public static class Scribe_References
    {
        public static Dictionary<string,Thing> Values=new Dictionary<string,Thing>();
        public static void Look<T>(ref T thing,string key) where T:Thing { Values[key]=thing; }
    }
    public class Verb
    {
        public Thing caster; public Thing Caster=>caster; public int Shots,ResetCalls;
        public Action castCompleteCallback;
        public Thing EquipmentSource; public virtual ThingDef Projectile=>null; public virtual void WarmupComplete(){}
        public virtual bool Available()=>true;
        protected virtual bool TryCastShot(){Shots++;return true;}
        public bool TestShot()=>TryCastShot(); public virtual void Reset(){ResetCalls++;}
    }
    public class Verb_Shoot:Verb { }
    public struct ShotReport
    {
        private float factorFromWeather;
        public float EquipmentForTest,CoverForTest;
        public ShotReport(float weather,float equipment,float cover){factorFromWeather=weather;EquipmentForTest=equipment;CoverForTest=cover;}
        public float WeatherForTest=>factorFromWeather;
        public static ShotReport HitReportFor(Thing caster,Verb verb,LocalTargetInfo target)=>default;
        public static float HitFactorFromShooter(Thing caster,float distance,float? acc=null)
            =>(float)Math.Pow(acc??(caster is Pawn pawn?pawn.ShootingAccuracy:.9f),distance);
    }
    public class ThingComp { public Thing parent; public CompProperties props; public virtual void Initialize(CompProperties p){props=p;} public virtual void PostDraw(){ } public virtual IEnumerable<Gizmo> CompGetGizmosExtra(){yield break;} public virtual string CompInspectStringExtra()=>null; }
    public class CompProperties { public Type compClass; }
}
namespace HarmonyLib
{
    public sealed class HarmonyPatch:Attribute { public HarmonyPatch(Type type,string method){ } }
    public static class AccessTools
    {
        public static System.Reflection.FieldInfo Field(Type type,string name)=>type.GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
    }
}
namespace Verse.AI
{
    public enum TargetIndex { A,B }
    public enum PathEndMode { InteractionCell,Touch,ClosestTouch }
    public enum Danger { Deadly,Some }
    public enum JobTag { Misc }
    public enum JobCondition { Incompletable,Succeeded }
    public enum ToilCompleteMode { Never }
    public class Job { public Verse.JobDef def; public Verse.LocalTargetInfo targetA,targetB; public int count; }
    public static class JobMaker { public static Job MakeJob(Verse.JobDef def,Verse.LocalTargetInfo a,Verse.LocalTargetInfo b=default)=>new Job{def=def,targetA=a,targetB=b}; }
    public class JobDriver
    {
        public Verse.Pawn pawn; public Job job; public int CurToilIndex;
        public Verse.Thing TargetThingA=>job.targetA.Thing;
        public virtual bool TryMakePreToilReservations(bool error)=>true;
        protected virtual IEnumerable<Toil> MakeNewToils(){yield break;}
        public List<Toil> ToilsForTest()=>MakeNewToils().ToList();
        public virtual void ExposeData(){ }
        public void EndJobWith(JobCondition condition){pawn.jobs.CurJob=null;}
    }
    public class Toil { public ToilCompleteMode defaultCompleteMode; public bool handlingFacing; public Action<int> tickIntervalAction; public Action initAction; }
    public static class ToilMaker { public static Toil MakeToil(string name)=>new Toil(); }
    public static class Toils_Goto { public static Toil GotoThing(TargetIndex i,PathEndMode end)=>new Toil(); }
    public static class FailConditions
    {
        public static void FailOnDespawnedNullOrForbidden(this JobDriver j,TargetIndex i){ }
        public static void FailOnBurningImmobile(this JobDriver j,TargetIndex i){ }
        public static void FailOn(this JobDriver j,Func<bool> condition){ }
        public static void FailOnDestroyedOrNull(this JobDriver j,TargetIndex i){ }
    }
}

namespace Helodrace { public class CompM167VadsGraphic:Verse.ThingComp { public void NotifyShotFired(){} } }
