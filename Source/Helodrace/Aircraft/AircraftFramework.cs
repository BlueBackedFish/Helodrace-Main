using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Aircraft
{
    public enum AircraftFlightState
    {
        Parked,
        TakingOff,
        Flying,
        Landing
    }

    public enum AircraftNavigationMode
    {
        Direct,
        Loiter
    }

    /// <summary>
    /// XML-facing flight characteristics. Add this as a modExtension to any
    /// ThingDef whose thingClass is AircraftThing.
    /// </summary>
    public sealed class AircraftDefExtension : DefModExtension
    {
        public float cruiseSpeed = 0.18f;
        public float turnRate = 2.5f;
        public int takeoffTicks = 180;
        public float takeoffDistance = 12f;
        public float landingDistance = 14f;
        public float arrivalRadius = 1.25f;
        public float defaultLoiterRadius = 9f;
        public float airborneDrawScale = 1.35f;
        public bool drawShadow = true;
        public int cruiseAltitude = 18;
        public float drawRotationOffset = 0f;
        public float explosiveDamageMultiplier = 0.1f;
        public float maxLandingTurnAngle = 60f;
        public float worldFuelPerTile = 3f;
        public int worldRange = 60;
        public int worldFlightDurationTicks = 4000;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (cruiseSpeed <= 0f) yield return "cruiseSpeed must be greater than zero.";
            if (turnRate <= 0f) yield return "turnRate must be greater than zero.";
            if (takeoffTicks < 1) yield return "takeoffTicks must be at least one.";
            if (takeoffDistance < 2f) yield return "takeoffDistance must be at least two cells.";
            if (landingDistance < 2f) yield return "landingDistance must be at least two cells.";
            if (arrivalRadius <= 0f) yield return "arrivalRadius must be greater than zero.";
            if (defaultLoiterRadius < 2f) yield return "defaultLoiterRadius must be at least two cells.";
            if (airborneDrawScale <= 0f) yield return "airborneDrawScale must be greater than zero.";
            if (cruiseAltitude < 1) yield return "cruiseAltitude must be a natural number greater than zero.";
            if (explosiveDamageMultiplier < 0f)
                yield return "explosiveDamageMultiplier cannot be negative.";
            if (maxLandingTurnAngle <= 0f || maxLandingTurnAngle > 180f)
                yield return "maxLandingTurnAngle must be greater than zero and at most 180 degrees.";
            if (worldFuelPerTile <= 0f) yield return "worldFuelPerTile must be greater than zero.";
            if (worldRange < 1) yield return "worldRange must be at least one tile.";
            if (worldFlightDurationTicks < 1) yield return "worldFlightDurationTicks must be at least one.";
        }
    }

    /// <summary>
    /// Stable, save-safe in-map aircraft core. It deliberately owns only
    /// movement and flight state; weapons, cargo and AI can be independent
    /// ThingComps which query IsAirborne and issue navigation commands.
    /// </summary>
    public class AircraftThing : ThingWithComps
    {
        private AircraftFlightState flightState;
        private AircraftNavigationMode navigationMode;
        private Vector3 exactPosition;
        private IntVec3 destination;
        private IntVec3 landingCell;
        private IntVec3 loiterCenter;
        private float loiterRadius;
        private float headingDegrees;
        private int takeoffTicksElapsed;
        private int loiterTurnSign = 1;
        private bool outOfFuelLandingTriggered;
        private float currentSpeed;
        private int currentAltitude;
        private int altitudeOverride = -1;
        private Vector3 takeoffStartPosition;
        private Vector3 takeoffEndPosition;
        private bool straightFlightLocked;
        private IntVec3 straightFlightEnd = IntVec3.Invalid;
        private float straightFlightHeading;
        private bool expeditionDepartureActive;
        private Vector3 expeditionExitPosition;

        private static Material shadowMaterial;

        public AircraftFlightState FlightState => flightState;
        public AircraftNavigationMode NavigationMode => navigationMode;
        public bool IsAirborne => flightState != AircraftFlightState.Parked;
        public Vector3 ExactPosition => exactPosition;
        public float HeadingDegrees => headingDegrees;
        public IntVec3 Destination => destination;
        public float CurrentSpeed => currentSpeed;
        public int Altitude => flightState == AircraftFlightState.Parked ? 0 : Mathf.Max(1, currentAltitude);
        public float NavigationArrivalRadius => FlightDef.arrivalRadius;
        public bool ExpeditionDepartureActive => expeditionDepartureActive;
        public CompRefuelable FuelComp => this.TryGetComp<CompRefuelable>();
        public CompAircraftManifest Manifest => this.TryGetComp<CompAircraftManifest>();

        public override Vector3 DrawPos => exactPosition == default(Vector3) ? base.DrawPos : exactPosition;

        private AircraftDefExtension FlightDef
        {
            get
            {
                AircraftDefExtension extension = def.GetModExtension<AircraftDefExtension>();
                return extension ?? AircraftDefaults.Extension;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad || exactPosition == default(Vector3))
            {
                exactPosition = Position.ToVector3Shifted();
                destination = Position;
                landingCell = Position;
                loiterCenter = Position;
                loiterRadius = FlightDef.defaultLoiterRadius;
                headingDegrees = Rotation.AsAngle;
                flightState = AircraftFlightState.Parked;
                currentAltitude = 0;
            }

            loiterRadius = Mathf.Max(2f, loiterRadius);
            ClampNavigationCellsToMap();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref flightState, "flightState", AircraftFlightState.Parked);
            Scribe_Values.Look(ref navigationMode, "navigationMode", AircraftNavigationMode.Direct);
            Scribe_Values.Look(ref exactPosition, "exactPosition");
            Scribe_Values.Look(ref destination, "destination");
            Scribe_Values.Look(ref landingCell, "landingCell");
            Scribe_Values.Look(ref loiterCenter, "loiterCenter");
            Scribe_Values.Look(ref loiterRadius, "loiterRadius", 9f);
            Scribe_Values.Look(ref headingDegrees, "headingDegrees");
            Scribe_Values.Look(ref takeoffTicksElapsed, "takeoffTicksElapsed");
            Scribe_Values.Look(ref loiterTurnSign, "loiterTurnSign", 1);
            Scribe_Values.Look(ref outOfFuelLandingTriggered, "outOfFuelLandingTriggered");
            Scribe_Values.Look(ref currentSpeed, "currentSpeed");
            Scribe_Values.Look(ref currentAltitude, "currentAltitude");
            Scribe_Values.Look(ref altitudeOverride, "aircraftAltitudeOverride", -1);
            Scribe_Values.Look(ref takeoffStartPosition, "aircraftTakeoffStartPosition");
            Scribe_Values.Look(ref takeoffEndPosition, "aircraftTakeoffEndPosition");
            Scribe_Values.Look(ref straightFlightLocked, "aircraftStraightFlightLocked");
            Scribe_Values.Look(ref straightFlightEnd, "aircraftStraightFlightEnd", IntVec3.Invalid);
            Scribe_Values.Look(ref straightFlightHeading, "aircraftStraightFlightHeading");
            Scribe_Values.Look(ref expeditionDepartureActive, "aircraftExpeditionDepartureActive");
            Scribe_Values.Look(ref expeditionExitPosition, "aircraftExpeditionExitPosition");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                currentAltitude = flightState == AircraftFlightState.Parked
                    ? 0
                    : Mathf.Clamp(currentAltitude, 1, FlightDef.cruiseAltitude);
                if (flightState == AircraftFlightState.TakingOff &&
                    takeoffEndPosition == default(Vector3))
                {
                    takeoffStartPosition = exactPosition;
                    takeoffEndPosition = exactPosition +
                        DirectionForDegrees(headingDegrees) * FlightDef.takeoffDistance;
                }
                if (flightState != AircraftFlightState.Flying)
                    expeditionDepartureActive = false;
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (Map == null || flightState == AircraftFlightState.Parked)
            {
                return;
            }

            TickFuel();
            TickPilotSafety();

            switch (flightState)
            {
                case AircraftFlightState.TakingOff:
                    TickTakingOff();
                    break;
                case AircraftFlightState.Landing:
                    TickLanding();
                    break;
                default:
                    TickFlying();
                    break;
            }
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (dinfo.Def != null && dinfo.Def.isExplosive)
            {
                dinfo.SetAmount(dinfo.Amount * FlightDef.explosiveDamageMultiplier);
            }

            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        public bool TryTakeOff(IntVec3 directionCell, out string rejection)
        {
            rejection = null;
            if (!Spawned || Map == null)
            {
                rejection = "HD_Aircraft_NotSpawned".Translate();
                return false;
            }

            if (flightState != AircraftFlightState.Parked)
            {
                rejection = "HD_Aircraft_AlreadyAirborne".Translate();
                return false;
            }

            if (FuelComp != null && !FuelComp.HasFuel)
            {
                rejection = "HD_Aircraft_NoFuelForTakeoff".Translate();
                return false;
            }
            if (Manifest != null && !Manifest.HasCapablePilot)
            {
                rejection = "HD_Aircraft_NoPilotForTakeoff".Translate();
                return false;
            }
            CompAircraftTransporter transporter = this.TryGetComp<CompAircraftTransporter>();
            if (transporter != null && transporter.AnythingLeftToLoad)
            {
                rejection = "HD_Aircraft_CargoLoadingIncomplete".Translate();
                return false;
            }
            CompAircraftBombBay bombBay = this.TryGetComp<CompAircraftBombBay>();
            if (bombBay != null && !bombBay.LoadoutReadyForTakeoff)
            {
                rejection = "HD_Aircraft_BombLoadoutIncomplete".Translate();
                return false;
            }

            Vector3 direction = directionCell.ToVector3Shifted() - exactPosition;
            direction.y = 0f;
            if (direction.magnitude < FlightDef.takeoffDistance)
            {
                rejection = "HD_Aircraft_TakeoffTooShort".Translate(FlightDef.takeoffDistance.ToString("0.#"));
                return false;
            }

            if (!RunwayIsClear(GetTakeoffRunwayCells(directionCell)))
            {
                rejection = "HD_Aircraft_TakeoffRunwayBlocked".Translate();
                return false;
            }

            bombBay?.PrepareForTakeoff();
            headingDegrees = DirectionToDegrees(direction);
            takeoffStartPosition = exactPosition;
            takeoffEndPosition = exactPosition + direction.normalized * FlightDef.takeoffDistance;
            destination = ClampToMap(takeoffEndPosition.ToIntVec3());
            navigationMode = AircraftNavigationMode.Direct;
            takeoffTicksElapsed = 0;
            flightState = AircraftFlightState.TakingOff;
            outOfFuelLandingTriggered = false;
            return true;
        }

        public bool TrySetDestination(IntVec3 cell)
        {
            if (flightState != AircraftFlightState.Flying || straightFlightLocked || expeditionDepartureActive ||
                Map == null || !cell.InBounds(Map))
            {
                return false;
            }

            destination = ClampToMap(cell);
            navigationMode = AircraftNavigationMode.Direct;
            return true;
        }

        public bool LockStraightFlightTo(IntVec3 cell)
        {
            if (flightState != AircraftFlightState.Flying || expeditionDepartureActive ||
                Map == null || !cell.InBounds(Map))
            {
                return false;
            }

            if (!straightFlightLocked || straightFlightEnd != cell)
            {
                Vector3 direction = cell.ToVector3Shifted() - exactPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.001f)
                {
                    return false;
                }
                straightFlightHeading = DirectionToDegrees(direction);
            }

            cell = ClampToMap(cell);
            straightFlightEnd = cell;
            straightFlightLocked = true;
            destination = cell;
            navigationMode = AircraftNavigationMode.Direct;
            headingDegrees = straightFlightHeading;
            return true;
        }

        public void ClearStraightFlightLock()
        {
            straightFlightLocked = false;
            straightFlightEnd = IntVec3.Invalid;
        }

        public void SetAltitudeOverride(int altitude)
        {
            altitudeOverride = Mathf.Clamp(altitude, 1, FlightDef.cruiseAltitude);
        }

        public void ClearAltitudeOverride()
        {
            altitudeOverride = -1;
        }

        /// <summary>
        /// Shared altitude gate for bombs and future aircraft weapons. Range
        /// must be strictly greater than the aircraft's natural-number altitude.
        /// </summary>
        public bool CanAttackWithRange(float weaponRange)
        {
            return IsAirborne && weaponRange > Altitude;
        }

        public bool TryLoiter(IntVec3 center, float radius)
        {
            if (flightState != AircraftFlightState.Flying || straightFlightLocked || expeditionDepartureActive ||
                Map == null || !center.InBounds(Map))
            {
                return false;
            }

            loiterCenter = ClampToMap(center);
            loiterRadius = Mathf.Max(2f, radius);
            loiterTurnSign = ChooseLoiterTurnSign();
            navigationMode = AircraftNavigationMode.Loiter;
            return true;
        }

        public bool TryLand(IntVec3 cell, out string rejection)
        {
            rejection = null;
            if (flightState != AircraftFlightState.Flying || straightFlightLocked ||
                expeditionDepartureActive || Map == null)
            {
                rejection = "HD_Aircraft_NotAirborne".Translate();
                return false;
            }

            if (!cell.InBounds(Map))
            {
                rejection = "HD_Aircraft_InvalidCell".Translate();
                return false;
            }

            if (!LandingTurnAllowed(cell))
            {
                rejection = "HD_Aircraft_LandingTurnTooSharp".Translate(
                    FlightDef.maxLandingTurnAngle.ToString("0.#"));
                return false;
            }

            if (!RunwayIsClear(GetLandingRunwayCells(cell)))
            {
                rejection = "HD_Aircraft_LandingRunwayBlocked".Translate();
                return false;
            }

            landingCell = cell;
            destination = cell;
            Vector3 approach = cell.ToVector3Shifted() - exactPosition;
            approach.y = 0f;
            if (approach.sqrMagnitude > 0.001f)
            {
                headingDegrees = DirectionToDegrees(approach);
            }
            navigationMode = AircraftNavigationMode.Direct;
            flightState = AircraftFlightState.Landing;
            return true;
        }

        public void AbortLanding()
        {
            if (flightState == AircraftFlightState.Landing)
            {
                destination = Position;
                navigationMode = AircraftNavigationMode.Loiter;
                loiterCenter = Position;
                loiterRadius = FlightDef.defaultLoiterRadius;
                flightState = AircraftFlightState.Flying;
            }
        }

        private void TickTakingOff()
        {
            takeoffTicksElapsed++;
            currentAltitude = Mathf.Max(1,
                Mathf.CeilToInt(FlightDef.cruiseAltitude * AltitudeProgress));
            float elapsedFraction = Mathf.Clamp01(takeoffTicksElapsed /
                (float)Mathf.Max(1, FlightDef.takeoffTicks));
            // Position is quadratic in time, so per-tick speed rises linearly.
            float routeProgress = elapsedFraction * elapsedFraction;
            SetExactPosition(Vector3.Lerp(takeoffStartPosition, takeoffEndPosition, routeProgress));
            if (takeoffTicksElapsed >= FlightDef.takeoffTicks)
            {
                takeoffTicksElapsed = FlightDef.takeoffTicks;
                SetExactPosition(takeoffEndPosition);
                flightState = AircraftFlightState.Flying;
                currentAltitude = FlightDef.cruiseAltitude;
                loiterCenter = destination;
                loiterRadius = FlightDef.defaultLoiterRadius;
                navigationMode = AircraftNavigationMode.Loiter;
                loiterTurnSign = ChooseLoiterTurnSign();
            }
        }

        private void TickFlying()
        {
            int desiredAltitude = altitudeOverride > 0
                ? Mathf.Clamp(altitudeOverride, 1, FlightDef.cruiseAltitude)
                : FlightDef.cruiseAltitude;
            if (this.IsHashIntervalTick(12))
            {
                currentAltitude = (int)Mathf.MoveTowards(currentAltitude, desiredAltitude, 1f);
            }
            if (expeditionDepartureActive)
            {
                TickExpeditionDeparture();
                return;
            }
            if (straightFlightLocked)
            {
                AdvanceStraightTowards(straightFlightEnd.ToVector3Shifted(), FlightDef.cruiseSpeed);
                return;
            }
            if (navigationMode == AircraftNavigationMode.Loiter)
            {
                FlyLoiterPattern();
                return;
            }

            FlyTowards(destination, 1f);
            if (HorizontalDistance(exactPosition, destination.ToVector3Shifted()) <= FlightDef.arrivalRadius)
            {
                loiterCenter = destination;
                loiterRadius = FlightDef.defaultLoiterRadius;
                navigationMode = AircraftNavigationMode.Loiter;
                loiterTurnSign = ChooseLoiterTurnSign();
            }
        }

        private void TickLanding()
        {
            float distance = HorizontalDistance(exactPosition, landingCell.ToVector3Shifted());
            currentAltitude = Mathf.Max(1,
                Mathf.CeilToInt(FlightDef.cruiseAltitude * AltitudeProgress));
            float speedFactor = Mathf.Clamp(distance / Mathf.Max(1f, FlightDef.landingDistance), 0.2f, 1f);
            AdvanceStraightTowards(landingCell.ToVector3Shifted(), FlightDef.cruiseSpeed * speedFactor);

            if (HorizontalDistance(exactPosition, landingCell.ToVector3Shifted()) <= FlightDef.arrivalRadius)
            {
                exactPosition = landingCell.ToVector3Shifted();
                Position = landingCell;
                destination = landingCell;
                loiterCenter = landingCell;
                takeoffTicksElapsed = 0;
                navigationMode = AircraftNavigationMode.Direct;
                flightState = AircraftFlightState.Parked;
                currentAltitude = 0;
                Rotation = Rot4.FromAngleFlat(headingDegrees);
                outOfFuelLandingTriggered = false;
                ClearStraightFlightLock();
            }
        }

        private void TickFuel()
        {
            CompRefuelable fuelComp = FuelComp;
            if (fuelComp == null)
            {
                return;
            }

            if (fuelComp.HasFuel)
            {
                fuelComp.Notify_UsedThisTick();
                return;
            }

            if (outOfFuelLandingTriggered || flightState == AircraftFlightState.Landing)
            {
                return;
            }

            outOfFuelLandingTriggered = true;
            expeditionDepartureActive = false;
            Vector3 glideDirection = DirectionForDegrees(headingDegrees);
            IntVec3 emergencyCell = ClampToMap(
                (exactPosition + glideDirection * FlightDef.landingDistance).ToIntVec3());
            landingCell = emergencyCell;
            destination = emergencyCell;
            navigationMode = AircraftNavigationMode.Direct;
            flightState = AircraftFlightState.Landing;
            Messages.Message("HD_Aircraft_OutOfFuelLanding".Translate(), this,
                MessageTypeDefOf.ThreatSmall, false);
        }

        private void TickPilotSafety()
        {
            CompAircraftManifest manifest = Manifest;
            if (manifest == null || manifest.HasCapablePilot || flightState == AircraftFlightState.Landing)
            {
                return;
            }

            expeditionDepartureActive = false;
            Vector3 glideDirection = DirectionForDegrees(headingDegrees);
            IntVec3 emergencyCell = ClampToMap(
                (exactPosition + glideDirection * FlightDef.landingDistance).ToIntVec3());
            landingCell = emergencyCell;
            destination = emergencyCell;
            navigationMode = AircraftNavigationMode.Direct;
            flightState = AircraftFlightState.Landing;
            Messages.Message("HD_Aircraft_PilotIncapacitatedLanding".Translate(), this,
                MessageTypeDefOf.ThreatSmall, false);
        }

        private void BeginExpeditionDeparture()
        {
            if (flightState != AircraftFlightState.Flying || Map == null || straightFlightLocked)
            {
                Messages.Message("HD_Aircraft_Expedition_NotReady".Translate(), this,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (Manifest != null && !Manifest.HasCapablePilot)
            {
                Messages.Message("HD_Aircraft_NoPilotForTakeoff".Translate(), this,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            CancelActiveStrafeRun();
            ClearStraightFlightLock();
            expeditionExitPosition = CalculateExpeditionExitPosition();
            destination = ClampToMap(expeditionExitPosition.ToIntVec3());
            navigationMode = AircraftNavigationMode.Direct;
            expeditionDepartureActive = true;
        }

        private void CancelExpeditionDeparture()
        {
            if (!expeditionDepartureActive) return;
            expeditionDepartureActive = false;
            loiterCenter = Position;
            loiterRadius = FlightDef.defaultLoiterRadius;
            loiterTurnSign = ChooseLoiterTurnSign();
            navigationMode = AircraftNavigationMode.Loiter;
        }

        private void TickExpeditionDeparture()
        {
            if (Map == null)
            {
                expeditionDepartureActive = false;
                return;
            }

            Vector3 remaining = expeditionExitPosition - exactPosition;
            remaining.y = 0f;
            float distance = remaining.magnitude;
            if (distance <= Mathf.Max(0.2f, FlightDef.cruiseSpeed * 1.5f))
            {
                SetExactPosition(expeditionExitPosition);
                CompleteExpeditionDeparture();
                return;
            }

            TurnTowards(remaining.normalized);
            float step = Mathf.Min(FlightDef.cruiseSpeed, distance);
            SetExactPosition(exactPosition + DirectionForDegrees(headingDegrees) * step);
        }

        private Vector3 CalculateExpeditionExitPosition()
        {
            Vector3 direction = DirectionForDegrees(headingDegrees);
            int margin = FlightSafetyMarginCells;
            float minX = margin + 0.5f;
            float maxX = Mathf.Max(minX, Map.Size.x - margin - 0.5f);
            float minZ = margin + 0.5f;
            float maxZ = Mathf.Max(minZ, Map.Size.z - margin - 0.5f);
            float distance = float.MaxValue;

            if (direction.x > 0.0001f) distance = Mathf.Min(distance, (maxX - exactPosition.x) / direction.x);
            else if (direction.x < -0.0001f) distance = Mathf.Min(distance, (minX - exactPosition.x) / direction.x);
            if (direction.z > 0.0001f) distance = Mathf.Min(distance, (maxZ - exactPosition.z) / direction.z);
            else if (direction.z < -0.0001f) distance = Mathf.Min(distance, (minZ - exactPosition.z) / direction.z);

            if (distance == float.MaxValue || distance < 0f) distance = 0f;
            Vector3 exit = exactPosition + direction * distance;
            exit.x = Mathf.Clamp(exit.x, minX, maxX);
            exit.z = Mathf.Clamp(exit.z, minZ, maxZ);
            return exit;
        }

        private void CompleteExpeditionDeparture()
        {
            if (!AircraftCaravan.TryCreateFromMap(this, out AircraftCaravan caravan,
                out string rejection))
            {
                Messages.Message(rejection, this, MessageTypeDefOf.RejectInput, false);
                CancelExpeditionDeparture();
                return;
            }

            expeditionDepartureActive = false;
            Messages.Message("HD_Aircraft_Expedition_Created".Translate(caravan.Label), caravan,
                MessageTypeDefOf.PositiveEvent, false);
        }

        private void FlyLoiterPattern()
        {
            Vector3 centerOffset = loiterCenter.ToVector3Shifted() - exactPosition;
            centerOffset.y = 0f;
            float distance = centerOffset.magnitude;
            Vector3 inward = distance > 0.001f ? centerOffset / distance : DirectionForDegrees(headingDegrees);
            Vector3 tangent = loiterTurnSign > 0
                ? new Vector3(inward.z, 0f, -inward.x)
                : new Vector3(-inward.z, 0f, inward.x);
            float radialCorrection = Mathf.Clamp((distance - loiterRadius) / Mathf.Max(2f, loiterRadius), -0.75f, 0.75f);
            Vector3 desired = (tangent + inward * radialCorrection).normalized;
            TurnTowards(desired);
            Advance(FlightDef.cruiseSpeed);
        }

        private void FlyTowards(IntVec3 cell, float speedFactor)
        {
            Vector3 direction = cell.ToVector3Shifted() - exactPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                TurnTowards(direction.normalized);
            }

            Advance(FlightDef.cruiseSpeed * Mathf.Clamp01(speedFactor));
        }

        private void TurnTowards(Vector3 desiredDirection)
        {
            float turnRateFactor = this.TryGetComp<CompAircraftBombBay>()?.TurnRateFactor ?? 1f;
            headingDegrees = Mathf.MoveTowardsAngle(
                headingDegrees,
                DirectionToDegrees(desiredDirection),
                FlightDef.turnRate * turnRateFactor);
        }

        private void Advance(float distance)
        {
            if (Map == null || distance <= 0f)
            {
                return;
            }

            EnsureInsideSafeFlightArea();
            Vector3 centerDirection = Map.Center.ToVector3Shifted() - exactPosition;
            centerDirection.y = 0f;
            float lookaheadDistance = Mathf.Max(8f, distance * 50f);
            Vector3 lookahead = exactPosition +
                DirectionForDegrees(headingDegrees) * lookaheadDistance;
            if (!IsInsideSafeFlightArea(lookahead) && centerDirection.sqrMagnitude > 0.001f)
            {
                TurnTowards(centerDirection.normalized);
            }

            Vector3 next = exactPosition + DirectionForDegrees(headingDegrees) * distance;
            if (!IsInsideSafeFlightArea(next))
            {
                EmergencyTurnAwayFromBoundary();
                next = exactPosition + DirectionForDegrees(headingDegrees) * distance;
            }

            SetExactPosition(next);
        }

        private void AdvanceStraightTowards(Vector3 target, float distance)
        {
            Vector3 remaining = target - exactPosition;
            remaining.y = 0f;
            float remainingDistance = remaining.magnitude;
            if (remainingDistance < 0.001f)
            {
                currentSpeed = 0f;
                return;
            }

            Vector3 direction = DirectionForDegrees(headingDegrees);
            float step = Mathf.Min(Mathf.Max(0f, distance), remainingDistance);
            Vector3 next = exactPosition + direction * step;
            if (!IsInsideSafeFlightArea(next))
            {
                EmergencyTurnAwayFromBoundary();
                next = exactPosition + DirectionForDegrees(headingDegrees) * step;
            }
            SetExactPosition(next);
        }

        private void SetExactPosition(Vector3 position)
        {
            Vector3 previous = exactPosition;
            exactPosition = ClampExactToSafeFlightArea(position);
            Position = ClampToMap(exactPosition.ToIntVec3());
            currentSpeed = HorizontalDistance(previous, exactPosition);
        }

        private void EmergencyTurnAwayFromBoundary()
        {
            Vector3 inward = Map.Center.ToVector3Shifted() - exactPosition;
            inward.y = 0f;
            if (inward.sqrMagnitude > 0.001f)
            {
                headingDegrees = DirectionToDegrees(inward);
            }
            ClearStraightFlightLock();
            this.TryGetComp<CompAircraftGun>()?.CancelForManualNavigation();
        }

        private void EnsureInsideSafeFlightArea()
        {
            if (IsInsideSafeFlightArea(exactPosition))
            {
                return;
            }

            exactPosition = ClampExactToSafeFlightArea(exactPosition);
            Position = ClampToMap(exactPosition.ToIntVec3());
            EmergencyTurnAwayFromBoundary();
        }

        private bool IsInsideSafeFlightArea(Vector3 position)
        {
            if (Map == null)
            {
                return false;
            }
            int margin = FlightSafetyMarginCells;
            float minX = margin + 0.5f;
            float maxX = Map.Size.x - margin - 0.5f;
            float minZ = margin + 0.5f;
            float maxZ = Map.Size.z - margin - 0.5f;
            return position.x >= minX && position.x <= maxX &&
                position.z >= minZ && position.z <= maxZ;
        }

        private Vector3 ClampExactToSafeFlightArea(Vector3 position)
        {
            if (Map == null)
            {
                return position;
            }
            int margin = FlightSafetyMarginCells;
            float minX = margin + 0.5f;
            float maxX = Mathf.Max(minX, Map.Size.x - margin - 0.5f);
            float minZ = margin + 0.5f;
            float maxZ = Mathf.Max(minZ, Map.Size.z - margin - 0.5f);
            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.z = Mathf.Clamp(position.z, minZ, maxZ);
            return position;
        }

        private int FlightSafetyMarginCells =>
            Mathf.Max(0, Mathf.CeilToInt((Mathf.Max(def.size.x, def.size.z) - 1) * 0.5f));

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (!Spawned)
            {
                yield break;
            }

            if (flightState == AircraftFlightState.Parked)
            {
                yield return Command("HD_Aircraft_Takeoff_Label", "HD_Aircraft_Takeoff_Desc", BeginTakeoffTargeting);
                yield break;
            }

            if (flightState == AircraftFlightState.TakingOff ||
                flightState == AircraftFlightState.Landing ||
                this.TryGetComp<CompAircraftGun>()?.StrafeRunActive == true)
            {
                yield break;
            }

            if (expeditionDepartureActive)
            {
                yield return Command("HD_Aircraft_ExpeditionCancel_Label",
                    "HD_Aircraft_ExpeditionCancel_Desc", CancelExpeditionDeparture);
                yield break;
            }

            yield return Command("HD_Aircraft_Expedition_Label", "HD_Aircraft_Expedition_Desc",
                BeginExpeditionDeparture);
            yield return Command("HD_Aircraft_Loiter_Label", "HD_Aircraft_Loiter_Desc", BeginLoiterTargeting);
            yield return Command("HD_Aircraft_Land_Label", "HD_Aircraft_Land_Desc", BeginLandingTargeting);
        }

        public override string GetInspectString()
        {
            string result = base.GetInspectString();
            string state = ("HD_Aircraft_State_" + flightState).Translate();
            string flightLine = "HD_Aircraft_InspectState".Translate(state);
            if (IsAirborne)
            {
                flightLine += "\n" + "HD_Aircraft_InspectAltitude".Translate(Altitude);
            }
            return result.NullOrEmpty() ? flightLine : result + "\n" + flightLine;
        }

        private Command_Action Command(string labelKey, string descriptionKey, System.Action action)
        {
            return new Command_Action
            {
                defaultLabel = labelKey.Translate(),
                defaultDesc = descriptionKey.Translate(),
                icon = BaseContent.BadTex,
                action = action
            };
        }

        private void BeginTakeoffTargeting()
        {
            BeginCellTargeting(cell =>
            {
                if (!TryTakeOff(cell, out string rejection))
                {
                    Messages.Message(rejection, this, MessageTypeDefOf.RejectInput, false);
                }
            }, cell => DrawRunwayPreview(GetTakeoffRunwayCells(cell)));
        }

        private void BeginLoiterTargeting()
        {
            BeginCellTargeting(cell =>
                {
                    if (TryLoiter(cell, FlightDef.defaultLoiterRadius))
                    {
                        CancelActiveStrafeRun();
                    }
                },
                cell => GenDraw.DrawRadiusRing(cell, FlightDef.defaultLoiterRadius));
        }

        private void BeginLandingTargeting()
        {
            BeginCellTargeting(cell =>
            {
                if (!TryLand(cell, out string rejection))
                {
                    Messages.Message(rejection, this, MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    CancelActiveStrafeRun();
                }
            }, cell => DrawRunwayPreview(GetLandingRunwayCells(cell), LandingTurnAllowed(cell)),
                LandingTurnAllowed);
        }

        private bool LandingTurnAllowed(IntVec3 cell)
        {
            Vector3 desired = cell.ToVector3Shifted() - exactPosition;
            desired.y = 0f;
            if (desired.sqrMagnitude < 0.001f)
            {
                return false;
            }
            return Vector3.Angle(DirectionForDegrees(headingDegrees), desired.normalized) <=
                FlightDef.maxLandingTurnAngle;
        }

        private void CancelActiveStrafeRun()
        {
            this.TryGetComp<CompAircraftGun>()?.CancelForManualNavigation();
        }

        private List<IntVec3> GetTakeoffRunwayCells(IntVec3 headingCell)
        {
            Vector3 direction = headingCell.ToVector3Shifted() - exactPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                return new List<IntVec3>();
            }

            direction.Normalize();
            return GetRunwayCells(exactPosition,
                exactPosition + direction * FlightDef.takeoffDistance);
        }

        private List<IntVec3> GetLandingRunwayCells(IntVec3 touchdownCell)
        {
            Vector3 touchdown = touchdownCell.ToVector3Shifted();
            Vector3 approachDirection = touchdown - exactPosition;
            approachDirection.y = 0f;
            if (approachDirection.sqrMagnitude < 0.001f)
            {
                approachDirection = DirectionForDegrees(headingDegrees);
            }
            else
            {
                approachDirection.Normalize();
            }

            return GetRunwayCells(
                touchdown - approachDirection * FlightDef.landingDistance,
                touchdown);
        }

        private List<IntVec3> GetRunwayCells(Vector3 start, Vector3 end)
        {
            float halfWidth = Mathf.Max(0f, (Mathf.Max(def.size.x, def.size.z) - 1) * 0.5f);
            float margin = halfWidth + 1f;
            int minX = Mathf.FloorToInt(Mathf.Min(start.x, end.x) - margin);
            int maxX = Mathf.CeilToInt(Mathf.Max(start.x, end.x) + margin);
            int minZ = Mathf.FloorToInt(Mathf.Min(start.z, end.z) - margin);
            int maxZ = Mathf.CeilToInt(Mathf.Max(start.z, end.z) + margin);
            Vector3 segment = end - start;
            segment.y = 0f;
            float segmentLengthSquared = segment.sqrMagnitude;
            List<IntVec3> cells = new List<IntVec3>();

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    Vector3 center = new IntVec3(x, 0, z).ToVector3Shifted();
                    Vector3 fromStart = center - start;
                    fromStart.y = 0f;
                    float projection = segmentLengthSquared > 0.001f
                        ? Mathf.Clamp01(Vector3.Dot(fromStart, segment) / segmentLengthSquared)
                        : 0f;
                    Vector3 closest = start + segment * projection;
                    if (HorizontalDistance(center, closest) <= halfWidth + 0.01f)
                    {
                        cells.Add(new IntVec3(x, 0, z));
                    }
                }
            }

            return cells;
        }

        private bool RunwayIsClear(List<IntVec3> runwayCells)
        {
            if (Map == null || runwayCells == null || runwayCells.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < runwayCells.Count; i++)
            {
                IntVec3 cell = runwayCells[i];
                if (!cell.InBounds(Map) || cell.GetTerrain(Map).passability == Traversability.Impassable)
                {
                    return false;
                }

                List<Thing> things = cell.GetThingList(Map);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = things[j];
                    // A multi-cell aircraft can be registered in every occupied
                    // map cell. Never treat the aircraft requesting the check as
                    // an obstacle on its own runway.
                    if (ReferenceEquals(thing, this) || thing.thingIDNumber == thingIDNumber)
                    {
                        continue;
                    }

                    ThingCategory category = thing.def.category;
                    if (category == ThingCategory.Pawn || category == ThingCategory.Item ||
                        category == ThingCategory.Building || category == ThingCategory.Plant)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void DrawRunwayPreview(List<IntVec3> runwayCells, bool directionAllowed = true)
        {
            if (Map == null || runwayCells == null || runwayCells.Count == 0)
            {
                return;
            }

            bool clear = directionAllowed && RunwayIsClear(runwayCells);
            List<IntVec3> visibleCells = runwayCells.FindAll(cell => cell.InBounds(Map));
            if (visibleCells.Count > 0)
            {
                // Lift the overlay above the terrain/selection meshes to avoid
                // depth fighting, which otherwise makes the outline flicker.
                GenDraw.DrawFieldEdges(visibleCells, clear ? Color.green : Color.red, 0.08f);
            }
        }

        private void BeginCellTargeting(System.Action<IntVec3> accepted,
            System.Action<IntVec3> onUpdate = null, System.Func<IntVec3, bool> extraValidator = null)
        {
            Map currentMap = Map;
            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetPawns = false,
                validator = target => currentMap != null && target.Cell.InBounds(currentMap) &&
                    (extraValidator == null || extraValidator(target.Cell))
            }, target => accepted(target.Cell), target => onUpdate?.Invoke(target.Cell));

            // Targeter's GUI callback is not guaranteed to repaint continuously
            // while the mouse is stationary. This is the same persistent update
            // path used by the mortar and drone targeting previews.
            if (currentMap != null && onUpdate != null)
            {
                global::Helodrace.MapComponent_PersistentTargetingOverlay.Set(currentMap,
                    target => onUpdate(target.Cell));
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            drawLoc = DrawPos;
            float altitude = AltitudeProgress;
            if (FlightDef.drawShadow && altitude > 0.01f)
            {
                DrawShadow(drawLoc, altitude);
            }

            drawLoc.y = IsAirborne ? AltitudeLayer.MoteOverhead.AltitudeFor() : def.altitudeLayer.AltitudeFor();
            float scale = Mathf.Lerp(1f, FlightDef.airborneDrawScale, altitude);
            Vector2 drawSize = def.graphicData?.drawSize ?? Vector2.one;
            Vector3 size = new Vector3(drawSize.x * scale / 10f, 1f, drawSize.y * scale / 10f);
            Material material = Graphic?.MatSingleFor(this);
            if (material == null)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawLoc,
                Quaternion.AngleAxis(headingDegrees + FlightDef.drawRotationOffset, Vector3.up), size), material, 0);
        }

        private void DrawShadow(Vector3 location, float altitude)
        {
            location.y = AltitudeLayer.Shadows.AltitudeFor();
            Vector2 drawSize = def.graphicData?.drawSize ?? Vector2.one;
            float shadowScale = Mathf.Lerp(0.65f, 1f, altitude);
            Vector3 size = new Vector3(drawSize.x * shadowScale / 10f, 1f, drawSize.y * 0.45f * shadowScale / 10f);
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(location,
                    Quaternion.AngleAxis(headingDegrees + FlightDef.drawRotationOffset, Vector3.up), size),
                ShadowMaterial, 0);
        }

        private float AltitudeProgress
        {
            get
            {
                if (flightState == AircraftFlightState.Parked) return 0f;
                if (flightState == AircraftFlightState.TakingOff)
                    return Mathf.Clamp01(takeoffTicksElapsed / (float)Mathf.Max(1, FlightDef.takeoffTicks));
                if (flightState == AircraftFlightState.Landing)
                {
                    float distance = HorizontalDistance(exactPosition, landingCell.ToVector3Shifted());
                    return Mathf.Clamp01(distance / Mathf.Max(1f, FlightDef.landingDistance));
                }

                return 1f;
            }
        }

        private int ChooseLoiterTurnSign()
        {
            Vector3 inward = loiterCenter.ToVector3Shifted() - exactPosition;
            Vector3 forward = DirectionForDegrees(headingDegrees);
            float cross = forward.x * inward.z - forward.z * inward.x;
            return cross >= 0f ? 1 : -1;
        }

        private void ClampNavigationCellsToMap()
        {
            if (Map == null) return;
            destination = ClampToMap(destination);
            landingCell = ClampToMap(landingCell);
            loiterCenter = ClampToMap(loiterCenter);
        }

        private IntVec3 ClampToMap(IntVec3 cell)
        {
            return ClampFlightCellToMap(cell);
        }

        public IntVec3 ClampFlightCellToMap(IntVec3 cell)
        {
            if (Map == null) return cell;
            int marginX = Mathf.Min(FlightSafetyMarginCells, Mathf.Max(0, (Map.Size.x - 1) / 2));
            int marginZ = Mathf.Min(FlightSafetyMarginCells, Mathf.Max(0, (Map.Size.z - 1) / 2));
            return new IntVec3(
                Mathf.Clamp(cell.x, marginX, Map.Size.x - marginX - 1),
                0,
                Mathf.Clamp(cell.z, marginZ, Map.Size.z - marginZ - 1));
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float DirectionToDegrees(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) return 0f;
            direction.Normalize();
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private static Vector3 DirectionForDegrees(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        private static Material ShadowMaterial => shadowMaterial ??
            (shadowMaterial = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0f, 0f, 0f, 0.25f), false));
    }

    internal static class AircraftDefaults
    {
        internal static readonly AircraftDefExtension Extension = new AircraftDefExtension();
    }

    /// <summary>
    /// Lets selected airborne aircraft use the normal map right-click gesture
    /// for movement. Targeting modes retain ownership of the same mouse button.
    /// </summary>
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    internal static class Patch_MapInterface_AircraftRightClickMove
    {
        private static bool Prefix()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.MouseDown || current.button != 1 ||
                Find.Targeter.IsTargeting || Find.CurrentMap == null)
            {
                return true;
            }

            IntVec3 destination = UI.MouseCell();
            if (!destination.InBounds(Find.CurrentMap))
            {
                return true;
            }

            bool orderedAny = false;
            List<object> selected = Find.Selector.SelectedObjectsListForReading;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is AircraftThing aircraft && aircraft.Spawned &&
                    aircraft.Map == Find.CurrentMap &&
                    aircraft.FlightState == AircraftFlightState.Flying &&
                    aircraft.TryGetComp<CompAircraftGun>()?.StrafeRunActive != true)
                {
                    orderedAny |= aircraft.TrySetDestination(destination);
                }
            }

            if (!orderedAny)
            {
                return true;
            }

            FleckMaker.Static(destination, Find.CurrentMap, FleckDefOf.FeedbackGoto);
            current.Use();
            return false;
        }
    }
}
