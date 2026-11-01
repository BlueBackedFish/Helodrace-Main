using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using GC=UnityEngine.Color;
using GV=UnityEngine.Vector2;
using GR=UnityEngine.Rect;
using NV=System.Numerics.Vector2;
using GE=UnityEngine.Event;
using GUI=UnityEngine.GUI;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private static int smallMode,smallLoadouts,smallMechanical,smallGas,smallBits,smallActions;
        private static bool smallBlocked;
        private static Thing smallSelected;
        private static IntVec3 smallCell;
        private static float[] smallDensity;
        private static readonly List<(int,int)> smallScrollInputs=new();
        private static GV smallScrollOutput;
        private static bool SmallScroll(GR __0,ref GV __1,GR __2)
        {if(!active||trace==null)return true;smallScrollInputs.Add((BitConverter.SingleToInt32Bits(__1.x),BitConverter.SingleToInt32Bits(__1.y)));Add("scroll",__0,null,__1.x,__1.y);Add("view",__2);__1=smallScrollOutput;return false;}
        private static bool SmallBool(MethodBase __originalMethod,GR __0,object[] __args,ref bool __result)
        {
            if(!active||trace==null)return true; Add(__originalMethod.Name,__0,TextArg(__args));
            __result=!clicked&&((smallMode==1&&__originalMethod.Name=="ButtonInvisible")||(smallMode==2&&__originalMethod.Name=="ButtonText"&&TextArg(__args)=="−")||(smallMode==3&&__originalMethod.Name=="ButtonText"&&TextArg(__args)=="+"));
            if(__result)clicked=true;return false;
        }
        private static bool SmallNumeric(GR __0,ref int __1,ref string __2)
        {if(!active||trace==null)return true;Add("numeric",__0,__2,__1);if(smallMode==4||smallMode==5){__1=smallMode==4?9999:0;__2=__1.ToString();}return false;}
        private static bool SmallSelected(ref Thing __result){if(!active)return true;__result=smallSelected;return false;}
        private static bool SmallBlocked(ref bool __result){if(!active)return true;__result=smallBlocked;return false;}
        private static bool SmallCell(ref IntVec3 __result){if(!active)return true;__result=smallCell;return false;}
        private static bool SmallDensity(HelodGasDef __2,ref float __result)
        {if(!active)return true;var defs=GasDefs();int i=Array.IndexOf(defs,__2);if(i<0)throw new InvalidOperationException("Unknown gas fixture.");__result=smallDensity[i];return false;}
        private static HelodGasDef[] GasDefs()=>new[]{HelodGasDefOf.HD_PhotochlorogenGasGrid,HelodGasDefOf.HD_SweetGasGrid,HelodGasDefOf.HD_CSGasGrid,HelodGasDefOf.HD_CNGasGrid,HelodGasDefOf.HD_HCSmokeGrid,HelodGasDefOf.HD_WhitePhosphorusSmokeGrid};
        private static bool SmallSize(string __0,ref GV __result)
        {if(!active)return true;Add("measure-size",default,__0);__result=new GV((__0?.Length??0)*7.5f,(__0??"").Count(c=>c=='\n')*18+18);return false;}
        private static void SmallFrame(Action a,Action e,Action prepare=null)
        {
            smallScrollInputs.Clear();Frame(a,e,prepare);int half=smallScrollInputs.Count/2;
            if(smallScrollInputs.Count%2!=0||!smallScrollInputs.Take(half).SequenceEqual(smallScrollInputs.Skip(half)))throw new InvalidOperationException("Small UI scroll input bits changed.");smallBits+=half;
        }
        private static void SmallScrollState(object a,object e)
        {NV v=(NV)Get(a,"scrollPosition");GV g=(GV)Get(e,"scrollPosition");Bits(new GV(v.X,v.Y),g,"small UI scroll");}
        private static void SmallLoadout(Pawn pawn,CompM6RocketBag bag)
        {
            var defs=bag.AllowedAmmoDefs.ToArray();if(defs.Length==0)throw new InvalidOperationException("Actual ammo definitions absent.");
            foreach(GR area in new[]{new GR(0,0,760,620),new GR(7.5f,-3.25f,1000,800),new GR(-12,25,420,300)})
                foreach(GV scroll in new[]{GV.zero,new GV(-0f,float.Epsilon),new GV(MathF.BitIncrement(.25f),300.5f)})
                    foreach(string search in new[]{"","7.62","grenade","NoMatch_9381"})foreach(bool expanded in new[]{false,true})
                    SmallLoadoutFrame(bag,defs,area,scroll,search,expanded,0,2);
            foreach(int mode in new[]{1,2,3,4,5})foreach(int count in new[]{0,1,9999})SmallLoadoutFrame(bag,defs,new GR(0,0,760,620),new GV(-0f,float.Epsilon),"",mode!=1,mode,count);
            var na=new Dialog_WeaponLoadout(null);var fe=new FrozenSmallUI.Dialog_WeaponLoadout(null);
            foreach(GR area in new[]{new GR(0,0,760,620),new GR(11,-3,400,200)}){SmallFrame(()=>na.DoWindowContents(area),()=>fe.DoWindowContents(area));smallLoadouts++;}
        }
        private static void SmallLoadoutFrame(CompM6RocketBag bag,ThingDef[] defs,GR area,GV scroll,string search,bool expanded,int mode,int count)
        {
            var a=new Dialog_WeaponLoadout(bag);var e=new FrozenSmallUI.Dialog_WeaponLoadout(bag);smallMode=mode;smallScrollOutput=new GV(MathF.BitIncrement(.5f),-0f);
            Set(a,"scrollPosition",new NV(scroll.x,scroll.y));Set(e,"scrollPosition",scroll);Set(a,"search",search);Set(e,"search",search);
            if(expanded)foreach(string group in defs.Select(d=>(string)Static(typeof(FrozenSmallUI.Dialog_WeaponLoadout),"CaliberGroupLabel",d)).Distinct())
            {((HashSet<string>)Get(a,"expandedGroups")).Add(group);((HashSet<string>)Get(e,"expandedGroups")).Add(group);}
            Dictionary<ThingDef,int> after=null;
            SmallFrame(()=>{a.DoWindowContents(area);after=new Dictionary<ThingDef,int>((Dictionary<ThingDef,int>)Get(bag,"desiredAmmo"));},()=>{e.DoWindowContents(area);var actual=(Dictionary<ThingDef,int>)Get(bag,"desiredAmmo");if(after.Count!=actual.Count||after.Any(p=>!actual.TryGetValue(p.Key,out int value)||value!=p.Value))throw new InvalidOperationException("Loadout desired state changed.");},()=>Set(bag,"desiredAmmo",defs.ToDictionary(d=>d,d=>count)));
            if(mode>=1&&mode<=3&&!clicked)throw new InvalidOperationException("Controlled loadout button was not reached; mode="+mode);
            if(mode!=0)smallActions++;
            SmallScrollState(a,e);
            if(!Equals(Get(a,"search"),Get(e,"search"))||!((HashSet<string>)Get(a,"expandedGroups")).SetEquals((HashSet<string>)Get(e,"expandedGroups")))throw new InvalidOperationException("Loadout grouping/search state changed.");
            var ab=(Dictionary<ThingDef,string>)Get(a,"countBuffers");var eb=(Dictionary<ThingDef,string>)Get(e,"countBuffers");if(ab.Count!=eb.Count||ab.Any(p=>!eb.TryGetValue(p.Key,out string value)||value!=p.Value))throw new InvalidOperationException("Loadout count buffer changed.");
            smallLoadouts++;
        }
        private static void SmallMechanical(ThingWithComps shaft)
        {
            var a=new ITab_MechanicalNetwork();var e=new FrozenSmallUI.ITab_MechanicalNetwork();CompMechanicalNode node=shaft.GetComp<CompMechanicalNode>();
            foreach(GV size in new[]{new GV(400,300),new GV(711.25f,512.5f)})foreach(GV scroll in new[]{GV.zero,new GV(-0f,float.Epsilon),new GV(100.5f,500)})foreach(int profile in Enumerable.Range(0,7))
            {
                AccessTools.Field(typeof(ITab_MechanicalNetwork),"size").SetValue(a,size);AccessTools.Field(typeof(FrozenSmallUI.ITab_MechanicalNetwork),"size").SetValue(e,size);
                Set(a,"scrollPosition",new NV(scroll.x,scroll.y));Set(e,"scrollPosition",scroll);smallScrollOutput=new GV(-0f,MathF.BitIncrement(.125f));
                smallSelected=profile==0?null:shaft;node.Network=profile==1?null:new MechanicalNetwork{CurrentPowerOutput=profile==2?0:profile==3?100:profile==4?75:200,CurrentMaximumPower=250,CurrentPowerNeeded=profile==2?0:100,GridRPM=profile==4?400:500,NominalGridRPM=profile==2?0:500};
                if(node.Network!=null&&profile>=5){node.Network.nodes.Add(new CompMechanicalEmitter());node.Network.nodes.Add(new CompMechanicalUser());node.Network.nodes.Add(new CompMechanicalUser());node.Network.nodes.Add(new CompMechanicalTransmitter());if(profile==6)node.Network.nodes.Add(new CompMechanicalTransmitter());}
                SmallFrame(()=>Call(a,"FillTab"),()=>Call(e,"FillTab"));SmallScrollState(a,e);smallMechanical++;
            }
        }
        private static void SmallGas(Map map)
        {
            var a=new MapComponent_GasDensityMouseover(map);var e=new FrozenSmallUI.MapComponent_GasDensityMouseover(map);
            foreach(int height in new[]{64,720,2160})foreach(int profile in Enumerable.Range(0,12))
            {
                UI.screenHeight=height;smallBlocked=false;smallCell=map.Center+new IntVec3(profile%3,0,0);smallDensity=new float[6];
                if(profile<6)smallDensity[profile]=.5f;else if(profile==6)smallDensity=Enumerable.Repeat(.02f,6).ToArray();else if(profile==7)smallDensity=Enumerable.Repeat(MathF.BitIncrement(.02f),6).ToArray();else if(profile==8)smallDensity=new[]{0,.125f,.5f,.995f,1,1.2f};else if(profile==9)smallBlocked=true;else if(profile==10)smallCell=new IntVec3(-1,0,-1);
                for(int repeat=0;repeat<2;repeat++)
                {
                    (GC,GameFont,UnityEngine.TextAnchor) after=default;
                    void StyleSeed(){color=new GC(.125f,.25f,.5f,.75f);font=GameFont.Medium;anchor=UnityEngine.TextAnchor.MiddleCenter;}
                    SmallFrame(()=>{StyleSeed();a.MapComponentOnGUI();after=(color,font,anchor);},()=>{StyleSeed();e.MapComponentOnGUI();if(after!=(color,font,anchor))throw new InvalidOperationException("Gas UI restoration changed.");});
                    if(color!=new GC(.125f,.25f,.5f,.75f)||font!=GameFont.Medium||anchor!=UnityEngine.TextAnchor.MiddleCenter)throw new InvalidOperationException("Gas label failed to restore context.");
                    foreach(string field in new[]{"cachedCell","cachedPhotoPercent","cachedSweetPercent","cachedCSPercent","cachedCNPercent","cachedHCPercent","cachedWhitePhosphorusPercent","cachedLabel"})if(!Equals(Get(a,field),Get(e,field)))throw new InvalidOperationException("Gas label cache changed: "+field);smallGas++;
                }
            }
        }
        internal static void RunSmallUi(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-small-ui");InstallUi(h,scrollPrefix:nameof(SmallScroll),numericPrefix:nameof(SmallNumeric),boolPrefix:nameof(SmallBool));
            void P(Type t,string n,string p,params Type[] args)=>h.Patch(AccessTools.Method(t,n,args),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),p));
            P(typeof(Widgets),"TextField",nameof(WeaponField),typeof(GR),typeof(string));
            P(typeof(GUI),"BeginGroup",nameof(WeaponGroup),typeof(GR));P(typeof(GUI),"EndGroup",nameof(WeaponEnd),Type.EmptyTypes);
            P(typeof(Text),"CalcSize",nameof(SmallSize),typeof(string));P(typeof(UI),"MouseCell",nameof(SmallCell),Type.EmptyTypes);
            h.Patch(AccessTools.PropertyGetter(typeof(ITab),"SelThing"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(SmallSelected)));
            h.Patch(AccessTools.PropertyGetter(typeof(Mouse),"IsInputBlockedNow"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(SmallBlocked)));
            h.Patch(AccessTools.Method(typeof(HelodGasDef).Assembly.GetType("Helodrace.HelodGasStore",true),"DensityPercentAt"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(SmallDensity)));
            Map map=Find.CurrentMap;GE original=GE.current;int height=UI.screenHeight;active=weaponActive=true;smallMode=0;var things=new List<Thing>();Pawn pawn=null;
            try
            {
                pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));things.Add(pawn);
                ThingDef bagDef=DefDatabase<ThingDef>.GetNamed("HD_Apparel_M6RocketBag");var apparel=(Apparel)ThingMaker.MakeThing(bagDef,GenStuff.DefaultStuffFor(bagDef));things.Add(apparel);pawn.apparel.Wear(apparel,false);var bag=apparel.GetComp<CompM6RocketBag>();if(bag.Wearer!=pawn)throw new InvalidOperationException("Actual Helod pawn did not equip its permitted loadout harness.");
                SmallLoadout(pawn,bag);smallMode=0;
                ThingDef shaftDef=DefDatabase<ThingDef>.AllDefsListForReading.First(d=>d.comps?.OfType<CompProperties_MechanicalTransmitter>().Any()==true);var shaft=(ThingWithComps)ThingMaker.MakeThing(shaftDef,GenStuff.DefaultStuffFor(shaftDef));things.Add(shaft);SmallMechanical(shaft);SmallGas(map);
                check(smallLoadouts==89&&smallActions==15,"native ammo loadout layout/search/group and actual count edits match frozen UI state across scroll profiles");
                check(smallMechanical==42,"native mechanical tab layout, no-selection/disconnected/overload/surplus and node counts match frozen submissions");
                check(smallGas==72,"native gas corner placement, threshold/cache and blocked/outside gates match frozen UI and restore font/anchor/color");
                check(typeof(Dialog_WeaponLoadout).GetField("scrollPosition",All).FieldType==typeof(NV)&&typeof(ITab_MechanicalNetwork).GetField("scrollPosition",All).FieldType==typeof(NV),"small UI stores native scroll state and transfers host by-ref values exactly");
            }
            finally {active=weaponActive=false;trace=null;GE.current=original;UI.screenHeight=height;smallSelected=null;h.UnpatchAll(h.Id);foreach(Thing thing in things)if(!thing.Destroyed)thing.Destroy();}
            File.WriteAllText(Path.Combine(output,"native-small-ui.json"),JsonSerializer.Serialize(new{Frames=frames,Loadouts=smallLoadouts,Actions=smallActions,Mechanical=smallMechanical,Gas=smallGas,Submissions=widgets,ScrollBitPairs=smallBits,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="57218ad8",ControlledWidgetsMeasurementsSelectionGasAndScreenHeight=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
