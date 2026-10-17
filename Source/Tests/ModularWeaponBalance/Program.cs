using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Xml.XPath;
using Helodrace;
using Helodrace.ModernWar;
using UnityEngine;
using Verse;

internal static class Program
{
    private const string CompClass = "Helodrace.ModernWar.CompProperties_ModularWeaponNode";
    private static readonly Dictionary<string, XElement> definitions = new();
    private static readonly Dictionary<string, XElement> parents = new();
    private static string root;
    private static int nextId, checks;
    private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static float Number(XElement e, float fallback = 0) => e == null ? fallback : float.Parse(e.Value, CultureInfo.InvariantCulture);
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    private static void Near(float a, float b, string reason) => Check(Math.Abs(a-b)<0.0001f, reason+": "+a+" != "+b);
    private static int Main(string[] args)
    {
        string managed = @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed";
        AppDomain.CurrentDomain.AssemblyResolve += (_, a) =>
        {
            string file = Path.Combine(managed, new AssemblyName(a.Name).Name+".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        return Run(args);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        try
        {
            root=Path.GetFullPath(args.Length>0 ? args[0] : ".");
            Load();
            SightTests();
            PresetTests();
            VariantTests();
            BallisticTests();
            BallisticAuthoringTests();
            Console.WriteLine("PASS: "+checks+" modular balance checks using production sight/stat code and patched preset geometry.");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Load()
    {
        var all=new XElement("Defs");
        foreach(string file in Directory.GetFiles(Path.Combine(root,"Defs"),"*.xml",SearchOption.AllDirectories))
            all.Add(XDocument.Load(file).Root.Elements().Select(e=>new XElement(e)));
        // Apply the actual balance operations, before XML inheritance as the game does.
        foreach(XElement op in XDocument.Load(Path.Combine(root,"Patches/ModularWeapon_Balance.xml")).Root.Elements())
        {
            string path=op.Element("xpath").Value;
            var document=new XDocument(all); var nodes=document.XPathSelectElements(path).ToArray();
            Check(nodes.Length>0,"Balance operation has no target: "+path);
            foreach(XElement n in nodes)
            {
                switch((string)op.Attribute("Class"))
                {
                    case "PatchOperationRemove": n.Remove(); break;
                    case "PatchOperationReplace": n.ReplaceWith(new XElement(op.Element("value").Elements().Single())); break;
                    case "PatchOperationAdd": n.Add(op.Element("value").Elements().Select(e=>new XElement(e))); break;
                    default: throw new Exception("Unsupported balance operation");
                }
            }
            all=document.Root; all.Remove();
        }
        foreach(XElement e in all.Elements())
        {
            if(e.Attribute("Name")!=null) parents[e.Attribute("Name").Value]=e;
            if(e.Element("defName")!=null) definitions[e.Element("defName").Value]=e;
        }
    }
    private static XElement Merge(XElement parent, XElement child)
    {
        var result=new XElement(parent);
        foreach(XElement item in child.Elements())
        {
            XElement old=result.Element(item.Name);
            if(old==null) { result.Add(new XElement(item)); continue; }
            if(item.Name=="statBases" || item.Name=="graphicData" || item.Name=="projectile") old.ReplaceWith(Merge(old,item));
            else if(item.Name=="comps")
            {
                foreach(XElement comp in item.Elements())
                {
                    XElement inherited=old.Elements().FirstOrDefault(c=>(string)c.Attribute("Class")== (string)comp.Attribute("Class"));
                    if(inherited==null) old.Add(new XElement(comp)); else inherited.ReplaceWith(Merge(inherited,comp));
                }
            }
            else if(item.Name=="verbs" && (string)item.Attribute("Inherit")!="False") old.Add(item.Elements().Select(e=>new XElement(e)));
            else old.ReplaceWith(new XElement(item));
        }
        return result;
    }
    private static XElement Resolve(XElement def)
    {
        string parent=(string)def.Attribute("ParentName");
        return parent!=null && parents.TryGetValue(parent,out var inherited) ? Merge(Resolve(inherited),def) : new XElement(def);
    }
    private static object Parse(Type type, XElement e)
    {
        if(type==typeof(string)) return e.Value;
        if(type==typeof(float)) return Number(e);
        if(type==typeof(int)) return (int)e;
        if(type==typeof(bool)) return (bool)e;
        if(typeof(Def).IsAssignableFrom(type))
        {
            var def=(Def)RuntimeHelpers.GetUninitializedObject(type);def.defName=e.Value;return def;
        }
        if(type.IsEnum) return Enum.Parse(type,e.Value);
        if(type==typeof(Vector2) || type==typeof(Vector3))
        {
            float[] v=e.Value.Trim('(',')').Split(',').Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
            return type==typeof(Vector2) ? (object)new Vector2(v[0],v[1]) : new Vector3(v[0],v[1],v[2]);
        }
        if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(List<>))
        {
            var list=(IList)Activator.CreateInstance(type);
            foreach(XElement item in e.Elements()) list.Add(Parse(type.GenericTypeArguments[0],item));
            return list;
        }
        object obj=Activator.CreateInstance(type);
        foreach(XElement item in e.Elements())
        {
            FieldInfo field=type.GetField(item.Name.LocalName,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(field!=null) field.SetValue(obj,Parse(field.FieldType,item));
        }
        return obj;
    }
    private static readonly string[] parsedFields={"internalStats","sight","sockets","mounts","performanceRole","graphicOffset","graphicAngle","assemblyScale","sightGroupHeightTolerance","laser","verticalOccupancy","verticalOccupancyOffset","projectileOverride","overridePriority"};
    private static CompProperties_ModularWeaponNode Properties(XElement def)
    {
        XElement c=def.Element("comps").Elements().Single(e=>(string)e.Attribute("Class")==CompClass);
        var props=new CompProperties_ModularWeaponNode();
        foreach(string key in parsedFields)
            if(c.Element(key)!=null) typeof(CompProperties_ModularWeaponNode).GetField(key).SetValue(props,Parse(typeof(CompProperties_ModularWeaponNode).GetField(key).FieldType,c.Element(key)));
        props.isAssemblyRoot=(string)c.Element("isAssemblyRoot")=="true";
        // All active definitions must now carry explicit component mass, so tests
        // do not need the game's stat worker/Unity initialization for a fallback.
        Check(props.internalStats?.massKg>0,"Missing component mass: "+def.Element("defName")?.Value);
        return props;
    }
    private static float Palette(XElement def)
    {
        string texture=(string)def.Element("graphicData")?.Element("texPath");
        string file=Path.Combine(root,"Textures",texture+".png");
        if(!File.Exists(file)) return 1;
        byte[] header=File.ReadAllBytes(file);
        int Read(int n) => header[n]<<24 | header[n+1]<<16 | header[n+2]<<8 | header[n+3];
        return Math.Max(Read(16),Read(20))/1024f;
    }
    private static ModularRenderNode Node(CompProperties_ModularWeaponNode props)
    {
        var thing=Bare<ThingWithComps>(); thing.def=Bare<ThingDef>(); thing.thingIDNumber=++nextId;
        var comp=new CompModularWeaponNode { parent=thing,props=props };
        return new ModularRenderNode { thing=thing,comp=comp,transform=ModularTransform2D.Identity };
    }
    private static List<ModularRenderNode> BuildAssembly(string weapon, XElement preset)
    {
        XElement def=Resolve(definitions[weapon]); var node=Node(Properties(def));
        var transform=ModularTransform2D.Identity; transform.scale=Vector2.one*node.Props.assemblyScale;node.transform=transform;
        var nodes=new List<ModularRenderNode>{node};
        void Children(ModularRenderNode parent,XElement parentDef,XElement entries)
        {
            if(entries==null)return;
            foreach(XElement entry in entries.Elements())
            {
                XElement childDef=Resolve(definitions[entry.Element("part").Value]); var child=Node(Properties(childDef));
                string socketId=(string)entry.Element("socketId"),mountId=(string)entry.Element("mountId");
                var socket=parent.Props.SocketNamed(socketId); var mount=child.Props.MountNamed(mountId);
                Check(socket!=null && mount!=null,"Preset socket/mount exists");
                var effective=socket.transform.Clone(); float offset=Number(entry.Element("railOffset"));
                if(socket.isRail){effective.angle=0;effective.position.x+=offset;}
                float ratio=Palette(childDef)/Palette(parentDef);
                if(mount.UsesOppositeSurface(socket)) effective.position+=mount.oppositeSurfaceOffset*ratio;
                child.transform=parent.transform.Attach(effective,mount.EffectiveTransform(socket),child.Props.graphicAngle,ratio);
                child.parentComp=parent.comp;child.parentSocketId=socketId;child.mountId=mount.id;child.attachedToRail=socket.isRail;
                float half=mount.EffectivePhysicalLength*ratio*.5f;
                var left=socket.transform.position;var right=left;
                left.x+=offset+mount.physicalCenterOffset*ratio-half;
                right.x+=offset+mount.physicalCenterOffset*ratio+half;
                child.occupiedRailStart=parent.transform.TransformPoint(left);
                child.occupiedRailEnd=parent.transform.TransformPoint(right);
                nodes.Add(child); Children(child,childDef,entry.Element("children"));
            }
        }
        Children(node,def,preset.Element("parts")); return nodes;
    }
    private static object SightResolution(List<ModularRenderNode> nodes) => typeof(CompModularWeaponNode).Assembly
        .GetType("Helodrace.ModernWar.ModularWeaponSightResolver").GetMethod("Resolve",BindingFlags.Public|BindingFlags.Static)
        .Invoke(null,new object[]{nodes[0].comp,nodes});
    private static ModularWeaponSightPerformance SightProfile(List<ModularRenderNode> nodes)
    {
        object r=SightResolution(nodes);return (ModularWeaponSightPerformance)r.GetType().GetField("performance").GetValue(r);
    }
    private static ModularWeaponConvertedStats Convert(List<ModularRenderNode> nodes)
    {
        object sights=SightResolution(nodes);
        var type=typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponPartPerformance");
        object weights=type.GetMethod("ResolveWeights",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,
            new object[]{nodes,sights.GetType().GetField("performanceWeights").GetValue(sights)});
        return (ModularWeaponConvertedStats)typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponStatConverter")
            .GetMethod("Resolve",BindingFlags.Public|BindingFlags.Static).Invoke(null,new[]{nodes[0].comp,(object)nodes,weights,1f,sights});
    }
    private static ModularRenderNode TestSight(ModularSightKind kind,float x,float z)
    {
        var n=Node(new CompProperties_ModularWeaponNode { internalStats=new ModularWeaponInternalStats { massKg=.15f,ergonomics=-2 },
            sight=new ModularWeaponSightProperties { kind=kind,axisOffset=new Vector3(x,0,z),selectionPriority=25 } });
        return n;
    }
    private static void SightTests()
    {
        var r=Node(new CompProperties_ModularWeaponNode { isAssemblyRoot=true,internalStats=new ModularWeaponInternalStats {massKg=1,ergonomics=55,aimingPrecision=60,targetAcquisition=55} });
        var scope=TestSight(ModularSightKind.Scope,-.1f,.18f);var dot=TestSight(ModularSightKind.Reflex,-.1f,.1f);
        var single=new List<ModularRenderNode>{r,scope}; var s=SightProfile(single);
        Check(s.Touch<1 && s.Short<1 && s.Warmup>1 && s.Long>1,"A lone fixed scope pays close-range and warmup costs");
        var d=SightProfile(new(){r,dot});Check(d.Long<1 && d.Short>1,"A lone reflex has a long-range disadvantage");
        var both=SightProfile(new(){r,scope,dot});
        Near(Math.Max(s.Touch,d.Touch),both.Touch,"Groups take maximum touch factor");
        Near(Math.Max(s.Short,d.Short),both.Short,"Groups take maximum short factor");
        Near(Math.Max(s.Long,d.Long),both.Long,"Groups take maximum long factor");
        Near(Math.Min(s.Warmup,d.Warmup),both.Warmup,"Groups take fastest independent acquisition factor");
        var copy=TestSight(ModularSightKind.Scope,-.1f,.24f);
        Near(s.Long,SightProfile(new(){r,scope,copy}).Long,"Duplicate groups never multiply bonuses");
        var one=Convert(single);var two=Convert(new(){r,scope,copy});
        Check(two.MassKg>one.MassKg && two.VanillaWarmupFactor>=one.VanillaWarmupFactor,"Inactive/duplicate optics still impose physical costs");
        Check(two.VanillaAccuracyLongFactor<=one.VanillaAccuracyLongFactor+0.0001f,"Duplicate optics do not increase final accuracy");
        scope.Props.internalStats.targetAcquisition=100;
        scope.Props.internalStats.aimingPrecision=100;
        scope.Props.internalStats.identificationDistanceMeters=1000;
        Near(one.VanillaAccuracyLongFactor,Convert(single).VanillaAccuracyLongFactor,
            "Optic authoring values cannot add a second accuracy channel on top of sight-group performance");
        var holo=TestSight(ModularSightKind.Holographic,0,.12f);var mag=TestSight(ModularSightKind.Magnifier,-.1f,.12f);
        var magnified=SightProfile(new(){r,holo,mag});
        Check(magnified.Touch<1 && magnified.Short<1 && magnified.Warmup>1 && magnified.Long>1,"A magnifier group retains close-range and acquisition drawbacks");
        var iron=TestSight(ModularSightKind.IronSet,-.1f,.1f);
        iron.Props.sight.integrated=true;
        var ironNodes=new List<ModularRenderNode>{r,iron};
        object ironResolution=SightResolution(ironNodes);
        var ironGroups=(List<ModularSightGroupStatus>)ironResolution.GetType().GetField("groups").GetValue(ironResolution);
        Check(ironGroups.Single().hasCompleteIronSight && ironGroups.Single().ironOrderValid,
            "A slide's integral front/rear set is reported as a complete iron sight");
        // Group membership must not erase mechanical accuracy of integrated irons.
        float before=Convert(ironNodes).VanillaAccuracyLongFactor;
        iron.Props.internalStats.aimingPrecision=10;
        Check(Convert(ironNodes).VanillaAccuracyLongFactor>before,"An integrated sight preserves its mechanical part's precision");
        Check(ModularWeaponSightPerformance.For(ModularSightKind.LowPowerVariableOptic,false,1).Short>=1,
            "Future LPVOs have an explicit separate profile rather than bypassing normal optic limits");
    }
    private static void PresetTests()
    {
        foreach(var pair in definitions.Where(p=>p.Key.StartsWith("HD_Gun_")).ToArray())
        {
            XElement def=Resolve(pair.Value);XElement comp=def.Element("comps")?.Elements().FirstOrDefault(e=>(string)e.Attribute("Class")==CompClass);
            if((string)comp?.Element("isAssemblyRoot")!="true")continue;
            XElement preset=definitions[comp.Element("spawnPreset").Value];
            var nodes=BuildAssembly(pair.Key,preset);var stats=Convert(nodes);
            if(!pair.Key.Contains("M4A1") && !pair.Key.Contains("M16A4"))
                Check(SightProfile(nodes).Short>=0.99f,"Stock integral iron sights must resolve a complete sight line: "+pair.Key);
            var verbs=def.Element("verbs").Elements().Where(e=>(string)e.Element("verbClass")=="Verb_Shoot").ToArray();
            float warmup=Number(verbs[0].Element("warmupTime"))*stats.VanillaWarmupFactor;
            bool pistol=pair.Key.Contains("P320")||pair.Key.Contains("M1911");
            float cooldown=Number(def.Element("statBases").Element("RangedWeapon_Cooldown"))*stats.VanillaCycleFactor*stats.VanillaCooldownFactor;
            float min=pistol?.80f:1.35f;
            float max=pistol?1.30f:2.25f;
            Check(cooldown>=min && cooldown<=max,"Stock cooldown outside vanilla-comparable band for "+pair.Key+": "+cooldown);
            float[] factors={stats.VanillaAccuracyTouchFactor,stats.VanillaAccuracyShortFactor,stats.VanillaAccuracyMediumFactor,stats.VanillaAccuracyLongFactor};
            string[] keys={"AccuracyTouch","AccuracyShort","AccuracyMedium","AccuracyLong"};
            float[] accuracy=keys.Select((key,i)=>Number(def.Element("statBases").Element(key))*factors[i]).ToArray();
            Check(accuracy.All(a=>a>0 && a<=.92f),"Stock gun accuracy must retain meaningful miss chances: "+pair.Key);
            Console.WriteLine(pair.Key+" mass="+stats.MassKg.ToString("F2")+" warmup="+warmup.ToString("F2")+" cooldown="+cooldown.ToString("F2")+" accuracy="+string.Join("/",accuracy.Select(a=>a.ToString("F3"))));
        }
    }
    private static void VariantTests()
    {
        int count=0;
        foreach(var preset in definitions.Values.Where(e=>e.Name.LocalName=="Helodrace.ModernWar.ModularWeaponPresetDef"))
        {
            string weapon=preset.Element("weaponDef").Value;
            var stats=Convert(BuildAssembly(weapon,preset));
            Check(stats.VanillaWarmupFactor>=0.65f && stats.VanillaWarmupFactor<=2.5f,"Preset warmup remains bounded: "+preset.Element("defName"));
            var def=Resolve(definitions[weapon]);
            float[] factors={stats.VanillaAccuracyTouchFactor,stats.VanillaAccuracyShortFactor,stats.VanillaAccuracyMediumFactor,stats.VanillaAccuracyLongFactor};
            string[] keys={"AccuracyTouch","AccuracyShort","AccuracyMedium","AccuracyLong"};
            Check(keys.Select((key,i)=>Number(def.Element("statBases").Element(key))*factors[i]).All(a=>a<=.95f),
                "Authored presets cannot reach near-certain weapon accuracy: "+preset.Element("defName"));
            count++;
        }
        var rootDef=Resolve(definitions["HD_Gun_M4A1_Weapon"]);
        var rootComp=rootDef.Element("comps").Elements().Single(e=>(string)e.Attribute("Class")==CompClass);
        var original=definitions[rootComp.Element("spawnPreset").Value];
        var redDot=new XElement(original);
        var upper=redDot.Element("parts").Elements().Single(e=>(string)e.Element("socketId")=="upper_receiver");
        upper.Element("children").Elements().Where(e=>(string)e.Element("part")=="HD_ModularPart_OpticMount_LumiconTA51_Part").Remove();
        upper.Element("children").Add(new XElement("li",new XElement("part","HD_ModularPart_Optic_VOTechEXPS"),
            new XElement("socketId","rail_receiver_top"),new XElement("mountId","rail_mount")));
        var scopeStats=Convert(BuildAssembly("HD_Gun_M4A1_Weapon",original));
        var dotStats=Convert(BuildAssembly("HD_Gun_M4A1_Weapon",redDot));
        Check(dotStats.VanillaWarmupFactor<scopeStats.VanillaWarmupFactor,"A lighter close-range optic build can still improve acquisition");
        Check(dotStats.VanillaAccuracyLongFactor<scopeStats.VanillaAccuracyLongFactor,"Faster acquisition retains a long-range tradeoff");
        Check(dotStats.VanillaCooldownFactor<scopeStats.VanillaCooldownFactor,"Lighter handling improves cooldown without changing mechanical RPM");
        Near(dotStats.VanillaCycleFactor,scopeStats.VanillaCycleFactor,"An optic swap cannot change mechanical cycling");
        Check(count>=10,"Stock presets and authored variants were exercised");
        Console.WriteLine("PASS: "+count+" authored presets and a real scope-to-holographic tuning tradeoff.");
    }

    private static ThingDef Projectile(string name)
    {
        XElement xml=Resolve(definitions[name]);
        var def=Bare<ThingDef>();def.defName=name;
        def.projectile=(ProjectileProperties)Parse(typeof(ProjectileProperties),xml.Element("projectile"));
        def.projectile.damageDef=Bare<DamageDef>();
        def.projectile.damageDef.armorCategory=Bare<DamageArmorCategoryDef>();
        def.modExtensions=new List<DefModExtension>();
        foreach(XElement extension in xml.Element("modExtensions")?.Elements() ?? Enumerable.Empty<XElement>())
            def.modExtensions.Add((DefModExtension)Parse(typeof(BulletBallisticsExtension).Assembly.GetType((string)extension.Attribute("Class")),extension));
        return def;
    }
    private static ModularWeaponBallisticProfile Ballistics(ThingDef projectile,ModularWeaponConvertedStats stats)
        => (ModularWeaponBallisticProfile)typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponBallistics")
            .GetMethod("Resolve",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{projectile,stats});
    private static void BallisticTests()
    {
        int count=0;
        foreach(var preset in definitions.Values.Where(e=>e.Name.LocalName=="Helodrace.ModernWar.ModularWeaponPresetDef"))
        {
            var nodes=BuildAssembly(preset.Element("weaponDef").Value,preset);
            ThingDef selected=null;int priority=int.MinValue;
            foreach(var node in nodes)
                if(node.Props.projectileOverride!=null && node.Props.overridePriority>=priority)
                { selected=node.Props.projectileOverride;priority=node.Props.overridePriority; }
            Check(selected!=null,"Every authored preset has an ammunition choice");
            var stats=Convert(nodes);var value=Ballistics(Projectile(selected.defName),stats);
            Check(value!=null && value.BarrelLengthInches>0 && value.MuzzleEnergyJoules>0,"Preset has a physical ballistic profile");
            Check(value.DamageAt()>0 && value.PenetrationAt()>0,"Preset has converted damage/AP");
            Check(value.DamageAt(25)<=value.DamageAt() && value.PenetrationAt(25)<value.PenetrationAt(),"Distance reduces damage and penetration");
            Check(value.FragmentationChanceAt(25)<=value.FragmentationChanceAt(),"Fragmentation shares retained velocity");
            Check(value.FragmentationChanceAt(10,.8f)<=value.FragmentationChanceAt(10),"Traversed armor reduces fragmentation");
            var snapshot=value.Snapshot(value.ReferenceDamage,value.ReferencePenetration);
            Near(value.DamageAt(12),snapshot.DamageAt(12),"UI/combat snapshot damage agree");
            Near(value.PenetrationAt(12),snapshot.PenetrationAt(12),"UI/combat snapshot penetration agree");
            var qualityShot=value.Snapshot((int)Math.Round(value.ReferenceDamage*1.25f),value.ReferencePenetration*1.1f);
            Near(value.DamageAt(12,1.25f),qualityShot.DamageAt(12),"Quality damage multiplier is applied exactly once");
            Near(value.PenetrationAt(12,1.1f),qualityShot.PenetrationAt(12),"Quality penetration multiplier is applied exactly once");
            Console.WriteLine((string)preset.Element("defName")+" barrel="+value.BarrelLengthInches.ToString("F1")
                +" velocity="+value.MuzzleVelocityMetersPerSecond.ToString("F0")+" damage="+value.DamageAt()
                +" AP="+value.PenetrationAt().ToString("P1")+" fragment="+value.FragmentationChanceAt().ToString("P1"));
            count++;
        }
        var props=new CompProperties_ModularWeaponNode {isAssemblyRoot=true,internalStats=new ModularWeaponInternalStats {massKg=1,barrelLengthInches=10.3f}};
        var receiver=Node(props);var nodes2=new List<ModularRenderNode>{receiver};var ammo=Projectile("HD_Bullet_M16A4_Proj");
        var shortBarrel=Ballistics(ammo,Convert(nodes2));props.internalStats.barrelLengthInches=20;
        var longBarrel=Ballistics(ammo,Convert(nodes2));
        foreach(float distance in new[]{0f,10f,25f,50f})
        {
            Near(longBarrel.DamageAt(distance),shortBarrel.DamageAt(distance),"Barrel length cannot change direct damage");
            Near(longBarrel.PenetrationAt(distance),shortBarrel.PenetrationAt(distance),"Barrel length cannot change direct penetration");
        }
        Check(longBarrel.FragmentationChanceAt(10)>shortBarrel.FragmentationChanceAt(10),"Only fragmentation retains barrel-derived lethality");
        var customBarrel=Node(new CompProperties_ModularWeaponNode {internalStats=new ModularWeaponInternalStats
            {massKg=.3f,barrelLengthInches=20,damageTransfer=100,penetrationEfficiency=100}});
        var terminalStats=Convert(new(){receiver,customBarrel});
        Near(1f,terminalStats.DamageTransferFactor,"Barrel authoring cannot add direct damage");
        Near(1f,terminalStats.PenetrationEfficiencyFactor,"Barrel authoring cannot add direct penetration");
        props.internalStats.damageTransfer=25;props.internalStats.penetrationEfficiency=20;
        var tuned=Ballistics(ammo,Convert(nodes2));
        Check(tuned.DamageAt()>longBarrel.DamageAt() && tuned.PenetrationAt()>longBarrel.PenetrationAt(),"Authored terminal-efficiency inputs reach converted combat stats");
        var optic=TestSight(ModularSightKind.Scope,0,.1f);optic.Props.internalStats.damageTransfer=100;optic.Props.internalStats.penetrationEfficiency=100;
        optic.Props.internalStats.muzzleVelocityOffsetMetersPerSecond=300;optic.Props.internalStats.barrelLengthInches=30;
        nodes2.Add(optic);var withOptic=Ballistics(ammo,Convert(nodes2));
        Near(tuned.DamageAt(),withOptic.DamageAt(),"Optics cannot increase ballistic damage");
        Near(tuned.PenetrationAt(),withOptic.PenetrationAt(),"Optics cannot increase ballistic penetration");
        Check(Ballistics(Projectile("HD_Bullet_556M995_Proj"),Convert(nodes2)).FragmentationChanceAt()==0,"AP ammo never acquires fragmentation through the new system");
        Check(Ballistics(Projectile("HD_40mmM381HE_Proj"),Convert(nodes2))==null,"Underbarrel grenades are not scaled as rifle bullets");
        Console.WriteLine("PASS: "+count+" preset ballistics and damage/AP/fragmentation integration checks.");
    }
    private static void BallisticAuthoringTests()
    {
        var inputs=new ModularWeaponInternalStats {massKg=.3f,barrelLengthInches=10.3f,
            muzzleVelocityOffsetMetersPerSecond=-15,damageTransfer=12,penetrationEfficiency=-5};
        var editor=typeof(Dialog_ModularAttachmentEditor);
        var xml=new System.Text.StringBuilder();
        editor.GetMethod("AppendInternalStats",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{xml,inputs});
        var exported=(ModularWeaponInternalStats)Parse(typeof(ModularWeaponInternalStats),XElement.Parse(xml.ToString()));
        var state=editor.GetNestedType("NodePropertiesState",BindingFlags.NonPublic);
        var cloned=(ModularWeaponInternalStats)state.GetMethod("CloneInternalStats",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{inputs});
        foreach(var copy in new[]{exported,cloned})
        {
            Near(inputs.barrelLengthInches,copy.barrelLengthInches,"Editor XML/undo preserves barrel length");
            Near(inputs.muzzleVelocityOffsetMetersPerSecond,copy.muzzleVelocityOffsetMetersPerSecond,"Editor XML/undo preserves velocity offset");
            Near(inputs.damageTransfer,copy.damageTransfer,"Editor XML/undo preserves damage transfer");
            Near(inputs.penetrationEfficiency,copy.penetrationEfficiency,"Editor XML/undo preserves penetration efficiency");
        }
        Check(!ReferenceEquals(inputs,cloned),"Undo owns an independent raw-stat copy");
        var integral=new ModularWeaponSightProperties {kind=ModularSightKind.IronSet,integrated=true};
        var sightXml=new System.Text.StringBuilder();
        editor.GetMethod("AppendSight",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{sightXml,integral});
        Check((bool)XElement.Parse(sightXml.ToString()).Element("integrated"),"Editor export preserves integral sight identity");
        var sightCopy=(ModularWeaponSightProperties)state.GetMethod("CloneSight",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{integral});
        Check(sightCopy.integrated,"Undo preserves integral sight identity");
        foreach(var def in definitions.Values.Where(e=>e.Element("modExtensions")?.Elements()
            .Any(ext=>(string)ext.Attribute("Class")==typeof(BulletBallisticsExtension).FullName)==true))
            Check(!Projectile(def.Element("defName").Value).GetModExtension<BulletBallisticsExtension>().ConfigErrors().Any(),"Authored ballistic input is valid");
        Check(new BulletBallisticsExtension {referenceVelocityMetersPerSecond=0}.ConfigErrors().Any(),"Invalid ballistic inputs report XML errors");
    }
}
