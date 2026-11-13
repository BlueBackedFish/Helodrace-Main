using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Numerics;
using Helodrace.Panels;
using RimWorld;
using Verse;
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
    static (int w,int h) PngSize(string path)
    {
        byte[] b=File.ReadAllBytes(path);
        return ((b[16]<<24)|(b[17]<<16)|(b[18]<<8)|b[19],(b[20]<<24)|(b[21]<<16)|(b[22]<<8)|b[23]);
    }
    // Every wall/panel texture path resolves to real PNGs; the linked atlas is the
    // engine's 4x4 grid; Graphic_Multi overlays exist for all four faces.
    static void CheckTextures(string root)
    {
        var doc=System.Xml.Linq.XDocument.Load(Path.Combine(root,"Defs/Buildings/WallPanels.xml"));
        foreach(var tex in doc.Descendants("texPath").Concat(doc.Descendants("uiIconPath")))
        {
            string cls=tex.Parent!.Element("graphicClass")?.Value??"";
            string[] files=cls=="Graphic_Multi"?new[]{"_north","_east","_south","_west"}.Select(s=>tex.Value+s).ToArray():new[]{tex.Value};
            foreach(string f in files)
            {
                string path=Path.Combine(root,"Textures",f+".png");
                Check(File.Exists(path),"Missing wall panel texture "+f);
                if(cls=="Graphic_Multi")Check(PngSize(path)==(252,252),"Overlay is not the 1.5-tile canvas: "+f);
            }
        }
        var atlas=PngSize(Path.Combine(root,"Textures/Buildings/WallPanels/HD_SandwichWall_Atlas.png"));
        Check(atlas.w==atlas.h&&atlas.w%32==0,"Linked atlas cannot hold the engine's 4x4 sub-UV grid");
        Check(!Directory.Exists(Path.Combine(root,"Meshes")),"Runtime meshes are still shipped");
    }
    static void Main(string[] args)
    {
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
        Building Edifice(ThingDef def){var b=(Building)RuntimeHelpers.GetUninitializedObject(typeof(Building));b.def=def;return b;}
        Check(!PanelRules.Buries(Edifice(new ThingDef{thingClass=typeof(Building),building=new BuildingProperties()})),"Furniture in front buried a lining");
        Check(PanelRules.Buries(Edifice(new ThingDef{thingClass=typeof(Building_Door),building=new BuildingProperties()})),"A door against the face did not bury it");
        Check(PanelRules.Buries(Edifice(new ThingDef{thingClass=typeof(Building),building=new BuildingProperties{isNaturalRock=true}})),"Rock against the face did not bury it");
        Check(!PanelRules.Buries(null),"An empty cell buried a lining");
        var frame=new Frame();
        var props=new CompProperties_PanelAnchor();
        var anchor=new CompPanelAnchor{parent=frame,props=props,anchored=true};
        Check(!anchor.Valid,"Orphan construction order accepted");
        Check(PanelText.AppendLine("","a")=="a"&&PanelText.AppendLine("a\n","b")=="a\nb"&&PanelText.AppendLine("a",null)=="a","Inspect text gained a blank line");
        var spentCore=new CompPolymerCore();typeof(CompPolymerCore).GetField("remaining",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(spentCore,0f);
        var spentWall=(ThingWithComps)RuntimeHelpers.GetUninitializedObject(typeof(ThingWithComps));typeof(ThingWithComps).GetField("comps",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(spentWall,new List<ThingComp>{spentCore});spentCore.parent=spentWall;
        float flammability=1.5f;new StatPart_SpentPolymerCore().TransformValue(StatRequest.For(spentWall),ref flammability);
        Check(flammability==0,"Burned-out foam core stayed flammable");
        EffectChecks.Run(Check);
        if(args.Length>0)
        {
            string root=Path.GetFullPath(args[0]);
            EffectChecks.CheckDefinitions(Check,root);
            CheckTextures(root);
        }
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
        Console.WriteLine($"PASS: {checks} host and engine-contract checks; no gameplay/GPU claim.");
    }
}
