using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.ModernWar;
using Verse;
using GC=UnityEngine.Color;
using GV=UnityEngine.Vector2;
using G3=UnityEngine.Vector3;
using GR=UnityEngine.Rect;
using GM=UnityEngine.Matrix4x4;
using GE=UnityEngine.Event;
using ET=UnityEngine.EventType;
using GUI=UnityEngine.GUI;
using GU=UnityEngine.GUIUtility;
using Texture=UnityEngine.Texture;
using Texture2D=UnityEngine.Texture2D;
using NV=System.Numerics.Vector2;
using N3=System.Numerics.Vector3;
using NR=Waylith.Math.Rect;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private static int attachmentDraws,attachmentDrags,attachmentUndos,attachmentViews,attachmentEquality,attachmentBits,attachmentMultiMoves;
        private static readonly List<(int,int)> attachmentScrollBits=new();
        private static GV attachmentScrollOutput;
        private static bool AttachmentScroll(GR __0,ref GV __1,GR __2)
        {if(!active||!weaponActive||trace==null)return true;attachmentScrollBits.Add((BitConverter.SingleToInt32Bits(__1.x),BitConverter.SingleToInt32Bits(__1.y)));Add("scroll",__0,null,__1.x,__1.y);Add("view",__2);__1=attachmentScrollOutput;return false;}
        private static bool AttachmentNumeric(GR __0,object[] __args)
        {if(!active||!weaponActive)return true;Add("numeric-preserved",__0,Convert.ToString(__args[2]),Convert.ToSingle(__args[1]));return false;}
        private static object AttachmentCapture(CompModularWeaponNode root)
        {Type t=typeof(FrozenAttachmentUI.Dialog_ModularAttachmentEditor).GetNestedType("EditorSnapshot",All);return t.GetMethod("Capture",All).Invoke(null,new object[]{root.RenderSnapshot(),root});}
        private static string AttachmentXml(CompModularWeaponNode root)=>FrozenAttachmentUI.Dialog_ModularAttachmentEditor.BuildXml(root.RenderSnapshot());
        private static void AttachmentRestore(object snapshot,CompModularWeaponNode root)
        {Call(snapshot,"Restore");root.InvalidateTree();Current.Game.GetComponent<GameComponent_ModularPlacementWorkspace>().Workspace.Refresh();}
        private static void AttachmentState(object a,object e)
        {
            foreach(string f in new[]{"treeScroll","inspectorScroll","pan"}){NV n=(NV)Get(a,f);Bits(new GV(n.X,n.Y),(GV)Get(e,f),"attachment "+f);}
            foreach(string f in new[]{"selectedPath","selectedMountIndex","selectedSocketIndex","target","section","movementConstraint","zoom","inspectorContentHeight","inspectorScrollContext","activeEditKey"})
                if(Convert.ToString(Get(a,f))!=Convert.ToString(Get(e,f)))throw new InvalidOperationException("Attachment view state changed: "+f);
            if(((IList)Get(a,"undoHistory")).Count!=((IList)Get(e,"undoHistory")).Count)throw new InvalidOperationException("Attachment undo count changed.");
        }
        private static void AttachmentFrame(object a,object e,CompModularWeaponNode root,object snapshot,Action left,Action right,ET type=ET.Repaint,GV point=default,GV delta=default,int button=0)
        {
            string xml=null;ET consumed=default;attachmentScrollBits.Clear();
            Frame(()=>{left();xml=AttachmentXml(root);consumed=GE.current.type;},()=>{right();if(xml!=AttachmentXml(root))throw new InvalidOperationException("Attachment live authoring/export state changed.");if(consumed!=GE.current.type)throw new InvalidOperationException("Attachment event consumption changed.");},
                ()=>{AttachmentRestore(snapshot,root);GE.current=new GE{type=type,rawType=type,mousePosition=point,delta=delta,button=button};weaponMatrix=GM.identity;weaponWrap=true;weaponHot=0;});
            int half=attachmentScrollBits.Count/2;if(attachmentScrollBits.Count%2!=0||!attachmentScrollBits.Take(half).SequenceEqual(attachmentScrollBits.Skip(half)))throw new InvalidOperationException("Attachment scroll bits changed.");attachmentBits+=half;AttachmentState(a,e);
        }
        private static void AttachmentDefaults(CompProperties_ModularWeaponNode p)
        {
            p.flashlight=new ModularWeaponFlashlightProperties{emitterOffset=new G3(.125f,0,-.0625f),farWidth=.7f,nearWidth=.1f};
            p.laser=new ModularWeaponLaserProperties{emitterOffset=new G3(-.125f,0,.0625f),maxLength=2.5f,color=new GC(.2f,.9f,.3f,.7f)};
            p.sight=new ModularWeaponSightProperties{axisOffset=new G3(.125f,0,.2f)};
            p.casingPort=new ModularWeaponCasingPortProperties{offset=new G3(.1f,0,.08f)};
            p.animatedPart=ModularWeaponAnimatedPartKind.Bolt;p.animationTravel=new G3(.08f,0,-.04f);p.animationPivot=new G3(-.03f,0,.02f);p.animationAngle=27;
            if(p.mounts.Count==0)p.mounts.Add(new ModularAttachmentMount{id="controlled_mount",transform=new ModularAttachmentTransform{position=new G3(.1f,0,.2f)}});
        }
        private static void AttachmentSemantics(Action<bool,string> check)
        {
            Type t=typeof(Dialog_ModularAttachmentEditor);
            float[] values={-0f,float.Epsilon,-float.Epsilon,1e-9f,.000009999f,.00001f,.000010001f,1f,MathF.BitIncrement(1f),float.NaN,float.PositiveInfinity};
            foreach(float x in values)foreach(float y in values)
            {
                bool a=(bool)t.GetMethod("IsAuthoredUnitScale",All).Invoke(null,new object[]{new NV(x,y)});bool e=new GV(x,y)==GV.one;
                if(a!=e)throw new InvalidOperationException("Current 1.7 unit-scale export equality changed.");attachmentEquality++;
                foreach(float z in new[]{-0f,float.Epsilon,.00001f,float.NaN})
                {a=(bool)t.GetMethod("IsAuthoredZeroOffset",All).Invoke(null,new object[]{new N3(x,y,z)});e=new G3(x,y,z)==G3.zero;if(a!=e)throw new InvalidOperationException("Current 1.7 zero-offset export equality changed.");attachmentEquality++;}
            }
            check(!ParseHelper.HandlesType(typeof(NV))&&!ParseHelper.HandlesType(typeof(N3))&&ParseHelper.HandlesType(typeof(G3)),"actual 1.7 parser requires current authoring DTOs; editor calculations and undo use native values");
            check(attachmentEquality==605,"native default scale/offset export decisions match actual exact 1.7 compatibility operators, including near-default/nonfinite values");
        }
        internal static void RunAttachment(string output,Action<bool,string> check)
        {
            AttachmentSemantics(check);
            var h=new Harmony("Helodrace.Tests.NativeAttachmentEditor");InstallUi(h,scrollPrefix:nameof(AttachmentScroll),numericPrefix:nameof(AttachmentNumeric));
            void P(Type t,string n,string p,params Type[] args)=>h.Patch(AccessTools.Method(t,n,args),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),p));
            void G(Type t,string n,string p)=>h.Patch(AccessTools.PropertyGetter(t,n),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),p));
            void S(Type t,string n,string p)=>h.Patch(AccessTools.PropertySetter(t,n),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),p));
            G(typeof(Waylith.Time),"RealtimeSinceStartupAsDouble",nameof(Time));G(typeof(GE),"type",nameof(WeaponEventType));
            foreach(MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m=>m.GetParameters().Length>1&&m.GetParameters()[0].ParameterType==typeof(GR)&&!m.IsGenericMethodDefinition))
                if(method.ReturnType==typeof(string)&&method.GetParameters()[1].ParameterType==typeof(string))h.Patch(method,prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(WeaponField)));
                else if(method.ReturnType==typeof(float)&&method.GetParameters()[1].ParameterType==typeof(float))h.Patch(method,prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(WeaponSlider)));
            P(typeof(Mouse),"IsOver",nameof(WeaponMouse),typeof(GR));P(typeof(Widgets),"DrawLine",nameof(WeaponLine),typeof(GV),typeof(GV),typeof(GC),typeof(float));
            P(typeof(GUI),"BeginGroup",nameof(WeaponGroup),typeof(GR));P(typeof(GUI),"EndGroup",nameof(WeaponEnd),Type.EmptyTypes);P(typeof(GUI),"FocusControl",nameof(WeaponFocus),typeof(string));
            P(typeof(GUI),"DrawTextureWithTexCoords",nameof(WeaponImage),typeof(GR),typeof(Texture),typeof(GR),typeof(bool));G(typeof(GUI),"matrix",nameof(WeaponMatrixGet));S(typeof(GUI),"matrix",nameof(WeaponMatrixSet));
            G(typeof(GU),"hotControl",nameof(WeaponHotGet));S(typeof(GU),"hotControl",nameof(WeaponHotSet));P(typeof(GU),"GetControlID",nameof(WeaponControl),typeof(int),typeof(UnityEngine.FocusType));
            G(typeof(Text),"WordWrap",nameof(WeaponWrapGet));S(typeof(Text),"WordWrap",nameof(WeaponWrapSet));P(typeof(Text),"CalcSize",nameof(WeaponSize),typeof(string));

            h.Patch(AccessTools.GetDeclaredMethods(typeof(Widgets)).Single(m=>m.Name=="TextFieldNumeric"&&m.IsGenericMethodDefinition).MakeGenericMethod(typeof(float)),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(AttachmentNumeric)));
            GE original=GE.current;active=weaponActive=true;clickButtons=false;realtime=100;
            var thing=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M16A4_Weapon"));var root=thing.TryGetComp<CompModularWeaponNode>();object originalState=AttachmentCapture(root);Thing secondThing=null;
            try
            {
                AttachmentDefaults(root.Props);root.InvalidateTree();object baseline=AttachmentCapture(root);
                var a=new Dialog_ModularAttachmentEditor(root);var e=new FrozenAttachmentUI.Dialog_ModularAttachmentEditor(root);Bits(a.InitialSize,e.InitialSize,"attachment initial size");
                string[] targets={"Graphic","Mount","Socket","Flashlight","Laser","Sight","AnimationPart","CasingPort","FeedStart","FeedEnd"};
                foreach(GR area in new[]{new GR(0,0,1380,850),new GR(7.25f,-3.5f,1000,650),new GR(-25,30,1700,1000)})
                    foreach(string section in new[]{"Placement","Animation"})foreach(string target in targets)
                        foreach(GV scroll in new[]{new GV(-0f,float.Epsilon),new GV(float.Epsilon,-0f),new GV(MathF.BitIncrement(.25f),64.5f)})
                        {
                            EnumField(a,"section",section);EnumField(e,"section",section);EnumField(a,"target",target);EnumField(e,"target",target);attachmentScrollOutput=scroll;
                            Set(a,"treeScroll",new NV(scroll.x,scroll.y));Set(e,"treeScroll",scroll);Set(a,"inspectorScroll",new NV(scroll.x,scroll.y));Set(e,"inspectorScroll",scroll);
                            AttachmentFrame(a,e,root,baseline,()=>a.DoWindowContents(area),()=>e.DoWindowContents(area),point:new GV(10000,10000));attachmentDraws++;realtime+=.125;
                        }
                foreach(string target in targets)foreach(string constraint in new[]{"Free","Horizontal","Vertical"})
                {
                    EnumField(a,"target",target);EnumField(e,"target",target);EnumField(a,"movementConstraint",constraint);EnumField(e,"movementConstraint",constraint);Set(a,"activeEditKey",null);Set(e,"activeEditKey",null);((IList)Get(a,"undoHistory")).Clear();((IList)Get(e,"undoHistory")).Clear();
                    var rect=new GR(0,0,400,300);string before=null;
                    AttachmentRestore(baseline,root);before=AttachmentXml(root);
                    AttachmentFrame(a,e,root,baseline,()=>Call(a,"HandleMarkerDrag",rect,root.RenderSnapshot().Single(n=>n.path=="root"),100f),()=>Call(e,"HandleMarkerDrag",rect,root.RenderSnapshot().Single(n=>n.path=="root"),100f),ET.MouseDrag,new GV(100,100),new GV(12,-8));attachmentDrags++;
                    if(before==AttachmentXml(root))throw new InvalidOperationException("Marker drag did not edit actual authoring data: "+target);
                    object edited=AttachmentCapture(root);
                    AttachmentFrame(a,e,root,edited,()=>Call(a,"UndoLastEdit"),()=>Call(e,"UndoLastEdit"));attachmentUndos++;
                    if(before!=AttachmentXml(root))throw new InvalidOperationException("Native undo did not restore exact original authoring XML.");
                }
                foreach(float angle in new[]{0f,37f,90f})foreach(NV scale in new[]{new NV(1,1),new NV(-.5f,2),new NV(-.000001f,.000001f)})foreach(GV delta in new[]{new GV(.125f,-.25f),new GV(-.75f,.5f)})
                {
                    AttachmentRestore(baseline,root);var node=(ModularRenderNode)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root.RenderSnapshot().Single(n=>n.path=="root"),null);
                    node.transform=new ModularTransform2D{position=NV.Zero,angle=angle,scale=scale};var nodes=new List<ModularRenderNode>{node,node};string before=AttachmentXml(root);
                    AttachmentFrame(a,e,root,baseline,()=>Static(typeof(Dialog_ModularAttachmentEditor),"MoveGraphics",nodes,delta),()=>Static(typeof(FrozenAttachmentUI.Dialog_ModularAttachmentEditor),"MoveGraphics",nodes,delta));attachmentMultiMoves++;
                    if(before==AttachmentXml(root))throw new InvalidOperationException("Shared-Def multi-move did not edit authoring data.");
                }
                secondThing=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M1911_Weapon"));var second=secondThing.TryGetComp<CompModularWeaponNode>();
                var pan=new GV(-0f,float.Epsilon);var scrollBack=new GV(MathF.BitIncrement(.25f),-0f);
                Set(a,"pan",new NV(pan.x,pan.y));Set(e,"pan",pan);Set(a,"treeScroll",new NV(scrollBack.x,scrollBack.y));Set(e,"treeScroll",scrollBack);Set(a,"zoom",1.75f);Set(e,"zoom",1.75f);
                Call(a,"SwitchWeapon",second);Call(e,"SwitchWeapon",second);AttachmentState(a,e);attachmentViews++;
                Set(a,"pan",new NV(10,-20));Set(e,"pan",new GV(10,-20));Set(a,"zoom",2.5f);Set(e,"zoom",2.5f);
                Call(a,"SwitchWeapon",root);Call(e,"SwitchWeapon",root);AttachmentState(a,e);attachmentViews++;
                NV restored=(NV)Get(a,"pan"),restoredScroll=(NV)Get(a,"treeScroll");Bits(new GV(restored.X,restored.Y),pan,"restored attachment view pan");Bits(new GV(restoredScroll.X,restoredScroll.Y),scrollBack,"restored attachment view scroll");
                if((float)Get(a,"zoom")!=1.75f)throw new InvalidOperationException("Attachment per-weapon zoom did not restore.");
                check(attachmentDraws==180,"attachment editor placement/animation and ten marker targets match frozen UI across three sizes and three scroll bit profiles");
                check(attachmentDrags==30&&attachmentUndos==30&&attachmentMultiMoves==18,"actual live marker edits in all three movement constraints and native undo restore exact authoring XML and event behavior");
                check(attachmentViews==2&&attachmentBits>0&&new[]{"treeScroll","inspectorScroll","pan"}.All(n=>typeof(Dialog_ModularAttachmentEditor).GetField(n,All).FieldType==typeof(NV)),"editor and per-weapon state use native geometry; ref scroll bits round-trip exactly");
            }
            finally{AttachmentRestore(originalState,root);GE.current=original;active=weaponActive=false;trace=null;h.UnpatchAll(h.Id);if(!thing.Destroyed)thing.Destroy();if(secondThing!=null&&!secondThing.Destroyed)secondThing.Destroy();}
            File.WriteAllText(Path.Combine(output,"native-attachment-ui.json"),JsonSerializer.Serialize(new{Frames=frames,Draws=attachmentDraws,Drags=attachmentDrags,Undos=attachmentUndos,MultiMoves=attachmentMultiMoves,ViewCases=attachmentViews,EqualityCases=attachmentEquality,Submissions=widgets,ScrollBitPairs=attachmentBits,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="21e76c3d",ControlledFeatureData=true,ControlledTextAndWidgets=true,SyntheticEvents=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
