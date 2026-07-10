using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace
{
    public enum SwitchbladeDroneMode
    {
        Deploying,
        Loiter,
        TurningToTarget,
        Diving
    }

    public class CompProperties_SwitchbladeTablet : CompProperties
    {
        public CompProperties_SwitchbladeTablet()
        {
            compClass = typeof(CompSwitchbladeTablet);
        }
    }

    public class CompSwitchbladeTablet : ThingComp
    {
        private Thing activeDrone;

        public SwitchbladeDrone ActiveDrone => activeDrone as SwitchbladeDrone;
        public bool HasActiveDrone => ActiveDrone is SwitchbladeDrone drone && !drone.Destroyed && drone.Spawned;

        private Pawn Wearer
        {
            get
            {
                if (parent.ParentHolder is Pawn_ApparelTracker tracker)
                {
                    return tracker.pawn;
                }

                return null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref activeDrone, "activeDrone");
        }

        public void SetActiveDrone(SwitchbladeDrone drone)
        {
            activeDrone = drone;
        }

        public bool CanLinkDrone()
        {
            return !HasActiveDrone;
        }

        private void BeginSelectLoiterCenter()
        {
            Pawn wearer = Wearer;
            SwitchbladeDrone drone = ActiveDrone;
            if (wearer?.Map == null || drone == null || drone.Destroyed || drone.Map != wearer.Map)
            {
                Messages.Message("HD_SwitchbladeTablet_NoLinkedDrone".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetPawns = false,
                validator = target => target.Cell.InBounds(wearer.Map)
            }, target =>
            {
                drone.SetLoiterCenter(target.Cell);
                Messages.Message("HD_SwitchbladeTablet_LoiterUpdated".Translate(), drone, MessageTypeDefOf.NeutralEvent, false);
            }, target =>
            {
                if (target.Cell.InBounds(wearer.Map))
                {
                    GenDraw.DrawRadiusRing(target.Cell, drone.LoiterRadius);
                }
            });
        }

        private void BeginDesignateTarget()
        {
            Pawn wearer = Wearer;
            SwitchbladeDrone drone = ActiveDrone;
            if (wearer?.Map == null || drone == null || drone.Destroyed || drone.Map != wearer.Map)
            {
                Messages.Message("HD_SwitchbladeTablet_NoLinkedDrone".Translate(), parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = true,
                canTargetItems = false,
                canTargetPawns = true,
                validator = target => target.Cell.InBounds(wearer.Map)
            }, target =>
            {
                drone.DesignateTarget(target);
                Messages.Message("HD_SwitchbladeTablet_TargetDesignated".Translate(), drone, MessageTypeDefOf.NeutralEvent, false);
            });
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wearer = Wearer;
            if (wearer == null || wearer.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            SwitchbladeDrone drone = ActiveDrone;
            bool hasDrone = drone != null && !drone.Destroyed && drone.Spawned && drone.Map == wearer.Map;
            if (!hasDrone)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "HD_SwitchbladeTablet_SelectLoiter_Label".Translate().ToString(),
                defaultDesc = "HD_SwitchbladeTablet_SelectLoiter_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("Item/HD_MilitaryTablet", false) ?? BaseContent.BadTex,
                action = BeginSelectLoiterCenter
            };

            yield return new Command_Action
            {
                defaultLabel = "HD_SwitchbladeTablet_Target_Label".Translate().ToString(),
                defaultDesc = "HD_SwitchbladeTablet_Target_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex,
                action = BeginDesignateTarget
            };

            yield return new Command_Action
            {
                defaultLabel = "HD_SwitchbladeTablet_ReturnLoiter_Label".Translate().ToString(),
                defaultDesc = "HD_SwitchbladeTablet_ReturnLoiter_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", false) ?? BaseContent.BadTex,
                Disabled = !drone.IsAttackRunActive,
                disabledReason = "HD_SwitchbladeTablet_ReturnLoiter_NotNeeded".Translate().ToString(),
                action = () => drone.ReturnToLoiter()
            };
        }
    }

    public class SwitchbladeDrone : ThingWithComps
    {
        private IntVec3 loiterCenter;
        private float loiterRadius = 8f;
        private float angle;
        private Vector3 exactPosition;
        private Thing linkedTablet;
        private Pawn linkedOperator;
        private int ticksSinceSmoke;
        private Sustainer loopSustainer;
        private bool loopSustainerDiving;
        private int deploymentTicks;
        private float drawRotationDegrees;
        private SwitchbladeDroneMode mode = SwitchbladeDroneMode.Deploying;
        private IntVec3 targetCell;
        private Thing targetThing;
        private float diveStartDistance;
        private int loiterTurnSign = 1;

        private const int DeploymentStageTicks = 3;
        private const int DeploymentCompleteTicks = 9;
        private const float LoiterAngularSpeed = 0.048f;
        private const float LoiterTurnDegreesPerTick = 2.7f;
        private const float AttackTurnDegreesPerTick = 5.2f;
        private const float DiveSpeedMultiplier = 1.8f;
        private const float ImpactDistance = 0.65f;
        private const float ExplosionRadius = 2.0f;
        private const int ExplosionDamage = 220;
        private const float ExplosionArmorPenetration = 3.0f;
        private const int DirectHitDamage = 520;
        private const float DirectHitArmorPenetration = 4.0f;
        private static readonly Vector2 DroneDrawSize = new Vector2(14.85f, 14.85f);
        private static readonly Color ZoomHighlightColor = new Color(0.2f, 0.9f, 1f, 0.28f);
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.22f);
        private static Material foldedMaterial;
        private static Material halfBladeMaterial;
        private static Material fullBladeMaterial;
        private static Material zoomHighlightMaterial;
        private static Material shadowMaterial;

        public override Vector3 DrawPos => exactPosition == default ? base.DrawPos : exactPosition;
        public bool IsAttackRunActive => mode == SwitchbladeDroneMode.TurningToTarget || mode == SwitchbladeDroneMode.Diving;
        public float LoiterRadius => loiterRadius;

        public void InitializeLoiter(IntVec3 center, float radius, Thing tablet, Pawn operatorPawn, Vector3 launchDirection)
        {
            loiterCenter = center;
            loiterRadius = Mathf.Max(2f, radius);
            linkedTablet = tablet;
            linkedOperator = operatorPawn;
            angle = Rand.Range(0f, Mathf.PI * 2f);
            exactPosition = Position.ToVector3Shifted();
            drawRotationDegrees = RotationDegreesForDirection(launchDirection);
            UpdateLoiterTurnSign();
            deploymentTicks = 0;
            mode = SwitchbladeDroneMode.Deploying;
        }

        public void SetLoiterCenter(IntVec3 center)
        {
            if (Map != null && center.InBounds(Map))
            {
                loiterCenter = center;
                UpdateLoiterTurnSign();
                mode = SwitchbladeDroneMode.Loiter;
                deploymentTicks = DeploymentCompleteTicks;
            }
        }

        public void DesignateTarget(LocalTargetInfo target)
        {
            if (Map == null || !target.Cell.InBounds(Map))
            {
                return;
            }

            targetThing = target.Thing;
            targetCell = target.Cell;
            deploymentTicks = DeploymentCompleteTicks;
            mode = SwitchbladeDroneMode.TurningToTarget;
        }

        public void ReturnToLoiter()
        {
            if (mode == SwitchbladeDroneMode.Loiter)
            {
                return;
            }

            loiterCenter = Position;
            UpdateLoiterTurnSign();
            deploymentTicks = DeploymentCompleteTicks;
            mode = SwitchbladeDroneMode.Loiter;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                loiterCenter = Position;
                exactPosition = Position.ToVector3Shifted();
                mode = SwitchbladeDroneMode.Deploying;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loiterCenter, "loiterCenter");
            Scribe_Values.Look(ref loiterRadius, "loiterRadius", 8f);
            Scribe_Values.Look(ref angle, "angle");
            Scribe_Values.Look(ref exactPosition, "exactPosition");
            Scribe_Values.Look(ref ticksSinceSmoke, "ticksSinceSmoke");
            Scribe_Values.Look(ref deploymentTicks, "deploymentTicks");
            Scribe_Values.Look(ref drawRotationDegrees, "drawRotationDegrees");
            Scribe_Values.Look(ref mode, "mode", SwitchbladeDroneMode.Deploying);
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_Values.Look(ref diveStartDistance, "diveStartDistance");
            Scribe_Values.Look(ref loiterTurnSign, "loiterTurnSign", 1);
            Scribe_References.Look(ref linkedTablet, "linkedTablet");
            Scribe_References.Look(ref linkedOperator, "linkedOperator");
            Scribe_References.Look(ref targetThing, "targetThing");
        }

        protected override void Tick()
        {
            base.Tick();
            TickMovement();
            ticksSinceSmoke++;
            if (Map != null && ticksSinceSmoke >= 18)
            {
                FleckMaker.ThrowSmoke(exactPosition, Map, 0.25f);
                ticksSinceSmoke = 0;
            }

            MaintainLoopSound();
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EndLoopSound();
            base.Destroy(mode);
        }

        private void MaintainLoopSound()
        {
            if (Map == null || deploymentTicks < DeploymentCompleteTicks)
            {
                return;
            }

            bool diving = mode == SwitchbladeDroneMode.Diving;
            if (loopSustainer == null || loopSustainer.Ended || loopSustainerDiving != diving)
            {
                RestartLoopSound(diving);
            }

            loopSustainer?.Maintain();
        }

        private void RestartLoopSound(bool diving)
        {
            EndLoopSound();

            SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(this), MaintenanceType.PerTick);
            soundInfo.volumeFactor = diving ? 2.15f : 1.2f;
            loopSustainer = SoundStarter.TrySpawnSustainer(SoundDef.Named("HD_DroneFlight_Loop"), soundInfo);
            loopSustainerDiving = diving;
        }

        private void EndLoopSound()
        {
            if (loopSustainer != null && !loopSustainer.Ended)
            {
                loopSustainer.End();
            }

            loopSustainer = null;
            loopSustainerDiving = false;
        }

        private void TickMovement()
        {
            switch (mode)
            {
                case SwitchbladeDroneMode.Deploying:
                    TickDeploying();
                    break;
                case SwitchbladeDroneMode.TurningToTarget:
                    TickTurningToTarget();
                    break;
                case SwitchbladeDroneMode.Diving:
                    TickDiving();
                    break;
                default:
                    TickLoiter();
                    break;
            }
        }

        private void TickDeploying()
        {
            deploymentTicks++;
            AdvanceForward(LoiterSpeed);
            if (deploymentTicks >= DeploymentCompleteTicks)
            {
                mode = SwitchbladeDroneMode.Loiter;
                TickLoiter();
            }
        }

        private void TickLoiter()
        {
            Vector3 desiredDirection = DesiredLoiterDirection();
            TurnTowardsDirection(desiredDirection, LoiterTurnDegreesPerTick);
            AdvanceForward(LoiterSpeed);
        }

        private void TickTurningToTarget()
        {
            if (!TryUpdateTargetCell())
            {
                mode = SwitchbladeDroneMode.Loiter;
                return;
            }

            Vector3 desiredDirection = targetCell.ToVector3Shifted() - exactPosition;
            desiredDirection.y = 0f;
            TurnTowardsDirection(desiredDirection, AttackTurnDegreesPerTick);
            AdvanceForward(LoiterSpeed);

            if (Mathf.Abs(Mathf.DeltaAngle(drawRotationDegrees, RotationDegreesForDirection(desiredDirection))) < 4f)
            {
                diveStartDistance = Mathf.Max(1f, desiredDirection.magnitude);
                mode = SwitchbladeDroneMode.Diving;
            }
        }

        private void TickDiving()
        {
            if (!TryUpdateTargetCell())
            {
                mode = SwitchbladeDroneMode.Loiter;
                return;
            }

            Vector3 target = targetCell.ToVector3Shifted();
            Vector3 direction = target - exactPosition;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance <= ImpactDistance)
            {
                Impact();
                return;
            }

            float speed = LoiterSpeed * DiveSpeedMultiplier;
            Vector3 normalizedDirection = direction.normalized;
            drawRotationDegrees = RotationDegreesForDirection(normalizedDirection);
            exactPosition += normalizedDirection * Mathf.Min(speed, distance);
            SetPositionFromExactPosition();
        }

        private float LoiterSpeed => LoiterAngularSpeed * loiterRadius;

        private bool TryUpdateTargetCell()
        {
            if (Map == null)
            {
                return false;
            }

            if (targetThing != null)
            {
                if (targetThing.Destroyed || !targetThing.Spawned || targetThing.Map != Map)
                {
                    return false;
                }

                targetCell = targetThing.Position;
            }

            return targetCell.InBounds(Map);
        }

        private Vector3 DesiredLoiterDirection()
        {
            Vector3 radial = exactPosition - loiterCenter.ToVector3Shifted();
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.001f)
            {
                return DirectionForRotationDegrees(drawRotationDegrees);
            }

            float distance = radial.magnitude;
            Vector3 radialDirection = radial / distance;
            Vector3 tangentDirection = new Vector3(-radialDirection.z, 0f, radialDirection.x) * loiterTurnSign;
            float radialCorrection = Mathf.Clamp((distance - loiterRadius) / loiterRadius, -0.85f, 0.85f);
            return (tangentDirection - radialDirection * radialCorrection).normalized;
        }

        private void UpdateLoiterTurnSign()
        {
            Vector3 radial = exactPosition - loiterCenter.ToVector3Shifted();
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.001f)
            {
                return;
            }

            Vector3 radialDirection = radial.normalized;
            Vector3 clockwiseTangent = new Vector3(-radialDirection.z, 0f, radialDirection.x);
            Vector3 currentDirection = DirectionForRotationDegrees(drawRotationDegrees);
            loiterTurnSign = Vector3.Dot(currentDirection, clockwiseTangent) >= 0f ? 1 : -1;
        }

        private void Impact()
        {
            if (Map != null)
            {
                List<Thing> ignoredThings = IgnoredThingsForExplosion();
                GenExplosion.DoExplosion(Position, Map, ExplosionRadius, DamageDefOf.Bomb, linkedOperator, ExplosionDamage, ExplosionArmorPenetration, ignoredThings: ignoredThings);
                ApplyDirectHitDamage();
            }

            Destroy(DestroyMode.Vanish);
        }

        private List<Thing> IgnoredThingsForExplosion()
        {
            if (targetThing == null || targetThing.Destroyed)
            {
                return null;
            }

            return new List<Thing> { targetThing };
        }

        private void ApplyDirectHitDamage()
        {
            if (targetThing == null || targetThing.Destroyed || !targetThing.Spawned || targetThing.Map != Map)
            {
                return;
            }

            DamageInfo damageInfo = new DamageInfo(DamageDefOf.Bomb, DirectHitDamage, DirectHitArmorPenetration, -1f, linkedOperator);
            targetThing.TakeDamage(damageInfo);
        }

        private void AdvanceForward(float speed)
        {
            if (Map == null)
            {
                return;
            }

            Vector3 direction = DirectionForRotationDegrees(drawRotationDegrees);
            Vector3 nextPosition = exactPosition + direction * speed;
            if (!nextPosition.ToIntVec3().InBounds(Map))
            {
                Vector3 recoveryDirection = loiterCenter.ToVector3Shifted() - exactPosition;
                recoveryDirection.y = 0f;
                if (recoveryDirection.sqrMagnitude > 0.001f)
                {
                    drawRotationDegrees = RotationDegreesForDirection(recoveryDirection);
                    nextPosition = exactPosition + DirectionForRotationDegrees(drawRotationDegrees) * speed;
                }
            }

            exactPosition = nextPosition;
            SetPositionFromExactPosition();
        }

        private void SetPositionFromExactPosition()
        {
            if (Map == null)
            {
                return;
            }

            IntVec3 cell = exactPosition.ToIntVec3();
            if (!cell.InBounds(Map))
            {
                cell = new IntVec3(
                    Mathf.Clamp(cell.x, 0, Map.Size.x - 1),
                    0,
                    Mathf.Clamp(cell.z, 0, Map.Size.z - 1));
                exactPosition = cell.ToVector3Shifted();
            }

            Position = cell;
        }

        private void TurnTowardsDirection(Vector3 desiredDirection, float maxDegrees)
        {
            desiredDirection.y = 0f;
            if (desiredDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            float desiredDegrees = RotationDegreesForDirection(desiredDirection);
            drawRotationDegrees = Mathf.MoveTowardsAngle(drawRotationDegrees, desiredDegrees, maxDegrees);
        }

        private static float NormalizeAngle(float value)
        {
            while (value < 0f)
            {
                value += Mathf.PI * 2f;
            }

            while (value >= Mathf.PI * 2f)
            {
                value -= Mathf.PI * 2f;
            }

            return value;
        }

        private static float RotationDegreesForDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return 0f;
            }

            direction.Normalize();
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static Vector3 DirectionForRotationDegrees(float rotationDegrees)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Material material = CurrentMaterial;
            if (material == null)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            drawLoc = DrawPos;
            float altitudeScale = VisualAltitudeScale;
            DrawGroundShadow(drawLoc);
            drawLoc.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            DrawZoomHighlight(drawLoc, altitudeScale);
            Vector3 scale = new Vector3(DroneDrawSize.x * altitudeScale / 10f, 1f, DroneDrawSize.y * altitudeScale / 10f);
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(drawRotationDegrees, Vector3.up), scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        private void DrawGroundShadow(Vector3 drawLoc)
        {
            float altitudeFactor = VisualAltitudeFactor;
            drawLoc.y = AltitudeLayer.Shadows.AltitudeFor();
            Vector3 scale = new Vector3(
                DroneDrawSize.x * Mathf.Lerp(0.55f, 0.9f, altitudeFactor) / 10f,
                1f,
                DroneDrawSize.y * Mathf.Lerp(0.28f, 0.48f, altitudeFactor) / 10f);
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(drawRotationDegrees, Vector3.up), scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, ShadowMaterial, 0);
        }

        private void DrawZoomHighlight(Vector3 drawLoc, float altitudeScale)
        {
            if (!ShouldDrawZoomHighlight)
            {
                return;
            }

            float pulse = 1f + Mathf.Sin((Find.TickManager?.TicksGame ?? 0) * 0.08f) * 0.08f;
            drawLoc.y -= 0.02f;
            Vector3 scale = new Vector3(DroneDrawSize.x * altitudeScale * 1.18f * pulse / 10f, 1f, DroneDrawSize.y * altitudeScale * 1.18f * pulse / 10f);
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(45f, Vector3.up), scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, ZoomHighlightMaterial, 0);
        }

        private static bool ShouldDrawZoomHighlight
        {
            get
            {
                if (Find.CameraDriver == null)
                {
                    return false;
                }

                CameraZoomRange zoom = Find.CameraDriver.CurrentZoom;
                return zoom == CameraZoomRange.Far || zoom == CameraZoomRange.Furthest;
            }
        }

        private float VisualAltitudeScale
        {
            get
            {
                return Mathf.Lerp(0.86f, 1.55f, VisualAltitudeFactor);
            }
        }

        private float VisualAltitudeFactor
        {
            get
            {
                if (mode == SwitchbladeDroneMode.Deploying)
                {
                    return Mathf.Lerp(0.95f, 0.78f, Mathf.Clamp01(deploymentTicks / (float)DeploymentCompleteTicks));
                }

                if (mode == SwitchbladeDroneMode.Diving)
                {
                    float targetDistance = targetCell.IsValid ? (exactPosition - targetCell.ToVector3Shifted()).magnitude : diveStartDistance;
                    float diveProgress = 1f - Mathf.Clamp01(targetDistance / Mathf.Max(1f, diveStartDistance));
                    return Mathf.Lerp(0.82f, 0.03f, diveProgress);
                }

                float pulse = Mathf.Sin((Find.TickManager?.TicksGame ?? 0) * 0.035f) * 0.06f;
                return Mathf.Clamp01(0.78f + pulse);
            }
        }

        private Material CurrentMaterial
        {
            get
            {
                if (deploymentTicks < DeploymentStageTicks)
                {
                    return FoldedMaterial;
                }

                if (deploymentTicks < DeploymentStageTicks * 2)
                {
                    return HalfBladeMaterial;
                }

                return FullBladeMaterial;
            }
        }

        private static Material FoldedMaterial => foldedMaterial ?? (foldedMaterial = MaterialPool.MatFrom("Weapon/ModernWar/Proj/HD_SwitchBlade600_FoldBlade", ShaderDatabase.Cutout));
        private static Material HalfBladeMaterial => halfBladeMaterial ?? (halfBladeMaterial = MaterialPool.MatFrom("Weapon/ModernWar/Proj/HD_SwitchBlade600_halfBlade", ShaderDatabase.Cutout));
        private static Material FullBladeMaterial => fullBladeMaterial ?? (fullBladeMaterial = MaterialPool.MatFrom("Weapon/ModernWar/Proj/HD_SwitchBlade600_FullBlade", ShaderDatabase.Cutout));
        private static Material ZoomHighlightMaterial => zoomHighlightMaterial ?? (zoomHighlightMaterial = SolidColorMaterials.SimpleSolidColorMaterial(ZoomHighlightColor, false));
        private static Material ShadowMaterial => shadowMaterial ?? (shadowMaterial = SolidColorMaterials.SimpleSolidColorMaterial(ShadowColor, false));
    }

}
