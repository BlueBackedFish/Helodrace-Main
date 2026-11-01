using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using GC=UnityEngine.Color;
using GV=UnityEngine.Vector2;
using GR=UnityEngine.Rect;
using GM=UnityEngine.Matrix4x4;
using GE=UnityEngine.Event;
using ET=UnityEngine.EventType;
using NV=System.Numerics.Vector2;
using NR=Waylith.Math.Rect;
using Texture=UnityEngine.Texture;
using Texture2D=UnityEngine.Texture2D;
using GUI=UnityEngine.GUI;
using GU=UnityEngine.GUIUtility;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private static bool weaponActive,weaponWrap=true;
        private static int weaponRailFrames;
        private static int weaponFrames,weaponInputs,weaponHot,weaponGroups,weaponReads,weaponBitPairs;
        private static GM weaponMatrix=GM.identity;
        private static readonly List<(int,int)> weaponScrollBits=new();
        private static GV weaponScrollOutput;
        private static bool WeaponScroll(GR __0,ref GV __1,GR __2)
        { if(!active||!weaponActive||trace==null)return true;weaponScrollBits.Add((BitConverter.SingleToInt32Bits(__1.x),BitConverter.SingleToInt32Bits(__1.y)));Add("scroll",__0,null,__1.x,__1.y);Add("view",__2);__1=weaponScrollOutput;return false; }
        private static bool WeaponField(GR __0,string __1,ref string __result)
        {if(!active||!weaponActive)return true;Add("field",__0,__1);__result=__1;return false;}
        private static bool WeaponSlider(GR __0,float __1,ref float __result)
        {if(!active||!weaponActive)return true;Add("slider",__0,null,__1);__result=__1;return false;}
        private static bool WeaponMouse(GR __0,ref bool __result)
        {if(!active||!weaponActive)return true;__result=__0.Contains(GE.current.mousePosition);return false;}
        private static bool WeaponGroup(GR __0)
        {if(!active||!weaponActive)return true;Add("group",__0);weaponGroups++;return false;}
        private static bool WeaponEnd(){if(!active||!weaponActive)return true;Add("end-group",default);return false;}
        private static bool WeaponFocus(string __0){if(!active||!weaponActive)return true;Add("focus",default,__0);return false;}
        private static bool WeaponLine(GV __0,GV __1,GC __2,float __3)
        {if(!active||!weaponActive)return true;Add("weapon-line",new GR(__0.x,__0.y,__1.x,__1.y),null,__2.r,__2.g,__2.b,__2.a,__3);return false;}
        private static bool WeaponImage(GR __0,Texture __1,GR __2,bool __3)
        {if(!active||!weaponActive)return true;Add("weapon-image",__0,__1?.name,__2.x,__2.y,__2.width,__2.height,__3?1:0);return false;}
        private static bool WeaponMatrixGet(ref GM __result){if(!active||!weaponActive)return true;__result=weaponMatrix;return false;}
        private static bool WeaponMatrixSet(GM __0){if(!active||!weaponActive)return true;weaponMatrix=__0;Add("matrix",default,null,Enumerable.Range(0,16).Select(i=>__0[i]).ToArray());return false;}
        private static bool WeaponHotGet(ref int __result){if(!active||!weaponActive)return true;__result=weaponHot;return false;}
        private static bool WeaponHotSet(int __0){if(!active||!weaponActive)return true;weaponHot=__0;return false;}
        private static bool WeaponControl(ref int __result){if(!active||!weaponActive)return true;__result=0x484457;return false;}
        private static bool WeaponWrapGet(ref bool __result){if(!active||!weaponActive)return true;__result=weaponWrap;return false;}
        private static bool WeaponWrapSet(bool __0){if(!active||!weaponActive)return true;weaponWrap=__0;return false;}
        private static bool WeaponEventType(GE __instance,ref ET __result)
        {if(!active||!weaponActive)return true;__result=(ET)typeof(GE).GetField("_type",All).GetValue(__instance);return false;}
        private static string WeaponConfiguration(string xml)
        {var doc=XDocument.Parse(xml);doc.Root.Elements().Single().Element("defName").Value="ConfigurationSnapshot";return doc.ToString(SaveOptions.DisableFormatting);}
        private static bool WeaponSize(string __0,ref GV __result){if(!active||!weaponActive)return true;__result=new GV((__0?.Length??0)*8,22);return false;}
        private static bool WeaponRead(MethodBase __originalMethod,ref object __result)
        {
            if(!active||!weaponActive)return true;Type type=((MethodInfo)__originalMethod).ReturnType;object value=Activator.CreateInstance(type,true);
            type.GetProperty("Alpha",All).SetValue(value,Enumerable.Repeat((byte)255,256*256).ToArray());type.GetProperty("VisibleUv",All).SetValue(value,new NR(0,0,1,1));type.GetProperty("HasPixels",All).SetValue(value,true);__result=value;weaponReads++;return false;
        }
        private static void WeaponState(object a,object e)
        {
            foreach(string name in new[]{"selectedPath","selectedSocketId","selectedAttachmentId","selectedCatalogGroup","navigationSearch","socketPage","socketPages","showRawStats","pressedPreviewPart"})
                if(!Equals(Get(a,name),Get(e,name)))throw new InvalidOperationException("Weapon view state changed: "+name);
            foreach(string name in new[]{"navigationScroll","catalogScroll","statsScroll"}){NV av=(NV)Get(a,name);Bits(new GV(av.X,av.Y),(GV)Get(e,name),"weapon "+name);}
            var ac=(IList)Get(a,"socketCards");var ec=(IList)Get(e,"socketCards");if(ac.Count!=ec.Count)throw new InvalidOperationException("Socket card count changed.");
            for(int i=0;i<ac.Count;i++)
            {NR r=(NR)Get(ac[i],"Rect");Rect(new GR(r.x,r.y,r.width,r.height),(GR)Get(ec[i],"Rect"));NV v=(NV)Get(ac[i],"Anchor");GV w=(GV)Get(ec[i],"Anchor");Same(v.X,w.x);Same(v.Y,w.y);}
            object ca=Get(a,"previewCamera"),ce=Get(e,"previewCamera");if(!Equals(ca.GetType().GetProperty("Zoom").GetValue(ca),ce.GetType().GetProperty("Zoom").GetValue(ce)))throw new InvalidOperationException("Weapon camera zoom changed.");
            NV pan=(NV)ca.GetType().GetProperty("Pan").GetValue(ca),old=(NV)ce.GetType().GetProperty("Pan").GetValue(ce);Same(pan.X,old.X);Same(pan.Y,old.Y);
        }
        private static void WeaponFrame(Dialog_ModularWeapon a,FrozenWeaponUI.Dialog_ModularWeapon e,GR area,ET type=ET.Repaint,GV point=default,GV delta=default,int button=0,int hot=0)
        {
            weaponScrollBits.Clear();(ET,int)after=default;
            Frame(()=>{a.DoWindowContents(area);after=(GE.current.type,weaponHot);},()=>{e.DoWindowContents(area);if(after!=(GE.current.type,weaponHot))throw new InvalidOperationException("Weapon event consumption/capture changed.");},
                ()=>{GE.current=new GE{type=type,rawType=type,mousePosition=point,delta=delta,button=button};weaponHot=hot;weaponMatrix=GM.identity;weaponWrap=true;});
            int half=weaponScrollBits.Count/2;if(weaponScrollBits.Count%2!=0||!weaponScrollBits.Take(half).SequenceEqual(weaponScrollBits.Skip(half)))throw new InvalidOperationException("Weapon scroll input bits changed.");weaponBitPairs+=half;WeaponState(a,e);weaponFrames++;
        }
        private static GV FreeWeaponPointer(object dialog,GR window)
        {
            float tree=Mathf.Clamp(window.width*.23f,260,320),stats=Mathf.Clamp(window.width*.21f,250,300);
            var rect=new GR(tree+12,40,window.width-tree-stats-24,window.height-40-206-38-8);
            for(int y=2;y<8;y++)for(int x=2;x<8;x++)
            {
                var point=new GV(rect.x+rect.width*x/10f,rect.y+rect.height*y/10f);GE.current=new GE{type=ET.MouseDown,rawType=ET.MouseDown,mousePosition=point,button=0};
                if(!(bool)Call(dialog,"PointerOverSocketCard",rect))return point;
            }
            throw new InvalidOperationException("No free preview surface for drag fixture.");
        }
        internal static void RunWeapon(string output,Action<bool,string> check)
        {
            NativeWeaponSocketChecks.Run(output,check);
            var h=new Harmony("Helodrace.Tests.NativeWeaponDialog");InstallUi(h,scrollPrefix:nameof(WeaponScroll));
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
            foreach(Type t in new[]{typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponTextureData",true),typeof(FrozenWeaponUI.ModularWeaponTextureData)})h.Patch(AccessTools.Method(t,"Read"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(WeaponRead)));
            GE original=GE.current;active=weaponActive=true;clickButtons=false;realtime=100;var things=new List<Thing>();
            try
            {
                foreach(string name in new[]{"HD_Gun_M16A4_Weapon","HD_Gun_M1911_Weapon"})
                {
                    ThingDef d=DefDatabase<ThingDef>.GetNamed(name);Thing thing=ThingMaker.MakeThing(d,GenStuff.DefaultStuffFor(d));things.Add(thing);var root=thing.TryGetComp<CompModularWeaponNode>();string before=ModularPresetXmlExporter.ExportWeapon(root);
                    var a=new Dialog_ModularWeapon(root);var e=new FrozenWeaponUI.Dialog_ModularWeapon(root);Bits(a.InitialSize,e.InitialSize,"weapon initial size");
                    foreach(GR area in new[]{new GR(0,0,1380,860),new GR(12.5f,-3.25f,900,640),new GR(-30,20,1700,1000),new GR(0,0,690,430)})
                        foreach(bool raw in new[]{false,true})foreach(string search in new[]{"","barrel","missing-no-match"})foreach(GV scroll in new[]{new GV(-0f,float.Epsilon),new GV(MathF.BitIncrement(.25f),91.25f)})
                        {
                            Set(a,"showRawStats",raw);Set(e,"showRawStats",raw);Set(a,"navigationSearch",search);Set(e,"navigationSearch",search);weaponScrollOutput=scroll;
                            foreach(string f in new[]{"navigationScroll","catalogScroll","statsScroll"}){Set(a,f,new NV(scroll.x,scroll.y));Set(e,f,scroll);}
                            WeaponFrame(a,e,area,point:new GV(10000,10000));realtime+=.125;
                        }
                    var rect=new GR(0,0,1380,860);GR preview=(GR)Call(e,"PreviewArea",new GR(332,40,724,568));GV pointer=preview.center;
                    foreach(float wheel in new[]{-3f,1f,30f,-30f})
                    {
                        object camera=Get(a,"previewCamera");float prior=(float)camera.GetType().GetProperty("Zoom").GetValue(camera);
                        WeaponFrame(a,e,rect,ET.ScrollWheel,pointer,new GV(0,wheel));weaponInputs++;
                        float expected=Mathf.Clamp(prior*Mathf.Pow(1.12f,-wheel),.25f,8f);
                        if((float)camera.GetType().GetProperty("Zoom").GetValue(camera)!=expected)throw new InvalidOperationException("Synthetic wheel did not apply expected zoom/clamp.");
                    }
                    Call(Get(a,"previewCamera"),"Reset");Call(Get(e,"previewCamera"),"Reset");
                    WeaponFrame(a,e,rect,point:new GV(10000,10000));
                    pointer=FreeWeaponPointer(a,rect);
                    WeaponFrame(a,e,rect,ET.MouseDown,pointer,button:0);weaponInputs++;
                    if(!(bool)Get(a,"previewDrag").GetType().GetProperty("Active").GetValue(Get(a,"previewDrag"))||weaponHot!=0x484457)throw new InvalidOperationException("Actual preview drag did not capture.");
                    WeaponFrame(a,e,rect,ET.MouseDrag,pointer+new GV(50,30),button:0,hot:0x484457);weaponInputs++;
                    NV moved=(NV)Get(a,"previewCamera").GetType().GetProperty("Pan").GetValue(Get(a,"previewCamera"));
                    if(moved.X!=50||moved.Y!=30)throw new InvalidOperationException("Actual preview drag did not pan by its delta.");
                    WeaponFrame(a,e,rect,ET.MouseUp,pointer+new GV(50,30),button:0,hot:0x484457);weaponInputs++;
                    if(weaponHot!=0||(bool)Get(a,"previewDrag").GetType().GetProperty("Active").GetValue(Get(a,"previewDrag")))throw new InvalidOperationException("Actual preview drag did not release capture.");
                    pointer=FreeWeaponPointer(a,rect);
                    WeaponFrame(a,e,rect,ET.MouseDown,pointer,button:0);weaponInputs++;
                    WeaponFrame(a,e,rect,ET.Ignore,pointer,button:0,hot:0x484457);weaponInputs++;
                    if(weaponHot!=0||(bool)Get(a,"previewDrag").GetType().GetProperty("Active").GetValue(Get(a,"previewDrag")))throw new InvalidOperationException("Ignored preview event did not cancel drag capture.");
                    foreach(ModularRenderNode node in root.RenderSnapshot().Where(n=>n.attachedToRail && (bool)Call(a,"IsPreviewPart",n)).Take(2))
                    {
                        Call(a,"SelectNode",node);Call(e,"SelectNode",node);
                        WeaponFrame(a,e,rect,point:new GV(10000,10000));weaponRailFrames++;
                        if(Get(a,"activeRailPart")==null || Get(a,"activeRailSocket")==null)throw new InvalidOperationException("Selected rail attachment did not exercise floating rail controls.");
                    }
                    string after=ModularPresetXmlExporter.ExportWeapon(root);
                    File.WriteAllText(Path.Combine(output,name+"-before.xml"),before);File.WriteAllText(Path.Combine(output,name+"-after.xml"),after);
                    if(WeaponConfiguration(before)!=WeaponConfiguration(after))throw new InvalidOperationException("Layout/preview mutated actual weapon configuration.");
                }
                check(weaponFrames==116+weaponRailFrames && weaponRailFrames>0,"two current weapon assemblies, four rectangles, raw/converted stats, search/scroll profiles and synthetic zoom/pan/cancel frames match frozen UI geometry/state");
                check(weaponBitPairs>0&&weaponGroups>0&&weaponReads>0,"native scroll bits, controlled alpha/cache paths and grouped preview/texture/UV/matrix submissions exercised");
                check(new[]{"navigationScroll","catalogScroll","statsScroll"}.All(n=>typeof(Dialog_ModularWeapon).GetField(n,All).FieldType==typeof(NV)),"weapon editor owns native scroll state and leaves source assemblies unchanged");
            }
            finally{GE.current=original;active=weaponActive=false;trace=null;h.UnpatchAll(h.Id);foreach(Thing t in things)if(!t.Destroyed)t.Destroy();}
            File.WriteAllText(Path.Combine(output,"native-weapon-ui.json"),JsonSerializer.Serialize(new{Frames=weaponFrames,Inputs=weaponInputs,RailFrames=weaponRailFrames,Submissions=widgets,ScrollBitPairs=weaponBitPairs,GroupObservationsBoth=weaponGroups,ControlledAlphaReads=weaponReads,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="586d610f",ControlledTextAndAlpha=true,SyntheticGameEvents=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
