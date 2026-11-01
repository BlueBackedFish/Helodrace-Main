using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.Tactical;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;
using GV=UnityEngine.Vector3;
using GM=UnityEngine.Matrix4x4;
using Mat=UnityEngine.Material;
using Mesh=UnityEngine.Mesh;

namespace Helodrace.Tests
{
    internal static class NativeTacticalPresentationChecks
    {
        private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed record RopeLine(GV From,GV To,Mat Material,float Width);
        private sealed record HeldMesh(Mesh Mesh,GM Matrix,Mat Material,int Layer);
        private static readonly Dictionary<Pawn,GV> positions=new();
        private static List<RopeLine> rope;
        private static List<HeldMesh> held;
        private static int takedowns,behind,rotations,ropeFrames,ropeLines,grenadeFrames,meshes,components;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static object Invoke(Type type,string method,params object[] args)=>type.GetMethod(method,All).Invoke(null,args);
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e){Near(a.x,e.x,"rope x");Near(a.y,e.y,"rope y");Near(a.z,e.z,"rope z");}
        private static bool Position(Pawn __instance,ref GV __result)=>!positions.TryGetValue(__instance,out __result);
        private static bool DrawLine(GV __0,GV __1,Mat __2,float __3)
        {if(rope==null)return true;rope.Add(new(__0,__1,__2,__3));return false;}
        private static bool DrawMesh(Mesh __0,GM __1,Mat __2,int __3)
        {if(held==null)return true;held.Add(new(__0,__1,__2,__3));return false;}
        private static void Takedown(Pawn attacker,Pawn target)
        {
            foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})foreach(GV origin in new[]{GV.zero,new GV(-0f,3,.125f),new GV(120.25f,-2,110.5f)})
                foreach(float angle in new[]{0f,60,119.999f,120,120.001f,180,239.999f,240,240.001f,359.99f})foreach(float distance in new[]{0f,.001f,1,5})
                {
                    target.Rotation=facing;positions[target]=origin;positions[attacker]=origin+GV.forward.RotatedBy(angle+facing.AsAngle)*distance+new GV(0,7,0);
                    bool a=(bool)Invoke(typeof(TacticalTakedownUtility),"IsBehindTarget",attacker,target),e=(bool)Invoke(typeof(FrozenPresentation.TakedownGeometry),"IsBehindTarget",attacker,target);if(a!=e)throw new InvalidOperationException("Takedown back-arc branch changed.");if(a)behind++;
                    attacker.Rotation=facing;Invoke(typeof(TacticalTakedownUtility),"BeginAnimation",attacker,target);Rot4 rotation=attacker.Rotation;
                    attacker.Rotation=facing;Invoke(typeof(FrozenPresentation.TakedownGeometry),"BeginAnimation",attacker,target);if(rotation!=attacker.Rotation)throw new InvalidOperationException("Takedown attacker facing changed.");rotations++;takedowns++;
                }
        }
        private static void RopeFrame(MapComponent_TcccDragging a,FrozenPresentation.RopeGeometry e,int expected)
        {
            rope=new();a.MapComponentDraw();var actual=rope.ToArray();rope.Clear();e.MapComponentDraw();if(actual.Length!=rope.Count||actual.Length!=expected)throw new InvalidOperationException("TCCC rope line count/gate changed.");
            for(int i=0;i<actual.Length;i++){Vector(actual[i].From,rope[i].From);Vector(actual[i].To,rope[i].To);Near(actual[i].Width,rope[i].Width,"rope width");if(!ReferenceEquals(actual[i].Material,rope[i].Material))throw new InvalidOperationException("TCCC material/order selection changed.");}
            ropeLines+=actual.Length;ropeFrames++;rope=null;
        }
        private static void Ropes(Map map,Pawn carrier,Pawn patient)
        {
            var a=new MapComponent_TcccDragging(map);var e=new FrozenPresentation.RopeGeometry(map);
            foreach(GV origin in new[]{GV.zero,new GV(120.25f,3,110.5f),new GV(-20,-2,30)})foreach(GV offset in new[]{GV.zero,new GV(.09f,0,0),new GV(1,4,0),new GV(-1,0,-1),new GV(0,0,2),new GV(.001f,-5,-.001f)})foreach(int mode in new[]{0,1,2,3,4})
            {
                positions[carrier]=origin;positions[patient]=origin+offset;var links=new List<TcccDragLink>();
                if(mode==1||mode==2)links.Add(new TcccDragLink{carrier=carrier,patient=patient,lastCarrierCell=carrier.Position});
                if(mode==2)links.Add(new TcccDragLink{carrier=patient,patient=carrier,lastCarrierCell=patient.Position});
                if(mode==3)links.Add(new TcccDragLink{patient=patient});if(mode==4)links.Add(new TcccDragLink{carrier=carrier});
                typeof(MapComponent_TcccDragging).GetField("links",All).SetValue(a,links);typeof(FrozenPresentation.RopeGeometry).GetField("links",All).SetValue(e,links);
                RopeFrame(a,e,mode==1?4:mode==2?8:0);
            }
            var detached=new List<TcccDragLink>{new(){carrier=carrier,patient=patient}};
            typeof(MapComponent_TcccDragging).GetField("links",All).SetValue(a,detached);typeof(FrozenPresentation.RopeGeometry).GetField("links",All).SetValue(e,detached);patient.DeSpawn();RopeFrame(a,e,0);GenSpawn.Spawn(patient,map.Center+new IntVec3(1,0,0),map);
        }
        private static void Grenades(Pawn pawn,Thing grenade)
        {
            JobDriver original=pawn.jobs.curDriver;Job job=JobMaker.MakeJob(JobDefOf.Wait);job.targetB=new LocalTargetInfo(grenade);
            try
            {
                foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})foreach(GV origin in new[]{GV.zero,new GV(120.25f,3,110.5f),new GV(-20,-2,30)})foreach(int mode in new[]{0,1,2,3,4,5,6})foreach(PawnRenderFlags flags in new[]{PawnRenderFlags.None,PawnRenderFlags.Portrait})
                {
                    JobDriver driver=mode==0?null:mode<4?new JobDriver_TacticalThrow{pawn=pawn,job=job,Prepared=mode!=1,Released=mode==3}:new JobDriver_ThrowInventoryGrenade{pawn=pawn,job=job,Released=mode==5};
                    if(mode==6){Job absent=JobMaker.MakeJob(JobDefOf.Wait);driver.job=absent;}
                    pawn.jobs.curDriver=driver;held=new();Patch_NewTactical_DrawGrenade.Postfix(pawn,origin,facing,flags);var actual=held.ToArray();held.Clear();FrozenPresentation.GrenadeGeometry.Postfix(pawn,origin,facing,flags);
                    int expected=(mode==2||mode==4)&&flags==PawnRenderFlags.None?1:0;
                    if(actual.Length!=held.Count||actual.Length!=expected)throw new InvalidOperationException("Held grenade lifecycle/portrait gate changed.");
                    for(int i=0;i<actual.Length;i++){for(int c=0;c<16;c++)Near(actual[i].Matrix[c],held[i].Matrix[c],"held grenade matrix");if(!ReferenceEquals(actual[i].Mesh,held[i].Mesh)||!ReferenceEquals(actual[i].Material,held[i].Material)||actual[i].Layer!=held[i].Layer)throw new InvalidOperationException("Held grenade resource/layer changed.");}
                    meshes+=actual.Length;grenadeFrames++;held=null;
                }
            }
            finally{pawn.jobs.curDriver=original;held=null;}
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-tactical-presentation");
            h.Patch(AccessTools.PropertyGetter(typeof(Pawn),"DrawPos"),prefix:new HarmonyMethod(typeof(NativeTacticalPresentationChecks),nameof(Position)));
            h.Patch(AccessTools.Method(typeof(GenDraw),"DrawLineBetween",new[]{typeof(GV),typeof(GV),typeof(Mat),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeTacticalPresentationChecks),nameof(DrawLine)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Graphics),"DrawMesh",new[]{typeof(Mesh),typeof(GM),typeof(Mat),typeof(int)}),prefix:new HarmonyMethod(typeof(NativeTacticalPresentationChecks),nameof(DrawMesh)));
            var pawns=new List<Pawn>();Thing grenade=null;
            try
            {
                Map map=Find.CurrentMap;for(int i=0;i<2;i++){Pawn p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));pawns.Add(p);GenSpawn.Spawn(p,map.Center+new IntVec3(i,0,0),map);}
                grenade=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Grenade_M67_Item"));if(grenade.Graphic==null)throw new InvalidOperationException("Actual grenade graphic absent.");
                Takedown(pawns[0],pawns[1]);Ropes(map,pawns[0],pawns[1]);Grenades(pawns[0],grenade);
                check(takedowns==480&&rotations==480&&behind>0,"native takedown back-arc gates and attacker animation facing match frozen code");
                check(ropeFrames==91&&ropeLines==216,"native TCCC rope offsets/slack bend/altitudes, link gates and material order match frozen game submissions");
                check(grenadeFrames==168&&meshes==24,"native held-grenade offset/TRS and tactical/inventory lifecycle/portrait gates match frozen mesh submissions");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-tactical-presentation.json"),JsonSerializer.Serialize(new{Takedowns=takedowns,Behind=behind,Rotations=rotations,RopeFrames=ropeFrames,RopeLines=ropeLines,GrenadeFrames=grenadeFrames,Meshes=meshes,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="14cfc7bd",ControlledDrawPositions=true,ConstructedDriverPresentationState=true,SuppressedGraphics=true},new JsonSerializerOptions{WriteIndented=true}));
                positions.Clear();rope=null;held=null;h.UnpatchAll(h.Id);foreach(Pawn pawn in pawns)if(!pawn.Destroyed)pawn.Destroy();if(grenade!=null&&!grenade.Destroyed)grenade.Destroy();
            }
        }
    }
}
