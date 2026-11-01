using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using GC = UnityEngine.Color;
using GV = UnityEngine.Vector2;
using GR = UnityEngine.Rect;
using GE = UnityEngine.Event;
using ET = UnityEngine.EventType;
using NV = System.Numerics.Vector2;
using NR = Waylith.Math.Rect;
using Texture = UnityEngine.Texture;
using Texture2D = UnityEngine.Texture2D;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private static bool armorActive;
        private static int armorTextureMode, armorDrawFrames, armorActions, armorInputFrames, armorScrollBitPairs, armorLines, armorImages;
        private static readonly List<(int,int)> armorScrollBits = new();
        private static GV armorScrollOutput;
        private static bool ArmorTextures(ref List<Texture2D> __result)
        {
            if (!active || !armorActive) return true;
            __result = armorTextureMode == 0 ? new List<Texture2D>() : armorTextureMode == 1
                ? new List<Texture2D>{BaseContent.WhiteTex} : new List<Texture2D>{BaseContent.WhiteTex,BaseContent.GreyTex};
            return false;
        }
        private static bool ArmorMouse(GR __0, ref bool __result)
        { if (!active || !armorActive) return true; __result=__0.Contains(GE.current.mousePosition); return false; }
        private static bool ArmorLine(GV __0, GV __1, GC __2, float __3)
        { if (!active || !armorActive) return true; Add("armor-line",new GR(__0.x,__0.y,__1.x,__1.y),null,__2.r,__2.g,__2.b,__2.a,__3); armorLines++; return false; }
        private static bool ArmorImage(GR __0, Texture __1, GR __2, bool __3)
        { if (!active || !armorActive) return true; Add("armor-image",__0,RuntimeHelpers.GetHashCode(__1).ToString(),__2.x,__2.y,__2.width,__2.height,__3?1:0); armorImages++; return false; }
        private static bool ArmorScroll(GR __0, ref GV __1, GR __2)
        {
            if (!active || !armorActive || trace==null) return true;
            armorScrollBits.Add((BitConverter.SingleToInt32Bits(__1.x),BitConverter.SingleToInt32Bits(__1.y)));
            Add("scroll",__0,null,__1.x,__1.y); Add("view",__2); __1=armorScrollOutput; scrollCalls++; return false;
        }
        private static void ArmorScrollState(object actual, object expected)
        {
            foreach (string field in new[]{"slotsScroll","catalogScroll","statsScroll"})
            {
                NV a=(NV)Get(actual,field); GV e=(GV)Get(expected,field);
                Bits(new GV(a.X,a.Y),e,"armor "+field);
            }
            foreach(string field in new[]{"previewFacing","showRawStats"})
                if(!Equals(Get(actual,field),Get(expected,field))) throw new InvalidOperationException("Armor view state changed: "+field);
            foreach(string field in new[]{"selectedPalsPart","selectedInstalledPart"})
            {
                object a=Get(actual,field),e=Get(expected,field);
                if(field=="selectedPalsPart" ? !ReferenceEquals(a,e) : (a==null)!=(e==null)) throw new InvalidOperationException("Armor selection state changed.");
            }
            var aa=(CompModularArmor)Get(actual,"comp"); var ee=(CompModularArmor)Get(expected,"comp");
            var signature=AccessTools.Method(typeof(CompModularArmor),"ConfigurationSignature");
            if(!Equals(signature.Invoke(aa,null),signature.Invoke(ee,null))) throw new InvalidOperationException("Armor draft configuration changed.");
            var am=(IDictionary)Get(actual,"statMotions"); var em=(IDictionary)Get(expected,"statMotions");
            if(am.Count!=em.Count) throw new InvalidOperationException("Armor stat motion count changed.");
            foreach(object key in am.Keys)
                foreach(FieldInfo f in am[key].GetType().GetFields(All|BindingFlags.DeclaredOnly))
                    if(!Equals(f.GetValue(am[key]),Get(em[key],f.Name))) throw new InvalidOperationException("Armor stat animation state changed: "+key+" "+f.Name+" "+f.GetValue(am[key])+" != "+Get(em[key],f.Name));
        }
        private static void ArmorFrame(object actual, object expected, Action a, Action e, ET type=ET.Repaint, GV point=default, int button=0, Action reset=null)
        {
            armorScrollBits.Clear(); ET after=default;
            Frame(()=>{a(); after=GE.current.type;},()=>{e(); if(after!=GE.current.type) throw new InvalidOperationException("Armor input consumption changed.");},
                ()=>{GE.current=new GE{type=type,rawType=type,mousePosition=point,button=button};reset?.Invoke();});
            int half=armorScrollBits.Count/2;
            if(armorScrollBits.Count%2!=0 || !armorScrollBits.Take(half).SequenceEqual(armorScrollBits.Skip(half))) throw new InvalidOperationException("Armor scroll input bits changed.");
            armorScrollBitPairs+=half; ArmorScrollState(actual,expected);
        }
        private static void PrimeArmorBounds(object dialog)
        {
            object bounds=Get(dialog,"previewBounds"); var cache=(IDictionary)Get(bounds,"cache");
            cache.Add(BaseContent.WhiteTex,new NR(.125f,.25f,.5f,.375f)); cache.Add(BaseContent.GreyTex,new NR(0,.125f,.875f,.75f));
        }
        private static void ResetArmorScroll(object a, object e, GV value)
        {
            foreach(string field in new[]{"slotsScroll","catalogScroll","statsScroll"}) {Set(a,field,new NV(value.x,value.y));Set(e,field,value);}
        }
        private static void ArmorWindows(CompModularArmor original, CompClothingWorkbench bench)
        {
            var a=new Dialog_ModularArmor(original,bench); var e=new FrozenArmorUI.Dialog_ModularArmor(original,bench);
            PrimeArmorBounds(a); PrimeArmorBounds(e); Bits(a.InitialSize,e.InitialSize,"armor initial window size");
            foreach(GR area in new[]{new GR(0,0,1280,820),new GR(7.25f,-3.5f,760,560),new GR(-20,12,1600,1000)})
                foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
                    foreach(bool raw in new[]{false,true})
                        foreach(int textureMode in new[]{0,1,2})
                            foreach(GV scroll in new[]{new GV(-0f,float.Epsilon),new GV(float.Epsilon,-0f),new GV(MathF.BitIncrement(.25f),91.25f)})
                            {
                                Set(a,"previewFacing",facing);Set(e,"previewFacing",facing);Set(a,"showRawStats",raw);Set(e,"showRawStats",raw);
                                armorTextureMode=textureMode;armorScrollOutput=scroll;realtime=100+armorDrawFrames*.03125;
                                ArmorFrame(a,e,()=>a.DoWindowContents(area),()=>e.DoWindowContents(area),point:new GV(area.width*.5f,area.height*.5f),reset:()=>ResetArmorScroll(a,e,scroll)); armorDrawFrames++;
                            }
            foreach(string arrow in new[]{"◀","▶","▶","◀"})
            {
                clickButtons=true;clickText=arrow;
                ArmorFrame(a,e,()=>Call(a,"DrawPreviewFacingControls",new GR(10,20,600,400),new List<ModularArmorSlotDef>()),()=>Call(e,"DrawPreviewFacingControls",new GR(10,20,600,400),new List<ModularArmorSlotDef>()));
                if(!clicked)throw new InvalidOperationException("Armor facing button did not fire."); armorActions++;
            }
            clickButtons=true;
            for(int i=0;i<2;i++)
            {
                clickText=((bool)Get(a,"showRawStats")?"HD_ModularArmor_StatsModeRaw":"HD_ModularArmor_StatsModeConverted").Translate();
                ArmorFrame(a,e,()=>Call(a,"DrawArmorStatsPanel",new GR(0,0,300,500)),()=>Call(e,"DrawArmorStatsPanel",new GR(0,0,300,500)));
                if(!clicked)throw new InvalidOperationException("Armor stat-mode button did not fire."); armorActions++;
            }
            clickButtons=false;
            var ac=(CompModularArmor)Get(a,"comp"); var ec=(CompModularArmor)Get(e,"comp");
            var clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
            var props=(CompProperties_ModularArmor)clone.Invoke(ac.props,null);
            ac.props=ec.props=props; Set(a,"showRawStats",true);Set(e,"showRawStats",true);
            foreach(float value in new[]{-5f,0f,12f,3f})
            {
                props.baseErgonomics=value; realtime+=.125;
                foreach(double dt in new[]{0d,.05d,.125d,1.8d,2d})
                { realtime+=dt;ArmorFrame(a,e,()=>Call(a,"DrawArmorStatsPanel",new GR(0,0,300,500)),()=>Call(e,"DrawArmorStatsPanel",new GR(0,0,300,500))); }
            }
        }
        private static void ArmorPals(CompModularArmor original, CompClothingWorkbench bench)
        {
            var a=new Dialog_ModularArmor(original,bench);var e=new FrozenArmorUI.Dialog_ModularArmor(original,bench);
            var ac=(CompModularArmor)Get(a,"comp");var ec=(CompModularArmor)Get(e,"comp");
            var panel=new ModularArmorPalsPanelDef{defName="NativeArmorPanel",label="controlled PALS",columns=6,rows=4};
            var part=new ModularArmorPartDef{defName="NativeArmorPart",label="controlled pouch",installMode=ModularArmorInstallMode.Positionable,palsWidth=2,palsHeight=1,allowedPalsPanels=new List<ModularArmorPalsPanelDef>{panel}};
            var clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
            var props=(CompProperties_ModularArmor)clone.Invoke(ac.props,null);props.palsPanels=new List<ModularArmorPalsPanelDef>{panel};ac.props=ec.props=props;
            var grid=new GR(20,40,300,160);float cw=50,ch=40;
            void SelectPart(){Set(a,"selectedPalsPart",part);Set(e,"selectedPalsPart",part);}
            void Input(float x,float y,int button=0)
            {
                ArmorFrame(a,e,()=>Call(a,"HandlePalsInput",grid,cw,ch,panel,ac.PartsOnPanel(panel).ToList()),()=>Call(e,"HandlePalsInput",grid,cw,ch,panel,ec.PartsOnPanel(panel).ToList()),ET.MouseDown,new GV(grid.x+x*cw,grid.y+y*ch),button);armorInputFrames++;
            }
            SelectPart();Input(3.25f,1.5f);
            if(ac.PartsOnPanel(panel).Count()!=1 || Get(a,"selectedPalsPart")!=null)throw new InvalidOperationException("Actual PALS placement did not occur.");
            Input(3.25f,1.5f);Input(.5f,.5f);
            var record=ac.PartsOnPanel(panel).Single();if(record.palsX!=0 || record.palsY!=0 || Get(a,"selectedInstalledPart")!=null)throw new InvalidOperationException("Actual PALS selection/move did not occur.");
            SelectPart();Input(5.9f,3.9f);Input(-.001f,1);Input(6,1);Input(.5f,.5f,2);
            if(ac.PartsOnPanel(panel).Count()!=1 || Get(a,"selectedPalsPart")!=part)throw new InvalidOperationException("Invalid/outside/other-button PALS input changed placement.");
            Input(.5f,.5f,1);if(ac.PartsOnPanel(panel).Any())throw new InvalidOperationException("Actual PALS removal did not occur.");
            SelectPart();
            foreach(GV point in new[]{new GV(grid.x,grid.y),new GV(grid.x+grid.width-.001f,grid.y+grid.height-.001f),new GV(grid.x-1,grid.y),new GV(grid.x+60,grid.y+60)})
            {
                ArmorFrame(a,e,()=>Call(a,"DrawPlacementPreview",grid,cw,ch,panel),()=>Call(e,"DrawPlacementPreview",grid,cw,ch,panel),point:point);armorInputFrames++;
            }
        }
        internal static void RunArmor(string output,Action<bool,string> check)
        {
            var h=new Harmony("Helodrace.Tests.NativeArmorDialog");InstallUi(h,scrollPrefix:nameof(ArmorScroll));
            h.Patch(AccessTools.PropertyGetter(typeof(Waylith.Time),"RealtimeSinceStartupAsDouble"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(Time)));
            h.Patch(AccessTools.Method(typeof(Mouse),"IsOver",new[]{typeof(GR)}),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(ArmorMouse)));
            h.Patch(AccessTools.Method(typeof(Widgets),"DrawLine",new[]{typeof(GV),typeof(GV),typeof(GC),typeof(float)}),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(ArmorLine)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.GUI),"DrawTextureWithTexCoords",new[]{typeof(GR),typeof(Texture),typeof(GR),typeof(bool)}),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(ArmorImage)));
            foreach(Type type in new[]{typeof(Dialog_ModularArmor),typeof(FrozenArmorUI.Dialog_ModularArmor)}) h.Patch(AccessTools.Method(type,"PreviewTextures"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(ArmorTextures)));
            GE originalEvent=GE.current; active=armorActive=true;clickButtons=false;
            var map=Find.CurrentMap;
            ThingDef benchDef=DefDatabase<ThingDef>.AllDefsListForReading.First(d=>d.GetCompProperties<CompProperties_ClothingWorkbench>()!=null);
            Thing benchThing=ThingMaker.MakeThing(benchDef,GenStuff.DefaultStuffFor(benchDef));benchThing.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(benchThing,map.Center,map);
            var bench=benchThing.TryGetComp<CompClothingWorkbench>();var apparel=new List<Thing>();
            try
            {
                foreach(string name in new[]{"HD_Apparel_IBTVAssault","HD_Apparel_MOLLEBattleBelt","HD_Apparel_FASTMT"})
                {
                    Thing item=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name),GenStuff.DefaultStuffFor(DefDatabase<ThingDef>.GetNamed(name)));GenSpawn.Spawn(item,map.Center+new IntVec3(apparel.Count+3,0,0),map);apparel.Add(item);
                    CompModularArmor comp=item.TryGetComp<CompModularArmor>(); string before=(string)Call(comp,"ConfigurationSignature");
                    ArmorWindows(comp,bench);if(apparel.Count==1)ArmorPals(comp,bench);
                    if(before!=(string)Call(comp,"ConfigurationSignature"))throw new InvalidOperationException("UI tests mutated original armor instead of editor drafts.");
                }
                check(armorDrawFrames==648,"three current armor types/four facings/three sizes/raw-converted stats/three texture modes/three scroll bit profiles match frozen dialogs");
                check(armorActions==18 && armorInputFrames==12,"facing/stat buttons and actual PALS draft place/select/move/reject/remove/preview match frozen input and event consumption");
                check(armorScrollBitPairs>0 && armorImages>0 && armorLines>0,"native ref scroll preserves signed-zero/subnormal/adjacent float bits and PALS lines/preview image UV submissions match");
                check(new[]{"slotsScroll","catalogScroll","statsScroll"}.All(n=>typeof(Dialog_ModularArmor).GetField(n,All).FieldType==typeof(NV)),"all armor dialog owned scroll state is native and original armor configurations stay unchanged");
            }
            finally
            { GE.current=originalEvent;active=armorActive=false;trace=null;clickButtons=false;h.UnpatchAll(h.Id);foreach(Thing item in apparel)if(!item.Destroyed)item.Destroy();if(!benchThing.Destroyed)benchThing.Destroy(); }
            File.WriteAllText(Path.Combine(output,"native-armor-ui.json"),JsonSerializer.Serialize(new{Frames=frames,DrawFrames=armorDrawFrames,Actions=armorActions,InputFrames=armorInputFrames,Submissions=widgets,ScrollBitPairs=armorScrollBitPairs,LineSubmissionsBoth=armorLines,ImageSubmissionsBoth=armorImages,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="b86b8353",ControlledTextMetrics=true,ControlledPreviewTextures=true,SyntheticGameEvents=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
