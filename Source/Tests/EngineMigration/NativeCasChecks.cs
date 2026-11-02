using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using NV = System.Numerics.Vector3;
using N2 = System.Numerics.Vector2;
using GV = UnityEngine.Vector3;
using G2 = UnityEngine.Vector2;
using GM = UnityEngine.Matrix4x4;
using Mat = UnityEngine.Material;

namespace Helodrace.Tests
{
    internal static class NativeCasChecks
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Visual = typeof(MapComponent_HelodCasSupport), OldVisual = typeof(Frozen.CasVisuals);
        private static List<(GM Matrix, Mat Material)> meshes;
        private static List<(GV Start, GV End, Mat Material, float Width)> lines;
        private static List<(ThingDef Def, Pawn Caller, GV Origin, IntVec3 Impact)> projectiles;
        private static List<(GV Position, float Size)> smoke;
        private static int components, profiles, ticks, aborts, scatter, guidance, bursts, rounds, releases, draws, lineSubmissions, meshSubmissions, helperCases, laserCases;
        private static float maximumError;

        private static object Get(object v, string name) => v.GetType().GetField(name, All).GetValue(v);
        private static void Set(object v, string name, object data) => v.GetType().GetField(name, All).SetValue(v, data);
        private static object Call(object v, string name, params object[] args) => v.GetType().GetMethod(name, All).Invoke(v, args);
        private static object Static(Type t, string name, params object[] args) => t.GetMethod(name, All).Invoke(null, args);
        private static object Clone(object v) => typeof(object).GetMethod("MemberwiseClone", All).Invoke(v, null);
        private static NV Native(GV v) => new NV(v.x, v.y, v.z);
        private static N2 Native(G2 v) => new N2(v.x, v.y);
        private static void Same(float a, float e)
        {
            components++;
            if (float.IsNaN(a) || float.IsNaN(e)) throw new InvalidOperationException("Unexpected CAS NaN.");
            if (a.Equals(e)) return;
            maximumError = Math.Max(maximumError, Math.Abs(a - e));
            if (!float.IsFinite(a) || !float.IsFinite(e) || Math.Abs(a - e) > 2e-5f * Math.Max(1f, Math.Abs(e)))
                throw new InvalidOperationException("CAS component differs: " + a + " != " + e);
        }
        private static void Value(object a, object e)
        {
            if (a is float f && e is float ef) Same(f, ef);
            else if (a is NV v && e is GV ev) { Same(v.X, ev.x); Same(v.Y, ev.y); Same(v.Z, ev.z); }
            else if (a is N2 n && e is G2 en) { Same(n.X, en.x); Same(n.Y, en.y); }
            else if (a is IExposable && e is IExposable && a.GetType().Namespace == "Helodrace") State(a, e);
            else if (a is IList list && e is IList old)
            {
                if (list.Count != old.Count) throw new InvalidOperationException("CAS list count changed.");
                for (int i = 0; i < list.Count; i++) Value(list[i], old[i]);
            }
            else if (!Equals(a, e)) throw new InvalidOperationException("CAS value changed: " + a + " != " + e);
        }
        private static void State(object a, object e)
        {
            foreach (FieldInfo field in a.GetType().GetFields(All | BindingFlags.DeclaredOnly).Where(f => !f.IsStatic))
            {
                try { Value(field.GetValue(a), Get(e, field.Name)); }
                catch (Exception error) { throw new InvalidOperationException(a.GetType().Name + "." + field.Name, error); }
            }
        }
        private static bool Mesh(GM __1, Mat __2) { if (meshes == null) return true; meshes.Add((__1, __2)); return false; }
        private static bool Line(GV __0, GV __1, Mat __2, float __3) { if (lines == null) return true; lines.Add((__0, __1, __2, __3)); return false; }
        private static bool Smoke(GV __0, float __2) { if (smoke == null) return true; smoke.Add((__0, __2)); return false; }
        private static bool Sound() => projectiles == null;
        private static bool Projectile(ThingDef def, Pawn caller, NV origin, IntVec3 impact, Map map)
        { projectiles.Add((def, caller, new GV(origin.X,origin.Y,origin.Z), impact)); return true; }
        private static void Material(Mat a, Mat e)
        {
            if (a.mainTexture != e.mainTexture || a.shader != e.shader) throw new InvalidOperationException("CAS texture/shader changed.");
            var ac = a.color; var ec = e.color; Same(ac.r, ec.r); Same(ac.g, ec.g); Same(ac.b, ec.b); Same(ac.a, ec.a);
        }
        private static void Draw(Action actual, Action expected)
        {
            meshes = new List<(GM, Mat)>(); lines = new List<(GV, GV, Mat, float)>(); actual();
            var am = meshes.ToArray(); var al = lines.ToArray(); meshes.Clear(); lines.Clear(); expected();
            if (am.Length != meshes.Count || al.Length != lines.Count) throw new InvalidOperationException("CAS draw submission counts changed.");
            for (int i = 0; i < meshes.Count; i++) { for (int c = 0; c < 16; c++) Same(am[i].Matrix[c], meshes[i].Matrix[c]); Material(am[i].Material, meshes[i].Material); }
            for (int i = 0; i < lines.Count; i++)
            { Value(Native(al[i].Start), lines[i].Start); Value(Native(al[i].End), lines[i].End); Same(al[i].Width, lines[i].Width); Material(al[i].Material, lines[i].Material); }
            meshSubmissions += am.Length; lineSubmissions += al.Length; draws++; meshes = null; lines = null;
        }
        private static void Frame(HelodCasStrike a, Frozen.HelodCasStrike e, int now)
        {
            Value(a.AircraftDrawPosition(now), e.AircraftDrawPosition(now)); Value(a.AircraftDrawDirection(now), e.AircraftDrawDirection(now));
            Same(a.AircraftDrawScale(now), e.AircraftDrawScale(now)); Same(a.StrafeAimDistance(now), e.StrafeAimDistance(now));
            if (a.ShouldDrawAircraft(now) != e.ShouldDrawAircraft(now)) throw new InvalidOperationException("CAS visibility changed.");
        }
        private static (HelodCasStrike, Frozen.HelodCasStrike) Pair(Map map, Pawn caller, IntVec3 entry, IntVec3 target,
            HelodCasAircraftKind aircraft, HelodCasAttackKind attack, HelodCasGuidanceMode mode, Thing guided)
        {
            var cells = new[] { target, target + new IntVec3(2, 0, -1) }; var things = new[] { guided, (Thing)null };
            var ap = new HelodCasAttackPlan(entry, target, mode, map, 8f, 3f, attack, aircraft, cells, things);
            var ep = new Frozen.HelodCasAttackPlan(entry, target, mode, map, 8f, 3f, attack, aircraft, cells, things);
            State(ap, ep); Value(ap.ApproachDirection, ep.ApproachDirection); Value(ap.StrafeStartPosition, ep.StrafeStartPosition); Value(ap.StrafeEndPosition, ep.StrafeEndPosition);
            Value(ap.CurrentStrafeStartPosition(map), ep.CurrentStrafeStartPosition(map));
            if (ap.StrafeStart != ep.StrafeStart || ap.StrafeEnd != ep.StrafeEnd) throw new InvalidOperationException("CAS corridor cell boundaries changed.");
            return (new HelodCasStrike(ap, caller, null, null, 100, 400, 2), new Frozen.HelodCasStrike(ep, caller, null, null, 100, 400, 2));
        }
        private static void Abort(HelodCasStrike live, Frozen.HelodCasStrike old, int now, Map map)
        {
            var a = (HelodCasStrike)Clone(live); var e = (Frozen.HelodCasStrike)Clone(old);
            bool ac = a.CanCancel(now, out string ar), ec = e.CanCancel(now, out string er);
            if (ac != ec || ar != er || a.BeginAbort(now, map) != e.BeginAbort(now, map)) throw new InvalidOperationException("CAS cancellation/abort decision changed.");
            State(a, e);
            foreach (int offset in new[] { -1, 0, 1, 5, 25, 100, 500, 2000 }) Frame(a, e, now + offset);
            foreach (int time in new[] { a.PhaseEndTick - 1, a.PhaseEndTick, a.PhaseEndTick + 1 })
            { if (a.TickAircraft(time, map) != e.TickAircraft(time, map)) throw new InvalidOperationException("CAS abort completion changed."); State(a, e); Frame(a, e, time); }
            aborts++;
        }
        private static void Fire(object aVisual, object eVisual, HelodCasStrike a, Frozen.HelodCasStrike e, int now)
        {
            var previous = HelodCasCombatBridge.ExternalProjectileLauncher;
            try
            {
                HelodCasCombatBridge.ExternalProjectileLauncher = Projectile;
                projectiles = new List<(ThingDef, Pawn, GV, IntVec3)>(); smoke = new List<(GV, float)>(); float ar, er;
                Rand.PushState(170700 + bursts); try { Call(aVisual, "FireStrafeBurst", a, now); ar = Rand.Value; } finally { Rand.PopState(); }
                var shots = projectiles.ToArray(); var clouds = smoke.ToArray(); projectiles.Clear(); smoke.Clear();
                Rand.PushState(170700 + bursts); try { Call(eVisual, "FireStrafeBurst", e, now); er = Rand.Value; } finally { Rand.PopState(); }
                if (ar != er || shots.Length != projectiles.Count || clouds.Length != smoke.Count) throw new InvalidOperationException("CAS strafe submission/RNG count changed.");
                for (int i = 0; i < shots.Length; i++)
                { Value(Native(shots[i].Origin), projectiles[i].Origin); if (shots[i].Def != projectiles[i].Def || shots[i].Caller != projectiles[i].Caller || shots[i].Impact != projectiles[i].Impact) throw new InvalidOperationException("CAS external launch contract changed."); }
                for (int i = 0; i < clouds.Length; i++) { Value(Native(clouds[i].Position), smoke[i].Position); Same(clouds[i].Size, smoke[i].Size); }
                rounds += shots.Length; bursts++;
            }
            finally { projectiles = null; smoke = null; HelodCasCombatBridge.ExternalProjectileLauncher = previous; }
        }
        private static void Release(object av, object ev, HelodCasStrike a, Frozen.HelodCasStrike e)
        {
            ((IList)Get(av, "fallingBombs")).Clear(); ((IList)Get(ev, "fallingBombs")).Clear();
            float ar, er;
            Rand.PushState(170600 + releases); try { Call(av, "ReleaseOrdnance", a); ar = Rand.Value; } finally { Rand.PopState(); }
            Rand.PushState(170600 + releases); try { Call(ev, "ReleaseOrdnance", e); er = Rand.Value; } finally { Rand.PopState(); }
            Value(Get(av, "fallingBombs"), Get(ev, "fallingBombs")); if (ar != er) throw new InvalidOperationException("CAS release RNG order changed."); releases++;
        }
        private static void Helpers(Map map)
        {
            foreach (GV start in new[] { GV.zero, new GV(-1, .25f, -1), map.Center.ToVector3Shifted(), new GV(map.Size.x, 1, map.Size.z) })
                foreach (G2 direction in new[] { G2.zero, new G2(.00001f, 1), new G2(.0001f, -1), new G2(1, 1), new G2(-3, 2), new G2(0, -1) })
                    foreach (float angle in new[] { -MathF.PI, -.0001f, -0f, .0001f, MathF.PI * .5f, MathF.PI })
                        foreach (float radius in new[] { 0f, .001f, 8f, 40f })
                        {
                            Value(Static(typeof(HelodCasStrike), "RotateDirection", Native(direction), angle), Static(typeof(Frozen.HelodCasStrike), "RotateDirection", direction, angle));
                            Value(Static(typeof(HelodCasStrike), "TurnArcPosition", Native(start), Native(direction), angle, radius), Static(typeof(Frozen.HelodCasStrike), "TurnArcPosition", start, direction, angle, radius));
                            Same((float)Static(typeof(HelodCasStrike), "DistanceToMapExit", Native(start), Native(direction), map), (float)Static(typeof(Frozen.HelodCasStrike), "DistanceToMapExit", start, direction, map));
                            if (!Equals(Static(typeof(HelodCasStrike), "ChooseTurnSign", Native(start), Native(direction), angle, radius, map), Static(typeof(Frozen.HelodCasStrike), "ChooseTurnSign", start, direction, angle, radius, map))) throw new InvalidOperationException("CAS shortest-exit turn decision changed.");
                            helperCases++;
                        }
        }
        internal static void Create(string output, Action<bool, string> check)
        {
            OriginContract(check);
            var harmony = new Harmony("Helodrace.Tests.NativeCas");
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Graphics), "DrawMesh", new[] { typeof(UnityEngine.Mesh), typeof(GM), typeof(Mat), typeof(int) }), prefix: new HarmonyMethod(typeof(NativeCasChecks), nameof(Mesh)));
            harmony.Patch(AccessTools.Method(typeof(GenDraw), "DrawLineBetween", new[] { typeof(GV), typeof(GV), typeof(Mat), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeCasChecks), nameof(Line)));
            harmony.Patch(AccessTools.Method(typeof(FleckMaker), "ThrowSmoke", new[] { typeof(GV), typeof(Map), typeof(float) }), prefix: new HarmonyMethod(typeof(NativeCasChecks), nameof(Smoke)));
            harmony.Patch(AccessTools.Method(typeof(SoundStarter), "PlayOneShot", new[] { typeof(SoundDef), typeof(SoundInfo) }), prefix: new HarmonyMethod(typeof(NativeCasChecks), nameof(Sound)));
            Map map = Find.CurrentMap; var av = map.GetComponent<MapComponent_HelodCasSupport>(); var ev = new Frozen.CasVisuals(map);
            int originalTick = Find.TickManager.TicksGame;
            Pawn caller = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); caller.health.RemoveAllHediffs(); GenSpawn.Spawn(caller, map.Center, map);
            Thing guided = ThingMaker.MakeThing(ThingDefOf.WoodLog); GenSpawn.Spawn(guided, map.Center + new IntVec3(4, 0, 6), map);
            try
            {
                Helpers(map);
                foreach (var aircraft in Enum.GetValues<HelodCasAircraftKind>())
                    foreach (var attack in Enum.GetValues<HelodCasAttackKind>().Where(k => aircraft == HelodCasAircraftKind.P47 ? k is HelodCasAttackKind.Bombing or HelodCasAttackKind.Strafing : k != HelodCasAttackKind.Bombing))
                        foreach (var mode in Enum.GetValues<HelodCasGuidanceMode>())
                            foreach (IntVec3 entry in new[] { new IntVec3(2, 0, map.Center.z), new IntVec3(map.Size.x - 3, 0, map.Center.z), new IntVec3(map.Center.x, 0, 2), new IntVec3(2, 0, 2) })
                            {
                                var (a, e) = Pair(map, caller, entry, map.Center, aircraft, attack, mode, guided);
                                foreach (int time in new[] { -100, 100, 250, 400, 2000 }) Frame(a, e, time);
                                Set(a, "runState", HelodCasRunState.GoAround); Set(e, "runState", HelodCasRunState.GoAround);
                                Set(a, "goAroundStartTick", 100); Set(e, "goAroundStartTick", 100);
                                Set(a, "nextGuidanceAttemptTick", 2400); Set(e, "nextGuidanceAttemptTick", 2400);
                                foreach (int sign in new[] { -1, 1 })
                                { Set(a, "goAroundTurnSign", sign); Set(e, "goAroundTurnSign", sign); foreach (int time in new[] { 100, 101, 500, 1000, 1500, 2100, 2400, 2500 }) Frame(a, e, time); }
                                for (int seed = 0; seed < 8; seed++)
                                {
                                    var ga = (HelodCasStrike)Clone(a); var ge = (Frozen.HelodCasStrike)Clone(e); bool ar, er; float an, en;
                                    Rand.PushState(seed + 170500); try { ar = ga.TryGuidance(map); an = Rand.Value; } finally { Rand.PopState(); }
                                    Rand.PushState(seed + 170500); try { er = ge.TryGuidance(map); en = Rand.Value; } finally { Rand.PopState(); }
                                    if (ar != er || an != en) throw new InvalidOperationException("CAS guidance/RNG transition changed."); State(ga, ge); guidance++;
                                }
                                Set(a, "runState", HelodCasRunState.Attacking); Set(e, "runState", HelodCasRunState.Attacking);
                                Call(a, "BeginAircraft", 1, 1000); Call(e, "BeginAircraft", 1, 1000);
                                var phases = new HashSet<HelodCasAircraftPhase>(); bool complete = false;
                                for (int now = 1000; now < 10000; now++)
                                {
                                    Find.TickManager.DebugSetTicksGame(now);
                                    var ae = a.TickAircraft(now, map); var ee = e.TickAircraft(now, map);
                                    if (ae != ee) throw new InvalidOperationException("CAS tick event changed."); State(a, e); Frame(a, e, now); ticks++;
                                    if (phases.Add(a.AircraftPhase)) Abort(a, e, now, map);
                                    if (now % 17 == 0) Draw(() => Static(Visual, "DrawAircraft", a, now), () => Static(OldVisual, "DrawAircraft", e, now));
                                    if (ae == HelodCasAircraftTickEvent.StrafeBurst) { Fire(av, ev, a, e, now); State(a, e); }
                                    if (ae == HelodCasAircraftTickEvent.ReleaseBombPair) Release(av, ev, a, e);
                                    if (ae == HelodCasAircraftTickEvent.Complete) { complete = true; break; }
                                }
                                if (!complete) throw new InvalidOperationException("CAS flight did not complete.");
                                for (int index = 0; index < 16; index++)
                                {
                                    IntVec3 ac, ec; float ar, er;
                                    Rand.PushState(170400 + index); try { ac = a.NextImpactCell(map, index); ar = Rand.Value; } finally { Rand.PopState(); }
                                    Rand.PushState(170400 + index); try { ec = e.NextImpactCell(map, index); er = Rand.Value; } finally { Rand.PopState(); }
                                    if (ac != ec || ar != er) throw new InvalidOperationException("CAS impact cell/order changed."); scatter++;
                                }
                                Draw(() => Static(Visual, "DrawScatterEllipse", map.Center, entry, 8f, 3f), () => Static(OldVisual, "DrawScatterEllipse", map.Center, entry, 8f, 3f));
                                Draw(() => Static(Visual, "DrawStrafeCorridor", entry, map.Center), () => Static(OldVisual, "DrawStrafeCorridor", entry, map.Center));
                                profiles++;
                            }
                VisualChecks(map, caller, guided);
                check(profiles == 96 && helperCases == 576, "96 legal aircraft/attack/guidance/route profiles and 576 turn/map-exit helper cases match frozen game-vector math");
                check(ticks > 0 && aborts > 0 && scatter == profiles * 16 && guidance == profiles * 8, "sequential flight/abort state, scatter cells and guidance transitions preserve event timing and exact Verse RNG order");
                check(bursts > 0 && rounds > 0 && releases > 0, "actual CAS release/strafe methods preserve shared munition state and current external launch contract");
                check(meshSubmissions > 0 && lineSubmissions > 0, "native aircraft/munition/designator/beam/glow/preview geometry and material channels match engine submission inputs");
            }
            finally { Find.TickManager.DebugSetTicksGame(originalTick); ((IList)Get(av, "fallingBombs")).Clear(); caller.Destroy(); guided.Destroy(); meshes = null; lines = null; projectiles = null; smoke = null; harmony.UnpatchAll(harmony.Id); }
            PrepareSave(map, output, check);
            File.WriteAllText(Path.Combine(output, "native-cas.json"), JsonSerializer.Serialize(new {
                Profiles = profiles, HelperCases = helperCases, FlightTicks = ticks, AbortCases = aborts, ScatterCases = scatter, GuidanceCases = guidance, LaserCases = laserCases,
                StrafeBursts = bursts, StrafeRounds = rounds, Releases = releases, DrawCalls = draws, MeshSubmissions = meshSubmissions,
                LineSubmissions = lineSubmissions, Components = components, MaximumFloatError = maximumError, Baseline = "97ab8f7b"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        private static void OriginContract(Action<bool,string> check)
        {
            check(typeof(HelodCasCombatBridge.ProjectileLaunchHandler).GetMethod("Invoke").GetParameters()[2].ParameterType==typeof(NV)
                && typeof(HelodCasCombatBridge).GetMethod("TryLaunchProjectile").GetParameters()[2].ParameterType==typeof(NV),
                "CAS extension and dispatch expose a native origin without a game-vector overload");
            var saved=HelodCasCombatBridge.ExternalProjectileLauncher;
            try
            {
                var origin=new NV(-0f,float.Epsilon,BitConverter.Int32BitsToSingle(unchecked((int)0x7FC12345)));
                var impact=new IntVec3(12,0,34); Map map=Find.CurrentMap; int calls=0; bool forwarded=false;
                HelodCasCombatBridge.ExternalProjectileLauncher=null;
                check(!HelodCasCombatBridge.TryLaunchProjectile(null,null,origin,impact,map),"missing CAS handler still delegates to the caller's vanilla fallback");
                HelodCasCombatBridge.ExternalProjectileLauncher=(ThingDef def,Pawn caller,NV point,IntVec3 cell,Map host) =>
                {
                    calls++;
                    forwarded=def==null && caller==null && host==map && cell==impact
                        && BitConverter.SingleToInt32Bits(point.X)==BitConverter.SingleToInt32Bits(origin.X)
                        && BitConverter.SingleToInt32Bits(point.Y)==BitConverter.SingleToInt32Bits(origin.Y)
                        && BitConverter.SingleToInt32Bits(point.Z)==BitConverter.SingleToInt32Bits(origin.Z);
                    return false;
                };
                check(!HelodCasCombatBridge.TryLaunchProjectile(null,null,origin,impact,map) && calls==1 && forwarded,
                    "refusing CAS handler preserves native component bits and fallback result");
            }
            finally { HelodCasCombatBridge.ExternalProjectileLauncher=saved; }
        }
        private static void VisualChecks(Map map, Pawn caller, Thing guided)
        {
            var (a, e) = Pair(map, caller, map.Center + new IntVec3(-25, 0, 11), map.Center, HelodCasAircraftKind.A10C, HelodCasAttackKind.Hydra70, HelodCasGuidanceMode.Laser, guided);
            foreach (HelodCasAttackKind kind in Enum.GetValues<HelodCasAttackKind>())
                foreach (G2 direction in new[] { G2.zero, new G2(1, 0), new G2(.125f, -1) })
                {
                    GV release = map.Center.ToVector3Shifted() + new GV(.125f, 0, -.25f);
                    var ab = new HelodCasFallingBomb(Native(release), guided.Position, caller, 100, 200, Native(direction), kind, guided);
                    var eb = new Frozen.HelodCasFallingBomb(release, guided.Position, caller, 100, 200, direction, kind, guided);
                    foreach (int now in new[] { 99, 100, 101, 150, 199, 200, 201 })
                    { State(ab, eb); Value(ab.FlightDirection, eb.FlightDirection); Draw(() => Static(Visual, "DrawFallingBomb", ab, now), () => Static(OldVisual, "DrawFallingBomb", eb, now)); }
                }
            foreach (Rot4 facing in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                caller.Rotation = facing;
                foreach (int now in new[] { 0, 1, 25, 250, 2000 })
                    Value(Static(Visual, "IzlidSkySignalEnd", a, caller, now), Static(OldVisual, "IzlidSkySignalEnd", e, caller, now));
                foreach (float intensity in new[] { -1f, 0f, .125f, .5f, 1f, 2f })
                {
                    NV origin = Native(caller.DrawPos), target = Native(guided.DrawPos);
                    Draw(() => Static(Visual, "DrawIzlidSkyBeam", origin, target), () => Static(OldVisual, "DrawIzlidSkyBeam", caller.DrawPos, guided.DrawPos));
                    Draw(() => Static(Visual, "DrawIzlidBeam", origin, target, intensity), () => Static(OldVisual, "DrawIzlidBeam", caller.DrawPos, guided.DrawPos, intensity));
                    Draw(() => Static(Visual, "DrawLaserAimGlow", guided.Position, intensity, 1.05f, .35f), () => Static(OldVisual, "DrawLaserAimGlow", guided.Position, intensity, 1.05f, .35f));
                }
                foreach (var designator in DefDatabase<ThingDef>.AllDefsListForReading.Select(d => d.GetModExtension<HelodCasLaserDesignatorExtension>()).Where(d => d != null))
                {
                    Set(a, "fixedGuidanceDevicePositionInitialized", false); Set(e, "fixedGuidanceDevicePositionInitialized", false);
                    Value(a.FixedGuidanceDevicePosition(caller, designator), e.FixedGuidanceDevicePosition(caller, designator));
                    caller.Rotation = caller.Rotation.Rotated(RotationDirection.Clockwise);
                    Value(a.FixedGuidanceDevicePosition(caller, designator), e.FixedGuidanceDevicePosition(caller, designator));
                }
            }
            map.fogGrid.Unfog(caller.Position);
            var av = map.GetComponent<MapComponent_HelodCasSupport>(); var ev = new Frozen.CasVisuals(map);
            foreach (ThingDef definition in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.GetModExtension<HelodCasLaserDesignatorExtension>() != null))
            {
                Thing device = ThingMaker.MakeThing(definition); caller.inventory.innerContainer.TryAdd(device);
                try
                {
                    if (!HelodCasSupportUtility.TryGetLaserDesignatorOperator(caller, map, caller.Position, out Pawn actualOperator, out _) || actualOperator != caller)
                        throw new InvalidOperationException("Controlled designator operator unavailable: " + definition.defName);
                    foreach (Rot4 facing in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                    {
                        caller.Rotation = facing;
                        Set(a, "fixedGuidanceDevicePositionInitialized", false); Set(e, "fixedGuidanceDevicePositionInitialized", false);
                        foreach (int now in new[] { 0, 100, 250, 400, 800 })
                        {
                            int before = meshSubmissions;
                            Draw(() => Call(av, "DrawLaserDesignator", a, now), () => Call(ev, "DrawLaserDesignator", e, now));
                            if (meshSubmissions != before + 1) throw new InvalidOperationException("Designator body draw was not exercised.");
                            Draw(() => Call(av, "DrawLaserGuidanceEffect", a, now), () => Call(ev, "DrawLaserGuidanceEffect", e, now));
                        }
                    }
                    var (la, le) = Pair(map, caller, map.Center + new IntVec3(-25, 0, 11), caller.Position, HelodCasAircraftKind.A10C, HelodCasAttackKind.GBU54, HelodCasGuidanceMode.Laser, guided);
                    for (int seed = 0; seed < 16; seed++)
                    {
                        IntVec3 ac, ec; bool ag, eg; float ar, er;
                        Rand.PushState(seed + 170300); try { ac = la.NextImpactCell(map); ag = la.TryGuidance(map); ar = Rand.Value; } finally { Rand.PopState(); }
                        Rand.PushState(seed + 170300); try { ec = le.NextImpactCell(map); eg = le.TryGuidance(map); er = Rand.Value; } finally { Rand.PopState(); }
                        if (ac != ec || ag != eg || ar != er) throw new InvalidOperationException("Active laser scatter/guidance/RNG changed."); State(la, le); laserCases++;
                    }
                }
                finally { caller.inventory.innerContainer.Remove(device); device.Destroy(); }
            }
        }
        private static string Describe(object v) => v switch {
            null => "null", float f => "f:" + BitConverter.SingleToInt32Bits(f),
            NV n => "v3:" + Describe(n.X) + "," + Describe(n.Y) + "," + Describe(n.Z),
            N2 n => "v2:" + Describe(n.X) + "," + Describe(n.Y),
            ILoadReferenceable reference => "ref:" + reference.GetUniqueLoadID(),
            IList list => "[" + string.Join(";", list.Cast<object>().Select(Describe)) + "]",
            IExposable state => "{" + string.Join(";", Fields(state).Select(f => f.Name + "=" + Describe(f.GetValue(state)))) + "}",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture)
        };
        private static FieldInfo[] Fields(object v) => v.GetType().GetFields(All | BindingFlags.DeclaredOnly).Where(f => !f.IsStatic).OrderBy(f => f.Name).ToArray();
        private static string Witness(object v) => Describe(v);
        private static void PrepareSave(Map map, string output, Action<bool, string> check)
        {
            Pawn caller = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); GenSpawn.Spawn(caller, map.Center, map);
            var support = map.GetComponent<MapComponent_HelodCasSupport>(); var strikes = (IList)Get(support, "strikes"); var bombs = (IList)Get(support, "fallingBombs"); strikes.Clear(); bombs.Clear();
            foreach (HelodCasAircraftPhase phase in Enum.GetValues<HelodCasAircraftPhase>())
            {
                var (strike, _) = Pair(map, caller, map.Center + new IntVec3(-30, 0, 0), map.Center, HelodCasAircraftKind.A10C, HelodCasAttackKind.Strafing, HelodCasGuidanceMode.TalkOn, caller);
                Set(strike, "aircraftPhase", phase); Set(strike, "runState", HelodCasRunState.Attacking);
                foreach (FieldInfo field in Fields(strike))
                {
                    if (field.FieldType == typeof(NV)) field.SetValue(strike, new NV(MathF.BitIncrement(map.Center.x + .5f), -0f, float.Epsilon));
                    if (field.FieldType == typeof(N2)) field.SetValue(strike, new N2(MathF.BitIncrement(.125f), -0f));
                }
                Set(strike, "fixedGuidanceDevicePositionInitialized", true); strikes.Add(strike);
            }
            foreach (var attack in Enum.GetValues<HelodCasAttackKind>()) bombs.Add(new HelodCasFallingBomb(new NV(MathF.BitIncrement(12.125f), 0, -.25f), caller.Position, caller, 100, 200, new N2(.125f, 1), attack, caller));
            File.WriteAllText(Path.Combine(output, "cas-states.json"), JsonSerializer.Serialize(new[] { strikes.Cast<object>().Select(Witness).ToArray(), bombs.Cast<object>().Select(Witness).ToArray() }));
            check(strikes.Count == 10 && bombs.Count == 7, "all ten aircraft phases and seven munition kinds prepare native vector/cell/scalar/reference save witnesses");
        }
        internal static void Verify(string output, Action<bool, string> check)
        {
            var expected = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(Path.Combine(output, "cas-states.json"))); var support = Find.CurrentMap.GetComponent<MapComponent_HelodCasSupport>();
            foreach (var pair in new[] { ("strikes", 0), ("fallingBombs", 1) })
                if (!((IList)Get(support, pair.Item1)).Cast<object>().Select(Witness).SequenceEqual(expected[pair.Item2])) throw new InvalidOperationException("CAS native state/reference bits did not round-trip before first tick: " + pair.Item1);
            var nodes = XDocument.Load(GenFilePaths.FilePathForSavedGame("CasNativeState17")).Descendants("helodCasSupportStrikes").Elements("li").ToArray();
            var vectors = Fields(new HelodCasStrike()).Where(f => f.FieldType == typeof(NV) || f.FieldType == typeof(N2)).Select(f => f.Name).ToArray();
            check(nodes.Length == 10 && vectors.Length == 8 && nodes.All(n => vectors.All(f => n.Element(f)?.Element("x") != null)), "eight native CAS vectors use explicit scalar nodes across all ten phase saves without an older Main vector reader");
            check(true, "CAS plans, phases, munitions and shared caller/target references preserve exact state before the first simulation tick");
        }
    }
}
