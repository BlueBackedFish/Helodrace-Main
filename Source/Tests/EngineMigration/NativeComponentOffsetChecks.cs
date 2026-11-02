using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using GV=UnityEngine.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeComponentOffsetChecks
    {
        private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static Pawn helod,human;
        private static ThingWithComps parent;
        private static HelodRaceSettingsDef settings;
        private static float age;
        private static LifeStageDef life;
        private static DevelopmentalStage stage;
        private static bool controlled;
        private static GV position;
        private static List<FleckCreationData> flecks;
        private sealed record Request(string Kind,GV Position,Map Map,float Scale);
        private static List<Request> requests;
        private static int headCases,bedCases,hairCases,utilityCases,heightCases,canisterPairs,bitComponents,finiteComponents,ageReads,lifeReads,settingsReads,positionReads,fleckCount,requestCount;
        private static readonly GV[] inputs={GV.zero,new GV(-0f,float.Epsilon,-0f),new GV(.13f,-2,.37f),new GV(1000,4,-9000),new GV(BitConverter.Int32BitsToSingle(0x7fc01234),-0f,.5f),new GV(float.PositiveInfinity,float.NegativeInfinity,-.5f),new GV(1,BitConverter.Int32BitsToSingle(unchecked((int)0xffc05678)),-2),new GV(1,2,float.Epsilon)};
        private static T Bare<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        private static object Call(object o,string method,params object[] args){Type t=o as Type??o.GetType();return t.GetMethod(method,All).Invoke(o is Type?null:o,args);}
        private static bool Age(Pawn_AgeTracker __instance,ref float __result){if(!controlled||__instance!=helod.ageTracker)return true;ageReads++;__result=age;return false;}
        private static bool Life(Pawn_AgeTracker __instance,ref LifeStageDef __result){if(!controlled||__instance!=helod.ageTracker)return true;lifeReads++;__result=life;return false;}
        private static bool Stage(Pawn __instance,ref DevelopmentalStage __result){if(!controlled||(__instance!=helod&&__instance!=human))return true;__result=stage;return false;}
        private static bool Settings(ref HelodRaceSettingsDef __result){if(!controlled)return true;settingsReads++;__result=settings;return false;}
        private static bool Position(Thing __instance,ref GV __result){if(flecks==null||__instance!=parent)return true;positionReads++;__result=position;return false;}
        private static bool Fleck(FleckCreationData __0){if(flecks==null)return true;flecks.Add(__0);return false;}
        private static bool Cull(ref bool __result){if(flecks==null)return true;__result=true;return false;}
        private static void Glow(GV __0,Map __1,float __2)=>requests?.Add(new("glow",__0,__1,__2));
        private static void Smoke(GV __0,Map __1,float __2)=>requests?.Add(new("smoke",__0,__1,__2));
        private static void Sparks(GV __0,Map __1)=>requests?.Add(new("sparks",__0,__1,0));
        private static void Bits(float a,float e){bitComponents++;if(BitConverter.SingleToInt32Bits(a)!=BitConverter.SingleToInt32Bits(e))throw new InvalidOperationException("Component bits changed: "+a+" != "+e);if(float.IsFinite(a)&&float.IsFinite(e))finiteComponents++;}
        private static void Vector(GV a,GV e){Bits(a.x,e.x);Bits(a.y,e.y);Bits(a.z,e.z);}
        private static void RefPair(Type actual,Type frozen,string method,params object[] prefix)
        {
            foreach(GV input in inputs)
            {
                object[] aa=prefix.Concat(new object[]{input}).ToArray(),ee=prefix.Concat(new object[]{input}).ToArray();Call(actual,method,aa);Call(frozen,method,ee);Vector((GV)aa[^1],(GV)ee[^1]);
            }
        }
        private static void Heads(HelodRaceSettingsDef original)
        {
            Gender hg=helod.gender,ug=human.gender;
            try
            {
                foreach(int profile in new[]{0,1,2,3,4})
                {
                    settings=profile==4?null:new HelodRaceSettingsDef();
                    if(settings!=null)settings.headOffsets=profile switch{0=>original.headOffsets,1=>new List<HelodHeadOffset>(),2=>null,_=>new List<HelodHeadOffset>{new(){minAge=3,female=.2f,male=-.3f},null,new(){minAge=0,female=-.1f,male=.15f,absolute=true},new(){minAge=3,female=-.7f,male=.9f,absolute=true},new(){minAge=13,female=.8f,male=-.6f}}};
                    foreach(Pawn pawn in new[]{helod,human,null})foreach(Gender gender in new[]{Gender.Male,Gender.Female})foreach(float years in new[]{-1f,0,1,MathF.BitDecrement(3),3,MathF.BitIncrement(3),13,18,1000})foreach(float factor in new[]{0f,.25f,1,4})
                    {
                        if(pawn!=null)pawn.gender=gender;age=years;life=new LifeStageDef{bodySizeFactor=factor};int reads=ageReads;
                        RefPair(typeof(Patch_HelodHeadOffset),typeof(FrozenComponentOffsets.Patch_HelodHeadOffset),"Postfix",pawn);headCases+=inputs.Length;
                        if(ageReads-reads!=(pawn==helod?inputs.Length*2:0))throw new InvalidOperationException("Controlled head age did not bind to both copies.");
                    }
                }
            }
            finally{helod.gender=hg;human.gender=ug;}
            foreach(Pawn pawn in new[]{helod,human,null})foreach(DevelopmentalStage ageStage in new[]{DevelopmentalStage.Baby,DevelopmentalStage.Child,DevelopmentalStage.Adult})foreach(PawnPosture posture in Enum.GetValues<PawnPosture>())
            {stage=ageStage;RefPair(typeof(Patch_HelodBabyBedOffset),typeof(FrozenComponentOffsets.Patch_HelodBabyBedOffset),"Postfix",pawn,posture);bedCases+=inputs.Length;}
        }
        private static void HairAndUtility(List<Thing> fixture)
        {
            var hair=Bare<PawnRenderNode_Hair>();var other=Bare<PawnRenderNode>();HairDef hh=helod.story.hairDef,uh=human.story.hairDef;
            try
            {
                foreach(Pawn pawn in new[]{helod,human,null})foreach(string name in new[]{"PPHF_NativeFixture","PPHF","pphf_lowercase","Other",null})foreach(PawnRenderNode node in new PawnRenderNode[]{hair,other,null})foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
                {if(pawn!=null)pawn.story.hairDef=name==null?null:new HairDef{defName=name};RefPair(typeof(PatchPonpecoHairOffset),typeof(FrozenComponentOffsets.PatchPonpecoHairOffset),"Postfix",node,new PawnDrawParms{pawn=pawn,facing=facing});hairCases+=inputs.Length;}
            }
            finally{helod.story.hairDef=hh;human.story.hairDef=uh;}
            Type layer=typeof(PatchHelodUnspecifiedUtilityOffset).Assembly.GetType("Helodrace.PatchHelodShellApparelLayer",true);MethodInfo predicate=layer.GetMethod("IsUnspecifiedVanillaUtility",All);
            ThingDef def=DefDatabase<ThingDef>.AllDefs.First(d=>(bool)predicate.Invoke(null,new object[]{d}));var allowed=(Apparel)ThingMaker.MakeThing(def,GenStuff.DefaultStuffFor(def));fixture.Add(allowed);ThingDef pants=DefDatabase<ThingDef>.GetNamed("Apparel_Pants");var rejected=(Apparel)ThingMaker.MakeThing(pants,GenStuff.DefaultStuffFor(pants));fixture.Add(rejected);
            var yes=Bare<PawnRenderNode_Apparel>();var no=Bare<PawnRenderNode_Apparel>();AccessTools.Field(typeof(PawnRenderNode),"apparel").SetValue(yes,allowed);AccessTools.Field(typeof(PawnRenderNode),"apparel").SetValue(no,rejected);
            if((bool)predicate.Invoke(null,new object[]{pants}))throw new InvalidOperationException("Utility rejection fixture must not be in cached utility set.");
            foreach(Pawn pawn in new[]{helod,human,null})foreach(PawnRenderNode node in new PawnRenderNode[]{yes,no,null})foreach(Rot4 facing in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West})
            {RefPair(typeof(PatchHelodUnspecifiedUtilityOffset),typeof(FrozenComponentOffsets.PatchHelodUnspecifiedUtilityOffset),"Postfix",node,new PawnDrawParms{pawn=pawn,facing=facing});utilityCases+=inputs.Length;}
        }
        private static void Heights(List<Thing> fixture)
        {
            Type actual=typeof(Helodrace.ModernWar.CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.Patch_DrawEquipmentAiming_HelodraceHeight",true);
            foreach(string name in new[]{"HD_Gun_M16A4_Weapon","Gun_Autopistol","MeleeWeapon_Knife","Steel",null})
            {
                Thing thing=null;if(name!=null){ThingDef def=DefDatabase<ThingDef>.GetNamed(name);thing=ThingMaker.MakeThing(def,GenStuff.DefaultStuffFor(def));fixture.Add(thing);}
                RefPair(actual,typeof(FrozenComponentOffsets.Patch_DrawEquipmentAiming_HelodraceHeight),"Prefix",thing);heightCases+=inputs.Length;
            }
        }
        private sealed record Frame(FleckCreationData[] Flecks,Request[] Requests,int Next,int Bits);
        private static Frame Record(object comp,string method,int seed)
        {
            flecks=new();requests=new();Rand.PushState(seed);
            try{Call(comp,method);return new(flecks.ToArray(),requests.ToArray(),Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}
            finally{Rand.PopState();flecks=null;requests=null;}
        }
        private static void Canisters(Map map)
        {
            foreach((ThingComp actual,ThingComp frozen) in new (ThingComp,ThingComp)[]{(new CompPhotochlorogenCan(),new FrozenComponentOffsets.Photo()),(new CompSweetGasCan(),new FrozenComponentOffsets.Sweet())})
            {
                actual.parent=frozen.parent=parent;
                foreach(string method in new[]{"ThrowIgnitionEffect","ThrowBurningEffect"})foreach(GV origin in new[]{map.Center.ToVector3Shifted(),new GV(.01f,3,.01f),new GV(map.Size.x+.1f,-0f,map.Size.z+.1f),new GV(-.1f,-2,-.1f)})foreach(int seed in new[]{1,3701})
                {
                    position=origin;int reads=positionReads;var a=Record(actual,method,seed);var e=Record(frozen,method,seed);if(positionReads!=reads+2)throw new InvalidOperationException("Controlled canister position did not bind to both copies.");
                    if(a.Flecks.Length!=e.Flecks.Length||a.Requests.Length!=e.Requests.Length||a.Next!=e.Next||a.Bits!=e.Bits)throw new InvalidOperationException("Canister requests/RNG changed.");
                    for(int i=0;i<a.Requests.Length;i++){Vector(a.Requests[i].Position,e.Requests[i].Position);Bits(a.Requests[i].Scale,e.Requests[i].Scale);if(a.Requests[i] with{Position=e.Requests[i].Position,Scale=e.Requests[i].Scale}!=e.Requests[i])throw new InvalidOperationException("Canister helper identity changed.");}
                    for(int i=0;i<a.Flecks.Length;i++)foreach(FieldInfo f in typeof(FleckCreationData).GetFields(BindingFlags.Public|BindingFlags.Instance))
                    {object av=f.GetValue(a.Flecks[i]),ev=f.GetValue(e.Flecks[i]);if(av is float af&&ev is float ef)Bits(af,ef);else if(av is GV v&&ev is GV v2)Vector(v,v2);else if(!Equals(av,ev))throw new InvalidOperationException("Canister FleckData changed: "+f.Name);}
                    canisterPairs++;fleckCount+=a.Flecks.Length;requestCount+=a.Requests.Length;
                }
            }
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-component-offsets");var fixture=new List<Thing>();HelodRaceSettingsDef original=HelodRace.Settings;
            void Patch(MethodInfo method,string prefix){if(method==null)throw new MissingMethodException("Fixture target for "+prefix);h.Patch(method,prefix:new HarmonyMethod(typeof(NativeComponentOffsetChecks),prefix));}
            Patch(AccessTools.PropertyGetter(typeof(Pawn_AgeTracker),"AgeBiologicalYearsFloat"),nameof(Age));Patch(AccessTools.PropertyGetter(typeof(Pawn_AgeTracker),"CurLifeStage"),nameof(Life));Patch(AccessTools.PropertyGetter(typeof(Pawn),"DevelopmentalStage"),nameof(Stage));Patch(AccessTools.PropertyGetter(typeof(HelodRace),"Settings"),nameof(Settings));Patch(AccessTools.PropertyGetter(typeof(Thing),"DrawPos"),nameof(Position));
            Patch(AccessTools.Method(typeof(FleckManager),"CreateFleck",new[]{typeof(FleckCreationData)}),nameof(Fleck));foreach(Type pos in new[]{typeof(GV),typeof(IntVec3)})Patch(AccessTools.Method(typeof(GenView),"ShouldSpawnMotesAt",new[]{pos,typeof(Map),typeof(bool)}),nameof(Cull));
            Patch(AccessTools.Method(typeof(FleckMaker),"ThrowFireGlow",new[]{typeof(GV),typeof(Map),typeof(float)}),nameof(Glow));Patch(AccessTools.Method(typeof(FleckMaker),"ThrowSmoke",new[]{typeof(GV),typeof(Map),typeof(float)}),nameof(Smoke));Patch(AccessTools.Method(typeof(FleckMaker),"ThrowMicroSparks",new[]{typeof(GV),typeof(Map)}),nameof(Sparks));
            try
            {
                Map map=Find.CurrentMap;helod=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));human=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,canGeneratePawnRelations:false));fixture.Add(helod);fixture.Add(human);if(!HelodRace.IsHelod(helod)||human.def!=ThingDefOf.Human)throw new InvalidOperationException("Actual race fixtures failed.");controlled=true;
                Heads(original);check(headCases==8640&&bedCases==3*3*Enum.GetValues<PawnPosture>().Length*inputs.Length&&lifeReads>0&&settingsReads>0,"native head/baby-bed offsets preserve settings ordering/ties, age/gender/body size, missing data and posture/race guards with exact component bits");
                HairAndUtility(fixture);check(hairCases==1440&&utilityCases==288,"native hair/utility adjustments preserve cached apparel membership, case-sensitive hair/race/facing guards and untouched component bits");
                Heights(fixture);check(heightCases==40,"native weapon-height adjustment preserves own/vanilla/ranged/melee/null gates and exact component bits");
                controlled=false;parent=(ThingWithComps)ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);fixture.Add(parent);GenSpawn.Spawn(parent,map.Center,map);Canisters(map);check(canisterPairs==32&&fleckCount>0&&requestCount==80,"native canister ignition/burning coordinates preserve actual FleckMaker data, helper payloads and every seeded RNG continuation");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-component-offsets.json"),JsonSerializer.Serialize(new{HeadCases=headCases,BedCases=bedCases,HairCases=hairCases,UtilityCases=utilityCases,HeightCases=heightCases,CanisterPairs=canisterPairs,BitComponents=bitComponents,FiniteComponents=finiteComponents,AgeReads=ageReads,LifeStageReads=lifeReads,SettingsReads=settingsReads,CanisterPositionReads=positionReads,Flecks=fleckCount,HelperRequests=requestCount,Baseline="c63db7e1",ControlledHeadAgeStageSettingsAndCanisterVisibility=true,SyntheticRenderNodesAndUnregisteredHairDefs=true,SuppressedParticleCreation=true},new JsonSerializerOptions{WriteIndented=true}));
                controlled=false;h.UnpatchAll(h.Id);foreach(Thing thing in fixture.AsEnumerable().Reverse())if(!thing.Destroyed)thing.Destroy();helod=human=null;parent=null;settings=null;
            }
        }
    }
}
