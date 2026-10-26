using System;
using Helodrace;
using UnityEngine;
using Verse;

internal static class Program
{
    private static void Main()
    {
        var comp = new CompSteamEngineSmoke();
        comp.props = new CompProperties_SteamEngineSmoke { smokeOffset = new Vector3(2, 3, 1) };
        comp.parent = new Thing { DrawPos = new Vector3(10.5f, .1f, 20.5f) };
        comp.PostSpawnSetup(false);
        // Independent camera projections for N/E/S/W; height remains upward.
        float[] x = { 2, 1, -2, -1 };
        float[] z = { 2.2139076f, -.4824747f, .4163193f, 3.1127017f };
        for (int i = 0; i < 4; i++)
        {
            comp.parent.Rotation = new Rot4(i);
            comp.CompTick();
            Vector3 actual = FleckMaker.Last;
            if (Math.Abs(actual.x - 10.5f - x[i]) > .0001f || Math.Abs(actual.z - 20.5f - z[i]) > .0001f)
                throw new Exception("Smoke projection mismatch for rotation " + i);
            if (actual.y != .1f) throw new Exception("Smoke must retain the building draw altitude.");
        }
        comp.parent.Emitter.IsProducingPower = false;
        int count = FleckMaker.Count;
        comp.CompTick();
        if (FleckMaker.Count != count) throw new Exception("Stopped engines must not smoke.");
        Console.WriteLine("PASS: smoke follows all four sprite views, true center, and running state.");
    }
}

// Exercise the production comp without initializing a Unity/RimWorld map.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero => new Vector3();
    }
    public static class Mathf
    {
        public const float Deg2Rad=(float)(Math.PI/180);
        public static float Sin(float x)=>(float)Math.Sin(x);
        public static float Cos(float x)=>(float)Math.Cos(x);
    }
}
namespace RimWorld { public class CompRefuelable { public bool HasFuel=true; } }
namespace Helodrace { public class CompMechanicalEmitter { public bool IsProducingPower=true; } }
namespace Verse
{
    public class CompProperties { public Type compClass; }
    public class ThingComp
    {
        public Thing parent; public CompProperties props;
        public virtual void PostSpawnSetup(bool respawningAfterLoad) { }
        public virtual void CompTick() { }
    }
    public struct Rot4
    {
        private readonly int value;
        public Rot4(int value) { this.value=value; }
        public float AsAngle => value*90;
    }
    public class Thing
    {
        public Vector3 DrawPos; public Rot4 Rotation; public object Map;
        public CompMechanicalEmitter Emitter=new CompMechanicalEmitter();
        public bool IsHashIntervalTick(int ticks) => true;
        public T TryGetComp<T>() where T:class => Emitter as T;
    }
    public static class GenGeo
    {
        public static Vector3 RotatedBy(this Vector3 v,float degrees)
        {
            double angle=degrees*Math.PI/180;
            return new Vector3((float)(v.x*Math.Cos(angle)+v.z*Math.Sin(angle)),v.y,
                (float)(v.z*Math.Cos(angle)-v.x*Math.Sin(angle)));
        }
    }
    public static class FleckMaker
    {
        public static Vector3 Last; public static int Count;
        public static void ThrowSmoke(Vector3 loc,object map,float size) { Last=loc;Count++; }
    }
}
