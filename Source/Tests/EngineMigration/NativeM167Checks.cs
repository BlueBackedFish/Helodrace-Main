using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using GV = UnityEngine.Vector3;
using NV = System.Numerics.Vector3;
using GM = UnityEngine.Matrix4x4;

namespace Helodrace.Tests
{
    internal static class NativeM167Checks
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [ThreadStatic] private static List<(GM Matrix, UnityEngine.Material Material)> draws;
        [ThreadStatic] private static List<(SoundDef Def, IntVec3 Cell, float Volume)> sounds;
        private static int cases, frames, components, soundCalls, expirations;
        private static float maximumError;
        private static bool Mesh(GM __1, UnityEngine.Material __2)
        { if (draws == null) return true; draws.Add((__1, __2)); return false; }
        private static bool Sound(SoundDef __0, SoundInfo __1)
        { if (sounds == null) return true; sounds.Add((__0, __1.Maker.Cell, __1.volumeFactor)); return false; }
        private static object Get(object v, string name) => v.GetType().GetField(name, Instance).GetValue(v);
        private static void Set(object v, string name, object data) => v.GetType().GetField(name, Instance).SetValue(v, data);
        private static object Call(object v, string name, params object[] args) => v.GetType().GetMethod(name, Instance).Invoke(v, args);
        private static IList Casings(object v) => (IList)Get(v, "casings");
        private static float[] Floats(object v) => v switch { float f => new[] { f }, NV n => new[] { n.X, n.Y, n.Z }, GV g => new[] { g.x, g.y, g.z }, _ => null };
        private static void Same(float a, float e)
        {
            components++; maximumError = Math.Max(maximumError, Math.Abs(a - e));
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1, Math.Abs(e)))
                throw new InvalidOperationException("M167 component differs: " + a + " != " + e);
        }
        private static void Vector(object a, object e)
        { var x = Floats(a); var y = Floats(e); for (int i = 0; i < x.Length; i++) Same(x[i], y[i]); }
        private static void Compare(CompM167VadsGraphic a, Frozen.CompM167VadsGraphic e)
        {
            var x = Casings(a); var y = Casings(e);
            if (x.Count != y.Count) throw new InvalidOperationException("M167 casing count changed.");
            for (int i = 0; i < x.Count; i++)
                foreach (var f in y[i].GetType().GetFields(Instance))
                {
                    object av = Get(x[i], f.Name), ev = f.GetValue(y[i]);
                    if (Floats(av) != null && Floats(ev) != null) Vector(av, ev);
                    else if (!Equals(av, ev)) throw new InvalidOperationException("M167 casing state changed: " + f.Name);
                }
            foreach (string name in new[] { "barrelFrame", "warmupDurationTicks", "frameProgress", "spinSpeedFramesPerTick", "wasWarmingUp", "externalWarmupEndTick", "lastCasingDropSoundTick" })
            {
                object av = Get(a, name), ev = Get(e, name);
                if (av is float af && ev is float ef) Same(af, ef);
                else if (!Equals(av, ev)) throw new InvalidOperationException("M167 scalar state changed: " + name);
            }
        }
        private static void Draw(CompM167VadsGraphic a, Frozen.CompM167VadsGraphic e)
        {
            draws = new List<(GM, UnityEngine.Material)>(); a.PostDraw(); var expected = draws.ToArray(); draws.Clear(); e.PostDraw();
            if (expected.Length != draws.Count || draws.Count < 3) throw new InvalidOperationException("M167 layer/casing draws missing or count changed.");
            for (int m = 0; m < draws.Count; m++)
            {
                if (expected[m].Material != draws[m].Material) throw new InvalidOperationException("M167 draw material changed.");
                for (int i = 0; i < 16; i++) Same(expected[m].Matrix[i], draws[m].Matrix[i]);
            }
            draws = null; frames++;
        }
        private static void Drop(CompM167VadsGraphic a, Frozen.CompM167VadsGraphic e, int tick)
        {
            sounds = new List<(SoundDef, IntVec3, float)>(); Call(a, "PlayCasingDropSounds", tick); var expected = sounds.ToArray(); sounds.Clear(); Call(e, "PlayCasingDropSounds", tick);
            if (expected.Length != sounds.Count) throw new InvalidOperationException("M167 drop sound count changed.");
            for (int i = 0; i < sounds.Count; i++)
            {
                if (expected[i].Def != sounds[i].Def || expected[i].Cell != sounds[i].Cell) throw new InvalidOperationException("M167 landing sound definition/cell changed.");
                Same(expected[i].Volume, sounds[i].Volume); soundCalls++;
            }
            sounds = null;
        }
        internal static void Run(string output, Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.NativeM167");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(UnityEngine.Material), typeof(int) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeM167Checks), nameof(Mesh))));
            harmony.Patch(AccessTools.Method(typeof(SoundStarter), "PlayOneShot", new[] { typeof(SoundDef), typeof(SoundInfo) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeM167Checks), nameof(Sound))));
            ThingDef definition = DefDatabase<ThingDef>.AllDefsListForReading.Single(d => d.comps?.OfType<CompProperties_M167VadsGraphic>().Any() == true);
            var turret = (Building_TurretGun)ThingMaker.MakeThing(definition, GenStuff.DefaultStuffFor(definition));
            GenSpawn.Spawn(turret, Find.CurrentMap.Center, Find.CurrentMap);
            var a = turret.GetComp<CompM167VadsGraphic>();
            var original = (CompProperties_M167VadsGraphic)a.props;
            var props = (CompProperties_M167VadsGraphic)typeof(object).GetMethod("MemberwiseClone", Instance).Invoke(original, null); a.props = props;
            var oldProps = new Frozen.CompProperties_M167VadsGraphic();
            foreach (var f in typeof(CompProperties_M167VadsGraphic).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)) oldProps.GetType().GetField(f.Name).SetValue(oldProps, f.GetValue(props));
            var e = new Frozen.CompM167VadsGraphic { parent = turret, props = oldProps };
            try
            {
                foreach (Rot4 facing in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                    foreach (float aim in new[] { -720.25f, -90f, 0f, 37.5f, 180f, 359.999f, 720.25f })
                        for (int profile = 0; profile < 3; profile++)
                        {
                            turret.Rotation = facing; turret.Top.CurRotation = aim;
                            props.turretCenterOffset = oldProps.turretCenterOffset = new[] { UnityEngine.Vector2.zero, new UnityEngine.Vector2(.2f, -.35f), new UnityEngine.Vector2(-1.125f, .75f) }[profile];
                            props.drawSize = oldProps.drawSize = new[] { 6f, .01f, -2f }[profile];
                            props.casingRandomStartTicks = oldProps.casingRandomStartTicks = new[] { 0, 15, 60 }[profile];
                            props.casingDropSoundTick = oldProps.casingDropSoundTick = profile == 0 ? 0 : 44;
                            Vector(a.TurretCenterWorldOffset, e.TurretCenterWorldOffset);
                            GV drawPosition = new GV(10, 2, 5); Patch_TurretTop_DrawTurret_M167DirectionalOffset.Prefix(turret.Top, ref drawPosition);
                            Vector(drawPosition, new GV(10, 2, 5) + e.TurretCenterWorldOffset);
                            Casings(a).Clear(); Casings(e).Clear();
                            for (int seed = 0; seed < 3; seed++)
                            {
                                float ar, er;
                                Rand.PushState(cases * 3 + seed); try { a.NotifyShotFired(); ar = Rand.Value; } finally { Rand.PopState(); }
                                Rand.PushState(cases * 3 + seed); try { e.NotifyShotFired(); er = Rand.Value; } finally { Rand.PopState(); }
                                if (ar != er) throw new InvalidOperationException("M167 ejection RNG changed.");
                            }
                            Set(a, "barrelFrame", cases % 16); Set(e, "barrelFrame", cases % 16);
                            foreach (int age in new[] { -1, 0, 14, 15, 16, 43, 44, 45, 46 })
                            {
                                foreach (object comp in new object[] { a, e })
                                {
                                    Set(comp, "lastCasingDropSoundTick", -99999);
                                    foreach (object casing in Casings(comp)) { Set(casing, "spawnTick", Find.TickManager.TicksGame - age); Set(casing, "dropSoundPlayed", false); }
                                }
                                Compare(a, e); Draw(a, e); Drop(a, e, Find.TickManager.TicksGame); Drop(a, e, Find.TickManager.TicksGame); Compare(a, e);
                            }
                            sounds = new List<(SoundDef, IntVec3, float)>(); a.CompTick(); sounds.Clear(); e.CompTick(); sounds = null;
                            Compare(a, e); if (Casings(a).Count != 0) throw new InvalidOperationException("Expired M167 casings remain."); expirations += 3;
                            cases++;
                        }
                Casings(a).Clear(); Casings(e).Clear();
                for (int seed = 0; seed < 130; seed++)
                {
                    Rand.PushState(seed); try { a.NotifyShotFired(); } finally { Rand.PopState(); }
                    Rand.PushState(seed); try { e.NotifyShotFired(); } finally { Rand.PopState(); }
                }
                Compare(a, e); check(Casings(a).Count == 128, "M167 retains the newest 128 native casing states at its existing cap");
                Type casingType = typeof(CompM167VadsGraphic).GetNestedType("CasingVisual", BindingFlags.NonPublic);
                check(new[] { "origin", "initialVelocity", "randomVelocity" }.All(n => casingType.GetField(n).FieldType == typeof(NV)), "M167 casing motion and turret-center offset are native");
                check(soundCalls > 0, cases + " ejection/rotation/scale profiles and " + frames + " layer/casing draw callbacks preserve matrices, materials, RNG and native/game offset boundary");
                check(expirations > 0, soundCalls + " actual landing sound calls and " + expirations + " casing expirations match frozen cells, volume, flags and per-tick guard");
            }
            finally { sounds = null; draws = null; harmony.UnpatchAll(harmony.Id); turret.Destroy(); }
            File.WriteAllText(Path.Combine(output, "native-m167.json"), JsonSerializer.Serialize(new {
                Profiles = cases, DrawCallbacks = frames, SoundCalls = soundCalls, Expirations = expirations, Components = components,
                MaximumFloatError = maximumError, Baseline = "0b8f5ab3"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
