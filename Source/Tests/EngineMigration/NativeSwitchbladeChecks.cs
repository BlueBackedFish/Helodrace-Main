using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using GV = UnityEngine.Vector3;
using NV = System.Numerics.Vector3;
using GM = UnityEngine.Matrix4x4;
using Material = UnityEngine.Material;

namespace Helodrace.Tests
{
    internal static class NativeSwitchbladeChecks
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] FloatFields = { "loiterRadius", "angle", "drawRotationDegrees", "diveStartDistance" };
        private static readonly string[] IntFields = { "ticksSinceSmoke", "deploymentTicks", "loiterTurnSign" };
        private static readonly string[] CellFields = { "loiterCenter", "targetCell" };
        private static readonly string[] ReferenceFields = { "linkedTablet", "linkedOperator", "targetThing" };
        private static List<(GM Matrix, Material Material)> draws;
        private static List<(GV Position, float Size)> smoke;
        private static List<(SoundDef Def, IntVec3 Cell, float Volume)> sounds;
        private static bool captureImpacts, forceFarZoom;
        private static int impactCount, profiles, movements, components, frames, drawSubmissions, smokeCalls, soundCalls, impacts, boundaries;
        private static float maximumError;

        public sealed class SavedDrone
        {
            public int Id { get; set; }
            public int[] Position { get; set; }
            public Dictionary<string, int> Scalars { get; set; }
            public Dictionary<string, int[]> Cells { get; set; }
            public Dictionary<string, int> References { get; set; }
        }
        private static object Get(object value, string name) => value.GetType().GetField(name, All).GetValue(value);
        private static void Set(object value, string name, object data) => value.GetType().GetField(name, All).SetValue(value, data);
        private static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, All).Invoke(value, args);
        private static object Static(Type type, string name, params object[] args) => type.GetMethod(name, All).Invoke(null, args);
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static GV Game(NV v) => new GV(v.X, v.Y, v.Z);
        private static int[] Bits(NV v) => new[] { BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y), BitConverter.SingleToInt32Bits(v.Z) };
        private static void Same(float actual, float expected)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(actual - expected));
            if (!float.IsFinite(actual) || !float.IsFinite(expected) || Math.Abs(actual - expected) > 2e-5f * Math.Max(1f, Math.Abs(expected)))
                throw new InvalidOperationException("Switchblade component changed: " + actual + " != " + expected);
        }
        private static void Vector(NV a, GV e) { Same(a.X, e.x); Same(a.Y, e.y); Same(a.Z, e.z); }
        private static void Compare(SwitchbladeDrone a, Frozen.SwitchbladeDrone e)
        {
            Vector((NV)Get(a, "exactPosition"), (GV)Get(e, "exactPosition"));
            Vector(Native(a.DrawPos), e.DrawPos);
            foreach (string field in FloatFields) Same((float)Get(a, field), (float)Get(e, field));
            foreach (string field in IntFields.Concat(CellFields).Concat(ReferenceFields).Append("mode"))
                if (!Equals(Get(a, field), Get(e, field))) throw new InvalidOperationException("Switchblade state changed: " + field);
            if (a.Position != e.Position || a.IsAttackRunActive != e.IsAttackRunActive) throw new InvalidOperationException("Switchblade cell/mode contract changed.");
            Same((float)a.GetType().GetProperty("VisualAltitudeFactor", All).GetValue(a), (float)e.GetType().GetProperty("VisualAltitudeFactor", All).GetValue(e));
        }
        private static bool Mesh(GM __1, Material __2)
        { if (draws == null) return true; draws.Add((__1, __2)); return false; }
        private static bool Smoke(GV __0, float __2)
        { if (smoke == null) return true; smoke.Add((__0, __2)); return false; }
        private static bool Sound(SoundDef __0, SoundInfo __1)
        { if (sounds == null) return true; sounds.Add((__0, __1.Maker.Cell, __1.volumeFactor)); return false; }
        private static bool Impact() { if (!captureImpacts) return true; impactCount++; return false; }
        private static bool Highlight(ref bool __result)
        { if (!forceFarZoom) return true; __result = true; return false; }

        private static void Draw(ThingWithComps a, ThingWithComps e, string method = "DrawAt")
        {
            draws = new List<(GM, Material)>();
            if (method == "DrawAt") Call(a, method, a.DrawPos, false); else a.GetComp<CompSwitchbladeLauncher>().PostDraw();
            var actual = draws.ToArray(); draws.Clear();
            if (method == "DrawAt") Call(e, method, e.DrawPos, false);
            else new Frozen.CompSwitchbladeLauncher { parent = e }.PostDraw();
            if (actual.Length != draws.Count || draws.Count != (method == "DrawAt" ? 3 : 1))
                throw new InvalidOperationException("Expected body/shadow/highlight or canister draw submissions changed/missing.");
            for (int m = 0; m < draws.Count; m++)
            {
                for (int i = 0; i < 16; i++) Same(actual[m].Matrix[i], draws[m].Matrix[i]);
                var ac = actual[m].Material.color; var ec = draws[m].Material.color;
                Same(ac.r, ec.r); Same(ac.g, ec.g); Same(ac.b, ec.b); Same(ac.a, ec.a);
                if (actual[m].Material.mainTexture != draws[m].Material.mainTexture || actual[m].Material.shader != draws[m].Material.shader)
                    throw new InvalidOperationException("Switchblade material texture/shader changed.");
            }
            drawSubmissions += draws.Count; draws = null; frames++;
        }
        private static void Step(SwitchbladeDrone a, Frozen.SwitchbladeDrone e, bool fullTick = false)
        {
            impactCount = 0; smoke = new List<(GV, float)>(); sounds = new List<(SoundDef, IntVec3, float)>();
            Call(a, fullTick ? "Tick" : "TickMovement");
            int actualImpacts = impactCount; var actualSmoke = smoke.ToArray(); var actualSounds = sounds.ToArray();
            impactCount = 0; smoke.Clear(); sounds.Clear(); Call(e, fullTick ? "Tick" : "TickMovement");
            if (actualImpacts != impactCount || actualSmoke.Length != smoke.Count || actualSounds.Length != sounds.Count)
                throw new InvalidOperationException("Switchblade impact/smoke/sound timing changed.");
            impacts += actualImpacts;
            for (int i = 0; i < smoke.Count; i++) { Vector(Native(actualSmoke[i].Position), smoke[i].Position); Same(actualSmoke[i].Size, smoke[i].Size); smokeCalls++; }
            for (int i = 0; i < sounds.Count; i++)
            {
                if (actualSounds[i].Def != sounds[i].Def || actualSounds[i].Cell != sounds[i].Cell) throw new InvalidOperationException("Switchblade loop sound/cell changed.");
                Same(actualSounds[i].Volume, sounds[i].Volume); soundCalls++;
            }
            smoke = null; sounds = null; Compare(a, e); movements++;
        }
        internal static void Create(string output, Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.NativeSwitchblade");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(Material), typeof(int) }), prefix: new HarmonyMethod(typeof(NativeSwitchbladeChecks), nameof(Mesh)));
            harmony.Patch(AccessTools.Method(typeof(FleckMaker), "ThrowSmoke", new[] { typeof(GV), typeof(Map), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeSwitchbladeChecks), nameof(Smoke)));
            harmony.Patch(AccessTools.Method(typeof(SoundStarter), "TrySpawnSustainer", new[] { typeof(SoundDef), typeof(SoundInfo) }), prefix: new HarmonyMethod(typeof(NativeSwitchbladeChecks), nameof(Sound)));
            foreach (Type type in new[] { typeof(SwitchbladeDrone), typeof(Frozen.SwitchbladeDrone) })
            {
                harmony.Patch(AccessTools.Method(type, "Impact"), prefix: new HarmonyMethod(typeof(NativeSwitchbladeChecks), nameof(Impact)));
                // Exercise the highlight branch even without an interactive
                // camera. Its live zoom decision is unchanged by this refactor.
                harmony.Patch(AccessTools.PropertyGetter(type, "ShouldDrawZoomHighlight"), prefix: new HarmonyMethod(typeof(NativeSwitchbladeChecks), nameof(Highlight)));
            }
            captureImpacts = true; forceFarZoom = true;
            Map map = Find.CurrentMap; ThingDef def = DefDatabase<ThingDef>.GetNamed("HD_Switchblade600_Drone");
            var a = (SwitchbladeDrone)ThingMaker.MakeThing(def); var e = new Frozen.SwitchbladeDrone { def = def };
            GenSpawn.Spawn(a, map.Center, map); GenSpawn.Spawn(e, map.Center, map);
            Thing target = ThingMaker.MakeThing(ThingDefOf.WoodLog); GenSpawn.Spawn(target, map.Center + new IntVec3(10, 0, 15), map);
            try
            {
                // Include exact-zero fallback and finite subnormal values explicitly.
                foreach (GV position in new[] { GV.zero, new GV(-0f, float.Epsilon, 0), map.Center.ToVector3Shifted() })
                { Set(a, "exactPosition", Native(position)); Set(e, "exactPosition", position); Compare(a, e); }
                foreach (float rotation in new[] { -720.25f, -90f, 0f, 37.5f, 180f, 359.999f })
                    foreach (float radius in new[] { .1f, 8f, 24f })
                        foreach (float offset in new[] { 0f, .03162f, 11f })
                        {
                            a.Position = e.Position = map.Center;
                            GV direction = new GV(MathF.Sin(rotation * MathF.PI / 180f), 2, MathF.Cos(rotation * MathF.PI / 180f));
                            Rand.PushState(170900 + profiles); try { a.InitializeLoiter(map.Center, radius, null, null, Native(direction)); } finally { Rand.PopState(); }
                            Rand.PushState(170900 + profiles); try { e.InitializeLoiter(map.Center, radius, null, null, direction); } finally { Rand.PopState(); }
                            GV p = map.Center.ToVector3Shifted() + new GV(offset, .125f, -offset);
                            Set(a, "exactPosition", Native(p)); Set(e, "exactPosition", p); Compare(a, e);
                            for (int tick = 0; tick < 100; tick++)
                            { Step(a, e, tick % 3 == 0); if (tick < 10 || tick % 12 == 0) Draw(a, e); }
                            a.SetLoiterCenter(map.Center + new IntVec3(-5, 0, 5)); e.SetLoiterCenter(map.Center + new IntVec3(-5, 0, 5)); Compare(a, e);
                            // Put the positive impact case downrange. A fixed
                            // point near a turning circle is not guaranteed to
                            // be reachable within this bounded observation.
                            GV forward = Game((NV)Static(typeof(SwitchbladeDrone), "DirectionForRotationDegrees", (float)Get(a, "drawRotationDegrees")));
                            target.Position = (a.DrawPos + forward * 30f).ToIntVec3();
                            a.DesignateTarget(target); e.DesignateTarget(target);
                            int before = impacts;
                            for (int tick = 0; tick < 220 && impacts == before; tick++)
                            {
                                if (tick == 5) target.Position += new IntVec3(-2, 0, 1);
                                Step(a, e, tick % 3 == 0); if (tick % 9 == 0) Draw(a, e);
                            }
                            if (impacts == before) throw new InvalidOperationException("Drone failed to reach the downrange moving target: profile=" + profiles + ", radius=" + radius + ", mode=" + Get(a, "mode") + ", position=" + a.DrawPos + ", target=" + target.Position);
                            a.ReturnToLoiter(); e.ReturnToLoiter(); Compare(a, e); Step(a, e); profiles++;
                        }
                foreach (GV p in new[] { new GV(-.125f, 0, 5), new GV(map.Size.x + .125f, 0, 5), new GV(5, 0, -.125f), new GV(5, 0, map.Size.z + .125f) })
                {
                    Set(a, "exactPosition", Native(p)); Set(e, "exactPosition", p);
                    Call(a, "SetPositionFromExactPosition"); Call(e, "SetPositionFromExactPosition"); Compare(a, e); boundaries++;
                    Call(a, "AdvanceForward", 8f); Call(e, "AdvanceForward", 8f); Compare(a, e); boundaries++;
                }
                Thing unspawned = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                foreach (SwitchbladeDroneMode mode in new[] { SwitchbladeDroneMode.TurningToTarget, SwitchbladeDroneMode.Diving })
                {
                    Set(a, "targetThing", unspawned); Set(e, "targetThing", unspawned); Set(a, "mode", mode); Set(e, "mode", mode); Step(a, e);
                    if (a.IsAttackRunActive) throw new InvalidOperationException("Drone did not abandon its unspawned target.");
                }
                LauncherChecks(map);
                check(profiles == 54 && impacts == profiles, profiles + " deployment/loiter/moving-target/dive/return profiles preserve state and impact timing");
                check(boundaries == 8, "map-edge clamp/recovery, zero/subnormal draw fallback and unavailable-target transitions match frozen behavior");
                check(frames > 0 && smokeCalls > 0 && soundCalls > 0, "native body/shadow/highlight/canister matrices, materials and smoke/loop-sound submissions match before engine execution");
            }
            finally
            {
                a.Destroy(); e.Destroy(); target.Destroy(); captureImpacts = false; forceFarZoom = false;
                draws = null; smoke = null; sounds = null; harmony.UnpatchAll(harmony.Id);
            }
            PrepareSave(output, check);
            File.WriteAllText(Path.Combine(output, "native-switchblade.json"), JsonSerializer.Serialize(new {
                Profiles = profiles, MovementTicks = movements, ImpactCallbacks = impacts, DrawFrames = frames, DrawSubmissions = drawSubmissions, SmokeSubmissions = smokeCalls,
                SoundSubmissions = soundCalls, BoundaryCases = boundaries, Components = components, MaximumFloatError = maximumError, Baseline = "9b83099f"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        private static void LauncherChecks(Map map)
        {
            var definition = DefDatabase<ThingDef>.GetNamed("HD_Building_Switchblade600Launcher");
            var launcher = (ThingWithComps)ThingMaker.MakeThing(definition, GenStuff.DefaultStuffFor(definition)); GenSpawn.Spawn(launcher, map.Center + new IntVec3(-10, 0, 0), map);
            try
            {
                foreach (Rot4 facing in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                {
                    launcher.Rotation = facing; Draw(launcher, launcher, "PostDraw");
                    foreach (LocalTargetInfo target in new[] { LocalTargetInfo.Invalid, new LocalTargetInfo(launcher.Position), new LocalTargetInfo(map.Center + new IntVec3(11, 0, -7)) })
                    {
                        NV a = (NV)Static(typeof(SwitchbladeLauncherUtility), "LaunchDirectionFor", launcher, target);
                        GV e = (GV)Static(typeof(Frozen.SwitchbladeLauncherUtility), "LaunchDirectionFor", launcher, target); Vector(a, e);
                        if (!Equals(Static(typeof(SwitchbladeLauncherUtility), "CellForDirection", a), Static(typeof(Frozen.SwitchbladeLauncherUtility), "CellForDirection", e)))
                            throw new InvalidOperationException("Launcher spawn direction/cell changed.");
                    }
                }
            }
            finally { launcher.Destroy(); }
        }
        private static SavedDrone Snapshot(SwitchbladeDrone drone) => new SavedDrone {
            Id = drone.thingIDNumber, Position = Bits((NV)Get(drone, "exactPosition")),
            Scalars = FloatFields.ToDictionary(f => f, f => BitConverter.SingleToInt32Bits((float)Get(drone, f)))
                .Concat(IntFields.ToDictionary(f => f, f => (int)Get(drone, f))).Append(new KeyValuePair<string, int>("mode", (int)(SwitchbladeDroneMode)Get(drone, "mode"))).ToDictionary(k => k.Key, k => k.Value),
            Cells = CellFields.Append("Position").ToDictionary(f => f, f => { IntVec3 c = f == "Position" ? drone.Position : (IntVec3)Get(drone, f); return new[] { c.x, c.y, c.z }; }),
            References = ReferenceFields.ToDictionary(f => f, f => (Get(drone, f) as Thing)?.thingIDNumber ?? -1)
        };
        private static void PrepareSave(string output, Action<bool, string> check)
        {
            Map map = Find.CurrentMap; Thing tablet = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_MilitaryTablet")); GenSpawn.Spawn(tablet, map.Center, map);
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); GenSpawn.Spawn(pawn, map.Center, map);
            var saved = new List<SavedDrone>();
            foreach (SwitchbladeDroneMode mode in Enum.GetValues<SwitchbladeDroneMode>())
            {
                var drone = (SwitchbladeDrone)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Switchblade600_Drone")); GenSpawn.Spawn(drone, map.Center, map);
                drone.InitializeLoiter(map.Center, 8, tablet, pawn, new NV(.125f, 0, 1));
                Set(drone, "mode", mode); Set(drone, "targetThing", pawn); Set(drone, "targetCell", pawn.Position);
                Set(drone, "exactPosition", new NV(MathF.BitIncrement(map.Center.x + .5f), mode == SwitchbladeDroneMode.Deploying ? -0f : float.Epsilon, map.Center.z + .125f));
                Set(drone, "deploymentTicks", (int)mode * 3); Set(drone, "ticksSinceSmoke", 17); Set(drone, "diveStartDistance", 21.125f);
                saved.Add(Snapshot(drone));
            }
            File.WriteAllText(Path.Combine(output, "switchblade-states.json"), JsonSerializer.Serialize(saved));
            check(saved.Count == 4, "four native drones prepare deployment/loiter/turn/dive save states with operator/tablet/target references");
        }
        internal static void Verify(string output, Action<bool, string> check)
        {
            var saved = JsonSerializer.Deserialize<SavedDrone[]>(File.ReadAllText(Path.Combine(output, "switchblade-states.json")));
            foreach (var expected in saved)
            {
                var drone = Find.CurrentMap.listerThings.AllThings.OfType<SwitchbladeDrone>().Single(d => d.thingIDNumber == expected.Id);
                var actual = Snapshot(drone);
                if (!actual.Position.SequenceEqual(expected.Position) || actual.Scalars.Any(k => expected.Scalars[k.Key] != k.Value)
                    || actual.Cells.Any(k => !expected.Cells[k.Key].SequenceEqual(k.Value)) || actual.References.Any(k => expected.References[k.Key] != k.Value))
                    throw new InvalidOperationException("Native drone geometry/state/reference did not round-trip before the first tick.");
            }
            var nodes = XDocument.Load(GenFilePaths.FilePathForSavedGame("SwitchbladeNativeState17")).Descendants().Where(n => (string)n.Attribute("Class") == typeof(SwitchbladeDrone).FullName).ToArray();
            check(saved.Length == 4 && nodes.Length == 4, "four drone modes preserve native position bits, scalar/cell state and tablet/operator/target references before the first tick");
            check(nodes.All(n => n.Element("exactPosition")?.Element("x") != null), "drone position uses explicit native scalar nodes without an older Main vector read path");
        }
    }
}
