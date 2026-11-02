using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.XPath;
using Helodrace;
using Helodrace.ModernWar;
using UnityEngine;
using Verse;

internal static partial class Program
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
        return Run(args);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        try
        {
            root=Path.GetFullPath(args.Length>0 ? args[0] : ".");
            Load();
            if (args.Contains("--snapshot")) { Snapshot(); return 0; }
            if (args.Contains("--stock-snapshot")) { StockSnapshot(); return 0; }
            if (args.Contains("--migration-snapshot")) { MigrationSnapshot(); return 0; }
            PlacementDefinitionTests();
            StatDirectionTests();
            SightTests();
            PresetTests();
            VariantTests();
            BallisticTests();
            BallisticAuthoringTests();
            RangeTests();
            AmmunitionCatalogTests();
            PartStatCoverageTests();
            Rifle762Tests();
            AmmunitionPrecisionTests();
            PartConflictTests.Run(Check);
            AuthoredPartConflictTests();
            PlacementEditorTests.Run(Check);
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
        // Apply balance first, then placement overrides, before XML inheritance.
        var patchFiles=new[]{Path.Combine(root,"Patches/ModularWeapon_Balance.xml")}
            .Concat(Directory.GetFiles(Path.Combine(root,"Patches"),"ModularWeapon_Placement_*.xml").OrderBy(p=>p,StringComparer.Ordinal));
        foreach(string patchFile in patchFiles)
        foreach(XElement op in XDocument.Load(patchFile).Root.Elements())
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
    private static readonly Dictionary<string,float[]> ReferenceAccuracy = new()
    {
        ["HD_Gun_M4A1_Weapon"]=new[]{.475f,.621f,.615f,.506f},
        ["HD_Gun_M14_Weapon"]=new[]{.570f,.765f,.802f,.680f},
        ["HD_Gun_M16A4_Weapon"]=new[]{.482f,.630f,.618f,.509f},
        ["HD_Gun_M16A1_Weapon"]=new[]{.591f,.692f,.611f,.476f},
        ["HD_Gun_M16A2_Weapon"]=new[]{.597f,.699f,.618f,.483f},
        ["HD_Gun_M16A3_Weapon"]=new[]{.597f,.697f,.616f,.480f},
        ["HD_Gun_M1911_Weapon"]=new[]{.750f,.709f,.436f,.243f},
        ["HD_Gun_M249_Weapon"]=new[]{.383f,.505f,.419f,.301f},
        ["HD_Gun_MP5_Weapon"]=new[]{.813f,.705f,.432f,.219f},
        ["HD_Gun_P320_Weapon"]=new[]{.760f,.715f,.429f,.244f}
    };
    private static readonly Dictionary<string,float[]> ReferenceTimes = new()
    {
        ["HD_Gun_M4A1_Weapon"]=new[]{1.07f,1.65f}, ["HD_Gun_M14_Weapon"]=new[]{1.37f,1.61f},
        ["HD_Gun_M16A4_Weapon"]=new[]{1.42f,1.62f}, ["HD_Gun_M16A1_Weapon"]=new[]{1.01f,1.66f},
        ["HD_Gun_M16A2_Weapon"]=new[]{1.18f,1.61f}, ["HD_Gun_M16A3_Weapon"]=new[]{1.06f,1.62f},
        ["HD_Gun_M1911_Weapon"]=new[]{.26f,.98f}, ["HD_Gun_M249_Weapon"]=new[]{1.47f,1.66f},
        ["HD_Gun_MP5_Weapon"]=new[]{.55f,1.50f}, ["HD_Gun_P320_Weapon"]=new[]{.26f,.95f}
    };
    private static void Snapshot()
    {
        foreach(var pair in ReferenceAccuracy)
        {
            var def=Resolve(definitions[pair.Key]); var props=Properties(def);
            var comp=def.Element("comps").Elements().Single(e=>(string)e.Attribute("Class")==CompClass);
            var stats=Convert(BuildAssembly(pair.Key,definitions[comp.Element("spawnPreset").Value]));
            var bands=new[]{stats.Touch,stats.Short,stats.Medium,stats.Long};
            var verb=def.Element("verbs").Elements().First(e=>(string)e.Element("verbClass")=="Verb_Shoot");
            Console.WriteLine(pair.Key+" referenceCosts="+((props.internalStats.warmupReferenceCost>0?props.internalStats.warmupReferenceCost:1)*Number(verb.Element("warmupTime"))*stats.VanillaWarmupFactor/ReferenceTimes[pair.Key][0]).ToString("F6",CultureInfo.InvariantCulture)
                +"/"+((props.internalStats.recoveryReferenceCost>0?props.internalStats.recoveryReferenceCost:1)*Number(def.Element("statBases").Element("RangedWeapon_Cooldown"))*stats.VanillaCooldownFactor/ReferenceTimes[pair.Key][1]).ToString("F6",CultureInfo.InvariantCulture));
            float[] current={props.internalStats.precisionTouchCeiling,props.internalStats.precisionShortCeiling,
                props.internalStats.precisionMediumCeiling,props.internalStats.precisionLongCeiling};
            Console.WriteLine(pair.Key+" suggestedCeilings="+string.Join("/",bands.Select((b,i)=>(current[i]*pair.Value[i]/b.Accuracy).ToString("F6",CultureInfo.InvariantCulture)))
                +" accuracy="+string.Join("/",bands.Select(b=>b.Accuracy.ToString("F3")))+" stability="+stats.AimStability+" stock="+stats.StockSupport);
        }
    }
    private static void MigrationSnapshot()
    {
        var snapshot = ReferenceAccuracy.Keys.ToDictionary(name => name, name => {
            var definition = Resolve(definitions[name]);
            var comp = definition.Element("comps").Elements().Single(e => (string)e.Attribute("Class") == CompClass);
            var stats = Convert(BuildAssembly(name, definitions[comp.Element("spawnPreset").Value]));
            var verb = definition.Element("verbs").Elements().First(e => (string)e.Element("verbClass") == "Verb_Shoot");
            return new {
                Accuracy = new[] { stats.Touch.Accuracy, stats.Short.Accuracy, stats.Medium.Accuracy, stats.Long.Accuracy },
                Warmup = Number(verb.Element("warmupTime")) * stats.VanillaWarmupFactor,
                Cooldown = Number(definition.Element("statBases").Element("RangedWeapon_Cooldown")) * stats.VanillaCooldownFactor
            };
        });
        Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
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
            if(field==null) throw new Exception("Unknown authored field: "+type.Name+"."+item.Name);
            field.SetValue(obj,Parse(field.FieldType,item));
        }
        return obj;
    }
    private static readonly string[] parsedFields={"internalStats","stock","isWeaponSubassembly","sight","sockets","mounts","performanceRole","graphicOffset","graphicAngle","assemblyScale","sightGroupHeightTolerance","laser","verticalOccupancy","verticalOccupancyOffset","projectileOverride","overridePriority","ammunitionType"};
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
    private static void PlacementDefinitionTests()
    {
        var m249=Properties(Resolve(definitions["HD_ModularPart_Handguard_M249E3_Part"]));
        var side=m249.SocketNamed("rail_side");
        Check(side?.isRail==true && side.railSurface==ModularRailSurface.Side,"Latest M249 side rail is a functional side attachment surface");
        Near(side.railLength,.125f,"Latest M249 side rail length");
        var ebr=Properties(Resolve(definitions["HD_ModularPart_Handguard_MK14EBR"]));
        Check(ebr.SocketNamed("rail_bottom")?.railSurface==ModularRailSurface.Bottom
            && ebr.SocketNamed("raid_side")?.railSurface==ModularRailSurface.Side,"Latest EBR bottom/side sockets are preserved");
        var stock=Properties(Resolve(definitions["HD_ModularPart_PistolGrip_MK14EBR"])).SocketNamed("stock");
        Check(stock?.isRail==true && stock.maxAttachments==1,"EBR stock extension becomes a one-stock rail rather than just a visual offset");
        Near(stock.railLength,.05f,"Saved EBR stock extension travel");
        var mk14=definitions["HD_WeaponPreset_MK14_Default"].Descendants("li")
            .Single(e=>(string)e.Element("part")=="HD_ModularPart_Stock_MK14EBR");
        Near(Number(mk14.Element("railOffset")),-.025f,"MK14 preset retains saved extended-stock position");
        var usmc=definitions["HD_WeaponPreset_M249_USMC"].Descendants("li")
            .Single(e=>(string)e.Element("part")=="HD_ModularPart_OpticMount_LumiconTA51_Part");
        Near(Number(usmc.Element("railOffset")),0,"M249 preset retains saved optic rail position");
        string[] protectedFields={"internalStats","statOffsets","statFactors","incompatibleParts","conditionalIncompatibleParts","projectileOverride","requiredResearch","craftCost","statBases"};
        foreach(string path in Directory.GetFiles(Path.Combine(root,"Patches"),"ModularWeapon_Placement_*.xml"))
            foreach(var op in XDocument.Load(path).Root.Elements())
                Check(!protectedFields.Any(field=>op.Element("xpath").Value.Contains("/"+field)
                    || op.Element("value").Descendants(field).Any()),"Placement imports cannot roll back current combat/crafting/conflict settings");
        Console.WriteLine("PASS: saved side/bottom rails, functional EBR stock extension, preset instance offsets and protected combat settings.");
    }
    private static void AmmunitionCatalogTests()
    {
        var catalog = typeof(Dialog_ModularWeapon).GetMethod("CatalogGroup", BindingFlags.Static | BindingFlags.NonPublic);
        var compatible = typeof(Dialog_ModularWeapon).GetMethod("CompatibleMount", BindingFlags.Static | BindingFlags.NonPublic);
        var magazine = Properties(Resolve(definitions["HD_ModularPart_Magazine_M1420_Part"]));
        var socket = magazine.SocketNamed("ammunition");
        int count = 0, natoCount = 0;
        foreach (var pair in definitions)
        {
            if (pair.Value.Name.LocalName != "ThingDef") continue;
            var xml = Resolve(pair.Value);
            var comp = xml.Element("comps")?.Elements().FirstOrDefault(e => (string)e.Attribute("Class") == CompClass);
            if (string.IsNullOrEmpty((string)comp?.Element("ammunitionType"))) continue;
            var props = Properties(xml);
            var def = Bare<ThingDef>(); def.defName = pair.Key; def.label = (string)xml.Element("label");
            def.comps = new List<CompProperties> { props };
            Check((string)catalog.Invoke(null, new object[] { def, socket }) == "Magazine",
                "All ammunition must share the visible magazine catalog group regardless of DefName: " + pair.Key);
            count++;
            if ((string)comp.Element("ammunitionCaliber") != "762x51") continue;
            Check(compatible.Invoke(null, new object[] { props, socket }) != null,
                "Every 7.62x51 selection must fit the M14 ammunition socket: " + pair.Key);
            Check(definitions.ContainsKey((string)comp.Element("projectileOverride")), "7.62x51 projectile exists: " + pair.Key);
            Check(File.Exists(Path.Combine(root, "Textures", (string)xml.Element("graphicData")?.Element("texPath") + ".png")),
                "7.62x51 catalog icon exists: " + pair.Key);
            Check(File.Exists(Path.Combine(root, "Textures", (string)comp.Element("feedingRoundTexPath") + ".png")),
                "7.62x51 feeding texture exists: " + pair.Key);
            natoCount++;
        }
        Check(natoCount == 8, "M80/M80A1/M198/M948/M993/M1158/MK316/MK319 are all selectable");
        Console.WriteLine("PASS: " + count + " ammunition catalog groups and all 8 M14 7.62x51 selections, projectiles and textures.");
    }
    private static void PartStatCoverageTests()
    {
        var rootNode = Node(new CompProperties_ModularWeaponNode {isAssemblyRoot=true,
            internalStats=new ModularWeaponInternalStats {massKg=1.5f,recoilImpulse=4.5f,muzzleRise=3.5f,
                aimSpeed=55,aimStability=55,mechanicalPrecision=60,operatingReliability=.97f,gasFlow=1}});
        var bare = Convert(new List<ModularRenderNode>{rootNode});
        int count=0,muzzles=0;
        foreach(var pair in definitions)
        {
            if(pair.Value.Name.LocalName!="ThingDef") continue;
            var xml=Resolve(pair.Value);
            var comp=xml.Element("comps")?.Elements().FirstOrDefault(e=>(string)e.Attribute("Class")==CompClass);
            if(comp==null || ((string)comp.Element("isAssemblyRoot")!="true" && comp.Element("mounts")?.Elements().Any()!=true)) continue;
            var props=Properties(xml);count++;
            bool effect=typeof(ModularWeaponInternalStats).GetFields().Any(f=>f.Name!="massKg" && Math.Abs((float)f.GetValue(props.internalStats))>.00001f);
            Check(effect || props.sight!=null || !string.IsNullOrEmpty((string)comp.Element("projectileOverride"))
                || pair.Key=="HD_ModularPart_MagazineWrap_StanagTapeBlue", "Unexplained mass-only part: "+pair.Key);
            if(!pair.Key.Contains("_Muzzle_") && !pair.Key.Contains("_Suppressor_"))continue;
            var part=Node(props);part.transform=new ModularTransform2D {position=new System.Numerics.Vector2(.4f,0),scale=System.Numerics.Vector2.One};
            var fitted=Convert(new List<ModularRenderNode>{rootNode,part});
            Check(fitted.RecoilControl>bare.RecoilControl && fitted.MuzzleControl>bare.MuzzleControl,"Muzzle attachment improves both control scores: "+pair.Key);
            Check(fitted.VanillaCooldownFactor<bare.VanillaCooldownFactor-.001f,"Muzzle attachment improves recovery despite added weight: "+pair.Key);
            Check(fitted.CERecoil<bare.CERecoil && fitted.MassKg>bare.MassKg,"CE recoil benefit and physical weight remain: "+pair.Key);
            muzzles++;
        }
        Check(muzzles==11,"Every muzzle/suppressor definition was tested");
        foreach(var preset in definitions.Values.Where(e=>e.Name.LocalName=="Helodrace.ModernWar.ModularWeaponPresetDef"))
        {
            var nodes=BuildAssembly(preset.Element("weaponDef").Value,preset);
            var fitted=Convert(nodes);
            foreach(var device in nodes.Where(n=>n.parentSocketId=="muzzle" || n.parentSocketId=="suppressor").ToArray())
            {
                var excluded=new HashSet<CompModularWeaponNode>{device.comp};
                bool changed;
                do{changed=false;foreach(var n in nodes)if(n.parentComp!=null && excluded.Contains(n.parentComp))changed|=excluded.Add(n.comp);}while(changed);
                var unfitted=Convert(nodes.Where(n=>!excluded.Contains(n.comp)).ToList());
                Check(fitted.VanillaCooldownFactor<unfitted.VanillaCooldownFactor,
                    "Actual preset muzzle attachment improves recovery: "+preset.Element("defName")+" / "+device.parentSocketId);
            }
        }
        Console.WriteLine("PASS: "+count+" usable parts/roots have stats; all "+muzzles+" muzzle parts outperform a bare muzzle in control/recovery.");
    }
    private static void Rifle762Tests()
    {
        var selectors=new Dictionary<string,(int damage,float ap,int count)>{
            {"HD_ModularPart_Ammunition_762M80_Part",(14,.24f,1)},
            {"HD_Ammunition_76251M80A1_Ammo",(18,.34f,1)},
            {"HD_Ammunition_76251M198_Ammo",(9,.14f,2)},
            {"HD_Ammunition_76251M948_Ammo",(10,.58f,1)},
            {"HD_Ammunition_76251M993_Ammo",(13,.48f,1)},
            {"HD_Ammunition_76251M1158_Ammo",(15,.56f,1)},
            {"HD_Ammunition_76251MK316_Ammo",(17,.26f,1)},
            {"HD_Ammunition_76251MK319_Ammo",(19,.29f,1)}};
        var projectiles=new HashSet<string>();var profiles=new Dictionary<string,ModularWeaponBallisticProfile>();
        var builds=new Dictionary<string,ModularWeaponConvertedStats>();
        foreach(var pair in selectors)
        {
            var nodes=BuildAssembly("HD_Gun_M14_Weapon",definitions["HD_WeaponPreset_ModularM14_Default"]);
            var ammo=nodes.Single(n=>!string.IsNullOrEmpty(n.Props.ammunitionType));
            ammo.comp.props=Properties(Resolve(definitions[pair.Key]));
            string projectile=ammo.Props.projectileOverride.defName;
            Check(projectiles.Add(projectile),"7.62x51 cartridges have distinct projectiles: "+pair.Key);
            var converted=Convert(nodes);builds[pair.Key]=converted;
            var profile=Ballistics(Projectile(projectile),converted);profiles[pair.Key]=profile;
            Check(profile.DamageAt()==pair.Value.damage && profile.ProjectilesPerShot==pair.Value.count,"Selected ammo damage/count reaches the real ballistic profile: "+pair.Key);
            Near(profile.PenetrationAt(),pair.Value.ap,"Selected ammo penetration reaches the real ballistic profile");
            var barrel=nodes.Single(n=>n.Props.internalStats.barrelLengthInches>0);barrel.Props.internalStats.barrelLengthInches=16;
            var shortProfile=Ballistics(Projectile(projectile),Convert(nodes));
            Near(shortProfile.DamageAt(20),profile.DamageAt(20),"7.62 direct damage is independent of barrel length");
            Near(shortProfile.PenetrationAt(20),profile.PenetrationAt(20),"7.62 direct penetration is independent of barrel length");
            if(pair.Key.Contains("M948")||pair.Key.Contains("M993")||pair.Key.Contains("M1158")||pair.Key.Contains("M198"))
                Check(profile.FragmentationChanceAt()==0,"AP/SLAP/duplex do not inherit EPR fragmentation");
        }
        var epr=builds["HD_Ammunition_76251M80A1_Ammo"];var match=builds["HD_Ammunition_76251MK316_Ammo"];
        Check(match.RangeFactor>epr.RangeFactor && match.VanillaAccuracyLongFactor>epr.VanillaAccuracyLongFactor,"MATCH provides a real precision/range benefit");
        Check(builds["HD_Ammunition_76251MK319_Ammo"].RecoilControl>epr.RecoilControl,"OTM has a real recoil tradeoff");
        Check(profiles["HD_Ammunition_76251MK316_Ammo"].FragmentationChanceAt()>0 && profiles["HD_Ammunition_76251MK319_Ammo"].FragmentationChanceAt()>0,"MATCH/OTM use the fragmentation system");
        var endpoint=DuplexProjectileRules.SecondaryEndpoint(System.Numerics.Vector3.Zero,new System.Numerics.Vector3(20,0,0),1.2f,1);
        var far=DuplexProjectileRules.SecondaryEndpoint(System.Numerics.Vector3.Zero,new System.Numerics.Vector3(40,0,0),1.2f,1);
        Near(endpoint.X,20,"Duplex spread stays around the primary resolved trajectory");Near(far.Z,endpoint.Z*2,"Duplex dispersion grows with distance");
        Check(new DuplexProjectileExtension {secondarySpreadDegrees=float.NaN}.ConfigErrors().Any(),"Invalid duplex XML spread is rejected");
        Console.WriteLine("PASS: all 8 7.62x51 profiles, distinct projectiles, duplex count/spread and barrel-independent terminal damage.");
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
        var transform=ModularTransform2D.Identity; transform.scale=System.Numerics.Vector2.One*node.Props.assemblyScale;node.transform=transform;
        node.thing.def.statBases=def.Element("statBases")?.Elements().Select(e=>new RimWorld.StatModifier {
            stat=new RimWorld.StatDef {defName=e.Name.LocalName},value=Number(e)}).ToList();
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
                child.transform=parent.transform.Attach(effective.ToNativePose(),mount.EffectiveTransform(socket).ToNativePose(),child.Props.graphicAngle,ratio);
                child.railOffset=offset;child.parentComp=parent.comp;child.parentSocketId=socketId;child.mountId=mount.id;child.attachedToRail=socket.isRail;
                float half=mount.EffectivePhysicalLength*ratio*.5f;
                var left=socket.transform.position;var right=left;
                left.x+=offset+mount.physicalCenterOffset*ratio-half;
                right.x+=offset+mount.physicalCenterOffset*ratio+half;
                child.occupiedRailStart=parent.transform.TransformPoint(left.ToNative());
                child.occupiedRailEnd=parent.transform.TransformPoint(right.ToNative());
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
        var n=Node(new CompProperties_ModularWeaponNode { internalStats=new ModularWeaponInternalStats { massKg=.15f,aimSpeed=-2 },
            sight=new ModularWeaponSightProperties { kind=kind,axisOffset=new Vector3(x,0,z),selectionPriority=25 } });
        return n;
    }
    private static void SightTests()
    {
        var r=Node(new CompProperties_ModularWeaponNode { isAssemblyRoot=true,internalStats=new ModularWeaponInternalStats {massKg=1,aimSpeed=55,mechanicalPrecision=60,aimStability=55} });
        var scope=TestSight(ModularSightKind.Scope,-.1f,.18f);var dot=TestSight(ModularSightKind.Reflex,-.1f,.1f);
        var single=new List<ModularRenderNode>{r,scope}; var s=SightProfile(single);
        var ironProfile=ModularWeaponSightPerformance.For(ModularSightKind.IronSet,false,1);
        Check(s.Touch<ironProfile.Touch && s.Short<ironProfile.Short && s.Warmup>1 && s.Long>ironProfile.Long,"A lone fixed scope pays close-range and warmup costs");
        var d=SightProfile(new(){r,dot});Check(d.Long<ironProfile.Long && d.Short>ironProfile.Short,"A lone reflex has a long-range disadvantage");
        var both=SightProfile(new(){r,scope,dot});
        Near(Math.Max(s.Touch,d.Touch),both.Touch,"Groups take maximum touch factor");
        Near(Math.Max(s.Short,d.Short),both.Short,"Groups take maximum short factor");
        Near(Math.Max(s.Long,d.Long),both.Long,"Groups take maximum long factor");
        Near(Math.Max(s.Range,d.Range),both.Range,"Groups take maximum range factor");
        var active=((List<ModularSightGroupStatus>)SightResolution(new(){r,scope,dot}).GetType().GetField("groups")
            .GetValue(SightResolution(new(){r,scope,dot}))).Single(g=>g.isActive);
        Near(active.performance.Warmup,both.Warmup,"Representative acquisition comes from one active group, not independent minimum costs");
        var copy=TestSight(ModularSightKind.Scope,-.1f,.24f);
        Near(s.Long,SightProfile(new(){r,scope,copy}).Long,"Duplicate groups never multiply bonuses");
        Near(s.Range,SightProfile(new(){r,scope,copy}).Range,"Duplicate sight groups never multiply range bonuses");
        var one=Convert(single);var two=Convert(new(){r,scope,copy});
        Check(two.MassKg>one.MassKg && two.VanillaWarmupFactor>=one.VanillaWarmupFactor,"Inactive/duplicate optics still impose physical costs");
        Check(two.VanillaAccuracyLongFactor<=one.VanillaAccuracyLongFactor+0.0001f,"Duplicate optics do not increase final accuracy");
        scope.Props.internalStats.aimStability=100;
        scope.Props.internalStats.mechanicalPrecision=100;
        scope.Props.internalStats.identificationDistanceMeters=1000;
        Near(one.VanillaAccuracyLongFactor,Convert(single).VanillaAccuracyLongFactor,
            "Optic authoring values cannot add a second accuracy channel on top of sight-group performance");
        var holo=TestSight(ModularSightKind.Holographic,0,.12f);var mag=TestSight(ModularSightKind.Magnifier,-.1f,.12f);
        var magnified=SightProfile(new(){r,holo,mag});
        Check(magnified.Touch<ironProfile.Touch && magnified.Short<ironProfile.Short && magnified.Warmup>1 && magnified.Long>ironProfile.Long,"A magnifier group retains close-range and acquisition drawbacks");
        var iron=TestSight(ModularSightKind.IronSet,-.1f,.1f);
        iron.Props.sight.integrated=true;
        var ironNodes=new List<ModularRenderNode>{r,iron};
        object ironResolution=SightResolution(ironNodes);
        var ironGroups=(List<ModularSightGroupStatus>)ironResolution.GetType().GetField("groups").GetValue(ironResolution);
        Check(ironGroups.Single().hasCompleteIronSight && ironGroups.Single().ironOrderValid,
            "A slide's integral front/rear set is reported as a complete iron sight");
        // Group membership must not erase mechanical accuracy of integrated irons.
        float before=Convert(ironNodes).VanillaAccuracyLongFactor;
        iron.Props.internalStats.mechanicalPrecision=10;
        Check(Convert(ironNodes).VanillaAccuracyLongFactor>before,"An integrated sight preserves its mechanical part's precision");
        Check(ModularWeaponSightPerformance.For(ModularSightKind.LowPowerVariableOptic,false,1).Short<ironProfile.Short,
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
                Check(SightProfile(nodes).Short>=0.96f,"Stock integral iron sights must resolve a complete sight line: "+pair.Key);
            var verbs=def.Element("verbs").Elements().Where(e=>(string)e.Element("verbClass")=="Verb_Shoot").ToArray();
            float warmup=Number(verbs[0].Element("warmupTime"))*stats.VanillaWarmupFactor;
            bool pistol=pair.Key.Contains("P320")||pair.Key.Contains("M1911");
            float cooldown=Number(def.Element("statBases").Element("RangedWeapon_Cooldown"))*stats.VanillaCooldownFactor;
            float min=pistol?.80f:1.35f;
            float max=pistol?1.30f:2.25f;
            Check(cooldown>=min && cooldown<=max,"Stock cooldown outside vanilla-comparable band for "+pair.Key+": "+cooldown);
            float[] factors={stats.VanillaAccuracyTouchFactor,stats.VanillaAccuracyShortFactor,stats.VanillaAccuracyMediumFactor,stats.VanillaAccuracyLongFactor};
            string[] keys={"AccuracyTouch","AccuracyShort","AccuracyMedium","AccuracyLong"};
            float[] accuracy=keys.Select((key,i)=>Number(def.Element("statBases").Element(key))*factors[i]).ToArray();
            Check(accuracy.All(a=>a>0 && a<=.92f),"Stock gun accuracy must retain meaningful miss chances: "+pair.Key);
            float range=Number(verbs[0].Element("range"))*stats.RangeFactor;
            Check(range>=15 && range<=50,"Default build range stays within weapon-scale bounds");
            Console.WriteLine(pair.Key+" mass="+stats.MassKg.ToString("F2")+" warmup="+warmup.ToString("F2")+" cooldown="+cooldown.ToString("F2")+" range="+range.ToString("F1")+" accuracy="+string.Join("/",accuracy.Select(a=>a.ToString("F3"))));
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
            Check(stats.RangeFactor>=.5f && stats.RangeFactor<=1.5f,"Preset range remains bounded");
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
        Check(dotStats.RangeFactor<scopeStats.RangeFactor,"Short-range optic retains a range tradeoff");
        Check(Math.Abs(dotStats.VanillaCooldownFactor-scopeStats.VanillaCooldownFactor)<.02f,"Optic mass can affect recovery but acquisition speed must not become a direct cooldown bonus");
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
            muzzleVelocityOffsetMetersPerSecond=-15,damageTransfer=12,penetrationEfficiency=-5,rangeAdjustmentPercent=-12};
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
            Near(inputs.rangeAdjustmentPercent,copy.rangeAdjustmentPercent,"Editor XML/undo preserves effective range adjustment");
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
    private static void RangeTests()
    {
        var receiver=Node(new CompProperties_ModularWeaponNode {isAssemblyRoot=true,internalStats=new ModularWeaponInternalStats {massKg=1}});
        var barrel=Node(Properties(Resolve(definitions["HD_ModularPart_Barrel_AR15103_Part"])));
        var sight=TestSight(ModularSightKind.IronSet,0,.1f);
        var nodes=new List<ModularRenderNode>{receiver,barrel,sight};
        var shortBuild=Convert(nodes);
        barrel.Props.internalStats=Properties(Resolve(definitions["HD_ModularPart_Barrel_AR1520_Part"])).internalStats;
        var longBuild=Convert(nodes);
        Check(longBuild.RangeFactor>shortBuild.RangeFactor,"Authored long barrel provides more range than the short barrel");
        var ammo=Projectile("HD_Bullet_M16A4_Proj");
        Near(Ballistics(ammo,longBuild).DamageAt(),Ballistics(ammo,shortBuild).DamageAt(),"Longer range does not increase primary damage");
        Near(Ballistics(ammo,longBuild).PenetrationAt(),Ballistics(ammo,shortBuild).PenetrationAt(),"Longer range does not increase primary penetration");
        var optic=TestSight(ModularSightKind.Scope,-.1f,.2f);optic.Props.internalStats.rangeAdjustmentPercent=100;
        nodes.Add(optic);var withOptic=Convert(nodes);
        Near(longBuild.MechanicalRangeFactor,withOptic.MechanicalRangeFactor,"Optic raw values cannot bypass group range limits");
        Check(withOptic.RangeFactor>longBuild.RangeFactor,"A valid scope can improve effective range");
        var duplicate=TestSight(ModularSightKind.Scope,-.1f,.3f);nodes.Add(duplicate);
        Near(withOptic.RangeFactor,Convert(nodes).RangeFactor,"Installing another scope cannot extend range again");
        nodes.Remove(sight);nodes.Remove(optic);nodes.Remove(duplicate);
        Check(Convert(nodes).RangeFactor<longBuild.RangeFactor,"Removing all usable sights reduces effective range");
        barrel.Props.internalStats.rangeAdjustmentPercent=10000;
        Check(Convert(nodes).RangeFactor<=1.5f,"Extreme positive range authoring is capped");
        barrel.Props.internalStats.rangeAdjustmentPercent=-10000;
        Check(Convert(nodes).RangeFactor>=.5f,"Extreme negative range authoring is capped");
        var full=Convert(BuildAssembly("HD_Gun_P320_Weapon",definitions["HD_WeaponPreset_ModularP320_Default"]));
        var raider=Convert(BuildAssembly("HD_Gun_P320_Weapon",definitions["HD_WeaponPreset_P320_RaiderKit"]));
        Check(raider.RangeFactor>full.RangeFactor,"Flux Raider support kit extends the pistol's effective range");
        Near(full.DamageTransferFactor,raider.DamageTransferFactor,"Flux range bonus cannot increase direct damage");
        Console.WriteLine("PASS: authored barrel/Flux range, sight-group maximums, input caps and terminal-damage independence.");
    }
}
