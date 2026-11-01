using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Helodrace.Tactical;

namespace Helodrace.Tests.FrozenTactical
{
    public static class TacticalHighSpeedMovementUtility
    {
        public const float MaximumDistance = 12f;
        private const string SlideJobDefName = "HD_TacticalSlide";
        private const float MovementTicksMultiplier = 0.5f;
        private const float SlideStartTicksMultiplier = 0.28f;
        private const float SlideEndTicksMultiplier = 0.5f;
        private const float IncomingAccuracyMultiplier = 0.6f;

        private sealed class State
        {
            public IntVec3 origin;
            public IntVec3 destination;
            public bool sliding;
            public float travelledDistance;
        }

        private static readonly Dictionary<int, State> activeStates =
            new Dictionary<int, State>();
        private static JobDef slideJobDef;

        private static JobDef SlideJobDef => slideJobDef
            ?? (slideJobDef = DefDatabase<JobDef>.GetNamedSilentFail(SlideJobDefName));

        private static bool IsHighSpeedJob(JobDef jobDef)
        {
            return jobDef == JobDefOf.Goto
                || (SlideJobDef != null && jobDef == SlideJobDef);
        }

        public static bool CanUse(Pawn pawn)
        {
            return pawn?.Map != null
                && pawn.Spawned
                && pawn.Drafted
                && !pawn.Dead
                && !pawn.Downed
                && TacticalAimUtility.HasAdvancedCQBTraining(pawn)
                && !TacticalAimUtility.IsAiming(pawn)
                && !IsActive(pawn);
        }

        public static bool IsActive(Pawn pawn)
        {
            if (IsSlideLocked(pawn)) return true;
            if (pawn == null
                || !activeStates.TryGetValue(pawn.thingIDNumber, out State state))
            {
                return false;
            }

            if (pawn.Dead
                || pawn.Downed
                || !pawn.Spawned
                || !TacticalAimUtility.HasAdvancedCQBTraining(pawn)
                || !IsHighSpeedJob(pawn.jobs?.curJob?.def)
                || pawn.Position == state.destination)
            {
                activeStates.Remove(pawn.thingIDNumber);
                return false;
            }

            return true;
        }

        public static bool IsSliding(Pawn pawn)
        {
            return IsSlideLocked(pawn);
        }

        public static bool IsSlideLocked(Pawn pawn)
        {
            return pawn?.Spawned == true && !pawn.Dead && !pawn.Downed
                && pawn.jobs?.curDriver is JobDriver_TacticalSlide driver
                && !driver.Released;
        }

        public static void InitializeSlide(Pawn pawn, IntVec3 destination)
        {
            activeStates[pawn.thingIDNumber] = new State
            {
                origin = pawn.Position, destination = destination, sliding = true
            };
        }

        public static bool TryGetSlideAngle(Pawn pawn, out float angle)
        {
            angle = 0f;
            if (!IsSliding(pawn)
                || !activeStates.TryGetValue(
                    pawn.thingIDNumber,
                    out State state))
            {
                return false;
            }

            Vector3 direction = (state.destination - state.origin).ToVector3();
            if (direction.sqrMagnitude < 0.01f)
            {
                return false;
            }

            angle = direction.AngleFlat();
            return true;
        }

        public static Rot4 SlideBodyFacing(float slideAngle)
        {
            Rot4 direction = Rot4.FromAngleFlat(slideAngle);
            // Use the same side-view sprite for both horizontal directions.
            // The existing draw angle rotates it into the feet-first slide.
            return direction == Rot4.East || direction == Rot4.West
                ? Rot4.East : Rot4.South;
        }

        public static float MovementTicksMultiplierFor(Pawn pawn)
        {
            if (!IsSliding(pawn)
                || !activeStates.TryGetValue(
                    pawn.thingIDNumber,
                    out State state))
            {
                return MovementTicksMultiplier;
            }

            float totalDistance = state.origin.DistanceTo(state.destination);
            float progress = totalDistance <= 0.01f
                ? 1f
                : Mathf.Clamp01(state.travelledDistance / totalDistance);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            return Mathf.Lerp(
                SlideStartTicksMultiplier,
                SlideEndTicksMultiplier,
                easedProgress);
        }

