using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Helodrace.ModernWar;
using UnityEngine;
using Verse;

internal static class PlacementEditorTests
{
    private sealed class NamedThing : ThingWithComps { public override string LabelNoCount => def.label; }
    private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static readonly BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    private static readonly Type Editor=typeof(Dialog_ModularAttachmentEditor);
    private static ModularRenderNode Node(string name,int id,CompProperties_ModularWeaponNode props)
    {
        var thing=Bare<NamedThing>();thing.def=Bare<ThingDef>();thing.def.defName=name;thing.def.label=name;thing.stackCount=1;thing.thingIDNumber=id;
        var comp=new CompModularWeaponNode {parent=thing,props=props};
        return new ModularRenderNode {thing=thing,comp=comp,path="root",transform=ModularTransform2D.Identity};
    }
    private static void Seed(ModularRenderNode root,List<ModularRenderNode> nodes)
    {
        typeof(CompModularWeaponNode).GetField("renderCache",Instance).SetValue(root.comp,nodes);
        typeof(CompModularWeaponNode).GetField("renderCacheDirty",Instance).SetValue(root.comp,false);
    }
    private static object Field(object owner,string name) => Editor.GetField(name,Instance).GetValue(owner);
    private static void Set(object owner,string name,object value) => Editor.GetField(name,Instance).SetValue(owner,value);
    internal static void Run(Action<bool,string> check)
    {
        var a=Node("Rifle",101,new CompProperties_ModularWeaponNode {isAssemblyRoot=true});
        var sharedProps=new CompProperties_ModularWeaponNode();
        var child=Node("Shared",102,sharedProps);child.path="root/rail[102]";child.parentComp=a.comp;
        child.parentSocketId="rail";child.mountId="mount";child.attachedToRail=true;child.railOffset=.12f;
        var b=Node("Pistol",103,new CompProperties_ModularWeaponNode {isAssemblyRoot=true});
        var shared=Node("Shared",104,sharedProps);shared.path="root/rail[104]";shared.parentComp=b.comp;
        shared.parentSocketId="rail";shared.mountId="mount";shared.attachedToRail=true;shared.railOffset=-.08f;
        var nodesA=new List<ModularRenderNode>{a,child};var nodesB=new List<ModularRenderNode>{b,shared};
        Seed(a,nodesA);Seed(b,nodesB);
        var invalidated=new List<CompModularWeaponNode>();
        var workspace=new ModularPlacementWorkspace(root=>invalidated.Add(root));workspace.Visit(a.comp);workspace.Visit(b.comp);workspace.Visit(a.comp);
        check(invalidated.Count==2,"Joining a workspace invalidates each weapon's potentially stale shared-definition caches");
        check(workspace.Batch.WeaponCount==2 && workspace.Batch.PartCount==3,"Real workspace deduplicates revisits and shared definitions");
        var xml=XDocument.Parse(workspace.Batch.Capture().Xml);
        check(xml.Root.Element("assemblies").Elements().Count()==2,"Real workspace captures both assembly contexts");
        var offsets=new List<string>();foreach(var e in xml.Descendants("railOffset"))offsets.Add(e.Value);
        check(offsets.Contains("0.12") && offsets.Contains("-0.08"),"Per-weapon rail positions survive the unified export");
        sharedProps.graphicOffset=new Vector3(.2f,0,-.1f);
        xml=XDocument.Parse(workspace.Batch.Capture().Xml);
        check(xml.Root.Element("parts").Elements().Single(e=>(string)e.Element("thingDef")=="Shared")
            .Element("settings").Element("graphicOffset").Value=="(0.2,0,-0.1)","Real exporter reads the latest shared settings without changing weapons again");
        workspace.Refresh();
        check(invalidated.Count==4,"Editing a shared definition invalidates every visited weapon");
        var component1=new GameComponent_ModularPlacementWorkspace(null);var component2=new GameComponent_ModularPlacementWorkspace(null);
        check(!ReferenceEquals(component1.Workspace,component2.Workspace),"Separate game components never share editing queues");

        var moving=Node("MoveA",201,new CompProperties_ModularWeaponNode());
        var rotated=Node("MoveB",202,new CompProperties_ModularWeaponNode());
        moving.transform=new ModularTransform2D {scale=new System.Numerics.Vector2(.5f,.5f)};
        rotated.transform=new ModularTransform2D {angle=90,scale=new System.Numerics.Vector2(2f,-.25f)};
        Vector2 delta=new Vector2(.12f,-.07f);
        var move=Editor.GetMethod("MoveGraphics",Static);
        move.Invoke(null,new object[]{new List<ModularRenderNode>{moving,rotated},delta});
        foreach(var node in new[]{moving,rotated})
        {
            var actual=node.transform.TransformPoint(node.Props.graphicOffset.ToNative())-node.transform.position;
            check((actual-delta.ToNative()).LengthSquared()<.000001f,"Group graphics move by identical screen-space distances across rotation, palette scale and mirroring");
        }
        var duplicate=Node("MoveA",203,moving.Props);Vector3 before=moving.Props.graphicOffset;
        move.Invoke(null,new object[]{new List<ModularRenderNode>{moving,duplicate},delta});
        check((moving.Props.graphicOffset-before*2).sqrMagnitude<.000001f,"Repeated instances of one shared definition are moved only once");

        var settings=new CompProperties_ModularWeaponNode();
        settings.internalStats=new ModularWeaponInternalStats {massKg=.5f,aimSpeed=7,aimStability=8,mechanicalPrecision=9,ammunitionPrecisionFactor=1.12f,
            precisionTouchCeiling=.6f,precisionShortCeiling=.7f,precisionMediumCeiling=.8f,precisionLongCeiling=.9f,
            warmupReferenceCost=1.1f,recoveryReferenceCost=1.2f,receiverLengthMeters=.18f,naturalSupport=.15f};
        settings.stock=new ModularWeaponStockProperties {adjustable=true,retractedOffset=.03f,extendedOffset=-.03f};
        var excluded=Bare<ThingDef>();excluded.defName="Excluded&Part";settings.incompatibleParts.Add(excluded);
        var condition=Bare<ThingDef>();condition.defName="Barrel&Condition";
        settings.conditionalIncompatibleParts.Add(new ModularWeaponConditionalPartConflict {
            part=excluded,whenAnyParts=new List<ThingDef>{condition}});
        settings.mounts.Add(new ModularAttachmentMount {id="mount&",label="<left & right>"});
        settings.sockets.Add(new ModularAttachmentSocket {id="socket&",label="<socket>",tags=new List<string>{"a&b"}});
        var named=Node("Escaping",204,settings);
        string fragment=(string)Editor.GetMethod("BuildXml",Static).Invoke(null,new object[]{new List<ModularRenderNode>{named}});
        var escaped=XElement.Parse("<settings>"+fragment+"</settings>");
        foreach(var f in typeof(ModularWeaponInternalStats).GetFields())
            check(Math.Abs(float.Parse(escaped.Element("internalStats").Element(f.Name).Value,System.Globalization.CultureInfo.InvariantCulture)
                -(float)f.GetValue(settings.internalStats))<.00001f,"Every raw physical/accuracy/timing field survives real editor XML export: "+f.Name);
        check((bool)escaped.Element("stock").Element("adjustable")
            && (float)escaped.Element("stock").Element("extendedOffset")==-.03f,"Stock state profiles survive real editor XML export");
        var propertyState=Editor.GetNestedType("NodePropertiesState",BindingFlags.NonPublic);
        object propertySnapshot=Activator.CreateInstance(propertyState,new object[]{settings});
        settings.internalStats.mechanicalPrecision=99;settings.internalStats.warmupReferenceCost=3;
        settings.internalStats.ammunitionPrecisionFactor=.8f;
        settings.stock.extendedOffset=.9f;
        propertyState.GetMethod("Restore").Invoke(propertySnapshot,null);
        check(settings.internalStats.mechanicalPrecision==9 && settings.internalStats.warmupReferenceCost==1.1f && settings.internalStats.ammunitionPrecisionFactor==1.12f
            && settings.stock.extendedOffset==-.03f,"Real Undo restores independent raw/stock profile copies");
        check(escaped.Element("mounts").Element("li").Element("label").Value=="<left & right>","Custom labels remain valid batch XML");
        check(escaped.Element("incompatibleParts").Element("li").Value=="Excluded&Part","Placement exports retain part conflicts with valid XML escaping");
        var rule=escaped.Element("conditionalIncompatibleParts").Element("li");
        check(rule.Element("part").Value=="Excluded&Part" && rule.Element("whenAnyParts").Element("li").Value=="Barrel&Condition",
            "Placement exports retain conditional conflict targets/conditions with valid XML escaping");

        var dialog=Bare<Dialog_ModularAttachmentEditor>();
        Set(dialog,"root",a.comp);Set(dialog,"workspace",workspace);
        foreach(string field in new[]{"views","numericBuffers","textBuffers","undoHistory"})
            Set(dialog,field,Activator.CreateInstance(Editor.GetField(field,Instance).FieldType));
        Set(dialog,"selectedPath","root");Set(dialog,"selectedPaths",new HashSet<string>{"root"});
        Set(dialog,"zoom",3f);Set(dialog,"pan",new Vector2(8,9));
        object undo=Field(dialog,"undoHistory");
        var change=Editor.GetMethod("SwitchWeapon",Instance);
        change.Invoke(dialog,new object[]{b.comp});
        check(ReferenceEquals(Field(dialog,"root"),b.comp),"In-editor weapon switch selects the next live weapon");
        check((float)Field(dialog,"zoom")==1,"A newly visited weapon gets a clean view");
        Set(dialog,"zoom",2f);change.Invoke(dialog,new object[]{a.comp});
        check((float)Field(dialog,"zoom")==3 && (Vector2)Field(dialog,"pan")==new Vector2(8,9),"Returning to a weapon restores its view");
        check(ReferenceEquals(undo,Field(dialog,"undoHistory")),"Undo history remains chronological across weapon switches");
        var select=Editor.GetMethod("SelectNode",Instance);
        select.Invoke(dialog,new object[]{child,false});select.Invoke(dialog,new object[]{a,true});
        check(((HashSet<string>)Field(dialog,"selectedPaths")).Count==2 && Field(dialog,"target").ToString()=="Graphic",
            "Additive selection enables multi-part graphic positioning");
        select.Invoke(dialog,new object[]{a,true});
        check(((HashSet<string>)Field(dialog,"selectedPaths")).Count==1 && (string)Field(dialog,"selectedPath")==child.path,
            "Ctrl-deselect leaves a valid active part");
        Editor.GetMethod("FindSelected",Instance).Invoke(dialog,new object[]{new List<ModularRenderNode>{a}});
        check(((HashSet<string>)Field(dialog,"selectedPaths")).SetEquals(new[]{"root"}),"Removing parts prunes stale multi-selection paths");
        var snapshotType=Editor.GetNestedType("EditorSnapshot",BindingFlags.NonPublic);
        object snapshot=snapshotType.GetMethod("Capture").Invoke(null,new object[]{nodesA,a.comp});
        check(ReferenceEquals(snapshotType.GetField("Owner").GetValue(snapshot),a.comp),"An undo record identifies which weapon to return to");
        Console.WriteLine("PASS: real batch/workspace XML, shared definitions, instance rails, multi-part motion and in-editor weapon switching.");
    }
}
