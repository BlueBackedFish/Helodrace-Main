// Frozen from fc204ee8; game math/state retained for native migration comparison.
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Helodrace.ModernWar;

namespace Helodrace.Tests.Frozen
{
    public class Projectile_ModernGrenade : Projectile_Explosive
    {
        private int age;
        private int fuseDurationTicks;
        private bool fuseTriggeredInFlight;
        private int landedTick = -1;
        private int bounceCount;
        private float startingRotation;
        private bool gasEmitterSpawned;
        private bool trajectoryInitialized;
        private int primaryFlightTicks;
        private int bounceFlightTicks;
        private int lastBounceContact = -1;
        private float bounceRotationDegrees;
        private float bounceSpinDegreesPerTick;
        private bool closeUnderhandThrow;
        private float primaryArcHeight = -1f;
        private float primaryVisualArcHeight = -1f;
        private float bounceArcHeight = -1f;
        private float bounceVisualArcFactor = -1f;
        private Vector2 bounceSurfaceFactors = Vector2.one;
        private Vector3 exactPosition;
        private Vector3 launchOrigin;
        private Vector3 bounceStartPosition;
        private Vector3 finalDestination;

        [Unsaved]
        private Material material;

        public override Vector3 ExactPosition => trajectoryInitialized ? exactPosition : base.ExactPosition;
        public override Vector3 DrawPos => ExactPosition;

        public override void Launch(
            Thing launcher,
            Vector3 origin,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags,
            bool preventFriendlyFire = false,
            Thing equipment = null,
            ThingDef targetCoverDef = null)
        {
            base.Launch(
                launcher,
                origin,
                usedTarget,
                intendedTarget,
                hitFlags,
                preventFriendlyFire,
                equipment,
                targetCoverDef);

            startingRotation = Rand.Range(0f, 360f);

            ModernGrenadeProjectileExtension extension =
                def.GetModExtension<ModernGrenadeProjectileExtension>();
            int baseFuseTicks = UnityEngine.Mathf.Max(1, extension?.fuseTicks ?? 270);
            int fuseVariance = UnityEngine.Mathf.Max(0, extension?.fuseVarianceTicks ?? 30);
            fuseDurationTicks = UnityEngine.Mathf.Max(
                1,
                baseFuseTicks + Rand.RangeInclusive(-fuseVariance, fuseVariance));
            InitializeTrajectory(origin, extension);
            if (Map == null || extension == null)
            {
                return;
            }

            Vector3 direction = finalDestination - origin;
            direction.y = 0f;
            float throwDistance = direction.magnitude;
            direction = throwDistance > 0.001f ? direction / throwDistance : Vector3.forward;

            SpawnPart(
                extension.clipDef,
                origin,
                direction,
                UnityEngine.Mathf.Min(extension.clipTravelDistance, throwDistance * 0.45f),
                0,
                UnityEngine.Mathf.Max(1, extension.clipFlightTicks),
                UnityEngine.Mathf.Max(0f, extension.clipArcHeight),
                -Rand.Range(34f, 48f),
                18,
                0.16f,
                1,
                240);

            Vector3 pinDirection = Quaternion.AngleAxis(Rand.Range(-38f, 38f), Vector3.up) * direction;
            SpawnPart(
                extension.pinDef,
                origin,
                pinDirection,
                Rand.Range(0.3f, 0.55f),
                UnityEngine.Mathf.Max(0, extension.pinDropDelayTicks),
                11,
                0.18f,
                Rand.Range(12f, 22f),
                0,
                0f,
                0,
                180);
        }

