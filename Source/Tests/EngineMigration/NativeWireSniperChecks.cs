using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using GV=UnityEngine.Vector3;
using GQ=UnityEngine.Quaternion;
using GM=UnityEngine.Matrix4x4;
using Mesh=UnityEngine.Mesh;
using Mat=UnityEngine.Material;
using NV=System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeWireSniperChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Dictionary<Thing,GV> positions=new();
        private static readonly HashSet<Projectile> probes=new();
        private static readonly List<Thing> fixture=new();
        private sealed record Line(GV From,GV To,Mat Material,float Width);
        private sealed record MeshCall(Mesh Mesh,GM Matrix,Mat Material,int Layer);
        private sealed record Hit(ThingDef Def,Thing Thing,bool Blocked,Map Map);
        private sealed record Register(CompZaperX26 Comp,Pawn Launcher,Pawn Target,Map Map);
        private sealed record Frame(Line[] Lines,MeshCall[] Meshes,Hit[] BaseHits,Dictionary<string,object>[] Damage,Register[] Registrations,int Next,int Bits);
        private static List<Line> lines;
        private static List<MeshCall> meshes;
        private static List<Hit> baseHits;
        private static List<Dictionary<string,object>> damage;
        private static List<Register> registrations;
        private static GQ rotation;
        private static Pawn launcher,target;
        private static bool drafted,wounded;
        private static float dealt;
        private static int wirePairs,flightPairs,tetherPairs,impactPairs,trailPairs,layerPairs,aimPairs,rngPairs,components,lineCount,meshCount,damageCount,registrationCount,guardPairs,rotationReads;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static readonly Type ActualSniper=typeof(MapComponent_HelodSniperSupport),FrozenSniper=typeof(FrozenWireSniper.Sniper);
        private static void Set(object o,string name,object value)=>AccessTools.Field(o.GetType(),name).SetValue(o,value);
        private static object Call(object o,string method,params object[] args){Type t=o as Type??o.GetType();return t.GetMethod(method,All).Invoke(o is Type?null:o,args);}
        private static NV Native(GV v)=>new(v.x,v.y,v.z);
        private static bool Position(Thing __instance,ref GV __result)=>!positions.TryGetValue(__instance,out __result);
        private static bool Rotation(Projectile __instance,ref GQ __result){if(lines==null||!probes.Contains(__instance))return true;rotationReads++;__result=rotation;return false;}
        private static bool Draft(Pawn __instance,ref bool __result){if(lines==null||__instance!=launcher)return true;__result=drafted;return false;}
        private static bool LineDraw(GV __0,GV __1,Mat __2,float __3){if(lines==null)return true;lines.Add(new(__0,__1,__2,__3));return false;}
        private static bool MeshDraw(Mesh __0,GM __1,Mat __2,int __3){if(meshes==null)return true;meshes.Add(new(__0,__1,__2,__3));return false;}
        private static bool BaseImpact(Projectile __instance,Thing __0,bool __1)
        {if(baseHits==null||!probes.Contains(__instance))return true;baseHits.Add(new(__instance.def,__0,__1,__instance.Map));return false;}
        private static bool Damage(Thing __instance,DamageInfo __0,ref DamageWorker.DamageResult __result)
        {if(damage==null||__instance!=target)return true;damage.Add(typeof(DamageInfo).GetFields(All).Where(f=>!f.IsStatic).ToDictionary(f=>f.Name,f=>f.GetValue(__0)));__result=new DamageWorker.DamageResult{totalDamageDealt=dealt,wounded=wounded};return false;}
        private static bool Registered(CompZaperX26 __instance,Pawn __0,Pawn __1,Map __2)
        {if(registrations==null)return true;registrations.Add(new(__instance,__0,__1,__2));return false;}
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static void Value(object a,object e,string scope)
        {if(a is float f&&e is float ef)Near(f,ef,scope);else if(a is GV v&&e is GV ev)Vector(v,ev,scope);else if(!Equals(a,e))throw new InvalidOperationException(scope+" field changed.");}
        private static Frame Record(Action action)
        {
            lines=new();meshes=new();baseHits=new();damage=new();registrations=new();Rand.PushState(3701);
            try{action();return new(lines.ToArray(),meshes.ToArray(),baseHits.ToArray(),damage.ToArray(),registrations.ToArray(),Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}
            finally{Rand.PopState();lines=null;meshes=null;baseHits=null;damage=null;registrations=null;}
        }
        private static Frame Pair(Action actual,Action frozen,bool empty=false)
        {
            var a=Record(actual);var e=Record(frozen);
            if(a.Lines.Length!=e.Lines.Length||a.Meshes.Length!=e.Meshes.Length||a.Damage.Length!=e.Damage.Length)throw new InvalidOperationException("Wire/sniper submission count changed.");
            for(int i=0;i<a.Lines.Length;i++){Vector(a.Lines[i].From,e.Lines[i].From,"wire start");Vector(a.Lines[i].To,e.Lines[i].To,"wire end");Near(a.Lines[i].Width,e.Lines[i].Width,"wire width");if(a.Lines[i].Material!=e.Lines[i].Material)throw new InvalidOperationException("Wire material changed.");}
            for(int i=0;i<a.Meshes.Length;i++){var m=a.Meshes[i];var em=e.Meshes[i];if(m.Mesh!=em.Mesh||m.Material!=em.Material||m.Layer!=em.Layer)throw new InvalidOperationException("Sniper mesh/resource identity changed.");for(int row=0;row<4;row++)for(int col=0;col<4;col++)Near(m.Matrix[row,col],em.Matrix[row,col],"sniper matrix");}
            for(int i=0;i<a.Damage.Length;i++){if(a.Damage[i].Count!=e.Damage[i].Count)throw new InvalidOperationException("DamageInfo fields changed.");foreach(var p in a.Damage[i])Value(p.Value,e.Damage[i][p.Key],"probe damage "+p.Key);}
            if(!a.BaseHits.SequenceEqual(e.BaseHits)||!a.Registrations.SequenceEqual(e.Registrations)||a.Next!=e.Next||a.Bits!=e.Bits)throw new InvalidOperationException("Probe base impact/registration/RNG changed.");
            if(empty){if(a.Lines.Length+a.Meshes.Length+a.BaseHits.Length+a.Damage.Length+a.Registrations.Length!=0)throw new InvalidOperationException("Render guard emitted.");guardPairs++;}
            lineCount+=a.Lines.Length;meshCount+=a.Meshes.Length;damageCount+=a.Damage.Length;registrationCount+=a.Registrations.Length;rngPairs++;return a;
        }
        private static void Wires(Map map,Projectile actual,Projectile frozen,ZaperX26TetherEffect tether,ThingWithComps weapon)
        {
            var deltas=new List<GV>{GV.zero,new GV(0,4,0),new GV(1e-6f,0,0),new GV(.031622774f,0,0),new GV(MathF.BitIncrement(.031622774f),0,0),new GV(1,0,0),new GV(0,0,-1),new GV(-1,3,1),new GV(20,-2,-17)};
            foreach(GV origin in new[]{GV.zero,new GV(-0f,3,float.Epsilon),map.Center.ToVector3Shifted(),new GV(-3000,500,7000)})foreach(GV delta in deltas)foreach(float offset in new[]{-.045f,0,.045f,.5f})foreach(float width in new[]{0f,.022f,.026f,-.1f})
            {GV end=origin+delta;Pair(()=>Call(typeof(Projectile_ZaperX26Probe),"DrawSaggingWire",Native(origin),Native(end),offset,width),()=>Call(typeof(FrozenWireSniper.Probe),"DrawSaggingWire",origin,end,offset,width));wirePairs++;}
            foreach(GV offset in deltas)
            {
                positions[launcher]=map.Center.ToVector3Shifted();positions[target]=positions[launcher]+offset;
                foreach(bool present in new[]{false,true}){Set(actual,"launcher",present?launcher:null);Set(frozen,"launcher",present?launcher:null);Pair(()=>Call(actual,"DrawFlightWire",positions[target]),()=>Call(frozen,"DrawFlightWire",positions[target]),!present);flightPairs++;}
                foreach(int ticks in new[]{0,300})foreach(bool equipped in new[]{false,true})
                {Set(tether,"ticksLeft",ticks);Set(tether,"sourceWeapon",equipped?weapon:null);Pair(()=>Call(tether,"DrawAt",GV.zero,false),()=>FrozenWireSniper.Tether.Draw(tether),ticks==0||!equipped);tetherPairs++;}
            }
        }
        private static void Impacts(Projectile actual,Projectile frozen,ThingWithComps weapon,Thing other)
        {
            foreach(GV euler in new[]{GV.zero,new GV(0,-720,0),new GV(0,359.99f,0),new GV(15,90,30),new GV(89.99f,37,120),new GV(90,-30,40),new GV(-90,270,0)})foreach(bool blocked in new[]{false,true})foreach(Thing hit in new Thing[]{target,null,other})foreach(Thing firing in new Thing[]{launcher,null,other})foreach(Thing gear in new Thing[]{weapon,null,other})foreach(int result in new[]{0,1,2,3})foreach(bool draft in new[]{false,true})
            {
                rotation=GQ.Euler(euler);drafted=draft;dealt=result>=2?3f:0f;wounded=(result&1)!=0;
                foreach(Projectile probe in new[]{actual,frozen}){Set(probe,"launcher",firing);Set(probe,"equipment",gear);Set(probe,"equipmentDef",gear?.def);probe.intendedTarget=new LocalTargetInfo(target);}
                int reads=rotationReads;var frame=Pair(()=>Call(actual,"Impact",hit,blocked),()=>Call(frozen,"Impact",hit,blocked));bool eligible=!blocked&&hit==target&&firing==launcher&&gear==weapon;
                if(rotationReads!=reads+2)throw new InvalidOperationException("Controlled probe rotation did not bind to both copies.");
                if(frame.BaseHits.Length!=1||frame.Damage.Length!=(eligible?1:0)||frame.Registrations.Length!=(eligible&&result==3?1:0))throw new InvalidOperationException("Probe damage/wound/registration gate expectation failed.");impactPairs++;
            }
        }
        private static void Snipers(Map map,int anchor)
        {
            foreach(IntVec3 origin in new[]{IntVec3.Zero,map.Center,new IntVec3(-100,0,900)})foreach(IntVec3 delta in new[]{IntVec3.Zero,new IntVec3(1,0,0),new IntVec3(0,9,0),new IntVec3(-1,0,1),new IntVec3(0,0,-1),new IntVec3(177,0,251)})foreach(int duration in new[]{0,1,60})foreach(int elapsed in new[]{-1,0,1,30,60,61})
            {var trail=new SniperSupportTrail(origin,origin+delta,anchor,duration);Find.TickManager.DebugSetTicksGame(anchor+elapsed);Pair(()=>Call(ActualSniper,"DrawTrail",trail),()=>Call(FrozenSniper,"DrawTrail",trail),delta.x==0&&delta.z==0);trailPairs++;}
            Pair(()=>Call(ActualSniper,"DrawTrail",new SniperSupportTrail()),()=>Call(FrozenSniper,"DrawTrail",new SniperSupportTrail()),true);trailPairs++;
            foreach(float angle in new[]{-720f,-90,0,37,90,180,359.99f,450})foreach(float length in new[]{0f,.01f,1,100})foreach(float width in new[]{-.1f,0,.025f,.18f})
            {GV center=new(17.25f,3,-29.5f);Pair(()=>Call(ActualSniper,"DrawTrailLayer",Native(center),angle,length,width,HelodSniperSupportUtility.TrailCoreMaterial),()=>Call(FrozenSniper,"DrawTrailLayer",center,angle,length,width,HelodSniperSupportUtility.TrailCoreMaterial));layerPairs++;}
            foreach(GV position in new[]{GV.zero,new GV(-0f,3,float.Epsilon),new GV(400,7,-500)})foreach(int duration in new[]{0,1,60})foreach(int elapsed in new[]{-1,0,1,15,30,59,60,61})
            {positions[target]=position;var effect=new SniperSupportEffect(target,HelodSniperSupportMode.Kill,anchor,anchor+duration,null,HelodSniperSupportDirection.North,IntVec3.Zero);Find.TickManager.DebugSetTicksGame(anchor+elapsed);Pair(()=>Call(ActualSniper,"DrawAimIcon",effect),()=>Call(FrozenSniper,"DrawAimIcon",effect));aimPairs++;}
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-wire-sniper");int oldTick=Find.TickManager.TicksGame;
            void Patch(MethodInfo method,string prefix){if(method==null)throw new MissingMethodException("Fixture target for "+prefix);h.Patch(method,prefix:new HarmonyMethod(typeof(NativeWireSniperChecks),prefix));}
            Patch(AccessTools.PropertyGetter(typeof(Thing),"DrawPos"),nameof(Position));Patch(AccessTools.PropertyGetter(typeof(Pawn),"DrawPos"),nameof(Position));Patch(AccessTools.PropertyGetter(typeof(Projectile),"ExactRotation"),nameof(Rotation));Patch(AccessTools.PropertyGetter(typeof(Pawn),"Drafted"),nameof(Draft));
            Patch(AccessTools.Method(typeof(GenDraw),"DrawLineBetween",new[]{typeof(GV),typeof(GV),typeof(Mat),typeof(float)}),nameof(LineDraw));Patch(AccessTools.Method(typeof(UnityEngine.Graphics),"DrawMesh",new[]{typeof(Mesh),typeof(GM),typeof(Mat),typeof(int)}),nameof(MeshDraw));
            Patch(AccessTools.Method(typeof(Projectile),"Impact",new[]{typeof(Thing),typeof(bool)}),nameof(BaseImpact));Patch(AccessTools.Method(typeof(Thing),"TakeDamage",new[]{typeof(DamageInfo)}),nameof(Damage));Patch(AccessTools.Method(typeof(CompZaperX26),"RegisterProbeHit"),nameof(Registered));
            try
            {
                Map map=Find.CurrentMap;int anchor=Math.Max(10000,oldTick);Find.TickManager.DebugSetTicksGame(anchor);
                launcher=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,canGeneratePawnRelations:false));target=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));fixture.Add(launcher);fixture.Add(target);GenSpawn.Spawn(launcher,map.Center,map);GenSpawn.Spawn(target,map.Center+new IntVec3(2,0,0),map);
                var weapon=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.AllDefs.First(d=>d.GetCompProperties<CompProperties_ZaperX26>()!=null));fixture.Add(weapon);launcher.apparel.DestroyAll();if(launcher.def!=ThingDefOf.Human||!weapon.PawnCanWear(launcher,true))throw new InvalidOperationException("Fixture must use an actual human permitted to wear Zaper.");launcher.apparel.Wear(weapon);if(weapon.TryGetComp<CompZaperX26>().Wearer!=launcher)throw new InvalidOperationException("Actual Zaper apparel ownership failed.");
                ThingDef def=DefDatabase<ThingDef>.GetNamed("HD_ZaperX26_Probe");var actual=(Projectile)ThingMaker.MakeThing(def);ThingDef clone=(ThingDef)typeof(object).GetMethod("MemberwiseClone",All).Invoke(def,null);clone.thingClass=typeof(FrozenWireSniper.Probe);var frozen=(Projectile)ThingMaker.MakeThing(clone);frozen.def=def;foreach(Projectile p in new[]{actual,frozen}){fixture.Add(p);GenSpawn.Spawn(p,map.Center,map);probes.Add(p);}
                var tether=(ZaperX26TetherEffect)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_ZaperX26_TetherEffect"));fixture.Add(tether);GenSpawn.Spawn(tether,map.Center,map);Set(tether,"launcher",launcher);Set(tether,"target",target);Set(tether,"sourceWeapon",weapon);Set(tether,"ticksLeft",300);
                Thing other=ThingMaker.MakeThing(ThingDefOf.Steel);fixture.Add(other);
                Wires(map,actual,frozen,tether,weapon);check(wirePairs==576&&flightPairs==18&&tetherPairs==36,"native wire segments match tiny-length/normalization/sag cutoff and flight/paired-tether caller boundaries");
                Impacts(actual,frozen,weapon,other);check(impactPairs==3024&&damageCount>0&&registrationCount>0,"native probe impact Euler angle preserves damage payload, blocked/launcher/equipment guards, guilt and wound registration");
                Snipers(map,anchor);check(trailPairs==325&&layerPairs==128&&aimPairs==72,"native sniper trail/aim matrices preserve progress/duration, widths, rotation, altitude and zero/invalid trail guards");
                check(rngPairs==wirePairs+flightPairs+tetherPairs+impactPairs+trailPairs+layerPairs+aimPairs,"all wire/sniper/impact pairs preserve caller RNG continuation");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-wire-sniper.json"),JsonSerializer.Serialize(new{WirePairs=wirePairs,FlightPairs=flightPairs,TetherPairs=tetherPairs,ImpactPairs=impactPairs,RotationReads=rotationReads,TrailPairs=trailPairs,LayerPairs=layerPairs,AimPairs=aimPairs,RngPairs=rngPairs,GuardPairs=guardPairs,Lines=lineCount,Meshes=meshCount,DamageRequests=damageCount,ProbeRegistrations=registrationCount,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="68ea21c0",ControlledPositionsRotationDraftAndDamageResult=true,SuppressedBaseImpactDamageRegistrationAndGpu=true},new JsonSerializerOptions{WriteIndented=true}));
                h.UnpatchAll(h.Id);positions.Clear();probes.Clear();Find.TickManager.DebugSetTicksGame(oldTick);foreach(Thing thing in fixture.AsEnumerable().Reverse())if(!thing.Destroyed)thing.Destroy();fixture.Clear();launcher=target=null;
            }
        }
    }
}
