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
using GC=UnityEngine.Color;
using G32=UnityEngine.Color32;
using GV=UnityEngine.Vector3;
using NC=Waylith.Math.Color;
using N32=Waylith.Math.Color32;

namespace Helodrace.Tests
{
    internal static class NativeColorChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static bool capture;
        private static int mode,greyCalls,greyTrue,randomGreyCalls;
        private static int palettePairs,guardPairs,naturalGreyPairs,forcedGreyPairs,gasPairs,byteToFloatCases,floatToByteCases,floatBits,byteComponents,vertexPairs,triangles,gasRngPairs;
        private static readonly HashSet<uint> palette=new();
        private static T Bare<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        private static T Copy<T>(T value)=>(T)typeof(object).GetMethod("MemberwiseClone",All).Invoke(value,null);
        private static bool Grey(ref bool __result){if(!capture||mode<0)return true;__result=mode==1;return false;}
        private static void GreyResult(bool __result){if(!capture)return;greyCalls++;if(__result)greyTrue++;}
        private static void RandomGrey(){if(capture)randomGreyCalls++;}
        private static void Bits(float a,float e){floatBits++;if(BitConverter.SingleToInt32Bits(a)!=BitConverter.SingleToInt32Bits(e))throw new InvalidOperationException("Color/vertex float bits changed: "+a+" != "+e);}
        private static void Color(GC a,GC e){Bits(a.r,e.r);Bits(a.g,e.g);Bits(a.b,e.b);Bits(a.a,e.a);}
        private static void Bytes(G32 a,G32 e){byteComponents+=4;if(a.r!=e.r||a.g!=e.g||a.b!=e.b||a.a!=e.a)throw new InvalidOperationException("RGBA byte conversion changed.");}
        private static uint Pack(G32 c)=>((uint)c.r<<24)|((uint)c.g<<16)|((uint)c.b<<8)|c.a;
        private sealed record HairFrame(bool RunOriginal,GC Color,int GreyCalls,int GreyTrue,int RandomGrey,int Next,int Bits);
        private static HairFrame Hair(Pawn pawn,int age,int seed,bool frozen)
        {
            greyCalls=greyTrue=randomGreyCalls=0;capture=true;Rand.PushState(seed);
            try{GC color=new(.13f,.27f,.39f,.53f);bool result=frozen?FrozenColors.Patch_HelodHairPalette.Prefix(pawn,age,ref color):Patch_HelodHairPalette.Prefix(pawn,age,ref color);return new(result,color,greyCalls,greyTrue,randomGreyCalls,Rand.Int,BitConverter.SingleToInt32Bits(Rand.Value));}
            finally{Rand.PopState();capture=false;}
        }
        private static void Hair(Pawn helod,Pawn human)
        {
            foreach(int policy in new[]{-1,0,1})foreach(Pawn pawn in new[]{helod,human,null})foreach(int age in new[]{0,20,40,60,100,1000})for(int seed=0;seed<512;seed++)
            {
                mode=policy;var a=Hair(pawn,age,seed,false);var e=Hair(pawn,age,seed,true);Color(a.Color,e.Color);
                if(a.RunOriginal!=e.RunOriginal||a.GreyCalls!=e.GreyCalls||a.GreyTrue!=e.GreyTrue||a.RandomGrey!=e.RandomGrey||a.Next!=e.Next||a.Bits!=e.Bits)throw new InvalidOperationException("Hair palette branch/delegation/RNG changed.");
                if(pawn!=helod)
                {
                    if(!a.RunOriginal||a.GreyCalls!=0||a.RandomGrey!=0)throw new InvalidOperationException("Non-Helod/null hair prefix guard changed.");Color(a.Color,new GC(.13f,.27f,.39f,.53f));Rand.PushState(seed);try{if(a.Next!=Rand.Int||a.Bits!=BitConverter.SingleToInt32Bits(Rand.Value))throw new InvalidOperationException("Hair guard consumed RNG.");}finally{Rand.PopState();}guardPairs++;
                }
                else
                {
                    if(a.RunOriginal||a.GreyCalls!=1||a.RandomGrey!=a.GreyTrue)throw new InvalidOperationException("Hair grey delegation expectation failed.");
                    if(a.RandomGrey==0)palette.Add(Pack((G32)a.Color));else if(policy<0)naturalGreyPairs++;else forcedGreyPairs++;
                }
                palettePairs++;
            }
        }
        private static void Conversions()
        {
            for(int i=0;i<256;i++)
            {
                var native=new N32((byte)i,(byte)(i*71),(byte)(255-i),(byte)(i^0x5a));var game=new G32(native.r,native.g,native.b,native.a);NC n=native;GC g=game;Color(new GC(n.r,n.g,n.b,n.a),g);byteToFloatCases++;
            }
            var values=new List<float>{-100f,0,1,100,float.NaN,float.NegativeInfinity,float.PositiveInfinity};
            for(int i=0;i<256;i++){float edge=(i+.5f)/255f;values.Add(MathF.BitDecrement(edge));values.Add(edge);values.Add(MathF.BitIncrement(edge));}
            foreach(float value in values){GC g=new(value,1-value,value*.5f,value);N32 n=new NC(g.r,g.g,g.b,g.a);G32 e=g;Bytes(new G32(n.r,n.g,n.b,n.a),e);floatToByteCases++;}
        }
        private static LayerSubMesh Buffer()
        {
            var mesh=Bare<LayerSubMesh>();mesh.verts=new();mesh.uvs=new();mesh.colors=new();mesh.tris=new();return mesh;
        }
        private static void Clear(LayerSubMesh mesh,int prefix)
        {
            mesh.verts.Clear();mesh.uvs.Clear();mesh.colors.Clear();mesh.tris.Clear();
            for(int i=0;i<prefix;i++){mesh.verts.Add(new GV(i,i+1,i+2));mesh.uvs.Add(new GV(i,0,1));mesh.colors.Add(new G32((byte)i,3,4,5));}
            if(prefix>0)mesh.tris.AddRange(new[]{0,1,2});
        }
        private static void Gas(Map map)
        {
            var section=Bare<Section>();AccessTools.Field(typeof(Section),"map").SetValue(section,map);var actual=new SectionLayer_HelodGas(section);
            if(!ReferenceEquals(AccessTools.PropertyGetter(typeof(SectionLayer),"Map").Invoke(actual,null),map))throw new InvalidOperationException("Actual gas-layer map boundary did not bind.");
            var frozen=new FrozenColors.GasCell(map);var am=Buffer();var em=Buffer();MethodInfo aa=typeof(SectionLayer_HelodGas).GetMethod("AddCell",All),ee=typeof(FrozenColors.GasCell).GetMethod("AddCell",All);
            var profiles=DefDatabase<HelodGasDef>.AllDefs.Select(Copy).ToList();HelodGasDef template=profiles[0];
            foreach(float alpha in new[]{-1f,0,.16f,.35f,.8f,1,1.5f,MathF.BitDecrement(127.5f/255f),127.5f/255f,MathF.BitIncrement(127.5f/255f)})foreach(ushort index in new ushort[]{0,1,ushort.MaxValue}){var gas=Copy(template);gas.index=index;gas.color=new GC(.13f,.27f,.39f,alpha);profiles.Add(gas);}
            foreach(HelodGasDef gas in profiles)foreach(IntVec3 cell in new[]{IntVec3.Zero,map.Center,new IntVec3(map.Size.x-1,0,map.Size.z-1)})foreach(float altitude in new[]{-0f,3.25f})foreach(int prefix in new[]{0,3})for(int density=0;density<256;density++)
            {
                Clear(am,prefix);Clear(em,prefix);object[] av={cell,gas,(byte)density,am,altitude},ev={cell,gas,(byte)density,em,altitude};
                Rand.PushState(3701);int an,ab;try{aa.Invoke(actual,av);an=Rand.Int;ab=BitConverter.SingleToInt32Bits(Rand.Value);}finally{Rand.PopState();}
                Rand.PushState(3701);try{ee.Invoke(frozen,ev);if(an!=Rand.Int||ab!=BitConverter.SingleToInt32Bits(Rand.Value))throw new InvalidOperationException("Gas cell builder RNG continuation changed.");}finally{Rand.PopState();}gasRngPairs++;
                if(am.verts.Count!=em.verts.Count||am.uvs.Count!=em.uvs.Count||am.colors.Count!=em.colors.Count||!am.tris.SequenceEqual(em.tris))throw new InvalidOperationException("Gas quad list/order changed.");
                for(int i=0;i<am.verts.Count;i++){GV v=am.verts[i],v2=em.verts[i],uv=am.uvs[i],uv2=em.uvs[i];Bits(v.x,v2.x);Bits(v.y,v2.y);Bits(v.z,v2.z);Bits(uv.x,uv2.x);Bits(uv.y,uv2.y);Bits(uv.z,uv2.z);Bytes(am.colors[i],em.colors[i]);vertexPairs++;}
                triangles+=am.tris.Count;gasPairs++;
            }
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-colors");Pawn helod=null,human=null;
            h.Patch(AccessTools.Method(typeof(PawnHairColors),"HasGreyHair",new[]{typeof(Pawn),typeof(int)}),prefix:new HarmonyMethod(typeof(NativeColorChecks),nameof(Grey)),postfix:new HarmonyMethod(typeof(NativeColorChecks),nameof(GreyResult)));
            h.Patch(AccessTools.Method(typeof(PawnHairColors),"RandomGreyHairColor"),prefix:new HarmonyMethod(typeof(NativeColorChecks),nameof(RandomGrey)));
            try
            {
                helod=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));human=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,canGeneratePawnRelations:false));if(!HelodRace.IsHelod(helod)||human.def!=ThingDefOf.Human)throw new InvalidOperationException("Hair race fixtures failed.");
                Hair(helod,human);check(palettePairs==27648&&guardPairs==18432&&palette.Count==11&&forcedGreyPairs>0,"native palette preserves all 11 weighted colors, actual/forced grey delegation, race/null guards, exact float bits and every seeded RNG continuation");
                Conversions();check(byteToFloatCases==256&&floatToByteCases==775,"native Color32 normalization and byte rounding match actual game API at every channel value and adjacent/clamp/nonfinite boundaries");
                Gas(Find.CurrentMap);check(gasPairs>0&&gasRngPairs==gasPairs&&vertexPairs>0,"native gas-cell color conversion preserves quad vertex/UV/color/triangle data and append offsets across every density, actual/synthetic alpha/index and map cells");
                check(typeof(Patch_HelodHairPalette).GetField("Colors",All).FieldType==typeof(N32[]),"owned hair palette storage uses native Color32 values");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-colors.json"),JsonSerializer.Serialize(new{PalettePairs=palettePairs,GuardPairs=guardPairs,PaletteColorsObserved=palette.Count,NaturalGreyPairs=naturalGreyPairs,ForcedGreyPairs=forcedGreyPairs,ByteToFloatCases=byteToFloatCases,FloatToByteCases=floatToByteCases,GasPairs=gasPairs,GasRngPairs=gasRngPairs,VertexPairs=vertexPairs,TriangleIndicesCompared=triangles,FloatBitComparisons=floatBits,ByteComponentsCompared=byteComponents,Baseline="bf34952f",ControlledGreyPolicyInTwoPhases=true,ActualGreyMethodsInNaturalPhase=true,CpuOnlyUnregisteredGasLayerAndBuffers=true},new JsonSerializerOptions{WriteIndented=true}));
                capture=false;h.UnpatchAll(h.Id);if(helod!=null&&!helod.Destroyed)helod.Destroy();if(human!=null&&!human.Destroyed)human.Destroy();
            }
        }
    }
}
