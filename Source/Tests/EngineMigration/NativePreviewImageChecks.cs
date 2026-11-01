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
using Verse;
using GC = UnityEngine.Color;
using GV = UnityEngine.Vector2;
using GR = UnityEngine.Rect;
using NV = System.Numerics.Vector2;
using NR = Waylith.Math.Rect;
using Texture = UnityEngine.Texture;
using Texture2D = UnityEngine.Texture2D;
using GUI = UnityEngine.GUI;

namespace Helodrace.Tests
{
    internal static class NativePreviewImageChecks
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Assembly Product = typeof(CompModularWeaponNode).Assembly;
        private static readonly Type Armor = Product.GetType("Helodrace.ModernWar.ModularArmorPreviewBounds", true);
        private static readonly Type Bounds = Product.GetType("Helodrace.ModernWar.ModularWeaponImageBounds", true);
        private static readonly Type Data = Product.GetType("Helodrace.ModernWar.ModularWeaponTextureData", true);
        private static readonly Type Image = Product.GetType("Helodrace.ModernWar.ModularWeaponPartImage", true);
        private delegate bool FindMask(byte[] alpha, int width, int height, out NR uv);
        private static int components, maskCases, crops, unions, combined, frames, submissions;
        private static float maximumError;
        private sealed record Command(string Kind, int Texture, float[] Values);
        private static List<Command> trace;
        private static GC color;
        private static readonly Dictionary<string, Texture2D> textures = new();
        private static NR Native(GR r) => new(r.x, r.y, r.width, r.height);
        private static GR Game(NR r) => new(r.x, r.y, r.width, r.height);
        private static object Call(Type type, object instance, string name, params object[] args) => type.GetMethod(name, All).Invoke(instance, args);
        private static void Same(float a, float e)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(a - e));
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1, Math.Abs(e)))
                throw new InvalidOperationException("Image geometry differs: " + a + " != " + e);
        }
        private static void Rect(NR a, GR e) { Same(a.x, e.x); Same(a.y, e.y); Same(a.width, e.width); Same(a.height, e.height); }
        private static void Add(string kind, GR r = default, Texture texture = null)
        {
            trace.Add(new Command(kind, texture == null ? 0 : RuntimeHelpers.GetHashCode(texture), new[] { r.x, r.y, r.width, r.height, color.r, color.g, color.b, color.a }));
        }
        private static bool GetColor(ref GC __result) { if (trace == null) return true; __result = color; return false; }
        private static bool SetColor(GC __0) { if (trace == null) return true; color = __0; Add("color"); return false; }
        private static bool Group(GR __0) { if (trace == null) return true; Add("group", __0); return false; }
        private static bool EndGroup() { if (trace == null) return true; Add("end"); return false; }
        private static bool Draw(GR __0, Texture __1) { if (trace == null) return true; Add("texture", __0, __1); return false; }
        private static bool Content(string __0, ref Texture2D __result)
        { if (!__0.StartsWith("NativeImage/", StringComparison.Ordinal)) return true; textures.TryGetValue(__0, out __result); return false; }
        private static void Frame(Action actual, Action expected)
        {
            color = new GC(.2f, .75f, 1.25f, .375f); trace = new(); actual(); var a = trace;
            color = new GC(.2f, .75f, 1.25f, .375f); trace = new(); expected(); var e = trace;
            if (a.Count != e.Count) throw new InvalidOperationException("Image submission count changed.");
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Kind != e[i].Kind || a[i].Texture != e[i].Texture) throw new InvalidOperationException("Layer/outline order changed.");
                for (int j = 0; j < a[i].Values.Length; j++) Same(a[i].Values[j], e[i].Values[j]);
            }
            frames++; submissions += a.Count; trace = null;
        }
        private static void Pure()
        {
            var armorFind = (FindMask)Armor.GetMethod("TryFindVisibleBounds", All).CreateDelegate(typeof(FindMask));
            var weaponFind = (FindMask)Bounds.GetMethod("TryFind", All).CreateDelegate(typeof(FindMask));
            var destination = (Func<NR, NR, int, int, NR, NR>)Armor.GetMethod("DestinationFor", All).CreateDelegate(typeof(Func<NR, NR, int, int, NR, NR>));
            var union = (Func<NR, NR, NR>)Bounds.GetMethod("Union", All).CreateDelegate(typeof(Func<NR, NR, NR>));
            var canvas = (Func<NR, NV, NR>)Bounds.GetMethod("OnCanvas", All).CreateDelegate(typeof(Func<NR, NV, NR>));
            var viewports = new[] { new GR(0,0,400,300), new GR(-7.25f,12,1,1), new GR(30,-50,0,200), new GR(0,0,-100,73), new GR(-0f,float.Epsilon,1024,768) };
            var sizes = new[] { new GV(1,1), new GV(4,.125f), new GV(-2,1), new GV(-0f,3) };
            var rects = new List<GR>();
            foreach (var dimension in new[] { (1,1), (2,3), (64,32), (33,65) })
                for (int profile = 0; profile < 12; profile++)
                {
                    int w = dimension.Item1, h = dimension.Item2; var alpha = new byte[w*h];
                    if (profile > 0)
                        for (int i = 0; i < alpha.Length; i++)
                            if (profile < 5 ? i == 0 || i == alpha.Length - 1 : profile >= 8 ? i == (h/3)*w+w/3 || i == (h*2/3)*w+w*2/3 : (i * 17 + profile) % 23 == 0)
                                alpha[i] = new byte[] { 0, 7, 8, 9, 24, 25, 127, 255 }[profile % 8];
                    bool a = armorFind(alpha,w,h,out NR ar), e = FrozenImages.ModularArmorPreviewBounds.TryFindVisibleBounds(alpha,w,h,out GR er);
                    if (a != e) throw new InvalidOperationException("Armor alpha threshold changed."); Rect(ar,er);
                    a = weaponFind(alpha,w,h,out NR wr); e = FrozenImages.ModularWeaponImageBounds.TryFind(alpha,w,h,out GR we);
                    if (a != e) throw new InvalidOperationException("Weapon alpha threshold/padding changed."); Rect(wr,we); rects.Add(we);
                    foreach (var size in sizes) Rect(canvas(wr,new NV(size.x,size.y)), FrozenImages.ModularWeaponImageBounds.OnCanvas(we,size));
                    foreach (GR area in viewports)
                        foreach (GR crop in new[] { new GR(0,0,1,1), new GR(.25f,.125f,.5f,.75f), new GR(0,0,-1,0) })
                            foreach (var textureSize in new[] { (1,1),(64,32),(4096,2048) })
                            { Rect(destination(Native(area),ar,textureSize.Item1,textureSize.Item2,Native(crop)), FrozenImages.ModularArmorPreviewBounds.DestinationFor(area,er,textureSize.Item1,textureSize.Item2,crop)); crops++; }
                    maskCases++;
                }
            foreach (var invalid in new[] { ((byte[])null,2,2), (new byte[1],0,1), (new byte[1],1,-1), (new byte[1],2,2) })
            {
                bool a = armorFind(invalid.Item1,invalid.Item2,invalid.Item3,out NR ar), e = FrozenImages.ModularArmorPreviewBounds.TryFindVisibleBounds(invalid.Item1,invalid.Item2,invalid.Item3,out GR er);
                if (a != e) throw new InvalidOperationException("Armor invalid mask handling changed."); Rect(ar,er); maskCases++;
            }
            rects.AddRange(new[] { new GR(1,2,-3,-4), new GR(-0f,float.Epsilon,0,0), new GR(-.5f,-.25f,4,2) });
            foreach (GR a in rects) foreach (GR e in rects) { Rect(union(Native(a),Native(e)),FrozenImages.ModularWeaponImageBounds.Union(a,e)); unions++; }
        }
        private static object SeedData(Type type, Texture2D texture, byte[] alpha)
        {
            object value = Activator.CreateInstance(type, true);
            GR uv; bool visible = FrozenImages.ModularWeaponImageBounds.TryFind(alpha,256,256,out uv);
            type.GetProperty("Alpha", All).SetValue(value, alpha);
            type.GetProperty("VisibleUv", All).SetValue(value, type == Data ? (object)Native(uv) : uv);
            type.GetProperty("HasPixels", All).SetValue(value, visible);
            object cache = type.GetField("cache",All).GetValue(null);
            cache.GetType().GetMethod("Remove",new[] {typeof(Texture2D)}).Invoke(cache,new object[] { texture });
            cache.GetType().GetMethod("Add",new[] {typeof(Texture2D),type}).Invoke(cache,new[] { texture, value });
            return value;
        }
        private static void Images(Texture2D[] source)
        {
            var content = AccessTools.Method(typeof(ContentFinder<Texture2D>), "Get", new[] { typeof(string), typeof(bool) });
            var harmony = new Harmony("Helodrace.Tests.NativePreviewImages");
            void Patch(MethodInfo method, string prefix) => harmony.Patch(method,prefix:new HarmonyMethod(typeof(NativePreviewImageChecks),prefix));
            Patch(content,nameof(Content)); Patch(AccessTools.PropertyGetter(typeof(GUI),"color"),nameof(GetColor)); Patch(AccessTools.PropertySetter(typeof(GUI),"color"),nameof(SetColor));
            Patch(AccessTools.Method(typeof(GUI),"BeginGroup",new[] {typeof(GR)}),nameof(Group)); Patch(AccessTools.Method(typeof(GUI),"EndGroup",Type.EmptyTypes),nameof(EndGroup));
            Patch(AccessTools.Method(typeof(GUI),"DrawTexture",new[] {typeof(GR),typeof(Texture)}),nameof(Draw));
            try
            {
                foreach (int mode in new[] { 0,1,2 })
                    foreach (int count in new[] { 0,1,3,5 })
                    {
                        textures.Clear();
                        for (int i=0;i<source.Length;i++)
                        {
                            textures["NativeImage/"+i] = source[i]; if (i%2==0) textures["NativeImage/"+i+"_Outline"] = source[(i+1)%source.Length];
                            var alpha = new byte[256*256];
                            if (mode != 2 && (mode == 0 || i != 1))
                                for (int p=0;p<alpha.Length;p++) if ((p + i*19)%71==0) alpha[p] = p%2==0 ? (byte)25 : (byte)255;
                            SeedData(Data,source[i],alpha); SeedData(typeof(FrozenImages.ModularWeaponTextureData),source[i],alpha);
                        }
                        var props = new CompProperties_ModularWeaponNode { graphicLayer=0, additionalGraphics=new List<ModularWeaponGraphicLayer>() };
                        var def = new ThingDef { defName="NativeImageCase_"+mode+"_"+count, comps=new List<CompProperties>{props} };
                        for (int i=0;i<count;i++)
                        {
                            var g=new GraphicData { texPath="NativeImage/"+(i%source.Length),drawSize=new GV(new[] {1f,4f,-2f,.001f,2f}[i],new[] {1f,.125f,1f,3f,-.5f}[i]) };
                            if (i==0) def.graphicData=g; else props.additionalGraphics.Add(new ModularWeaponGraphicLayer { graphicData=g,graphicLayer=new[] {0f,0f,-1f,2f}[i-1] });
                        }
                        object actual=Call(Image,null,"For",def); var expected=FrozenImages.ModularWeaponPartImage.For(def);
                        foreach (GR area in new[] {new GR(30,70,360,250),new GR(-100.5f,-7.25f,760,560),new GR(0,0,1,1),new GR(0,0,0,100),new GR(0,0,100,-1)})
                            foreach (float zoom in new[] {-1f,0f,.25f,1f,3f,8f})
                                foreach (GV pan in new[] {GV.zero,new GV(-31.125f,72.5f),new GV(1024,-512)})
                                    Frame(()=>Call(Image,actual,"Draw",Native(area),zoom,new NV(pan.x,pan.y)),()=>expected.Draw(area,zoom,pan));
                    }
            }
            finally { trace=null; textures.Clear(); harmony.UnpatchAll(harmony.Id); }
        }
        private static void Combined(Texture2D[] source)
        {
            object actual=Activator.CreateInstance(Armor,true); var expected=new FrozenImages.ModularArmorPreviewBounds();
            var a=(IDictionary)Armor.GetField("cache",All).GetValue(actual); var e=(IDictionary)expected.GetType().GetField("cache",All).GetValue(expected);
            GR[] regions={new GR(.125f,.25f,.5f,.25f),new GR(0,0,0,0),new GR(.25f,.125f,.75f,.875f)};
            for (int i=0;i<source.Length;i++) { a.Add(source[i],Native(regions[i])); e.Add(source[i],regions[i]); }
            for (int flags=0;flags<8;flags++)
                foreach (bool reverse in new[] {false,true})
                {
                    var layers=new List<Texture2D>{null}; for(int i=0;i<3;i++) if((flags&(1<<i))!=0) layers.Add(source[i]); if(reverse) layers.Reverse();
                    Rect((NR)Call(Armor,actual,"CombinedBounds",layers),expected.CombinedBounds(layers)); combined++;
                }
        }
        internal static void Run(string output, Action<bool,string> check)
        {
            Pure(); var source=new[] {BaseContent.WhiteTex,BaseContent.BlackTex,BaseContent.GreyTex}; Combined(source); Images(source);
            check(maskCases==52,"native armor/weapon alpha thresholds, padding, edge cases and invalid armor masks match actual game geometry");
            check(crops==2160 && unions==2601 && combined==16,"crop fitting, negative extents, nonuniform/mirrored canvas and primed per-texture unions match frozen calculations");
            check(frames==1080,"layer ordering, real/synthesized outlines, empty pixels/images, clipping groups, pan/zoom and rejected areas match frozen submissions");
            check(Image.GetField("bounds",All).FieldType==typeof(NR) && Data.GetProperty("VisibleUv",All).PropertyType==typeof(NR)
                && Armor.GetField("cache",All).FieldType.GetGenericArguments()[1]==typeof(NR)
                && Image.GetNestedType("Layer",All).GetField("Size",All).FieldType==typeof(NV),"owned image bounds/UV/cache use native rectangles; current texture/GUI contracts remain explicit boundaries");
            File.WriteAllText(Path.Combine(output,"native-preview-images.json"),JsonSerializer.Serialize(new {MaskCases=maskCases,CropCases=crops,UnionCases=unions,CombinedCases=combined,Frames=frames,Submissions=submissions,Components=components,MaximumFloatError=maximumError,Baseline="debae443",PrimedAlphaCaches=true,GpuSubmissionSuppressed=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