        public void ConfigureInventoryThrow(float distance)
        {
            closeUnderhandThrow = InventoryGrenadeUtility.UnderhandThrow(distance);
            age = 0;
            landedTick = -1;
            InitializeTrajectory(
                launchOrigin,
                def.GetModExtension<ModernGrenadeProjectileExtension>());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref age, "modernGrenadeAge");
            Scribe_Values.Look(ref fuseDurationTicks, "modernGrenadeFuseDurationTicks");
            Scribe_Values.Look(ref fuseTriggeredInFlight, "modernGrenadeFuseTriggeredInFlight");
            Scribe_Values.Look(ref landedTick, "modernGrenadeLandedTick", -1);
            Scribe_Values.Look(ref bounceCount, "modernGrenadeBounceCount");
            Scribe_Values.Look(ref startingRotation, "modernGrenadeStartingRotation");
            Scribe_Values.Look(ref gasEmitterSpawned, "modernGrenadeGasEmitterSpawned");
            Scribe_Values.Look(ref trajectoryInitialized, "modernGrenadeTrajectoryInitialized");
            Scribe_Values.Look(ref primaryFlightTicks, "modernGrenadePrimaryFlightTicks");
            Scribe_Values.Look(ref bounceFlightTicks, "modernGrenadeBounceFlightTicks");
            Scribe_Values.Look(ref lastBounceContact, "modernGrenadeLastBounceContact", -1);
            Scribe_Values.Look(ref bounceRotationDegrees, "modernGrenadeBounceRotationDegrees");
            Scribe_Values.Look(ref bounceSpinDegreesPerTick, "modernGrenadeBounceSpinDegreesPerTick");
            Scribe_Values.Look(ref closeUnderhandThrow, "modernGrenadeCloseUnderhandThrow");
            Scribe_Values.Look(ref primaryArcHeight, "modernGrenadePrimaryArcHeight", -1f);
            Scribe_Values.Look(ref primaryVisualArcHeight, "modernGrenadePrimaryVisualArcHeight", -1f);
            Scribe_Values.Look(ref bounceArcHeight, "modernGrenadeBounceArcHeight", -1f);
            Scribe_Values.Look(ref bounceVisualArcFactor, "modernGrenadeBounceVisualArcFactor", -1f);
            Scribe_Values.Look(ref bounceSurfaceFactors, "modernGrenadeBounceSurfaceFactors", Vector2.one);
            Scribe_Values.Look(ref exactPosition, "modernGrenadeExactPosition");
            Scribe_Values.Look(ref launchOrigin, "modernGrenadeLaunchOrigin");
            Scribe_Values.Look(ref bounceStartPosition, "modernGrenadeBounceStartPosition");
            Scribe_Values.Look(ref finalDestination, "modernGrenadeFinalDestination");
        }

