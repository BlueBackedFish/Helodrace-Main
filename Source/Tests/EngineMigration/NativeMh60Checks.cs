using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.Future;
using RimWorld;
using Verse;
using Verse.Sound;
using NV = System.Numerics.Vector3;
using GV = UnityEngine.Vector3;
using GM = UnityEngine.Matrix4x4;
using Mat = UnityEngine.Material;

namespace Helodrace.Tests
{
    internal static class NativeMh60Checks
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Stages = { "ApproachEndTick", "DoorOpenStart", "RopeDeployStart", "UnloadStart", "UnloadEnd", "RopeRetractEnd", "DoorCloseEnd", "ExitEnd" };
        [ThreadStatic] private static List<(GM Matrix, Mat Material)> meshes;
        [ThreadStatic] private static List<(PawnRenderer Renderer, GV Position, Rot4? Rotation, bool NeverAim)> pawnDraws;
        [ThreadStatic] private static List<(GV Position, float Size, FleckDef Def)> effects;
        [ThreadStatic] private static bool active;
        private static int profiles, movementTicks, frames, meshCalls, pawnCalls, effectCases, dustCalls, smokeCalls, lifecycleFlights, landed, resumed, components, soundRequests;
        private static float maximumError;
        public sealed class SavedAircraft
        {
            public int Id { get; set; }
            public Dictionary<string, string> Scalars { get; set; }
            public int[] Passengers { get; set; }
            public int[] Descending { get; set; }
            public int[] Starts { get; set; }
            public int[] AllPassengers { get; set; }
            public int[] SpawnedPassengers { get; set; }
        }
        private static object Get(object v, string name) => v.GetType().GetField(name, All).GetValue(v);
        private static void Set(object v, string name, object data) => v.GetType().GetField(name, All).SetValue(v, data);
        private static object Call(object v, string name, params object[] args) => v.GetType().GetMethod(name, All).Invoke(v, args);
        private static int Stage(object v, string name) => (int)v.GetType().GetProperty(name, All).GetValue(v);
        private static ThingOwner<Pawn> Passengers(object v) => (ThingOwner<Pawn>)Get(v, "passengers");
        private static FieldInfo[] Scalars(object v) => v.GetType().GetFields(All | BindingFlags.DeclaredOnly).Where(f => !f.IsStatic && (f.FieldType == typeof(int) || f.FieldType == typeof(float) || f.FieldType == typeof(bool))).OrderBy(f => f.Name).ToArray();
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static void Same(float a, float e)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(a - e));
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1f, Math.Abs(e))) throw new InvalidOperationException("MH-60 component changed: " + a + " != " + e);
        }
        private static void Vector(NV a, GV e) { Same(a.X, e.x); Same(a.Y, e.y); Same(a.Z, e.z); }
        private static void Compare(Mh60HeliborneAircraft a, Frozen.Mh60HeliborneAircraft e)
        {
            foreach (FieldInfo field in Scalars(a))
            {
                object av = field.GetValue(a), ev = Get(e, field.Name);
                if (av is float af && ev is float ef) Same(af, ef);
                else if (!Equals(av, ev)) throw new InvalidOperationException("MH-60 scalar state changed: " + field.Name);
            }
            Vector((NV)Call(a, "AircraftDrawPosition"), (GV)Call(e, "AircraftDrawPosition"));
            foreach (string method in new[] { "AircraftAngleDegrees", "DoorOpenFactor", "RopeFactor" }) Same((float)Call(a, method), (float)Call(e, method));
            foreach (string stage in Stages) if (Stage(a, stage) != Stage(e, stage)) throw new InvalidOperationException("MH-60 phase boundary changed: " + stage);
        }
        private static bool Mesh(GM __1, Mat __2) { if (meshes == null) return true; meshes.Add((__1, __2)); return false; }
        private static bool Pawn(PawnRenderer __instance, GV __0, Rot4? __1, bool __2)
        { if (pawnDraws == null) return true; pawnDraws.Add((__instance, __0, __1, __2)); return false; }
        private static bool Dust(GV __0, float __2)
        { if (!active) return true; effects?.Add((__0, __2, null)); return false; }
        private static bool Smoke(GV __0, FleckDef __2, float __3)
        { if (!active) return true; effects?.Add((__0, __3, __2)); return false; }
        private static bool Sound(SoundDef __0)
        { if (!active) return true; if (__0.defName != "HD_HelicopterLoop") throw new InvalidOperationException("Unexpected MH-60 sound request."); soundRequests++; return false; }
        private static void Draw(Mh60HeliborneAircraft a, Frozen.Mh60HeliborneAircraft e)
        {
            meshes = new List<(GM, Mat)>(); pawnDraws = new List<(PawnRenderer, GV, Rot4?, bool)>();
            Call(a, "DrawAt", a.DrawPos, false); var am = meshes.ToArray(); var ap = pawnDraws.ToArray(); meshes.Clear(); pawnDraws.Clear(); Call(e, "DrawAt", e.DrawPos, false);
            if (am.Length != meshes.Count || ap.Length != pawnDraws.Count || am.Length < 5) throw new InvalidOperationException("MH-60 body/rope/passenger draw count changed.");
            for (int i = 0; i < am.Length; i++)
            {
                for (int c = 0; c < 16; c++) Same(am[i].Matrix[c], meshes[i].Matrix[c]);
                if (am[i].Material != meshes[i].Material) throw new InvalidOperationException("MH-60 layer/rotor material changed.");
            }
            for (int i = 0; i < ap.Length; i++)
            {
                Vector(Native(ap[i].Position), pawnDraws[i].Position);
                if (ap[i].Renderer != pawnDraws[i].Renderer || ap[i].Rotation != pawnDraws[i].Rotation || ap[i].NeverAim != pawnDraws[i].NeverAim) throw new InvalidOperationException("MH-60 held-pawn render contract changed.");
            }
            meshCalls += am.Length; pawnCalls += ap.Length; frames++; meshes = null; pawnDraws = null;
        }
        private static void Effects(Mh60HeliborneAircraft a, Frozen.Mh60HeliborneAircraft e, int seed)
        {
            effects = new List<(GV, float, FleckDef)>(); float ar, er;
            Rand.PushState(seed); try { Call(a, "ThrowRotorWashDust"); ar = Rand.Value; } finally { Rand.PopState(); }
            var actual = effects.ToArray(); effects.Clear();
            Rand.PushState(seed); try { Call(e, "ThrowRotorWashDust"); er = Rand.Value; } finally { Rand.PopState(); }
            if (ar != er || actual.Length != effects.Count) throw new InvalidOperationException("MH-60 dust emission/RNG count changed.");
            for (int i = 0; i < actual.Length; i++)
            {
                Vector(Native(actual[i].Position), effects[i].Position); Same(actual[i].Size, effects[i].Size);
                if (actual[i].Def != effects[i].Def) throw new InvalidOperationException("MH-60 visual smoke definition changed.");
                if (actual[i].Def == null) dustCalls++; else smokeCalls++;
            }
            effectCases++; effects = null;
        }
        private static Pawn NewPawn()
        {
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, fixedBiologicalAge: 25, fixedChronologicalAge: 25, canGeneratePawnRelations: false));
            pawn.health.RemoveAllHediffs(); return pawn;
        }
        private static Mh60HeliborneAircraft NewAircraft(Map map, IntVec3 cell, int count)
        {
            var aircraft = (Mh60HeliborneAircraft)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_MH60M_HeliborneAircraft"));
            if (count > 0 && !aircraft.TryLoadPassengers(Enumerable.Range(0, count).Select(_ => NewPawn()))) throw new InvalidOperationException("MH-60 passenger fixture could not load.");
            GenSpawn.Spawn(aircraft, cell, map, Rot4.East); return aircraft;
        }
        private static void ScalarStep(object aircraft, int age)
        { Set(aircraft, "ageTicks", age); Call(aircraft, "TickApproachMovement"); Call(aircraft, "TickApproachTilt"); }
        private static int Approach(object aircraft)
        {
            for (int age = 1; age <= 2000; age++) { ScalarStep(aircraft, age); if ((int)Get(aircraft, "approachCompleteTick") >= 0) return age; }
            throw new InvalidOperationException("MH-60 approach failed to settle.");
        }
        private static void Geometry(Map map)
        {
            foreach (IntVec3 cell in new[] { map.Center, new IntVec3(20, 0, 20), new IntVec3(map.Size.x - 20, 0, map.Size.z - 20) })
                foreach (int count in new[] { 0, 1, 4, 8 })
                {
                    var a = NewAircraft(map, cell, count); var e = new Frozen.Mh60HeliborneAircraft { def = a.def }; GenSpawn.Spawn(e, cell, map, Rot4.East); Set(e, "initialPassengerCount", count);
                    try
                    {
                        bool settled = false;
                        for (int age = 0; age <= 2000; age++)
                        {
                            ScalarStep(a, age); ScalarStep(e, age); Compare(a, e); movementTicks++;
                            if (age % 11 == 0) { Draw(a, e); Effects(a, e, 171000 + age); }
                            if ((int)Get(a, "approachCompleteTick") >= 0) { settled = true; break; }
                        }
                        if (!settled) throw new InvalidOperationException("MH-60 native/frozen approach did not settle.");
                        var ages = new SortedSet<int>();
                        foreach (string stage in Stages) foreach (int offset in new[] { -1, 0, 1, 2, 3, 15, 37, 60 }) ages.Add(Stage(a, stage) + offset);
                        foreach (int age in ages)
                        {
                            Set(a, "ageTicks", age); Set(e, "ageTicks", age);
                            var all = Passengers(a).InnerListForReading;
                            var descending = all.Select((p, i) => (Pawn: p, Start: Stage(a, "UnloadStart") + i * 40)).Where(p => age >= p.Start && age < p.Start + 120).ToArray();
                            Set(a, "descendingPassengers", descending.Select(p => p.Pawn).ToList()); Set(e, "descendingPassengers", descending.Select(p => p.Pawn).ToList());
                            Set(a, "descendingStartTicks", descending.Select(p => p.Start).ToList()); Set(e, "descendingStartTicks", descending.Select(p => p.Start).ToList());
                            Compare(a, e); Draw(a, e);
                            // Force emission and skip branches around both cadence rules.
                            foreach (int cadence in new[] { age, age / 4 * 4, age / 6 * 6 })
                            {
                                Set(a, "ageTicks", cadence); Set(e, "ageTicks", cadence);
                                for (int seed = 0; seed < 8; seed++) Effects(a, e, 172000 + seed);
                            }
                        }
                        profiles++;
                    }
                    finally
                    {
                        ((List<Pawn>)Get(a, "descendingPassengers")).Clear(); Passengers(a).ClearAndDestroyContents(); a.Destroy(); e.Destroy();
                    }
                }
        }
        private static void Flight(Mh60HeliborneAircraft aircraft, int expected, bool resuming, Pawn[] known = null)
        {
            var all = known ?? Passengers(aircraft).InnerListForReading.ToArray();
            var releasedAt = all.Where(p => p.Spawned).ToDictionary(p => p, _ => -1);
            int end = -1;
            for (int tick = 0; tick < 4000 && !aircraft.Destroyed; tick++)
            {
                Call(aircraft, "Tick");
                int age = (int)Get(aircraft, "ageTicks");
                if ((int)Get(aircraft, "approachCompleteTick") >= 0) end = Stage(aircraft, "ExitEnd");
                foreach (Pawn pawn in all.Where(p => p.Spawned && !releasedAt.ContainsKey(p)))
                {
                    int index = Array.IndexOf(all, pawn);
                    int earliest = Stage(aircraft, "UnloadStart") + index * 40 + 120;
                    if (age != earliest || !pawn.Position.InBounds(pawn.Map) || pawn.holdingOwner != pawn.Map.spawnedThings || Passengers(aircraft).Contains(pawn))
                        throw new InvalidOperationException("MH-60 passenger landing tick or holder release changed: age=" + age + ", expected=" + earliest + ", index=" + index + ", initial=" + expected + ", position=" + pawn.Position + ", map=" + pawn.Map + ", owner=" + pawn.holdingOwner + ", resuming=" + resuming);
                    releasedAt[pawn] = age;
                }
                int shouldRelease = Math.Clamp((age - Stage(aircraft, "UnloadStart") - 120) / 40 + 1, 0, expected);
                if (age < Stage(aircraft, "UnloadStart") + 120) shouldRelease = 0;
                if ((int)Get(aircraft, "releasedPassengerCount") != shouldRelease) throw new InvalidOperationException("MH-60 passenger release cadence changed.");
                if (aircraft.Destroyed && age != end) throw new InvalidOperationException("MH-60 departed on the wrong tick.");
            }
            if (!aircraft.Destroyed || releasedAt.Count != all.Length || Passengers(aircraft).Count != 0 || all.Any(p => !p.Spawned || p.Destroyed || p.holdingOwner != p.Map.spawnedThings)) throw new InvalidOperationException("MH-60 flight/resume lost or duplicated passengers.");
            if (resuming) resumed++; else lifecycleFlights++;
            landed += all.Length; foreach (Pawn pawn in all) pawn.Destroy();
        }
        private static Harmony Install()
        {
            var harmony = new Harmony("Helodrace.Tests.NativeMh60");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(Mat), typeof(int) }), prefix: new HarmonyMethod(typeof(NativeMh60Checks), nameof(Mesh)));
            harmony.Patch(AccessTools.Method(typeof(PawnRenderer), "RenderPawnAt", new[] { typeof(GV), typeof(Rot4?), typeof(bool) }), prefix: new HarmonyMethod(typeof(NativeMh60Checks), nameof(Pawn)));
            harmony.Patch(AccessTools.Method(typeof(FleckMaker), "ThrowDustPuff", new[] { typeof(GV), typeof(Map), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeMh60Checks), nameof(Dust)));
            harmony.Patch(AccessTools.Method(typeof(FleckMaker), "Static", new[] { typeof(GV), typeof(Map), typeof(FleckDef), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeMh60Checks), nameof(Smoke)));
            harmony.Patch(AccessTools.Method(typeof(SoundStarter), "TrySpawnSustainer", new[] { typeof(SoundDef), typeof(SoundInfo) }), prefix: new HarmonyMethod(typeof(NativeMh60Checks), nameof(Sound)));
            active = true; return harmony;
        }
        internal static void Create(string output, Action<bool, string> check)
        {
            var harmony = Install();
            try
            {
                Map map = Find.CurrentMap; Geometry(map);
                check(profiles == 12 && movementTicks > 0, "12 MH-60 location/passenger profiles preserve approach, braking, tilt and every phase boundary");
                check(meshCalls > 0 && pawnCalls > 0, "native body/door/rotor/rope matrices and held-pawn render positions match frozen submission inputs");
                check(effectCases > 0 && dustCalls > 0 && smokeCalls > 0, "native rotor-wash rim/interior/smoke positions, clipping, cadence and exact RNG order match frozen emissions");
                WriteMetrics(output);
                if (!Mh60HeliborneUtility.TryFindHoverCell(map, map.Center, out IntVec3 hover)) throw new InvalidOperationException("No valid MH-60 hover fixture.");
                foreach (int count in new[] { 1, 4, 8 }) Flight(NewAircraft(map, hover, count), count, false);
                check(lifecycleFlights == 3 && landed == 13 && soundRequests > 0, "actual one/four/eight-passenger flights preserve landing cadence, holder release and exact departure tick");
                PrepareSave(map, output, check);
            }
            finally { active = false; meshes = null; pawnDraws = null; effects = null; harmony.UnpatchAll(harmony.Id); }
            WriteMetrics(output);
        }
        private static void WriteMetrics(string output) => File.WriteAllText(Path.Combine(output, "native-mh60.json"), JsonSerializer.Serialize(new {
            Profiles = profiles, MovementTicks = movementTicks, DrawFrames = frames, MeshSubmissions = meshCalls, PawnSubmissions = pawnCalls,
            EmissionCases = effectCases, DustSubmissions = dustCalls, SmokeSubmissions = smokeCalls, LifecycleFlights = lifecycleFlights,
            ResumedFlights = resumed, LandedPassengers = landed, LoopSoundRequests = soundRequests, Components = components, MaximumFloatError = maximumError, Baseline = "ba8ef86b"
        }, new JsonSerializerOptions { WriteIndented = true }));
        private static SavedAircraft Snapshot(Mh60HeliborneAircraft a) => new SavedAircraft {
            Id = a.thingIDNumber,
            Scalars = Scalars(a).ToDictionary(f => f.Name, f => f.FieldType == typeof(float) ? "f:" + BitConverter.SingleToInt32Bits((float)f.GetValue(a)) : f.GetValue(a).ToString()),
            Passengers = Passengers(a).InnerListForReading.Select(p => p.thingIDNumber).ToArray(), Descending = ((List<Pawn>)Get(a, "descendingPassengers")).Select(p => p.thingIDNumber).ToArray(), Starts = ((List<int>)Get(a, "descendingStartTicks")).ToArray()
        };
        private static void PrepareSave(Map map, string output, Action<bool, string> check)
        {
            if (!Mh60HeliborneUtility.TryFindHoverCell(map, map.Center, out IntVec3 hover)) throw new InvalidOperationException("No save hover fixture.");
            var saved = new List<SavedAircraft>();
            foreach (string phase in new[] { "Approach", "Door", "Descent", "Retract", "Exit" })
            {
                var a = NewAircraft(map, hover, 3);
                var all = Passengers(a).InnerListForReading.ToArray();
                if (phase == "Approach") { for (int age = 1; age < 25; age++) ScalarStep(a, age); }
                else
                {
                    Approach(a);
                    int age = phase switch { "Door" => Stage(a, "DoorOpenStart") + 30, "Descent" => Stage(a, "UnloadStart") + 41, "Retract" => Stage(a, "UnloadEnd") + 20, _ => Stage(a, "DoorCloseEnd") + 30 };
                    // Run real ticks through these phases to keep the holder,
                    // counters and descending references mutually consistent.
                    while ((int)Get(a, "ageTicks") < age) Call(a, "Tick");
                }
                var snapshot = Snapshot(a); snapshot.AllPassengers = all.Select(p => p.thingIDNumber).ToArray(); snapshot.SpawnedPassengers = all.Where(p => p.Spawned).Select(p => p.thingIDNumber).ToArray(); saved.Add(snapshot);
            }
            File.WriteAllText(Path.Combine(output, "mh60-states.json"), JsonSerializer.Serialize(saved));
            check(saved.Count == 5 && saved.Any(s => s.Descending.Length > 0), "five actual approach/door/descent/retract/exit states prepare scalar bits, deep passenger holders and descending references");
        }
        internal static void Verify(string output, Action<bool, string> check)
        {
            var saved = JsonSerializer.Deserialize<SavedAircraft[]>(File.ReadAllText(Path.Combine(output, "mh60-states.json"))); var flights = new List<Mh60HeliborneAircraft>();
            foreach (var expected in saved)
            {
                var aircraft = Find.CurrentMap.listerThings.AllThings.OfType<Mh60HeliborneAircraft>().Single(t => t.thingIDNumber == expected.Id); var actual = Snapshot(aircraft);
                if (!actual.Scalars.OrderBy(p => p.Key).SequenceEqual(expected.Scalars.OrderBy(p => p.Key)) || !actual.Passengers.SequenceEqual(expected.Passengers) || !actual.Descending.SequenceEqual(expected.Descending) || !actual.Starts.SequenceEqual(expected.Starts)) throw new InvalidOperationException("MH-60 state/holder/reference changed before first tick.");
                if (Passengers(aircraft).InnerListForReading.Any(p => p.Spawned || p.holdingOwner != Passengers(aircraft)) || ((List<Pawn>)Get(aircraft, "descendingPassengers")).Any(p => !Passengers(aircraft).Contains(p))) throw new InvalidOperationException("MH-60 deep holder/descending reference identity lost.");
                flights.Add(aircraft);
            }
            check(flights.Count == 5, "five phase states retain exact scalar bits, passenger order/deep ownership and descending references before the first simulation tick");
            var pawns = Find.CurrentMap.listerThings.AllThings.OfType<Pawn>().Concat(flights.SelectMany(f => Passengers(f).InnerListForReading)).ToDictionary(p => p.thingIDNumber);
            for (int i = 0; i < flights.Count; i++)
                if (!saved[i].AllPassengers.Where(id => pawns[id].Spawned).SequenceEqual(saved[i].SpawnedPassengers)) throw new InvalidOperationException("Already-landed passenger state changed on reload.");
            var harmony = Install();
            try { for (int i = 0; i < flights.Count; i++) Flight(flights[i], (int)Get(flights[i], "initialPassengerCount"), true, saved[i].AllPassengers.Select(id => pawns[id]).ToArray()); }
            finally { active = false; harmony.UnpatchAll(harmony.Id); }
            check(resumed == 5, "all five reloaded flights complete without lost/duplicate passengers or retained holders");
            WriteMetrics(output);
        }
    }
}