        public enum SlideAdvanceResult
        {
            Continue,
            Completed,
            Blocked
        }

        public static SlideAdvanceResult AdvanceSlide(Pawn pawn)
        {
            if (!TacticalAimUtility.HasAdvancedCQBTraining(pawn)
                || !IsActive(pawn)
                || !activeStates.TryGetValue(
                    pawn.thingIDNumber,
                    out State state)
                || !state.sliding)
            {
                return SlideAdvanceResult.Blocked;
            }

            if (!HasClearSlidePath(pawn, state.destination))
            {
                state.sliding = false;
                return SlideAdvanceResult.Blocked;
            }

            float totalDistance = state.origin.DistanceTo(state.destination);
            if (totalDistance <= 0.01f)
            {
                pawn.SetPositionDirect(state.destination);
                return SlideAdvanceResult.Completed;
            }

            // Cardinal getter already includes our TicksPerMove multiplier.
            float ticksPerCell = Mathf.Max(1f, pawn.TicksPerMoveCardinal);
            state.travelledDistance += 1f / ticksPerCell;
            if (state.travelledDistance >= totalDistance)
            {
                pawn.SetPositionDirect(state.destination);
                return SlideAdvanceResult.Completed;
            }

            Vector3 delta = (state.destination - state.origin).ToVector3();
            float progress = Mathf.Clamp01(
                state.travelledDistance / totalDistance);
            IntVec3 next = new IntVec3(
                Mathf.RoundToInt(state.origin.x + delta.x * progress),
                0,
                Mathf.RoundToInt(state.origin.z + delta.z * progress));
            if (next == pawn.Position)
            {
                return SlideAdvanceResult.Continue;
            }

            if (!next.InBounds(pawn.Map)
                || !next.Standable(pawn.Map)
                || (next.GetFirstPawn(pawn.Map) != null
                    && next.GetFirstPawn(pawn.Map) != pawn))
            {
                state.sliding = false;
                return SlideAdvanceResult.Blocked;
            }

            pawn.SetPositionDirect(next);
            return SlideAdvanceResult.Continue;
        }

        public static void SwitchToNormalMovement(Pawn pawn)
        {
            if (pawn == null
                || !activeStates.TryGetValue(
                    pawn.thingIDNumber,
                    out State state))
            {
                return;
            }

            state.sliding = false;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, state.destination);
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static bool HasClearSlidePath(Pawn pawn, IntVec3 destination)
        {
            if (pawn?.Map == null
                || !destination.InBounds(pawn.Map)
                || !GenSight.LineOfSight(
                    pawn.Position,
                    destination,
                    pawn.Map,
                    true))
            {
                return false;
            }

            IntVec3 origin = pawn.Position;
            Vector3 delta = (destination - origin).ToVector3();
            int steps = Mathf.CeilToInt(delta.magnitude * 2f);
            IntVec3 previous = origin;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                IntVec3 cell = new IntVec3(
                    Mathf.RoundToInt(origin.x + delta.x * t),
                    0,
                    Mathf.RoundToInt(origin.z + delta.z * t));
                if (cell == previous)
                {
                    continue;
                }

                if (!cell.InBounds(pawn.Map)
                    || !cell.Walkable(pawn.Map)
                    || (cell.GetFirstPawn(pawn.Map) != null
                        && cell.GetFirstPawn(pawn.Map) != pawn))
                {
                    return false;
                }

                previous = cell;
            }

            return true;
        }

        public static bool IsValidDestination(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn?.Map == null
                || !target.IsValid
                || !target.Cell.InBounds(pawn.Map)
                || target.Cell == pawn.Position
                || pawn.Position.DistanceTo(target.Cell) > MaximumDistance)
            {
                return false;
            }

