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
    public sealed class HelodCasFallingBomb : IExposable
    {
        private float releaseX;
        private float releaseZ;
        private IntVec3 impactCell;
        private Pawn caller;
        private int releaseTick;
        private int impactTick;
        private float approachX;
        private float approachZ;
        private HelodCasAttackKind attackKind;
        private Thing guidedTarget;

        public Vector3 ReleasePosition => new Vector3(releaseX, 0f, releaseZ);
        public IntVec3 ImpactCell => guidedTarget != null && guidedTarget.Spawned
            && !guidedTarget.Destroyed ? guidedTarget.Position : impactCell;
        public Pawn Caller => caller;
        public Thing GuidedTarget => guidedTarget != null && !guidedTarget.Destroyed
            ? guidedTarget : null;
        public int ReleaseTick => releaseTick;
        public int ImpactTick => impactTick;
        public Vector2 ApproachDirection => new Vector2(approachX, approachZ).normalized;
        public HelodCasAttackKind AttackKind => attackKind;
        public Vector2 FlightDirection
        {
            get
            {
                IntVec3 currentImpact = ImpactCell;
                Vector2 direction = new Vector2(currentImpact.x + 0.5f - releaseX,
                    currentImpact.z + 0.5f - releaseZ);
                return direction.sqrMagnitude > 0.0001f
                    ? direction.normalized : ApproachDirection;
            }
        }

        public HelodCasFallingBomb()
        {
        }

        public HelodCasFallingBomb(Vector3 releasePosition, IntVec3 impactCell,
            Pawn caller, int releaseTick, int impactTick, Vector2 approachDirection,
            HelodCasAttackKind attackKind, Thing guidedTarget = null)
        {
            releaseX = releasePosition.x;
            releaseZ = releasePosition.z;
            this.impactCell = impactCell;
            this.caller = caller;
            this.releaseTick = releaseTick;
            this.impactTick = impactTick;
            approachX = approachDirection.x;
            approachZ = approachDirection.y;
            this.attackKind = attackKind;
            this.guidedTarget = guidedTarget;
        }

        public float Progress(int now)
        {
            return Mathf.InverseLerp(releaseTick, impactTick, now);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref releaseX, "releaseX", 0f);
            Scribe_Values.Look(ref releaseZ, "releaseZ", 0f);
            Scribe_Values.Look(ref impactCell, "impactCell");
            Scribe_References.Look(ref caller, "caller");
            Scribe_Values.Look(ref releaseTick, "releaseTick", 0);
            Scribe_Values.Look(ref impactTick, "impactTick", 0);
            Scribe_Values.Look(ref approachX, "approachX", 0f);
            Scribe_Values.Look(ref approachZ, "approachZ", 1f);
            Scribe_Values.Look(ref attackKind, "attackKind", HelodCasAttackKind.Bombing);
            Scribe_References.Look(ref guidedTarget, "guidedTarget");
        }
    }

    public sealed class HelodCasAttackPlan : IExposable
    {
        private IntVec3 entryCell;
        private IntVec3 targetCell;
        private float approachX;
        private float approachZ;
        private float majorScatterRadius;
        private float minorScatterRadius;
        private int mountainRoofCount;
        private HelodCasGuidanceMode guidanceMode;
        private float routeLength;
        private Pawn targetPawn;
        private HelodCasAttackKind attackKind;
        private HelodCasAircraftKind aircraftKind;
        private List<IntVec3> designatedTargets = new List<IntVec3>();
        private List<Thing> designatedTargetThings = new List<Thing>();
        private int munitionCount;

        public IntVec3 EntryCell => entryCell;
        public IntVec3 TargetCell => targetCell;
        public Vector2 ApproachDirection => new Vector2(approachX, approachZ).normalized;
        public float MajorScatterRadius => majorScatterRadius;
        public float MinorScatterRadius => minorScatterRadius;
        public int MountainRoofCount => mountainRoofCount;
        public HelodCasGuidanceMode GuidanceMode => guidanceMode;
        public float RouteLength => routeLength;
        public Pawn TargetPawn => targetPawn;
        public HelodCasAttackKind AttackKind => attackKind;
        public HelodCasAircraftKind AircraftKind => aircraftKind;
        public int DesignatedTargetCount => designatedTargets?.Count ?? 0;
        public int MunitionCount => munitionCount > 0 ? munitionCount
            : HelodCasSupportUtility.MunitionCount(attackKind);
        public float FlightRouteLength => HelodCasSupportUtility
            .UsesAttackCorridor(attackKind)
            ? Mathf.Max(0f, routeLength - HelodCasSupportUtility.StrafeLength * 0.5f)
            : routeLength;
        public Vector3 StrafeStartPosition => StrafePoint(-0.5f);
        public Vector3 StrafeEndPosition => StrafePoint(0.5f);
        public IntVec3 StrafeStart => StrafeStartCell(entryCell, targetCell);
        public IntVec3 StrafeEnd => StrafeEndCell(entryCell, targetCell);

        public HelodCasAttackPlan()
        {
        }

        public HelodCasAttackPlan(IntVec3 entryCell, IntVec3 targetCell,
            HelodCasGuidanceMode guidanceMode, Map map, float majorScatterRadius,
            float minorScatterRadius,
            HelodCasAttackKind attackKind = HelodCasAttackKind.Bombing,
            HelodCasAircraftKind aircraftKind = HelodCasAircraftKind.P47,
            IEnumerable<IntVec3> designatedTargets = null,
            IEnumerable<Thing> designatedTargetThings = null,
            int munitionCount = 0)
        {
            this.entryCell = entryCell;
            this.targetCell = targetCell;
            this.guidanceMode = guidanceMode;
            this.majorScatterRadius = majorScatterRadius;
            this.minorScatterRadius = minorScatterRadius;
            this.attackKind = attackKind;
            this.aircraftKind = aircraftKind;
            this.designatedTargets = designatedTargets?.ToList()
                ?? new List<IntVec3>();
            this.designatedTargetThings = designatedTargetThings?.ToList()
                ?? new List<Thing>();
            this.munitionCount = munitionCount > 0 ? munitionCount
                : HelodCasSupportUtility.MunitionCount(attackKind);
            Vector2 direction = new Vector2(targetCell.x - entryCell.x,
                targetCell.z - entryCell.z).normalized;
            approachX = direction.x;
            approachZ = direction.y;
            routeLength = entryCell.DistanceTo(targetCell);
            mountainRoofCount = LineCells(entryCell, targetCell)
                .Count(cell => IsMountainRoof(map, cell));
            targetPawn = HelodCasSupportUtility.UsesAreaFire(attackKind)
                ? null : map?.thingGrid?.ThingsListAtFast(targetCell)
                    .OfType<Pawn>().FirstOrDefault();
        }

        public static IEnumerable<IntVec3> LineCells(IntVec3 start, IntVec3 end)
        {
            int x0 = start.x;
            int z0 = start.z;
            int x1 = end.x;
            int z1 = end.z;
            int dx = Mathf.Abs(x1 - x0);
            int dz = Mathf.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int error = dx - dz;
            while (true)
            {
                yield return new IntVec3(x0, 0, z0);
                if (x0 == x1 && z0 == z1)
                {
                    yield break;
                }
                int doubled = error * 2;
                if (doubled > -dz)
                {
                    error -= dz;
                    x0 += sx;
                }
                if (doubled < dx)
                {
                    error += dx;
                    z0 += sz;
                }
            }
        }

        private Vector3 StrafePoint(float lengthFactor)
        {
            Vector3 center = targetCell.ToVector3Shifted();
            Vector2 direction = ApproachDirection;
            return center + new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.StrafeLength * lengthFactor);
        }

        public IntVec3 CurrentAimCell(Map map)
        {
            if (guidanceMode == HelodCasGuidanceMode.TalkOn && targetPawn != null
                && targetPawn.Spawned && !targetPawn.Dead && targetPawn.Map == map)
            {
                return targetPawn.Position;
            }
            return targetCell;
        }

        public IntVec3 DesignatedTargetCell(int index, Map map)
        {
            if (designatedTargets == null || designatedTargets.Count == 0)
            {
                return CurrentAimCell(map);
            }
            int safeIndex = Mathf.Clamp(index, 0, designatedTargets.Count - 1);
            if (designatedTargetThings != null
                && safeIndex < designatedTargetThings.Count)
            {
                Thing thing = designatedTargetThings[safeIndex];
                if (thing != null && thing.Spawned && !thing.Destroyed && thing.Map == map)
                {
                    return thing.Position;
                }
            }
            return designatedTargets[safeIndex];
        }

        public Thing DesignatedTargetThing(int index)
        {
            if (designatedTargetThings == null || designatedTargetThings.Count == 0)
            {
                return null;
            }
            int safeIndex = Mathf.Clamp(index, 0, designatedTargetThings.Count - 1);
            Thing thing = designatedTargetThings[safeIndex];
            return thing != null && !thing.Destroyed ? thing : null;
        }

        public Vector3 CurrentStrafeStartPosition(Map map)
        {
            IntVec3 centerCell = CurrentAimCell(map);
            Vector3 center = centerCell.ToVector3Shifted();
            Vector2 direction = ApproachDirection;
            return center - new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.StrafeLength * 0.5f);
        }

        public static IntVec3 StrafeStartCell(IntVec3 entry, IntVec3 center)
        {
            return StrafeBoundaryCell(entry, center, -0.5f);
        }

        public static IntVec3 StrafeEndCell(IntVec3 entry, IntVec3 center)
        {
            return StrafeBoundaryCell(entry, center, 0.5f);
        }

        private static IntVec3 StrafeBoundaryCell(IntVec3 entry, IntVec3 center,
            float lengthFactor)
        {
            Vector2 direction = new Vector2(center.x - entry.x, center.z - entry.z).normalized;
            return new IntVec3(
                Mathf.RoundToInt(center.x + direction.x
                    * HelodCasSupportUtility.StrafeLength * lengthFactor),
                0,
                Mathf.RoundToInt(center.z + direction.y
                    * HelodCasSupportUtility.StrafeLength * lengthFactor));
        }

        public static bool IsMountainRoof(Map map, IntVec3 cell)
        {
            RoofDef roof = map?.roofGrid?.RoofAt(cell);
            return roof != null && roof.isThickRoof;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref entryCell, "entryCell");
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_Values.Look(ref approachX, "approachX", 0f);
            Scribe_Values.Look(ref approachZ, "approachZ", 1f);
            Scribe_Values.Look(ref majorScatterRadius, "majorScatterRadius",
                HelodCasSupportUtility.MajorScatterRadius);
            Scribe_Values.Look(ref minorScatterRadius, "minorScatterRadius",
                HelodCasSupportUtility.MinorScatterRadius);
            Scribe_Values.Look(ref mountainRoofCount, "mountainRoofCount", 0);
            Scribe_Values.Look(ref guidanceMode, "guidanceMode", HelodCasGuidanceMode.TalkOn);
            Scribe_Values.Look(ref routeLength, "routeLength", 0f);
            Scribe_References.Look(ref targetPawn, "targetPawn");
            Scribe_Values.Look(ref attackKind, "attackKind", HelodCasAttackKind.Bombing);
            Scribe_Values.Look(ref aircraftKind, "aircraftKind",
                HelodCasAircraftKind.P47);
            Scribe_Collections.Look(ref designatedTargets, "designatedTargets",
                LookMode.Value);
            Scribe_Collections.Look(ref designatedTargetThings,
                "designatedTargetThings", LookMode.Reference);
            Scribe_Values.Look(ref munitionCount, "munitionCount", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                designatedTargets = designatedTargets ?? new List<IntVec3>();
                designatedTargetThings = designatedTargetThings ?? new List<Thing>();
            }
        }
    }

    public sealed class HelodCasStrike : IExposable
    {
        private HelodCasAttackPlan plan;
        private Pawn caller;
        private HelodForwardBase forwardBase;
        private Thing_M8FlareTarget flareTarget;
        private int queuedTick;
        private int nextGuidanceAttemptTick;
        private int guidanceAttempts;
        private int goAroundCount;
        private float lastGuidanceChance;
        private float lastGuidanceRoll;
        private HelodCasRunState runState;
        private HelodCasAircraftPhase aircraftPhase;
        private int currentAircraftIndex;
        private int aircraftCount = HelodCasSupportUtility.AircraftCount;
        private int aircraftEntryTick;
        private int phaseStartTick;
        private int phaseEndTick;
        private int bombReleaseTick;
        private int cancellationLockTick;
        private int nextAircraftEntryTick;
        private bool bombPairReleased;
        private int goAroundStartTick;
        private int goAroundExitTick;
        private int goAroundTurnSign = 1;
        private bool aborting;
        private Vector3 abortStartPosition;
        private Vector2 abortStartDirection;
        private int abortTurnSign = 1;
        private float abortSignedAngle;
        private int abortTurnTicks;
        private Vector2 abortExitDirection;
        private Vector3 recoveryStartPosition;
        private float recoveryInitialSpeed;
        private float recoveryStartScale = 0.92f;
        private float nextStrafeBurstTick;
        private float a10CStrafeRoundAccumulator;
        private int pendingStrafeRounds;
        private int a10CStrafeRoundsQueued;
        private int a10CStrafeSequenceIndex;
        private Vector3 strafeExitStartPosition;
        private Vector2 strafeExitStartDirection;
        private Vector2 strafeExitDirection;
        private float strafeExitSignedAngle;
        private int strafeExitTurnTicks;
        private float strafeExitStartScale = HelodCasSupportUtility.StrafeMinimumScale;
        private Vector3 fixedGuidanceDevicePosition;
        private bool fixedGuidanceDevicePositionInitialized;

        public HelodCasAttackPlan Plan => plan;
        public Pawn Caller => caller;
        public HelodForwardBase ForwardBase => forwardBase;
        public HelodCasRunState RunState => runState;
        public HelodCasAircraftPhase AircraftPhase => aircraftPhase;
        public int CurrentAircraftIndex => currentAircraftIndex;
        public int AircraftCount => aircraftCount;
        public int RecallableBombingAircraftCount => plan?.AttackKind
            == HelodCasAttackKind.Bombing && bombPairReleased
            ? Mathf.Max(0, aircraftCount - currentAircraftIndex) : 0;
        public int GuidanceAttempts => guidanceAttempts;
        public int GoAroundCount => goAroundCount;
        public float LastGuidanceChance => lastGuidanceChance;
        public float LastGuidanceRoll => lastGuidanceRoll;
        public int PhaseStartTick => phaseStartTick;
        public int PhaseEndTick => phaseEndTick;
        public int BombReleaseTick => bombReleaseTick;
        public int CancellationLockTick => cancellationLockTick;
        public bool RequiresStationaryGuidance => !aborting
            && aircraftPhase != HelodCasAircraftPhase.Complete;

        public Vector3 FixedGuidanceDevicePosition(Pawn operatorPawn,
            HelodCasLaserDesignatorExtension designator)
        {
            if (!fixedGuidanceDevicePositionInitialized)
            {
                Vector3 forward = operatorPawn.Rotation.FacingCell.ToVector3();
                Vector3 right = new Vector3(forward.z, 0f, -forward.x);
                fixedGuidanceDevicePosition = operatorPawn.DrawPos
                    + forward * designator.guidanceGraphicForwardOffset
                    + right * designator.guidanceGraphicLateralOffset;
                fixedGuidanceDevicePositionInitialized = true;
            }
            return fixedGuidanceDevicePosition;
        }

        public bool IzlidSkySignalActive(int now)
        {
            if (aborting || runState == HelodCasRunState.Attacking)
            {
                return false;
            }
            int approachStart = runState == HelodCasRunState.Approaching
                ? queuedTick : nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
            return now >= approachStart && now < approachStart
                + HelodCasSupportUtility.IzlidSkySignalTicks;
        }

        public bool IzlidTargetSignalActive(int now)
        {
            if (aborting)
            {
                return false;
            }
            int approachStart = runState == HelodCasRunState.Approaching
                ? queuedTick : nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
            int targetStart = approachStart
                    + HelodCasSupportUtility.IzlidSkySignalTicks
                    + HelodCasSupportUtility.IzlidSignalPauseTicks;
            return now >= targetStart && now < targetStart
                + HelodCasSupportUtility.IzlidTargetSignalTicks;
        }

        public int IzlidTargetSignalStartTick => nextGuidanceAttemptTick
            - (HelodCasSupportUtility.ArrivalDelayTicks
                - HelodCasSupportUtility.IzlidSkySignalTicks
                - HelodCasSupportUtility.IzlidSignalPauseTicks);

        public HelodCasStrike()
        {
        }

        public HelodCasStrike(HelodCasAttackPlan plan, Pawn caller,
            HelodForwardBase forwardBase, Thing_M8FlareTarget flareTarget,
            int queuedTick, int firstGuidanceTick, int aircraftCount)
        {
            this.plan = plan;
            this.caller = caller;
            this.forwardBase = forwardBase;
            this.flareTarget = flareTarget;
            this.queuedTick = queuedTick;
            this.aircraftCount = Mathf.Max(1, aircraftCount);
            nextGuidanceAttemptTick = firstGuidanceTick;
            runState = HelodCasRunState.Approaching;
            aircraftPhase = HelodCasAircraftPhase.NotStarted;
        }

        public bool GuidanceValid(Map map)
        {
            if (aborting)
            {
                return true;
            }
            if (plan == null)
            {
                return false;
            }
            if (caller == null || !caller.Spawned || caller.Map != map
                || caller.Dead || caller.Downed)
            {
                return false;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.TalkOn)
            {
                return true;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Flare)
            {
                return flareTarget != null && flareTarget.IsActiveFlare && flareTarget.Map == map;
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Laser)
            {
                return HelodCasSupportUtility.TryGetLaserDesignatorOperator(caller,
                    map, plan.CurrentAimCell(map), out _, out _);
            }
            return false;
        }

        public bool NeedsGuidanceAttempt(int now)
        {
            return runState != HelodCasRunState.Attacking
                && now >= nextGuidanceAttemptTick;
        }

        public int TicksUntilGuidanceAttempt(int now)
        {
            return Mathf.Max(0, nextGuidanceAttemptTick - now);
        }

        public float CurrentGuidanceChance(Map map)
        {
            return CasGuidance.GuidanceSuccessChance(plan, caller, map,
                goAroundCount);
        }

        public bool TryGuidance(Map map)
        {
            lastGuidanceChance = CurrentGuidanceChance(map);
            lastGuidanceRoll = Rand.Value;
            guidanceAttempts++;
            if (lastGuidanceRoll <= lastGuidanceChance)
            {
                runState = HelodCasRunState.Attacking;
                BeginAircraft(1, Find.TickManager.TicksGame);
                return true;
            }

            runState = HelodCasRunState.GoAround;
            aircraftPhase = HelodCasAircraftPhase.NotStarted;
            goAroundCount++;
            goAroundStartTick = Find.TickManager.TicksGame;
            goAroundExitTick = goAroundStartTick + HelodCasSupportUtility.GoAroundExitTicks;
            goAroundTurnSign = Rand.Bool ? 1 : -1;
            int minimumGoAroundTicks = plan.AircraftKind == HelodCasAircraftKind.A10C
                ? HelodCasSupportUtility.A10CGoAroundMinimumTicks
                : HelodCasSupportUtility.GoAroundMinimumTicks;
            int maximumGoAroundTicks = plan.AircraftKind == HelodCasAircraftKind.A10C
                ? HelodCasSupportUtility.A10CGoAroundMaximumTicks
                : HelodCasSupportUtility.GoAroundMaximumTicks;
            nextGuidanceAttemptTick = Find.TickManager.TicksGame
                + Rand.RangeInclusive(minimumGoAroundTicks, maximumGoAroundTicks);
            return false;
        }

        public HelodCasAircraftTickEvent TickAircraft(int now, Map map)
        {
            if (runState != HelodCasRunState.Attacking)
            {
                return HelodCasAircraftTickEvent.None;
            }

            if (plan.AircraftKind == HelodCasAircraftKind.A10C
                && plan.AttackKind == HelodCasAttackKind.Strafing
                && a10CStrafeRoundsQueued >= HelodCasSupportUtility.A10CStrafeRoundCount
                && (aircraftPhase == HelodCasAircraftPhase.StrafeApproach
                    || aircraftPhase == HelodCasAircraftPhase.Strafing))
            {
                BeginStrafeExit(now, map, AircraftDrawPosition(now),
                    AircraftDrawScale(now), false);
            }

            if (aircraftPhase == HelodCasAircraftPhase.Entry && now >= phaseEndTick)
            {
                if (plan.AttackKind == HelodCasAttackKind.Strafing)
                {
                    BeginStrafeApproach(now);
                }
                else if (HelodCasSupportUtility.RequiresDive(plan.AircraftKind,
                    plan.AttackKind))
                {
                    BeginDive(now);
                }
                else
                {
                    BeginLevelAttack(now);
                }
            }

            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                if (!bombPairReleased && now >= bombReleaseTick)
                {
                    bombPairReleased = true;
                    return HelodCasAircraftTickEvent.ReleaseBombPair;
                }
                if (now >= phaseEndTick)
                {
                    BeginRecovery(now, plan.TargetCell.ToVector3Shifted(),
                        HelodCasSupportUtility.AircraftDiveMinimumSpeed, 0.92f);
                }
            }

            if (aircraftPhase == HelodCasAircraftPhase.LevelAttack)
            {
                if (!bombPairReleased && now >= bombReleaseTick)
                {
                    bombPairReleased = true;
                    return HelodCasAircraftTickEvent.ReleaseBombPair;
                }
                if (now >= phaseEndTick)
                {
                    BeginRecovery(now, plan.TargetCell.ToVector3Shifted(),
                        HelodCasSupportUtility.AircraftAttackSpeed, 1.15f);
                }
            }

            if (aircraftPhase == HelodCasAircraftPhase.Recovery && now >= phaseEndTick)
            {
                if (aborting
                    || currentAircraftIndex >= aircraftCount)
                {
                    aircraftPhase = HelodCasAircraftPhase.Complete;
                    return HelodCasAircraftTickEvent.Complete;
                }
                if (now >= nextAircraftEntryTick)
                {
                    BeginAircraft(currentAircraftIndex + 1, now);
                }
            }
            if (aircraftPhase == HelodCasAircraftPhase.AbortTurn && now >= phaseEndTick)
            {
                aircraftPhase = HelodCasAircraftPhase.Complete;
                return HelodCasAircraftTickEvent.Complete;
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeApproach
                && now >= phaseEndTick)
            {
                BeginStrafing(now);
            }
            else if (aircraftPhase == HelodCasAircraftPhase.StrafeApproach
                && TryQueueStrafeFire(now))
            {
                return HelodCasAircraftTickEvent.StrafeBurst;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                if (now < phaseEndTick && TryQueueStrafeFire(now))
                {
                    return HelodCasAircraftTickEvent.StrafeBurst;
                }
                if (now >= phaseEndTick)
                {
                    BeginStrafeExit(now, map, AircraftDrawPosition(now),
                        HelodCasSupportUtility.StrafeMinimumScale, false);
                }
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeExit
                && now >= phaseEndTick)
            {
                if (aborting
                    || currentAircraftIndex >= aircraftCount)
                {
                    aircraftPhase = HelodCasAircraftPhase.Complete;
                    return HelodCasAircraftTickEvent.Complete;
                }
                if (now >= nextAircraftEntryTick)
                {
                    BeginAircraft(currentAircraftIndex + 1, now);
                }
            }
            return HelodCasAircraftTickEvent.None;
        }

        private void BeginAircraft(int aircraftIndex, int now)
        {
            currentAircraftIndex = aircraftIndex;
            aircraftPhase = HelodCasAircraftPhase.Entry;
            aircraftEntryTick = now;
            phaseStartTick = now;
            float terminalApproachDistance = plan.AttackKind == HelodCasAttackKind.Strafing
                ? HelodCasSupportUtility.StrafeApproachDistanceFor(plan.AircraftKind)
                : HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind);
            float entryDistance = Mathf.Max(0f,
                plan.FlightRouteLength - terminalApproachDistance);
            phaseEndTick = now + Mathf.Max(1,
                Mathf.CeilToInt(entryDistance / HelodCasSupportUtility.AircraftAttackSpeed));
            nextAircraftEntryTick = now + HelodCasSupportUtility.FollowupEntryIntervalTicks;
            bombPairReleased = false;
            bombReleaseTick = 0;
            cancellationLockTick = 0;
            nextStrafeBurstTick = 0f;
            a10CStrafeRoundAccumulator = 0f;
            pendingStrafeRounds = 0;
            a10CStrafeRoundsQueued = 0;
            a10CStrafeSequenceIndex = 0;
        }

        private void BeginDive(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.Dive;
            phaseStartTick = now;
            float diveDistance = Mathf.Min(plan.RouteLength,
                HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
            float averageDiveSpeed = (HelodCasSupportUtility.AircraftAttackSpeed
                + HelodCasSupportUtility.AircraftDiveMinimumSpeed) * 0.5f;
            int diveTicks = Mathf.Max(1,
                Mathf.RoundToInt(diveDistance / averageDiveSpeed));
            phaseEndTick = now + diveTicks;
            float releaseDistance = HelodCasSupportUtility.MunitionReleaseDistance(
                plan.AttackKind);
            float distanceBeforeRelease = Mathf.Max(0f, diveDistance
                - Mathf.Min(releaseDistance, diveDistance));
            int releaseOffset = 1;
            while (releaseOffset < diveTicks
                && DiveTravelDistance(releaseOffset, diveTicks, diveDistance)
                    < distanceBeforeRelease)
            {
                releaseOffset++;
            }
            bombReleaseTick = now + releaseOffset;
            cancellationLockTick = bombReleaseTick
                - HelodCasSupportUtility.CancellationLockBeforeReleaseTicks;
        }

        private void BeginLevelAttack(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.LevelAttack;
            phaseStartTick = now;
            float attackDistance = Mathf.Min(plan.RouteLength,
                HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
            int attackTicks = Mathf.Max(1, Mathf.RoundToInt(attackDistance
                / HelodCasSupportUtility.AircraftAttackSpeed));
            phaseEndTick = now + attackTicks;
            float releaseDistance = HelodCasSupportUtility.MunitionReleaseDistance(
                plan.AttackKind);
            float distanceBeforeRelease = Mathf.Max(0f, attackDistance
                - Mathf.Min(releaseDistance, attackDistance));
            bombReleaseTick = now + Mathf.Clamp(Mathf.RoundToInt(
                distanceBeforeRelease / HelodCasSupportUtility.AircraftAttackSpeed),
                1, attackTicks);
            cancellationLockTick = bombReleaseTick
                - HelodCasSupportUtility.CancellationLockBeforeReleaseTicks;
        }

        private void BeginRecovery(int now, Vector3 startPosition, float startSpeed,
            float startScale)
        {
            aircraftPhase = HelodCasAircraftPhase.Recovery;
            phaseStartTick = now;
            phaseEndTick = now + HelodCasSupportUtility.AircraftRecoveryTicks;
            recoveryStartPosition = startPosition;
            recoveryInitialSpeed = startSpeed;
            recoveryStartScale = startScale;
        }

        private void BeginStrafeApproach(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.StrafeApproach;
            phaseStartTick = now;
            float distance = Mathf.Min(plan.FlightRouteLength,
                HelodCasSupportUtility.StrafeApproachDistanceFor(plan.AircraftKind));
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            float averageSpeed = (HelodCasSupportUtility.AircraftAttackSpeed
                + strafeSpeed) * 0.5f;
            phaseEndTick = now + Mathf.Max(1,
                Mathf.RoundToInt(distance / averageSpeed));
            nextStrafeBurstTick = now;
        }

        private bool TryQueueStrafeFire(int now)
        {
            if (now < nextStrafeBurstTick || !CanFireStrafeAt(now))
            {
                return false;
            }
            if (plan.AircraftKind == HelodCasAircraftKind.A10C)
            {
                int remainingRounds = HelodCasSupportUtility.A10CStrafeRoundCount
                    - a10CStrafeRoundsQueued;
                if (remainingRounds <= 0)
                {
                    return false;
                }
                nextStrafeBurstTick = now + 1f;
                a10CStrafeRoundAccumulator += HelodCasSupportUtility.A10CRoundsPerTick;
                pendingStrafeRounds = Mathf.Min(remainingRounds,
                    Mathf.FloorToInt(a10CStrafeRoundAccumulator));
                a10CStrafeRoundAccumulator -= pendingStrafeRounds;
                a10CStrafeRoundsQueued += pendingStrafeRounds;
                return pendingStrafeRounds > 0;
            }

            nextStrafeBurstTick += HelodCasSupportUtility.StrafeTicksPerBurst;
            pendingStrafeRounds = HelodCasSupportUtility.StrafeRoundsPerBurst;
            return true;
        }

        public int ConsumePendingStrafeRounds()
        {
            int rounds = pendingStrafeRounds;
            pendingStrafeRounds = 0;
            return rounds;
        }

        public bool NextA10CStrafeRoundIsHighExplosive(out int shotIndex)
        {
            shotIndex = a10CStrafeSequenceIndex;
            bool highExplosive = shotIndex % 5 == 4;
            a10CStrafeSequenceIndex++;
            return highExplosive;
        }

        private void BeginStrafing(int now)
        {
            aircraftPhase = HelodCasAircraftPhase.Strafing;
            phaseStartTick = now;
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            int strafeTicks = Mathf.Max(1, Mathf.RoundToInt(
                HelodCasSupportUtility.StrafeLength / strafeSpeed));
            if (plan.AircraftKind == HelodCasAircraftKind.A10C)
            {
                int remainingRounds = Mathf.Max(0,
                    HelodCasSupportUtility.A10CStrafeRoundCount
                        - a10CStrafeRoundsQueued);
                int remainingFireTicks = Mathf.CeilToInt(remainingRounds
                    / HelodCasSupportUtility.A10CRoundsPerTick) + 1;
                strafeTicks = Mathf.Max(strafeTicks, remainingFireTicks);
            }
            phaseEndTick = now + strafeTicks;
        }

        private void BeginStrafeExit(int now, Map map, Vector3 startPosition,
            float startScale, bool abortTurn)
        {
            aircraftPhase = HelodCasAircraftPhase.StrafeExit;
            phaseStartTick = now;
            strafeExitStartPosition = startPosition;
            strafeExitStartDirection = plan.ApproachDirection;
            strafeExitStartScale = startScale;
            float turnAngle = (abortTurn
                ? HelodCasSupportUtility.AbortTurnAngleDegrees
                : HelodCasSupportUtility.StrafeExitTurnAngleDegrees)
                * Mathf.Deg2Rad;
            int turnSign = ChooseTurnSign(strafeExitStartPosition,
                strafeExitStartDirection, turnAngle,
                HelodCasSupportUtility.StrafeTurnRadius, map);
            strafeExitSignedAngle = turnAngle * turnSign;
            strafeExitDirection = RotateDirection(strafeExitStartDirection,
                strafeExitSignedAngle);
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            float averageSpeed = (strafeSpeed
                + HelodCasSupportUtility.AircraftAttackSpeed) * 0.5f;
            strafeExitTurnTicks = Mathf.Max(1, Mathf.RoundToInt(
                Mathf.Abs(strafeExitSignedAngle)
                    * HelodCasSupportUtility.StrafeTurnRadius / averageSpeed));
            Vector3 turnEnd = StrafeExitTurnPosition(strafeExitTurnTicks);
            float distanceToEdge = DistanceToMapExit(turnEnd, strafeExitDirection, map);
            int straightTicks = Mathf.Max(1, Mathf.CeilToInt(distanceToEdge
                / HelodCasSupportUtility.AircraftAttackSpeed));
            phaseEndTick = now + strafeExitTurnTicks + straightTicks;
        }

        private static float TransitionTravelDistance(float elapsed, float duration,
            float totalDistance, float startSpeed, float endSpeed)
        {
            duration = Mathf.Max(1f, duration);
            float clampedElapsed = Mathf.Clamp(elapsed, 0f, duration);
            float progress = clampedElapsed / duration;
            float unscaledDistance = startSpeed * clampedElapsed
                + (endSpeed - startSpeed) * duration * SmoothStepIntegral(progress);
            float unscaledTotal = (startSpeed + endSpeed) * 0.5f * duration;
            return totalDistance * unscaledDistance / unscaledTotal;
        }

        private static float SmoothStepIntegral(float progress)
        {
            progress = Mathf.Clamp01(progress);
            return progress * progress * progress * (1f - 0.5f * progress);
        }

        private static float DiveTravelDistance(float elapsed, float duration,
            float totalDistance)
        {
            duration = Mathf.Max(1f, duration);
            float clampedElapsed = Mathf.Clamp(elapsed, 0f, duration);
            float progress = clampedElapsed / duration;
            float unscaledDistance = HelodCasSupportUtility.AircraftAttackSpeed
                    * clampedElapsed
                + (HelodCasSupportUtility.AircraftDiveMinimumSpeed
                    - HelodCasSupportUtility.AircraftAttackSpeed)
                    * duration * SmoothStepIntegral(progress);
            float unscaledTotal = (HelodCasSupportUtility.AircraftAttackSpeed
                + HelodCasSupportUtility.AircraftDiveMinimumSpeed) * 0.5f * duration;
            return totalDistance * unscaledDistance / unscaledTotal;
        }

        private static float DiveSpeed(float elapsed, float duration,
            float totalDistance)
        {
            duration = Mathf.Max(1f, duration);
            float progress = Mathf.Clamp01(elapsed / duration);
            float smoothProgress = progress * progress * (3f - 2f * progress);
            float unscaledSpeed = Mathf.Lerp(
                HelodCasSupportUtility.AircraftAttackSpeed,
                HelodCasSupportUtility.AircraftDiveMinimumSpeed, smoothProgress);
            float unscaledTotal = (HelodCasSupportUtility.AircraftAttackSpeed
                + HelodCasSupportUtility.AircraftDiveMinimumSpeed) * 0.5f * duration;
            return unscaledSpeed * totalDistance / unscaledTotal * duration;
        }

        public bool BeginAbort(int now, Map map)
        {
            if (runState != HelodCasRunState.Attacking
                || aircraftPhase == HelodCasAircraftPhase.NotStarted)
            {
                return false;
            }

            aborting = true;
            nextAircraftEntryTick = int.MaxValue;
            if (aircraftPhase == HelodCasAircraftPhase.Entry
                || aircraftPhase == HelodCasAircraftPhase.StrafeApproach)
            {
                abortStartPosition = AircraftDrawPosition(now);
                abortStartDirection = AircraftDrawDirection(now);
                float turnAngle = HelodCasSupportUtility.AbortTurnAngleDegrees
                    * Mathf.Deg2Rad;
                abortTurnSign = ChooseTurnSign(abortStartPosition,
                    abortStartDirection, turnAngle,
                    HelodCasSupportUtility.AbortTurnRadius, map);
                abortSignedAngle = turnAngle * abortTurnSign;
                abortExitDirection = RotateDirection(abortStartDirection,
                    abortSignedAngle);
                abortTurnTicks = Mathf.Max(1, Mathf.RoundToInt(turnAngle
                    * HelodCasSupportUtility.AbortTurnRadius
                    / HelodCasSupportUtility.AircraftAttackSpeed));
                aircraftPhase = HelodCasAircraftPhase.AbortTurn;
                phaseStartTick = now;
                Vector3 turnEnd = TurnArcPosition(abortStartPosition,
                    abortStartDirection, abortSignedAngle,
                    HelodCasSupportUtility.AbortTurnRadius);
                int exitTicks = Mathf.Max(1, Mathf.CeilToInt(
                    DistanceToMapExit(turnEnd, abortExitDirection, map)
                    / HelodCasSupportUtility.AircraftAttackSpeed));
                phaseEndTick = now + abortTurnTicks + exitTicks;
                return true;
            }
            if (aircraftPhase == HelodCasAircraftPhase.LevelAttack)
            {
                Vector3 startPosition = AircraftDrawPosition(now);
                bombPairReleased = true;
                BeginRecovery(now, startPosition,
                    HelodCasSupportUtility.AircraftAttackSpeed, 1.15f);
                return true;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                Vector3 startPosition = AircraftDrawPosition(now);
                float startScale = AircraftDrawScale(now);
                float diveDistance = Mathf.Min(plan.RouteLength,
                    HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
                float startSpeed = DiveSpeed(now - phaseStartTick,
                    phaseEndTick - phaseStartTick, diveDistance);
                bombPairReleased = true;
                BeginRecovery(now, startPosition, startSpeed, startScale);
                return true;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                Vector3 startPosition = AircraftDrawPosition(now);
                float startScale = AircraftDrawScale(now);
                BeginStrafeExit(now, map, startPosition, startScale, true);
                return true;
            }
            return aircraftPhase == HelodCasAircraftPhase.Recovery
                || aircraftPhase == HelodCasAircraftPhase.AbortTurn
                || aircraftPhase == HelodCasAircraftPhase.StrafeExit;
        }

        public bool CanCancel(int now, out string rejection)
        {
            rejection = null;
            if (aborting)
            {
                rejection = "HD_CAS_Cancel_NoActive".Translate().ToString();
                return false;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Complete)
            {
                rejection = "HD_CAS_Cancel_NoActive".Translate().ToString();
                return false;
            }
            if ((aircraftPhase == HelodCasAircraftPhase.Dive
                || aircraftPhase == HelodCasAircraftPhase.LevelAttack)
                && now >= cancellationLockTick)
            {
                rejection = "HD_CAS_Cancel_Locked".Translate().ToString();
                return false;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Recovery
                && currentAircraftIndex >= aircraftCount)
            {
                rejection = "HD_CAS_Cancel_NoFollowup".Translate().ToString();
                return false;
            }
            return true;
        }

        public IntVec3 NextImpactCell(Map map, int munitionIndex = 0)
        {
            Vector2 direction = plan.ApproachDirection;
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            float angle = Rand.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Rand.Value);
            float along = Mathf.Cos(angle) * plan.MajorScatterRadius * radius;
            float across = Mathf.Sin(angle) * plan.MinorScatterRadius * radius;
            if (HelodCasSupportUtility.IsGuidedMissile(plan.AttackKind))
            {
                along = 0f;
                across = 0f;
            }
            else if (plan.AttackKind == HelodCasAttackKind.GBU54
                && plan.GuidanceMode == HelodCasGuidanceMode.Laser
                && HelodCasSupportUtility.TryGetLaserDesignatorOperator(caller, map,
                    plan.CurrentAimCell(map), out _,
                    out HelodCasLaserDesignatorExtension designator)
                && designator.adjustsGbu54Scatter)
            {
                along *= HelodCasSupportUtility.GBU54LaserScatterMultiplier;
                across *= HelodCasSupportUtility.GBU54LaserScatterMultiplier;
            }
            IntVec3 center = HelodCasSupportUtility.UsesSequentialTargets(
                plan.AttackKind)
                ? plan.DesignatedTargetCell(munitionIndex, map)
                : plan.GuidanceMode == HelodCasGuidanceMode.Flare
                && flareTarget?.IsActiveFlare == true
                ? flareTarget.Position : plan.CurrentAimCell(map);
            if (plan.AttackKind == HelodCasAttackKind.Hydra70)
            {
                int count = plan.MunitionCount;
                float lineProgress = count <= 1 ? 0.5f
                    : (munitionIndex + 0.5f) / count;
                float lineOffset = Mathf.Lerp(-HelodCasSupportUtility.StrafeLength * 0.5f,
                    HelodCasSupportUtility.StrafeLength * 0.5f, lineProgress);
                center = new IntVec3(
                    Mathf.RoundToInt(center.x + direction.x * lineOffset), 0,
                    Mathf.RoundToInt(center.z + direction.y * lineOffset));
                along = Rand.Range(-1.25f, 1.25f);
                across = Rand.Range(-HelodCasSupportUtility.StrafeWidth * 0.5f,
                    HelodCasSupportUtility.StrafeWidth * 0.5f);
            }
            IntVec3 cell = new IntVec3(
                Mathf.RoundToInt(center.x + direction.x * along + lateral.x * across),
                0,
                Mathf.RoundToInt(center.z + direction.y * along + lateral.y * across));
            return new IntVec3(Mathf.Clamp(cell.x, 0, map.Size.x - 1), 0,
                Mathf.Clamp(cell.z, 0, map.Size.z - 1));
        }

        public float StrafeAimDistance(int now)
        {
            Vector3 aircraft = AircraftDrawPosition(now);
            Vector3 start = plan.StrafeStartPosition;
            Vector2 direction = plan.ApproachDirection;
            float aircraftDistance = (aircraft.x - start.x) * direction.x
                + (aircraft.z - start.z) * direction.y;
            float leadDistance = HelodCasSupportUtility.StrafeBulletLeadDistanceFor(
                plan.AircraftKind);
            return Mathf.Clamp(aircraftDistance
                + leadDistance,
                0f, HelodCasSupportUtility.StrafeLength);
        }

        private bool CanFireStrafeAt(int now)
        {
            if (aircraftPhase != HelodCasAircraftPhase.StrafeApproach
                && aircraftPhase != HelodCasAircraftPhase.Strafing)
            {
                return false;
            }
            if (plan.AircraftKind == HelodCasAircraftKind.A10C)
            {
                return a10CStrafeRoundsQueued
                    < HelodCasSupportUtility.A10CStrafeRoundCount;
            }
            Vector3 aircraft = AircraftDrawPosition(now);
            Vector3 start = plan.StrafeStartPosition;
            Vector2 direction = plan.ApproachDirection;
            float aircraftDistance = (aircraft.x - start.x) * direction.x
                + (aircraft.z - start.z) * direction.y;
            float leadDistance = HelodCasSupportUtility.StrafeBulletLeadDistanceFor(
                plan.AircraftKind);
            return aircraftDistance >= -leadDistance
                && aircraftDistance <= HelodCasSupportUtility.StrafeLength
                    - leadDistance;
        }

        public Vector3 AircraftDrawPosition(int now)
        {
            Vector3 entry = plan.EntryCell.ToVector3Shifted();
            Vector3 target = plan.TargetCell.ToVector3Shifted();
            Vector2 direction = plan.ApproachDirection;
            if (runState == HelodCasRunState.GoAround)
            {
                int retryApproachStart = nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
                if (now >= retryApproachStart)
                {
                    return InboundToEntryPosition(entry, direction,
                        retryApproachStart, nextGuidanceAttemptTick, now);
                }
                return GoAroundPosition(entry, direction, now);
            }
            if (runState != HelodCasRunState.Attacking
                || aircraftPhase == HelodCasAircraftPhase.NotStarted)
            {
                return InboundToEntryPosition(entry, direction, queuedTick,
                    nextGuidanceAttemptTick, now);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Entry)
            {
                float terminalApproachDistance = plan.AttackKind
                    == HelodCasAttackKind.Strafing
                    ? HelodCasSupportUtility.StrafeApproachDistanceFor(
                        plan.AircraftKind)
                    : HelodCasSupportUtility.AttackApproachDistance(
                        plan.AttackKind);
                float entryDistance = Mathf.Max(0f,
                    plan.FlightRouteLength - terminalApproachDistance);
                float runDistance = Mathf.Min(entryDistance,
                    Mathf.Max(0, now - aircraftEntryTick)
                    * HelodCasSupportUtility.AircraftAttackSpeed);
                return entry + new Vector3(direction.x, 0f, direction.y) * runDistance;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                float diveDistance = Mathf.Min(plan.RouteLength,
                    HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
                Vector3 diveStart = target
                    - new Vector3(direction.x, 0f, direction.y) * diveDistance;
                float runDistance = DiveTravelDistance(now - phaseStartTick,
                    phaseEndTick - phaseStartTick, diveDistance);
                return diveStart
                    + new Vector3(direction.x, 0f, direction.y) * runDistance;
            }
            if (aircraftPhase == HelodCasAircraftPhase.LevelAttack)
            {
                float attackDistance = Mathf.Min(plan.RouteLength,
                    HelodCasSupportUtility.AttackApproachDistance(plan.AttackKind));
                Vector3 attackStart = target
                    - new Vector3(direction.x, 0f, direction.y) * attackDistance;
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return attackStart + new Vector3(direction.x, 0f, direction.y)
                    * (attackDistance * progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.AbortTurn)
            {
                return AbortTurnPosition(now);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeApproach)
            {
                Vector3 strafeStart = plan.StrafeStartPosition;
                float approachDistance = Mathf.Min(plan.FlightRouteLength,
                    HelodCasSupportUtility.StrafeApproachDistanceFor(
                        plan.AircraftKind));
                Vector3 approachStart = strafeStart
                    - new Vector3(direction.x, 0f, direction.y) * approachDistance;
                float runDistance = TransitionTravelDistance(now - phaseStartTick,
                    phaseEndTick - phaseStartTick, approachDistance,
                    HelodCasSupportUtility.AircraftAttackSpeed,
                    HelodCasSupportUtility.AircraftAttackSpeed
                        * HelodCasSupportUtility.StrafeSpeedFactor);
                return approachStart
                    + new Vector3(direction.x, 0f, direction.y) * runDistance;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return plan.StrafeStartPosition
                    + new Vector3(direction.x, 0f, direction.y)
                    * (HelodCasSupportUtility.StrafeLength * progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeExit)
            {
                int strafeExitElapsed = Mathf.Max(0, now - phaseStartTick);
                if (strafeExitElapsed <= strafeExitTurnTicks)
                {
                    return StrafeExitTurnPosition(strafeExitElapsed);
                }
                Vector3 turnEnd = StrafeExitTurnPosition(strafeExitTurnTicks);
                return turnEnd + new Vector3(strafeExitDirection.x, 0f,
                    strafeExitDirection.y) * (strafeExitElapsed - strafeExitTurnTicks)
                    * HelodCasSupportUtility.AircraftAttackSpeed;
            }
            float elapsed = Mathf.Max(0f, now - phaseStartTick);
            float accelerationTicks = HelodCasSupportUtility.AircraftRecoveryTicks;
            float acceleratedTime = Mathf.Min(elapsed, accelerationTicks);
            float accelerationProgress = acceleratedTime / accelerationTicks;
            float smoothStepIntegral = SmoothStepIntegral(accelerationProgress);
            float recoveryDistance = recoveryInitialSpeed
                    * acceleratedTime
                + (HelodCasSupportUtility.AircraftRecoverySpeed
                    - recoveryInitialSpeed)
                    * accelerationTicks * smoothStepIntegral;
            if (elapsed > accelerationTicks)
            {
                recoveryDistance += (elapsed - accelerationTicks)
                    * HelodCasSupportUtility.AircraftRecoverySpeed;
            }
            return recoveryStartPosition
                + new Vector3(direction.x, 0f, direction.y) * recoveryDistance;
        }

        public bool ShouldDrawAircraft(int now)
        {
            if (runState == HelodCasRunState.Attacking)
            {
                return aircraftPhase != HelodCasAircraftPhase.Complete;
            }
            if (runState == HelodCasRunState.Approaching)
            {
                return true;
            }
            if (runState == HelodCasRunState.GoAround)
            {
                return now <= goAroundExitTick || now >= nextGuidanceAttemptTick
                    - HelodCasSupportUtility.ArrivalDelayTicks;
            }
            return false;
        }

        public Vector2 AircraftDrawDirection(int now)
        {
            Vector3 current = AircraftDrawPosition(now);
            Vector3 next = AircraftDrawPosition(now + 1);
            Vector3 delta = next - current;
            Vector2 direction = new Vector2(delta.x, delta.z);
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized : plan.ApproachDirection;
        }

        private static Vector3 InboundToEntryPosition(Vector3 entry, Vector2 direction,
            int startTick, int endTick, int now)
        {
            int duration = Mathf.Max(1, endTick - startTick);
            Vector3 outside = entry - new Vector3(direction.x, 0f, direction.y)
                * (HelodCasSupportUtility.AircraftAttackSpeed * duration);
            float progress = Mathf.InverseLerp(startTick, endTick, now);
            return Vector3.Lerp(outside, entry, progress);
        }

        private Vector3 GoAroundPosition(Vector3 entry, Vector2 direction, int now)
        {
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            int elapsed = Mathf.Max(0, now - goAroundStartTick);
            float turnProgress = Mathf.Clamp01(elapsed
                / (float)HelodCasSupportUtility.GoAroundTurnTicks);
            float angle = turnProgress * Mathf.PI * goAroundTurnSign;
            Vector2 initialRadius = -lateral * goAroundTurnSign
                * HelodCasSupportUtility.GoAroundTurnRadius;
            Vector2 rotatedRadius = new Vector2(
                initialRadius.x * Mathf.Cos(angle) - initialRadius.y * Mathf.Sin(angle),
                initialRadius.x * Mathf.Sin(angle) + initialRadius.y * Mathf.Cos(angle));
            Vector2 turnCenter = new Vector2(entry.x, entry.z)
                + lateral * goAroundTurnSign * HelodCasSupportUtility.GoAroundTurnRadius;
            Vector3 turnPosition = new Vector3(turnCenter.x + rotatedRadius.x,
                0f, turnCenter.y + rotatedRadius.y);
            if (elapsed <= HelodCasSupportUtility.GoAroundTurnTicks)
            {
                return turnPosition;
            }
            float exitElapsed = elapsed - HelodCasSupportUtility.GoAroundTurnTicks;
            return turnPosition - new Vector3(direction.x, 0f, direction.y)
                * exitElapsed * HelodCasSupportUtility.GoAroundExitSpeed;
        }

        private Vector3 AbortTurnPosition(int now)
        {
            Vector2 direction = abortStartDirection.sqrMagnitude > 0.0001f
                ? abortStartDirection.normalized : plan.ApproachDirection;
            int elapsed = Mathf.Max(0, now - phaseStartTick);
            float turnProgress = Mathf.Clamp01(elapsed
                / (float)Mathf.Max(1, abortTurnTicks));
            Vector3 turnPosition = TurnArcPosition(abortStartPosition, direction,
                abortSignedAngle * turnProgress,
                HelodCasSupportUtility.AbortTurnRadius);
            if (elapsed <= abortTurnTicks)
            {
                return turnPosition;
            }
            float exitElapsed = elapsed - abortTurnTicks;
            return turnPosition + new Vector3(abortExitDirection.x, 0f,
                abortExitDirection.y)
                * exitElapsed * HelodCasSupportUtility.AircraftAttackSpeed;
        }

        private Vector3 StrafeExitTurnPosition(float elapsed)
        {
            float turnTicks = Mathf.Max(1f, strafeExitTurnTicks);
            float strafeSpeed = HelodCasSupportUtility.AircraftAttackSpeed
                * HelodCasSupportUtility.StrafeSpeedFactor;
            float turnDistance = Mathf.Abs(strafeExitSignedAngle)
                * HelodCasSupportUtility.StrafeTurnRadius;
            float travelled = TransitionTravelDistance(elapsed, turnTicks,
                turnDistance, strafeSpeed, HelodCasSupportUtility.AircraftAttackSpeed);
            float angleProgress = turnDistance > 0.001f
                ? travelled / turnDistance : 1f;
            float angle = strafeExitSignedAngle * angleProgress;
            return TurnArcPosition(strafeExitStartPosition, strafeExitStartDirection,
                angle, HelodCasSupportUtility.StrafeTurnRadius);
        }

        private static float DistanceToMapExit(Vector3 start, Vector2 direction, Map map)
        {
            direction.Normalize();
            float xDistance = float.PositiveInfinity;
            float zDistance = float.PositiveInfinity;
            if (direction.x < -0.0001f)
            {
                xDistance = (-1f - start.x) / direction.x;
            }
            else if (direction.x > 0.0001f)
            {
                xDistance = (map.Size.x + 1f - start.x) / direction.x;
            }
            if (direction.y < -0.0001f)
            {
                zDistance = (-1f - start.z) / direction.y;
            }
            else if (direction.y > 0.0001f)
            {
                zDistance = (map.Size.z + 1f - start.z) / direction.y;
            }
            return Mathf.Max(0f, Mathf.Min(xDistance, zDistance));
        }

        private static int ChooseTurnSign(Vector3 start, Vector2 direction,
            float turnAngle, float radius, Map map)
        {
            Vector3 leftEnd = TurnArcPosition(start, direction, turnAngle, radius);
            Vector2 leftDirection = RotateDirection(direction, turnAngle);
            float leftExitDistance = DistanceToMapExit(leftEnd, leftDirection, map);
            Vector3 rightEnd = TurnArcPosition(start, direction, -turnAngle, radius);
            Vector2 rightDirection = RotateDirection(direction, -turnAngle);
            float rightExitDistance = DistanceToMapExit(rightEnd, rightDirection, map);
            return leftExitDistance <= rightExitDistance ? 1 : -1;
        }

        private static Vector2 RotateDirection(Vector2 direction, float angle)
        {
            return new Vector2(
                direction.x * Mathf.Cos(angle) - direction.y * Mathf.Sin(angle),
                direction.x * Mathf.Sin(angle) + direction.y * Mathf.Cos(angle))
                .normalized;
        }

        private static Vector3 TurnArcPosition(Vector3 start, Vector2 direction,
            float signedAngle, float radius)
        {
            float sign = signedAngle >= 0f ? 1f : -1f;
            Vector2 lateral = new Vector2(-direction.y, direction.x);
            Vector2 radiusVector = -lateral * sign * radius;
            Vector2 center = new Vector2(start.x, start.z) - radiusVector;
            Vector2 rotatedRadius = RotateDirection(radiusVector, signedAngle) * radius;
            return new Vector3(center.x + rotatedRadius.x, start.y,
                center.y + rotatedRadius.y);
        }

        public float AircraftDrawScale(int now)
        {
            if (runState != HelodCasRunState.Attacking)
            {
                return 1.15f;
            }
            if (aircraftPhase == HelodCasAircraftPhase.Dive)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return Mathf.SmoothStep(1.15f, 0.92f, progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Recovery)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return Mathf.SmoothStep(recoveryStartScale, 1.15f, progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.Strafing)
            {
                float progress = Mathf.InverseLerp(phaseStartTick, phaseEndTick, now);
                return Mathf.SmoothStep(1.15f,
                    HelodCasSupportUtility.StrafeMinimumScale,
                    progress);
            }
            if (aircraftPhase == HelodCasAircraftPhase.StrafeExit)
            {
                float progress = Mathf.InverseLerp(phaseStartTick,
                    phaseStartTick + Mathf.Max(1, strafeExitTurnTicks), now);
                return Mathf.SmoothStep(strafeExitStartScale, 1.15f, progress);
            }
            return 1.15f;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref plan, "attackPlan");
            Scribe_References.Look(ref caller, "caller");
            Scribe_References.Look(ref forwardBase, "forwardBase");
            Scribe_References.Look(ref flareTarget, "flareTarget");
            Scribe_Values.Look(ref queuedTick, "queuedTick", 0);
            Scribe_Values.Look(ref nextGuidanceAttemptTick, "nextGuidanceAttemptTick", 0);
            Scribe_Values.Look(ref guidanceAttempts, "guidanceAttempts", 0);
            Scribe_Values.Look(ref goAroundCount, "goAroundCount", 0);
            Scribe_Values.Look(ref lastGuidanceChance, "lastGuidanceChance", 0f);
            Scribe_Values.Look(ref lastGuidanceRoll, "lastGuidanceRoll", 0f);
            Scribe_Values.Look(ref runState, "runState", HelodCasRunState.Approaching);
            Scribe_Values.Look(ref aircraftPhase, "aircraftPhase", HelodCasAircraftPhase.NotStarted);
            Scribe_Values.Look(ref currentAircraftIndex, "currentAircraftIndex", 0);
            Scribe_Values.Look(ref aircraftCount, "aircraftCount",
                HelodCasSupportUtility.AircraftCount);
            Scribe_Values.Look(ref aircraftEntryTick, "aircraftEntryTick", 0);
            Scribe_Values.Look(ref phaseStartTick, "phaseStartTick", 0);
            Scribe_Values.Look(ref phaseEndTick, "phaseEndTick", 0);
            Scribe_Values.Look(ref bombReleaseTick, "bombReleaseTick", 0);
            Scribe_Values.Look(ref cancellationLockTick, "cancellationLockTick", 0);
            Scribe_Values.Look(ref nextAircraftEntryTick, "nextAircraftEntryTick", 0);
            Scribe_Values.Look(ref bombPairReleased, "bombPairReleased", false);
            Scribe_Values.Look(ref goAroundStartTick, "goAroundStartTick", 0);
            Scribe_Values.Look(ref goAroundExitTick, "goAroundExitTick", 0);
            Scribe_Values.Look(ref goAroundTurnSign, "goAroundTurnSign", 1);
            Scribe_Values.Look(ref aborting, "aborting", false);
            Scribe_Values.Look(ref abortStartPosition, "abortStartPosition");
            Scribe_Values.Look(ref abortStartDirection, "abortStartDirection");
            Scribe_Values.Look(ref abortTurnSign, "abortTurnSign", 1);
            Scribe_Values.Look(ref abortSignedAngle, "abortSignedAngle", 0f);
            Scribe_Values.Look(ref abortTurnTicks, "abortTurnTicks", 0);
            Scribe_Values.Look(ref abortExitDirection, "abortExitDirection");
            Scribe_Values.Look(ref recoveryStartPosition, "recoveryStartPosition");
            Scribe_Values.Look(ref recoveryInitialSpeed, "recoveryInitialSpeed", 0f);
            Scribe_Values.Look(ref recoveryStartScale, "recoveryStartScale", 0.92f);
            Scribe_Values.Look(ref nextStrafeBurstTick, "nextStrafeBurstTick", 0f);
            Scribe_Values.Look(ref a10CStrafeRoundAccumulator,
                "a10CStrafeRoundAccumulator", 0f);
            Scribe_Values.Look(ref pendingStrafeRounds, "pendingStrafeRounds", 0);
            Scribe_Values.Look(ref a10CStrafeRoundsQueued,
                "a10CStrafeRoundsQueued", 0);
            Scribe_Values.Look(ref a10CStrafeSequenceIndex,
                "a10CStrafeSequenceIndex", 0);
            Scribe_Values.Look(ref strafeExitStartPosition, "strafeExitStartPosition");
            Scribe_Values.Look(ref strafeExitStartDirection, "strafeExitStartDirection");
            Scribe_Values.Look(ref strafeExitDirection, "strafeExitDirection");
            Scribe_Values.Look(ref strafeExitSignedAngle, "strafeExitSignedAngle", 0f);
            Scribe_Values.Look(ref strafeExitTurnTicks, "strafeExitTurnTicks", 0);
            Scribe_Values.Look(ref strafeExitStartScale, "strafeExitStartScale",
                HelodCasSupportUtility.StrafeMinimumScale);
            Scribe_Values.Look(ref fixedGuidanceDevicePosition,
                "fixedGuidanceDevicePosition");
            Scribe_Values.Look(ref fixedGuidanceDevicePositionInitialized,
                "fixedGuidanceDevicePositionInitialized", false);
        }
    }
    internal static class CasGuidance
    {
        public static int NearbySimilarPawnCount(Map map, HelodCasAttackPlan plan)
        {
            Pawn targetPawn = plan?.TargetPawn;
            if (map == null || targetPawn == null || targetPawn.Destroyed
                || targetPawn.Map != map)
            {
                return 0;
            }

            return GenRadial.RadialDistinctThingsAround(targetPawn.Position, map,
                    SimilarPawnRadius, true)
                .OfType<Pawn>()
                .Count(pawn => pawn != targetPawn && !pawn.Dead
                    && pawn.kindDef == targetPawn.kindDef && pawn.Faction == targetPawn.Faction);
        }

        public static float GuidanceSuccessChance(HelodCasAttackPlan plan, Pawn caller,
            Map map, int goAroundCount)
        {
            if (plan == null)
            {
                return 0f;
            }

            int social = Mathf.Clamp(SocialLevel(caller), 0, 20);
            float weatherPenalty = WeatherGuidancePenalty(map);
            if (plan.GuidanceMode == HelodCasGuidanceMode.Laser)
            {
                if (!TryGetLaserDesignatorOperator(caller, map,
                    plan.CurrentAimCell(map), out _,
                    out HelodCasLaserDesignatorExtension designator))
                {
                    return 0f;
                }
                float laserChance = designator.baseGuidanceChance
                    + goAroundCount * 0.06f
                    - weatherPenalty * designator.weatherPenaltyFactor;
                return Mathf.Clamp(laserChance, 0.05f, 0.995f);
            }
            if (plan.GuidanceMode == HelodCasGuidanceMode.Flare)
            {
                float flareChance = 0.88f + social * 0.005f
                    + goAroundCount * FlareGoAroundBonus
                    - weatherPenalty * 0.75f;
                return Mathf.Clamp(flareChance, 0.05f, 0.995f);
            }
            if (plan.GuidanceMode != HelodCasGuidanceMode.TalkOn)
            {
                return 0f;
            }

            float socialBonus = social * 0.025f;
            float routeBonus = Mathf.InverseLerp(8f, 80f, plan.RouteLength) * 0.15f;
            float roofPenalty = Mathf.Min(0.30f, plan.MountainRoofCount * 0.02f);
            float flareBonus = HasNearbyFlare(map, plan.TargetCell) ? 0.10f : 0f;
            float similarPawnPenalty = Mathf.Min(0.18f,
                NearbySimilarPawnCount(map, plan) * 0.03f);
            float retryBonus = goAroundCount * TalkOnGoAroundBonus;
            return Mathf.Clamp(0.30f + socialBonus + routeBonus - roofPenalty
                + flareBonus - similarPawnPenalty + retryBonus
                - weatherPenalty * 0.50f, 0.05f, 0.98f);
        }

    }
}
