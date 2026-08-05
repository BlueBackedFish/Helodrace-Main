using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public class CompProperties_M2A2HowitzerGraphic : CompProperties
    {
        public string baseTexPath;
        public string bodyOutlineTexPath;
        public string bodyTexPath;
        public string barrelOutlineTexPath;
        public string barrelTexPath;
        public string lever1OutlineTexPath;
        public string lever1TexPath;
        public string lever2OutlineTexPath;
        public string lever2TexPath;
        public string lever3OutlineTexPath;
        public string lever3TexPath;
        public bool useM114LayerLayout;
        public string topUnderOutlineTexPath;
        public string topUnderTexPath;
        public string topTopOutlineTexPath;
        public string topTopTexPath;
        public string chamberOutlineTexPath;
        public string chamberTexPath;
        public float m114Lever1AngleDegrees = 66f;
        public float m114GraphicDownOffset;
        public float m114GraphicRightOffset = 0.10f;
        public float chamberLeftOffset = 0.05f;
        public float chamberDownOffset = 0.04f;
        public float chamberOpenAngleDegrees = 72f;
        public int chamberOpenDelayTicks = 86;
        public int chamberOpenTicks = 24;
        public int chamberMinimumOpenTicks = 50;
        public int chamberCloseTicks = 24;
        public float drawSize = 3.2f;
        public float rotationOffsetDegrees = -90f;
        public float recoilDistance = 0.66f;
        public int recoilTicks = 24;
        public float lever1Stroke = 0.112f;
        public float lever2Stroke = 0.16f;
        public float lever3Stroke = 0.130667f;
        public string casingTexPath;
        public string casingDropSoundDef;
        public float casingDrawSize = 9f;
        public float casingEjectDistance = 0.9f;
        public int casingDropDelayTicks = 18;
        public int casingBounceTicks = 14;
        public int casingSmokeStartTicks = 3;
        public int casingSmokeIntervalTicks = 4;
        public float casingSmokeSize = 0.18f;
        public float firingDustRadius = 4f;
        public float rearSmokeRadius = 4f;
        public float frontSmokeMinRadius = 1f;
        public float frontSmokeMaxRadius = 8f;
        public float frontSmokeOriginOffset = 2f;
        public float frontSmokeHalfAngle = 65f;
        public float frontSmokeCoreHalfAngle = 50f;
        public float screenShakeMagnitude = 1.1f;
        public int screenShakeDurationTicks = 12;

        public CompProperties_M2A2HowitzerGraphic()
        {
            compClass = typeof(CompM2A2HowitzerGraphic);
        }
    }

    /// <summary>
    /// Draws the M2A2 from artwork authored as aligned 1024x1024 layers.
    /// The carriage, receiver and control levers remain fixed while the two
    /// barrel layers recoil together after a successful shot.
    /// </summary>
    public class CompM2A2HowitzerGraphic : ThingComp
    {
        private const float LayerStep = 0.0015f;

        private int lastShotTick = -99999;
        private int casingEjectTick = -99999;
        private bool casingDropPending;
        private int warmupStartTick = -1;
        private int warmupDurationTicks;
        private bool displayedRotationInitialized;
        private float displayedRotationDegrees;
        private float casingEjectRotationDegrees;
        private float lastAimedDistance = -1f;
        private float pendingAimDistance = -1f;
        private float lastFiredAimRotationDegrees;
        private bool hasLastFiredAimRotation;
        private int lever1Cycles = 2;
        private int lever2Cycles = 1;
        private int lever3Cycles = 2;
        private bool chamberCycleActive;
        private int chamberCloseStartTick = -1;
        private Material baseMaterial;
        private Material bodyOutlineMaterial;
        private Material bodyMaterial;
        private Material barrelOutlineMaterial;
        private Material barrelMaterial;
        private Material lever1OutlineMaterial;
        private Material lever1Material;
        private Material lever2OutlineMaterial;
        private Material lever2Material;
        private Material lever3OutlineMaterial;
        private Material lever3Material;
        private Material topUnderOutlineMaterial;
        private Material topUnderMaterial;
        private Material topTopOutlineMaterial;
        private Material topTopMaterial;
        private Material chamberOutlineMaterial;
        private Material chamberMaterial;
        private Material casingMaterial;
        private FleckDef smokeScreenFleck;

        private CompProperties_M2A2HowitzerGraphic Props =>
            (CompProperties_M2A2HowitzerGraphic)props;

        private float InitialGraphicRotationDegrees =>
            parent.Rotation.AsInt * 90f + Props.rotationOffsetDegrees;

        public void NotifyShotFired()
        {
            lastShotTick = Find.TickManager.TicksGame;
            if (Props.useM114LayerLayout)
            {
                chamberCycleActive = true;
                chamberCloseStartTick = -1;
            }
            casingEjectTick = lastShotTick;
            casingDropPending = true;
            if (parent is Building_TurretGun turret && turret.Top != null)
            {
                displayedRotationDegrees =
                    turret.Top.CurRotation + Props.rotationOffsetDegrees;
                displayedRotationInitialized = true;
            }
            casingEjectRotationDegrees = displayedRotationInitialized
                ? displayedRotationDegrees
                : InitialGraphicRotationDegrees;
            if (pendingAimDistance >= 0f)
            {
                lastAimedDistance = pendingAimDistance;
            }
            lastFiredAimRotationDegrees = casingEjectRotationDegrees;
            hasLastFiredAimRotation = true;
            SpawnImmediateFiringEffects();
            warmupStartTick = -1;
            warmupDurationTicks = 0;
        }

        public void NotifyWarmupStarted(int durationTicks)
        {
            if (durationTicks <= 0)
            {
                return;
            }

            warmupStartTick = Find.TickManager.TicksGame;
            warmupDurationTicks = durationTicks;
            ConfigureLeverMotion();
        }

        public override void CompTick()
        {
            base.CompTick();
            UpdateChamberCycle();

            int age = Find.TickManager.TicksGame - casingEjectTick;
            if (age >= Props.casingSmokeStartTicks
                && age <= Props.casingDropDelayTicks + Props.casingBounceTicks
                && Props.casingSmokeIntervalTicks > 0
                && age % Props.casingSmokeIntervalTicks == 0
                && parent.Map != null)
            {
                Quaternion smokeRotation = Quaternion.AngleAxis(
                    casingEjectRotationDegrees,
                    Vector3.up);
                Vector3 backward = -(smokeRotation * Vector3.right);
                TryThrowSmoke(
                    parent.DrawPos + backward * 0.24f,
                    Props.casingSmokeSize);
            }

            if (!casingDropPending || age < Props.casingDropDelayTicks)
            {
                return;
            }

            casingDropPending = false;
            if (!(parent is Building_TurretGun) || parent.Map == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.AngleAxis(
                casingEjectRotationDegrees,
                Vector3.up);
            IntVec3 soundCell = CasingLandingPosition(rotation).ToIntVec3();
            if (!soundCell.InBounds(parent.Map))
            {
                soundCell = parent.Position;
            }

            if (!Props.casingDropSoundDef.NullOrEmpty())
            {
                DefDatabase<SoundDef>.GetNamedSilentFail(Props.casingDropSoundDef)
                    ?.PlayOneShot(new TargetInfo(soundCell, parent.Map));
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();

            if (!(parent is Building_TurretGun turret) || turret.Top == null)
            {
                return;
            }

            float graphicRotationDegrees = GraphicRotationDegrees(turret);
            Quaternion rotation = Quaternion.AngleAxis(graphicRotationDegrees, Vector3.up);
            Vector3 forward = rotation * Vector3.right;
            Vector3 drawPos = parent.DrawPos;
            drawPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            Vector3 scale = new Vector3(Props.drawSize, 1f, Props.drawSize);

            if (Props.useM114LayerLayout)
            {
                DrawM114Layers(turret, drawPos, rotation, forward, scale);
                DrawEjectedCasing();
                return;
            }

            GetLeverOffsets(
                turret,
                rotation,
                out Vector3 lever1Offset,
                out Vector3 lever2Offset,
                out Vector3 lever3Offset);

            // Keep every outline behind all filled artwork. The barrel outline
            // still follows the barrel recoil so both silhouettes stay aligned.
            DrawLayer(ref drawPos, rotation, scale, BodyOutlineMaterial);
            Vector3 barrelPos = drawPos - forward * CurrentRecoil;
            DrawLayer(ref barrelPos, rotation, scale, BarrelOutlineMaterial);
            drawPos.y = barrelPos.y;
            DrawMovingLayer(ref drawPos, lever1Offset, rotation, scale, Lever1OutlineMaterial);
            DrawMovingLayer(ref drawPos, lever2Offset, rotation, scale, Lever2OutlineMaterial);
            DrawMovingLayer(ref drawPos, lever3Offset, rotation, scale, Lever3OutlineMaterial);

            // Filled artwork order: carriage, controls, receiver, recoiling tube.
            DrawLayer(ref drawPos, rotation, scale, BaseMaterial);
            DrawMovingLayer(ref drawPos, lever1Offset, rotation, scale, Lever1Material);
            DrawMovingLayer(ref drawPos, lever2Offset, rotation, scale, Lever2Material);
            DrawMovingLayer(ref drawPos, lever3Offset, rotation, scale, Lever3Material);
            DrawLayer(ref drawPos, rotation, scale, BodyMaterial);

            barrelPos = drawPos - forward * CurrentRecoil;
            DrawLayer(ref barrelPos, rotation, scale, BarrelMaterial);
            DrawEjectedCasing();
        }

        private void DrawM114Layers(
            Building_TurretGun turret,
            Vector3 drawPos,
            Quaternion rotation,
            Vector3 forward,
            Vector3 scale)
        {
            drawPos -= forward * Props.m114GraphicDownOffset;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            drawPos += right * Props.m114GraphicRightOffset;
            GetM114LeverOffsets(
                turret,
                rotation,
                out Vector3 lever1Offset,
                out Vector3 lever2Offset);
            Vector3 chamberOffset = ChamberStaticOffset(rotation);
            float chamberAngle = -Props.chamberOpenAngleDegrees
                * CurrentChamberOpenFraction;

            // All separate outline textures remain below every filled layer.
            // Recoiling silhouettes use the same displacement as their art.
            DrawLayer(ref drawPos, rotation, scale, TopUnderOutlineMaterial);
            DrawRotatedOffsetRecoilLayer(
                ref drawPos,
                forward,
                chamberOffset,
                rotation,
                scale,
                ChamberOutlineMaterial,
                chamberAngle);
            DrawRecoilLayer(
                ref drawPos,
                forward,
                rotation,
                scale,
                BarrelOutlineMaterial);
            DrawLayer(ref drawPos, rotation, scale, TopTopOutlineMaterial);
            DrawMovingLayer(ref drawPos, lever1Offset, rotation, scale, Lever1OutlineMaterial);
            DrawMovingLayer(ref drawPos, lever2Offset, rotation, scale, Lever2OutlineMaterial);

            // Filled order from bottom to top: carriage, lower cradle,
            // chamber, barrel, upper cradle, then the two control handles.
            DrawLayer(ref drawPos, rotation, scale, BaseMaterial);
            DrawLayer(ref drawPos, rotation, scale, TopUnderMaterial);
            DrawRotatedOffsetRecoilLayer(
                ref drawPos,
                forward,
                chamberOffset,
                rotation,
                scale,
                ChamberMaterial,
                chamberAngle);
            DrawRecoilLayer(
                ref drawPos,
                forward,
                rotation,
                scale,
                BarrelMaterial);
            DrawLayer(ref drawPos, rotation, scale, TopTopMaterial);
            DrawMovingLayer(ref drawPos, lever1Offset, rotation, scale, Lever1Material);
            DrawMovingLayer(ref drawPos, lever2Offset, rotation, scale, Lever2Material);
        }

        private void DrawEjectedCasing()
        {
            int age = Find.TickManager.TicksGame - casingEjectTick;
            int visualTicks = Props.casingDropDelayTicks + Props.casingBounceTicks;
            if (age < 0 || age >= visualTicks || CasingMaterial == null)
            {
                return;
            }

            Quaternion ejectionRotation = Quaternion.AngleAxis(
                casingEjectRotationDegrees,
                Vector3.up);
            Vector3 backward = -(ejectionRotation * Vector3.right);
            Vector3 side = ejectionRotation * Vector3.forward;
            Vector3 position;
            float casingAngle = casingEjectRotationDegrees;

            if (age < Props.casingDropDelayTicks)
            {
                // The airborne phase is a clean, straight rearward ejection.
                float flightProgress = age
                    / (float)Mathf.Max(1, Props.casingDropDelayTicks);
                position = parent.DrawPos
                    + backward * (0.22f + Props.casingEjectDistance * flightProgress);
            }
            else
            {
                // Rock and make two short, diminishing hops only after impact.
                float bounceProgress = (age - Props.casingDropDelayTicks)
                    / (float)Mathf.Max(1, Props.casingBounceTicks);
                float envelope = 1f - bounceProgress;
                float hop = Mathf.Abs(Mathf.Sin(bounceProgress * Mathf.PI * 2f))
                    * 0.12f * envelope;
                float rock = Mathf.Sin(bounceProgress * Mathf.PI * 4f) * envelope;
                position = CasingLandingPosition(ejectionRotation)
                    + backward * hop
                    + side * (rock * 0.055f);
                casingAngle += rock * 22f;
            }

            position.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + 0.03f;

            Quaternion casingRotation = Quaternion.AngleAxis(casingAngle, Vector3.up);
            Vector3 casingScale = new Vector3(
                Props.casingDrawSize / 10f,
                1f,
                Props.casingDrawSize / 10f);
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(position, casingRotation, casingScale),
                CasingMaterial,
                0);
        }

        private Vector3 CasingLandingPosition(Quaternion rotation)
        {
            Vector3 backward = -(rotation * Vector3.right);
            return parent.DrawPos + backward * (0.22f + Props.casingEjectDistance);
        }

        private void SpawnImmediateFiringEffects()
        {
            if (parent.Map == null)
            {
                return;
            }

            Find.CameraDriver?.shaker?.DoShake(
                Props.screenShakeMagnitude,
                Props.screenShakeDurationTicks);

            Quaternion shotRotation = Quaternion.AngleAxis(
                casingEjectRotationDegrees,
                Vector3.up);
            Vector3 forward = shotRotation * Vector3.right;

            // Scatter uniformly across the area of a radius-four disc. Taking
            // the square root of the random radius avoids crowding the center.
            const int dustPoints = 72;
            for (int i = 0; i < dustPoints; i++)
            {
                float angle = Rand.Range(0f, 360f);
                float radius = Mathf.Sqrt(Rand.Value) * Props.firingDustRadius;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up)
                    * Vector3.right;
                TryThrowDust(
                    parent.DrawPos + direction * radius,
                    Rand.Range(0.42f, 0.62f));
            }

            // Two dense radial bands form the 60-degree rear dust arc. Keep
            // this as short-lived ground dust without any lingering smoke.
            const int rearArcPoints = 13;
            const int rearArcBands = 2;
            Vector3 rearward = -forward;
            for (int band = 0; band < rearArcBands; band++)
            {
                float radius = Props.rearSmokeRadius
                    * Mathf.Lerp(0.78f, 1f, band);
                for (int i = 0; i < rearArcPoints; i++)
                {
                    float angle = Mathf.Lerp(-30f, 30f, i / (rearArcPoints - 1f));
                    Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up)
                        * rearward;
                    Vector3 position = parent.DrawPos + direction * radius;
                    TryThrowDust(position, Rand.Range(0.46f, 0.68f));
                }
            }

            SpawnFrontSmokeSector();
        }

        private void SpawnFrontSmokeSector()
        {
            if (parent.Map == null)
            {
                return;
            }

            Quaternion shotRotation = Quaternion.AngleAxis(
                casingEjectRotationDegrees,
                Vector3.up);
            Vector3 forward = shotRotation * Vector3.right;
            Vector3 smokeOrigin =
                parent.DrawPos + forward * Props.frontSmokeOriginOffset;
            // Scatter only the vanilla smoke-shell burst visual throughout the
            // complete 100-degree fan. This has no gas or accuracy gameplay.
            // Random area sampling removes the visible concentric arcs.
            const int smokeCandidates = 420;
            float minRadiusSquared =
                Props.frontSmokeMinRadius * Props.frontSmokeMinRadius;
            float featheredMaxRadius = Props.frontSmokeMaxRadius * 1.1f;
            float featheredMaxRadiusSquared =
                featheredMaxRadius * featheredMaxRadius;
            for (int i = 0; i < smokeCandidates; i++)
            {
                float angle = Rand.Range(
                    -Props.frontSmokeHalfAngle,
                    Props.frontSmokeHalfAngle);
                float radius = Mathf.Sqrt(Mathf.Lerp(
                    minRadiusSquared,
                    featheredMaxRadiusSquared,
                    Rand.Value));

                // Full density in the central fan, then progressively sparse,
                // irregular fringes along both sides and the outer curve.
                float angleEdge = Mathf.InverseLerp(
                    Props.frontSmokeHalfAngle,
                    Props.frontSmokeCoreHalfAngle,
                    Mathf.Abs(angle));
                float radiusEdge = Mathf.InverseLerp(
                    featheredMaxRadius,
                    Props.frontSmokeMaxRadius * 0.78f,
                    radius);
                if (!Rand.Chance(angleEdge * radiusEdge))
                {
                    continue;
                }

                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up)
                    * forward;
                float edgeJitter = Rand.Range(-0.18f, 0.18f);
                TrySpawnSmokeScreenVisual(
                    smokeOrigin + direction * (radius + edgeJitter));
            }
        }

        private void TryThrowDust(Vector3 position, float size)
        {
            if (parent.Map != null && position.ToIntVec3().InBounds(parent.Map))
            {
                FleckMaker.ThrowDustPuff(position, parent.Map, size);
            }
        }

        private void TryThrowSmoke(Vector3 position, float size)
        {
            if (parent.Map != null && position.ToIntVec3().InBounds(parent.Map))
            {
                FleckMaker.Static(position, parent.Map, FleckDefOf.Smoke, size);
            }
        }

        private void TrySpawnSmokeScreenVisual(Vector3 position)
        {
            IntVec3 cell = position.ToIntVec3();
            FleckDef fleck = SmokeScreenFleck;
            if (parent.Map != null && cell.InBounds(parent.Map) && fleck != null)
            {
                FleckMaker.Static(
                    position,
                    parent.Map,
                    fleck,
                    Rand.Range(0.8f, 1.2f));
            }
        }

        private FleckDef SmokeScreenFleck =>
            smokeScreenFleck ?? (smokeScreenFleck =
                DefDatabase<FleckDef>.GetNamedSilentFail("HD_M2A2SmokeScreenVisual"));

        private float GraphicRotationDegrees(Building_TurretGun turret)
        {
            if (!displayedRotationInitialized)
            {
                displayedRotationDegrees = InitialGraphicRotationDegrees;
                displayedRotationInitialized = true;
            }

            // Follow the real turret angle only while it has a target. Once
            // the target is cleared, retain the last aimed direction instead
            // of displaying Building_TurretGun's random idle scanning turns.
            if (turret?.Top != null && turret.CurrentTarget.IsValid)
            {
                displayedRotationDegrees =
                    turret.Top.CurRotation + Props.rotationOffsetDegrees;
            }

            return displayedRotationDegrees;
        }

        private void GetLeverOffsets(
            Building_TurretGun turret,
            Quaternion rotation,
            out Vector3 lever1Offset,
            out Vector3 lever2Offset,
            out Vector3 lever3Offset)
        {
            lever1Offset = Vector3.zero;
            lever2Offset = Vector3.zero;
            lever3Offset = Vector3.zero;

            float progress = WarmupAnimationProgress(turret);
            if (progress < 0f || progress >= 0.94f)
            {
                return;
            }

            Vector3 localRight = rotation * Vector3.right;
            float elevationStroke = DetailedHandwheelStroke(
                WindowProgress(progress, 0f, 0.60f),
                lever1Cycles);
            lever1Offset = -localRight * (Props.lever1Stroke * elevationStroke);

            float traverseStroke = DetailedHandwheelStroke(
                WindowProgress(progress, 0.16f, 0.74f),
                lever2Cycles);
            lever2Offset = localRight * (Props.lever2Stroke * traverseStroke);

            float sightStroke = DetailedHandwheelStroke(
                WindowProgress(progress, 0.43f, 0.91f),
                lever3Cycles);
            float angle = 104f * Mathf.Deg2Rad;
            Vector3 lever3Direction = rotation * new Vector3(
                Mathf.Cos(angle),
                0f,
                -Mathf.Sin(angle));
            lever3Offset = lever3Direction * (Props.lever3Stroke * sightStroke);
        }

        private void GetM114LeverOffsets(
            Building_TurretGun turret,
            Quaternion rotation,
            out Vector3 lever1Offset,
            out Vector3 lever2Offset)
        {
            lever1Offset = Vector3.zero;
            lever2Offset = Vector3.zero;

            float progress = WarmupAnimationProgress(turret);
            if (progress < 0f || progress >= 0.94f)
            {
                return;
            }

            float lever1Stroke = EndpointHarmonicStroke(
                WindowProgress(progress, 0f, 0.60f),
                lever1Cycles);
            float angle = Props.m114Lever1AngleDegrees * Mathf.Deg2Rad;
            // The artwork is end-aligned, not center-anchored. Move the whole
            // handle on the axis 90 degrees from its authored 66-degree angle.
            Vector3 lever1Direction = rotation * new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle));
            lever1Offset = lever1Direction * (Props.lever1Stroke * lever1Stroke);

            float lever2Stroke = EndpointHarmonicStroke(
                WindowProgress(progress, 0.16f, 0.74f),
                lever2Cycles);
            // Lever 2 is horizontal in the artwork, so its travel is vertical.
            Vector3 lever2Direction = rotation * Vector3.right;
            lever2Offset = lever2Direction * (Props.lever2Stroke * lever2Stroke);
        }

        private Vector3 ChamberStaticOffset(Quaternion rotation)
        {
            Vector3 staticOffset = new Vector3(
                -Props.chamberDownOffset,
                0f,
                Props.chamberLeftOffset);
            return rotation * staticOffset;
        }

        private void ConfigureLeverMotion()
        {
            if (!(parent is Building_TurretGun turret) || turret.Top == null)
            {
                lever1Cycles = 2;
                lever2Cycles = 1;
                lever3Cycles = 2;
                pendingAimDistance = -1f;
                return;
            }

            LocalTargetInfo target = turret.CurrentTarget;
            float distance = target.IsValid
                ? parent.Position.DistanceTo(target.Cell)
                : Mathf.Max(0f, lastAimedDistance);
            pendingAimDistance = distance;

            Verb attackVerb = turret.AttackVerb;
            float minimumRange = attackVerb?.verbProps?.minRange ?? 0f;
            float maximumRange = attackVerb?.verbProps?.range ?? 120f;
            float practicalMaximum = Mathf.Min(maximumRange, 120f);
            float rangeFactor = Mathf.InverseLerp(
                minimumRange,
                Mathf.Max(minimumRange + 1f, practicalMaximum),
                distance);
            float rangeChangeFactor = lastAimedDistance < 0f
                ? rangeFactor
                : Mathf.Clamp01(Mathf.Abs(distance - lastAimedDistance) / 40f);

            // Elevation wheel: large range corrections need more turns.
            lever1Cycles = Mathf.Clamp(
                2 + Mathf.RoundToInt(rangeChangeFactor * 4f),
                2,
                6);

            float targetRotation =
                turret.Top.CurRotation + Props.rotationOffsetDegrees;
            float referenceRotation = hasLastFiredAimRotation
                ? lastFiredAimRotationDegrees
                : InitialGraphicRotationDegrees;
            float traverseDelta = Mathf.Abs(
                Mathf.DeltaAngle(referenceRotation, targetRotation));

            // Traverse wheel: roughly one additional turn per 20 degrees.
            lever2Cycles = Mathf.Clamp(
                1 + Mathf.CeilToInt(traverseDelta / 20f),
                1,
                6);

            // Sight adjustment is quicker and finer, scaling with range.
            lever3Cycles = Mathf.Clamp(
                2 + Mathf.RoundToInt(rangeFactor * 3f),
                2,
                5);
        }

        private float WarmupAnimationProgress(Building_TurretGun turret)
        {
            int remainingTicks = Patch_BuildingTurretGun_WarmupAnimation_M2A2
                .WarmupTicksRemaining(turret);
            if (remainingTicks <= 0)
            {
                warmupStartTick = -1;
                warmupDurationTicks = 0;
                return -1f;
            }

            if (warmupStartTick < 0 || warmupDurationTicks <= 0)
            {
                warmupStartTick = Find.TickManager.TicksGame;
                warmupDurationTicks = Mathf.Max(1, remainingTicks);
            }

            int age = Find.TickManager.TicksGame - warmupStartTick;
            return Mathf.Clamp01(age / (float)Mathf.Max(1, warmupDurationTicks));
        }

        private static float WindowProgress(
            float totalProgress,
            float start,
            float end)
        {
            if (totalProgress < start || totalProgress >= end)
            {
                return -1f;
            }

            return Mathf.InverseLerp(start, end, totalProgress);
        }

        private static float DetailedHandwheelStroke(float phase, int cycles)
        {
            if (phase < 0f)
            {
                return 0f;
            }

            phase = Mathf.Clamp01(phase);
            const float turningFraction = 0.82f;
            if (phase < turningFraction)
            {
                float turningPhase = phase / turningFraction;
                return 0.5f - 0.5f * Mathf.Cos(
                    turningPhase * cycles * 2f * Mathf.PI);
            }

            // A short reverse kick followed by two rapidly damped oscillations
            // gives each handwheel a mechanical stop instead of a soft glide.
            float settle = Mathf.InverseLerp(turningFraction, 1f, phase);
            float envelope = (1f - settle) * (1f - settle);
            return -Mathf.Sin(settle * Mathf.PI * 4f) * envelope * 0.22f;
        }

        private static float EndpointHarmonicStroke(float phase, int cycles)
        {
            if (phase < 0f)
            {
                return 0f;
            }

            float radians = Mathf.Clamp01(phase) * cycles * 2f * Mathf.PI;
            return 0.5f - 0.5f * Mathf.Cos(radians);
        }

        private float CurrentChamberOpenFraction
        {
            get
            {
                if (!chamberCycleActive)
                {
                    return 0f;
                }

                int age = Find.TickManager.TicksGame - lastShotTick;
                int openingStart = Props.chamberOpenDelayTicks;
                int fullyOpenTick = openingStart + Props.chamberOpenTicks;

                if (age < openingStart)
                {
                    return 0f;
                }

                if (age < fullyOpenTick)
                {
                    float progress = (age - openingStart)
                        / (float)Mathf.Max(1, Props.chamberOpenTicks);
                    return SmoothStep01(progress);
                }

                if (chamberCloseStartTick < 0)
                {
                    return 1f;
                }

                float closingProgress =
                    (Find.TickManager.TicksGame - chamberCloseStartTick)
                    / (float)Mathf.Max(1, Props.chamberCloseTicks);
                return 1f - SmoothStep01(closingProgress);
            }
        }

        private void UpdateChamberCycle()
        {
            if (!Props.useM114LayerLayout || !chamberCycleActive)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            int earliestCloseTick = lastShotTick
                + Props.chamberOpenDelayTicks
                + Props.chamberOpenTicks
                + Props.chamberMinimumOpenTicks;

            if (chamberCloseStartTick < 0
                && now >= earliestCloseTick
                && IsNextRoundLoaded)
            {
                chamberCloseStartTick = now;
            }

            if (chamberCloseStartTick >= 0
                && now - chamberCloseStartTick >= Props.chamberCloseTicks)
            {
                chamberCycleActive = false;
                chamberCloseStartTick = -1;
            }
        }

        private bool IsNextRoundLoaded
        {
            get
            {
                Building_TurretGun turret = parent as Building_TurretGun;
                CompChangeableProjectile loader = turret?.GunCompEq?.parent?
                    .TryGetComp<CompChangeableProjectile>();
                return loader?.Loaded == true;
            }
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return 0.5f - 0.5f * Mathf.Cos(value * Mathf.PI);
        }

        private float CurrentRecoil
        {
            get
            {
                int age = Find.TickManager.TicksGame - lastShotTick;
                if (age < 0 || age >= Props.recoilTicks)
                {
                    return 0f;
                }

                float progress = age / (float)Props.recoilTicks;
                return Props.recoilDistance * (1f - progress * progress);
            }
        }

        private static void DrawLayer(
            ref Vector3 drawPos,
            Quaternion rotation,
            Vector3 scale,
            Material material)
        {
            if (material != null)
            {
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    Matrix4x4.TRS(drawPos, rotation, scale),
                    material,
                    0);
            }

            drawPos.y += LayerStep;
        }

        private static void DrawMovingLayer(
            ref Vector3 layerCursor,
            Vector3 movementOffset,
            Quaternion rotation,
            Vector3 scale,
            Material material)
        {
            Vector3 movingPos = layerCursor + movementOffset;
            DrawLayer(ref movingPos, rotation, scale, material);
            layerCursor.y = movingPos.y;
        }

        private void DrawRecoilLayer(
            ref Vector3 layerCursor,
            Vector3 forward,
            Quaternion rotation,
            Vector3 scale,
            Material material)
        {
            Vector3 movingPos = layerCursor - forward * CurrentRecoil;
            DrawLayer(ref movingPos, rotation, scale, material);
            layerCursor.y = movingPos.y;
        }

        private void DrawRotatedOffsetRecoilLayer(
            ref Vector3 layerCursor,
            Vector3 forward,
            Vector3 offset,
            Quaternion rotation,
            Vector3 scale,
            Material material,
            float additionalAngle)
        {
            Vector3 layerCenter = layerCursor
                - forward * CurrentRecoil
                + offset;
            Quaternion layerRotation = rotation
                * Quaternion.AngleAxis(additionalAngle, Vector3.up);
            DrawLayer(ref layerCenter, layerRotation, scale, material);
            layerCursor.y = layerCenter.y;
        }

        private static Material MaterialFrom(string texPath)
        {
            return texPath.NullOrEmpty()
                ? null
                : MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout);
        }

        private Material BaseMaterial =>
            baseMaterial ?? (baseMaterial = MaterialFrom(Props.baseTexPath));
        private Material BodyOutlineMaterial =>
            bodyOutlineMaterial ?? (bodyOutlineMaterial = MaterialFrom(Props.bodyOutlineTexPath));
        private Material BodyMaterial =>
            bodyMaterial ?? (bodyMaterial = MaterialFrom(Props.bodyTexPath));
        private Material BarrelOutlineMaterial =>
            barrelOutlineMaterial ?? (barrelOutlineMaterial = MaterialFrom(Props.barrelOutlineTexPath));
        private Material BarrelMaterial =>
            barrelMaterial ?? (barrelMaterial = MaterialFrom(Props.barrelTexPath));
        private Material Lever1OutlineMaterial =>
            lever1OutlineMaterial ?? (lever1OutlineMaterial = MaterialFrom(Props.lever1OutlineTexPath));
        private Material Lever1Material =>
            lever1Material ?? (lever1Material = MaterialFrom(Props.lever1TexPath));
        private Material Lever2OutlineMaterial =>
            lever2OutlineMaterial ?? (lever2OutlineMaterial = MaterialFrom(Props.lever2OutlineTexPath));
        private Material Lever2Material =>
            lever2Material ?? (lever2Material = MaterialFrom(Props.lever2TexPath));
        private Material Lever3OutlineMaterial =>
            lever3OutlineMaterial ?? (lever3OutlineMaterial = MaterialFrom(Props.lever3OutlineTexPath));
        private Material Lever3Material =>
            lever3Material ?? (lever3Material = MaterialFrom(Props.lever3TexPath));
        private Material TopUnderOutlineMaterial =>
            topUnderOutlineMaterial ?? (topUnderOutlineMaterial = MaterialFrom(Props.topUnderOutlineTexPath));
        private Material TopUnderMaterial =>
            topUnderMaterial ?? (topUnderMaterial = MaterialFrom(Props.topUnderTexPath));
        private Material TopTopOutlineMaterial =>
            topTopOutlineMaterial ?? (topTopOutlineMaterial = MaterialFrom(Props.topTopOutlineTexPath));
        private Material TopTopMaterial =>
            topTopMaterial ?? (topTopMaterial = MaterialFrom(Props.topTopTexPath));
        private Material ChamberOutlineMaterial =>
            chamberOutlineMaterial ?? (chamberOutlineMaterial = MaterialFrom(Props.chamberOutlineTexPath));
        private Material ChamberMaterial =>
            chamberMaterial ?? (chamberMaterial = MaterialFrom(Props.chamberTexPath));
        private Material CasingMaterial =>
            casingMaterial ?? (casingMaterial = MaterialFrom(Props.casingTexPath));
    }

    [HarmonyPatch(typeof(Building_TurretGun), "TryStartShootSomething")]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_BuildingTurretGun_WarmupAnimation_M2A2
    {
        private static readonly System.Reflection.FieldInfo BurstWarmupTicksLeftField =
            AccessTools.Field(typeof(Building_TurretGun), "burstWarmupTicksLeft");

        public static void Postfix(
            Building_TurretGun __instance,
            ref int ___burstWarmupTicksLeft)
        {
            if (___burstWarmupTicksLeft > 0)
            {
                __instance?.TryGetComp<CompM2A2HowitzerGraphic>()
                    ?.NotifyWarmupStarted(___burstWarmupTicksLeft);
            }
        }

        public static int WarmupTicksRemaining(Building_TurretGun turret)
        {
            return turret != null && BurstWarmupTicksLeftField != null
                ? (int)BurstWarmupTicksLeftField.GetValue(turret)
                : 1;
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_VerbLaunchProjectile_TryCastShot_M2A2HowitzerGraphic
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result && __instance.Caster is Building_TurretGun turret)
            {
                turret.TryGetComp<CompM2A2HowitzerGraphic>()?.NotifyShotFired();
            }
        }
    }
}
