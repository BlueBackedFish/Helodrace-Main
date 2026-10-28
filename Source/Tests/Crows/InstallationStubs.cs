using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Verse
{
    public class Def
    {
        public string defName,label;
        public string LabelCap=>label??defName??"definition";
        public virtual IEnumerable<string> ConfigErrors(){yield break;}
    }
    public class ThingDefCountClass { public ThingDef thingDef; public int count; }
    public static class DefDatabase<T> where T:Def { public static List<T> AllDefsListForReading=new List<T>(); }
    public interface IThingHolder { ThingOwner GetDirectlyHeldThings(); void GetChildHolders(List<IThingHolder> children); }
    public class ThingOwner:List<Thing>
    {
        public int TotalStackCountOfDef(ThingDef def)=>this.Where(t=>t.def==def).Sum(t=>t.stackCount);
        public int TryTransferToContainer(Thing t,ThingOwner target,int count)
        {
            if (!Contains(t)) return 0;
            int moved=Math.Min(count,t.stackCount);
            if(moved==t.stackCount){Remove(t);target.Add(t);}
            else{t.stackCount-=moved;target.Add(new Thing{def=t.def,stackCount=moved});}
            return moved;
        }
        public bool TryDropAll(IntVec3 cell,Map map,ThingPlaceMode mode)
        {
            if(!map.CanDropMaterials)return false;
            map.Materials.AddRange(this);Clear();return true;
        }
        public void ClearAndDestroyContents(){foreach(Thing t in this)t.Destroy();Clear();}
    }
    public class ThingOwner<T>:ThingOwner where T:Thing { public ThingOwner(IThingHolder owner){ } }
    public static class ThingOwnerUtility { public static void AppendThingHoldersFromThings(List<IThingHolder> children,ThingOwner things){ } }
    public enum ThingPlaceMode { Near }
    public partial class Thing
    {
        public int stackCount=1; public bool Destroyed;
        public void Destroy(){Destroyed=true;}
    }
    public partial class Map { public bool CanDropMaterials=true; public List<Thing> Materials=new List<Thing>(); }
    public class CompEquippable { public List<Verb> AllVerbs=new List<Verb>(); }
    public static class ThingMaker
    {
        public static Thing MakeThing(ThingDef def)=>new Thing {def=def,Equippable=new CompEquippable {AllVerbs=new List<Verb>{new Helodrace.Verb_Crows()}}};
    }
    public class FloatMenu { public List<FloatMenuOption> Options; public FloatMenu(List<FloatMenuOption> options){Options=options;} }
    public class WindowStack { public object Last; public void Add(object window){Last=window;} }
    public static partial class Extensions
    {
        public static string ToStringPercent(this float value)=>(value*100).ToString("F0")+"%";
        public static bool IsBurning(this Thing thing)=>false;
        public static bool CanReserveAndReach(this Pawn pawn,Thing thing,PathEndMode mode,Danger danger)=>true;
        public static float GetStatValue(this Pawn pawn,object stat)=>1f;
    }
    public struct ThingRequest { public ThingDef Def; public static ThingRequest ForDef(ThingDef def)=>new ThingRequest{Def=def}; }
    public struct TraverseParms { public static TraverseParms For(Pawn pawn)=>new TraverseParms(); }
    public static class GenClosest
    {
        public static Thing ClosestThingReachable(IntVec3 pos,Map map,ThingRequest request,PathEndMode mode,TraverseParms parms,float distance,Predicate<Thing> validator)
            =>map.Materials.FirstOrDefault(t=>t.def==request.Def&&validator(t));
    }
    public class PawnCarryTracker
    {
        readonly Pawn pawn;
        public PawnCarryTracker(Pawn pawn){this.pawn=pawn;}
        public ThingOwner innerContainer=new ThingOwner(); public Thing CarriedThing=>innerContainer.FirstOrDefault();
        public bool TryDropCarriedThing(IntVec3 cell,ThingPlaceMode mode,out Thing thing){thing=CarriedThing;return innerContainer.TryDropAll(cell,pawn.Map,mode);}
    }
    public partial class Pawn
    {
        public RimWorld.SkillTracker skills=new RimWorld.SkillTracker(); public PawnCarryTracker carryTracker;
        public Pawn(){carryTracker=new PawnCarryTracker(this);}
    }
    public class ThingFilter
    {
        readonly HashSet<ThingDef> allowed=new HashSet<ThingDef>();
        public void SetDisallowAll()=>allowed.Clear();
        public void SetAllow(ThingDef def,bool value){if(value)allowed.Add(def);else allowed.Remove(def);}
        public bool Allows(ThingDef def)=>allowed.Contains(def);
    }
    public enum LoadSaveMode { Inactive,Saving,LoadingVars,PostLoadInit }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Defs
    {
        public static Dictionary<string,Def> Values=new Dictionary<string,Def>();
        public static void Look<T>(ref T value,string key) where T:Def
        { if(Scribe.mode==LoadSaveMode.LoadingVars)value=Values[key] as T;else Values[key]=value; }
    }
    public static class Scribe_Values
    {
        public static Dictionary<string,float> Values=new Dictionary<string,float>();
        public static void Look(ref float value,string key,float defaultValue)
        {if(Scribe.mode==LoadSaveMode.LoadingVars)value=Values[key];else Values[key]=value;}
    }
    public static class Scribe_Deep
    {
        static ThingOwner<Thing> saved;
        public static void Look(ref ThingOwner<Thing> value,string key,params object[] args)
        {
            if(Scribe.mode==LoadSaveMode.LoadingVars){value=new ThingOwner<Thing>((IThingHolder)args[0]);if(saved!=null)value.AddRange(saved.Select(t=>new Thing{def=t.def,stackCount=t.stackCount}));}
            else saved=value;
        }
    }
}
namespace RimWorld
{
    public static class ThingDefOf { public static ThingDef Steel=new ThingDef{defName="Steel",label="steel"}; }
    public static class SkillDefOf { public static object Construction=new object(); }
    public static class StatDefOf { public static object ConstructionSpeed=new object(); }
    public class SkillTracker { public SkillRecord Construction=new SkillRecord(); public SkillRecord GetSkill(object skill)=>Construction; }
    public class SkillRecord { public int Level=8; }
    public abstract class WorkGiver_Scanner
    {
        public virtual ThingRequest PotentialWorkThingRequest=>default;
        public virtual PathEndMode PathEndMode=>PathEndMode.Touch;
        public virtual bool HasJobOnThing(Pawn pawn,Thing thing,bool forced=false)=>false;
        public virtual Job JobOnThing(Pawn pawn,Thing thing,bool forced=false)=>null;
    }
    public class CompProperties_Refuelable:CompProperties
    {
        public ThingFilter fuelFilter=new ThingFilter(); public float fuelCapacity=100;
    }
    public class CompRefuelable:ThingComp
    {
        public CompProperties_Refuelable Props=>(CompProperties_Refuelable)props;
        public float Fuel; public bool HasFuel=>Fuel>=1;
        public void Refuel(float amount){Fuel=Math.Min(Props.fuelCapacity,Fuel+amount);}
        public void ConsumeFuel(float amount){Fuel=Math.Max(0,Fuel-amount);}
        public override IEnumerable<Gizmo> CompGetGizmosExtra(){yield return new Gizmo();}
        public override string CompInspectStringExtra()=>"ammunition";
    }
}
namespace Verse.AI
{
    public static class Toils_Haul { public static Toil StartCarryThing(TargetIndex index)=>new Toil(); }
    public static class Toils_General { public static Toil Do(Action action)=>new Toil {initAction=action}; }
    public static class ToilProgress { public static Toil WithProgressBar(this Toil toil,TargetIndex index,Func<float> progress)=>toil; }
}
