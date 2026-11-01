using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Verse;
using GV = UnityEngine.Vector3;
using NV = System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeCigaretteChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        private static List<(Graphic Graphic, GV Position, float Angle)> draws;
        private static int components, frames, ticks, profiles;
        private static float maximumError;
        public sealed class SavedButt
        {
            public int Id { get; set; }
            public Dictionary<string, int[]> State { get; set; }
        }
        private static bool Capture(Graphic __instance, GV __0, float __3)
        { if (draws == null) return true; draws.Add((__instance, __0, __3)); return false; }
        private static object Get(object item, string name) => item.GetType().GetField(name, Fields).GetValue(item);
        private static void Set(object item, string name, object value) => item.GetType().GetField(name, Fields).SetValue(item, value);
        private static void Call(object item, string name, params object[] args) => item.GetType().GetMethod(name, Fields).Invoke(item, args);
        private static float[] Floats(object value) => value switch {
            float f => new[] { f }, NV n => new[] { n.X, n.Y, n.Z }, GV g => new[] { g.x, g.y, g.z }, _ => null };
        private static Dictionary<string, int[]> Snapshot(object item)
        {
            var result = new Dictionary<string, int[]>();
            foreach (FieldInfo f in item.GetType().GetFields(Fields))
            {
                object value = f.GetValue(item); var floats = Floats(value);
                result[f.Name] = floats != null ? floats.Select(BitConverter.SingleToInt32Bits).ToArray()
                    : value is int i ? new[] { i } : value is string s ? s.Select(c => (int)c).ToArray()
                    : value == null ? null : throw new InvalidOperationException("Uncovered cigarette state: " + f.Name);
            }
            return result;
        }
        private static void Exact(Dictionary<string, int[]> actual, Dictionary<string, int[]> expected)
        {
            if (actual.Count != expected.Count || expected.Any(p => !actual.TryGetValue(p.Key, out var a)
                || (a == null) != (p.Value == null) || (a != null && !a.SequenceEqual(p.Value))))
                throw new InvalidOperationException("Cigarette state bits changed.");
        }
        private static void Same(float actual, float expected)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(actual - expected));
            if (!float.IsFinite(actual) || !float.IsFinite(expected) || Math.Abs(actual - expected) > 2e-5f * Math.Max(1, Math.Abs(expected)))
                throw new InvalidOperationException("Cigarette draw component differs: " + actual + " != " + expected);
        }
        private static void Draw(object item, GV location)
        { draws = new(); Call(item, "DrawAt", location, false); }
        internal static void Create(string output, Action<bool, string> check)
        {
            ThingDef definition = DefDatabase<ThingDef>.GetNamed("HD_CigaretteButt");
            var actual = (Thing_CigaretteButt)ThingMaker.MakeThing(definition);
            var expected = new Frozen.Thing_CigaretteButt { def = definition };
            actual.Position = expected.Position = Find.CurrentMap.Center;
            var harmony = new Harmony("Helodrace.Tests.NativeCigarette");
            harmony.Patch(AccessTools.Method(typeof(Graphic), nameof(Graphic.Draw), new[] { typeof(GV), typeof(Rot4), typeof(Thing), typeof(float) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeCigaretteChecks), nameof(Capture))));
            try
            {
                foreach (GV origin in new[] { new GV(125.125f, -3, 124.75f), new GV(-.125f, 50, .25f), new GV(0, 0, 0) })
                    foreach (int duration in new[] { -5, 0, 1, 12, 37 })
                        foreach (int ember in new[] { -1, 0, 1, 10, 101 })
                            foreach (float height in new[] { 0f, .7f })
                                foreach (float spin in new[] { 0f, 32f })
                                {
                                    actual.texturePath = expected.texturePath = profiles % 2 == 0 ? null : "Items/Cigarette/OnHand/HD_Cigarette131";
                                    actual.fireTexturePath = expected.fireTexturePath = profiles % 3 == 0 ? null : "Items/Cigarette/OnHand/HD_CigaretteFire14";
                                    actual.BeginFlight(new NV(origin.x, origin.y, origin.z), duration, height, spin);
                                    expected.BeginFlight(origin, duration, height, spin);
                                    actual.BeginEmber(ember); expected.BeginEmber(ember);
                                    int limit = Math.Max(Math.Max(1, duration), Math.Max(0, ember)) + 2;
                                    for (int step = 0; step <= limit; step++)
                                    {
                                        Exact(Snapshot(actual), Snapshot(expected));
                                        GV location = new GV(125.5f, .023f, 125.5f);
                                        Draw(actual, location); var a = draws; Draw(expected, location); var e = draws;
                                        if (a.Count != e.Count) throw new InvalidOperationException("Cigarette ember draw lifetime changed.");
                                        for (int j = 0; j < a.Count; j++)
                                        {
                                            if (!ReferenceEquals(a[j].Graphic, e[j].Graphic)) throw new InvalidOperationException("Cigarette texture/color/material-cache selection changed.");
                                            Same(a[j].Position.x, e[j].Position.x); Same(a[j].Position.y, e[j].Position.y); Same(a[j].Position.z, e[j].Position.z); Same(a[j].Angle, e[j].Angle);
                                        }
                                        frames++; Call(actual, "Tick"); Call(expected, "Tick"); ticks++;
                                    }
                                    profiles++;
                                }
            }
            finally { draws = null; harmony.UnpatchAll(harmony.Id); }
            check(true, profiles + " cigarette profiles match frozen state bits, all flight/ember ticks and cached graphic selection");
            check(true, frames + " draw calls preserve flight positions, altitude, rotation, ember offset/fade/lifetime; max error " + maximumError);
            check(typeof(Thing_CigaretteButt).GetFields(Fields).All(f => f.FieldType != typeof(GV)), "cigarette owned state uses native vectors; DrawAt/Graphic remain game boundaries");
            var saved = new List<SavedButt>();
            var cells = new List<string>();
            File.WriteAllLines(Path.Combine(output, "cigarette-compression-defs.txt"), DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.saveCompressible).Select(d => d.shortHash + ": " + d.defName + " wipes=" + GenSpawn.SpawningWipes(d, definition)));
            for (int i = 0; i < 4; i++)
            {
                var butt = (Thing_CigaretteButt)ThingMaker.MakeThing(definition);
                IntVec3 cell = Find.CurrentMap.Center + new IntVec3(i + 2, 0, 2);
                cells.Add(cell + ": " + string.Join(", ", Find.CurrentMap.thingGrid.ThingsListAtFast(cell).Select(t => t.def.defName)));
                foreach (Thing obstacle in Find.CurrentMap.thingGrid.ThingsListAtFast(cell).Where(t => GenSpawn.SpawningWipes(t.def, definition)).ToArray()) obstacle.Destroy();
                GenSpawn.Spawn(butt, cell, Find.CurrentMap);
                butt.texturePath = i % 2 == 0 ? null : "Items/Cigarette/OnHand/HD_Cigarette131";
                butt.fireTexturePath = i == 0 ? null : "Items/Cigarette/OnHand/HD_CigaretteFire14";
                butt.BeginFlight(new NV(125.75f, 0, 124.125f), 37, .7f, 32f); butt.BeginEmber(101);
                Set(butt, "startDrawOffset", new NV(-0f, float.Epsilon, MathF.BitIncrement(.25f)));
                for (int j = 0; j < new[] { 0, 12, 37, 102 }[i]; j++) Call(butt, "Tick");
                saved.Add(new SavedButt { Id = butt.thingIDNumber, State = Snapshot(butt) });
            }
            File.WriteAllLines(Path.Combine(output, "cigarette-probe-cells.txt"), cells);
            check(saved.All(p => !Find.CurrentMap.listerThings.AllThings.Single(t => t.thingIDNumber == p.Id).Position.GetThingList(Find.CurrentMap).Any(t => GenSpawn.SpawningWipes(t.def, definition))), "save probes occupy cells with no spawning-wipe conflicts, including compressed chunks/edifices");
            File.WriteAllText(Path.Combine(output, "cigarette-states.json"), JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "native-cigarette.json"), JsonSerializer.Serialize(new { Profiles = profiles, Frames = frames, Ticks = ticks, Components = components, MaximumFloatError = maximumError, Baseline = "336e112a" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        internal static void Verify(string output, Action<bool, string> check)
        {
            var saved = JsonSerializer.Deserialize<SavedButt[]>(File.ReadAllText(Path.Combine(output, "cigarette-states.json")));
            File.WriteAllText(Path.Combine(output, "cigarette-loaded.txt"), string.Join("\n", Find.CurrentMap.spawnedThings.OfType<Thing_CigaretteButt>().Select(t => t.thingIDNumber + " " + t.Position + " lister=" + Find.CurrentMap.listerThings.AllThings.Contains(t))));
            foreach (var state in saved)
                Exact(Snapshot(Find.CurrentMap.listerThings.AllThings.OfType<Thing_CigaretteButt>().Single(t => t.thingIDNumber == state.Id)), state.State);
            check(saved.Length == 4, "four flight/ember phase states, textures and native offset signed-zero/subnormal/adjacent float bits restored before ticking");
        }
    }
}
