// Frozen from 97ab8f7b; original game-vector CAS math. Shared scalar utility/contracts remain in Main.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static Helodrace.HelodCasSupportUtility;
namespace Helodrace.Tests.Frozen
{
    internal sealed class CasVisuals
    {
        private readonly List<HelodCasFallingBomb> fallingBombs = new List<HelodCasFallingBomb>();
        private readonly Map map;
        internal CasVisuals(Map map) { this.map = map; }
        private static readonly Dictionary<HelodCasAircraftKind, Material> aircraftMaterials
            = new Dictionary<HelodCasAircraftKind, Material>();
        private static readonly Dictionary<string, Material> munitionMaterials
            = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Material> designatorMaterials
            = new Dictionary<string, Material>();
        private static readonly Material izlidBeamOuterMaterial
            = SolidColorMaterials.SimpleSolidColorMaterial(
                new Color(0.18f, 1f, 0.42f, 0.16f), false);
        private static readonly Material izlidBeamCoreMaterial
            = SolidColorMaterials.SimpleSolidColorMaterial(
                new Color(0.58f, 1f, 0.72f, 0.52f), false);
        private static readonly Material[] izlidSkyBeamOuterMaterials
            = CreateSkyFadeMaterials(new Color(0.18f, 1f, 0.42f), 0.20f,
                0.008f);
        private static readonly Material[] izlidSkyBeamCoreMaterials
            = CreateSkyFadeMaterials(new Color(0.58f, 1f, 0.72f), 0.58f,
                0.012f);
        private static readonly Material[] pulsingBeamOuterMaterials
            = CreatePulsingSolidMaterials(new Color(0.18f, 1f, 0.42f), 0f,
                0.16f);
        private static readonly Material[] pulsingBeamCoreMaterials
            = CreatePulsingSolidMaterials(new Color(0.58f, 1f, 0.72f), 0f,
                0.52f);
        private static readonly Material[] laserAimGlowMaterials
            = CreatePulsingGlowMaterials();

        private static void DrawScatterEllipse(IntVec3 center, IntVec3 entry,
            float majorRadius, float minorRadius)
        {
            Vector2 direction = new Vector2(center.x - entry.x, center.z - entry.z).normalized;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            Vector3 previous = Vector3.zero;
            const int segments = 32;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector2 offset = direction * (Mathf.Cos(angle) * majorRadius)
                    + lateral * (Mathf.Sin(angle) * minorRadius);
                Vector3 point = new Vector3(center.x + 0.5f + offset.x,
                    AltitudeLayer.MetaOverlays.AltitudeFor(), center.z + 0.5f + offset.y);
                if (i > 0)
                {
                    GenDraw.DrawLineBetween(previous, point, SimpleColor.White);
                }
                previous = point;
            }
        }