        protected override void Tick()
        {
            ModernGrenadeProjectileExtension extension =
                def.GetModExtension<ModernGrenadeProjectileExtension>();
            if (!trajectoryInitialized)
            {
                InitializeTrajectory(base.ExactPosition, extension);
            }

            age++;
            if (landedTick >= 0)
            {
                if (FuseExpired(extension))
                {
                    DetonateFromFuse();
                    return;
                }

                base.Tick();
                return;
            }

            if (age > primaryFlightTicks)
            {
                bounceRotationDegrees += bounceSpinDegreesPerTick;
            }

            UpdateTrajectoryPosition();
            ThrowFlightTrail(extension);
            ThrowBounceContactFleck();

            // Vanilla still handles map-cell updates, interception, and the
            // explosive countdown. The custom trajectory decides when the
            // grenade reaches the final impact cell.
            ticksToImpact = 1000000;
            base.Tick();
            if (Destroyed)
            {
                return;
            }

            if (FuseExpired(extension))
            {
                if (extension?.waitForGroundBeforeRelease == true)
                {
                    TriggerFuseInFlight();
                }
                else
                {
                    DetonateFromFuse();
                    return;
                }
            }

            if (age >= primaryFlightTicks + bounceFlightTicks)
            {
                Impact(null, false);
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            ModernGrenadeProjectileExtension extension =
                def.GetModExtension<ModernGrenadeProjectileExtension>();
            TrySpawnGasEmitter();
            base.Impact(hitThing, blockedByShield);
            if (!Destroyed && landedTick < 0)
            {
                landedTick = age;
                exactPosition = Position.ToVector3Shifted();
            }

            if (!Destroyed && extension?.waitForGroundBeforeRelease == true)
            {
                Explode();
            }
        }

        protected virtual void PrepareFuseExplosion()
        {
            TrySpawnGasEmitter();
        }

        private bool FuseExpired(ModernGrenadeProjectileExtension extension)
        {
            if (fuseDurationTicks <= 0)
            {
                fuseDurationTicks = UnityEngine.Mathf.Max(1, extension?.fuseTicks ?? 270);
            }

            return age >= fuseDurationTicks;
        }

        private void DetonateFromFuse()
        {
            PrepareFuseExplosion();
            Explode();
        }

        private void TriggerFuseInFlight()
        {
            if (fuseTriggeredInFlight)
            {
                return;
            }

            fuseTriggeredInFlight = true;
            if (Map != null)
            {
                def.projectile.soundExplode?.PlayOneShot(
                    new TargetInfo(Position, Map));
            }
        }

        private void ThrowFlightTrail(ModernGrenadeProjectileExtension extension)
        {
            if (extension?.smokeTrail != true
                || Map == null
                || age % UnityEngine.Mathf.Max(1, extension.trailIntervalTicks) != 0)
            {
                return;
            }

            Vector3 direction = finalDestination - launchOrigin;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.forward;
            Vector3 position = CurrentVisualTrailPosition(extension);
            FleckCreationData smoke = FleckMaker.GetDataStatic(
                position,
                Map,
                FleckDefOf.Smoke,
                UnityEngine.Mathf.Max(0.05f, extension.trailSmokeScale) * Rand.Range(0.78f, 1.18f));
            smoke.rotation = Rand.Range(0f, 360f);
            smoke.rotationRate = Rand.Range(-12f, 12f);
            smoke.velocity = -direction * Rand.Range(0.05f, 0.12f);
            smoke.solidTimeOverride = Rand.Range(0.16f, 0.34f);
            Map.flecks.CreateFleck(smoke);

            if (extension.glowingTrail)
            {
                FleckMaker.ThrowFireGlow(
                    position,
                    Map,
                    UnityEngine.Mathf.Max(0.05f, extension.trailGlowScale));
            }
        }

        private Vector3 CurrentVisualTrailPosition(
            ModernGrenadeProjectileExtension extension)
        {
            Vector3 position = ExactPosition;
            if (age <= primaryFlightTicks)
            {
                float progress = UnityEngine.Mathf.Clamp01(
                    age / (float)UnityEngine.Mathf.Max(1, primaryFlightTicks));
                float visualArcHeight = primaryVisualArcHeight >= 0f
                    ? primaryVisualArcHeight
                    : UnityEngine.Mathf.Max(0f, extension?.projectileVisualArcHeight ?? 1.6f);
                position.z += UnityEngine.Mathf.Sin(progress * UnityEngine.Mathf.PI) * visualArcHeight;
                return position;
            }

            float bounceProgress = UnityEngine.Mathf.Clamp01(
                (age - primaryFlightTicks) / (float)UnityEngine.Mathf.Max(1, bounceFlightTicks));
            float bounceHeight = BounceHeightAt(
                bounceProgress,
                bounceArcHeight >= 0f
                    ? bounceArcHeight
                    : UnityEngine.Mathf.Max(0f, extension?.projectileBounceHeight ?? 0.34f));
            position.z += bounceHeight * (bounceVisualArcFactor >= 0f
                ? bounceVisualArcFactor
                : 0.75f);
            return position;
        }

        private void TrySpawnGasEmitter()
        {
            ModernGrenadeProjectileExtension extension =
                def.GetModExtension<ModernGrenadeProjectileExtension>();
            if (gasEmitterSpawned || extension?.gasDef == null || Map == null)
            {
                return;
            }

            gasEmitterSpawned = true;
            CSGasEmitter emitter = ThingMaker.MakeThing(
                DefDatabase<ThingDef>.GetNamed("HD_CSGasEmitter")) as CSGasEmitter;
            emitter?.Initialize(
                extension.gasDef,
                extension.gasReleaseDelayTicks,
                extension.gasEmissionRadius,
                extension.gasDensity,
                extension.gasEdgeDensityFactor,
                extension.gasEmissionDurationTicksMin,
                extension.gasEmissionDurationTicksMax,
                extension.gasEmissionIntervalTicks,
                extension.gasPulseDensityFactor);
            if (emitter != null)
            {
                GenSpawn.Spawn(emitter, Position, Map);
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            ModernGrenadeProjectileExtension extension =
                def.GetModExtension<ModernGrenadeProjectileExtension>();
            float flightSpin = extension?.projectileSpinDegreesPerTick ?? 6f;
            int flightRotationTicks = UnityEngine.Mathf.Min(age, primaryFlightTicks);
            float rotation = startingRotation + flightRotationTicks * flightSpin;
            if (age > primaryFlightTicks)
            {
                rotation += bounceRotationDegrees;
            }

            drawLoc = ExactPosition;
            drawLoc.y = AltitudeLayer.Projectile.AltitudeFor();
            if (landedTick < 0 && age <= primaryFlightTicks)
            {
                float flightProgress = UnityEngine.Mathf.Clamp01(age / (float)UnityEngine.Mathf.Max(1, primaryFlightTicks));
                float throwDistance = Vector3.Distance(launchOrigin, bounceStartPosition);
                float flightArcHeight = primaryArcHeight >= 0f
                    ? primaryArcHeight
                    : UnityEngine.Mathf.Clamp(throwDistance * 0.16f, 0.8f, 2.8f);
                float visualArcHeight = primaryVisualArcHeight >= 0f
                    ? primaryVisualArcHeight
                    : UnityEngine.Mathf.Max(0f, extension?.projectileVisualArcHeight ?? 1.6f);
                float arcProgress = UnityEngine.Mathf.Sin(flightProgress * UnityEngine.Mathf.PI);
                drawLoc.y += arcProgress * flightArcHeight;
                drawLoc.z += arcProgress * visualArcHeight;
            }
            else if (landedTick < 0)
            {
                float bounceProgress = UnityEngine.Mathf.Clamp01(
                    (age - primaryFlightTicks) / (float)UnityEngine.Mathf.Max(1, bounceFlightTicks));
                float bounceHeight = BounceHeightAt(
                    bounceProgress,
                    bounceArcHeight >= 0f
                        ? bounceArcHeight
                        : UnityEngine.Mathf.Max(0f, extension?.projectileBounceHeight ?? 0.34f));
                drawLoc.y += bounceHeight;
                drawLoc.z += bounceHeight * (bounceVisualArcFactor >= 0f
                    ? bounceVisualArcFactor
                    : 0.75f);
                rotation += bounceHeight * 110f;
            }

            Vector2 drawSize = def.graphicData?.drawSize ?? new Vector2(0.46f, 0.46f);
            Vector3 scale = new Vector3(drawSize.x, 1f, drawSize.y);
            material = material ?? MaterialPool.MatFrom(def.graphicData.texPath, ShaderDatabase.Cutout);
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(rotation, Vector3.up), scale),
                material,
                0);
        }

