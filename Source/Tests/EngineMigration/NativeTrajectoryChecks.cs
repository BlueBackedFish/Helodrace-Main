using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using RimWorld.Planet;
using Verse;
using GV = UnityEngine.Vector3;
using NV = System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeTrajectoryChecks
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static readonly Type Frozen = typeof(FrozenTrajectory.Geometry), Grenades = typeof(InventoryGrenadeUtility), FrozenGrenades = typeof(FrozenTrajectory.Grenades);
        private static Pawn pawn;
        private static Thing bullet;
        private static GV position, exactPosition;
        private static bool controlled, capture, failSpawn, allowSource;
        private static IntVec3 source;
        private static List<Thing> transient;
        private sealed record SpawnCall(ThingDef Def, IntVec3 Cell, Map Map, Rot4 Rotation);
        private sealed record LaunchCall(Thing Launcher, GV Origin, LocalTargetInfo Used, LocalTargetInfo Intended, ProjectileHitFlags Flags, bool PreventFriendlyFire, Thing Equipment, ThingDef Cover);
        private static List<SpawnCall> spawns;
        private static List<LaunchCall> launches;
        private static int components, duplexCases, travelCases, radiusCases, edgeCases, worldEdges, scatterPairs, launchPairs, guardPairs, rngPairs, fallbackPairs, thingFallbackPairs;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences = new();
        private static NV Native(GV v) => new(v.x,v.y,v.z);
        private static object Call(Type t, string name, params object[] args) => t.GetMethod(name, All, null, args.Select(a=>a.GetType()).ToArray(), null).Invoke(null,args);
        private static void Near(float a,float e,string scope)
        {
            components++; float error=Math.Abs(a-e); maximumError=Math.Max(maximumError,error);
            if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);
            if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);
        }
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static bool Position(Thing __instance,ref GV __result){if(!controlled||__instance!=pawn)return true;__result=position;return false;}
        private static bool Exact(Projectile __instance,ref GV __result){if(!controlled||__instance!=bullet)return true;__result=exactPosition;return false;}
        private static bool Source(Pawn __0,ref IntVec3 __3,ref bool __result){if(!capture||__0!=pawn)return true;__3=source;__result=allowSource;return false;}
        private static bool Spawn(Thing __0,IntVec3 __1,Map __2,Rot4 __3,ref Thing __result)
        {if(!capture)return true;spawns.Add(new(__0.def,__1,__2,__3));transient.Add(__0);__result=failSpawn?null:__0;return false;}
        private static bool Launch(Projectile __instance,Thing __0,GV __1,LocalTargetInfo __2,LocalTargetInfo __3,ProjectileHitFlags __4,bool __5,Thing __6,ThingDef __7)
        {
            if(!capture)return true;launches.Add(new(__0,__1,__2,__3,__4,__5,__6,__7));
            // Give the unchanged real ConfigureInventoryThrow a deterministic launch state.
            AccessTools.Field(typeof(Projectile),"destination").SetValue(__instance,__2.CenterVector3);
            AccessTools.Field(typeof(Projectile),"ticksToImpact").SetValue(__instance,45);
            AccessTools.Field(typeof(Projectile_ModernGrenade),"launchOrigin").SetValue(__instance,Native(__1));return false;
        }
        private static (T Value,int Next,int Bits) Seeded<T>(int seed,Func<T> action)
        {Rand.PushState(seed);try{T value=action();return(value,Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}finally{Rand.PopState();}}
        private static void Rng<T>((T Value,int Next,int Bits) a,(T Value,int Next,int Bits) e)
        {if(a.Next!=e.Next||a.Bits!=e.Bits)throw new InvalidOperationException("Caller RNG continuation changed.");rngPairs++;}
        private static void Geometry(Map map)
        {
            GV[] values={GV.zero,new GV(-0f,4,float.Epsilon),new GV(1e-6f,-3,-1e-6f),new GV(.5f,0,-.5f),new GV(-2,8,3),new GV(100.25f,-2,90.5f),new GV(30000,30,-20000)};
            foreach(GV origin in values)foreach(GV delta in values)
            {
                GV endpoint=origin+delta;
                foreach(float spread in new[]{-1f,0,1.2f,9.9999f,10,11})foreach(float sample in new[]{-2f,-1,-.001f,0,.001f,1,2})
                {
                    NV a=DuplexProjectileRules.SecondaryEndpoint(Native(origin),Native(endpoint),spread,sample);
                    GV e=(GV)Call(Frozen,"SecondaryEndpoint",origin,endpoint,spread,sample);
                    Vector(new GV(a.X,a.Y,a.Z),e,"duplex endpoint");
                    if(new GV(a.X,a.Y,a.Z).ToIntVec3()!=e.ToIntVec3())throw new InvalidOperationException("Duplex endpoint grid cell changed.");duplexCases++;
                }
                AccessTools.Field(typeof(Projectile),"origin").SetValue(bullet,origin);exactPosition=endpoint;
                if(((Projectile)bullet).ExactPosition!=endpoint)throw new InvalidOperationException("Controlled ballistic position did not bind.");
                Near((float)AccessTools.Property(typeof(Projectile_BallisticBullet),"TravelDistance").GetValue(bullet),(float)Call(Frozen,"TravelDistance",endpoint,origin),"ballistic distance");travelCases++;
            }
            foreach(IntVec3 center in new[]{IntVec3.Zero,new IntVec3(-1,0,-1),map.Center,new IntVec3(map.Size.x-1,0,map.Size.z-1),new IntVec3(map.Size.x+3,7,-10)})
            {
                GV o=center.ToVector3Shifted();float x=map.Size.x-.5f,z=map.Size.z-.5f;
                float boundary=MathF.Sqrt(new[]{new GV(.5f,0,.5f),new GV(x,0,.5f),new GV(.5f,0,z),new GV(x,0,z)}.Max(v=>(v-o).sqrMagnitude));
                foreach(float r in new[]{-1f,0,1,50,boundary,MathF.BitDecrement(boundary),MathF.BitIncrement(boundary),100000})
                {bool a=(bool)Call(typeof(PlaceWorker_ShowExtendedTurretRadius),"CoversWholeMap",center,r,map),e=(bool)Call(Frozen,"CoversWholeMap",center,r,map);if(a!=e)throw new InvalidOperationException("Turret whole-map radius branch changed.");radiusCases++;}
            }
        }
        private static void Edge(GV target,GV origin,IntVec3 size)
        {
            var method=typeof(MapComponent_HelodMortarSupport).GetMethod("IncomingEdgeCell",All,null,new[]{typeof(NV),typeof(NV),typeof(IntVec3)},null);
            IntVec3 a=(IntVec3)method.Invoke(null,new object[]{Native(target),Native(origin),size}),e=(IntVec3)Call(Frozen,"IncomingEdgeCell",target,origin,size);
            if(a!=e||a.x<0||a.z<0||a.x>=size.x||a.z>=size.z||(a.x!=0&&a.z!=0&&a.x!=size.x-1&&a.z!=size.z-1))throw new InvalidOperationException("Mortar incoming edge cell changed: "+a+" != "+e);edgeCases++;
        }
        private static void Edges(Map map)
        {
            GV[] points={GV.zero,GV.up,-GV.up,GV.right,-GV.right,GV.forward,-GV.forward,new GV(1e-6f,1,0),new GV(1e-5f,0,0),new GV(.001f,1,0),new GV(1,2,3),new GV(-5,4,-7),new GV(1000,-3000,1700)};
            foreach(GV target in points)foreach(GV origin in points)foreach(IntVec3 size in new[]{new IntVec3(1,1,1),new IntVec3(2,1,3),new IntVec3(75,1,125),new IntVec3(126,1,200),map.Size})Edge(target,origin,size);
            GV local=Find.WorldGrid.GetTileCenter(map.Tile);
            var support=new HelodForwardBase();var component=map.GetComponent<MapComponent_HelodMortarSupport>();
            for(int i=0;i<256;i++)
            {
                var tile=new PlanetTile((int)((long)i*(Find.WorldGrid.TilesCount-1)/255));support.Tile=tile;GV remote=Find.WorldGrid.GetTileCenter(tile);Edge(local,remote,map.Size);
                IntVec3 a=(IntVec3)typeof(MapComponent_HelodMortarSupport).GetMethod("IncomingEdgeCell",All,null,new[]{typeof(HelodForwardBase)},null).Invoke(component,new object[]{support});
                if(a!=(IntVec3)Call(Frozen,"IncomingEdgeCell",local,remote,map.Size))throw new InvalidOperationException("Mortar world-tile boundary conversion changed.");worldEdges++;
            }
        }
        private static void Scatter(Map map)
        {
            foreach(IntVec3 origin in new[]{map.Center,IntVec3.Zero,new IntVec3(map.Size.x-1,0,map.Size.z-1)})foreach(int skill in new[]{0,10,20})
            {
                pawn.SetPositionDirect(origin);pawn.skills.GetSkill(SkillDefOf.Shooting).Level=skill;
                foreach(IntVec3 target in new[]{origin,origin+new IntVec3(1,0,0),origin+new IntVec3(20,0,-2),IntVec3.Zero,new IntVec3(map.Size.x-1,0,map.Size.z-1)})foreach(int seed in new[]{1,18,3701,19119})
                {
                    LocalTargetInfo intended=new(target);var a=Seeded(seed,()=> (LocalTargetInfo)Call(Grenades,"ScatteredThrowTarget",pawn,intended));var e=Seeded(seed,()=> (LocalTargetInfo)Call(FrozenGrenades,"ScatteredThrowTarget",pawn,intended));
                    if(a.Value!=e.Value)throw new InvalidOperationException("Inventory grenade scatter cell/fallback changed.");Rng(a,e);scatterPairs++;if(a.Value==intended)fallbackPairs++;
                }
            }
            Thing marker=ThingMaker.MakeThing(ThingDefOf.Steel);
            try
            {
                foreach(IntVec3 edge in new[]{IntVec3.Zero,new IntVec3(map.Size.x-1,0,map.Size.z-1)})
                {
                    marker.Position=edge;pawn.SetPositionDirect(map.Center);pawn.skills.GetSkill(SkillDefOf.Shooting).Level=0;var intended=new LocalTargetInfo(marker);
                    for(int seed=0;seed<64;seed++)
                    {
                        var a=Seeded(seed,()=> (LocalTargetInfo)Call(Grenades,"ScatteredThrowTarget",pawn,intended));var e=Seeded(seed,()=> (LocalTargetInfo)Call(FrozenGrenades,"ScatteredThrowTarget",pawn,intended));
                        if(a.Value!=e.Value)throw new InvalidOperationException("Thing-target map-edge fallback identity changed.");if(a.Value.HasThing){if(a.Value.Thing!=marker)throw new InvalidOperationException("Scatter fallback lost original target identity.");thingFallbackPairs++;}scatterPairs++;Rng(a,e);
                    }
                }
            }
            finally{if(!marker.Destroyed)marker.Destroy();}
        }
        private sealed record Frame(SpawnCall[] Spawns,LaunchCall[] Launches,bool Returned,int Stack,bool Destroyed,Dictionary<string,object> State);
        private static Frame Throw(Type type,ThingDef def,LocalTargetInfo target)
        {
            Thing grenade=ThingMaker.MakeThing(def);grenade.stackCount=2;spawns=new();launches=new();transient=new();capture=true;
            try
            {
                Projectile result=(Projectile)type.GetMethod("Launch",All).Invoke(null,new object[]{pawn,grenade,target});
                var state=result is Projectile_ModernGrenade?typeof(Projectile_ModernGrenade).GetFields(All|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic&&f.Name!="material").ToDictionary(f=>f.Name,f=>f.GetValue(result)):new();
                return new(spawns.ToArray(),launches.ToArray(),result!=null,grenade.stackCount,grenade.Destroyed,state);
            }
            finally{capture=false;foreach(Thing thing in transient)if(!thing.Destroyed)thing.Destroy();if(!grenade.Destroyed)grenade.Destroy();}
        }
        private static void Throws(Map map)
        {
            ThingDef[] defs={DefDatabase<ThingDef>.GetNamed("HD_Grenade_M67_Item"),DefDatabase<ThingDef>.GetNamed("HD_Grenade_M14_Item")};
            pawn.SetPositionDirect(map.Center);var target=new LocalTargetInfo(pawn.Position+new IntVec3(8,0,2));
            foreach(ThingDef def in defs)foreach(IntVec3 targetOffset in new[]{new IntVec3(8,0,2),new IntVec3(18,0,2)})foreach(IntVec3 offset in new[]{IntVec3.Zero,new IntVec3(1,0,0),new IntVec3(-1,0,0),new IntVec3(0,0,1),new IntVec3(0,0,-1)})foreach(GV delta in new[]{GV.zero,new GV(.5f,3,.25f),new GV(1e-6f,-2,0),new GV(0,-0f,0)})foreach(int seed in new[]{1,3701})
            {
                target=new LocalTargetInfo(pawn.Position+targetOffset);source=pawn.Position+offset;position=source.ToVector3Shifted()+delta;failSpawn=false;allowSource=true;
                var a=Seeded(seed,()=>Throw(Grenades,def,target));var e=Seeded(seed,()=>Throw(FrozenGrenades,def,target));Compare(a.Value,e.Value);Rng(a,e);launchPairs++;
            }
            foreach(bool fail in new[]{false,true})foreach(bool allow in new[]{false,true})foreach(bool valid in new[]{false,true})
            {
                failSpawn=fail;allowSource=allow;var t=valid?target:LocalTargetInfo.Invalid;var a=Seeded(52,()=>Throw(Grenades,defs[0],t));var e=Seeded(52,()=>Throw(FrozenGrenades,defs[0],t));Compare(a.Value,e.Value);Rng(a,e);guardPairs++;
                int expectedSpawns=valid&&allow?1:0,expectedLaunches=expectedSpawns==1&&!fail?1:0;
                if(a.Value.Spawns.Length!=expectedSpawns||a.Value.Launches.Length!=expectedLaunches||a.Value.Stack!=(expectedSpawns==1?1:2))throw new InvalidOperationException("Grenade guard/debit expectation failed.");
            }
        }
        private static void Compare(Frame a,Frame e)
        {
            if(!a.Spawns.SequenceEqual(e.Spawns)||a.Returned!=e.Returned||a.Stack!=e.Stack||a.Destroyed!=e.Destroyed||a.Launches.Length!=e.Launches.Length)throw new InvalidOperationException("Grenade spawn/debit/result changed.");
            for(int i=0;i<a.Launches.Length;i++){Vector(a.Launches[i].Origin,e.Launches[i].Origin,"grenade lean origin");if(a.Launches[i] with{Origin=e.Launches[i].Origin}!=e.Launches[i])throw new InvalidOperationException("Grenade launch payload changed.");}
            if(a.State.Count!=e.State.Count)throw new InvalidOperationException("Grenade state count changed.");
            foreach(var pair in a.State){object expected=e.State[pair.Key];if(pair.Value is float f)Near(f,(float)expected,"grenade "+pair.Key);else if(pair.Value is NV v){NV ev=(NV)expected;Vector(new GV(v.X,v.Y,v.Z),new GV(ev.X,ev.Y,ev.Z),"grenade state");}else if(!Equals(pair.Value,expected))throw new InvalidOperationException("Grenade configuration changed: "+pair.Key);}
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-trajectory");
            h.Patch(AccessTools.PropertyGetter(typeof(Pawn),"DrawPos"),prefix:new HarmonyMethod(typeof(NativeTrajectoryChecks),nameof(Position)));
            h.Patch(AccessTools.PropertyGetter(typeof(Projectile),"ExactPosition"),prefix:new HarmonyMethod(typeof(NativeTrajectoryChecks),nameof(Exact)));
            h.Patch(AccessTools.Method(Grenades,"TryFindThrowSource"),prefix:new HarmonyMethod(typeof(NativeTrajectoryChecks),nameof(Source)));
            h.Patch(AccessTools.Method(typeof(GenSpawn),"Spawn",new[]{typeof(Thing),typeof(IntVec3),typeof(Map),typeof(Rot4),typeof(WipeMode),typeof(bool),typeof(bool)}),prefix:new HarmonyMethod(typeof(NativeTrajectoryChecks),nameof(Spawn)));
            Type[] signature={typeof(Thing),typeof(GV),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)};
            foreach(Type type in new[]{typeof(Projectile),typeof(Projectile_ModernGrenade)})h.Patch(AccessTools.Method(type,"Launch",signature),prefix:new HarmonyMethod(typeof(NativeTrajectoryChecks),nameof(Launch)));
            try
            {
                Map map=Find.CurrentMap;pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));GenSpawn.Spawn(pawn,map.Center,map);
                bullet=ThingMaker.MakeThing(DefDatabase<ThingDef>.AllDefs.First(d=>d.thingClass==typeof(Projectile_BallisticBullet)));controlled=true;
                Geometry(map);check(duplexCases==2058&&travelCases==49,"native duplex endpoints/grid cells and ballistic horizontal distances match frozen code");
                check(radiusCases==40,"native turret whole-map coverage preserves exact boundary decisions");
                Edges(map);check(edgeCases==1101&&worldEdges==256,"native mortar tangent-plane projection preserves polar/zero/tiny/rectangular and real world-tile incoming edge cells");
                Scatter(map);check(scatterPairs==308&&thingFallbackPairs>0,"native inventory scatter preserves seeded cells and map-edge fallback target identity");
                Throws(map);check(launchPairs==160&&guardPairs==8,"native inventory launch preserves lean origin, payload, grenade state, debit and source/spawn/target guards");
                check(rngPairs==scatterPairs+launchPairs+guardPairs,"every grenade pair preserves caller RNG continuation");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-trajectory.json"),JsonSerializer.Serialize(new{DuplexCases=duplexCases,TravelCases=travelCases,RadiusCases=radiusCases,EdgeCases=edgeCases,WorldTileEdges=worldEdges,ScatterPairs=scatterPairs,LaunchPairs=launchPairs,GuardPairs=guardPairs,RngPairs=rngPairs,SameAsIntendedPairs=fallbackPairs,ThingFallbackPairs=thingFallbackPairs,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="79a88777",ControlledDrawPositionAndLaunchSource=true,SuppressedProjectileSpawnAndLaunch=true},new JsonSerializerOptions{WriteIndented=true}));
                controlled=capture=false;h.UnpatchAll(h.Id);if(pawn!=null&&!pawn.Destroyed)pawn.Destroy();if(bullet!=null&&!bullet.Destroyed)bullet.Destroy();pawn=null;bullet=null;
            }
        }
    }
}