            return pawn.CanReach(target.Cell, PathEndMode.OnCell, Danger.Deadly);
        }

        public static bool IsValidDestination(Pawn pawn, TargetInfo target)
        {
            return IsValidDestination(
                pawn,
                new LocalTargetInfo(target.Cell));
        }

        public static void BeginTargeting(Pawn pawn)
        {
            if (!CanUse(pawn))
            {
                return;
            }

            Map map = pawn.Map;
            IntVec3 origin = pawn.Position;
            Action<LocalTargetInfo> drawPreview = target =>
            {
                GenDraw.DrawRadiusRing(origin, MaximumDistance, Color.cyan);
                if (target.IsValid && target.Cell.InBounds(map))
                {
                    GenDraw.DrawRadiusRing(
                        target.Cell,
                        0.55f,
                        IsValidDestination(pawn, target)
                            ? Color.green
                            : Color.red);
                }
            };

            Find.Targeter.BeginTargeting(
                new TargetingParameters
                {
                    canTargetPawns = false,
                    canTargetBuildings = false,
                    canTargetLocations = true,
                    validator = target => IsValidDestination(pawn, target)
                },
                target => Start(pawn, target.Cell),
                drawPreview);

            Helodrace.MapComponent_PersistentTargetingOverlay.Set(map, drawPreview);
        }

        public static void Start(Pawn pawn, IntVec3 destination)
        {
            LocalTargetInfo target = new LocalTargetInfo(destination);
            if (!CanUse(pawn) || !IsValidDestination(pawn, target))
            {
                Messages.Message(
                    "HD_TacticalHighSpeed_InvalidTarget".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            bool canSlide = HasClearSlidePath(pawn, destination)
                && SlideJobDef != null;
            activeStates[pawn.thingIDNumber] = new State
            {
                origin = pawn.Position,
                destination = destination,
                sliding = canSlide,
                travelledDistance = 0f
            };

            Job job = JobMaker.MakeJob(
                canSlide ? SlideJobDef : JobDefOf.Goto,
                destination);
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            Messages.Message(
                "HD_TacticalHighSpeed_Started".Translate(),
                pawn,
                MessageTypeDefOf.NeutralEvent,
                false);
        }

        public static Command CreateCommand(Pawn pawn)
        {
            bool disabled = !CanUse(pawn);
            string disabledReason = null;
            if (TacticalAimUtility.IsAiming(pawn))
            {
                disabledReason = "HD_TacticalHighSpeed_AimDisabled".Translate();
            }

            return new Command_Action
            {
                defaultLabel = "HD_TacticalHighSpeed_Command".Translate(),
                defaultDesc = "HD_TacticalHighSpeed_CommandDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Skill/HD_CQB_HighSpeedMovement", false)
                    ?? BaseContent.BadTex,
                Disabled = disabled,
                disabledReason = disabledReason,
                action = () => BeginTargeting(pawn)
            };
        }

        public static bool TryGetIncomingAccuracyMultiplier(
            ShotReport report,
            out float multiplier)
        {
            multiplier = 1f;
            if (TargetField == null)
            {
                return false;
            }

            object rawTarget = TargetField.GetValue(report);
            Pawn pawn = rawTarget as Pawn;
            if (pawn == null && rawTarget is LocalTargetInfo localTarget)
            {
                pawn = localTarget.Thing as Pawn;
            }

            if (pawn == null || !IsActive(pawn))
            {
                return false;
            }

            multiplier = IncomingAccuracyMultiplier;
            return true;
        }

        private static readonly FieldInfo TargetField =
            AccessTools.Field(typeof(ShotReport), "target");

        public static void NotifyPawnTick(Pawn pawn)
        {
            if (pawn != null)
            {
                IsActive(pawn);
            }
        }

        public static void Cancel(Pawn pawn)
        {
            if (pawn != null)
            {
                activeStates.Remove(pawn.thingIDNumber);
            }
        }
    }

}