        private void InitializeTrajectory(
            Vector3 origin,
            ModernGrenadeProjectileExtension extension)
        {
            launchOrigin = origin;
            launchOrigin.y = 0f;
            finalDestination = destination;
            finalDestination.y = 0f;

            Vector3 direction = finalDestination - launchOrigin;
            float throwDistance = direction.magnitude;
            direction = throwDistance > 0.001f ? direction / throwDistance : Vector3.forward;

            float baseBounceDistance = UnityEngine.Mathf.Max(
                0.2f,
                extension?.projectileBounceTravelDistance ?? 2f);
            float requestedBounceDistance = closeUnderhandThrow
                ? baseBounceDistance * 0.38f
                : baseBounceDistance;
            float bounceDistance = UnityEngine.Mathf.Min(
                requestedBounceDistance,
                throwDistance * (closeUnderhandThrow ? 0.18f : 0.35f));
            bounceStartPosition = finalDestination - direction * bounceDistance;
            exactPosition = launchOrigin;
            primaryFlightTicks = UnityEngine.Mathf.Max(1, ticksToImpact);
            int baseBounceTicks = extension?.projectileBounceTicks ?? 30;
            bounceFlightTicks = closeUnderhandThrow
                ? UnityEngine.Mathf.Max(8, UnityEngine.Mathf.RoundToInt(baseBounceTicks * 0.55f))
                : UnityEngine.Mathf.Max(8, baseBounceTicks);
            bounceCount = closeUnderhandThrow ? 1 : Rand.RangeInclusive(1, 2);

            float primaryDistance = Vector3.Distance(launchOrigin, bounceStartPosition);
            float baseVisualArcHeight = UnityEngine.Mathf.Max(
                0f,
                extension?.projectileVisualArcHeight ?? 1.6f);
            float baseBounceHeight = UnityEngine.Mathf.Max(
                0f,
                extension?.projectileBounceHeight ?? 0.34f);
            primaryArcHeight = closeUnderhandThrow
                ? UnityEngine.Mathf.Clamp(primaryDistance * 0.075f, 0.22f, 0.65f)
                : UnityEngine.Mathf.Clamp(primaryDistance * 0.16f, 0.8f, 2.8f);
            primaryVisualArcHeight = closeUnderhandThrow
                ? UnityEngine.Mathf.Min(0.45f, baseVisualArcHeight * 0.28f)
                : baseVisualArcHeight;
            bounceArcHeight = closeUnderhandThrow
                ? baseBounceHeight * 0.45f
                : baseBounceHeight;
            bounceVisualArcFactor = closeUnderhandThrow ? 0.35f : 0.75f;
            bounceSpinDegreesPerTick = extension?.projectileSpinDegreesPerTick ?? 6f;
            bounceSurfaceFactors = Vector2.one;
            lastBounceContact = -1;
            bounceRotationDegrees = 0f;
            trajectoryInitialized = true;
        }

