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
using Verse.AI;
using GV=UnityEngine.Vector3;
using G2=UnityEngine.Vector2;
using GC=UnityEngine.Color;
using NC=Waylith.Math.Color;
using NV=System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeSmallEffectChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Dictionary<Thing,GV> positions=new();
        private static readonly List<Pawn> pawns=new();
        private sealed record Helper(string Kind,GV Position,Map Map,FleckDef Def,float Scale);
        private sealed record Mote(ThingDef Def,IntVec3 Cell,Map Map,Rot4 Rot,Dictionary<string,object> State);
        private sealed record Draw(Graphic Graphic,GV Position,Rot4 Rot,Thing Thing,float Angle);
        private sealed record Frame(FleckCreationData[] Flecks,Helper[] Helpers,Mote[] Motes,Draw[] Draws,object State,int Next,int Bits);
        private static List<FleckCreationData> flecks;
        private static List<Helper> helpers;
        private static List<Mote> motes;
        private static List<Draw> draws;
        private static List<Thing> transient;
        private static bool powered,carryFlip;
        private static GV carryPosition;
        private static int cigaretteEffects,colors,carriedFrames,steamFrames,m79Frames,suppressionFrames,oilFrames,rngPairs,guardPairs,components,fleckCount,moteCount,helperCount,drawCount;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static void Set(object o,string name,object value)=>AccessTools.Field(o.GetType(),name).SetValue(o,value);
        private static object Get(object o,string name)=>AccessTools.Field(o.GetType(),name).GetValue(o);
        private static object Call(object o,string method,params object[] args){Type t=o as Type??o.GetType();return t.GetMethod(method,All).Invoke(o is Type?null:o,args);}
        private static bool Position(Thing __instance,ref GV __result)=>!positions.TryGetValue(__instance,out __result);
        private static bool Fleck(FleckCreationData __0){if(flecks==null)return true;flecks.Add(__0);return false;}
        private static void Smoke(GV __0,Map __1,float __2){helpers?.Add(new("smoke",__0,__1,null,__2));}
        private static void Dust(GV __0,Map __1,float __2){helpers?.Add(new("dust",__0,__1,null,__2));}
        private static void Static(GV __0,Map __1,FleckDef __2,float __3){helpers?.Add(new("static",__0,__1,__2,__3));}
        private static bool Cull(ref bool __result){if(flecks==null)return true;__result=true;return false;}
        private static bool Power(ref bool __result){if(flecks==null)return true;__result=powered;return false;}
        private static bool Carry(ref GV __2,ref bool __3){if(draws==null)return true;__2=carryPosition;__3=carryFlip;return false;}
        private static bool Graphic(Graphic __instance,GV __0,Rot4 __1,Thing __2,float __3){if(draws==null)return true;draws.Add(new(__instance,__0,__1,__2,__3));return false;}
        private static bool Spawn(Thing __0,IntVec3 __1,Map __2,Rot4 __3,ref Thing __result)
        {
            if(motes==null)return true;var fields=new Dictionary<string,object>();
            for(Type t=__0.GetType();t!=null&&t!=typeof(Thing);t=t.BaseType)foreach(FieldInfo f in t.GetFields(All|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic&&f.Name!="spawnRealTime"))fields[t.Name+"."+f.Name]=f.GetValue(__0);
            motes.Add(new(__0.def,__1,__2,__3,fields));transient.Add(__0);__result=__0;return false;
        }
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static void Compare(object a,object e,string scope)
        {
            if(a is float f&&e is float ef)Near(f,ef,scope);
            else if(a is GV v&&e is GV ev)Vector(v,ev,scope);
            else if(a is G2 v2&&e is G2 ev2){Near(v2.x,ev2.x,scope);Near(v2.y,ev2.y,scope);}
            else if((a is GC||a is NC)&&(e is GC||e is NC)){GC ac=a is GC c?c:new GC(((NC)a).r,((NC)a).g,((NC)a).b,((NC)a).a);GC ec=e is GC c2?c2:new GC(((NC)e).r,((NC)e).g,((NC)e).b,((NC)e).a);Near(ac.r,ec.r,scope);Near(ac.g,ec.g,scope);Near(ac.b,ec.b,scope);Near(ac.a,ec.a,scope);}
            else if(a is Dictionary<string,object> ad&&e is Dictionary<string,object> ed){if(ad.Count!=ed.Count)throw new InvalidOperationException(scope+" state count changed.");foreach(var pair in ad)Compare(pair.Value,ed[pair.Key],scope+" "+pair.Key);}
            else if(!Equals(a,e))throw new InvalidOperationException(scope+" discrete field changed: "+a+" != "+e);
        }
        private static Frame Record(Action action,int seed,Func<object> state)
        {
            flecks=new();helpers=new();motes=new();draws=new();transient=new();Rand.PushState(seed);
            try{action();return new(flecks.ToArray(),helpers.ToArray(),motes.ToArray(),draws.ToArray(),state?.Invoke(),Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}
            finally{Rand.PopState();flecks=null;helpers=null;motes=null;draws=null;foreach(Thing t in transient)if(!t.Destroyed)t.Destroy();transient=null;}
        }
        private static void Pair(Action actual,Action frozen,int seed,Func<object> actualState=null,Func<object> frozenState=null,bool guard=false)
        {
            var a=Record(actual,seed,actualState);var e=Record(frozen,seed,frozenState);
            if(a.Flecks.Length!=e.Flecks.Length||a.Helpers.Length!=e.Helpers.Length||a.Motes.Length!=e.Motes.Length||a.Draws.Length!=e.Draws.Length)throw new InvalidOperationException("Effect submission count changed.");
            for(int i=0;i<a.Flecks.Length;i++)foreach(FieldInfo f in typeof(FleckCreationData).GetFields(BindingFlags.Public|BindingFlags.Instance))Compare(f.GetValue(a.Flecks[i]),f.GetValue(e.Flecks[i]),"fleck "+f.Name);
            for(int i=0;i<a.Helpers.Length;i++){Vector(a.Helpers[i].Position,e.Helpers[i].Position,"helper position");Near(a.Helpers[i].Scale,e.Helpers[i].Scale,"helper scale");if(a.Helpers[i] with{Position=e.Helpers[i].Position,Scale=e.Helpers[i].Scale}!=e.Helpers[i])throw new InvalidOperationException("Fleck helper payload changed.");}
            for(int i=0;i<a.Motes.Length;i++){var m=a.Motes[i];var em=e.Motes[i];if(m.Def!=em.Def||m.Cell!=em.Cell||m.Map!=em.Map||m.Rot!=em.Rot)throw new InvalidOperationException("Casing spawn changed.");Compare(m.State,em.State,"casing");}
            for(int i=0;i<a.Draws.Length;i++){Vector(a.Draws[i].Position,e.Draws[i].Position,"carried graphic");Near(a.Draws[i].Angle,e.Draws[i].Angle,"graphic angle");if(a.Draws[i] with{Position=e.Draws[i].Position,Angle=e.Draws[i].Angle}!=e.Draws[i])throw new InvalidOperationException("Carried graphic identity/rotation/payload changed.");}
            Compare(a.State,e.State,"caller state");if(a.Next!=e.Next||a.Bits!=e.Bits)throw new InvalidOperationException("Effect caller RNG continuation changed.");
            if(guard){if(a.Flecks.Length+a.Helpers.Length+a.Motes.Length+a.Draws.Length!=0)throw new InvalidOperationException("Guard emitted an effect.");Rand.PushState(seed);try{if(a.Next!=Rand.Int||a.Bits!=BitConverter.SingleToInt32Bits(Rand.Value))throw new InvalidOperationException("Guard consumed RNG.");}finally{Rand.PopState();}guardPairs++;}
            rngPairs++;fleckCount+=a.Flecks.Length;moteCount+=a.Motes.Length;helperCount+=a.Helpers.Length;drawCount+=a.Draws.Length;
        }
        private static Dictionary<string,object> State(object o,params string[] fields)=>fields.ToDictionary(f=>f,f=>Get(o,f));
        private static void Cigarettes(Map map)
        {
            foreach(Pawn pawn in pawns)
            {
                foreach(float progress in Enumerable.Range(0,257).Select(i=>i/256f).Concat(new[]{-.1f,1.1f,MathF.BitDecrement(.045f),.045f,MathF.BitIncrement(.045f)}))
                {Compare(CigaretteSmokingUtility.EmberColorFor(pawn,progress),FrozenSmallEffects.CigaretteSmokingUtility.EmberColorFor(pawn,progress),"ember color");colors++;}
                foreach(Rot4 rot in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})foreach(GV origin in new[]{map.Center.ToVector3Shifted(),new GV(.01f,3,.01f),new GV(map.Size.x-.01f,-2,map.Size.z-.01f),new GV(-.1f,-0f,-.1f)})foreach(float size in new[]{-.1f,0,.16f,.32f,1f})foreach(int seed in new[]{1,3701})
                {
                    pawn.Rotation=rot;positions[pawn]=origin;Pair(()=>Call(typeof(CigaretteSmokingUtility),"ThrowDirectionalSmoke",pawn,size),()=>Call(typeof(FrozenSmallEffects.CigaretteSmokingUtility),"ThrowDirectionalSmoke",pawn,size),seed);cigaretteEffects++;
                }
                Thing cigarette=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Cigarette"));pawn.carryTracker.TryStartCarry(cigarette);if(pawn.carryTracker.CarriedThing!=cigarette)throw new InvalidOperationException("Fixture cigarette ownership failed.");
                Job job=JobMaker.MakeJob(JobDefOf.Ingest,cigarette);var driver=new JobDriver_Ingest{pawn=pawn,job=job};var toil=new Toil{defaultDuration=1000};Set(driver,"toils",new List<Toil>{toil});Set(driver,"curToilIndex",0);Set(driver,"chewing",toil);var oldJob=pawn.jobs.curJob;var oldDriver=pawn.jobs.curDriver;pawn.jobs.curJob=job;pawn.jobs.curDriver=driver;
                try
                {
                    foreach(int elapsed in new[]{0,44,45,46,100,250,500,680,900,999,1000})foreach(Rot4 rot in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})foreach(bool flip in new[]{false,true})
                    {
                        driver.ticksLeftThisToil=1000-elapsed;pawn.Rotation=rot;carryPosition=new GV(35.25f,-0f,46.75f);carryFlip=flip;
                        if(!CigaretteSmokingUtility.TryGetSmokingProgress(pawn,cigarette,out _,out _))throw new InvalidOperationException("Constructed chewing state did not bind.");
                        object[] aa={pawn,new GV(1,2,3),cigarette},ee={pawn,new GV(1,2,3),cigarette};object ar=null,er=null;
                        Pair(()=>ar=Call(typeof(Patch_CigaretteCarriedDrawing),"Prefix",aa),()=>er=Call(typeof(FrozenSmallEffects.Patch_CigaretteCarriedDrawing),"Prefix",ee),19,()=>new Dictionary<string,object>{{"result",ar},{"drawLoc",aa[1]}},()=>new Dictionary<string,object>{{"result",er},{"drawLoc",ee[1]}});carriedFrames++;
                    }
                }
                finally{pawn.jobs.curJob=oldJob;pawn.jobs.curDriver=oldDriver;if(!cigarette.Destroyed)cigarette.Destroy();}
            }
        }
        private static void Steam(ThingWithComps parent,Map map)
        {
            var offsets=DefDatabase<ThingDef>.AllDefs.Select(d=>d.GetCompProperties<CompProperties_SteamEngineSmoke>()).Where(p=>p!=null).Select(p=>p.smokeOffset).Concat(new[]{GV.zero,new GV(-.4f,.7f,-.2f),new GV(0,-1,2)}).ToArray();
            var a=new CompSteamEngineSmoke{parent=parent};var e=new FrozenSmallEffects.Steam{parent=parent};
            foreach(GV offset in offsets)foreach(Rot4 rot in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})foreach(GV origin in new[]{map.Center.ToVector3Shifted(),new GV(.01f,3,.01f),new GV(-.1f,-0f,-.1f)})foreach(int seed in new[]{1,3701})
            {var props=new CompProperties_SteamEngineSmoke{smokeOffset=offset};a.props=e.props=props;parent.Rotation=rot;positions[parent]=origin;Pair(()=>Call(a,"ThrowSmoke"),()=>Call(e,"ThrowSmoke"),seed);steamFrames++;}
        }
        private static void M79(Pawn pawn,Map map)
        {
            ThingWithComps gun=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M79_Weapon"));pawn.equipment.DestroyAllEquipment();pawn.equipment.AddEquipment(gun);var a=gun.TryGetComp<CompM79Launcher>();var e=new FrozenSmallEffects.M79{parent=gun};var original=a.props;var verb=new Verb_Shoot();Set(verb,"currentTarget",new LocalTargetInfo(map.Center+new IntVec3(3,0,-4)));GV target=verb.CurrentTarget.CenterVector3;
            try
            {
                foreach(GV delta in new[]{GV.zero,new GV(0,4,0),new GV(1e-6f,0,0),new GV(1,2,0),new GV(0,0,-1),new GV(-1,0,1),new GV(20,-3,-17)})foreach(int delay in new[]{-1,0,1,42})foreach(int seed in new[]{1,3701})
                {
                    positions[pawn]=target-delta;var props=new CompProperties_M79Launcher{casingDelayTicks=delay,casingMoteDef=((CompProperties_M79Launcher)original).casingMoteDef};a.props=e.props=props;
                    Set(a,"loaded",true);Set(e,"loaded",true);Set(a,"casingDueTick",-1);Set(e,"casingDueTick",-1);
                    Pair(()=>a.NotifyShotFired(verb),()=>e.NotifyShotFired(verb),seed,()=>State(a,"loaded","casingDueTick","casingAngle"),()=>State(e,"loaded","casingDueTick","casingAngle"));
                    if((int)Get(a,"casingDueTick")!=Find.TickManager.TicksGame+Math.Max(1,delay)||a.Loaded||e.Loaded)throw new InvalidOperationException("M79 debit/due tick expectation failed.");
                    Pair(()=>Call(a,"SpawnCasing"),()=>Call(e,"SpawnCasing"),seed);m79Frames++;
                }
                a.props=e.props=new CompProperties_M79Launcher();Pair(()=>Call(a,"SpawnCasing"),()=>Call(e,"SpawnCasing"),1,guard:true);a.props=e.props=original;
                pawn.DeSpawn();Pair(()=>Call(a,"SpawnCasing"),()=>Call(e,"SpawnCasing"),1,guard:true);GenSpawn.Spawn(pawn,map.Center,map);
            }
            finally
            {
                a.props=original;foreach(object comp in new object[]{a,e}){object pending=comp.GetType().GetField("pendingDelayedEffects",All).GetValue(null);pending.GetType().GetMethod("Remove").Invoke(pending,new[]{comp});}if(!gun.Destroyed)gun.Destroy();
            }
        }
        private static void Suppression(Pawn pawn,Map map)
        {
            HediffDef def=DefDatabase<HediffDef>.GetNamed("HD_FlashbangSuppression");int tick=Find.TickManager.TicksGame;while(pawn.IsHashIntervalTick(200)){Find.TickManager.DebugSetTicksGame(++tick);}
            foreach(bool spawned in new[]{true,false})
            {
                if(!spawned)pawn.DeSpawn();
                foreach(float severity in new[]{0f,.001f,MathF.BitIncrement(.001f),.25f,1f})foreach(int timer in new[]{-1,0,1,24})foreach(int delta in new[]{1,24,250})foreach(Rot4 rot in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
                {
                    var a=(Hediff_FlashbangSuppression)HediffMaker.MakeHediff(def,pawn);var e=new FrozenSmallEffects.Hediff_FlashbangSuppression{def=def,pawn=pawn};e.PostMake();a.Severity=e.Severity=severity;Set(a,"ticksUntilVisual",timer);Set(e,"ticksUntilVisual",timer);pawn.Rotation=rot;positions[pawn]=new GV(35.25f,2,46.75f);
                    Pair(()=>a.TickInterval(delta),()=>e.TickInterval(delta),19,()=>new Dictionary<string,object>{{"timer",Get(a,"ticksUntilVisual")},{"age",a.ageTicks},{"severity",a.Severity}},()=>new Dictionary<string,object>{{"timer",Get(e,"ticksUntilVisual")},{"age",e.ageTicks},{"severity",e.Severity}},!spawned||severity<=.001f||timer>delta);suppressionFrames++;
                }
                if(!spawned)GenSpawn.Spawn(pawn,map.Center,map);
            }
        }
        private static void Oil(ThingWithComps parent,Map map)
        {
            var a=new CompOilFieldDevelopment{parent=parent};var e=new FrozenSmallEffects.CompOilFieldDevelopment{parent=parent};
            foreach(bool spawned in new[]{true,false})
            {
                if(!spawned)parent.DeSpawn();
                foreach(IntVec3 floor in new[]{IntVec3.Zero,new IntVec3(-2,0,1),new IntVec3(map.Size.x,0,-map.Size.z)})foreach(int state in new[]{0,1,2,3})foreach(int interval in new[]{1,250,2000})foreach(int seed in new[]{1,3701})
                {
                    var props=new CompProperties_OilFieldDevelopment{floorOffset=floor,totalDaysNeeded=float.MaxValue,mtbDaysConfirmation=float.MaxValue,mtbDaysMaintenance=float.MaxValue};a.props=e.props=props;powered=state!=1;a.needsMaintenance=e.needsMaintenance=state==2;a.developmentComplete=e.developmentComplete=state==3;a.currentProgressDays=e.currentProgressDays=.25f;
                    Pair(()=>Call(a,"TickDevelopment",interval),()=>Call(e,"TickDevelopment",interval),seed,()=>State(a,"currentProgressDays","needsMaintenance","developmentComplete"),()=>State(e,"currentProgressDays","needsMaintenance","developmentComplete"),state!=0);oilFrames++;
                    if(state==0&&(a.developmentComplete||a.needsMaintenance))throw new InvalidOperationException("Oil fixture unexpectedly entered an event/completion branch.");
                }
                if(!spawned)GenSpawn.Spawn(parent,map.Center+new IntVec3(3,0,0),map);
            }
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-small-effects");int oldTick=Find.TickManager.TicksGame;ThingWithComps parent=null;
            h.Patch(AccessTools.PropertyGetter(typeof(Thing),"DrawPos"),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Position)));h.Patch(AccessTools.PropertyGetter(typeof(Pawn),"DrawPos"),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Position)));
            h.Patch(AccessTools.Method(typeof(FleckManager),"CreateFleck",new[]{typeof(FleckCreationData)}),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Fleck)));
            foreach(Type pos in new[]{typeof(GV),typeof(IntVec3)})h.Patch(AccessTools.Method(typeof(GenView),"ShouldSpawnMotesAt",new[]{pos,typeof(Map),typeof(bool)}),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Cull)));
            h.Patch(AccessTools.Method(typeof(FleckMaker),"ThrowSmoke",new[]{typeof(GV),typeof(Map),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Smoke)));h.Patch(AccessTools.Method(typeof(FleckMaker),"ThrowDustPuff",new[]{typeof(GV),typeof(Map),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Dust)));h.Patch(AccessTools.Method(typeof(FleckMaker),"Static",new[]{typeof(GV),typeof(Map),typeof(FleckDef),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Static)));
            h.Patch(AccessTools.Method(typeof(GenSpawn),"Spawn",new[]{typeof(Thing),typeof(IntVec3),typeof(Map),typeof(Rot4),typeof(WipeMode),typeof(bool),typeof(bool)}),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Spawn)));
            h.Patch(AccessTools.Method(typeof(PawnRenderUtility),"CalculateCarriedDrawPos"),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Carry)));h.Patch(AccessTools.Method(typeof(Graphic),"Draw",new[]{typeof(GV),typeof(Rot4),typeof(Thing),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Graphic)));
            foreach(Type type in new[]{typeof(CompOilFieldDevelopment),typeof(FrozenSmallEffects.CompOilFieldDevelopment)})h.Patch(AccessTools.PropertyGetter(type,"IsPowered"),prefix:new HarmonyMethod(typeof(NativeSmallEffectChecks),nameof(Power)));
            try
            {
                Map map=Find.CurrentMap;Find.TickManager.DebugSetTicksGame(Math.Max(10000,oldTick));var habits=new HashSet<int>();for(int i=0;i<64&&habits.Count<5;i++){Pawn pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));if(habits.Add((int)CigaretteSmokingUtility.HabitFor(pawn))){GenSpawn.Spawn(pawn,map.Center+new IntVec3(pawns.Count,0,0),map);pawns.Add(pawn);}else pawn.Destroy();}if(pawns.Count!=5)throw new InvalidOperationException("Did not obtain all five real smoking habits.");
                parent=(ThingWithComps)ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(parent,map.Center+new IntVec3(3,0,0),map);
                Cigarettes(map);check(cigaretteEffects==800&&colors==1310&&carriedFrames==440,"native smoke/ember color/carried ember offset match frozen effects and graphics across all five actual smoking habits");
                Steam(parent,map);check(steamFrames>=72,"native steam outlet rotation and elevation projection match authored/synthetic offsets");
                M79(pawns[0],map);check(m79Frames==56,"native M79 shot angle/debit/due tick and casing payload match tiny/vertical/cardinal/diagonal targets");
                Suppression(pawns[0],map);check(suppressionFrames==480,"native suppression rear offset preserves severity/timer/spawn guards and interval state");
                Oil(parent,map);check(oilFrames==144,"native drilling dust offset preserves progress, power/maintenance/completion gates and caller RNG");
                check(guardPairs>0&&rngPairs==cigaretteEffects+carriedFrames+steamFrames+m79Frames*2+2+suppressionFrames+oilFrames,"all seeded effect pairs preserve caller RNG continuation and guards emit/consume nothing");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-small-effects.json"),JsonSerializer.Serialize(new{CigaretteEffects=cigaretteEffects,Colors=colors,CarriedFrames=carriedFrames,SteamFrames=steamFrames,M79Frames=m79Frames,SuppressionFrames=suppressionFrames,OilFrames=oilFrames,RngPairs=rngPairs,GuardPairs=guardPairs,Flecks=fleckCount,Motes=moteCount,HelperRequests=helperCount,GraphicDraws=drawCount,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="7304f521",ControlledDrawPositionCarryPresentationAndOilPower=true,SuppressedFleckCreationMoteSpawnAndGraphicDraw=true,ExcludedNondeterministicMoteSpawnRealTime=true},new JsonSerializerOptions{WriteIndented=true}));
                positions.Clear();h.UnpatchAll(h.Id);Find.TickManager.DebugSetTicksGame(oldTick);foreach(Pawn p in pawns)if(!p.Destroyed)p.Destroy();pawns.Clear();if(parent!=null&&!parent.Destroyed)parent.Destroy();
            }
        }
    }
}
