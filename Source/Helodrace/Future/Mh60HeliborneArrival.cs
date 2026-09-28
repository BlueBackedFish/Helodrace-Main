using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Helodrace.Future
{
    /// <summary>
    /// Reusable raid arrival mode which keeps the generated pawns inside an MH-60
    /// until the fast-rope animation reaches the ground.
    /// </summary>
    public sealed class PawnsArrivalModeWorker_Mh60Heliborne : PawnsArrivalModeWorker
    {
        public override bool CanUseWith(IncidentParms parms)
        {
            return parms?.target is Map map && CanUseOnMap(map);
        }

        public override bool CanUseOnMap(Map map)
        {
            return map != null && Mh60HeliborneUtility.TryFindHoverCell(map, IntVec3.Invalid, out _);
        }

        public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
        {
            if (!(parms?.target is Map map)
                || !Mh60HeliborneUtility.TryFindHoverCell(map, parms.spawnCenter, out IntVec3 hoverCell))
            {
                return false;
            }

            parms.spawnCenter = hoverCell;
            return true;
        }

        public override void Arrive(List<Pawn> pawns, IncidentParms parms)
        {
            if (!(parms?.target is Map map) || pawns.NullOrEmpty())
            {
                return;
            }

            if (!Mh60HeliborneUtility.TryFindHoverCell(map, parms.spawnCenter, out IntVec3 hoverCell)
                || !Mh60HeliborneUtility.TryStart(map, hoverCell, pawns))
            {
                Mh60HeliborneUtility.SpawnPassengersImmediately(map, parms.spawnCenter, pawns);
            }
        }
    }

    public static class Mh60HeliborneUtility
    {
        public const int RopeLandingDistance = 14;
        private const int SafeMapMargin = 18;
        private const string AircraftDefName = "HD_MH60M_HeliborneAircraft";

        /// <summary>
        /// Starts an arrival at a caller-selected hover point. This is the entry
        /// point for scripted incidents which do not use a PawnsArrivalModeDef.
        /// </summary>
        public static bool TryStart(Map map, IntVec3 requestedHoverCell, IEnumerable<Pawn> pawns)
        {
            List<Pawn> passengers = pawns?
                .Where(pawn => pawn != null && !pawn.Destroyed && !pawn.Spawned)
                .ToList();
            if (map == null || passengers.NullOrEmpty()
                || !TryFindHoverCell(map, requestedHoverCell, out IntVec3 hoverCell))
            {
                return false;
            }

            ThingDef aircraftDef = DefDatabase<ThingDef>.GetNamedSilentFail(AircraftDefName);
            if (aircraftDef == null)
            {
                Log.Error($"[Helodrace] Missing ThingDef {AircraftDefName}.");
                return false;
            }

            Mh60HeliborneAircraft aircraft = ThingMaker.MakeThing(aircraftDef) as Mh60HeliborneAircraft;
            if (aircraft == null || !aircraft.TryLoadPassengers(passengers))
            {
                return false;
            }

            GenSpawn.Spawn(aircraft, hoverCell, map, Rot4.East);
            return true;
        }

        public static bool TryFindHoverCell(Map map, IntVec3 requested, out IntVec3 hoverCell)
        {
            hoverCell = IntVec3.Invalid;
            if (map != null && IsValidHoverCell(requested, map))
            {
                hoverCell = requested;
                return true;
            }

            if (map != null && requested.IsValid
                && CellFinder.TryFindRandomCellNear(requested, map, 20,
                    cell => IsValidHoverCell(cell, map), out hoverCell))
            {
                return true;
            }

            return map != null
                && CellFinder.TryFindRandomCell(map, cell => IsValidHoverCell(cell, map), out hoverCell);
        }

        public static IntVec3 LandingCell(IntVec3 hoverCell)
        {
            return hoverCell + new IntVec3(0, 0, -RopeLandingDistance);
        }

        public static void SpawnPassengersImmediately(Map map, IntVec3 near, IEnumerable<Pawn> pawns)
        {
            if (map == null || pawns == null)
            {
                return;
            }

            IntVec3 center = near.IsValid && near.InBounds(map)
                ? near
                : map.Center;
            foreach (Pawn pawn in pawns.Where(pawn => pawn != null && !pawn.Destroyed && !pawn.Spawned))
            {
                IntVec3 cell;
                if (!CellFinder.TryFindRandomSpawnCellForPawnNear(center, map, out cell, 12))
                {
                    cell = CellFinder.RandomClosewalkCellNear(center, map, 12);
                }

                GenSpawn.Spawn(pawn, cell, map);
            }
        }

        public static bool IsValidHoverCell(IntVec3 cell, Map map)
        {
            if (!cell.IsValid || !cell.InBounds(map)
                || cell.x < SafeMapMargin || cell.x >= map.Size.x - SafeMapMargin
                || cell.z < SafeMapMargin || cell.z >= map.Size.z - SafeMapMargin)
            {
                return false;
            }

            IntVec3 landingCell = LandingCell(cell);
            return landingCell.InBounds(map)
                && landingCell.Standable(map)
                && !landingCell.Fogged(map);
        }
    }

    [StaticConstructorOnStartup]
    public sealed class Mh60HeliborneAircraft : ThingWithComps, IThingHolder
    {
        private const string TextureRoot = "Effects/Aircraft/MH60M/";
        private const int SettleTicks = 60;
        private const int DoorTicks = 90;
        private const int RopeTicks = 75;
        private const int PassengerDescentTicks = 120;
        private const int PassengerStartIntervalTicks = 40;
        private const int ExitTicks = 210;
        private const int MainRotorFrameTicks = 2;
        private const float MaximumApproachSpeed = 1.6f;
        private const float MaximumApproachAcceleration = 0.055f;
        private const float ApproachBrakeAcceleration = 0.04f;
        private const float ApproachBrakingLeadFactor = 2f;
        private const float ApproachAccelerationDecay = 1.8f;
        private const float MaximumApproachTiltDegrees = 12f;
        private const float ApproachTiltSpeed = 0.6f;
        private const float BrakeTiltPullPortion = 0.2f;
        private const float AircraftDrawSize = 160f;
        private const float FastRopeDrawSize = 160f;
        private const float FastRopePlacementSize = 16f;
        private const float ClosedDoorEastOffset = 1.42f;
        private const float RopeTopSouthOffset = 0.55f;
        private const float PassengerEastOffset = 1.25f;
        private const float DustRingInnerRadius = 3.5f;
        private const float DustRingOuterRadius = 7.2f;
        private const string HelicopterLoopSoundDefName = "HD_HelicopterLoop";
        private const string RotorWashSmokeFleckDefName = "HD_M2A2SmokeScreenVisual";

        private static Material doorOutlineMaterial;
        private static Material baseMaterial;
        private static Material doorFrontMaterial;
        private static Material[] mainRotorFlyMaterials;
        private static Material wingFlyMaterial;
        private static Material ropeMaterial;
        private static FleckDef rotorWashSmokeFleck;

        private ThingOwner<Pawn> passengers;
        private List<Pawn> descendingPassengers = new List<Pawn>();
        private List<int> descendingStartTicks = new List<int>();
        private int ageTicks;
        private int initialPassengerCount;
        private int startedPassengerCount;
        private int releasedPassengerCount;
        private bool approachInitialized;
        private int approachCompleteTick = -1;
        private float approachDrawX;
        private float approachSpeed;
        private float approachAcceleration;
        private float approachTiltDegrees;
        private bool approachBraking;
        private float approachBrakeStartSpeed;
        private Sustainer helicopterLoopSustainer;

        public Mh60HeliborneAircraft()
        {
            passengers = new ThingOwner<Pawn>(this, LookMode.Deep, false);
        }

        private int ApproachEndTick => approachCompleteTick >= 0
            ? approachCompleteTick
            : int.MaxValue / 4;
        private int DoorOpenStart => ApproachEndTick + SettleTicks;
        private int RopeDeployStart => DoorOpenStart + DoorTicks;
        private int UnloadStart => RopeDeployStart + RopeTicks;
        private int UnloadEnd => UnloadStart
            + Mathf.Max(0, initialPassengerCount - 1) * PassengerStartIntervalTicks
            + PassengerDescentTicks;
        private int RopeRetractEnd => UnloadEnd + RopeTicks;
        private int DoorCloseEnd => RopeRetractEnd + DoorTicks;
        private int ExitEnd => DoorCloseEnd + ExitTicks;

        public ThingOwner GetDirectlyHeldThings()
        {
            return passengers;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, passengers);
        }

        public bool TryLoadPassengers(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
            {
                return false;
            }

            foreach (Pawn pawn in pawns)
            {
                if (pawn != null && !pawn.Spawned && !pawn.Destroyed)
                {
                    passengers.TryAdd(pawn, false);
                }
            }

            initialPassengerCount = passengers.Count;
            return initialPassengerCount > 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ageTicks, "ageTicks");
            Scribe_Values.Look(ref initialPassengerCount, "initialPassengerCount");
            Scribe_Values.Look(ref startedPassengerCount, "startedPassengerCount");
            Scribe_Values.Look(ref releasedPassengerCount, "releasedPassengerCount");
            Scribe_Values.Look(ref approachInitialized, "approachInitialized");
            Scribe_Values.Look(ref approachCompleteTick, "approachCompleteTick", -1);
            Scribe_Values.Look(ref approachDrawX, "approachDrawX");
            Scribe_Values.Look(ref approachSpeed, "approachSpeed");
            Scribe_Values.Look(ref approachAcceleration, "approachAcceleration");
            Scribe_Values.Look(ref approachTiltDegrees, "approachTiltDegrees");
            Scribe_Values.Look(ref approachBraking, "approachBraking");
            Scribe_Values.Look(ref approachBrakeStartSpeed, "approachBrakeStartSpeed");
            if (passengers == null)
            {
                passengers = new ThingOwner<Pawn>(this, LookMode.Deep, false);
            }
            passengers.ExposeData();
            Scribe_Collections.Look(ref descendingPassengers,
                "descendingPassengers", LookMode.Reference);
            Scribe_Collections.Look(ref descendingStartTicks,
                "descendingStartTicks", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (descendingPassengers == null)
                {
                    descendingPassengers = new List<Pawn>();
                }
                if (descendingStartTicks == null)
                {
                    descendingStartTicks = new List<int>();
                }
            }
        }

        protected override void Tick()
        {
            base.Tick();
            ageTicks++;
            TickApproachMovement();
            TickApproachTilt();
            MaintainLoopSound();
            ThrowRotorWashDust();

            while (startedPassengerCount < initialPassengerCount
                && ageTicks >= UnloadStart
                    + startedPassengerCount * PassengerStartIntervalTicks)
            {
                StartNextPassengerDescent();
            }

            for (int index = descendingPassengers.Count - 1; index >= 0; index--)
            {
                if (index < descendingStartTicks.Count
                    && ageTicks >= descendingStartTicks[index] + PassengerDescentTicks)
                {
                    ReleasePassenger(descendingPassengers[index]);
                    descendingPassengers.RemoveAt(index);
                    descendingStartTicks.RemoveAt(index);
                }
            }

            if (ageTicks >= ExitEnd)
            {
                ReleaseAllPassengers();
                Destroy(DestroyMode.Vanish);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            EndLoopSound();

            if (Spawned && passengers.Count > 0)
            {
                ReleaseAllPassengers();
            }

            base.Destroy(mode);
        }

        private void MaintainLoopSound()
        {
            if (Map == null)
            {
                return;
            }

            if (helicopterLoopSustainer == null || helicopterLoopSustainer.Ended)
            {
                SoundDef soundDef = DefDatabase<SoundDef>.GetNamedSilentFail(
                    HelicopterLoopSoundDefName);
                if (soundDef != null)
                {
                    SoundInfo soundInfo = SoundInfo.InMap(
                        new TargetInfo(this), MaintenanceType.PerTick);
                    helicopterLoopSustainer =
                        SoundStarter.TrySpawnSustainer(soundDef, soundInfo);
                }
            }

            helicopterLoopSustainer?.Maintain();
        }

        private void EndLoopSound()
        {
            if (helicopterLoopSustainer != null
                && !helicopterLoopSustainer.Ended)
            {
                helicopterLoopSustainer.End();
            }

            helicopterLoopSustainer = null;
        }

        private void ThrowRotorWashDust()
        {
            if (Map == null)
            {
                return;
            }

            bool hovering = approachCompleteTick >= 0
                && ageTicks < DoorCloseEnd;
            int interval = hovering ? 4 : 6;
            if (ageTicks % interval != 0)
            {
                return;
            }

            Vector3 center = AircraftDrawPosition();
            center.z -= Mh60HeliborneUtility.RopeLandingDistance;
            int dustCount = hovering ? 14 : 11;
            float baseAngle = ageTicks * 13f + Rand.Range(0f, 360f);
            for (int index = 0; index < dustCount; index++)
            {
                float angle = baseAngle
                    + index * (360f / dustCount)
                    + Rand.Range(-8f, 8f);
                float radius = Rand.Range(
                    DustRingInnerRadius, DustRingOuterRadius);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up)
                    * Vector3.right;
                Vector3 dustPosition = center + direction * radius;
                if (!dustPosition.ToIntVec3().InBounds(Map))
                {
                    continue;
                }

                float size = hovering
                    ? Rand.Range(0.9f, 1.35f)
                    : Rand.Range(0.65f, 0.95f);
                FleckMaker.ThrowDustPuff(dustPosition, Map, size);
            }

            // Sparse interior particles keep the rotor wash from looking like
            // a perfectly hollow outline while preserving the stronger rim.
            int interiorDustCount = hovering ? 4 : 4;
            for (int index = 0; index < interiorDustCount; index++)
            {
                float angle = Rand.Range(0f, 360f);
                float radius = Mathf.Sqrt(Rand.Value) * DustRingInnerRadius;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up)
                    * Vector3.right;
                Vector3 dustPosition = center + direction * radius;
                if (dustPosition.ToIntVec3().InBounds(Map))
                {
                    FleckMaker.ThrowDustPuff(
                        dustPosition,
                        Map,
                        hovering
                            ? Rand.Range(0.55f, 0.85f)
                            : Rand.Range(0.5f, 0.75f));
                }
            }

            // Occasionally mix in the visual-only smoke fleck used by the
            // M2A2 firing effect. It has no gas or gameplay consequences.
            float smokeChance = hovering ? 0.18f : 0.12f;
            if (Rand.Chance(smokeChance) && RotorWashSmokeFleck != null)
            {
                float angle = Rand.Range(0f, 360f);
                float radius = Mathf.Sqrt(Rand.Value) * DustRingOuterRadius;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up)
                    * Vector3.right;
                Vector3 smokePosition = center + direction * radius;
                if (smokePosition.ToIntVec3().InBounds(Map))
                {
                    FleckMaker.Static(
                        smokePosition,
                        Map,
                        RotorWashSmokeFleck,
                        Rand.Range(0.45f, 0.75f));
                }
            }
        }

        private static FleckDef RotorWashSmokeFleck =>
            rotorWashSmokeFleck ?? (rotorWashSmokeFleck =
                DefDatabase<FleckDef>.GetNamedSilentFail(
                    RotorWashSmokeFleckDefName));

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Vector3 aircraftPosition = AircraftDrawPosition();
            float baseAltitude = AltitudeLayer.MoteOverhead.AltitudeFor();
            aircraftPosition.y = baseAltitude;
            Quaternion aircraftRotation = Quaternion.AngleAxis(AircraftAngleDegrees(), Vector3.up);

            float doorOpen = DoorOpenFactor();
            Vector3 doorOffset = aircraftRotation
                * new Vector3(Mathf.Lerp(ClosedDoorEastOffset, 0f, doorOpen), 0f, 0f);
            Vector3 doorPosition = aircraftPosition + doorOffset;

            // The outline is intentionally the very bottom aircraft layer.
            DrawPart(doorPosition, baseAltitude, DoorOutlineMaterial,
                AircraftDrawSize, AircraftDrawSize, aircraftRotation);
            DrawRope(aircraftPosition, baseAltitude + 0.01f);
            DrawDescendingPassenger(aircraftPosition, baseAltitude + 0.02f);
            DrawPart(aircraftPosition, baseAltitude + 0.03f, BaseMaterial,
                AircraftDrawSize, AircraftDrawSize, aircraftRotation);
            DrawPart(doorPosition, baseAltitude + 0.04f, DoorFrontMaterial,
                AircraftDrawSize, AircraftDrawSize, aircraftRotation);
            // Rotor art must always be above the fuselage. The main rotor
            // animates independently while the existing tail rotor remains visible.
            DrawPart(aircraftPosition, baseAltitude + 0.05f, MainRotorFlyMaterial,
                AircraftDrawSize, AircraftDrawSize, aircraftRotation);
            DrawPart(aircraftPosition, baseAltitude + 0.06f, WingFlyMaterial,
                AircraftDrawSize, AircraftDrawSize, aircraftRotation);
        }

        private void StartNextPassengerDescent()
        {
            Pawn pawn = passengers.InnerListForReading
                .FirstOrDefault(candidate => candidate != null
                    && !descendingPassengers.Contains(candidate));
            startedPassengerCount++;
            if (pawn == null)
            {
                return;
            }

            descendingPassengers.Add(pawn);
            descendingStartTicks.Add(ageTicks);
        }

        private void ReleasePassenger(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            passengers.Take(pawn);
            releasedPassengerCount++;
            if (Map == null || pawn.Destroyed)
            {
                return;
            }

            IntVec3 landingCenter = Mh60HeliborneUtility.LandingCell(Position)
                + new IntVec3(Mathf.RoundToInt(PassengerEastOffset), 0, 0);
            IntVec3 spawnCell;
            if (!CellFinder.TryFindRandomSpawnCellForPawnNear(landingCenter, Map, out spawnCell, 3))
            {
                spawnCell = landingCenter;
            }

            GenSpawn.Spawn(pawn, spawnCell, Map);
        }

        private void ReleaseAllPassengers()
        {
            descendingPassengers.Clear();
            descendingStartTicks.Clear();
            while (passengers.Count > 0)
            {
                ReleasePassenger(passengers[0]);
            }
        }

        private Vector3 AircraftDrawPosition()
        {
            Vector3 target = Position.ToVector3Shifted();
            if (Map == null)
            {
                return target;
            }

            if (approachCompleteTick < 0)
            {
                EnsureApproachInitialized();
                target.x = approachDrawX;
                return target;
            }

            if (ageTicks >= DoorCloseEnd)
            {
                float progress = ExitProgress((ageTicks - DoorCloseEnd) / (float)ExitTicks);
                Vector3 end = target;
                end.x = Map.Size.x + AircraftDrawSize;
                return Vector3.Lerp(target, end, progress);
            }

            float hoverBlend = Smooth01(
                (ageTicks - ApproachEndTick) / (float)SettleTicks);
            target.x += Mathf.Sin(ageTicks * 0.052f) * 0.035f * hoverBlend;
            target.z += Mathf.Sin(ageTicks * 0.075f) * 0.08f * hoverBlend;
            return target;
        }

        private float AircraftAngleDegrees()
        {
            if (ageTicks >= DoorCloseEnd)
            {
                float exitProgress = (ageTicks - DoorCloseEnd) / (float)ExitTicks;
                // ExitProgress has acceleration PI * sin(PI*t), so this angle
                // follows the same acceleration curve with a fixed scale.
                return 6f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(exitProgress));
            }

            return approachTiltDegrees;
        }

        private void TickApproachTilt()
        {
            if (approachCompleteTick >= 0)
            {
                // Position, speed, and approach tilt finish on the same tick.
                approachTiltDegrees = 0f;
                return;
            }

            if (approachBraking && approachBrakeStartSpeed > 0.001f)
            {
                float brakingProgress = 1f - Mathf.Clamp01(
                    approachSpeed / approachBrakeStartSpeed);
                float pull = Smooth01(brakingProgress / BrakeTiltPullPortion);
                float release = 1f - Smooth01(
                    (brakingProgress - BrakeTiltPullPortion)
                    / (1f - BrakeTiltPullPortion));
                approachTiltDegrees = -MaximumApproachTiltDegrees
                    * pull * release;
                return;
            }

            float targetTilt = Mathf.Clamp(
                approachAcceleration / MaximumApproachAcceleration, 0f, 1f)
                * MaximumApproachTiltDegrees;
            approachTiltDegrees = Mathf.MoveTowards(
                approachTiltDegrees, targetTilt, ApproachTiltSpeed);
        }

        private void EnsureApproachInitialized()
        {
            if (approachInitialized)
            {
                return;
            }

            approachInitialized = true;
            approachDrawX = -AircraftDrawSize;
            approachSpeed = 0f;
            approachAcceleration = 0f;
        }

        private void TickApproachMovement()
        {
            if (approachCompleteTick >= 0 || Map == null)
            {
                return;
            }

            EnsureApproachInitialized();
            float targetX = Position.ToVector3Shifted().x;
            float remainingDistance = Mathf.Max(0f, targetX - approachDrawX);
            if (remainingDistance <= 0.001f)
            {
                CompleteApproach(targetX);
                return;
            }

            float brakingDistance = approachSpeed * approachSpeed
                / (2f * ApproachBrakeAcceleration)
                * ApproachBrakingLeadFactor;
            float nextSpeed;
            if (remainingDistance <= brakingDistance)
            {
                if (!approachBraking)
                {
                    approachBraking = true;
                    approachBrakeStartSpeed = Mathf.Max(approachSpeed, 0.001f);
                }

                float desiredSpeed = Mathf.Sqrt(
                    2f * ApproachBrakeAcceleration * remainingDistance
                    / ApproachBrakingLeadFactor);
                nextSpeed = Mathf.MoveTowards(
                    approachSpeed, desiredSpeed, ApproachBrakeAcceleration);
            }
            else
            {
                // Acceleration is strongest at low speed and falls
                // exponentially as the helicopter approaches maximum speed.
                float speedRatio = approachSpeed / MaximumApproachSpeed;
                float acceleration = MaximumApproachAcceleration
                    * Mathf.Exp(-ApproachAccelerationDecay * speedRatio);
                nextSpeed = Mathf.Min(
                    MaximumApproachSpeed, approachSpeed + acceleration);
            }

            approachAcceleration = nextSpeed - approachSpeed;
            float travelDistance = (approachSpeed + nextSpeed) * 0.5f;
            approachSpeed = nextSpeed;
            if (travelDistance >= remainingDistance || remainingDistance < 0.05f)
            {
                CompleteApproach(targetX);
                return;
            }

            approachDrawX += travelDistance;
        }

        private void CompleteApproach(float targetX)
        {
            approachDrawX = targetX;
            approachSpeed = 0f;
            approachAcceleration = 0f;
            approachCompleteTick = ageTicks;
        }

        private static float ExitProgress(float value)
        {
            value = Mathf.Clamp01(value);
            return value - Mathf.Sin(Mathf.PI * value) / Mathf.PI;
        }

        private float DoorOpenFactor()
        {
            if (ageTicks < DoorOpenStart)
            {
                return 0f;
            }

            if (ageTicks < RopeRetractEnd)
            {
                return Smooth01((ageTicks - DoorOpenStart) / (float)DoorTicks);
            }

            return 1f - Smooth01((ageTicks - RopeRetractEnd) / (float)DoorTicks);
        }

        private float RopeFactor()
        {
            if (ageTicks < RopeDeployStart || ageTicks >= RopeRetractEnd)
            {
                return 0f;
            }

            // The texture already depicts a rope touching the ground. It appears
            // at full length immediately and never scales toward its center.
            return 1f;
        }

        private void DrawRope(Vector3 aircraftPosition, float altitude)
        {
            float factor = RopeFactor();
            if (factor <= 0.001f)
            {
                return;
            }

            float height = FastRopeDrawSize;
            // Keep the rope's vertical placement fixed in map space, but inherit
            // the helicopter's horizontal hover so the attachment point stays aligned.
            Vector3 ropePosition = Position.ToVector3Shifted();
            ropePosition.x = aircraftPosition.x + PassengerEastOffset;
            // The source texture has transparent space above the rope. Moving the
            // draw box uses the original, unscaled placement size. Only the
            // texture itself is enlarged; its positional offset stays unchanged.
            ropePosition.z -= RopeTopSouthOffset
                + FastRopePlacementSize * (0.5f - 0.0625f);
            DrawPart(ropePosition, altitude, RopeMaterial,
                FastRopeDrawSize, height, Quaternion.identity);
        }

        private void DrawDescendingPassenger(Vector3 aircraftPosition, float altitude)
        {
            if (descendingPassengers.Count == 0)
            {
                return;
            }

            int count = Mathf.Min(descendingPassengers.Count, descendingStartTicks.Count);
            for (int index = 0; index < count; index++)
            {
                Pawn pawn = descendingPassengers[index];
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                float progress = Smooth01(
                    (ageTicks - descendingStartTicks[index])
                    / (float)PassengerDescentTicks);
                Vector3 ropeTop = Position.ToVector3Shifted()
                    + new Vector3(0f, 0f, -RopeTopSouthOffset);
                Vector3 ropeBottom = Mh60HeliborneUtility.LandingCell(Position).ToVector3Shifted();
                Vector3 pawnPosition = Vector3.Lerp(ropeTop, ropeBottom, progress);
                pawnPosition.x = aircraftPosition.x + PassengerEastOffset;
                pawnPosition.y = altitude + index * 0.0001f;
                pawn.Drawer.renderer.RenderPawnAt(pawnPosition, Rot4.South, false);
            }
        }

        private static void DrawPart(Vector3 position, float altitude, Material material,
            float width, float height, Quaternion rotation)
        {
            if (material == null)
            {
                return;
            }

            position.y = altitude;
            Vector3 scale = new Vector3(width / 10f, 1f, height / 10f);
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(position, rotation, scale), material, 0);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static Material DoorOutlineMaterial => doorOutlineMaterial
            ?? (doorOutlineMaterial = MaterialPool.MatFrom(TextureRoot + "HD_MH60M_DoorOutline", ShaderDatabase.Cutout));

        private static Material BaseMaterial => baseMaterial
            ?? (baseMaterial = MaterialPool.MatFrom(TextureRoot + "HD_MH60M_Base", ShaderDatabase.Cutout));

        private static Material DoorFrontMaterial => doorFrontMaterial
            ?? (doorFrontMaterial = MaterialPool.MatFrom(TextureRoot + "HD_MH60M_DoorFront", ShaderDatabase.Cutout));

        private Material MainRotorFlyMaterial
        {
            get
            {
                if (mainRotorFlyMaterials == null)
                {
                    mainRotorFlyMaterials = new[]
                    {
                        MaterialPool.MatFrom(TextureRoot + "HD_MH60M_1Fly", ShaderDatabase.Transparent),
                        MaterialPool.MatFrom(TextureRoot + "HD_MH60M_2Fly", ShaderDatabase.Transparent),
                        MaterialPool.MatFrom(TextureRoot + "HD_MH60M_3Fly", ShaderDatabase.Transparent),
                        MaterialPool.MatFrom(TextureRoot + "HD_MH60M_4Fly", ShaderDatabase.Transparent)
                    };
                }

                int frameIndex = ageTicks / MainRotorFrameTicks
                    % mainRotorFlyMaterials.Length;
                return mainRotorFlyMaterials[frameIndex];
            }
        }

        private static Material WingFlyMaterial => wingFlyMaterial
            ?? (wingFlyMaterial = MaterialPool.MatFrom(TextureRoot + "HD_MH60M_WingFly", ShaderDatabase.Transparent));

        private static Material RopeMaterial => ropeMaterial
            ?? (ropeMaterial = MaterialPool.MatFrom(TextureRoot + "HD_Fastrope", ShaderDatabase.Transparent));
    }
}