        private void UpdateTrajectoryPosition()
        {
            if (age <= primaryFlightTicks)
            {
                float progress = UnityEngine.Mathf.Clamp01(age / (float)UnityEngine.Mathf.Max(1, primaryFlightTicks));
                exactPosition = Vector3.Lerp(launchOrigin, bounceStartPosition, progress);
                return;
            }

            float bounceProgress = UnityEngine.Mathf.Clamp01(
                (age - primaryFlightTicks) / (float)UnityEngine.Mathf.Max(1, bounceFlightTicks));
            exactPosition = Vector3.Lerp(bounceStartPosition, finalDestination, bounceProgress);
        }

        private float BounceHeightAt(float progress, float firstBounceHeight)
        {
            int bounces = UnityEngine.Mathf.Max(1, bounceCount);
            float scaledProgress = UnityEngine.Mathf.Clamp01(progress) * bounces;
            int bounceIndex = UnityEngine.Mathf.Min(UnityEngine.Mathf.FloorToInt(scaledProgress), bounces - 1);
            float localProgress = scaledProgress - bounceIndex;
            if (progress >= 1f)
            {
                localProgress = 1f;
            }

            float surfaceFactor = bounceIndex == 0
                ? bounceSurfaceFactors.x
                : bounceSurfaceFactors.y;
            float height = firstBounceHeight
                * UnityEngine.Mathf.Pow(0.58f, bounceIndex)
                * surfaceFactor;
            return 4f * height * localProgress * (1f - localProgress);
        }

