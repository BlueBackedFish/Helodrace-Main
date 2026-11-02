using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using Waylith.Math;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;
using Color = Waylith.Math.Color;

namespace Helodrace
{
    public class TowProjectileExtension : DefModExtension
    {
        public string normalTexPath;
        public string ignitionTexPath;
        public int ignitionDelayTicks = 3;
        public int thrustTicks = 96;
        public int decelerationTicks = 120;
        public float coastSpeedFactor = 0.58f;
        public float turnDegreesPerTick = 2.5f;
        public float oscillationDegrees = 0.45f;
        public float oscillationCyclesPerSecond = 1.15f;
        public float terminalGuidanceDistance = 12f;
        public float terminalOscillationFactor = 0.15f;
        public float directHitRadius = 0.75f;
        public float maximumLaunchDeviationDegrees = 55f;
        public float textureRotationOffsetDegrees;
        public int maximumLifetimeTicks = 900;
    }

    /// <summary>
    /// Continuously steered TOW missile. Unlike the M47 Dragon, this projectile
    /// corrects its heading every tick. It keeps full speed through motor burn,
    /// then linearly decelerates to a fixed coasting speed.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Projectile_Tow : Projectile_Explosive
    {
        private const float WireWidth = 0.045f;
        private const float WireSeparation = 0.09f;
        private const float WireCollisionSampleSpacing = 0.35f;
        private const int WirePathPointCount = 19;
        private const float WireFollowNearLauncher = 0.18f;
        private const float WireFollowNearMissile = 0.07f;

        private bool flightInitialized;
        private bool guidanceActive = true;
        private int flightTicks;
        private Vector3 exactPosition;
        private Vector3 flightDirection;
        private Vector3 initialGuidanceDirection;
        private ThingWithComps launcherPlatform;
        private List<Vector3> wirePath;
        private Vector3 lastTrackedTargetPosition;
        private Vector3 trackedTargetVelocity;
        private int lastTargetSampleTick;
        private bool targetVectorInitialized;
        private bool ignitionSoundStarted;
        private bool ignitionSoundEnded;

        private Material normalMaterial;
        private Material ignitionMaterial;
        private static Material wireMaterial;
        private Sustainer ignitionSustainer;

        private TowProjectileExtension Extension => def.GetModExtension<TowProjectileExtension>();

        public bool GuidanceActive => guidanceActive;
        public ThingWithComps LauncherPlatform => launcherPlatform;
        private ITowGuidanceSource GuidanceSource => launcherPlatform?.AllComps.OfType<ITowGuidanceSource>().FirstOrDefault();
        private bool LauncherAvailable => launcherPlatform != null && !launcherPlatform.Destroyed &&
            launcherPlatform.Map == Map && (GuidanceSource?.CanGuideTow ?? true);
        private Vector3 NativeExactPosition => flightInitialized ? exactPosition : base.ExactPosition.ToNative();
        public override UnityEngine.Vector3 ExactPosition => NativeExactPosition.ToGame();
        public override UnityEngine.Vector3 DrawPos => ExactPosition;

        private static Material WireMaterial => wireMaterial ??
            (wireMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.58f, 0.48f, 0.32f).ToGame()));

        public override void Launch(
            Thing launcher,
            UnityEngine.Vector3 origin,
            LocalTargetInfo usedTarget,
            LocalTargetInfo intendedTarget,
            ProjectileHitFlags hitFlags,
            bool preventFriendlyFire,
            Thing equipment = null,
            ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            exactPosition = origin.ToNative();
            flightDirection = intendedTarget.CenterVector3.ToNative() - exactPosition;
            flightDirection.Y = 0f;
            if (flightDirection.LengthSquared() < 0.001f)
            {
                flightDirection = Vector3.UnitZ;
            }

            flightDirection.Normalize();
            initialGuidanceDirection = flightDirection;
            flightInitialized = true;
            flightTicks = 0;
            launcherPlatform = ResolveLauncherPlatform(launcher, equipment);
            RegisterWithLauncher();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref flightInitialized, "towFlightInitialized");
            Scribe_Values.Look(ref guidanceActive, "towGuidanceActive", true);
            Scribe_Values.Look(ref flightTicks, "towFlightTicks");
            NativeVectorScribe.Look(ref exactPosition, "towExactPosition");
            NativeVectorScribe.Look(ref flightDirection, "towFlightDirection");
            NativeVectorScribe.Look(ref initialGuidanceDirection, "towInitialGuidanceDirection");
            Scribe_References.Look(ref launcherPlatform, "towLauncherPlatform");
            NativeVectorScribe.Look(ref wirePath, "towWirePath");
            NativeVectorScribe.Look(ref lastTrackedTargetPosition, "towLastTrackedTargetPosition");
            NativeVectorScribe.Look(ref trackedTargetVelocity, "towTrackedTargetVelocity");
            Scribe_Values.Look(ref lastTargetSampleTick, "towLastTargetSampleTick");
            Scribe_Values.Look(ref targetVectorInitialized, "towTargetVectorInitialized");
            Scribe_Values.Look(ref ignitionSoundStarted, "towIgnitionSoundStarted");
            Scribe_Values.Look(ref ignitionSoundEnded, "towIgnitionSoundEnded");
        }

        protected override void Tick()
        {
            if (!flightInitialized)
            {
                InitializeFromBaseFlight();
            }

            RegisterWithLauncher();

            if (Map == null)
            {
                Destroy();
                return;
            }

            if (guidanceActive)
            {
                if (!LauncherAvailable)
                {
                    // Unusual turret verbs can expose the manning pawn or held gun first;
                    // give equipment/holder resolution a few flight ticks to find the source.
                    if (flightTicks >= 10)
                    {
                        SeverGuidanceWire();
                    }
                }
                else
                {
                    ApplyContinuousSteering();
                }
            }

            // Start/maintain audio before movement so an impact on this tick
            // still terminates the active ignition sound and plays its end cue.
            MaintainIgnitionSound();

            Vector3 previousPosition = exactPosition;
            exactPosition += flightDirection * CurrentSpeedPerTick;
            exactPosition.Y = 0f;

            if (!exactPosition.ToGame().ToIntVec3().InBounds(Map))
            {
                Destroy();
                return;
            }

            // Let vanilla update the projectile's map cell, lifetime, ambient
            // sustainer and interception checks. Our ExactPosition override
            // supplies the custom movement; this sentinel prevents vanilla's
            // fixed-speed destination timer from ending the flight for us.
            ticksToImpact = 1000000;
            base.Tick();
            if (Destroyed)
            {
                return;
            }

            if (TryImpactTrackedTarget(previousPosition, exactPosition)
                || TryImpactAimPoint(previousPosition, exactPosition)
                || TryImpactBlockingBuilding(previousPosition, exactPosition))
            {
                return;
            }

            UpdateWirePath();
            if (guidanceActive && WireIntersectsTree(out Thing tree))
            {
                SeverGuidanceWire(tree);
            }

            EmitMotorEffects();
            flightTicks++;
            if (flightTicks >= Mathf.Max(1, Extension?.maximumLifetimeTicks ?? 900))
            {
                Impact(null, false);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EndIgnitionSound();
            base.Destroy(mode);
        }

        protected override void DrawAt(UnityEngine.Vector3 drawLoc, bool flip = false)
        {
            Vector3 position = NativeExactPosition;
            position.Y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Vector2 drawSize = def.graphicData?.drawSize.ToNative() ?? new Vector2(0.9f, 0.9f);
            float rotationDegrees = Mathf.Atan2(flightDirection.X, flightDirection.Z) * Mathf.Rad2Deg
                + (Extension?.textureRotationOffsetDegrees ?? 0f);
            Vector3 scale = new Vector3(drawSize.X, 1f, drawSize.Y);
            Graphics.DrawMesh(
                MeshPool.plane10,
                SpatialMath.CreateTransform(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, rotationDegrees * Mathf.Deg2Rad), scale).ToGame(),
                CurrentMaterial,
                0);

            if (guidanceActive && TryGetWireEndpoints(out Vector3 launcherPoint, out Vector3 missilePoint))
            {
                EnsureWirePath(launcherPoint, missilePoint);
                DrawWire(-1f);
                DrawWire(1f);
            }
        }

        private void InitializeFromBaseFlight()
        {
            exactPosition = base.ExactPosition.ToNative();
            flightDirection = destination.ToNative() - exactPosition;
            flightDirection.Y = 0f;
            if (flightDirection.LengthSquared() < 0.001f)
            {
                flightDirection = Vector3.UnitZ;
            }

            flightDirection.Normalize();
            if (initialGuidanceDirection.LengthSquared() < 0.001f)
            {
                initialGuidanceDirection = flightDirection;
            }
            flightInitialized = true;
        }

        public bool TryChangeTarget(LocalTargetInfo newTarget)
        {
            if (!guidanceActive || Map == null || !newTarget.IsValid || !newTarget.Cell.InBounds(Map))
            {
                return false;
            }

            intendedTarget = newTarget;
            usedTarget = newTarget;
            destination = newTarget.CenterVector3;
            trackedTargetVelocity = Vector3.Zero;
            targetVectorInitialized = false;
            return true;
        }

        private void RegisterWithLauncher()
        {
            if (launcherPlatform == null)
            {
                launcherPlatform = ResolveLauncherPlatform(Launcher, equipment);
            }

            CompTowLauncher launcherComp = launcherPlatform?.TryGetComp<CompTowLauncher>();
            if (launcherComp?.ActiveMissile != this)
            {
                launcherComp?.SetActiveMissile(this);
            }
        }

        private ThingWithComps ResolveLauncherPlatform(Thing launcher, Thing equipmentThing)
        {
            if (launcher is ThingWithComps directLauncher
                && directLauncher.TryGetComp<CompTowLauncher>() != null)
            {
                return directLauncher;
            }

            IThingHolder holder = equipmentThing?.ParentHolder;
            for (int depth = 0; holder != null && depth < 5; depth++)
            {
                if (holder is ThingWithComps turret
                    && turret.TryGetComp<CompTowLauncher>() != null)
                {
                    return turret;
                }

                if (holder is ThingOwner owner)
                {
                    holder = owner.Owner;
                }
                else if (holder is Thing thing)
                {
                    holder = thing.ParentHolder;
                }
                else
                {
                    break;
                }
            }

            // Turret verbs can identify the manning pawn as the launcher on
            // some firing paths. Recover the physical TOW from its gun or its
            // mannable comp so retargeting never depends on that distinction.
            if (Map != null)
            {
                Building_TurretGun nearestTow = null;
                float nearestDistanceSquared = float.MaxValue;
                foreach (Thing thing in Map.listerThings.AllThings)
                {
                    if (!(thing is Building_TurretGun turret)
                        || turret.TryGetComp<CompTowLauncher>() == null)
                    {
                        continue;
                    }

                    bool ownsGun = equipmentThing != null && turret.GunCompEq?.parent == equipmentThing;
                    bool hasLauncherPawn = launcher is Pawn pawn
                        && turret.TryGetComp<CompMannable>()?.ManningPawn == pawn;
                    if (ownsGun || hasLauncherPawn)
                    {
                        return turret;
                    }

                    float distanceSquared = (turret.DrawPos.ToNative() - exactPosition).LengthSquared();
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearestTow = turret;
                        nearestDistanceSquared = distanceSquared;
                    }
                }

                // The projectile originates inside its launcher, so a short
                // distance fallback is unambiguous even for unmanned test fire.
                if (nearestTow != null && nearestDistanceSquared <= 4f)
                {
                    return nearestTow;
                }
            }

            return null;
        }

        private void ApplyContinuousSteering()
        {
            Vector3 targetPosition = intendedTarget.CenterVector3.ToNative();
            targetPosition.Y = 0f;
            UpdateTrackedTargetVelocity(targetPosition);
            targetPosition = PredictedTargetPosition(targetPosition);
            Vector3 desiredDirection = targetPosition - exactPosition;
            desiredDirection.Y = 0f;
            if (desiredDirection.LengthSquared() < 0.001f)
            {
                return;
            }

            float distanceToTarget = desiredDirection.Length();
            desiredDirection.Normalize();

            TowProjectileExtension extension = Extension;
            float terminalDistance = Mathf.Max(0.1f, extension?.terminalGuidanceDistance ?? 12f);
            float terminalFactor = 1f - Mathf.Clamp01(distanceToTarget / terminalDistance);
            terminalFactor = terminalFactor * terminalFactor * (3f - 2f * terminalFactor);
            float terminalOscillation = Mathf.Clamp01(extension?.terminalOscillationFactor ?? 0.15f);
            float oscillationScale = Mathf.Lerp(1f, terminalOscillation, terminalFactor);
            float cyclesPerTick = Mathf.Max(0f, extension?.oscillationCyclesPerSecond ?? 1.15f) / 60f;
            float phase = thingIDNumber * 0.173f + flightTicks * cyclesPerTick * Mathf.PI * 2f;
            float oscillation = Mathf.Sin(phase)
                * Mathf.Max(0f, extension?.oscillationDegrees ?? 0.45f)
                * oscillationScale;
            desiredDirection = SpatialMath.RotateAroundY(desiredDirection, oscillation);

            float signedError = SpatialMath.SignedAngleBetween(flightDirection, desiredDirection, Vector3.UnitY);
            float maximumTurn = Mathf.Max(0.01f, extension?.turnDegreesPerTick ?? 2.5f);
            float appliedTurn = Mathf.Clamp(signedError, -maximumTurn, maximumTurn);
            flightDirection = SpatialMath.RotateAroundY(flightDirection, appliedTurn);
            flightDirection.Y = 0f;
            flightDirection = ClampToLaunchCone(flightDirection);
        }

        private Vector3 ClampToLaunchCone(Vector3 direction)
        {
            direction.Y = 0f;
            if (direction.LengthSquared() < 0.001f)
            {
                return initialGuidanceDirection;
            }

            direction.Normalize();
            if (initialGuidanceDirection.LengthSquared() < 0.001f)
            {
                initialGuidanceDirection = direction;
                return direction;
            }

            initialGuidanceDirection.Y = 0f;
            initialGuidanceDirection.Normalize();
            float maximumDeviation = Mathf.Clamp(
                Extension?.maximumLaunchDeviationDegrees ?? 55f,
                1f,
                89f);
            float signedDeviation = SpatialMath.SignedAngleBetween(
                initialGuidanceDirection,
                direction,
                Vector3.UnitY);
            return SpatialMath.RotateAroundY(initialGuidanceDirection, Mathf.Clamp(signedDeviation, -maximumDeviation, maximumDeviation));
        }

        private void UpdateTrackedTargetVelocity(Vector3 targetPosition)
        {
            if (!intendedTarget.HasThing)
            {
                trackedTargetVelocity = Vector3.Zero;
                targetVectorInitialized = false;
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (!targetVectorInitialized)
            {
                lastTrackedTargetPosition = targetPosition;
                lastTargetSampleTick = currentTick;
                targetVectorInitialized = true;
                return;
            }

            int elapsedTicks = Mathf.Max(1, currentTick - lastTargetSampleTick);
            Vector3 measuredVelocity = (targetPosition - lastTrackedTargetPosition) / elapsedTicks;
            measuredVelocity.Y = 0f;
            trackedTargetVelocity = SpatialMath.Interpolate(trackedTargetVelocity, measuredVelocity, 0.55f);
            lastTrackedTargetPosition = targetPosition;
            lastTargetSampleTick = currentTick;
        }

        private Vector3 PredictedTargetPosition(Vector3 targetPosition)
        {
            if (!intendedTarget.HasThing || trackedTargetVelocity.LengthSquared() < 0.000001f)
            {
                return targetPosition;
            }

            float distance = Vector3.Distance(exactPosition, targetPosition);
            float interceptTicks = distance / Mathf.Max(0.0001f, CurrentSpeedPerTick);
            return targetPosition + trackedTargetVelocity * Mathf.Min(interceptTicks, 30f);
        }

        private float CurrentSpeedPerTick
        {
            get
            {
                TowProjectileExtension extension = Extension;
                float poweredSpeed = Mathf.Max(0.0001f, def.projectile.SpeedTilesPerTick);
                int poweredTicks = Mathf.Max(0, extension?.ignitionDelayTicks ?? 3)
                    + Mathf.Max(0, extension?.thrustTicks ?? 96);
                if (flightTicks < poweredTicks)
                {
                    return poweredSpeed;
                }

                float coastFactor = Mathf.Clamp(extension?.coastSpeedFactor ?? 0.58f, 0.05f, 1f);
                int decelerationTicks = Mathf.Max(1, extension?.decelerationTicks ?? 120);
                float decelerationProgress = Mathf.Clamp01((flightTicks - poweredTicks) / (float)decelerationTicks);
                return Mathf.Lerp(poweredSpeed, poweredSpeed * coastFactor, decelerationProgress);
            }
        }

        private bool IgnitionActive
        {
            get
            {
                int ignitionStart = Mathf.Max(0, Extension?.ignitionDelayTicks ?? 3);
                int ignitionEnd = ignitionStart + Mathf.Max(0, Extension?.thrustTicks ?? 96);
                return flightTicks >= ignitionStart && flightTicks < ignitionEnd;
            }
        }

        private Material CurrentMaterial => IgnitionActive ? IgnitionMaterial : NormalMaterial;

        private Material NormalMaterial => normalMaterial ?? (normalMaterial = MaterialPool.MatFrom(
            Extension?.normalTexPath ?? def.graphicData.texPath,
            ShaderDatabase.Cutout));

        private Material IgnitionMaterial => ignitionMaterial ?? (ignitionMaterial = MaterialPool.MatFrom(
            Extension?.ignitionTexPath ?? def.graphicData.texPath,
            ShaderDatabase.Cutout));

        private void EmitMotorEffects()
        {
            if (!IgnitionActive || Map == null || flightTicks % 2 != 0)
            {
                return;
            }

            FleckMaker.ThrowFireGlow(exactPosition.ToGame(), Map, 0.35f);
            FleckMaker.ThrowSmoke(exactPosition.ToGame(), Map, 0.18f);
        }

        private void MaintainIgnitionSound()
        {
            if (!IgnitionActive)
            {
                if (ignitionSoundStarted)
                {
                    EndIgnitionSound();
                }

                return;
            }

            ignitionSoundStarted = true;
            if (ignitionSoundEnded || Map == null)
            {
                return;
            }

            if (ignitionSustainer == null || ignitionSustainer.Ended)
            {
                SoundDef soundDef = DefDatabase<SoundDef>.GetNamedSilentFail("HD_TOWMissile");
                if (soundDef != null)
                {
                    SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(this), MaintenanceType.PerTick);
                    ignitionSustainer = SoundStarter.TrySpawnSustainer(soundDef, soundInfo);
                }
            }

            ignitionSustainer?.Maintain();
        }

        private void EndIgnitionSound()
        {
            if (!ignitionSoundStarted || ignitionSoundEnded)
            {
                return;
            }

            ignitionSoundEnded = true;
            if (ignitionSustainer != null && !ignitionSustainer.Ended)
            {
                // HD_TOWMissile uses HD_TOWMissileEnd as its sustainStopSound.
                ignitionSustainer.End();
            }
            else if (Map != null)
            {
                // Covers loading a save during ignition before the sustainer
                // has been recreated on the next tick.
                DefDatabase<SoundDef>.GetNamedSilentFail("HD_TOWMissileEnd")?
                    .PlayOneShot(new TargetInfo(NativeExactPosition.ToGame().ToIntVec3(), Map));
            }

            ignitionSustainer = null;
        }

        private bool TryImpactTrackedTarget(Vector3 segmentStart, Vector3 segmentEnd)
        {
            if (Map == null || !intendedTarget.HasThing)
            {
                return false;
            }

            Thing target = intendedTarget.Thing;
            if (target == null || target.Destroyed || !target.Spawned || target.Map != Map)
            {
                return false;
            }

            float hitRadius = Mathf.Max(0.05f, Extension?.directHitRadius ?? 0.75f);
            float travelDistance = Vector3.Distance(segmentStart, segmentEnd);
            int samples = Mathf.Max(1, Mathf.CeilToInt(travelDistance / 0.1f));
            for (int i = 1; i <= samples; i++)
            {
                Vector3 point = SpatialMath.Interpolate(segmentStart, segmentEnd, i / (float)samples);
                bool occupiesCell = target.def.category == ThingCategory.Building
                    && target.OccupiedRect().Contains(point.ToGame().ToIntVec3());
                Vector3 targetCenter = target.DrawPos.ToNative();
                targetCenter.Y = 0f;
                point.Y = 0f;
                bool intersectsCenter = (point - targetCenter).LengthSquared() <= hitRadius * hitRadius;
                if (occupiesCell || intersectsCenter)
                {
                    Impact(target, false);
                    return true;
                }
            }

            return false;
        }

        private bool TryImpactAimPoint(Vector3 segmentStart, Vector3 segmentEnd)
        {
            if (intendedTarget.HasThing
                && intendedTarget.Thing != null
                && !intendedTarget.Thing.Destroyed
                && intendedTarget.Thing.Spawned)
            {
                return false;
            }

            Vector3 aimPoint = intendedTarget.CenterVector3.ToNative();
            aimPoint.Y = 0f;
            float hitRadius = Mathf.Max(0.05f, Extension?.directHitRadius ?? 0.75f);
            float travelDistance = Vector3.Distance(segmentStart, segmentEnd);
            int samples = Mathf.Max(1, Mathf.CeilToInt(travelDistance / 0.1f));
            for (int i = 1; i <= samples; i++)
            {
                Vector3 point = SpatialMath.Interpolate(segmentStart, segmentEnd, i / (float)samples);
                point.Y = 0f;
                if ((point - aimPoint).LengthSquared() <= hitRadius * hitRadius)
                {
                    Impact(null, false);
                    return true;
                }
            }

            return false;
        }

        private bool TryImpactBlockingBuilding(Vector3 segmentStart, Vector3 segmentEnd)
        {
            float travelDistance = Vector3.Distance(segmentStart, segmentEnd);
            int samples = Mathf.Max(1, Mathf.CeilToInt(travelDistance / 0.2f));
            for (int i = 1; i <= samples; i++)
            {
                IntVec3 cell = SpatialMath.Interpolate(segmentStart, segmentEnd, i / (float)samples).ToGame().ToIntVec3();
                if (!cell.InBounds(Map))
                {
                    continue;
                }

                Building edifice = cell.GetEdifice(Map);
                if (edifice != null
                    && edifice != launcherPlatform
                    && edifice != Launcher
                    && edifice.def.Fillage == FillCategory.Full)
                {
                    Impact(edifice, false);
                    return true;
                }
            }

            return false;
        }

        private void SeverGuidanceWire(Thing obstruction = null)
        {
            if (!guidanceActive)
            {
                return;
            }

            guidanceActive = false;
            if (Map != null && launcherPlatform?.Faction == Faction.OfPlayer)
            {
                string reason = obstruction == null
                    ? "TOW guidance wire was severed."
                    : $"TOW guidance wire was caught on {obstruction.LabelNoCount} and severed.";
                Messages.Message(reason, this, MessageTypeDefOf.NegativeEvent, false);
            }
        }

        private void UpdateWirePath()
        {
            if (!guidanceActive || !TryGetWireEndpoints(out Vector3 launcherPoint, out Vector3 missilePoint))
            {
                return;
            }

            EnsureWirePath(launcherPoint, missilePoint);
            wirePath[0] = launcherPoint;
            wirePath[wirePath.Count - 1] = missilePoint;
            for (int i = 1; i < wirePath.Count - 1; i++)
            {
                float t = i / (float)(wirePath.Count - 1);
                Vector3 straightPoint = SpatialMath.Interpolate(launcherPoint, missilePoint, t);
                float followFactor = Mathf.Lerp(WireFollowNearLauncher, WireFollowNearMissile, t);
                wirePath[i] = SpatialMath.Interpolate(wirePath[i], straightPoint, followFactor);
            }
        }

        private void EnsureWirePath(Vector3 launcherPoint, Vector3 missilePoint)
        {
            if (wirePath != null && wirePath.Count == WirePathPointCount)
            {
                return;
            }

            wirePath = new List<Vector3>(WirePathPointCount);
            for (int i = 0; i < WirePathPointCount; i++)
            {
                wirePath.Add(SpatialMath.Interpolate(launcherPoint, missilePoint, i / (float)(WirePathPointCount - 1)));
            }
        }

        private void DrawWire(float side)
        {
            if (wirePath == null || wirePath.Count < 2)
            {
                return;
            }

            Vector3 previous = OffsetWirePoint(0, side);
            previous.Y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
            for (int i = 1; i < wirePath.Count; i++)
            {
                Vector3 current = OffsetWirePoint(i, side);
                current.Y = previous.Y;
                GenDraw.DrawLineBetween(previous.ToGame(), current.ToGame(), WireMaterial, WireWidth);
                previous = current;
            }
        }

        private bool WireIntersectsTree(out Thing tree)
        {
            tree = null;
            if (!TryGetWireEndpoints(out Vector3 launcherPoint, out Vector3 missilePoint))
            {
                return false;
            }

            EnsureWirePath(launcherPoint, missilePoint);
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                for (int segmentIndex = 0; segmentIndex < wirePath.Count - 1; segmentIndex++)
                {
                    Vector3 segmentStart = OffsetWirePoint(segmentIndex, side);
                    Vector3 segmentEnd = OffsetWirePoint(segmentIndex + 1, side);
                    int samples = Mathf.Max(1, Mathf.CeilToInt(
                        Vector3.Distance(segmentStart, segmentEnd) / WireCollisionSampleSpacing));
                    for (int sampleIndex = 0; sampleIndex <= samples; sampleIndex++)
                    {
                        Vector3 point = SpatialMath.Interpolate(segmentStart, segmentEnd, sampleIndex / (float)samples);
                        IntVec3 cell = point.ToGame().ToIntVec3();
                        if (!cell.InBounds(Map))
                        {
                            continue;
                        }

                        foreach (Thing thing in cell.GetThingList(Map))
                        {
                            if (thing == launcherPlatform || thing == Launcher || thing == equipment || thing == this)
                            {
                                continue;
                            }

                            if (thing.def?.plant?.IsTree == true || thing.def?.holdsRoof == true)
                            {
                                tree = thing;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private bool TryGetWireEndpoints(out Vector3 launcherPoint, out Vector3 missilePoint)
        {
            launcherPoint = default;
            missilePoint = default;
            if (Map == null || !LauncherAvailable) return false;
            ITowGuidanceSource source = GuidanceSource;
            if (source != null)
            {
                launcherPoint = source.TowWireOrigin;
            }
            else
            {
                launcherPoint = launcherPlatform.DrawPos.ToNative();
                if (launcherPlatform is Building_TurretGun building && building.Top != null)
                    launcherPoint += SpatialMath.RotateAroundY(Vector3.UnitZ, building.Top.CurRotation) * 0.25f;
            }
            missilePoint = NativeExactPosition;
            launcherPoint.Y = 0f;
            missilePoint.Y = 0f;
            return true;
        }

        private Vector3 OffsetWirePoint(int index, float side)
        {
            int previousIndex = Mathf.Max(0, index - 1);
            int nextIndex = Mathf.Min(wirePath.Count - 1, index + 1);
            Vector3 tangent = wirePath[nextIndex] - wirePath[previousIndex];
            tangent.Y = 0f;
            Vector3 perpendicular = tangent.LengthSquared() > 0.001f
                ? new Vector3(-tangent.Z, 0f, tangent.X).normalized
                : Vector3.UnitX;
            return wirePath[index] + perpendicular * side * WireSeparation;
        }
    }
}
