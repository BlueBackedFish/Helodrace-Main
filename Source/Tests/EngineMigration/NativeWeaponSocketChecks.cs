using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Helodrace.ModernWar;
using GR = UnityEngine.Rect;
using GV = UnityEngine.Vector2;
using NR = Waylith.Math.Rect;
using NV = System.Numerics.Vector2;

namespace Helodrace.Tests
{
    internal static class NativeWeaponSocketChecks
    {
        private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static readonly Type Product=typeof(CompModularWeaponNode).Assembly.GetType("Helodrace.ModernWar.ModularWeaponSocketLayout",true);
        private static int layouts,components,projections,sliders; private static float error;
        private static NR N(GR r)=>new(r.x,r.y,r.width,r.height);
        private static NV N(GV v)=>new(v.x,v.y);
        private static object Call(string method,params object[] args)=>Product.GetMethod(method,All).Invoke(null,args);
        private static object Get(object value,string name)=>value.GetType().GetField(name,All).GetValue(value);
        private static void Same(float a,float e)
        { components++;error=Math.Max(error,Math.Abs(a-e));if(!float.IsFinite(a)||!float.IsFinite(e)||Math.Abs(a-e)>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException("Socket geometry differs."); }
        private static void Rect(NR a,GR e){Same(a.x,e.x);Same(a.y,e.y);Same(a.width,e.width);Same(a.height,e.height);}
        private static void Vector(NV a,GV e){Same(a.X,e.x);Same(a.Y,e.y);}
        internal static void Run(string output,Action<bool,string> check)
        {
            Type cardType=Product.GetNestedType("Card",All);
            foreach(GR area in new[]{new GR(0,0,800,420),new GR(325,120,370,280),new GR(-23.25f,-12.5f,180,160),new GR(0,0,40,140)})
                foreach(int count in new[]{0,1,6,17,40})
                    foreach(int mode in new[]{0,1,2})
                        foreach(GV normal in new[]{new GV(0,-1),new GV(0,1),new GV(1,0),new GV(-.6f,.8f)})
                        {
                            var a=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(cardType));var e=new List<FrozenWeaponUI.ModularWeaponSocketLayout.Card>();
                            for(int i=0;i<count;i++)
                            {
                                GV anchor=area.center+new GV((float)Math.Cos(i*2.399963)*area.width*.25f,(float)Math.Sin(i*2.399963)*area.height*.15f);
                                bool compact=mode==1 || mode==2 && i%2==0,rail=i%3==0;object c=Activator.CreateInstance(cardType);
                                cardType.GetField("Anchor").SetValue(c,N(anchor));cardType.GetField("Outward").SetValue(c,N(normal));cardType.GetField("Compact").SetValue(c,compact);cardType.GetField("Rail").SetValue(c,rail);a.Add(c);
                                e.Add(new FrozenWeaponUI.ModularWeaponSocketLayout.Card{Anchor=anchor,Outward=normal,Compact=compact,Rail=rail});
                            }
                            var weapon=new GR(area.x+area.width*.25f,area.y+area.height*.4f,area.width*.5f,area.height*.2f);
                            object actual=Call("Arrange",N(area),a,N(weapon));var expected=FrozenWeaponUI.ModularWeaponSocketLayout.Arrange(area,e,weapon);
                            var ar=(IList)Get(actual,"Rects");var ap=(IList)Get(actual,"Pages");
                            if((int)actual.GetType().GetProperty("PageCount").GetValue(actual)!=expected.PageCount || ar.Count!=expected.Rects.Count)throw new InvalidOperationException("Socket page count changed.");
                            for(int i=0;i<ar.Count;i++){if((int)ap[i]!=expected.Pages[i])throw new InvalidOperationException("Socket source-index pagination changed.");Rect((NR)ar[i],expected.Rects[i]);}
                            layouts++;
                        }
            GV[] points={GV.zero,new GV(float.Epsilon,-0f),new GV(.000999f,0),new GV(.001f,0),new GV(.001001f,0),new GV(12,0),new GV(12.000001f,0),new GV(-123.125f,71.5f),new GV(2048,1024)};
            foreach(GV start in points)foreach(GV end in points)
            {
                object a=Call("FloatingSlider",N(start),N(end));var e=FrozenWeaponUI.ModularWeaponSocketLayout.FloatingSlider(start,end);
                Rect((NR)Get(a,"Rect"),e.Rect);Vector((NV)Get(a,"Start"),e.Start);Vector((NV)Get(a,"End"),e.End);Same((float)Get(a,"Angle"),e.Angle);sliders++;
                foreach(GV point in points)
                {
                    Same((float)Call("Fraction",N(point),N(start),N(end)),FrozenWeaponUI.ModularWeaponSocketLayout.Fraction(point,start,end));
                    Same((float)Call("Distance",N(point),N(start),N(end)),FrozenWeaponUI.ModularWeaponSocketLayout.Distance(point,start,end));
                    if((bool)a.GetType().GetMethod("Contains").Invoke(a,new object[]{N(point)})!=e.Contains(point))throw new InvalidOperationException("Rail hit test changed.");projections++;
                }
                foreach(GV offset in points)Rect((NR)Call("Follow",N(e.Rect),N(offset)),FrozenWeaponUI.ModularWeaponSocketLayout.Follow(e.Rect,offset));
            }
            check(layouts==240 && sliders==81 && projections==729,"native socket layout/source-index pages/rail side constraints, slider geometry and exact hit decisions match frozen game geometry");
            check(cardType.GetField("Anchor").FieldType==typeof(NV) && Product.GetNestedType("RailSlider",All).GetField("Rect").FieldType==typeof(NR),"socket placement, stored card data and floating rail geometry use native vectors/rectangles");
            File.WriteAllText(Path.Combine(output,"native-weapon-sockets.json"),JsonSerializer.Serialize(new{Layouts=layouts,Sliders=sliders,Projections=projections,Components=components,MaximumFloatError=error,Baseline="586d610f"},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
