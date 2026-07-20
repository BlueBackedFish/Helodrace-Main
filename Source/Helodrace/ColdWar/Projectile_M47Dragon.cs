using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public class M47DragonProjectileExtension : DefModExtension
    {
        public float steeringOscillationDegrees = 1.1f;
        public float accuracyInfluence = 0.45f;
        public float oscillationStepRadians = 1.35f;
        public float vectorLeadStartDistance = 45f;
        public float vectorLeadFullDistance = 10f;
        public float maximumLeadTicks = 45f;
        public float targetVectorSmoothing = 0.45f;
        public float terminalGuidanceDistance = 14f;
        public float terminalOscillationFactor = 0.15f;
        public float terminalSteeringMultiplier = 1.6f;
        public float directHitRadius = 0.42f;
        public float alignedCoastAngleDegrees = 2f;
        public float maximumLaunchDeviationDegrees = 55f;
    }

    /// <summary>
    /// M222 missile with a pair of command-guidance wires running back to the
    /// launcher. Steering code must check GuidanceActive before changing the
    /// missile's course.
    /// </summary>
    public class Projectile_M47Dragon : Projectile_Explosive
    {
        private const float WireWidth = 0.025f;
        private const float WireSeparation = 0.09f;
        private const float CollisionSampleSpacing = 0.35f;
        private const int WirePathPointCount = 19;
        private const float WirePathFollowFactorNearLauncher = 0.18f;
        private const float WirePathFollowFactorNearMissile = 0.07f;
        private const int SteeringPulseIntervalTicks = 6;
        private const int SteeringFlameTicks = 2;
        private const float SteeringSmokeSize = 0.28f;
        private const float SteeringTextureScale = 10f;
        private const float WeakSteeringThresholdDegrees = 12f;
        private const float WeakSteeringDegreesPerPulse = 2.5f;
        private const float StrongSteeringDegreesPerPulse = 7f;
        private const string PulseTextureRoot = "Weapon/ColdWar/Proj/M222/HD_M47M222Missile_";

        private bool guidanceActive = true;
        private int wireAgeTicks;
        private Vector3 flightDirection;
        private Vector3 initialGuidanceDirection;
        private int steeringFlameTicksLeft;
        private string steeringTextureSuffix;
        private bool registeredWithLauncher;
        private List<Vector3> wirePath;
        private int steeringPulseCount;
        private Vector3 lastTrackedTargetPosition;
        private Vector3 trackedTargetVelocity;
        private int lastTargetVectorSampleTick;
        private bool targetVectorInitialized;

        private static Material wireMaterial;

        public bool GuidanceActive => guidanceActive;

        private static Material WireMaterial => wireMaterial ??
            (wireMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.24f, 0.20f, 0.14f)));

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref guidanceActive, "guidanceActive", true);
            Scribe_Values.Look(ref wireAgeTicks, "wireAgeTicks");
            Scribe_Values.Look(ref flightDirection, "flightDirection");
            Scribe_Values.Look(ref initialGuidanceDirection, "initialGuidanceDirection");
            Scribe_Values.Look(ref steeringFlameTicksLeft, "steeringFlameTicksLeft");
            Scribe_Values.Look(ref steeringTextureSuffix, "steeringTextureSuffix");
            Scribe_Values.Look(ref registeredWithLauncher, "registeredWithLauncher");
            Scribe_Collections.Look(ref wirePath, "wirePath", LookMode.Value);
            Scribe_Values.Look(ref steeringPulseCount, "steeringPulseCount");
            Scribe_Values.Look(ref lastTrackedTargetPosition, "lastTrackedTargetPosition");
            Scribe_Values.Look(ref trackedTargetVelocity, "trackedTargetVelocity");
            Scribe_Values.Look(ref lastTargetVectorSampleTick, "lastTargetVectorSampleTick");
            Scribe_Values.Look(ref targetVectorInitialized, "targetVectorInitialized");
        }

        protected override void Tick()
        {
            RegisterWithLauncher();
            InitializeFlightDirection();
            Vector3 positionBeforeTick = ExactPosition;
            if (steeringFlameTicksLeft > 0)
            {
                steeringFlameTicksLeft--;
            }

            if (guidanceActive && wireAgeTicks % SteeringPulseIntervalTicks == 0)
            {
                ApplySteeringPulse();
            }

            base.Tick();
            if (Destroyed)
            {
                return;
            }

            if (TryImpactTrackedTarget(positionBeforeTick, ExactPosition)
                || TryImpactBlockingBuilding(positionBeforeTick, ExactPosition))
            {
                return;
            }

            EmitSteeringEffects();

            wireAgeTicks++;
            UpdateWirePath();
            if (guidanceActive && (Launcher == null || Launcher.Destroyed || Launcher.Map != Map))
            {
                SeverGuidanceWire();
            }
            else if (guidanceActive && WireIntersectsTree(out Thing tree))
            {
                SeverGuidanceWire(tree);
            }

        }

        public bool TryChangeTarget(LocalTargetInfo newTarget)
        {
            if (!guidanceActive || Map == null || !newTarget.IsValid || !newTarget.Cell.InBounds(Map))
            {
                return false;
            }

            intendedTarget = newTarget;
            usedTarget = newTarget;
            targetVectorInitialized = false;
            trackedTargetVelocity = Vector3.zero;

            // An explicit retarget command establishes a new remaining range
            // from the missile's current position. Direction is left untouched
            // until the next steering pulse, and speed remains constant.
            Vector3 missilePosition = ExactPosition;
            float newRemainingDistance = Vector3.Distance(missilePosition, newTarget.CenterVector3);
            float speedPerTick = Mathf.Max(0.0001f, def.projectile.SpeedTilesPerTick);
            origin = missilePosition;
            destination = missilePosition + flightDirection * newRemainingDistance;
            ticksToImpact = Mathf.Max(1, Mathf.CeilToInt(newRemainingDistance / speedPerTick));
            return true;
        }

        private void RegisterWithLauncher()
        {
            if (registeredWithLauncher)
            {
                return;
            }

            CompM47DragonLauncher launcherComp = equipment?.TryGetComp<CompM47DragonLauncher>();
            if (launcherComp != null)
            {
                launcherComp.SetActiveMissile(this);
                registeredWithLauncher = true;
                if (Launcher is Pawn operatorPawn
                    && operatorPawn.Faction == Faction.OfPlayer
                    && Find.Targeter.IsTargeting)
                {
                    Find.Targeter.StopTargeting();
                }
            }
        }

        private bool TryImpactBlockingBuilding(Vector3 segmentStart, Vector3 segmentEnd)
        {
            if (Map == null)
            {
                return false;
            }

            float distance = Vector3.Distance(segmentStart, segmentEnd);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / 0.2f));
            for (int i = 1; i <= samples; i++)
            {
                IntVec3 cell = Vector3.Lerp(segmentStart, segmentEnd, i / (float)samples).ToIntVec3();
                if (!cell.InBounds(Map))
                {
                    continue;
                }

                Building edifice = cell.GetEdifice(Map);
                if (edifice != null && edifice.def.Fillage == FillCategory.Full)
                {
                    Impact(edifice, false);
                    return true;
                }
            }

            return false;
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

            M47DragonProjectileExtension extension = def.GetModExtension<M47DragonProjectileExtension>();
            float directHitRadius = Mathf.Max(0.05f, extension?.directHitRadius ?? 0.42f);
            float travelDistance = Vector3.Distance(segmentStart, segmentEnd);
            int samples = Mathf.Max(1, Mathf.CeilToInt(travelDistance / 0.1f));

            for (int i = 1; i <= samples; i++)
            {
                Vector3 point = Vector3.Lerp(segmentStart, segmentEnd, i / (float)samples);
                IntVec3 cell = point.ToIntVec3();
                bool intersectsOccupiedCell = target.def.category == ThingCategory.Building
                    && target.OccupiedRect().Contains(cell);
                bool intersectsCenter = (point - target.DrawPos).sqrMagnitude <= directHitRadius * directHitRadius;
                if (intersectsOccupiedCell || intersectsCenter)
                {
                    Impact(target, false);
                    return true;
                }
            }

            return false;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (HasActiveSteeringFrame)
            {
                DrawSteeringPulse(drawLoc);
            }
            else
            {
                base.DrawAt(drawLoc, flip);
            }

            if (!guidanceActive || !TryGetWireEndpoints(out Vector3 launcherPoint, out Vector3 missilePoint))
            {
                return;
            }

            EnsureWirePath(launcherPoint, missilePoint);
            DrawWire(-1f);
            DrawWire(1f);
        }

        public void SeverGuidanceWire(Thing obstruction = null)
        {
            if (!guidanceActive)
            {
                return;
            }

            guidanceActive = false;
            if (Map != null && Launcher?.Faction == Faction.OfPlayer)
            {
                string reason = obstruction == null
                    ? "M47 Dragon guidance wire was severed."
                    : $"M47 Dragon guidance wire was caught on {obstruction.LabelNoCount} and severed.";
                Messages.Message(reason, this, MessageTypeDefOf.NegativeEvent, false);
            }
        }

        private bool HasActiveSteeringFrame =>
            steeringFlameTicksLeft > 0 && !steeringTextureSuffix.NullOrEmpty();

        private void InitializeFlightDirection()
        {
            if (flightDirection.sqrMagnitude > 0.001f)
            {
                return;
            }

            flightDirection = destination - origin;
            flightDirection.y = 0f;
            if (flightDirection.sqrMagnitude < 0.001f)
            {
                flightDirection = Vector3.forward;
            }

            flightDirection.Normalize();
            if (initialGuidanceDirection.sqrMagnitude < 0.001f)
            {
                initialGuidanceDirection = flightDirection;
            }
        }

        private void ApplySteeringPulse()
        {
            Vector3 missilePosition = ExactPosition;
            Vector3 targetPosition = intendedTarget.CenterVector3;
            UpdateTrackedTargetVector(targetPosition);
            targetPosition = ActiveLeadPosition(missilePosition, targetPosition);
            Vector3 desiredDirection = targetPosition - missilePosition;
            desiredDirection.y = 0f;
            if (desiredDirection.sqrMagnitude < 0.01f)
            {
                return;
            }

            float distanceToAimPoint = desiredDirection.magnitude;
            desiredDirection.Normalize();
            M47DragonProjectileExtension extension = def.GetModExtension<M47DragonProjectileExtension>();
            float idealSteeringError = Vector3.SignedAngle(flightDirection, desiredDirection, Vector3.up);
            float coastAngle = Mathf.Max(0f, extension?.alignedCoastAngleDegrees ?? 2f);
            if (Mathf.Abs(idealSteeringError) <= coastAngle)
            {
                // The missile is already sufficiently aligned. Advance the
                // oscillation phase, but do not fire a steering charge during
                // this cycle.
                steeringPulseCount++;
                steeringFlameTicksLeft = 0;
                steeringTextureSuffix = null;
                return;
            }

            float terminalFactor = TerminalGuidanceFactor(distanceToAimPoint);
            float oscillationDegrees = SteeringOscillationDegrees(terminalFactor);
            desiredDirection = Quaternion.AngleAxis(oscillationDegrees, Vector3.up) * desiredDirection;
            float signedError = Vector3.SignedAngle(flightDirection, desiredDirection, Vector3.up);
            float absoluteError = Mathf.Abs(signedError);
            bool weakSteering = absoluteError <= WeakSteeringThresholdDegrees;
            float maximumTurn = weakSteering ? WeakSteeringDegreesPerPulse : StrongSteeringDegreesPerPulse;
            float terminalSteeringMultiplier = Mathf.Max(1f, extension?.terminalSteeringMultiplier ?? 1.6f);
            maximumTurn *= Mathf.Lerp(1f, terminalSteeringMultiplier, terminalFactor);
            float appliedTurn = Mathf.Clamp(signedError, -maximumTurn, maximumTurn);

            flightDirection = Quaternion.AngleAxis(appliedTurn, Vector3.up) * flightDirection;
            flightDirection.y = 0f;
            flightDirection = ClampToLaunchCone(flightDirection);

            // Rotate only the remaining flight vector. Keeping ticksToImpact
            // and rebuilding the remaining distance from the projectile def's
            // speed prevents every steering pulse from adding acceleration.
            origin = missilePosition;
            float speedPerTick = Mathf.Max(0.0001f, def.projectile.SpeedTilesPerTick);
            float remainingTravelDistance = speedPerTick * Mathf.Max(1, ticksToImpact);
            float distanceToMapEdge = DistanceToMapEdge(missilePosition, flightDirection);
            if (remainingTravelDistance > distanceToMapEdge)
            {
                remainingTravelDistance = distanceToMapEdge;
                ticksToImpact = Mathf.Max(1, Mathf.CeilToInt(remainingTravelDistance / speedPerTick));
            }
            destination = missilePosition + flightDirection * remainingTravelDistance;

            // signedError is calculated after the regular oscillation has been
            // applied, so the frame reflects the final requested steering
            // direction rather than the unmodified target bearing.
            steeringTextureSuffix = TextureSuffixForFinalDirection(signedError);
            steeringFlameTicksLeft = SteeringFlameTicks;
            steeringPulseCount++;
            if (Map != null)
            {
                SoundDef.Named("Shot_Revolver").PlayOneShot(new TargetInfo(missilePosition.ToIntVec3(), Map));
            }
        }

        private Vector3 ClampToLaunchCone(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return initialGuidanceDirection;
            }

            direction.Normalize();
            if (initialGuidanceDirection.sqrMagnitude < 0.001f)
            {
                initialGuidanceDirection = direction;
                return direction;
            }

            initialGuidanceDirection.y = 0f;
            initialGuidanceDirection.Normalize();
            M47DragonProjectileExtension extension = def.GetModExtension<M47DragonProjectileExtension>();
            float maximumDeviation = Mathf.Clamp(
                extension?.maximumLaunchDeviationDegrees ?? 55f,
                1f,
                89f);
            float signedDeviation = Vector3.SignedAngle(
                initialGuidanceDirection,
                direction,
                Vector3.up);
            return Quaternion.AngleAxis(
                Mathf.Clamp(signedDeviation, -maximumDeviation, maximumDeviation),
                Vector3.up) * initialGuidanceDirection;
        }

        private void UpdateTrackedTargetVector(Vector3 currentTargetPosition)
        {
            int currentTick = Find.TickManager.TicksGame;
            if (!intendedTarget.HasThing)
            {
                trackedTargetVelocity = Vector3.zero;
                targetVectorInitialized = false;
                return;
            }

            if (!targetVectorInitialized)
            {
                lastTrackedTargetPosition = currentTargetPosition;
                lastTargetVectorSampleTick = currentTick;
                targetVectorInitialized = true;
                return;
            }

            int elapsedTicks = Mathf.Max(1, currentTick - lastTargetVectorSampleTick);
            Vector3 measuredVelocity = (currentTargetPosition - lastTrackedTargetPosition) / elapsedTicks;
            measuredVelocity.y = 0f;

            M47DragonProjectileExtension extension = def.GetModExtension<M47DragonProjectileExtension>();
            float smoothing = Mathf.Clamp01(extension?.targetVectorSmoothing ?? 0.45f);
            trackedTargetVelocity = Vector3.Lerp(trackedTargetVelocity, measuredVelocity, smoothing);
            lastTrackedTargetPosition = currentTargetPosition;
            lastTargetVectorSampleTick = currentTick;
        }

        private Vector3 ActiveLeadPosition(Vector3 missilePosition, Vector3 targetPosition)
        {
            if (!intendedTarget.HasThing || trackedTargetVelocity.sqrMagnitude < 0.000001f)
            {
                return targetPosition;
            }

            M47DragonProjectileExtension extension = def.GetModExtension<M47DragonProjectileExtension>();
            float leadStartDistance = Mathf.Max(1f, extension?.vectorLeadStartDistance ?? 45f);
            float leadFullDistance = Mathf.Clamp(extension?.vectorLeadFullDistance ?? 10f, 0f, leadStartDistance - 0.1f);
            float distanceToTarget = Vector3.Distance(missilePosition, targetPosition);

            // Smoothly move from almost no velocity compensation at long range
            // to full lead near the target.
            float proximity = Mathf.InverseLerp(leadStartDistance, leadFullDistance, distanceToTarget);
            proximity = proximity * proximity * (3f - 2f * proximity);

            float speedPerTick = Mathf.Max(0.0001f, def.projectile.SpeedTilesPerTick);
            float interceptTicks = distanceToTarget / speedPerTick;
            float maximumLeadTicks = Mathf.Max(0f, extension?.maximumLeadTicks ?? 45f);
            float predictedTicks = Mathf.Min(interceptTicks, maximumLeadTicks) * proximity;
            return targetPosition + trackedTargetVelocity * predictedTicks;
        }

        private float TerminalGuidanceFactor(float distanceToTarget)
        {
            M47DragonProjectileExtension extension = def.GetModExtension<M47DragonProjectileExtension>();
            float terminalDistance = Mathf.Max(0.1f, extension?.terminalGuidanceDistance ?? 14f);
            float factor = 1f - Mathf.Clamp01(distanceToTarget / terminalDistance);
            return factor * factor * (3f - 2f * factor);
        }

        private float SteeringOscillationDegrees(float terminalFactor)
        {
            M47DragonProjectileExtension extension = def.GetModExtension<M47DragonProjectileExtension>();
            float baseAmplitude = Mathf.Max(0f, extension?.steeringOscillationDegrees ?? 1.1f);
            float accuracyInfluence = Mathf.Clamp01(extension?.accuracyInfluence ?? 0.45f);
            float phaseStep = Mathf.Max(0.01f, extension?.oscillationStepRadians ?? 1.35f);

            float operatorAccuracy = 1f;
            if (Launcher is Pawn operatorPawn)
            {
                operatorAccuracy = operatorPawn.GetStatValue(StatDefOf.ShootingAccuracyPawn);
            }

            float accuracyFactor = Mathf.InverseLerp(0.45f, 1.45f, operatorAccuracy);
            float amplitudeMultiplier = Mathf.Lerp(
                1f + accuracyInfluence,
                1f - accuracyInfluence * 0.7f,
                accuracyFactor);
            float terminalOscillationFactor = Mathf.Clamp01(extension?.terminalOscillationFactor ?? 0.15f);
            amplitudeMultiplier *= Mathf.Lerp(1f, terminalOscillationFactor, terminalFactor);
            float phase = thingIDNumber * 0.173f + steeringPulseCount * phaseStep;
            return Mathf.Sin(phase) * baseAmplitude * amplitudeMultiplier;
        }

        private void EmitSteeringEffects()
        {
            if (!HasActiveSteeringFrame || Map == null)
            {
                return;
            }

            Vector3 effectPosition = ExactPosition;
            FleckMaker.ThrowFireGlow(effectPosition, Map, 0.42f);
            FleckMaker.ThrowMicroSparks(effectPosition, Map);
            FleckMaker.ThrowSmoke(effectPosition, Map, SteeringSmokeSize);
        }

        private float DistanceToMapEdge(Vector3 position, Vector3 direction)
        {
            if (Map == null)
            {
                return float.MaxValue;
            }

            // Keep the endpoint far enough inside the map that ToIntVec3 cannot
            // round it into the first out-of-bounds cell.
            const float minimumCoordinate = 0.01f;
            float maximumX = Map.Size.x - 0.01f;
            float maximumZ = Map.Size.z - 0.01f;
            float distance = float.MaxValue;

            if (direction.x > 0.0001f)
            {
                distance = Mathf.Min(distance, (maximumX - position.x) / direction.x);
            }
            else if (direction.x < -0.0001f)
            {
                distance = Mathf.Min(distance, (minimumCoordinate - position.x) / direction.x);
            }

            if (direction.z > 0.0001f)
            {
                distance = Mathf.Min(distance, (maximumZ - position.z) / direction.z);
            }
            else if (direction.z < -0.0001f)
            {
                distance = Mathf.Min(distance, (minimumCoordinate - position.z) / direction.z);
            }

            return Mathf.Max(0.01f, distance);
        }

        private static string TextureSuffixForFinalDirection(float signedAngle)
        {
            // Unity's signed yaw is opposite to the left/right placement used
            // by the authored M222 steering frames.
            signedAngle = -signedAngle;
            float absoluteAngle = Mathf.Abs(signedAngle);
            if (absoluteAngle <= 0.35f)
            {
                return "up";
            }

            float normalizedAngle = Mathf.Repeat(signedAngle + 180f, 360f) - 180f;
            if (normalizedAngle >= -22.5f && normalizedAngle < 22.5f)
            {
                return normalizedAngle > 0f ? "midright" : "midleft";
            }
            if (normalizedAngle >= 22.5f && normalizedAngle < 67.5f)
            {
                return "upright";
            }
            if (normalizedAngle >= 67.5f && normalizedAngle < 112.5f)
            {
                return "right";
            }
            if (normalizedAngle >= 112.5f && normalizedAngle < 157.5f)
            {
                return "downright";
            }
            if (normalizedAngle >= 157.5f || normalizedAngle < -157.5f)
            {
                return "down";
            }
            if (normalizedAngle >= -157.5f && normalizedAngle < -112.5f)
            {
                return "downleft";
            }
            if (normalizedAngle >= -112.5f && normalizedAngle < -67.5f)
            {
                return "left";
            }

            return "upleft";
        }

        private void DrawSteeringPulse(Vector3 drawLoc)
        {
            if (!HasActiveSteeringFrame)
            {
                return;
            }

            Material material = MaterialPool.MatFrom(PulseTextureRoot + steeringTextureSuffix, ShaderDatabase.Cutout);
            drawLoc = DrawPos;
            drawLoc.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.001f;
            Vector2 drawSize = def.graphicData?.drawSize ?? new Vector2(0.9f, 0.9f);
            float rotationDegrees = Mathf.Atan2(flightDirection.x, flightDirection.z) * Mathf.Rad2Deg;
            Vector3 scale = new Vector3(
                drawSize.x * SteeringTextureScale / 10f,
                1f,
                drawSize.y * SteeringTextureScale / 10f);
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(rotationDegrees, Vector3.up), scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
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
                Vector3 straightLinePoint = Vector3.Lerp(launcherPoint, missilePoint, t);
                float followFactor = Mathf.Lerp(
                    WirePathFollowFactorNearLauncher,
                    WirePathFollowFactorNearMissile,
                    t);
                wirePath[i] = Vector3.Lerp(wirePath[i], straightLinePoint, followFactor);
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
                wirePath.Add(Vector3.Lerp(launcherPoint, missilePoint, i / (float)(WirePathPointCount - 1)));
            }
        }

        private void DrawWire(float side)
        {
            if (wirePath == null || wirePath.Count < 2)
            {
                return;
            }

            Vector3 previous = OffsetWirePoint(0, side);
            previous.y = AltitudeLayer.MoteOverhead.AltitudeFor();

            for (int i = 1; i < wirePath.Count; i++)
            {
                Vector3 current = OffsetWirePoint(i, side);
                current.y = previous.y;
                GenDraw.DrawLineBetween(previous, current, WireMaterial, WireWidth);
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
                    int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(segmentStart, segmentEnd) / CollisionSampleSpacing));
                    for (int sampleIndex = 0; sampleIndex <= samples; sampleIndex++)
                    {
                        Vector3 point = Vector3.Lerp(segmentStart, segmentEnd, sampleIndex / (float)samples);
                        IntVec3 cell = point.ToIntVec3();
                        if (!cell.InBounds(Map))
                        {
                            continue;
                        }

                        foreach (Thing thing in cell.GetThingList(Map))
                        {
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
            if (Map == null || Launcher == null || Launcher.Destroyed || Launcher.Map != Map)
            {
                return false;
            }

            launcherPoint = Launcher.DrawPos;
            missilePoint = ExactPosition;
            launcherPoint.y = 0f;
            missilePoint.y = 0f;
            return true;
        }

        private Vector3 OffsetWirePoint(int index, float side)
        {
            int previousIndex = Mathf.Max(0, index - 1);
            int nextIndex = Mathf.Min(wirePath.Count - 1, index + 1);
            Vector3 tangent = wirePath[nextIndex] - wirePath[previousIndex];
            tangent.y = 0f;
            Vector3 perpendicular = tangent.sqrMagnitude > 0.001f
                ? new Vector3(-tangent.z, 0f, tangent.x).normalized
                : Vector3.right;
            return wirePath[index] + perpendicular * side * WireSeparation;
        }
    }
}
