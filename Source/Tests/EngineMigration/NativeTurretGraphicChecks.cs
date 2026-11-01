using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using GV=UnityEngine.Vector3;
using GM=UnityEngine.Matrix4x4;
using Mat=UnityEngine.Material;
using Mesh=UnityEngine.Mesh;

namespace Helodrace.Tests
{
    internal static class NativeTurretGraphicChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed record DrawCall(Mesh Mesh,GM Matrix,Mat Material,int Layer);
        private static List<DrawCall> trace;
        private static readonly Dictionary<Thing,GV> positions=new();
        private static readonly Rot4[] Facings={Rot4.North,Rot4.East,Rot4.South,Rot4.West};
        private static readonly float[] Angles={-720.25f,-90f,0f,37.5f,180,359.999f};
        private static readonly GV[] Origins={GV.zero,new GV(120.25f,3,110.5f),new GV(-20,-2,30)};
        private static readonly int[] Ages={-1,0,1,3,5,6,7};
        private static int crowsFrames,towFrames,barrelFrames,guardFrames,cacheChanges,meshCalls,stateFields,components;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static bool MeshDraw(Mesh __0,GM __1,Mat __2,int __3){if(trace==null)return true;trace.Add(new(__0,__1,__2,__3));return false;}
        private static bool Position(Thing __instance,ref GV __result)=>!positions.TryGetValue(__instance,out __result);
        private static object Get(object o,string f)=>o.GetType().GetField(f,All).GetValue(o);
        private static void Set(object o,string f,object value)=>o.GetType().GetField(f,All).SetValue(o,value);
        private static object Copy(object o)=>typeof(object).GetMethod("MemberwiseClone",All).Invoke(o,null);
        private static CompProperties Props(CompProperties original,Type type)
        {var p=(CompProperties)Activator.CreateInstance(type);foreach(FieldInfo f in original.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly))type.GetField(f.Name).SetValue(p,f.GetValue(original));return p;}
        private static void Near(float a,float e)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences["matrix"]=Math.Max(differences.GetValueOrDefault("matrix"),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException("Turret matrix changed: "+a+" != "+e);}
        private static void State(object a,object e)
        {foreach(FieldInfo f in a.GetType().GetFields(All|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic)){if(!Equals(f.GetValue(a),Get(e,f.Name)))throw new InvalidOperationException("Turret cache/state changed: "+f.Name);stateFields++;}}
        private static void Frame(ThingComp a,ThingComp e,int expected)
        {
            trace=new();a.PostDraw();var actual=trace.ToArray();trace.Clear();e.PostDraw();if(actual.Length!=trace.Count||actual.Length!=expected)throw new InvalidOperationException("Turret layer count changed: "+actual.Length+" != "+trace.Count+" expected "+expected);
            for(int i=0;i<actual.Length;i++){if(!ReferenceEquals(actual[i].Mesh,trace[i].Mesh)||!ReferenceEquals(actual[i].Material,trace[i].Material)||actual[i].Layer!=trace[i].Layer)throw new InvalidOperationException("Turret mesh/material/layer ordering changed.");for(int c=0;c<16;c++)Near(actual[i].Matrix[c],trace[i].Matrix[c]);}meshCalls+=actual.Length;trace=null;State(a,e);
        }
        private static void Crows(Building_Crows turret,int now)
        {
            var a=new CompCrowsGraphic{parent=turret};var e=new FrozenTurretGraphics.CompCrowsGraphic{parent=turret};var defs=DefDatabase<CrowsWeaponDef>.AllDefsListForReading.ToArray();if(defs.Length<2)throw new InvalidOperationException("Actual CROWS weapon variants absent.");
            var sequence=new CrowsWeaponDef[]{null}.Concat(defs).Concat(new[]{defs[0],null}).ToArray();
            foreach(CrowsWeaponDef weapon in sequence)
            {
                Set(turret,"installedWeapon",weapon);a.NotifyWeaponChanged();e.NotifyWeaponChanged();State(a,e);cacheChanges++;
                foreach(Rot4 facing in Facings)foreach(float angle in Angles)foreach(GV origin in Origins)foreach(int age in Ages)
                {turret.Rotation=facing;turret.Top.CurRotation=angle;positions[turret]=origin;a.NotifyShotFired();e.NotifyShotFired();Set(a,"lastShotTick",now-age);Set(e,"lastShotTick",now-age);Frame(a,e,weapon==null?2:5);crowsFrames++;}
            }
            Set(turret,"installedWeapon",null);
        }
        private static void Tow(Building_TurretGun turret)
        {
            var original=(CompProperties_TowLauncherGraphic)turret.GetComp<CompTowLauncherGraphic>().props;var loader=turret.GunCompEq?.parent?.TryGetComp<CompChangeableProjectile>();if(loader==null)throw new InvalidOperationException("Actual TOW shell loader absent.");
            ThingDef shell=DefDatabase<ThingDef>.AllDefsListForReading.First(d=>d.GetModExtension<TowLoadedGraphicExtension>()!=null);var graphic=shell.GetModExtension<TowLoadedGraphicExtension>();
            var missingLine=(ThingDef)Copy(shell);missingLine.defName=shell.defName+"_NativeMissingLine";missingLine.modExtensions=(shell.modExtensions??new()).Where(x=>!(x is TowLoadedGraphicExtension)).ToList();missingLine.modExtensions.Add(new TowLoadedGraphicExtension{loadedTexPath=graphic.loadedTexPath});
            var absent=(ThingDef)Copy(shell);absent.defName=shell.defName+"_NativeNoGraphic";absent.modExtensions=(shell.modExtensions??new()).Where(x=>!(x is TowLoadedGraphicExtension)).ToList();
            ThingDef[] sequence={null,shell,missingLine,absent,shell,null,shell};
            foreach(int mode in new[]{0,1})
            {
                var p=(CompProperties_TowLauncherGraphic)Props(original,typeof(CompProperties_TowLauncherGraphic));if(mode==1)p.lowTexPath=p.highTexPath=null;
                var a=new CompTowLauncherGraphic{parent=turret,props=p};var e=new FrozenTurretGraphics.CompTowLauncherGraphic{parent=turret,props=Props(p,typeof(FrozenTurretGraphics.CompProperties_TowLauncherGraphic))};
                foreach(ThingDef loaded in sequence)
                {
                    AccessTools.Field(typeof(CompChangeableProjectile),"loadedShell").SetValue(loader,loaded??shell);loader.loadedCount=loaded==null?0:1;
                    var ext=loaded?.GetModExtension<TowLoadedGraphicExtension>();int expected=(mode==0?2:0)+(ext==null?0:(!ext.loadedLineTexPath.NullOrEmpty()?1:0)+(!ext.loadedTexPath.NullOrEmpty()?1:0));
                    foreach(Rot4 facing in Facings)foreach(float angle in Angles)foreach(GV origin in Origins)
                    {turret.Rotation=facing;turret.Top.CurRotation=angle;positions[turret]=origin;Frame(a,e,expected);towFrames++;}
                }
            }
        }
        private static void Barrel(Building_TurretGun turret,int now)
        {
            var original=(CompProperties_ReciprocatingTurretBarrel)turret.GetComp<CompReciprocatingTurretBarrel>().props;
            foreach(int profile in new[]{0,1,2})foreach(int duration in new[]{0,1,4,7})
            {
                var p=(CompProperties_ReciprocatingTurretBarrel)Props(original,typeof(CompProperties_ReciprocatingTurretBarrel));p.drawSize=new[]{1.2f,0,-3}[profile];p.drawOffset=new[]{0f,-.5f,2}[profile];p.recoilDistance=new[]{.075f,0f,.5f}[profile];p.recoilTicks=duration;
                var a=new CompReciprocatingTurretBarrel{parent=turret,props=p};var e=new FrozenTurretGraphics.CompReciprocatingTurretBarrel{parent=turret,props=Props(p,typeof(FrozenTurretGraphics.CompProperties_ReciprocatingTurretBarrel))};
                foreach(Rot4 facing in Facings)foreach(float angle in Angles)foreach(GV origin in Origins)foreach(int age in Ages)
                {turret.Rotation=facing;turret.Top.CurRotation=angle;positions[turret]=origin;a.NotifyShotFired();e.NotifyShotFired();Set(a,"lastShotTick",now-age);Set(e,"lastShotTick",now-age);Frame(a,e,2);barrelFrames++;}
            }
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            int originalTick=Find.TickManager.TicksGame,now=Math.Max(originalTick,10000);Find.TickManager.DebugSetTicksGame(now);
            try{NativeM167Checks.Run(output,check);}finally{Find.TickManager.DebugSetTicksGame(now);}
            var h=new Harmony("helodrace.tests.native-turret-graphics");h.Patch(AccessTools.Method(typeof(UnityEngine.Graphics),"DrawMesh",new[]{typeof(Mesh),typeof(GM),typeof(Mat),typeof(int)}),prefix:new HarmonyMethod(typeof(NativeTurretGraphicChecks),nameof(MeshDraw)));h.Patch(AccessTools.PropertyGetter(typeof(Thing),"DrawPos"),prefix:new HarmonyMethod(typeof(NativeTurretGraphicChecks),nameof(Position)));
            var things=new List<Thing>();
            try
            {
                Map map=Find.CurrentMap;
                foreach(string name in new[]{"HD_Building_CrowsII","HD_Building_M220TOW","HD_Building_M2HB"})
                {
                    var turret=(Building_TurretGun)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name),GenStuff.DefaultStuffFor(DefDatabase<ThingDef>.GetNamed(name)));things.Add(turret);GenSpawn.Spawn(turret,map.Center,map);if(turret.Top==null)throw new InvalidOperationException("Actual turret top absent.");
                    if(turret is Building_Crows crows)Crows(crows,now);else if(turret.GetComp<CompTowLauncherGraphic>()!=null)Tow(turret);else Barrel(turret,now);positions.Remove(turret);turret.DeSpawn();
                }
                var plain=(ThingWithComps)ThingMaker.MakeThing(ThingDefOf.Steel);things.Add(plain);
                Frame(new CompCrowsGraphic{parent=plain},new FrozenTurretGraphics.CompCrowsGraphic{parent=plain},0);guardFrames++;
                Frame(new CompTowLauncherGraphic{parent=plain},new FrozenTurretGraphics.CompTowLauncherGraphic{parent=plain},0);guardFrames++;
                Frame(new CompReciprocatingTurretBarrel{parent=plain},new FrozenTurretGraphics.CompReciprocatingTurretBarrel{parent=plain},0);guardFrames++;
                check(crowsFrames>0&&cacheChanges>=4,"native CROWS base/frame/recoil layers and weapon-change cache state match frozen submissions for all loaded weapon Defs and unarmed transitions");
                check(towFrames==1008,"native TOW load/unload/shell-cache and partial/missing material paths preserve explicit layer heights and matrices");
                check(barrelFrames==6048&&guardFrames==3,"native reciprocating barrel offset/recoil/TRS match zero/negative scales and duration boundaries, with no-top/wrong-parent guards");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-turret-graphics.json"),JsonSerializer.Serialize(new{CrowsFrames=crowsFrames,CrowsCacheChanges=cacheChanges,TowFrames=towFrames,BarrelFrames=barrelFrames,GuardFrames=guardFrames,MeshSubmissions=meshCalls,StateFields=stateFields,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="31668478",ControlledDrawPositionsAndMountLoaderState=true,SuppressedGraphics=true,FixtureTick=now},new JsonSerializerOptions{WriteIndented=true}));
                positions.Clear();trace=null;h.UnpatchAll(h.Id);Find.TickManager.DebugSetTicksGame(originalTick);foreach(Thing thing in things)if(!thing.Destroyed)thing.Destroy();
            }
        }
    }
}
