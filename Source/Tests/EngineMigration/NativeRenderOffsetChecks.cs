using System;
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
using GV = UnityEngine.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeRenderOffsetChecks
    {
        private static readonly HashSet<PawnRenderNode> nodes=new();
        private static readonly HashSet<Thing> charges=new();
        private static GV baseOffset,pivot,basePosition;
        private static DevelopmentalStage stage;
        private static Pawn pawn;
        private static bool controlled;
        private static HelodRaceSettingsDef settings;
        private static int appendages,armor,chargeCases,components,pivotBits,baseCalls,positionCalls,authoredBits,settingsCalls;
        private static float maximumError;
        private static int selectorCases, cropCases, bridgeBits;
        private static readonly Dictionary<string,float> differences=new();
        private static readonly GV[] offsets={GV.zero,new GV(-0f,float.Epsilon,-0f),new GV(.13f,-2,.37f),new GV(-4000,50,7000)};
        private static T Bare<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        private static void Set(object o,string field,object value)=>AccessTools.Field(o.GetType(),field).SetValue(o,value);
        private static bool Base(PawnRenderNode __0,ref GV __2,ref GV __result)
        {if(!controlled||!nodes.Contains(__0))return true;baseCalls++;__2=pivot;__result=baseOffset;return false;}
        private static bool Position(Thing __instance,ref GV __result)
        {if(!controlled||!charges.Contains(__instance))return true;positionCalls++;__result=basePosition;return false;}
        private static bool Stage(Pawn __instance,ref DevelopmentalStage __result)
        {if(!controlled||__instance!=pawn)return true;__result=stage;return false;}
        private static bool Settings(ref HelodRaceSettingsDef __result)
        {if(!controlled)return true;settingsCalls++;__result=settings;return false;}
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static void Bits(GV a,GV e,bool authored=false)
        {foreach(var pair in new[]{(a.x,e.x),(a.y,e.y),(a.z,e.z)}){if(BitConverter.SingleToInt32Bits(pair.Item1)!=BitConverter.SingleToInt32Bits(pair.Item2))throw new InvalidOperationException("Host pivot/authored base bits changed.");if(authored)authoredBits++;else pivotBits++;}}
        private static void Pair(PawnRenderNodeWorker a,PawnRenderNodeWorker e,PawnRenderNode node,PawnDrawParms parms,string scope,bool authored=false)
        {
            int calls=baseCalls,reads=settingsCalls;GV av=a.OffsetFor(node,parms,out GV ap),ev=e.OffsetFor(node,parms,out GV ep);
            if(baseCalls!=calls+2)throw new InvalidOperationException("Base OffsetFor override did not bind to both copies.");Vector(av,ev,scope);Bits(ap,pivot);Bits(ep,pivot);if(authored){Bits(av,baseOffset,true);Bits(ev,baseOffset,true);}
            if(scope=="appendage offset")
            {int expected=settings?.appendageOffsets?.Any(x=>x!=null&&x.appendage==((PawnRenderNodeProperties_HelodAppendage)node.Props).appendage)==true?4:2;if(settingsCalls-reads!=expected)throw new InvalidOperationException("Controlled appendage settings did not bind to both copies.");}
        }
        private static HelodAppendageOffset Entry(HelodAppendage kind,int index)
        {
            var e=new HelodAppendageOffset{appendage=kind};int i=1;
            foreach(FieldInfo f in typeof(HelodAppendageOffset).GetFields().Where(f=>f.FieldType==typeof(GV)))
            {f.SetValue(e,new GV((index+1)*i*.013f,(index-1)*i*.007f,-(index+2)*i*.021f));i++;}return e;
        }
        private static void Appendages()
        {
            var a=new PawnRenderNodeWorker_HelodAppendage();var e=new FrozenRenderOffsets.PawnRenderNodeWorker_HelodAppendage();
            var entries=new[]{Entry(HelodAppendage.Tail,0),Entry(HelodAppendage.LeftEar,1),Entry(HelodAppendage.RightEar,2)};
            var node=Bare<PawnRenderNode_HelodAppendage>();var props=new PawnRenderNodeProperties_HelodAppendage();Set(node,"props",props);nodes.Add(node);
            foreach(int profile in new[]{0,1,2,3,4})
            {
                settings=profile==4?null:new HelodRaceSettingsDef();
                if(settings!=null)settings.appendageOffsets=profile switch{0=>entries.ToList(),1=>new List<HelodAppendageOffset>(),2=>new List<HelodAppendageOffset>{entries[1]},_=>new List<HelodAppendageOffset>{null,entries[2],entries[0],Entry(HelodAppendage.Tail,7)}};
                foreach(HelodAppendage kind in Enum.GetValues<HelodAppendage>())foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})foreach(bool flip in new[]{false,true})foreach(DevelopmentalStage age in new[]{DevelopmentalStage.Baby,DevelopmentalStage.Child,DevelopmentalStage.Adult})foreach(GV value in offsets)
                {
                    stage=age;baseOffset=value;pivot=new GV(-0f,float.Epsilon,-4);props.appendage=kind;
                    if(pawn.DevelopmentalStage!=age)throw new InvalidOperationException("Controlled developmental stage did not bind.");
                    Pair(a,e,node,new PawnDrawParms{pawn=pawn,facing=facing,flipHead=flip},"appendage offset");appendages++;
                }
            }
            nodes.Remove(node);
        }
        private static ModularArmorDrawOffsets Directional(float factor)=>new(){north=new GV(.1f*factor,.2f*factor,-.3f*factor),east=new GV(-.3f*factor,.7f*factor,.5f*factor),south=new GV(.7f*factor,-.1f*factor,.9f*factor),west=new GV(-.5f*factor,.8f*factor,-.2f*factor)};
        private static GraphicData Graphic(float factor)=>new(){drawOffset=new GV(.2f*factor,.03f*factor,-.1f*factor),drawOffsetNorth=new GV(-.4f*factor,0,.6f*factor),drawOffsetWest=new GV(.5f*factor,-.7f*factor,.2f*factor)};
        private static void Armor()
        {
            var a=new PawnRenderNodeWorker_ModularArmorPart();var e=new FrozenRenderOffsets.ArmorWorker();var node=Bare<PawnRenderNode_ModularArmorPart>();nodes.Add(node);
            foreach(int profile in new[]{0,1,2})foreach(int mask in Enumerable.Range(0,8))foreach(bool pals in new[]{false,true})foreach(bool authored in new[]{false,true})foreach(bool north in new[]{false,true})foreach(int grid in new[]{-1,0,4})
            {
                var pos=new ModularArmorPositionDef{drawOffsets=(mask&1)!=0?Directional(.5f):null};
                var panel=new ModularArmorPalsPanelDef{armorPosition=pos,columns=profile==0?6:1,rows=profile==2?0:4,drawCellSize=new UnityEngine.Vector2(profile==2?-.2f:.08f,profile==1?0:.11f),drawOrigin=(mask&4)!=0?Directional(-1):null};
                var part=new ModularArmorPartDef{installMode=pals?ModularArmorInstallMode.Positionable:ModularArmorInstallMode.Fixed,palsWidth=profile==0?2:4,palsHeight=profile==2?-1:2,fillNarrowPalsPanel=profile!=0,drawOffsets=(mask&2)!=0?Directional(2):null,graphicData=Graphic(1),northUnderGraphicData=Graphic(-2)};
                var record=new InstalledModularArmorPart{part=part,position=pos,palsPanel=panel,palsX=grid,palsY=grid+1};Set(node,"Installed",record);Set(node,"NorthUnderlay",north);Set(node,"AuthoredPalsGraphic",authored?BaseContent.BadGraphic:null);
                foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})foreach(GV value in offsets)
                {baseOffset=value;pivot=new GV(float.Epsilon,-0f,3);Pair(a,e,node,new PawnDrawParms{pawn=pawn,facing=facing},"armor offset",authored);armor++;}
            }
            nodes.Remove(node);
        }
        private static void Charges()
        {
            ThingDef def=DefDatabase<ThingDef>.GetNamed("HD_InstalledBreachCharge");var a=new Thing_InstalledBreachCharge{def=def};var e=new FrozenRenderOffsets.Thing_InstalledBreachCharge{def=def};charges.Add(a);charges.Add(e);
            foreach(GV value in offsets.Concat(new[]{new GV(.5f,9,.5f),new GV(-.5f,-1,-.5f)}))foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
            {basePosition=value;a.Rotation=e.Rotation=facing;int calls=positionCalls;Vector(a.DrawPos,e.DrawPos,"installed charge");if(positionCalls!=calls+2)throw new InvalidOperationException("Charge base position did not bind to both copies.");chargeCases++;}
            charges.Clear();
        }
        private static void AuthoredBoundaries()
        {
            void Exact(float actual, float expected)
            {
                if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected))
                    throw new InvalidOperationException("Authored native boundary altered component bits.");
                bridgeBits++;
            }
            float[] values = { 0f, -0f, float.Epsilon, -.25f, 23.5f,
                float.PositiveInfinity, BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345)) };
            foreach (float value in values)
            {
                var authored = new ModularArmorDrawOffsets {
                    north = new GV(value, -.5f, 1), east = new GV(2, value, 3),
                    south = new GV(4, 5, value), west = new GV(value, value, value) };
                foreach (Rot4 facing in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West, Rot4.Invalid })
                {
                    var actual = authored.For(facing);
                    GV expected = facing == Rot4.North ? authored.north : facing == Rot4.East ? authored.east
                        : facing == Rot4.West ? authored.west : authored.south;
                    Exact(actual.X, expected.x); Exact(actual.Y, expected.y); Exact(actual.Z, expected.z); selectorCases++;
                }
            }
            foreach (float x in values) foreach (float y in values)
                foreach (float width in values) foreach (float height in values)
                {
                    var actual = new ModularArmorPreviewCrop { x=x, y=y, width=width, height=height }.Rect;
                    var expected = new UnityEngine.Rect(x, y, width, height);
                    Exact(actual.x, expected.x); Exact(actual.y, expected.y);
                    Exact(actual.width, expected.width); Exact(actual.height, expected.height); cropCases++;
                }
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-render-offsets");
            h.Patch(AccessTools.Method(typeof(PawnRenderNodeWorker),"OffsetFor"),prefix:new HarmonyMethod(typeof(NativeRenderOffsetChecks),nameof(Base)));
            h.Patch(AccessTools.PropertyGetter(typeof(Thing),"DrawPos"),prefix:new HarmonyMethod(typeof(NativeRenderOffsetChecks),nameof(Position)));
            h.Patch(AccessTools.PropertyGetter(typeof(Pawn),"DevelopmentalStage"),prefix:new HarmonyMethod(typeof(NativeRenderOffsetChecks),nameof(Stage)));
            h.Patch(AccessTools.PropertyGetter(typeof(HelodRace),"Settings"),prefix:new HarmonyMethod(typeof(NativeRenderOffsetChecks),nameof(Settings)));
            try
            {
                AuthoredBoundaries();
                check(selectorCases == 35 && cropCases == 2401 && bridgeBits == 9709,
                    "native armor selectors/crop preserve authored component bits, negative extents, invalid-facing fallback and nonfinite inputs");
                check(typeof(ModularArmorDrawOffsets).GetMethod("For").ReturnType == typeof(System.Numerics.Vector3)
                    && typeof(ModularArmorPreviewCrop).GetProperty("Rect").PropertyType == typeof(Waylith.Math.Rect),
                    "owned armor selectors/crop return native values without game overloads");
                pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));controlled=true;
                Appendages();check(appendages==1440,"native appendage offsets preserve growth stage, missing settings, directional and flipped-head/anatomical ear rules");
                Armor();check(armor==9216,"native armor offsets preserve directional optional offsets, PALS grids, narrow panel widths and authored/north-underlay branches");
                Charges();check(chargeCases==24,"native installed-charge DrawPos preserves cardinal half-cell displacement");
                check(pivotBits==(appendages+armor)*6&&authoredBits==27648,"host pivots and authored-PALS early-return base coordinates preserve exact component bits");
                var method=typeof(PawnRenderNodeWorker_HelodAppendage).GetMethod("TryGetConfiguredOffset",BindingFlags.NonPublic|BindingFlags.Static);
                check(method.GetParameters()[2].ParameterType==typeof(System.Numerics.Vector3).MakeByRefType(),"owned appendage helper output uses native vectors with no game overload");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-render-offsets.json"),JsonSerializer.Serialize(new{AppendageCases=appendages,ArmorCases=armor,ChargeCases=chargeCases,SelectorCases=selectorCases,CropCases=cropCases,AuthoredBoundaryBits=bridgeBits,BaseOffsetCalls=baseCalls,ChargePositionCalls=positionCalls,SettingsReads=settingsCalls,PivotComponentBits=pivotBits,AuthoredBaseComponentBits=authoredBits,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="a4f805c4",AuthoredSelectorBaseline="d822f681",ControlledBaseOffsetPivotAgeAndSettings=true,SyntheticUnregisteredRenderNodesAndArmorDefinitions=true},new JsonSerializerOptions{WriteIndented=true}));
                controlled=false;nodes.Clear();charges.Clear();settings=null;h.UnpatchAll(h.Id);if(pawn!=null&&!pawn.Destroyed)pawn.Destroy();pawn=null;
            }
        }
    }
}
