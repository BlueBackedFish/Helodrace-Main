using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using GV=UnityEngine.Vector3;
using G2=UnityEngine.Vector2;
using GC=UnityEngine.Color;
using NV=System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativePowerCutterChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly FieldInfo[] FleckFields=typeof(FleckCreationData).GetFields(BindingFlags.Public|BindingFlags.Instance);
        private static readonly Dictionary<Thing,GV> positions=new();
        private sealed record SpawnCall(ThingDef Def,IntVec3 Cell,Map Map,Rot4 Rot);
        private sealed record Capture(FleckCreationData[] Flecks,SpawnCall[] Spawns,int NextInt,int NextValueBits);
        private static List<FleckCreationData> flecks;
        private static List<SpawnCall> spawns;
        private static List<Thing> transient;
        private static Pawn worker;
        private static JobDriver actualDriver,frozenDriver;
        private static int geometryCases,equipmentCases,effectPairs,guardPairs,graphicChecks,fleckCalls,spawnCalls,rngPairs,heightBits,components;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static bool Position(Thing __instance,ref GV __result)=>!positions.TryGetValue(__instance,out __result);
        private static bool Fleck(FleckCreationData __0){if(flecks==null)return true;flecks.Add(__0);return false;}
        private static bool Spawn(Thing __0,IntVec3 __1,Map __2,Rot4 __3,ref Thing __result)
        {if(spawns==null)return true;spawns.Add(new(__0.def,__1,__2,__3));transient.Add(__0);__result=__0;return false;}
        private static void Set(object o,string field,object value)=>o.GetType().GetField(field,All).SetValue(o,value);
        private static object Call(object target,string method,params object[] args)
        {Type type=target as Type??target.GetType();return type.GetMethod(method,All).Invoke(target is Type?null:target,args);}
        private static GV Game(object v)=>v is NV n?new GV(n.X,n.Y,n.Z):(GV)v;
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static Capture Record(JobDriver owner,Action body,int seed)
        {
            worker.jobs.curDriver=owner;flecks=new();spawns=new();transient=new();Rand.PushState(seed);
            try{body();return new(flecks.ToArray(),spawns.ToArray(),Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}
            finally{Rand.PopState();flecks=null;spawns=null;foreach(Thing thing in transient)if(!thing.Destroyed)thing.Destroy();transient=null;}
        }
        private static void Effects(string method,Building target,int seed,bool guard=false)
        {
            var a=Record(actualDriver,()=>Call(actualDriver,method,target),seed);var e=Record(frozenDriver,()=>Call(frozenDriver,method,target),seed);
            if(a.Flecks.Length!=e.Flecks.Length||a.Spawns.Length!=e.Spawns.Length)throw new InvalidOperationException(method+" output count changed.");
            for(int i=0;i<a.Flecks.Length;i++)foreach(FieldInfo f in FleckFields)
            {
                object x=f.GetValue(a.Flecks[i]),y=f.GetValue(e.Flecks[i]);
                if(x is float xf&&y is float yf)Near(xf,yf,"fleck "+f.Name);else if(x is GV xv&&y is GV yv)Vector(xv,yv,"fleck "+f.Name);else if(x is G2 x2&&y is G2 y2){Near(x2.x,y2.x,"fleck vec2");Near(x2.y,y2.y,"fleck vec2");}else if(x is GC xc&&y is GC yc){Near(xc.r,yc.r,"color");Near(xc.g,yc.g,"color");Near(xc.b,yc.b,"color");Near(xc.a,yc.a,"color");}else if(!Equals(x,y))throw new InvalidOperationException(method+" fleck field changed: "+f.Name);
            }
            if(!a.Spawns.SequenceEqual(e.Spawns)||a.NextInt!=e.NextInt||a.NextValueBits!=e.NextValueBits)throw new InvalidOperationException(method+" light spawn/RNG continuation changed.");
            if(guard){Rand.PushState(seed);try{if(a.Flecks.Length!=0||a.Spawns.Length!=0||a.NextInt!=Rand.Int||a.NextValueBits!=BitConverter.SingleToInt32Bits(Rand.Value))throw new InvalidOperationException("Cutter guard emitted/consumed RNG.");}finally{Rand.PopState();}guardPairs++;}
            fleckCalls+=a.Flecks.Length;spawnCalls+=a.Spawns.Length;rngPairs++;effectPairs++;
        }
        private static CompProperties Props(CompProperties source,Type type,int profile)
        {
            var p=(CompProperties)Activator.CreateInstance(type);foreach(FieldInfo f in source.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly))type.GetField(f.Name).SetValue(p,f.GetValue(source));
            if(profile==1||profile==2)foreach(FieldInfo f in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly))if(f.FieldType==typeof(float))f.SetValue(p,profile==1?-1f:0f);else if(f.FieldType==typeof(int))f.SetValue(p,profile==1?-1:0);
            if(profile==3)foreach(var pair in new (string,object)[]{("sparksPerEmission",7),("smokePerEmission",5),("dustPerEmission",6),("sparkOriginRadius",1.1f),("dustOriginRadius",2.5f),("sparkBrightness",3.5f),("sparkOriginScreenUpOffset",3f),("breachContactCenterOffset",2f),("cuttingGraphicRightReach",1.25f)})type.GetField(pair.Item1).SetValue(p,pair.Item2);
            return p;
        }
        private static void Equipment(ThingWithComps tool,int mode,float height)
        {
            Set(actualDriver,"cuttingActive",mode!=1);Set(frozenDriver,"cuttingActive",mode!=1);
            GV start=new(10,height,20);object[] a={tool,start,37f},e={tool,start,37f};worker.jobs.curDriver=mode==2?null:actualDriver;Call(typeof(Patch_PawnRenderUtility_DrawEquipmentAiming_PowerCutterBreach),"Prefix",a);worker.jobs.curDriver=mode==2?null:frozenDriver;Call(typeof(FrozenPowerCutter.Patch_PawnRenderUtility_DrawEquipmentAiming_PowerCutterBreach),"Prefix",e);
            Vector((GV)a[1],(GV)e[1],"equipment");Near((float)a[2],(float)e[2],"aim angle");if(BitConverter.SingleToInt32Bits(((GV)a[1]).y)!=BitConverter.SingleToInt32Bits(height)||BitConverter.SingleToInt32Bits(((GV)e[1]).y)!=BitConverter.SingleToInt32Bits(height))throw new InvalidOperationException("Cutter failed to preserve game height bits.");heightBits++;
            if(mode!=0&&(((GV)a[1])!=start||(float)a[2]!=37f))throw new InvalidOperationException("Inactive cutter mutated equipment geometry.");equipmentCases++;
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-power-cutter");h.Patch(AccessTools.PropertyGetter(typeof(Thing),"DrawPos"),prefix:new HarmonyMethod(typeof(NativePowerCutterChecks),nameof(Position)));h.Patch(AccessTools.PropertyGetter(typeof(Pawn),"DrawPos"),prefix:new HarmonyMethod(typeof(NativePowerCutterChecks),nameof(Position)));
            h.Patch(AccessTools.Method(typeof(FleckManager),"CreateFleck",new[]{typeof(FleckCreationData)}),prefix:new HarmonyMethod(typeof(NativePowerCutterChecks),nameof(Fleck)));
            h.Patch(AccessTools.Method(typeof(GenSpawn),"Spawn",new[]{typeof(Thing),typeof(IntVec3),typeof(Map),typeof(Rot4),typeof(WipeMode),typeof(bool),typeof(bool)}),prefix:new HarmonyMethod(typeof(NativePowerCutterChecks),nameof(Spawn)));
            ThingWithComps tool=null;Building wall=null;FrozenPowerCutter.CompPowerCutterBreach frozenComp=null;Job previousJob=null;JobDriver previousDriver=null;
            try
            {
                Map map=Find.CurrentMap;worker=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));GenSpawn.Spawn(worker,map.Center,map);worker.equipment.DestroyAllEquipment();ThingDef toolDef=DefDatabase<ThingDef>.GetNamed("HD_PowerCutter");tool=(ThingWithComps)ThingMaker.MakeThing(toolDef,GenStuff.DefaultStuffFor(toolDef));worker.equipment.AddEquipment(tool);
                wall=(Building)ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(wall,map.Center+new IntVec3(2,0,0),map);
                var actual=tool.GetComp<CompPowerCutterBreach>();var original=actual.props;frozenComp=new FrozenPowerCutter.CompPowerCutterBreach{parent=tool};frozenComp.Initialize(Props(original,typeof(FrozenPowerCutter.CompProperties_PowerCutterBreach),0));tool.AllComps.Add(frozenComp);
                if(actual.Wielder!=worker||frozenComp.Wielder!=worker)throw new InvalidOperationException("Actual/frozen tool ownership differs.");
                previousJob=worker.jobs.curJob;previousDriver=worker.jobs.curDriver;var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_PowerCutterBreach"),wall,worker.Position);worker.jobs.curJob=job;actualDriver=new JobDriver_PowerCutterBreach{pawn=worker,job=job};frozenDriver=new FrozenPowerCutter.JobDriver_PowerCutterBreach{pawn=worker,job=job};
                var deltas=new List<GV>{GV.zero,new GV(0,5,0),new GV(.01f,0,0),new GV(.031622774f,0,0),new GV(1,2,0),new GV(-1,0,-1),new GV(0,0,-1),new GV(-1,0,0)};foreach(float angle in new[]{44.9999f,45,45.0001f,200,225,315,340})deltas.Add(GV.forward.RotatedBy(angle));
                GV[] origins={map.Center.ToVector3Shifted(),new GV(.02f,3,.02f),new GV(map.Size.x-.02f,-2,map.Size.z-.02f),new GV(-.02f,0,.3f)};
                foreach(int profile in new[]{0,1,2,3})
                {
                    actual.Initialize(Props(original,typeof(CompProperties_PowerCutterBreach),profile));frozenComp.Initialize(Props(original,typeof(FrozenPowerCutter.CompProperties_PowerCutterBreach),profile));
                    if(!ReferenceEquals(actual.CuttingGraphic,frozenComp.CuttingGraphic))throw new InvalidOperationException("Cutting graphic selection/cache changed.");graphicChecks++;
                    foreach(GV origin in origins)foreach(GV delta in deltas)foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
                    {
                        worker.Rotation=facing;positions[worker]=origin;positions[wall]=origin+delta;if(worker.DrawPos!=origin||wall.DrawPos!=origin+delta)throw new InvalidOperationException("Controlled cutter positions did not bind.");
                        foreach(string method in new[]{"CardinalTowardTarget","CuttingGraphicUpDirection"})Vector(Game(Call(typeof(CompPowerCutterBreach),method,worker,wall)),Game(Call(typeof(FrozenPowerCutter.CompPowerCutterBreach),method,worker,wall)),method);
                        foreach(bool missing in new[]{false,true})foreach(string method in new[]{"BreachContactPoint","EffectOrigin"})Vector(Game(Call(typeof(JobDriver_PowerCutterBreach),method,wall,worker,missing?null:actual)),Game(Call(typeof(FrozenPowerCutter.JobDriver_PowerCutterBreach),method,wall,worker,missing?null:frozenComp)),method);
                        geometryCases++;foreach(int mode in new[]{0,1,2})foreach(float height in new[]{-0f,float.Epsilon,9.5f})Equipment(tool,mode,height);
                        foreach(int seed in new[]{1,27})foreach(string method in new[]{"ThrowCuttingSparks","ThrowCuttingSmoke","ThrowCuttingDust","ThrowMetalFlash"})Effects(method,wall,seed);
                    }
                }
                worker.DeSpawn();foreach(string method in new[]{"ThrowCuttingSparks","ThrowCuttingSmoke","ThrowCuttingDust","ThrowMetalFlash"})Effects(method,wall,3,true);GenSpawn.Spawn(worker,map.Center,map);wall.DeSpawn();foreach(string method in new[]{"ThrowCuttingSparks","ThrowCuttingSmoke","ThrowCuttingDust","ThrowMetalFlash"})Effects(method,wall,3,true);
                check(geometryCases==960&&graphicChecks==4,"native cutter cardinal/up axes, contact/effect origins and graphic identity match frozen code at cardinal/tiny/diagonal/edge profiles");
                check(equipmentCases==8640&&heightBits==8640,"native cutter equipment offsets/aim preserve inactive guards and exact game height bits");
                check(effectPairs==7688&&guardPairs==8&&fleckCalls>0&&spawnCalls>0,"native cutting sparks/smoke/dust/metal flash data and light requests preserve bounds, clamping and guard non-consumption");
                check(rngPairs==effectPairs,"caller RNG continuations match frozen effects in every seeded pair");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-power-cutter.json"),JsonSerializer.Serialize(new{GeometryCases=geometryCases,EquipmentCases=equipmentCases,EffectPairs=effectPairs,GuardPairs=guardPairs,GraphicChecks=graphicChecks,Flecks=fleckCalls,LightRequests=spawnCalls,RngPairs=rngPairs,HeightBitPairs=heightBits,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="d4f6b98c",ControlledDrawPositionsAndDriverPresentation=true,SuppressedParticleCreationAndLightSpawn=true},new JsonSerializerOptions{WriteIndented=true}));
                positions.Clear();h.UnpatchAll(h.Id);if(worker!=null){worker.jobs.curJob=previousJob;worker.jobs.curDriver=previousDriver;if(tool!=null&&frozenComp!=null)tool.AllComps.Remove(frozenComp);if(!worker.Destroyed)worker.Destroy();}if(wall!=null&&!wall.Destroyed)wall.Destroy();worker=null;actualDriver=frozenDriver=null;
            }
        }
    }
}
