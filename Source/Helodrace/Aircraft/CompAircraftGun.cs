using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace.Aircraft
{
    public enum AircraftGunMountType
    {
        Fixed,
        Turret
    }

    public enum AircraftStrafeState
    {
        Idle,
        Approaching,
        Strafing,
        Recovering
    }

    public sealed class CompProperties_AircraftGun : CompProperties
    {
        public ThingDef gunDef;
        public AircraftGunMountType mountType = AircraftGunMountType.Fixed;
        public float strafeLength = 30f;
        public float strafeWidth = 3f;
        public int strafeAltitude = 5;
        public float roundsPerMinute = 600f;
        public int gunCount = 1;
        public float fireLeadDistance = 6f;
        public float maxApproachTurnAngle = 30f;
        public float approachRadius = 1.25f;
        public float recoveryDistance = 10f;

        public CompProperties_AircraftGun()
        {
            compClass = typeof(CompAircraftGun);
        }
    }

    public sealed class CompAircraftGun : ThingComp
    {
        private AircraftStrafeState strafeState;
        private IntVec3 strafeStart = IntVec3.Invalid;
        private IntVec3 strafeEnd = IntVec3.Invalid;
        private IntVec3 recoveryPoint = IntVec3.Invalid;
        private float fireTimerTicks;

        public CompProperties_AircraftGun Props => (CompProperties_AircraftGun)props;
        public AircraftThing Aircraft => parent as AircraftThing;
        public bool StrafeRunActive => strafeState != AircraftStrafeState.Idle;

        private VerbProperties GunVerb => Props.gunDef?.Verbs?
            .FirstOrDefault(verb => verb.defaultProjectile != null);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref strafeState, "aircraftStrafeState", AircraftStrafeState.Idle);
            Scribe_Values.Look(ref strafeStart, "aircraftStrafeStart", IntVec3.Invalid);
            Scribe_Values.Look(ref strafeEnd, "aircraftStrafeEnd", IntVec3.Invalid);
            Scribe_Values.Look(ref recoveryPoint, "aircraftStrafeRecoveryPoint", IntVec3.Invalid);
            Scribe_Values.Look(ref fireTimerTicks, "aircraftStrafeFireTimerTicks");
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!StrafeRunActive)
            {
                return;
            }

            AircraftThing aircraft = Aircraft;
            if (aircraft == null || !aircraft.Spawned || aircraft.Map == null ||
                aircraft.FlightState == AircraftFlightState.Parked ||
                aircraft.Manifest?.HasCapablePilot == false)
            {
                CancelStrafeRun(false);
                return;
            }

            switch (strafeState)
            {
                case AircraftStrafeState.Approaching:
                    TickApproach(aircraft);
                    break;
                case AircraftStrafeState.Strafing:
                    TickStrafing(aircraft);
                    break;
                case AircraftStrafeState.Recovering:
                    TickRecovery(aircraft);
                    break;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            AircraftThing aircraft = Aircraft;
            if (aircraft == null || !aircraft.Spawned || Props.mountType != AircraftGunMountType.Fixed)
            {
                yield break;
            }

            Command_Action strafe = new Command_Action
            {
                defaultLabel = "HD_Aircraft_Strafe_Label".Translate(
                    Props.gunDef?.LabelCap ?? "-", Mathf.Max(1, Props.gunCount)),
                defaultDesc = "HD_Aircraft_Strafe_Desc".Translate(
                    Props.strafeLength.ToString("0.#"), Props.strafeAltitude,
                    Props.roundsPerMinute.ToString("0.#"), Mathf.Max(1, Props.gunCount)),
                icon = Props.gunDef?.uiIcon ?? BaseContent.BadTex,
                action = BeginStrafeTargeting
            };

            string rejection = StrafeRejection();
            if (rejection != null)
            {
                strafe.Disable(rejection);
            }
            yield return strafe;

            if (StrafeRunActive)
            {
                yield return new Command_Action
                {
                    defaultLabel = "HD_Aircraft_CancelStrafe_Label".Translate(),
                    defaultDesc = "HD_Aircraft_CancelStrafe_Desc".Translate(),
                    icon = TexCommand.CannotShoot,
                    action = () => CancelStrafeRun(true)
                };
            }
        }

        private string StrafeRejection()
        {
            AircraftThing aircraft = Aircraft;
            if (aircraft == null || aircraft.FlightState != AircraftFlightState.Flying)
                return "HD_Aircraft_Strafe_NotFlying".Translate();
            if (aircraft.ExpeditionDepartureActive)
                return "HD_Aircraft_Strafe_ExpeditionDeparture".Translate();
            if (aircraft.Manifest != null && !aircraft.Manifest.HasCapablePilot)
                return "HD_Aircraft_Strafe_NoPilot".Translate();
            if (StrafeRunActive)
                return "HD_Aircraft_Strafe_AlreadyActive".Translate();
            if (GunVerb?.defaultProjectile == null)
                return "HD_Aircraft_Strafe_InvalidGun".Translate();
            if (GunVerb.range <= Props.strafeAltitude)
                return "HD_Aircraft_Strafe_OutOfRange".Translate(
                    GunVerb.range.ToString("0.#"), Props.strafeAltitude);
            return null;
        }

        private void BeginStrafeTargeting()
        {
            AircraftThing aircraft = Aircraft;
            Map map = aircraft?.Map;
            if (map == null)
            {
                return;
            }

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetPawns = false,
                validator = target => target.Cell.InBounds(map) && StrafeTurnAllowed(target.Cell)
            };
            Find.Targeter.BeginTargeting(parameters,
                target => StartStrafeRun(target.Cell),
                target => DrawStrafePreview(target.Cell));
            global::Helodrace.MapComponent_PersistentTargetingOverlay.Set(map,
                target => DrawStrafePreview(target.Cell));
        }

        private void StartStrafeRun(IntVec3 selectedCenter)
        {
            string rejection = StrafeRejection();
            if (rejection != null)
            {
                Messages.Message(rejection, parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            CalculateStrafeLine(selectedCenter, out strafeStart, out strafeEnd);
            if (!strafeStart.IsValid || !strafeEnd.IsValid)
            {
                Messages.Message("HD_Aircraft_Strafe_InvalidLine".Translate(), parent,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!StrafeTurnAllowed(selectedCenter))
            {
                Messages.Message("HD_Aircraft_Strafe_TurnTooSharp".Translate(
                    Props.maxApproachTurnAngle.ToString("0.#")), parent,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            fireTimerTicks = 0f;
            strafeState = AircraftStrafeState.Approaching;
            Aircraft.SetAltitudeOverride(Props.strafeAltitude);
            Aircraft.TrySetDestination(AttackFlightPoint(strafeStart));
        }

        public void CancelForManualNavigation()
        {
            if (StrafeRunActive)
            {
                CancelStrafeRun(false);
            }
        }

        private void TickApproach(AircraftThing aircraft)
        {
            aircraft.SetAltitudeOverride(Props.strafeAltitude);
            IntVec3 flightStart = AttackFlightPoint(strafeStart);
            aircraft.TrySetDestination(flightStart);
            float arrivalRadius = Mathf.Max(0.5f,
                Mathf.Max(Props.approachRadius, aircraft.NavigationArrivalRadius));
            if (HorizontalDistance(aircraft.ExactPosition, flightStart.ToVector3Shifted()) <= arrivalRadius)
            {
                strafeState = AircraftStrafeState.Strafing;
                fireTimerTicks = 0f;
                aircraft.LockStraightFlightTo(AttackFlightPoint(strafeEnd));
                FireAt(strafeStart);
            }
        }

        private void TickStrafing(AircraftThing aircraft)
        {
            aircraft.SetAltitudeOverride(Props.strafeAltitude);
            IntVec3 flightStart = AttackFlightPoint(strafeStart);
            IntVec3 flightEnd = AttackFlightPoint(strafeEnd);
            aircraft.LockStraightFlightTo(flightEnd);

            float progress = ProgressAlongLine(aircraft.ExactPosition,
                flightStart.ToVector3Shifted(), flightEnd.ToVector3Shifted());
            fireTimerTicks += 1f;
            float shotInterval = ShotIntervalTicks;
            while (fireTimerTicks >= shotInterval)
            {
                FireAt(PointAlongStrafe(progress));
                fireTimerTicks -= shotInterval;
            }

            if (progress < 0.995f && HorizontalDistance(aircraft.ExactPosition,
                    flightEnd.ToVector3Shifted()) > 0.2f)
            {
                return;
            }

            BeginRecovery(aircraft);
        }

        private void BeginRecovery(AircraftThing aircraft)
        {
            Vector3 direction = strafeEnd.ToVector3Shifted() - strafeStart.ToVector3Shifted();
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : DirectionForDegrees(aircraft.HeadingDegrees);
            recoveryPoint = ClampAircraftCell((AttackFlightPoint(strafeEnd).ToVector3Shifted() +
                direction * Mathf.Max(2f, Props.recoveryDistance)).ToIntVec3());
            strafeState = AircraftStrafeState.Recovering;
            aircraft.ClearAltitudeOverride();
            aircraft.LockStraightFlightTo(recoveryPoint);
        }

        private void TickRecovery(AircraftThing aircraft)
        {
            aircraft.ClearAltitudeOverride();
            aircraft.LockStraightFlightTo(recoveryPoint);
            if (HorizontalDistance(aircraft.ExactPosition, recoveryPoint.ToVector3Shifted()) <= 0.2f)
            {
                aircraft.ClearStraightFlightLock();
                strafeState = AircraftStrafeState.Idle;
                strafeStart = IntVec3.Invalid;
                strafeEnd = IntVec3.Invalid;
                recoveryPoint = IntVec3.Invalid;
            }
        }

        private void FireAt(IntVec3 targetCell)
        {
            AircraftThing aircraft = Aircraft;
            VerbProperties verb = GunVerb;
            if (aircraft?.Map == null || verb?.defaultProjectile == null)
            {
                return;
            }

            targetCell = ClampToMap(targetCell);
            Thing groundTarget = FindGroundTargetNear(targetCell);
            LocalTargetInfo target = groundTarget != null
                ? new LocalTargetInfo(groundTarget)
                : new LocalTargetInfo(targetCell);

            int gunCount = Mathf.Max(1, Props.gunCount);
            Vector3 forward = DirectionForDegrees(aircraft.HeadingDegrees);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            float maximumSpan = Mathf.Max(0f,
                Mathf.Max(aircraft.def.size.x, aircraft.def.size.z) - 1f);
            float totalSpan = gunCount > 1
                ? Mathf.Min((gunCount - 1) * 0.3f, maximumSpan)
                : 0f;

            for (int gunIndex = 0; gunIndex < gunCount; gunIndex++)
            {
                Projectile projectile = ThingMaker.MakeThing(verb.defaultProjectile) as Projectile;
                if (projectile == null)
                {
                    continue;
                }

                float lateralOffset = gunCount > 1
                    ? Mathf.Lerp(-totalSpan * 0.5f, totalSpan * 0.5f,
                        gunIndex / (float)(gunCount - 1))
                    : 0f;
                Vector3 origin = aircraft.ExactPosition + right * lateralOffset;
                GenSpawn.Spawn(projectile, aircraft.Position, aircraft.Map);
                projectile.Launch(aircraft, origin, target, target,
                    ProjectileHitFlags.All, false, null, Props.gunDef);
            }
            verb.soundCast?.PlayOneShot(new TargetInfo(aircraft.Position, aircraft.Map));
        }

        private Thing FindGroundTargetNear(IntVec3 center)
        {
            Map map = Aircraft?.Map;
            if (map == null)
            {
                return null;
            }

            float radius = Mathf.Max(0.5f, Props.strafeWidth * 0.5f);
            Thing best = null;
            int bestDistance = int.MaxValue;
            HashSet<Thing> checkedThings = new HashSet<Thing>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || ReferenceEquals(thing, parent) ||
                        !checkedThings.Add(thing) || !CanStrafeTarget(thing))
                    {
                        continue;
                    }

                    int distance = cell.DistanceToSquared(center);
                    if (distance < bestDistance)
                    {
                        best = thing;
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        private static bool CanStrafeTarget(Thing thing)
        {
            if (thing is AircraftThing aircraft && aircraft.IsAirborne)
            {
                return false;
            }

            ThingCategory category = thing.def.category;
            if (category == ThingCategory.Pawn || category == ThingCategory.Building ||
                category == ThingCategory.Plant)
            {
                return true;
            }

            return category == ThingCategory.Item && thing.def.useHitPoints;
        }

        private IntVec3 PointAlongStrafe(float progress)
        {
            return Vector3.Lerp(strafeStart.ToVector3Shifted(), strafeEnd.ToVector3Shifted(),
                Mathf.Clamp01(progress)).ToIntVec3();
        }

        private IntVec3 AttackFlightPoint(IntVec3 groundTarget)
        {
            return AttackFlightPoint(groundTarget, strafeStart, strafeEnd);
        }

        private IntVec3 AttackFlightPoint(IntVec3 groundTarget, IntVec3 lineStart, IntVec3 lineEnd)
        {
            Vector3 direction = lineEnd.ToVector3Shifted() - lineStart.ToVector3Shifted();
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = DirectionForDegrees(Aircraft?.HeadingDegrees ?? 0f);
            }
            else
            {
                direction.Normalize();
            }

            return ClampAircraftCell((groundTarget.ToVector3Shifted() -
                direction * Mathf.Max(0f, Props.fireLeadDistance)).ToIntVec3());
        }

        private void CancelStrafeRun(bool notify)
        {
            Aircraft?.ClearAltitudeOverride();
            Aircraft?.ClearStraightFlightLock();
            strafeState = AircraftStrafeState.Idle;
            strafeStart = IntVec3.Invalid;
            strafeEnd = IntVec3.Invalid;
            recoveryPoint = IntVec3.Invalid;
            fireTimerTicks = 0f;
            if (notify && parent.Spawned)
            {
                Messages.Message("HD_Aircraft_Strafe_Cancelled".Translate(), parent,
                    MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private void CalculateStrafeLine(IntVec3 selectedCenter, out IntVec3 start, out IntVec3 end)
        {
            Vector3 center = selectedCenter.ToVector3Shifted();
            Vector3 direction = center - Aircraft.ExactPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = DirectionForDegrees(Aircraft.HeadingDegrees);
            }
            else
            {
                direction.Normalize();
            }

            float halfLength = Mathf.Max(2f, Props.strafeLength) * 0.5f;
            start = ClampToMap((center - direction * halfLength).ToIntVec3());
            end = ClampToMap((center + direction * halfLength).ToIntVec3());
        }

        private void DrawStrafePreview(IntVec3 selectedCenter)
        {
            if (Aircraft?.Map == null || !selectedCenter.InBounds(Aircraft.Map))
            {
                return;
            }

            CalculateStrafeLine(selectedCenter, out IntVec3 start, out IntVec3 end);
            List<IntVec3> cells = LineCells(start, end, Props.strafeWidth);
            if (cells.Count > 0)
            {
                Color color = StrafeTurnAllowed(selectedCenter)
                    ? Color.red
                    : new Color(0.4f, 0.4f, 0.4f);
                GenDraw.DrawFieldEdges(cells, color, 0.08f);
            }
        }

        private bool StrafeTurnAllowed(IntVec3 selectedCenter)
        {
            AircraftThing aircraft = Aircraft;
            if (aircraft?.Map == null)
            {
                return false;
            }

            CalculateStrafeLine(selectedCenter, out IntVec3 start, out IntVec3 end);
            IntVec3 flightStart = AttackFlightPoint(start, start, end);
            Vector3 toSelectedCenter = selectedCenter.ToVector3Shifted() - aircraft.ExactPosition;
            toSelectedCenter.y = 0f;
            Vector3 desired = flightStart.ToVector3Shifted() - aircraft.ExactPosition;
            desired.y = 0f;
            if (toSelectedCenter.sqrMagnitude < 0.001f || desired.sqrMagnitude < 0.001f)
            {
                return false;
            }

            Vector3 forward = DirectionForDegrees(aircraft.HeadingDegrees);
            float allowedAngle = Mathf.Clamp(Props.maxApproachTurnAngle, 0.1f, 89f);
            // Fixed guns may only designate inside the forward hemisphere. Both
            // the selected center and the actual attack-run entry point must fit
            // inside the configured forward cone.
            return Vector3.Dot(forward, toSelectedCenter.normalized) > 0f &&
                Vector3.Dot(forward, desired.normalized) > 0f &&
                Vector3.Angle(forward, toSelectedCenter.normalized) <= allowedAngle &&
                Vector3.Angle(forward, desired.normalized) <= allowedAngle;
        }

        private List<IntVec3> LineCells(IntVec3 startCell, IntVec3 endCell, float width)
        {
            Vector3 start = startCell.ToVector3Shifted();
            Vector3 end = endCell.ToVector3Shifted();
            Vector3 segment = end - start;
            segment.y = 0f;
            float lengthSquared = segment.sqrMagnitude;
            float radius = Mathf.Max(0f, width * 0.5f);
            int margin = Mathf.CeilToInt(radius + 1f);
            int minX = Mathf.Min(startCell.x, endCell.x) - margin;
            int maxX = Mathf.Max(startCell.x, endCell.x) + margin;
            int minZ = Mathf.Min(startCell.z, endCell.z) - margin;
            int maxZ = Mathf.Max(startCell.z, endCell.z) + margin;
            List<IntVec3> cells = new List<IntVec3>();

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(Aircraft.Map)) continue;
                    Vector3 point = cell.ToVector3Shifted();
                    float projection = lengthSquared > 0.001f
                        ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared)
                        : 0f;
                    Vector3 closest = start + segment * projection;
                    if (HorizontalDistance(point, closest) <= radius + 0.01f)
                    {
                        cells.Add(cell);
                    }
                }
            }
            return cells;
        }

        private static float ProgressAlongLine(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            segment.y = 0f;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.001f) return 1f;
            point.y = start.y;
            return Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        }

        private float ShotIntervalTicks => 3600f / Mathf.Max(1f, Props.roundsPerMinute);

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static Vector3 DirectionForDegrees(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        private IntVec3 ClampToMap(IntVec3 cell)
        {
            Map map = Aircraft?.Map;
            if (map == null) return cell;
            return new IntVec3(Mathf.Clamp(cell.x, 0, map.Size.x - 1), 0,
                Mathf.Clamp(cell.z, 0, map.Size.z - 1));
        }

        private IntVec3 ClampAircraftCell(IntVec3 cell)
        {
            return Aircraft?.ClampFlightCellToMap(cell) ?? cell;
        }
    }
}
