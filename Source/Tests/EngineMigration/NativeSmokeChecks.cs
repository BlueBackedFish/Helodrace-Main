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
using G2=UnityEngine.Vector2;
using GC=UnityEngine.Color;
using NV=System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeSmokeChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Type ActualSmoke=typeof(DirectionalSmokeFleckUtility),FrozenSmoke=typeof(FrozenSmoke.DirectionalSmokeFleckUtility);
        private static readonly Type ActualEffects=typeof(DirectionalSmokeFleckUtility).Assembly.GetType("Helodrace.ModernWar.GrenadeExplosionEffectUtility",true),FrozenEffects=typeof(FrozenSmoke.GrenadeExplosionEffectUtility);
        private static readonly FieldInfo[] FleckFields=typeof(FleckCreationData).GetFields(BindingFlags.Public|BindingFlags.Instance);
        private sealed record SpawnCall(ThingDef Def,IntVec3 Cell,Map Map,Rot4 Rot);
        private sealed record LaunchCall(ThingDef Def,Thing Launcher,GV Origin,LocalTargetInfo Used,LocalTargetInfo Intended,ProjectileHitFlags Flags,bool Prevent,Thing Equipment,ThingDef Cover);
        private sealed record Capture(FleckCreationData[] Flecks,SpawnCall[] Spawns,LaunchCall[] Launches,int NextInt,int NextValueBits);
        private static List<FleckCreationData> flecks;
        private static List<SpawnCall> spawns;
        private static List<LaunchCall> launches;
        private static List<Thing> transient;
        private static Pawn actor;
        private static int directional,radial,visuals,fragmentFrames,framePairs,fleckCalls,spawnCalls,launchCalls,targetedLaunches,emptyCases,components,guardAssertions;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static bool Fleck(FleckCreationData __0){if(flecks==null)return true;flecks.Add(__0);return false;}
        private static bool Spawn(Thing __0,IntVec3 __1,Map __2,Rot4 __3,ref Thing __result)
        {if(spawns==null)return true;spawns.Add(new(__0.def,__1,__2,__3));transient.Add(__0);__result=__0;return false;}
        private static bool Launch(Projectile __instance,Thing __0,GV __1,LocalTargetInfo __2,LocalTargetInfo __3,ProjectileHitFlags __4,bool __5,Thing __6,ThingDef __7)
        {if(launches==null)return true;launches.Add(new(__instance.def,__0,__1,__2,__3,__4,__5,__6,__7));return false;}
        private static bool Cull(ref bool __result){__result=true;return false;}
        private static bool TargetHit(LocalTargetInfo __1,ref bool __result)
        {if(flecks==null)return true;__result=__1.Thing is Pawn p&&p!=actor&&!p.Dead;return false;}
        private static object Invoke(Type type,string name,params object[] input)
        {
            var method=type.GetMethod(name,All);object[] args=(object[])input.Clone();var parameters=method.GetParameters();
            for(int i=0;i<args.Length;i++)if(parameters[i].ParameterType==typeof(NV)&&args[i] is GV v)args[i]=new NV(v.x,v.y,v.z);
            return method.Invoke(null,args);
        }
        private static Capture Record(Action body,int seed)
        {
            flecks=new();spawns=new();launches=new();transient=new();Rand.PushState(seed);
            try{body();return new(flecks.ToArray(),spawns.ToArray(),launches.ToArray(),Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}
            finally{Rand.PopState();flecks=null;spawns=null;launches=null;foreach(Thing thing in transient)if(!thing.Destroyed)thing.Destroy();transient=null;}
        }
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static Capture Pair(Type a,Type e,string method,int seed,params object[] args)
        {
            Capture actual=Record(()=>Invoke(a,method,args),seed),expected=Record(()=>Invoke(e,method,args),seed);
            if(actual.Flecks.Length!=expected.Flecks.Length||actual.Spawns.Length!=expected.Spawns.Length||actual.Launches.Length!=expected.Launches.Length)throw new InvalidOperationException(method+" output count changed.");
            for(int i=0;i<actual.Flecks.Length;i++)foreach(var f in FleckFields)
            {
                object x=f.GetValue(actual.Flecks[i]),y=f.GetValue(expected.Flecks[i]);
                if(x is float xf&&y is float yf)Near(xf,yf,"fleck "+f.Name);else if(x is GV xv&&y is GV yv)Vector(xv,yv,"fleck "+f.Name);else if(x is G2 x2&&y is G2 y2){Near(x2.x,y2.x,"fleck vec2");Near(x2.y,y2.y,"fleck vec2");}else if(x is GC xc&&y is GC yc){Near(xc.r,yc.r,"color");Near(xc.g,yc.g,"color");Near(xc.b,yc.b,"color");Near(xc.a,yc.a,"color");}else if(!Equals(x,y))throw new InvalidOperationException(method+" fleck field changed: "+f.Name);
            }
            for(int i=0;i<actual.Spawns.Length;i++)if(actual.Spawns[i]!=expected.Spawns[i])throw new InvalidOperationException(method+" spawn Def/cell/map/rotation changed.");
            for(int i=0;i<actual.Launches.Length;i++)
            {
                var x=actual.Launches[i];var y=expected.Launches[i];Vector(x.Origin,y.Origin,"launch origin");if(x with{Origin=default}!=y with{Origin=default})throw new InvalidOperationException(method+" target/flags/launcher/cover changed.");if(x.Intended.HasThing&&x.Intended.Thing is Pawn)targetedLaunches++;
            }
            if(actual.NextInt!=expected.NextInt||actual.NextValueBits!=expected.NextValueBits)throw new InvalidOperationException(method+" caller RNG continuation changed.");
            if(actual.Flecks.Length+actual.Launches.Length+actual.Spawns.Length==0)emptyCases++;
            fleckCalls+=actual.Flecks.Length;spawnCalls+=actual.Spawns.Length;launchCalls+=actual.Launches.Length;framePairs++;return actual;
        }
        private static void Guard(Capture result,int seed)
        {
            Rand.PushState(seed);int next;int bits;try{next=Rand.Int;bits=BitConverter.SingleToInt32Bits(Rand.Value);}finally{Rand.PopState();}
            if(result.Flecks.Length!=0||result.NextInt!=next||result.NextValueBits!=bits)throw new InvalidOperationException("Smoke guard consumed RNG or emitted particles.");guardAssertions++;
        }
        private static void SmokeCases(Map map)
        {
            GV[] origins={map.Center.ToVector3Shifted(),new GV(.05f,3,.05f),new GV(map.Size.x-.05f,-2,map.Size.z-.05f),new GV(-.05f,0,.5f),GV.zero,new GV(MathF.BitDecrement(1),-0f,MathF.BitIncrement(1))};
            GV[] directions={GV.zero,new GV(0,5,0),new GV(.009999999f,3,0),new GV(.01f,0,0),new GV(MathF.BitIncrement(.01f),-2,0),new GV(1,3,0),new GV(-1,0,-1),new GV(0,0,-1)};
            var ranges=new[]{new FloatRange(-2,-1),new FloatRange(0,0),new FloatRange(.1f,2),new FloatRange(3,12)};
            foreach(string method in new[]{"ThrowDirectional","ThrowDirectionalEaseOut"})foreach(GV origin in origins)foreach(GV direction in directions)foreach(int count in new[]{-2,0,1,9})foreach(float spread in new[]{-10f,0,65})foreach(int profile in Enumerable.Range(0,4))foreach(int seed in new[]{1,27})foreach(bool missing in new[]{false,true})
            {
                FleckDef def=missing?null:FleckDefOf.Smoke;FloatRange r=ranges[profile];float radius=profile==0?-1:profile==1?0:profile==2?2:500;
                object[] args=method=="ThrowDirectional"?new object[]{origin,map,direction,count,spread,r,r,r,radius,def}:new object[]{origin,map,direction,count,spread,r,r,r,r,r,radius,def};
                Capture result=Pair(ActualSmoke,FrozenSmoke,method,seed,args);if(count<=0||!origin.ToIntVec3().InBounds(map)||method=="ThrowDirectionalEaseOut"&&def==null)Guard(result,seed);directional++;
            }
            foreach(string method in new[]{"ThrowRadial","ThrowRadialEaseOut"})foreach(GV origin in origins)foreach(int count in new[]{-2,0,1,9})foreach(float jitter in new[]{-10f,0,65})foreach(int profile in Enumerable.Range(0,4))foreach(int seed in new[]{1,27})foreach(bool missing in new[]{false,true})foreach(float angle in new[]{-360f,0,37})
            {
                FleckDef def=missing?null:FleckDefOf.Smoke;FloatRange r=ranges[profile];var radius=profile==3?new FloatRange(250,500):r;
                object[] args=method=="ThrowRadial"?new object[]{origin,map,count,jitter,r,r,r,radius,angle,def}:new object[]{origin,map,count,jitter,r,r,r,r,r,radius,def,angle};
                Capture result=Pair(ActualSmoke,FrozenSmoke,method,seed,args);if(count<=0||method=="ThrowRadialEaseOut"&&def==null||method=="ThrowRadial"&&!origin.ToIntVec3().InBounds(map))Guard(result,seed);radial++;
            }
            foreach(string method in new[]{"ThrowDirectional","ThrowDirectionalEaseOut","ThrowRadial","ThrowRadialEaseOut"})
            {
                var r=new FloatRange(1,2);object[] args=method=="ThrowDirectional"?new object[]{GV.zero,null,GV.forward,9,30f,r,r,r,1f,FleckDefOf.Smoke}:method=="ThrowDirectionalEaseOut"?new object[]{GV.zero,null,GV.forward,9,30f,r,r,r,r,r,1f,FleckDefOf.Smoke}:method=="ThrowRadial"?new object[]{GV.zero,null,9,30f,r,r,r,r,0f,FleckDefOf.Smoke}:new object[]{GV.zero,null,9,30f,r,r,r,r,r,r,FleckDefOf.Smoke,0f};Guard(Pair(ActualSmoke,FrozenSmoke,method,5,args),5);
            }
        }
        private static void Visuals(Map map)
        {
            IntVec3[] centers={map.Center,IntVec3.Zero,new IntVec3(map.Size.x-1,0,map.Size.z-1),new IntVec3(-1,0,0)};
            var profiles=new ExplosiveGrenadeVisualExtension[]{null,new(),new(){smokeCount=-1,expansionSpeed=-1,smokeScale=-1,initialRadius=-1},new(){smokeCount=100,expansionSpeed=12,smokeScale=3,initialRadius=500}};
            foreach(IntVec3 center in centers)foreach(Map m in new[]{map,null})foreach(int seed in new[]{1,27})
            {
                foreach(var extension in profiles)foreach(string method in new[]{"ThrowExpandingSmokeRing","ThrowLingeringExplosionDust"}){Pair(ActualEffects,FrozenEffects,method,seed,center,m,extension);visuals++;}
                Pair(ActualEffects,FrozenEffects,"ThrowFlashbangEffects",seed,center,m);visuals++;
            }
        }
        private static void Fragments(Map map)
        {
            ThingDef def=DefDatabase<ThingDef>.GetNamed("HD_Projectile_M67Fragment");IntVec3[] centers={map.Center,IntVec3.Zero,new IntVec3(map.Size.x-1,0,map.Size.z-1),new IntVec3(-1,0,0)};
            foreach(IntVec3 center in centers)foreach(int count in new[]{0,8,100})foreach(bool targeted in new[]{false,true})foreach(int seed in new[]{1,27})
            {
                var extension=new FragmentationGrenadeExtension{fragmentProjectile=def,fragmentCount=count,radius=12,minimumRangeFactor=.1f,targetedFragmentFraction=targeted?1:0,minimumTargetedAimChance=1,closeAimMultiplier=1,edgeAimMultiplier=1,longRangeFragmentFraction=.3f,longRangeRadius=20};
                Pair(ActualEffects,FrozenEffects,"ThrowFragments",seed,center,map,extension,null,actor);fragmentFrames++;
                foreach(GV direction in new[]{GV.zero,new GV(.001f,4,0),new GV(1,3,0),new GV(-1,0,-1)})foreach(float cone in new[]{-1f,60,360,500})
                {Pair(ActualEffects,FrozenEffects,"ThrowDirectionalFragments",seed,center,map,extension,null,actor,direction,cone,null);fragmentFrames++;}
            }
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-smoke");void P(Type t,string n,string p,params Type[] args)=>h.Patch(AccessTools.Method(t,n,args),prefix:new HarmonyMethod(typeof(NativeSmokeChecks),p));
            P(typeof(FleckManager),"CreateFleck",nameof(Fleck),typeof(FleckCreationData));
            P(typeof(GenSpawn),"Spawn",nameof(Spawn),typeof(Thing),typeof(IntVec3),typeof(Map),typeof(Rot4),typeof(WipeMode),typeof(bool),typeof(bool));
            P(typeof(Projectile),"Launch",nameof(Launch),typeof(Thing),typeof(GV),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef));
            foreach(Type pos in new[]{typeof(GV),typeof(IntVec3)})P(typeof(GenView),"ShouldSpawnMotesAt",nameof(Cull),pos,typeof(Map),typeof(bool));
            P(typeof(Verb),"CanHitTargetFrom",nameof(TargetHit),typeof(IntVec3),typeof(LocalTargetInfo));
            var pawns=new List<Pawn>();
            try
            {
                Map map=Find.CurrentMap;for(int i=0;i<2;i++){Pawn p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));pawns.Add(p);GenSpawn.Spawn(p,map.Center+new IntVec3(3+i,0,0),map);}actor=pawns[0];
                SmokeCases(map);Visuals(map);Fragments(map);
                check(directional==18432&&radial==6912,"native directional/radial smoke and ease-out data/culling/RNG match frozen code across edge cells and parameter/guard profiles");
                check(visuals==144,"native expanding ring/dust/flashbang smoke sparks colors and light requests match frozen clients");
                check(fragmentFrames==816&&launchCalls>0&&targetedLaunches>0,"native random/targeted/directional fragment origin/target cells cover/launcher/flags match frozen launch requests");
                check(guardAssertions>0&&framePairs==directional+radial+visuals+fragmentFrames+4,"guard non-emission and caller RNG continuity are checked for every pair");
                check(ActualSmoke.GetMethod("ThrowDirectional").GetParameters()[0].ParameterType==typeof(NV),"smoke helper API takes native vectors directly without legacy game-vector overloads");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-smoke.json"),JsonSerializer.Serialize(new{Pairs=framePairs,Directional=directional,Radial=radial,Visuals=visuals,FragmentFrames=fragmentFrames,Flecks=fleckCalls,SpawnRequests=spawnCalls,LaunchRequests=launchCalls,TargetedLaunches=targetedLaunches,EmptyCases=emptyCases,GuardAssertions=guardAssertions,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="3940cabc",SuppressedParticleCreationAndSpawnAndLaunch=true,ControlledMoteCullingAndTargetedHitEligibility=true},new JsonSerializerOptions{WriteIndented=true}));
                h.UnpatchAll(h.Id);actor=null;foreach(Pawn p in pawns)if(!p.Destroyed)p.Destroy();
            }
        }
    }
}
