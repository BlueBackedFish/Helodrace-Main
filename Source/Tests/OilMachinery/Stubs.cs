// Small deterministic game boundary for tests of the linked production components.
using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine
{
    public struct Vector3 { public Vector3(float x,float y,float z) {} public static Vector3 operator +(Vector3 a,Vector3 b)=>a; }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
}
namespace Verse
{
    public class CompProperties { public Type compClass; }
    public class ThingComp
    {
        public ThingWithComps parent; public CompProperties props;
        public virtual void PostSpawnSetup(bool load) {} public virtual void PostExposeData() {}
        public virtual void CompTick() {} public virtual void CompTickRare() {} public virtual string CompInspectStringExtra()=>null;
    }
    public class Thing
    {
        public Map Map; public IntVec3 Position; public bool Spawned; public int stackCount; public string Label=>"test";
        public T TryGetComp<T>() where T:class => (this as ThingWithComps)?.GetComp<T>();
        public bool IsForbidden(Pawn p)=>false; public void Destroy() {} public bool IsHashIntervalTick(int t)=>true;
    }
    public class ThingWithComps:Thing
    {
        public List<ThingComp> comps = new(); public T GetComp<T>() where T:class=>comps.OfType<T>().FirstOrDefault();
        public IEnumerable<IntVec3> OccupiedRect() { yield return Position; yield return new IntVec3(1,0,0); }
    }
    public class Building:ThingWithComps {}
    public class Pawn:Thing
    {
        public SkillTracker skills=new(); public bool CanReserve(Thing t,int a,int b,object c,bool f)=>true;
        public bool Reserve(Thing t,AI.Job j,int a,int b,object c,bool f)=>true;
    }
    public class SkillTracker { public void Learn(object skill,float amount) {} }
    public class BuildingLister { public List<Building> allBuildingsColonist=new(); }
    public class Map
    {
        public BuildingLister listerBuildings=new(); public TerrainGrid terrainGrid=new();
        public Helodrace.MapComponent_OilFields Fields=new(); public T GetComponent<T>() where T:class =>Fields as T;
    }
    public class TerrainGrid { public void SetUnderTerrain(IntVec3 p,TerrainDef d) {} public void SetTerrain(IntVec3 p,TerrainDef d) {} }
    public struct IntVec3
    {
        public int x,y,z; public IntVec3(int x,int y,int z) { this.x=x;this.y=y;this.z=z; }
        public static IntVec3 operator +(IntVec3 a,IntVec3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public UnityEngine.Vector3 ToVector3Shifted()=>new(); public bool InBounds(Map m)=>true;
        public TerrainDef GetTerrain(Map m)=>new();
    }
    public struct Rot4 {}
    public class BuildableDef { public IntVec3 Size; } public class ThingDef:BuildableDef {} public class TerrainDef { public bool layerable; }
    public static class GenAdj { public static IEnumerable<IntVec3> OccupiedRect(IntVec3 p,Rot4 r,IntVec3 s) { yield return p; } }
    public struct AcceptanceReport { public static implicit operator AcceptanceReport(bool b)=>new(); public static implicit operator AcceptanceReport(string s)=>new(); }
    public class PlaceWorker { public virtual AcceptanceReport AllowsPlacing(BuildableDef d,IntVec3 p,Rot4 r,Map m,Thing ignore=null,Thing thing=null)=>true; }
    public static class Scribe_Values { public static void Look<T>(ref T v,string key,T fallback) {} }
    public static class GenTicks { public const int TickRareInterval=250,TicksPerRealSecond=60; }
    public static class Rand { public static float Range(float a,float b)=>a; public static bool MTBEventOccurs(float a,float b,int c)=>false; }
    public static class FleckMaker { public static void ThrowDustPuff(UnityEngine.Vector3 p,Map m,float f) {} }
    public static class DefDatabase<T> where T:new() { public static T GetNamed(string n,bool f=true)=>new(); }
    public static class Messages { public static void Message(string s,Thing t,object kind) {} }
    public static class Translator { public static string Translate(this string s,params object[] args)=>s; }
    public static class ThingMaker { public static Thing MakeThing(ThingDef d)=>new(); }
    public enum ThingPlaceMode { Near }
    public static class GenPlace { public static Thing Last; public static bool TryPlaceThing(Thing t,IntVec3 p,Map m,ThingPlaceMode mode) { Last=t;return true; } }
    public static class GenTemperature { public static float Heat; public static float GetTemperatureForCell(IntVec3 p,Map m)=>20; public static void PushHeat(IntVec3 p,Map m,float h) { Heat+=h; } }
    public enum ThingRequestGroup { BuildingArtificial }
    public struct ThingRequest { public static ThingRequest ForGroup(ThingRequestGroup group)=>new(); }
}
namespace RimWorld
{
    public class CompFlickable:Verse.ThingComp { public bool SwitchIsOn; }
    public class CompPowerTrader:Verse.ThingComp { public bool PowerOn; }
    public class CompRefuelable:Verse.ThingComp { public bool HasFuel; public int Notifications; public void Notify_UsedThisTick() { Notifications++; } }
    public static class MessageTypeDefOf { public static object PositiveEvent,CautionInput; }
    public static class SkillDefOf { public static object Construction; }
    public class WorkGiver_Scanner
    {
        public virtual Verse.ThingRequest PotentialWorkThingRequest=>new();
        public virtual IEnumerable<Verse.Thing> PotentialWorkThingsGlobal(Verse.Pawn p)=>null;
        public virtual bool HasJobOnThing(Verse.Pawn p,Verse.Thing t,bool f=false)=>false;
        public virtual Verse.AI.Job JobOnThing(Verse.Pawn p,Verse.Thing t,bool f=false)=>null;
    }
}
namespace Verse.AI
{
    public class JobDef {} public enum TargetIndex { A } public enum PathEndMode { InteractionCell }
    public class Target { public Thing Thing; }
    public class Job { public Target GetTarget(TargetIndex i)=>new(); }
    public static class JobMaker { public static Job MakeJob(JobDef d,Thing t)=>new(); }
    public class JobDriver
    {
        public Job job=new(); public Pawn pawn=new(); public virtual bool TryMakePreToilReservations(bool error)=>true;
        protected virtual IEnumerable<Toil> MakeNewToils()=>null;
        public void FailOnDespawnedNullOrForbidden(TargetIndex i) {}
    }
    public class Toil { public Action tickAction; public void WithProgressBarToilDelay(TargetIndex i) {} public void AddFinishAction(Action a) {} }
    public static class Toils_Goto { public static Toil GotoThing(TargetIndex i,PathEndMode m)=>new(); }
    public static class Toils_General { public static Toil Wait(int t)=>new(); }
}
namespace Helodrace
{
    public class MechanicalProperties { public float recommendedRPM=60; }
    public class CompMechanicalUser:Verse.ThingComp { public bool HasPower; public float EffectiveRPM=60; public MechanicalProperties Props=new(); }
    public class CompMechanicalEmitter:Verse.ThingComp { public bool IsProducingPower; }
    public class OilFieldRecord { public float quality=1,pressure=1; }
    public class MapComponent_OilFields
    {
        public const string OilFieldTerrainDefName="HD_OilField";
        public OilFieldRecord Field; public Verse.IntVec3 FieldPosition; public int PressureReductions;
        public bool IsOilFieldTerrain(Verse.IntVec3 p)=>Field!=null && p.x==FieldPosition.x && p.z==FieldPosition.z;
        public OilFieldRecord FieldAtOrCreateLegacy(Verse.IntVec3 p)=>IsOilFieldTerrain(p)?Field:null;
        public void CreateOrReplace(Verse.IntVec3 p,float quality,float pressure) { FieldPosition=p;Field=new() { quality=quality,pressure=pressure }; }
        public void ReducePressure(OilFieldRecord field,float amount,float minimum) { PressureReductions++;field.pressure=Math.Max(minimum,field.pressure-amount); }
    }
}