        private void ThrowBounceContactFleck()
        {
            int bounceAge = age - primaryFlightTicks;
            if (Map == null || bounceAge < 0 || bounceAge >= bounceFlightTicks)
            {
                return;
            }

            int contact = UnityEngine.Mathf.FloorToInt(
                bounceAge * UnityEngine.Mathf.Max(1, bounceCount) / (float)UnityEngine.Mathf.Max(1, bounceFlightTicks));
            if (contact <= lastBounceContact)
            {
                return;
            }

            lastBounceContact = contact;
            bool hardSurface = IsHardSurface(exactPosition.ToIntVec3());
            SetBounceSurfaceFactor(contact, hardSurface ? 1.15f : 0.52f);

            if (hardSurface)
            {
                float spinDirection = Rand.Bool ? -1f : 1f;
                bounceRotationDegrees += spinDirection * Rand.Range(48f, 96f);
                bounceSpinDegreesPerTick = spinDirection * Rand.Range(15f, 25f);
            }
            else
            {
                bounceSpinDegreesPerTick *= 0.48f;
            }

            FleckMaker.ThrowDustPuff(
                exactPosition,
                Map,
                hardSurface ? Rand.Range(0.42f, 0.62f) : Rand.Range(0.68f, 0.95f));
        }

        private bool IsHardSurface(IntVec3 cell)
        {
            if (!cell.InBounds(Map))
            {
                return false;
            }

            TerrainDef terrain = cell.GetTerrain(Map);
            if (terrain?.smoothedTerrain != null ||
                terrain?.defName.EndsWith("_Rough") == true ||
                terrain?.defName.EndsWith("_RoughHewn") == true)
            {
                return true;
            }

            int terrainPathCost = UnityEngine.Mathf.Max(0, terrain?.pathCost ?? 0);
            float movementSpeedFactor = 13f / (13f + terrainPathCost);
            return movementSpeedFactor >= 0.99f;
        }

        private void SetBounceSurfaceFactor(int bounceIndex, float factor)
        {
            if (bounceIndex <= 0)
            {
                bounceSurfaceFactors.x = factor;
            }
            else
            {
                bounceSurfaceFactors.y = factor;
            }
        }

