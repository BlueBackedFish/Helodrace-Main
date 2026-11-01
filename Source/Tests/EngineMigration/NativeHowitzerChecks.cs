using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using GV=UnityEngine.Vector3;
using G2=UnityEngine.Vector2;
using GC=UnityEngine.Color;
using GM=UnityEngine.Matrix4x4;
using Mat=UnityEngine.Material;
using Mesh=UnityEngine.Mesh;

namespace Helodrace.Tests
{
    internal static class NativeHowitzerChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed record MeshCall(Mesh Mesh,GM Matrix,Mat Material,int Layer);
        private sealed record Effect(string Kind,GV Position,FleckDef Def,float Scale);
        private sealed record SoundCall(SoundDef Def,TargetInfo Maker,bool OnCamera,float Volume,float Pitch);
        private sealed record Capture(MeshCall[] Meshes,FleckCreationData[] Flecks,Effect[] Effects,SoundCall[] Sounds,(float,int)[] Shakes,int NextInt,int NextValueBits);
        private static List<MeshCall> meshTrace;
        private static List<FleckCreationData> fleckTrace;
        private static List<Effect> effectTrace;
        private static List<SoundCall> soundTrace;
        private static List<(float,int)> shakeTrace;
        private static int warmupRemaining,originalTick,fixtureTick,warmupAssertions;
        private static bool loaded;
        private static LocalTargetInfo target;
        private static int drawFrames,effectFrames,tickPairs,notifications,meshCalls,fleckCalls,effectCalls,soundCalls,rngPairs,statePairs,components;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static bool MeshDraw(Mesh __0,GM __1,Mat __2,int __3){if(meshTrace==null)return true;meshTrace.Add(new(__0,__1,__2,__3));return false;}
        private static bool Fleck(FleckCreationData __0){if(fleckTrace==null)return true;fleckTrace.Add(__0);return false;}
        private static void Dust(GV __0,float __2){effectTrace?.Add(new("dust",__0,FleckDefOf.DustPuff,__2));}
        private static void Static(GV __0,FleckDef __2,float __3){effectTrace?.Add(new("static",__0,__2,__3));}
        private static bool Sound(SoundDef __0,SoundInfo __1){if(soundTrace==null)return true;soundTrace.Add(new(__0,__1.Maker,__1.IsOnCamera,__1.volumeFactor,__1.pitchFactor));return false;}
        private static bool Shake(float __0,int __1){if(shakeTrace==null)return true;shakeTrace.Add((__0,__1));return false;}
        private static bool Cull(ref bool __result){__result=true;return false;}
        private static bool Warmup(ref int __result){__result=warmupRemaining;return false;}
        private static bool Loaded(ref bool __result){__result=loaded;return false;}
        private static bool Target(ref LocalTargetInfo __result){__result=target;return false;}
        private static object Get(object o,string f)=>o.GetType().GetField(f,All).GetValue(o);
        private static void Set(object o,string f,object v)=>o.GetType().GetField(f,All).SetValue(o,v);
        private static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,All).Invoke(o,args);
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static Capture Record(Action body,int seed)
        {
            meshTrace=new();fleckTrace=new();effectTrace=new();soundTrace=new();shakeTrace=new();Rand.PushState(seed);
            try {body();return new(meshTrace.ToArray(),fleckTrace.ToArray(),effectTrace.ToArray(),soundTrace.ToArray(),shakeTrace.ToArray(),Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}
            finally {Rand.PopState();meshTrace=null;fleckTrace=null;effectTrace=null;soundTrace=null;shakeTrace=null;}
        }
        private static void Compare(Capture a,Capture e)
        {
            if(a.Meshes.Length!=e.Meshes.Length||a.Flecks.Length!=e.Flecks.Length||a.Effects.Length!=e.Effects.Length||a.Sounds.Length!=e.Sounds.Length||a.Shakes.Length!=e.Shakes.Length)throw new InvalidOperationException("Howitzer submission counts changed.");
            for(int i=0;i<a.Meshes.Length;i++){var x=a.Meshes[i];var y=e.Meshes[i];if(!ReferenceEquals(x.Mesh,y.Mesh)||!ReferenceEquals(x.Material,y.Material)||x.Layer!=y.Layer)throw new InvalidOperationException("Howitzer layer/material order changed.");for(int c=0;c<16;c++)Near(x.Matrix[c],y.Matrix[c],"matrix");}
            for(int i=0;i<a.Effects.Length;i++){var x=a.Effects[i];var y=e.Effects[i];if(x.Kind!=y.Kind||x.Def!=y.Def)throw new InvalidOperationException("Howitzer effect kind/Def changed.");Vector(x.Position,y.Position,"effect position");Near(x.Scale,y.Scale,"effect scale");}
            for(int i=0;i<a.Flecks.Length;i++)foreach(FieldInfo f in typeof(FleckCreationData).GetFields(BindingFlags.Public|BindingFlags.Instance))
            {
                object x=f.GetValue(a.Flecks[i]),y=f.GetValue(e.Flecks[i]);
                if(x is float xf&&y is float yf)Near(xf,yf,"fleck "+f.Name);else if(x is GV xv&&y is GV yv)Vector(xv,yv,"fleck "+f.Name);else if(x is G2 x2&&y is G2 y2){Near(x2.x,y2.x,"fleck vector2");Near(x2.y,y2.y,"fleck vector2");}else if(x is GC xc&&y is GC yc){Near(xc.r,yc.r,"fleck color");Near(xc.g,yc.g,"fleck color");Near(xc.b,yc.b,"fleck color");Near(xc.a,yc.a,"fleck color");}else if(!Equals(x,y))throw new InvalidOperationException("Howitzer fleck data changed: "+f.Name);
            }
            for(int i=0;i<a.Sounds.Length;i++){var x=a.Sounds[i];var y=e.Sounds[i];if(x.Def!=y.Def||!x.Maker.Equals(y.Maker)||x.OnCamera!=y.OnCamera)throw new InvalidOperationException("Casing sound target/Def changed.");Near(x.Volume,y.Volume,"sound volume");Near(x.Pitch,y.Pitch,"sound pitch");}
            for(int i=0;i<a.Shakes.Length;i++){Near(a.Shakes[i].Item1,e.Shakes[i].Item1,"shake");if(a.Shakes[i].Item2!=e.Shakes[i].Item2)throw new InvalidOperationException("Shake duration changed.");}
            if(a.NextInt!=e.NextInt||a.NextValueBits!=e.NextValueBits)throw new InvalidOperationException("Howitzer caller RNG continuation changed.");rngPairs++;
            meshCalls+=a.Meshes.Length;fleckCalls+=a.Flecks.Length;effectCalls+=a.Effects.Length;soundCalls+=a.Sounds.Length;
        }
        private static void State(object a,object e)
        {
            foreach(FieldInfo f in a.GetType().GetFields(All|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic&&(f.FieldType.IsPrimitive||f.FieldType==typeof(string))))
            {object x=f.GetValue(a),y=Get(e,f.Name);if(x is float xf&&y is float yf){if(BitConverter.SingleToInt32Bits(xf)!=BitConverter.SingleToInt32Bits(yf))throw new InvalidOperationException("Howitzer scalar state bits changed: "+f.Name);}else if(!Equals(x,y))throw new InvalidOperationException("Howitzer state changed: "+f.Name);statePairs++;}
        }
        private static CompProperties Props(CompProperties source,Type type,int materialMode)
        {
            var result=(CompProperties)Activator.CreateInstance(type);
            foreach(FieldInfo f in source.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
            {object value=f.GetValue(source);if(f.Name.EndsWith("TexPath")&&(materialMode==1||materialMode==2&&f.Name.Contains("Outline")))value=null;type.GetField(f.Name).SetValue(result,value);}
            return result;
        }
        private static void InitializeState(object comp,int now,int age,int warmupAge,float angle,int variation)
        {
            foreach(FieldInfo f in comp.GetType().GetFields(All|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic&&f.FieldType.IsPrimitive))f.SetValue(comp,Activator.CreateInstance(f.FieldType));
            Set(comp,"lastShotTick",now-age);Set(comp,"casingEjectTick",now-age);Set(comp,"casingDropPending",true);Set(comp,"casingEjectRotationDegrees",angle);Set(comp,"displayedRotationInitialized",variation%2==0);Set(comp,"displayedRotationDegrees",angle);Set(comp,"lastFiredAimRotationDegrees",angle-90);Set(comp,"hasLastFiredAimRotation",true);
            Set(comp,"warmupStartTick",warmupAge<0?-1:now-warmupAge);Set(comp,"warmupDurationTicks",1000);Set(comp,"chamberCycleActive",true);Set(comp,"chamberCloseStartTick",variation%3==0?now-12:-1);Set(comp,"lastAimedDistance",30f);Set(comp,"pendingAimDistance",70f);
            Set(comp,"lever1Cycles",2+variation%5);Set(comp,"lever2Cycles",1+variation%6);Set(comp,"lever3Cycles",2+variation%4);
        }
        private static void Draws(Building_TurretGun turret,CompProperties_M2A2HowitzerGraphic original,int now)
        {
            foreach(int mode in new[]{0,1,2})
            {
                var a=new CompM2A2HowitzerGraphic{parent=turret};a.Initialize(Props(original,typeof(CompProperties_M2A2HowitzerGraphic),mode));var e=new FrozenHowitzer.CompM2A2HowitzerGraphic{parent=turret};e.Initialize(Props(original,typeof(FrozenHowitzer.CompProperties_M2A2HowitzerGraphic),mode));
                warmupRemaining=1000;InitializeState(a,now,0,0,37,0);InitializeState(e,now,0,0,37,0);Record(a.PostDraw,1);Record(e.PostDraw,1); // Warm actual host material caches before seeded comparisons.
                int[] ages=new[]{-1,0,1,original.recoilTicks-1,original.recoilTicks,original.casingDropDelayTicks-1,original.casingDropDelayTicks,original.casingDropDelayTicks+original.casingBounceTicks-1,original.casingDropDelayTicks+original.casingBounceTicks,original.chamberOpenDelayTicks-1,original.chamberOpenDelayTicks,original.chamberOpenDelayTicks+original.chamberOpenTicks-1,original.chamberOpenDelayTicks+original.chamberOpenTicks,original.chamberOpenDelayTicks+original.chamberOpenTicks+original.chamberMinimumOpenTicks}.Distinct().ToArray();
                foreach(float angle in new[]{-720f,-90f,0f,37f,180f,359.99f})foreach(int age in ages)foreach(int warmupAge in new[]{-1,0,159,160,429,430,599,600,739,740,909,910,939,940,999,1000})
                {
                    int variation=age+warmupAge+2000;target=variation%2==0?new LocalTargetInfo(turret.Position+new IntVec3(20,0,0)):LocalTargetInfo.Invalid;warmupRemaining=warmupAge<0?0:1000-warmupAge;turret.Top.CurRotation=angle;
                    InitializeState(a,now,age,warmupAge,angle,variation);InitializeState(e,now,age,warmupAge,angle,variation);float expectedProgress=warmupRemaining<=0?-1f:warmupAge/1000f;float ap=(float)Call(a,"WarmupAnimationProgress",turret),ep=(float)Call(e,"WarmupAnimationProgress",turret);if(!ap.Equals(expectedProgress)||!ep.Equals(expectedProgress))throw new InvalidOperationException("Intended howitzer warmup window was not reached.");warmupAssertions++;var ac=Record(a.PostDraw,drawFrames);var ec=Record(e.PostDraw,drawFrames);Compare(ac,ec);State(a,e);
                    if(mode==1&&ac.Meshes.Length!=0||mode==0&&ac.Meshes.Length<11)throw new InvalidOperationException("Howitzer material coverage was not exercised.");drawFrames++;
                }
            }
        }
        private static void Effects(Building_TurretGun turret,CompProperties_M2A2HowitzerGraphic original,int now)
        {
            var a=new CompM2A2HowitzerGraphic{parent=turret};a.Initialize(Props(original,typeof(CompProperties_M2A2HowitzerGraphic),0));var e=new FrozenHowitzer.CompM2A2HowitzerGraphic{parent=turret};e.Initialize(Props(original,typeof(FrozenHowitzer.CompProperties_M2A2HowitzerGraphic),0));
            foreach(float angle in new[]{-90f,0,37,180,359.99f})foreach(int seed in new[]{1,17,1009})
            {
                turret.Top.CurRotation=angle;target=new LocalTargetInfo(turret.Position+new IntVec3(20,0,0));warmupRemaining=100;InitializeState(a,now,0,0,angle,0);InitializeState(e,now,0,0,angle,0);
                Compare(Record(()=>a.NotifyWarmupStarted(100),seed),Record(()=>e.NotifyWarmupStarted(100),seed));State(a,e);notifications++;
                Compare(Record(a.NotifyShotFired,seed),Record(e.NotifyShotFired,seed));State(a,e);effectFrames++;
            }
            foreach(bool nextLoaded in new[]{false,true})
            {
                loaded=nextLoaded;InitializeState(a,now,0,-1,37,1);InitializeState(e,now,0,-1,37,1);
                int end=original.chamberOpenDelayTicks+original.chamberOpenTicks+original.chamberMinimumOpenTicks+original.chamberCloseTicks+2;
                for(int age=-1;age<=end;age++)
                {Find.TickManager.DebugSetTicksGame(now+age);Compare(Record(a.CompTick,age),Record(e.CompTick,age));State(a,e);tickPairs++;}
            }
            Find.TickManager.DebugSetTicksGame(now);loaded=false;
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-howitzer");
            void P(Type type,string name,string prefix,params Type[] args)=>h.Patch(AccessTools.Method(type,name,args),prefix:new HarmonyMethod(typeof(NativeHowitzerChecks),prefix));
            P(typeof(UnityEngine.Graphics),"DrawMesh",nameof(MeshDraw),typeof(Mesh),typeof(GM),typeof(Mat),typeof(int));P(typeof(FleckManager),"CreateFleck",nameof(Fleck),typeof(FleckCreationData));
            h.Patch(AccessTools.Method(typeof(FleckMaker),"ThrowDustPuff",new[]{typeof(GV),typeof(Map),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeHowitzerChecks),nameof(Dust)));
            h.Patch(AccessTools.Method(typeof(FleckMaker),"Static",new[]{typeof(GV),typeof(Map),typeof(FleckDef),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeHowitzerChecks),nameof(Static)));
            foreach(Type position in new[]{typeof(GV),typeof(IntVec3)})P(typeof(GenView),"ShouldSpawnMotesAt",nameof(Cull),position,typeof(Map),typeof(bool));
            P(typeof(SoundStarter),"PlayOneShot",nameof(Sound),typeof(SoundDef),typeof(SoundInfo));P(typeof(CameraShaker),"DoShake",nameof(Shake),typeof(float),typeof(int));
            P(typeof(Patch_BuildingTurretGun_WarmupAnimation_M2A2),"WarmupTicksRemaining",nameof(Warmup),typeof(Building_TurretGun));P(typeof(AnimatedTurretUtility),"CurrentTarget",nameof(Target),typeof(ThingWithComps));
            foreach(Type type in new[]{typeof(CompM2A2HowitzerGraphic),typeof(FrozenHowitzer.CompM2A2HowitzerGraphic)})h.Patch(AccessTools.PropertyGetter(type,"IsNextRoundLoaded"),prefix:new HarmonyMethod(typeof(NativeHowitzerChecks),nameof(Loaded)));
            originalTick=Find.TickManager.TicksGame;int now=fixtureTick=Math.Max(originalTick,10000);Find.TickManager.DebugSetTicksGame(now);var things=new List<Thing>();
            try
            {
                Map map=Find.CurrentMap;foreach(string name in new[]{"HD_Building_M2A2LightHowitzer","HD_Building_M114Howitzer"})
                {
                    var turret=(Building_TurretGun)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));things.Add(turret);GenSpawn.Spawn(turret,map.Center,map);var props=(CompProperties_M2A2HowitzerGraphic)turret.TryGetComp<CompM2A2HowitzerGraphic>().props;if(turret.Top==null)throw new InvalidOperationException("Actual turret top missing.");Draws(turret,props,now);Effects(turret,props,now);turret.DeSpawn();
                }
                check(drawFrames>0&&meshCalls>0,"native M2A2/M114 layer order/TRS, recoil, warmup levers, chamber and casing phases match frozen geometry including absent materials");
                check(effectFrames==30&&notifications==30&&fleckCalls>0&&effectCalls>0,"seeded actual warmup/shot notifications and firing dust/smoke parameters/Defs match frozen effects");
                check(tickPairs>0&&soundCalls>0,"native casing smoke/drop sound cell and loaded/unloaded chamber tick state match frozen behavior");
                check(rngPairs==drawFrames+effectFrames+notifications+tickPairs,"seeded caller RNG continuations and scalar state bits match across all pairs");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-howitzer.json"),JsonSerializer.Serialize(new{OriginalTick=originalTick,FixtureTick=fixtureTick,WarmupAssertions=warmupAssertions,DrawFrames=drawFrames,ShotEffects=effectFrames,WarmupNotifications=notifications,TickPairs=tickPairs,MeshSubmissions=meshCalls,FleckData=fleckCalls,EffectEntries=effectCalls,SoundEntries=soundCalls,RngPairs=rngPairs,StateFields=statePairs,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="fee3972e",ControlledWarmupTargetLoadedAndCulling=true,SuppressedGraphicsFleckCreationSoundAndShake=true},new JsonSerializerOptions{WriteIndented=true}));
                h.UnpatchAll(h.Id);Find.TickManager.DebugSetTicksGame(originalTick);foreach(Thing thing in things)if(!thing.Destroyed)thing.Destroy();
            }
        }
    }
}
