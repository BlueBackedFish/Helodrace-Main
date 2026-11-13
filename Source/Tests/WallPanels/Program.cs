using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Numerics;
using Helodrace.Panels;
using RimWorld;
using Verse;
using Waylith.Graphics.Meshes;
using HarmonyLib;

internal static class Runtime
{
    [ModuleInitializer] internal static void Init()
    {
        var metadata=Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a=>a.Key,a=>a.Value);
        string root=metadata["Helodrace.GameRoot"];
        string mod=Path.GetFullPath(metadata["Helodrace.Assembly"]);
        AssemblyLoadContext.Default.Resolving+=(c,n)=>{
            string p=n.Name=="Helodrace"?mod:Path.Combine(root,n.Name+".dll");
            if(n.Name=="0Harmony")p=Path.GetFullPath(Path.Combine(root,"../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll"));
            return File.Exists(p)?c.LoadFromAssemblyPath(p):null;
        };
    }
}

internal static class Program
{
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception(message);}
    static void Main(string[] args)
    {
        string assetRoot=Path.GetFullPath(args[0]);
        for(int mask=0;mask<16;mask++)
        {
            using var mesh=ObjMeshLoader.LoadFromFile(Path.Combine(assetRoot,$"Wall_{mask:00}.obj"));
            var v=MeshArrays.GetPositions(mesh.CpuStorage);
            var normals=MeshArrays.GetNormals(mesh.CpuStorage);
            var tris=MeshArrays.GetTriangles(mesh.CpuStorage,0);
            Console.WriteLine($"Mask {mask}: X {v.Min(p=>p.X)}..{v.Max(p=>p.X)}, Z {v.Min(p=>p.Z)}..{v.Max(p=>p.Z)}");
            Check(v.Length>0&&tris.Length%3==0,$"Mask {mask}: empty or broken mesh");
            Check(v.All(p=>p.X>=-.50001f&&p.X<=.50001f&&p.Z>=-.50001f&&p.Z<=.50001f&&p.Y>=0&&p.Y<=1.25001f),$"Mask {mask}: out of cell bounds");
            for(int i=0;i<tris.Length;i+=3)
            {
                var cross=Vector3.Cross(v[tris[i+1]]-v[tris[i]],v[tris[i+2]]-v[tris[i]]);
                Check(cross.LengthSquared()>1e-10f,$"Mask {mask}: degenerate face");
                Check(Vector3.Dot(Vector3.Normalize(cross),normals[tris[i]])>.99f,$"Mask {mask}: reversed face");
            }
            for(int d=0;d<4;d++) if((mask&(1<<d))!=0)
            {
                // Every connector meets the neighboring cell at the same .18-wide port.
                var edge=v.Where(p=>d==0?MathF.Abs(p.Z-.5f)<.00001f:d==1?MathF.Abs(p.X-.5f)<.00001f:d==2?MathF.Abs(p.Z+.5f)<.00001f:MathF.Abs(p.X+.5f)<.00001f).ToArray();
                Check(edge.Length>0,$"Mask {mask}: missing port {d}");
                float width=(d%2==0?edge.Max(p=>p.X)-edge.Min(p=>p.X):edge.Max(p=>p.Z)-edge.Min(p=>p.Z));
                Check(MathF.Abs(width-.18f)<.00001f,$"Mask {mask}: cracked port {d}");
            }
        }
        Check(PanelRules.IsHost(new ThingDef{thingClass=typeof(Building_Door),building=new BuildingProperties()}),"Door is not supported");
        Check(!PanelRules.IsHost(new ThingDef{thingClass=typeof(Building),building=new BuildingProperties()}),"Furniture accepted as support");
        foreach(var door in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
        {
            Check(PanelRules.DoorFaceAllowed(door,door),"Front door face rejected");
            Check(PanelRules.DoorFaceAllowed(door,new Rot4((door.AsInt+2)%4)),"Rear door face rejected");
            Check(!PanelRules.DoorFaceAllowed(door,new Rot4((door.AsInt+1)%4)),"Door hinge side accepted");
        }
        // Validate actual installed 1.7 signatures, not stubs.
        foreach(var t in typeof(PanelRules).Assembly.GetTypes().Where(t=>t.Namespace=="Helodrace.Panels"))
        {
            foreach(var method in t.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Where(m=>m.Name=="TargetMethods"))
                foreach(MethodBase target in (IEnumerable<MethodBase>)method.Invoke(null,null)!)
                    Check(target!=null,"Missing runtime patch target in "+t.Name);
        }
        var frame=new Frame();
        var props=new CompProperties_PanelAnchor();
        var anchor=new CompPanelAnchor{parent=frame,props=props,anchored=true};
        Check(!anchor.Valid,"Orphan construction order accepted");
        EffectChecks.Run(Check);
        if(args.Length>1)EffectChecks.CheckDefinitions(Check,Path.GetFullPath(args[1]));
        var harmony=new Harmony("Helodrace.WallPanels.ContractChecks");
        try
        {
            foreach(var type in typeof(PanelRules).Assembly.GetTypes().Where(t=>t.Namespace=="Helodrace.Panels" && t.GetCustomAttributes<HarmonyPatch>().Any()))
            {
                var patched=harmony.CreateClassProcessor(type).Patch();
                Check(patched!=null&&patched.Count>0,"Patch did not bind: "+type.Name);
            }
            EffectChecks.RunPatched(Check);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        Console.WriteLine($"PASS: {checks} mesh, connector, host and engine-contract checks; no gameplay/GPU claim.");
    }
}