        private void SpawnPart(
            ThingDef partDef,
            Vector3 start,
            Vector3 direction,
            float travelDistance,
            int delayTicks,
            int flightTicks,
            float arcHeight,
            float spinDegreesPerTick,
            int bounceTicks,
            float bounceHeight,
            int bounceCount,
            int settledTicks)
        {
            if (partDef == null)
            {
                return;
            }

            ModernGrenadePart part = ThingMaker.MakeThing(partDef) as ModernGrenadePart;
            if (part == null)
            {
                Log.ErrorOnce(
                    $"{partDef.defName} must use {nameof(ModernGrenadePart)}.",
                    partDef.shortHash);
                return;
            }

            part.Initialize(
                start,
                direction,
                travelDistance,
                delayTicks,
                flightTicks,
                arcHeight,
                spinDegreesPerTick,
                bounceTicks,
                bounceHeight,
                bounceCount,
                settledTicks);
            GenSpawn.Spawn(part, start.ToIntVec3(), Map);
        }
    }

    public sealed class ModernGrenadePart : Thing
    {
        private Vector3 start;
        private Vector3 direction;
        private Vector3 exactPosition;
        private float travelDistance;
        private float arcHeight;
        private float spinDegreesPerTick;
        private float bounceHeight;
        private float startingAngle;
        private int delayTicks;
        private int flightTicks;
        private int bounceTicks;
        private int bounceCount;
        private int settledTicks;
        private int age;

        [Unsaved]
        private Material material;

        public override Vector3 DrawPos => exactPosition;

        public void Initialize(
            Vector3 startPosition,
            Vector3 flightDirection,
            float distance,
            int delay,
            int duration,
            float height,
            float spin,
            int bounceDuration,
            float bounceArcHeight,
            int bounces,
            int remainTicks)
        {
            start = startPosition;
            direction = flightDirection.normalized;
            exactPosition = startPosition;
            travelDistance = UnityEngine.Mathf.Max(0f, distance);
            delayTicks = UnityEngine.Mathf.Max(0, delay);
            flightTicks = UnityEngine.Mathf.Max(1, duration);
            arcHeight = UnityEngine.Mathf.Max(0f, height);
            spinDegreesPerTick = spin;
            bounceTicks = UnityEngine.Mathf.Max(0, bounceDuration);
            bounceHeight = UnityEngine.Mathf.Max(0f, bounceArcHeight);
            bounceCount = UnityEngine.Mathf.Max(0, bounces);
            settledTicks = UnityEngine.Mathf.Max(1, remainTicks);
            startingAngle = Rand.Range(0f, 360f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref start, "start");
            Scribe_Values.Look(ref direction, "direction");
            Scribe_Values.Look(ref exactPosition, "exactPosition");
            Scribe_Values.Look(ref travelDistance, "travelDistance");
            Scribe_Values.Look(ref arcHeight, "arcHeight");
            Scribe_Values.Look(ref spinDegreesPerTick, "spinDegreesPerTick");
            Scribe_Values.Look(ref bounceHeight, "bounceHeight");
            Scribe_Values.Look(ref startingAngle, "startingAngle");
            Scribe_Values.Look(ref delayTicks, "delayTicks");
            Scribe_Values.Look(ref flightTicks, "flightTicks", 1);
            Scribe_Values.Look(ref bounceTicks, "bounceTicks");
            Scribe_Values.Look(ref bounceCount, "bounceCount");
            Scribe_Values.Look(ref settledTicks, "settledTicks", 1);
            Scribe_Values.Look(ref age, "age");
        }

        protected override void Tick()
        {
            base.Tick();
            age++;

            int flightAge = age - delayTicks;
            if (flightAge >= 0)
            {
                float progress = UnityEngine.Mathf.Clamp01(flightAge / (float)flightTicks);
                exactPosition = start + direction * (travelDistance * progress);
                exactPosition.y = AltitudeLayer.MoteOverhead.AltitudeFor()
                    + UnityEngine.Mathf.Sin(progress * UnityEngine.Mathf.PI) * arcHeight;

                int bounceAge = flightAge - flightTicks;
                if (bounceAge >= 0 && bounceTicks > 0 && bounceCount > 0)
                {
                    float bounceProgress = UnityEngine.Mathf.Clamp01(bounceAge / (float)bounceTicks);
                    float envelope = 1f - bounceProgress;
                    exactPosition.y += UnityEngine.Mathf.Abs(
                        UnityEngine.Mathf.Sin(bounceProgress * UnityEngine.Mathf.PI * bounceCount))
                        * bounceHeight
                        * envelope;
                }
            }

            if (age >= delayTicks + flightTicks + bounceTicks + settledTicks)
            {
                Destroy(DestroyMode.Vanish);
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (age < delayTicks)
            {
                return;
            }

            int flightAge = age - delayTicks;
            int movingAge = UnityEngine.Mathf.Min(flightAge, flightTicks + bounceTicks);
            float rotation = startingAngle
                + spinDegreesPerTick * UnityEngine.Mathf.Min(flightAge, flightTicks)
                + spinDegreesPerTick * 0.35f * UnityEngine.Mathf.Max(0, movingAge - flightTicks);
            Vector2 drawSize = def.graphicData?.drawSize ?? new Vector2(0.3f, 0.3f);
            Vector3 scale = new Vector3(drawSize.x, 1f, drawSize.y);
            material = material ?? MaterialPool.MatFrom(def.graphicData.texPath, ShaderDatabase.Cutout);
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(DrawPos, Quaternion.AngleAxis(rotation, Vector3.up), scale),
                material,
                0);
        }
    }
}
