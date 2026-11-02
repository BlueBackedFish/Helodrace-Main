using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using GV=UnityEngine.Vector3;
using G2=UnityEngine.Vector2;

namespace Helodrace.Tests
{
    internal static class NativeSnapshotChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly MethodInfo Clone=typeof(object).GetMethod("MemberwiseClone",All);
        private static readonly Type Resolver=typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponSightResolver",true),FrozenResolver=typeof(FrozenSnapshot.ModularWeaponSightResolver);
        private static int presets,frames,nodePairs,sightRecords,occupancyRecords,groups,activeGroups,components,values,rngPairs,authoredPoses;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static T Copy<T>(T value) where T:class=>(T)Clone.Invoke(value,null);
        private static object Invoke(Type type,string method,params object[] args)=>type.GetMethod(method,All).Invoke(null,args);
        private static void Set(object o,string field,object value)=>o.GetType().GetField(field,All).SetValue(o,value);
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Compare(object a,object e,string scope)
        {
            values++;if(ReferenceEquals(a,e))return;if(a==null||e==null)throw new InvalidOperationException(scope+" null mismatch.");
            if(a is float f){Near(f,(float)e,scope);return;}
            if(a is ModularAttachmentPose pose){var other=(ModularAttachmentPose)e;Compare(pose.position,other.position,scope+" position");Compare(pose.scale,other.scale,scope+" scale");Near(pose.angle,other.angle,scope+" angle");Near(pose.layer,other.layer,scope+" layer");return;}
            if(a is string||a.GetType().IsPrimitive||a.GetType().IsEnum){if(!Equals(a,e))throw new InvalidOperationException(scope+" scalar mismatch.");return;}
            if(a is IDictionary ad&&e is IDictionary ed){if(ad.Count!=ed.Count)throw new InvalidOperationException(scope+" dictionary count changed.");foreach(object key in ad.Keys){if(!ed.Contains(key))throw new InvalidOperationException(scope+" dictionary key changed.");Compare(ad[key],ed[key],scope+" weight");}return;}
            if(a is IList al&&e is IList el){if(al.Count!=el.Count)throw new InvalidOperationException(scope+" list count changed.");for(int i=0;i<al.Count;i++)Compare(al[i],el[i],scope+" item");return;}
            var af=a.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public);var ef=e.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public);
            if(af.Length==0||af.Length!=ef.Length)throw new InvalidOperationException(scope+" unhandled/changed fields: "+a.GetType());
            foreach(FieldInfo field in af){FieldInfo other=e.GetType().GetField(field.Name,BindingFlags.Instance|BindingFlags.Public)??throw new InvalidOperationException(scope+" field missing.");Compare(field.GetValue(a),other.GetValue(e),scope+"."+field.Name);}
        }
        private sealed record Pose(ModularAttachmentTransform Transform,GV Position,G2 Scale,float Angle,float Layer);
        private static List<Pose> Poses(IEnumerable<CompModularWeaponNode> comps)=>comps.SelectMany(c=>c.Props.sockets.Select(s=>s.transform).Concat(c.Props.mounts.Select(m=>m.transform))).Where(t=>t!=null).Select(t=>new Pose(t,t.position,t.scale,t.angle,t.layer)).ToList();
        private static void Unchanged(List<Pose> poses)
        {
            foreach(Pose p in poses)
            {
                float[] a={p.Transform.position.x,p.Transform.position.y,p.Transform.position.z,p.Transform.scale.x,p.Transform.scale.y,p.Transform.angle,p.Transform.layer};float[] e={p.Position.x,p.Position.y,p.Position.z,p.Scale.x,p.Scale.y,p.Angle,p.Layer};
                for(int i=0;i<a.Length;i++)if(BitConverter.SingleToInt32Bits(a[i])!=BitConverter.SingleToInt32Bits(e[i]))throw new InvalidOperationException("Snapshot mutated authored transform.");authoredPoses++;
            }
        }
        private static CompProperties_ModularWeaponNode Profile(CompProperties_ModularWeaponNode original,int profile,int index)
        {
            var p=Copy(original);if(profile==0)return p;
            p.assemblyScale=profile==1?.37f:1.7f;p.graphicAngle=profile==1?37:-90;p.graphicOffset=new GV(.013f*index,-.02f*index,.027f*index);
            p.sight=p.sight==null?null:Copy(p.sight);if(p.sight!=null)p.sight.axisOffset=new GV(-.01f*index,.02f*index,.05f*index);
            p.sockets=p.sockets.Select(Copy).ToList();p.mounts=p.mounts.Select(Copy).ToList();
            foreach(var socket in p.sockets)
            {
                socket.transform=socket.transform?.Clone();socket.railSurface=ModularRailSurface.Top;
                if(profile==3){socket.isRail=false;socket.transform=null;continue;}
                if(socket.transform!=null){socket.transform.position=new GV(.12f*index,-.03f,.07f*index);socket.transform.scale=new G2(profile==1?-.75f:1e-6f,profile==1?.4f:-2);socket.transform.angle=profile==1?90:-30;}
                socket.railLength=profile==1?.75f:.05f;
            }
            foreach(var mount in p.mounts)
            {
                mount.transform=profile==3?null:mount.transform?.Clone();mount.railSurface=ModularRailSurface.Bottom;mount.oppositeSurfaceOffset=new GV(.04f*index,.1f,-.03f*index);
                if(mount.transform!=null){mount.transform.scale=new G2(profile==1?.5f:-1.5f,profile==1?-1.25f:1e-6f);mount.transform.angle=profile==1?-90:30;}
                mount.physicalLength=profile==1?.3f:0;mount.railContactLength=.15f;mount.physicalCenterOffset=profile==1?-.07f:.04f;
            }
            return p;
        }
        private static (List<ModularRenderNode> Nodes,object Sights,int Next,int Bits) Sample(CompModularWeaponNode root,bool frozen)
        {
            Rand.PushState(3701);
            try
            {
                if(!frozen)Set(root,"renderCacheDirty",true);
                var nodes=frozen?FrozenSnapshot.Tree.Snapshot(root):root.RenderSnapshot();object resolution=Invoke(frozen?FrozenResolver:Resolver,"Resolve",root,nodes);
                return(nodes,resolution,Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));
            }
            finally{Rand.PopState();}
        }
        private static void Pair(CompModularWeaponNode root)
        {
            var a=Sample(root,false);var e=Sample(root,true);Compare(a.Nodes,e.Nodes,"render node");nodePairs+=a.Nodes.Count;
            for(int i=0;i<a.Nodes.Count;i++){Compare(a.Nodes[i].GraphicCenter,e.Nodes[i].GraphicCenter,"graphic center");Near(a.Nodes[i].GraphicLayer,e.Nodes[i].GraphicLayer,"graphic layer");}
            Compare(a.Sights,e.Sights,"sight resolution");
            foreach(string method in new[]{"BuildSightRecords","BuildOccupancies"})
            {
                object[] aa=method=="BuildSightRecords"?new object[]{a.Nodes,root}:new object[]{a.Nodes};object[] ee=method=="BuildSightRecords"?new object[]{e.Nodes,root}:new object[]{e.Nodes};
                var ar=(IList)Invoke(Resolver,method,aa);var er=(IList)Invoke(FrozenResolver,method,ee);Compare(ar,er,method);if(method=="BuildSightRecords")sightRecords+=ar.Count;else occupancyRecords+=ar.Count;
            }
            foreach(object group in (IList)a.Sights.GetType().GetField("groups").GetValue(a.Sights)){groups++;if((bool)group.GetType().GetField("isActive").GetValue(group))activeGroups++;}
            if(a.Next!=e.Next||a.Bits!=e.Bits)throw new InvalidOperationException("Snapshot/sight caller RNG continuation changed.");rngPairs++;frames++;
        }
        private static void Mounts()
        {
            foreach(ModularRailSurface mountSurface in Enum.GetValues<ModularRailSurface>())foreach(ModularRailSurface socketSurface in Enum.GetValues<ModularRailSurface>())foreach(bool missing in new[]{false,true})
            {
                var mount=new ModularAttachmentMount{railSurface=mountSurface,transform=missing?null:new ModularAttachmentTransform{position=new GV(.2f,-.03f,.4f),scale=new G2(-0f,-1.25f),angle=-90,layer=3}};var socket=new ModularAttachmentSocket{railSurface=socketSurface};
                var pose=mount.EffectiveTransform(socket);var old=FrozenSnapshot.Tree.EffectiveTransform(mount,socket).ToNativePose();Compare(pose,old,"mount effective pose");if(mount.transform!=null&&BitConverter.SingleToInt32Bits(mount.transform.scale.y)!=BitConverter.SingleToInt32Bits(-1.25f))throw new InvalidOperationException("Mount mirror mutated XML transform.");
            }
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            try
            {
                foreach(ModularWeaponPresetDef preset in DefDatabase<ModularWeaponPresetDef>.AllDefs.OrderBy(p=>p.defName,StringComparer.Ordinal))
                {
                    Thing thing=ThingMaker.MakeThing(preset.weaponDef,GenStuff.DefaultStuffFor(preset.weaponDef));var root=thing.TryGetComp<CompModularWeaponNode>();
                    try
                    {
                        if(root==null||!root.TryApplyPreset(preset,out string rejection))throw new InvalidOperationException("Actual preset creation failed: "+preset.defName);
                        var comps=root.RenderSnapshot().Select(n=>n.comp).Distinct().ToArray();var originals=comps.Select(c=>c.props).ToArray();var untouched=Poses(comps);
                        try
                        {
                            for(int profile=0;profile<4;profile++){for(int i=0;i<comps.Length;i++)comps[i].props=Profile((CompProperties_ModularWeaponNode)originals[i],profile,i+1);var poses=Poses(comps);Pair(root);Unchanged(poses);}
                        }
                        finally{for(int i=0;i<comps.Length;i++)comps[i].props=originals[i];Set(root,"renderCacheDirty",true);}
                        Unchanged(untouched);presets++;
                    }
                    finally{if(!thing.Destroyed)thing.Destroy();}
                }
                check(presets>=21&&frames==presets*4&&nodePairs>0,"native tree snapshots match frozen recursion for all actual presets and mirrored/scaled/opposite-surface/null-transform profiles");
                check(sightRecords>0&&occupancyRecords>0&&groups>0&&activeGroups>0,"native sight/occupancy records and complete sight-resolution results match frozen code");
                check(rngPairs==frames&&authoredPoses>0,"every snapshot/sight pair preserves caller RNG continuation and authored transform bits");
                Mounts();check(typeof(ModularAttachmentMount).GetMethod("EffectiveTransform").ReturnType==typeof(ModularAttachmentPose),"effective mount transforms use native value poses without legacy DTO overloads");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-snapshot.json"),JsonSerializer.Serialize(new{Presets=presets,Frames=frames,NodePairs=nodePairs,SightRecords=sightRecords,OccupancyRecords=occupancyRecords,Groups=groups,ActiveGroups=activeGroups,RngPairs=rngPairs,AuthoredPoses=authoredPoses,Values=values,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="713096d1",ActualPresetCreation=true,InstancePropsClonedAndRestored=true,NoGpuSubmission=true},new JsonSerializerOptions{WriteIndented=true}));
            }
        }
    }
}
