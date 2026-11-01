using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.Tactical;
using RimWorld;
using Verse;
using GV = UnityEngine.Vector3;
using GC = UnityEngine.Color;
using NV = System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativePreemptiveChecks
    {
        private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Actual = typeof(TacticalPreemptiveFireUtility), Frozen = typeof(FrozenPreemptive.TacticalPreemptiveFireUtility);
        private sealed record Submission(string Kind, float[] Values);
        private static List<Submission> trace;
        private static int components, previews, activeFrames, starts, submissions;
        private static float range;
        private static bool Line(GV __0, GV __1, SimpleColor __2, float __3)
        { if (trace == null) return true; trace.Add(new("line", new[] { __0.x, __0.y, __0.z, __1.x, __1.y, __1.z, (float)__2, __3 })); return false; }
        private static bool Ring(IntVec3 __0, float __1, GC __2)
        { if (trace == null) return true; trace.Add(new("ring", new[] { (float)__0.x, __0.y, __0.z, __1, __2.r, __2.g, __2.b, __2.a })); return false; }
        private static bool Range(ref float __result) { __result = range; return false; }
        private static bool Access(ref bool __result) { __result = true; return false; }
        private static object Get(object v, string f) => v.GetType().GetField(f, All).GetValue(v);
        private static IDictionary States(Type t) => (IDictionary)t.GetField("states", All).GetValue(null);
        private static void Same(float a, float e)
        { components++; if (!a.Equals(e)) throw new InvalidOperationException("Preemptive geometry changed: " + a + " != " + e); }
        private static void Frame(Action actual, Action frozen, int count)
        {
            trace = new(); actual(); var a = trace.ToArray(); trace.Clear(); frozen(); var e = trace.ToArray(); trace = null;
            if (a.Length != count || e.Length != count) throw new InvalidOperationException("Preemptive submission count changed.");
            for (int i = 0; i < count; i++) { if (a[i].Kind != e[i].Kind) throw new InvalidOperationException("Preemptive submission order changed."); for (int j = 0; j < a[i].Values.Length; j++) Same(a[i].Values[j], e[i].Values[j]); }
            submissions += count;
        }
        private static void Preview(Type t, IntVec3 origin, IntVec3 destination, Verb verb, Map map, bool valid)
            => t.GetMethod("DrawLinePreview", All).Invoke(null, new object[] { origin, destination, verb, map, valid });
        private static void Start(Type t, Pawn p, IntVec3 destination) => t.GetMethod("Start", All).Invoke(null, new object[] { p, destination });
        private static void State(Type t, Pawn p, IntVec3 origin, IntVec3 destination, int ready)
        {
            object state = Activator.CreateInstance(t.GetNestedType("State", All), true);
            GV raw = destination.ToVector3Shifted() - origin.ToVector3Shifted(); raw.y = 0; GV direction = raw.normalized;
            foreach (var pair in new (string, object)[] { ("pawn", p), ("origin", origin), ("destination", destination), ("direction", t == Actual ? new NV(direction.x, direction.y, direction.z) : (object)direction), ("length", Math.Min(raw.magnitude, range)), ("readyTick", ready) })
                state.GetType().GetField(pair.Item1, All).SetValue(state, pair.Item2);
            States(t)[p.thingIDNumber] = state;
        }
        internal static void Run(string output, Action<bool, string> check)
        {
            Map map = Find.CurrentMap; int tick = Find.TickManager.TicksGame;
            if (States(Actual).Count != 0 || States(Frozen).Count != 0) throw new InvalidOperationException("Use an isolated preemptive fixture.");
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            var weapon = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M16A4_Weapon"));
            pawn.equipment.AddEquipment(weapon); GenSpawn.Spawn(pawn, map.Center, map); pawn.drafter.Drafted = true;
            Verb verb = pawn.equipment.Primary.GetComp<CompEquippable>().PrimaryVerb;
            var h = new Harmony("helodrace.tests.native-preemptive");
            void Patch(MethodInfo method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(NativePreemptiveChecks), prefix));
            try
            {
                Patch(AccessTools.Method(typeof(GenDraw), "DrawLineBetween", new[] { typeof(GV), typeof(GV), typeof(SimpleColor), typeof(float) }), nameof(Line));
                Patch(AccessTools.Method(typeof(GenDraw), "DrawRadiusRing", new[] { typeof(IntVec3), typeof(float), typeof(GC), typeof(Func<IntVec3, bool>) }), nameof(Ring));
                Patch(AccessTools.PropertyGetter(typeof(Verb), "EffectiveRange"), nameof(Range));
                foreach (Type t in new[] { Actual, Frozen }) { Patch(AccessTools.Method(t, "HasAccess"), nameof(Access)); Patch(AccessTools.Method(t, "IsValidLine"), nameof(Access)); }
                var origins = new[] { map.Center, new IntVec3(0, 0, 0), new IntVec3(map.Size.x - 1, 3, map.Size.z - 1) };
                var offsets = new[] { IntVec3.Zero, new IntVec3(1, 0, 0), new IntVec3(-1, 2, 0), new IntVec3(0, -3, 1), new IntVec3(0, 0, -1), new IntVec3(1, 0, 1), new IntVec3(-1, 0, 1), new IntVec3(17, 0, -23), new IntVec3(-90, 0, -91) };
                foreach (IntVec3 origin in origins) foreach (IntVec3 offset in offsets) foreach (float r in new[] { 0f, .5f, 1f, 20f, 1000f }) foreach (bool valid in new[] { false, true }) foreach (bool absent in new[] { false, true })
                {
                    range = r; Verb v = absent ? null : verb; IntVec3 destination = origin + offset;
                    Frame(() => Preview(Actual, origin, destination, v, map, valid), () => Preview(Frozen, origin, destination, v, map, valid), offset.x == 0 && offset.z == 0 ? 0 : 4); previews++;
                }
                foreach (bool nullMap in new[] { false, true })
                { IntVec3 origin = new(-1, 0, -1); Frame(() => Preview(Actual, origin, map.Center, verb, nullMap ? null : map, true), () => Preview(Frozen, origin, map.Center, verb, nullMap ? null : map, true), 0); previews++; }
                range = 30;
                foreach (IntVec3 offset in offsets.Skip(1)) foreach (int ready in new[] { tick - 1, tick, tick + 1 }) foreach (bool moved in new[] { false, true })
                {
                    IntVec3 origin = moved ? pawn.Position + new IntVec3(1, 0, 0) : pawn.Position;
                    State(Actual, pawn, origin, origin + offset, ready); State(Frozen, pawn, origin, origin + offset, ready);
                    Frame(() => TacticalPreemptiveFireUtility.DrawActive(map), () => FrozenPreemptive.TacticalPreemptiveFireUtility.DrawActive(map), moved ? 0 : 3); activeFrames++;
                    Frame(() => TacticalPreemptiveFireUtility.DrawActive(null), () => FrozenPreemptive.TacticalPreemptiveFireUtility.DrawActive(null), 0); activeFrames++;
                }
                foreach (IntVec3 offset in offsets.Skip(1)) foreach (float r in new[] { .5f, 30f, 1000f })
                {
                    range = r; States(Actual).Clear(); States(Frozen).Clear();
                    Start(Actual, pawn, pawn.Position + offset); Start(Frozen, pawn, pawn.Position + offset);
                    object a = States(Actual)[pawn.thingIDNumber], e = States(Frozen)[pawn.thingIDNumber];
                    if (a == null || e == null) throw new InvalidOperationException("Actual ordered-job start did not create state.");
                    NV ad = (NV)Get(a, "direction"); GV ed = (GV)Get(e, "direction"); Same(ad.X, ed.x); Same(ad.Y, ed.y); Same(ad.Z, ed.z); Same((float)Get(a, "length"), (float)Get(e, "length"));
                    foreach (string f in new[] { "pawn", "verb", "origin", "destination", "requiresAim", "readyTick", "nextScanTick", "firing", "nextPairTick" }) if (!Equals(Get(a, f), Get(e, f))) throw new InvalidOperationException("Preemptive start state changed: " + f);
                    Frame(() => TacticalPreemptiveFireUtility.DrawActive(map), () => FrozenPreemptive.TacticalPreemptiveFireUtility.DrawActive(map), 3); starts++;
                }
                check(Actual.GetNestedType("State", All).GetField("direction").FieldType == typeof(NV), "preemptive firing direction state uses native BCL vector");
                check(previews == 542, "preview endpoints, corridor offsets, color/width, zero-line and invalid-map gates match frozen code");
                check(activeFrames == 96, "active line ready-tick colors, widths and moved/wrong-map gates match frozen code");
                check(starts == 24, "actual ordered-job starts produce identical native direction, clamped length and transient firing state");
            }
            finally { trace = null; States(Actual).Clear(); States(Frozen).Clear(); h.UnpatchAll(h.Id); Find.TickManager.DebugSetTicksGame(tick); pawn.Destroy(); }
            File.WriteAllText(Path.Combine(output, "native-preemptive.json"), JsonSerializer.Serialize(new { Previews = previews, ActiveFrames = activeFrames, Starts = starts, Submissions = submissions, Components = components, MaximumFloatError = 0, Baseline = "de5143c6", ControlledAccessAndLineValidityAndRange = true, SuppressedDrawSubmissions = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
