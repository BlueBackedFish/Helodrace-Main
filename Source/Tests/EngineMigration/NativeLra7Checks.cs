using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using HarmonyLib;
using Helodrace.Future;
using RimWorld;
using Verse;
using NV = System.Numerics.Vector3;
using GV = UnityEngine.Vector3;
using GC = UnityEngine.Color;
using Mat = UnityEngine.Material;

namespace Helodrace.Tests
{
    internal static class NativeLra7Checks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        [ThreadStatic] private static List<(GV Start, GV End, Mat Material, float Width)> lines;
        [ThreadStatic] private static Thing damageTarget;
        [ThreadStatic] private static float? damageAmount;
        private sealed class SavedBeam
        {
            public SavedBeam() { }
            public int Id { get; set; }
            // Integer bit witnesses avoid a JSON float formatter changing the
            // reference before it is compared with the actual game save.
            public int[] OriginBits { get; set; }
            public int[] DestinationBits { get; set; }
            public int InitialTicks { get; set; }
            public int TicksLeft { get; set; }
            public float Width { get; set; }
        }
        private static int[] Bits(NV v) => new[] { BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y), BitConverter.SingleToInt32Bits(v.Z) };
        private static GV Game(NV v) => new GV(v.X, v.Y, v.Z);
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static bool CaptureLine(GV __0, GV __1, Mat __2, float __3)
        { if (lines == null) return true; lines.Add((__0, __1, __2, __3)); return false; }
        private static void CaptureDamage(Thing __instance, DamageInfo __0)
        { if (__instance == damageTarget) damageAmount = __0.Amount; }
        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, PrivateInstance).GetValue(value);
        private static void Set(object value, string name, object data) => value.GetType().GetField(name, PrivateInstance).SetValue(value, data);
        private static void Same(float actual, float expected)
        {
            if (!float.IsFinite(actual) || Math.Abs(actual - expected) > 2e-6f * Math.Max(1f, Math.Abs(expected)))
                throw new InvalidOperationException("LRA-7 value differs: " + actual + " != " + expected);
        }
        private static void Vector(GV actual, GV expected)
        { Same(actual.x, expected.x); Same(actual.y, expected.y); Same(actual.z, expected.z); }

        internal static void Create(string output, Action<bool, string> check)
        {
            Map map = Find.CurrentMap;
            var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
            IntVec3 clear = map.AllCells.First(c => c.x > 12 && c.z > 12 && c.x < map.Size.x - 12 && c.z < map.Size.z - 12 && c.GetEdifice(map) == null);
            GenSpawn.Spawn(wall, clear, map);
            int coneCases = 0;
            try
            {
                foreach (IntVec3 origin in new[] { map.Center, clear, clear + new IntVec3(0, 0, -2), IntVec3.Zero, map.Size - new IntVec3(1, 1, 1) })
                    foreach (GV direction in new[] { GV.zero, new GV(.000001f, 2, 0), GV.forward, GV.right, new GV(1, 0, 1), new GV(-3, 4, -2) })
                        foreach (float radius in new[] { 0f, .1f, 1f, 3.2f, 4.2f, 8f })
                            foreach (float angle in new[] { 0f, 1f, 45f, 70f, 90f, 180f, 270f })
                            {
                                var actual = LanceLra7FeedbackUtility.AffectedCells(map, origin, Native(direction), radius, angle).ToArray();
                                var expected = Frozen.LanceLra7FeedbackUtility.AffectedCells(map, origin, direction, radius, angle).ToArray();
                                if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("LRA-7 ordered cone cells changed at " + origin + " direction=" + direction + " radius=" + radius + " angle=" + angle);
                                coneCases++;
                            }
            }
            finally { wall.Destroy(DestroyMode.Vanish); }
            check(!LanceLra7FeedbackUtility.AffectedCells(null, IntVec3.Zero, NV.UnitZ, 4, 70).Any(), "LRA-7 null-map cone remains empty");
            check(true, coneCases + " ordered feedback cone cases match the frozen map/LOS/wall/angle calculation");

            ThingDef beamDef = DefDatabase<ThingDef>.GetNamed("HD_LANCE_LRA7_BeamEffect");
            MethodInfo spawn = typeof(Projectile_LanceLra7).GetMethod("SpawnBeamEffect", BindingFlags.Static | BindingFlags.NonPublic);
            int segments = 0;
            foreach (GV delta in new[] { GV.zero, new GV(5.999999f, 0, 0), new GV(6, 0, 0), new GV(6.000001f, 0, 0),
                new GV(3.6f, 0, 4.8f), new GV(2.1f, 1.8f, -16.7f), new GV(-12, 2, 8) })
            {
                GV start = clear.ToVector3Shifted() + new GV(.125f, .2f, -.25f), end = start + delta;
                var existing = new HashSet<Thing>(map.listerThings.ThingsOfDef(beamDef));
                spawn.Invoke(null, new object[] { map, Native(start), Native(end), new LanceLra7ProjectileExtension() });
                var created = map.listerThings.ThingsOfDef(beamDef).Where(t => !existing.Contains(t)).Cast<LanceLra7BeamEffect>().ToArray();
                try
                {
                    int count = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(GV.Distance(start, end) / 6f));
                    var expected = new List<(GV Start, GV End, IntVec3 Cell)>();
                    for (int i = 0; i < count; i++)
                    {
                        GV a = GV.Lerp(start, end, i / (float)count), b = GV.Lerp(start, end, (i + 1) / (float)count);
                        IntVec3 cell = GV.Lerp(a, b, .5f).ToIntVec3();
                        if (cell.InBounds(map)) expected.Add((a, b, cell));
                    }
                    if (created.Length != expected.Count) throw new InvalidOperationException("LRA-7 segment count changed.");
                    for (int i = 0; i < created.Length; i++)
                    {
                        Vector(Game(Field<NV>(created[i], "origin")), expected[i].Start);
                        Vector(Game(Field<NV>(created[i], "destination")), expected[i].End);
                        if (created[i].Position != expected[i].Cell) throw new InvalidOperationException("LRA-7 segment anchor cell changed.");
                        segments++;
                    }
                }
                finally { foreach (var beam in created) beam.Destroy(DestroyMode.Vanish); }
            }
            check(true, segments + " actual LRA-7 beam segments preserve endpoints and exact anchor cells around six-cell boundaries");
            VerifyLaunch(map, clear, check);

            var saved = new List<SavedBeam>();
            for (int i = 0; i < 4; i++)
            {
                var beam = (LanceLra7BeamEffect)ThingMaker.MakeThing(beamDef);
                NV a = new NV(10.125f + i, -0f, MathF.BitIncrement(12f));
                NV b = new NV(-.25f, .0000001f, 30.75f + i);
                beam.Initialize(a, b, 600 + i, new[] { -.1f, .01f, .02f, .12f }[i]);
                GenSpawn.Spawn(beam, clear + new IntVec3(i, 0, 0), map);
                saved.Add(new SavedBeam { Id = beam.thingIDNumber, OriginBits = Bits(a), DestinationBits = Bits(b),
                    InitialTicks = Field<int>(beam, "initialTicks"), TicksLeft = Field<int>(beam, "ticksLeft"), Width = Field<float>(beam, "width") });
                VerifyDrawing(beam);
            }
            File.WriteAllText(Path.Combine(output, "lra7-beams.json"), JsonSerializer.Serialize(saved, JsonOptions));
            check(typeof(LanceLra7BeamEffect).GetField("origin", PrivateInstance).FieldType == typeof(NV)
                && typeof(LanceLra7BeamEffect).GetField("destination", PrivateInstance).FieldType == typeof(NV), "LRA-7 beam owns native endpoints");
            check(true, "four actual beam draw callbacks preserve line endpoints, fade, widths and gradient colors");
        }

        private static void VerifyLaunch(Map map, IntVec3 clear, Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.Lra7Damage");
            harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeLra7Checks), nameof(CaptureDamage))));
            ThingDef projectileDef = DefDatabase<ThingDef>.AllDefsListForReading.Single(d => d.thingClass == typeof(Projectile_LanceLra7));
            Thing launcher = ThingMaker.MakeThing(ThingDefOf.WoodLog);
            Thing equipment = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle"));
            GenSpawn.Spawn(launcher, clear, map);
            int hits = 0;
            try
            {
                for (int seed = 0; seed < 12; seed++)
                {
                    var target = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
                    GenSpawn.Spawn(target, clear + new IntVec3(8, 0, 0), map);
                    var projectile = (Projectile_LanceLra7)ThingMaker.MakeThing(projectileDef);
                    GenSpawn.Spawn(projectile, clear, map);
                    GV origin = clear.ToVector3Shifted();
                    float expected = projectileDef.projectile.GetDamageAmount(equipment)
                        + GV.Distance(origin, target.Position.ToVector3Shifted()) * projectileDef.GetModExtension<LanceLra7ProjectileExtension>().damagePerCell;
                    damageTarget = target; damageAmount = null;
                    Rand.PushState(seed);
                    try { projectile.Launch(launcher, origin, target, target, ProjectileHitFlags.All, equipment: equipment); }
                    finally { Rand.PopState(); damageTarget = null; }
                    if (damageAmount.HasValue) { Same(damageAmount.Value, expected); hits++; }
                    if (!projectile.Destroyed) projectile.Destroy();
                    if (!target.Destroyed) target.Destroy();
                    foreach (Thing beam in map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_LANCE_LRA7_BeamEffect")).ToArray()) beam.Destroy();
                }
            }
            finally { damageTarget = null; damageAmount = null; harmony.UnpatchAll(harmony.Id); launcher.Destroy(); equipment.Destroy(); }
            check(hits > 0, "12 actual LRA-7 launches preserve range-derived hit damage (observed hits=" + hits + ")");
        }

        private static void VerifyDrawing(LanceLra7BeamEffect beam)
        {
            var harmony = new Harmony("Helodrace.Tests.Lra7Draw");
            harmony.Patch(AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawLineBetween), new[] { typeof(GV), typeof(GV), typeof(Mat), typeof(float) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeLra7Checks), nameof(CaptureLine))));
            int originalTicks = Field<int>(beam, "ticksLeft"), initial = Field<int>(beam, "initialTicks");
            try
            {
                foreach (int left in new[] { 0, 1, initial / 2, initial, initial + 1 })
                {
                    Set(beam, "ticksLeft", left); lines = new List<(GV, GV, Mat, float)>();
                    typeof(LanceLra7BeamEffect).GetMethod("DrawAt", PrivateInstance).Invoke(beam, new object[] { new GV(999, 999, 999), false });
                    if (lines.Count != 2) throw new InvalidOperationException("LRA-7 must submit one outer and one core line.");
                    float fade = UnityEngine.Mathf.Clamp01(left / (float)UnityEngine.Mathf.Max(1, initial));
                    int index = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt((1 - fade) * 15), 0, 15);
                    GV a = Game(Field<NV>(beam, "origin")), b = Game(Field<NV>(beam, "destination"));
                    a.y = b.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                    for (int layer = 0; layer < 2; layer++)
                    {
                        Vector(lines[layer].Start, a); Vector(lines[layer].End, b);
                        Same(lines[layer].Width, Field<float>(beam, "width") * (layer == 0 ? 1 : .34f) * fade);
                        GC start = layer == 0 ? new GC(1, 1, 1, .76f) : new GC(1, 1, 1, .99f);
                        GC end = layer == 0 ? new GC(.55f, .86f, 1, .76f) : new GC(.82f, .95f, 1, .99f);
                        // The existing game material cache quantizes this factory's
                        // colors to RGBA8. Compare the same game resource contract.
                        GC expected = SolidColorMaterials.SimpleSolidColorMaterial(GC.Lerp(start, end, index / 15f)).color;
                        GC actual = lines[layer].Material.color;
                        for (int channel = 0; channel < 4; channel++) Same(actual[channel], expected[channel]);
                    }
                }
            }
            finally { Set(beam, "ticksLeft", originalTicks); lines = null; harmony.UnpatchAll(harmony.Id); }
        }

        internal static void Verify(string output, Action<bool, string> check)
        {
            var specs = JsonSerializer.Deserialize<SavedBeam[]>(File.ReadAllText(Path.Combine(output, "lra7-beams.json")), JsonOptions);
            foreach (var spec in specs)
            {
                var beam = Find.CurrentMap.listerThings.AllThings.OfType<LanceLra7BeamEffect>().Single(b => b.thingIDNumber == spec.Id);
                NV a = Field<NV>(beam, "origin"), b = Field<NV>(beam, "destination");
                for (int axis = 0; axis < 3; axis++)
                    if (BitConverter.SingleToInt32Bits(a[axis]) != spec.OriginBits[axis]
                        || BitConverter.SingleToInt32Bits(b[axis]) != spec.DestinationBits[axis])
                        throw new InvalidOperationException("Native LRA-7 endpoint did not round-trip exact float bits: beam=" + spec.Id + " axis=" + axis + " origin=" + BitConverter.SingleToInt32Bits(a[axis]) + "/" + spec.OriginBits[axis] + " destination=" + BitConverter.SingleToInt32Bits(b[axis]) + "/" + spec.DestinationBits[axis]);
                check(Field<int>(beam, "initialTicks") == spec.InitialTicks && Field<int>(beam, "ticksLeft") == spec.TicksLeft,
                    "LRA-7 duration and remaining lifetime survive save/reload: " + spec.Id);
                check(Field<float>(beam, "width") == spec.Width, "LRA-7 native endpoints and width survive save/reload: " + spec.Id);
                VerifyDrawing(beam);
            }
            string path = GenFilePaths.FilePathForSavedGame("Lra7NativeState17");
            int nativeNodes = 0;
            using var reader = XmlReader.Create(path);
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.GetAttribute("Class") == typeof(LanceLra7BeamEffect).FullName)
                {
                    var node = new XmlDocument(); node.LoadXml(reader.ReadOuterXml());
                    if (node.SelectNodes("/*/origin/x | /*/origin/y | /*/origin/z | /*/destination/x | /*/destination/y | /*/destination/z").Count != 6)
                        throw new InvalidOperationException("Expected explicit scalar native vector serialization.");
                    nativeNodes++;
                }
            check(nativeNodes == specs.Length, "saved LRA-7 endpoints use explicit x/y/z nodes without an older vector schema fallback");
        }
    }
}
