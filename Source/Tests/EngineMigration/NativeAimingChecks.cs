using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using NativeVector = System.Numerics.Vector3;
using GameVector = UnityEngine.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeAimingChecks
    {
        private delegate void ResolvePose(Thing equipment, Pawn pawn, float aim,
            out NativeVector position, out float bodyAngle, out bool flipped, out bool recoilActive);
        private delegate void ResolveRecoil(Thing equipment, float aim, out NativeVector position, out float angle);

        internal static void Run(string output, Action<bool, string> check)
        {
            Assembly product = typeof(ModularWeaponRecoilUtility).Assembly;
            Type poses = product.GetType("Helodrace.ModernWar.ModularWeaponAimingPoseUtility", true);
            const BindingFlags allStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var resolve = (ResolvePose)poses.GetMethod("Resolve", allStatic, null, new[] {
                typeof(Thing), typeof(Pawn), typeof(float), typeof(NativeVector).MakeByRefType(),
                typeof(float).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType()
            }, null).CreateDelegate(typeof(ResolvePose));
            var record = (Action<Thing, NativeVector, float, float, bool, bool>)poses.GetMethod("Record", allStatic)
                .CreateDelegate(typeof(Action<Thing, NativeVector, float, float, bool, bool>));
            var rotate = (Func<NativeVector, float, NativeVector>)product.GetType("Helodrace.SpatialMath", true)
                .GetMethod("RotateAroundY", allStatic).CreateDelegate(typeof(Func<NativeVector, float, NativeVector>));
            var recoil = (ResolveRecoil)typeof(ModularWeaponRecoilUtility).GetMethod("ResolveNative", allStatic)
                .CreateDelegate(typeof(ResolveRecoil));
            var cache = (IDictionary)poses.GetField("poses", allStatic).GetValue(null);
            var savedResolver = ModularWeaponRecoilUtility.ExternalResolver;
            var equipment = new Thing { def = new ThingDef(), thingIDNumber = -1707001 };
            Pawn pawn = null;
            int components = 0, flags = 0, cached = 0;
            void Near(float actual, float expected)
            {
                components++;
                if (!float.IsFinite(actual) || !float.IsFinite(expected)
                    || Math.Abs(actual - expected) > 2e-5f * Math.Max(1f, Math.Abs(expected)))
                    throw new InvalidOperationException("Native aiming component differs: " + actual + " != " + expected);
            }
            void SameVector(NativeVector actual, GameVector expected)
            { Near(actual.X, expected.x); Near(actual.Y, expected.y); Near(actual.Z, expected.z); }
            var angles = new[] { -720.25f, -1f, 0f, 20f, 90f, 159.999f, 160f,
                200f, 200.001f, 270f, 339.999f, 340f, 360f, 720.25f };
            try
            {
                foreach (float aim in angles)
                    foreach (var point in new[] { NativeVector.Zero, NativeVector.UnitX, NativeVector.UnitY,
                        NativeVector.UnitZ, new NativeVector(-2, .7f, 3), new NativeVector(100, -5, -.01f) })
                        SameVector(rotate(point, aim), UnityEngine.Quaternion.AngleAxis(aim, GameVector.up)
                            * new GameVector(point.X, point.Y, point.Z));

                pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(pawn, Find.CurrentMap.Center, Find.CurrentMap);
                var gameRecoil = GameVector.zero;
                float recoilAngle = 0;
                ModularWeaponRecoilUtility.ExternalResolver = (Thing item, float aim, out GameVector offset, out float angle) =>
                { offset = gameRecoil; angle = recoilAngle; return true; };
                foreach (Pawn wielder in new[] { null, pawn })
                    foreach (float distance in new[] { -.4f, 0f, .7f })
                        foreach (float equippedAngle in new[] { -15f, 0f, 23f })
                            foreach (float aim in angles)
                                foreach (var kick in new[] { GameVector.zero, new GameVector(.0001f, 0, 0),
                                    new GameVector(.001f, 0, 0), new GameVector(.02f, -.01f, .03f) })
                                    foreach (float kickAngle in new[] { 0f, .001f, -2f })
                                    {
                                        equipment.def.equippedDistanceOffset = distance;
                                        equipment.def.equippedAngleOffset = equippedAngle;
                                        gameRecoil = kick; recoilAngle = kickAngle;
                                        // Frozen pre-conversion formula from f2d09c7e; use the actual game's
                                        // quaternion, RotatedBy and vector semantics as the independent path.
                                        float factor = wielder?.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;
                                        GameVector expected = (wielder?.DrawPos ?? GameVector.zero)
                                            + new GameVector(0, 0, .4f + distance).RotatedBy(aim) * factor;
                                        bool flip = aim > 200f && aim < 340f;
                                        float body = aim - 90f;
                                        if (flip) { body -= 180f; body -= equippedAngle; }
                                        else body += equippedAngle;
                                        expected += kick;
                                        body += flip ? -kickAngle : kickAngle;
                                        body %= 360f;
                                        resolve(equipment, wielder, aim, out var position, out float actualBody,
                                            out bool actualFlip, out bool active);
                                        SameVector(position, expected); Near(actualBody, body);
                                        flags++;
                                        if (actualFlip != flip || active != (kick.sqrMagnitude > .000001f || Math.Abs(kickAngle) > .001f))
                                            throw new InvalidOperationException("Aiming flip/recoil threshold changed.");
                                    }
                gameRecoil = GameVector.zero; recoilAngle = 0;
                foreach (int age in new[] { 0, 3, 4 })
                    foreach (float delta in new[] { 0f, 2f, 3f, 3.001f })
                    {
                        var recorded = new NativeVector(30, .2f, 40);
                        record(equipment, recorded, 359f, 172f, true, true);
                        object entry = cache[equipment.thingIDNumber];
                        entry.GetType().GetField("tick").SetValue(entry, Find.TickManager.TicksGame - age);
                        resolve(equipment, null, 359f + delta, out var position, out float body, out bool flip, out bool active);
                        bool shouldUseCache = age <= 3 && delta <= 3f;
                        if ((position == recorded) != shouldUseCache
                            || (shouldUseCache && (body != 172f || !flip || !active)))
                            throw new InvalidOperationException("Cached aiming age/angular tolerance changed.");
                        cached++;
                        cache.Remove(equipment.thingIDNumber);
                    }
                // Resolver refusal must discard all its output before the vanilla path.
                ModularWeaponRecoilUtility.ExternalResolver = (Thing item, float aim, out GameVector offset, out float angle) =>
                { offset = new GameVector(5, 6, 7); angle = 12; return false; };
                recoil(null, 25, out var discarded, out float discardedAngle);
                check(discarded == NativeVector.Zero && discardedAngle == 0, "refused external recoil is reset before fallback");
                ModularWeaponRecoilUtility.Resolve(null, 25, out GameVector publicOffset, out float publicAngle);
                check(publicOffset == GameVector.zero && publicAngle == 0, "public game recoil boundary preserves fallback behavior");
                check(cache.GetType().GetGenericArguments()[1].GetField("drawLoc").FieldType == typeof(NativeVector),
                    "aiming cache owns native vectors rather than compatibility structs");
                check(true, components + " aiming/rotation components and " + flags + " flip/recoil thresholds match frozen game calculations");
                check(true, cached + " cache age/angular boundary combinations pass");
                File.WriteAllText(Path.Combine(output, "native-aiming.json"), JsonSerializer.Serialize(new {
                    Components = components, FlagCases = flags, CacheCases = cached,
                    BaselineRevision = "f2d09c7e", NativeCachedPose = true
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                ModularWeaponRecoilUtility.ExternalResolver = savedResolver;
                cache.Remove(equipment.thingIDNumber);
                pawn?.Destroy(DestroyMode.Vanish);
            }
        }
    }
}