        private static void DrawStrafeCorridor(IntVec3 entry, IntVec3 center)
        {
            Vector2 direction = new Vector2(center.x - entry.x,
                center.z - entry.z).normalized;
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }
            Vector2 lateral = new Vector2(-direction.y, direction.x)
                * (HelodCasSupportUtility.StrafeWidth * 0.5f);
            Vector3 designatedCenter = center.ToVector3Shifted();
            Vector3 halfLength = new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.StrafeLength * 0.5f);
            Vector3 startCenter = designatedCenter - halfLength;
            Vector3 endCenter = designatedCenter + halfLength;
            Vector3 lateral3 = new Vector3(lateral.x, 0f, lateral.y);
            GenDraw.DrawLineBetween(startCenter - lateral3, startCenter + lateral3,
                SimpleColor.White);
            GenDraw.DrawLineBetween(startCenter - lateral3, endCenter - lateral3,
                SimpleColor.White);
            GenDraw.DrawLineBetween(startCenter + lateral3, endCenter + lateral3,
                SimpleColor.White);
            GenDraw.DrawLineBetween(endCenter - lateral3, endCenter + lateral3,
                SimpleColor.White);
        }

        private void ReleaseOrdnance(HelodCasStrike strike)
        {
            int now = Find.TickManager.TicksGame;
            Vector3 releasePosition = strike.AircraftDrawPosition(now);
            int count = strike.Plan.MunitionCount;
            for (int munition = 0; munition < count; munition++)
            {
                IntVec3 impact = strike.NextImpactCell(map, munition);
                if (!impact.InBounds(map))
                {
                    continue;
                }
                int releaseTick = now + munition
                    * HelodCasSupportUtility.MunitionReleaseInterval(
                        strike.Plan.AttackKind);
                Vector3 scheduledReleasePosition = munition == 0 ? releasePosition
                    : strike.AircraftDrawPosition(releaseTick);
                Thing guidedTarget = HelodCasSupportUtility.IsGuidedMissile(
                    strike.Plan.AttackKind)
                    ? strike.Plan.DesignatedTargetThing(munition) : null;
                fallingBombs.Add(new HelodCasFallingBomb(scheduledReleasePosition, impact,
                    strike.Caller, releaseTick,
                    releaseTick + HelodCasSupportUtility.BombFallTicks,
                    strike.Plan.ApproachDirection, strike.Plan.AttackKind,
                    guidedTarget));
            }
        }

        private void FireStrafeBurst(HelodCasStrike strike, int now)
        {
            bool isA10C = strike.Plan.AircraftKind == HelodCasAircraftKind.A10C;
            Vector3 origin = strike.AircraftDrawPosition(now);
            Vector2 direction = strike.Plan.ApproachDirection;
            if (isA10C)
            {
                origin += new Vector3(direction.x, 0f, direction.y)
                    * HelodCasSupportUtility.A10CMuzzleForwardOffset;
            }
            IntVec3 originCell = origin.ToIntVec3();
            if (!originCell.InBounds(map))
            {
                return;
            }
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(
                HelodCasSupportUtility.StrafeSoundDefName);
            if (!isA10C)
            {
                sound?.PlayOneShot(new TargetInfo(originCell, map));
            }
            if (isA10C)
            {
                FleckMaker.ThrowSmoke(origin, map, 0.42f);
            }

            Vector2 lateral = new Vector2(-direction.y, direction.x);
            float aimDistance = isA10C ? 0f : strike.StrafeAimDistance(now);
            int roundsToFire = strike.ConsumePendingStrafeRounds();
            for (int round = 0; round < roundsToFire; round++)
            {
                int a10CShotIndex = -1;
                string projectileDefName = isA10C
                    ? (strike.NextA10CStrafeRoundIsHighExplosive(out a10CShotIndex)
                        ? "HD_Projectile_A10C_PGU13Round"
                        : "HD_Projectile_A10C_PGU14Round")
                    : HelodCasSupportUtility.StrafeProjectileDefName;
                ThingDef projectileDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                    projectileDefName);
                if (projectileDef == null)
                {
                    continue;
                }
                float lateralOffset = Rand.Range(
                    -HelodCasSupportUtility.StrafeWidth * 0.5f,
                    HelodCasSupportUtility.StrafeWidth * 0.5f);
                float longitudinalOffset = Rand.Range(-1.25f, 1.25f);
                float baseAimDistance = isA10C
                    ? HelodCasSupportUtility.StrafeLength * a10CShotIndex
                        / (HelodCasSupportUtility.A10CStrafeRoundCount - 1f)
                    : aimDistance;
                float roundAimDistance = Mathf.Clamp(baseAimDistance
                    + longitudinalOffset,
                    0f, HelodCasSupportUtility.StrafeLength);
                Vector3 start = strike.Plan.CurrentStrafeStartPosition(map);
                IntVec3 impact = new IntVec3(
                    Mathf.RoundToInt(start.x + direction.x
                        * roundAimDistance
                        + lateral.x * lateralOffset), 0,
                    Mathf.RoundToInt(start.z + direction.y
                        * roundAimDistance
                        + lateral.y * lateralOffset));
                if (!impact.InBounds(map))
                {
                    continue;
                }
                if (!HelodCasCombatBridge.TryLaunchProjectile(projectileDef,
                    strike.Caller, new System.Numerics.Vector3(origin.x, origin.y, origin.z), impact, map))
                {
                    Projectile projectile = (Projectile)GenSpawn.Spawn(projectileDef,
                        originCell, map);
                    projectile.Launch(strike.Caller, origin, impact, impact,
                        ProjectileHitFlags.All);
                }
                if (isA10C)
                {
                    sound?.PlayOneShot(new TargetInfo(originCell, map));
                }
            }
        }

        private static Material AircraftMaterial(HelodCasAircraftKind aircraftKind)
        {
            if (aircraftMaterials.TryGetValue(aircraftKind, out Material material))
            {
                return material;
            }
            string texturePath = HelodCasSupportUtility.AircraftTexture(aircraftKind);
            if (ContentFinder<Texture2D>.Get(texturePath, false) == null)
            {
                return null;
            }
            material = MaterialPool.MatFrom(texturePath, ShaderDatabase.Cutout);
            aircraftMaterials[aircraftKind] = material;
            return material;
        }

        private static Material MunitionMaterial(HelodCasAttackKind attackKind)
        {
            string texturePath = HelodCasSupportUtility.MunitionTexture(attackKind);
            if (munitionMaterials.TryGetValue(texturePath, out Material material))
            {
                return material;
            }
            if (ContentFinder<Texture2D>.Get(texturePath, false) == null)
            {
                return null;
            }
            material = MaterialPool.MatFrom(texturePath, ShaderDatabase.Cutout);
            munitionMaterials[texturePath] = material;
            return material;
        }

        private void DrawLaserGuidanceEffect(HelodCasStrike strike, int now)
        {
            if (strike?.Plan?.GuidanceMode != HelodCasGuidanceMode.Laser
                || !strike.RequiresStationaryGuidance
                || !HelodCasSupportUtility.TryGetLaserDesignatorOperator(
                    strike.Caller, map, strike.Plan.CurrentAimCell(map),
                    out Pawn operatorPawn,
                    out HelodCasLaserDesignatorExtension designator))
            {
                return;
            }

            if (!designator.usesPilotSignalVisual)
            {
                float glowBlink = LaserBlinkIntensity(now);
                DrawLaserAimGlow(strike.Plan.CurrentAimCell(map), glowBlink,
                    1.25f, 0.45f);
                return;
            }

            if (strike.IzlidSkySignalActive(now))
            {
                DrawIzlidSkySignal(strike, operatorPawn, now);
                return;
            }
            if (strike.IzlidTargetSignalActive(now))
            {
                DrawIzlidTargetSignal(operatorPawn,
                    strike.Plan.CurrentAimCell(map),
                    LaserBlinkIntensity(now - strike.IzlidTargetSignalStartTick));
            }
        }

        private void DrawLaserDesignator(HelodCasStrike strike, int now)
        {
            if (strike?.Plan?.GuidanceMode != HelodCasGuidanceMode.Laser
                || !strike.RequiresStationaryGuidance
                || !HelodCasSupportUtility.TryGetLaserDesignatorOperator(
                    strike.Caller, map, strike.Plan.CurrentAimCell(map),
                    out Pawn operatorPawn,
                    out HelodCasLaserDesignatorExtension designator)
                || !HelodCasSupportUtility.TryGetBestLaserDesignatorThing(operatorPawn,
                    out _, out _)
                || designator.guidanceGraphicPath.NullOrEmpty())
            {
                return;
            }

            Material material = LaserDesignatorMaterial(
                designator.guidanceGraphicPath);
            if (material == null)
            {
                return;
            }

            Vector3 aimPosition = strike.Plan.CurrentAimCell(map).ToVector3Shifted();
            if (designator.usesPilotSignalVisual
                && strike.IzlidSkySignalActive(now))
            {
                aimPosition = IzlidSkySignalEnd(strike, operatorPawn, now);
            }
            Vector3 direction = aimPosition - operatorPawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = operatorPawn.Rotation.FacingCell.ToVector3();
            }
            direction.Normalize();

            Vector3 drawPosition;
            if (designator.fixedGuidanceGraphicPosition)
            {
                drawPosition = strike.FixedGuidanceDevicePosition(operatorPawn,
                    designator);
                direction = aimPosition - drawPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = operatorPawn.Rotation.FacingCell.ToVector3();
                }
                direction.Normalize();
            }
            else
            {
                Vector3 right = new Vector3(direction.z, 0f, -direction.x);
                drawPosition = operatorPawn.DrawPos
                    + direction * designator.guidanceGraphicForwardOffset
                    + right * designator.guidanceGraphicLateralOffset;
            }

            drawPosition.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.04f;
            float rotation = direction.AngleFlat()
                + designator.guidanceGraphicRotationOffset;
            float size = Mathf.Max(0.1f, designator.guidanceGraphicSize);
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(drawPosition,
                    Quaternion.AngleAxis(rotation, Vector3.up),
                    new Vector3(size, 1f, size)), material, 0);
        }

        private static Material LaserDesignatorMaterial(string texturePath)
        {
            if (designatorMaterials.TryGetValue(texturePath, out Material material))
            {
                return material;
            }
            if (ContentFinder<Texture2D>.Get(texturePath, false) == null)
            {
                return null;
            }
            material = MaterialPool.MatFrom(texturePath, ShaderDatabase.Cutout);
            designatorMaterials[texturePath] = material;
            return material;
        }

        private static Material[] CreatePulsingSolidMaterials(Color color,
            float minimumAlpha, float maximumAlpha)
        {
            Material[] materials = new Material[16];
            for (int i = 0; i < materials.Length; i++)
            {
                float progress = i / (float)(materials.Length - 1);
                materials[i] = SolidColorMaterials.SimpleSolidColorMaterial(
                    new Color(color.r, color.g, color.b,
                        Mathf.Lerp(minimumAlpha, maximumAlpha, progress)), false);
            }
            return materials;
        }

        private static Material[] CreateSkyFadeMaterials(Color color,
            float nearAlpha, float farAlpha)
        {
            int count = Mathf.Max(2, HelodCasSupportUtility.IzlidSkyBeamSegments);
            Material[] materials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)(count - 1);
                float easedProgress = progress * progress;
                materials[i] = SolidColorMaterials.SimpleSolidColorMaterial(
                    new Color(color.r, color.g, color.b,
                        Mathf.Lerp(nearAlpha, farAlpha, easedProgress)), false);
            }
            return materials;
        }

        private static Material[] CreatePulsingGlowMaterials()
        {
            Material[] materials = new Material[16];
            for (int i = 0; i < materials.Length; i++)
            {
                float progress = i / (float)(materials.Length - 1);
                materials[i] = MaterialPool.MatFrom("Things/Mote/FireGlow",
                    ShaderDatabase.MoteGlow, new Color(0.20f, 1f, 0.40f,
                        Mathf.Lerp(0f, 0.65f, progress)));
            }
            return materials;
        }

        private static float LaserBlinkIntensity(int elapsedTicks)
        {
            int interval = Mathf.Max(1,
                HelodCasSupportUtility.LaserBlinkIntervalTicks);
            return Mathf.FloorToInt(Mathf.Max(0, elapsedTicks) / (float)interval)
                % 2 == 0 ? 1f : 0f;
        }

        private static int PulseMaterialIndex(float intensity)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(intensity)
                * (laserAimGlowMaterials.Length - 1)), 0,
                laserAimGlowMaterials.Length - 1);
        }

        private static void DrawIzlidSkySignal(HelodCasStrike strike,
            Pawn operatorPawn, int now)
        {
            Vector3 origin = operatorPawn.DrawPos;
            Vector3 end = IzlidSkySignalEnd(strike, operatorPawn, now);
            DrawIzlidSkyBeam(origin, end);
        }

        private static Vector3 IzlidSkySignalEnd(HelodCasStrike strike,
            Pawn operatorPawn, int now)
        {
            Vector3 origin = operatorPawn.DrawPos;
            Vector2 direction = new Vector2(0f, 1f);
            float waveDegrees = Mathf.Sin(now * 0.06f
                + operatorPawn.thingIDNumber * 0.37f) * 8f;
            float radians = waveDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 wavedDirection = new Vector2(
                direction.x * cos - direction.y * sin,
                direction.x * sin + direction.y * cos);
            return origin + new Vector3(wavedDirection.x, 0f,
                wavedDirection.y) * HelodCasSupportUtility.IzlidSkyBeamLength;
        }

        private static void DrawIzlidSkyBeam(Vector3 origin, Vector3 destination)
        {
            origin.y = destination.y = AltitudeLayer.MoteOverhead.AltitudeFor()
                + 0.018f;
            int segments = Mathf.Min(izlidSkyBeamOuterMaterials.Length,
                izlidSkyBeamCoreMaterials.Length);
            Vector3 previous = origin;
            for (int i = 0; i < segments; i++)
            {
                float endProgress = (i + 1f) / segments;
                Vector3 current = Vector3.Lerp(origin, destination, endProgress);
                float widthProgress = i / (float)Mathf.Max(1, segments - 1);
                GenDraw.DrawLineBetween(previous, current,
                    izlidSkyBeamOuterMaterials[i],
                    Mathf.Lerp(0.095f, 0.022f, widthProgress));
                GenDraw.DrawLineBetween(previous, current,
                    izlidSkyBeamCoreMaterials[i],
                    Mathf.Lerp(0.030f, 0.005f, widthProgress));
                previous = current;
            }
        }

        private static void DrawIzlidTargetSignal(Pawn operatorPawn,
            IntVec3 target, float intensity)
        {
            Vector3 targetPosition = target.ToVector3Shifted();
            DrawIzlidBeam(operatorPawn.DrawPos, targetPosition, intensity);
            DrawLaserAimGlow(target, intensity, 1.05f, 0.35f);
        }

        private static void DrawIzlidBeam(Vector3 origin, Vector3 destination,
            float intensity = -1f)
        {
            origin.y = destination.y = AltitudeLayer.MoteOverhead.AltitudeFor()
                + 0.018f;
            Material outer = izlidBeamOuterMaterial;
            Material core = izlidBeamCoreMaterial;
            if (intensity >= 0f)
            {
                int materialIndex = PulseMaterialIndex(intensity);
                outer = pulsingBeamOuterMaterials[materialIndex];
                core = pulsingBeamCoreMaterials[materialIndex];
            }
            GenDraw.DrawLineBetween(origin, destination, outer, 0.075f);
            GenDraw.DrawLineBetween(origin, destination, core, 0.022f);
        }

        private static void DrawLaserAimGlow(IntVec3 target, float intensity,
            float baseSize, float pulseAmount)
        {
            Vector3 position = target.ToVector3Shifted();
            position.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
            float size = baseSize + Mathf.Clamp01(intensity) * pulseAmount;
            Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.identity,
                new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix,
                laserAimGlowMaterials[PulseMaterialIndex(intensity)], 0);
        }

        private static void DrawAircraft(HelodCasStrike strike, int now)
        {
            Material material = strike?.Plan == null ? null
                : AircraftMaterial(strike.Plan.AircraftKind);
            if (material == null || strike?.Plan == null || !strike.ShouldDrawAircraft(now))
            {
                return;
            }
            Vector3 position = strike.AircraftDrawPosition(now);
            position.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Vector2 direction = strike.AircraftDrawDirection(now);
            float rotation = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            float size = HelodCasSupportUtility.AircraftDrawSize(
                strike.Plan.AircraftKind) * strike.AircraftDrawScale(now);
            Matrix4x4 matrix = Matrix4x4.TRS(position,
                Quaternion.AngleAxis(rotation, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        private static void DrawFallingBomb(HelodCasFallingBomb bomb, int now)
        {
            Material material = bomb == null ? null
                : MunitionMaterial(bomb.AttackKind);
            if (material == null || bomb == null || now < bomb.ReleaseTick)
            {
                return;
            }
            float progress = bomb.Progress(now);
            float smoothProgress = progress * progress * (3f - 2f * progress);
            Vector3 position = Vector3.Lerp(bomb.ReleasePosition,
                bomb.ImpactCell.ToVector3Shifted(), progress);
            position.y = Mathf.Lerp(AltitudeLayer.MoteOverhead.AltitudeFor(),
                AltitudeLayer.Projectile.AltitudeFor(), smoothProgress);
            float size = Mathf.Lerp(1.05f, 0.5f, smoothProgress)
                * HelodCasSupportUtility.MunitionDrawScale;
            Vector2 drawDirection = bomb.AttackKind == HelodCasAttackKind.Hydra70
                || HelodCasSupportUtility.UsesSequentialTargets(bomb.AttackKind)
                ? bomb.FlightDirection : bomb.ApproachDirection;
            float rotation = Mathf.Atan2(drawDirection.x,
                drawDirection.y) * Mathf.Rad2Deg;
            Matrix4x4 matrix = Matrix4x4.TRS(position,
                Quaternion.AngleAxis(rotation, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

    }
}
