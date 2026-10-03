using System.Collections.Generic;
using System;
using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactical;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class RaidEntryObservation : IExposable
    {
        public const string JobDefName = "HD_RaidObserveOpening";
        public Pawn Observer;
        public IntVec3 Position = IntVec3.Invalid;
        public IntVec3 Source = IntVec3.Invalid;
        public int ObservedTicks;
        public bool Complete;
        public bool Unavailable;
        public List<IntVec3> VisibleCells = new List<IntVec3>();
        public List<IntVec3> EnemyCells = new List<IntVec3>();

        public void ExposeData()
        {
            Scribe_References.Look(ref Observer, "observer");
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
            Scribe_Values.Look(ref Source, "source", IntVec3.Invalid);
            Scribe_Values.Look(ref ObservedTicks, "observedTicks");
            Scribe_Values.Look(ref Complete, "complete");
            Scribe_Values.Look(ref Unavailable, "unavailable");
            Scribe_Collections.Look(ref VisibleCells, "visibleCells", LookMode.Value);
            Scribe_Collections.Look(ref EnemyCells, "enemyCells", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (VisibleCells == null) VisibleCells = new List<IntVec3>();
                if (EnemyCells == null) EnemyCells = new List<IntVec3>();
            }
        }

        internal static JobDriver_RaidObserveOpening Active(Pawn pawn) => pawn?.CurJobDef?.defName == JobDefName
            && pawn.jobs.curDriver is JobDriver_RaidObserveOpening driver && !driver.ended
            && driver.job == pawn.CurJob ? driver : null;

        internal static IEnumerable<IntVec3> SidePositions(IntVec3 breach, IntVec3 inside)
        {
            IntVec3 inward = inside - breach, side = new IntVec3(-inward.z, 0, inward.x);
            yield return breach - inward + side;
            yield return breach - inward - side;
        }

        internal static IEnumerable<IntVec3> ThrowTargets(RaidEntryObservation observation,
            IEnumerable<IntVec3> candidates, Func<IntVec3, bool> valid)
        {
            var seen = new HashSet<IntVec3>(observation?.VisibleCells ?? new List<IntVec3>());
            return (observation?.EnemyCells ?? new List<IntVec3>()).Where(valid)
                .Concat(candidates.Where(cell => valid(cell) && !seen.Contains(cell))).Distinct();
        }
    }

    public sealed class JobDriver_RaidObserveOpening : JobDriver
    {
        private string organizationId;
        public bool Peeking;
        public IntVec3 LeanSource => job.targetC.Cell;
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref organizationId, "organizationId");
            Scribe_Values.Look(ref Peeking, "peeking");
        }

        private MapComponent_RaidTacticalExecution.ExecutionState Owner => organizationId == null ? null
            : pawn.Map?.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(organizationId);

        internal bool OwnerStillValid() => Owner?.ActivePlan?.PlannedTick == job.count
            && Owner.Observation?.Observer == pawn
            && (Owner.Phase == RaidExecutionPhase.Breach || Owner.Phase == RaidExecutionPhase.ObserveOpening);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Dead || pawn.Downed || pawn.Faction == RimWorld.Faction.OfPlayer
                || organizationId != null && !OwnerStillValid());
            this.FailOn(() => !job.targetA.Cell.InBounds(pawn.Map) || !job.targetA.Cell.Standable(pawn.Map));
            yield return new Toil { initAction = () => {
                organizationId = OrganizationAPI.GetOrganization(pawn)?.id;
                TacticalAimUtility.Cancel(pawn);
                foreach (Verb verb in pawn.equipment?.AllEquipmentVerbs ?? new List<Verb>()) verb.Reset();
                pawn.stances.CancelBusyStanceHard();
            }, defaultCompleteMode = ToilCompleteMode.Instant };
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil observe = Toils_General.Wait(1, TargetIndex.B);
            observe.defaultCompleteMode = ToilCompleteMode.Never;
            observe.tickAction = () => {
                var state = Owner;
                if (state?.Observation == null) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.rotationTracker.FaceCell(job.targetB.Cell);
                // A reusable friendly door can be opened from the adjacent side;
                // an enemy locked door still requires the engineer's breach.
                if (state.Phase == RaidExecutionPhase.ObserveOpening && state.ActivePlan.ReusePassage
                    && state.ActivePlan.BreachCell.GetEdifice(pawn.Map) is Building_Door door
                    && door.PawnCanOpen(pawn) && pawn.Position.DistanceToSquared(door.Position) <= 2
                    && (!door.Open || state.Observation.ObservedTicks % 30 == 0))
                    door.StartManualOpenBy(pawn);
                Peeking = state.Phase == RaidExecutionPhase.ObserveOpening
                    && GenSight.LineOfSight(LeanSource, job.targetB.Cell, pawn.Map, true);
                if (!Peeking) return;
                RaidEntryObservation observation = state.Observation;
                if (observation.ObservedTicks % 30 == 0)
                    pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>().ObserveInterior(pawn, state);
                observation.ObservedTicks++;
                if (observation.ObservedTicks >= RaidEntryObservationPolicy.ObservationTicks)
                {
                    observation.Complete = true;
                    Peeking = false;
                    ReadyForNextToil();
                }
            };
            yield return observe;
        }
    }

    // Use the game's existing smoothed half-cell lean, without moving into the opening.
    [HarmonyPatch(typeof(PawnLeaner), nameof(PawnLeaner.ShouldLean))]
    public static class Patch_RaidOpeningObservation_Lean
    {
        public static void Postfix(Pawn ___pawn, ref IntVec3 ___shootSourceOffset, ref bool __result)
        {
            var driver = RaidEntryObservation.Active(___pawn);
            if (driver?.Peeking != true) return;
            ___shootSourceOffset = driver.LeanSource - ___pawn.Position;
            __result = true;
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private void EnsureOpeningObserver(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (!plan.BreachCell.IsValid) return;
            if (state.Observation?.Complete == true || state.Observation?.Unavailable == true) return;
            if (state.Observation?.Observer != null && members.Contains(state.Observation.Observer)
                && RaidEntryObservation.Active(state.Observation.Observer) != null) return;
            if (state.Observation == null) state.Observation = new RaidEntryObservation();
            // Selection is bounded to two positions beside the opening, never its centerline.
            IntVec3 inward = plan.BreachInside - plan.BreachCell;
            IntVec3 source = plan.BreachCell - inward;
            foreach (IntVec3 position in RaidEntryObservation.SidePositions(plan.BreachCell, plan.BreachInside))
            {
                if (!position.InBounds(map) || !source.InBounds(map) || !position.Standable(map)
                    || !source.Standable(map) || plan.AvoidedTrapCells.Contains(position)
                    || !(position + inward).InBounds(map) || (position + inward).CanBeSeenOver(map)) continue;
                RaidStructureSnapshot structure = StructureFor(map, plan);
                if (structure == null || structure.RoomAt(position) != structure.RoomAt(plan.Entry)) continue;
                foreach (Pawn pawn in members.Where(value => value != state.Breacher
                    && structure.RoomAt(value.Position) == structure.RoomAt(plan.Entry)
                    && !IsTaserOperation(value) && !MapComponent_RaidTacticalOrders.Protected(value))
                    .OrderBy(value => value.Position.DistanceToSquared(position)))
                {
                    if (members.Any(other => other != pawn && other.Position == position)
                        || plan.Assignments.Any(value => value.Pawn != pawn && members.Contains(value.Pawn)
                            && value.Position == position)
                        || !pawn.CanReserveAndReach(position, PathEndMode.OnCell, Danger.Deadly)) continue;
                    JobDef definition = DefDatabase<JobDef>.GetNamedSilentFail(RaidEntryObservation.JobDefName);
                    if (definition == null) { state.Observation.Unavailable = true; return; }
                    state.Observation.Observer = pawn;
                    state.Observation.Position = position;
                    state.Observation.Source = source;
                    state.Observation.ObservedTicks = 0;
                    state.Observation.VisibleCells.Clear();
                    state.Observation.EnemyCells.Clear();
                    Job job = JobMaker.MakeJob(definition, position, plan.BreachInside, source);
                    job.count = plan.PlannedTick;
                    job.canUseRangedWeapon = false;
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    if (pawn.CurJob == job)
                    {
                        MapComponent_RaidTacticalTrace.Record(pawn, "Opening observer: move beside engineer, then peek for 1.5 seconds");
                        return;
                    }
                }
            }
            state.Observation.Unavailable = true;
            state.SupportStatus = "Observation skipped: no reachable covered side position";
        }

        private void UpdateOpeningObservation(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (!plan.BreachCell.IsValid) { Advance(state, RaidExecutionPhase.Support, tick); return; }
            if (!BreachOpened(plan))
            {
                CancelOpeningObservation(state);
                state.Observation = null;
                Advance(state, RaidExecutionPhase.Breach, tick);
                return;
            }
            EnsureOpeningObserver(members, plan, state, tick);
            MaintainStack(members, plan);
            if (state.Observation?.Complete == true || state.Observation?.Unavailable == true
                || tick - state.PhaseStarted >= 300)
            {
                CancelOpeningObservation(state);
                state.SupportStatus = state.Observation?.Complete == true
                    ? $"Opening observed: {state.Observation.EnemyCells.Count} enemy positions"
                    : "Observation unavailable/interrupted; retain unknown interior sectors";
                Advance(state, RaidExecutionPhase.Support, tick);
            }
        }

        private static void CancelOpeningObservation(ExecutionState state)
        {
            Pawn observer = state.Observation?.Observer;
            if (RaidEntryObservation.Active(observer) != null)
                observer.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        internal void ObserveInterior(Pawn observer, ExecutionState state)
        {
            RaidTacticalPlan plan = state.ActivePlan;
            RaidStructureSnapshot structure = StructureFor(map, plan);
            if (structure == null || state.Observation == null) return;
            int room = structure.RoomAt(plan.BreachInside);
            IntVec3 source = state.Observation.Source, inward = plan.BreachInside - plan.BreachCell;
            bool InTarget(IntVec3 cell) => cell.InBounds(map) && structure.RoomAt(cell) == room
                && (cell.x - plan.BreachCell.x) * inward.x + (cell.z - plan.BreachCell.z) * inward.z > 0
                && source.DistanceToSquared(cell) <= 196;
            var visible = new HashSet<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(source, 14f, true))
                if (InTarget(cell) && GenSight.LineOfSight(source, cell, map, true)) visible.Add(cell);
            state.Observation.VisibleCells = visible.ToList();
            foreach (Pawn enemy in map.mapPawns.AllPawnsSpawned)
                if (!enemy.Dead && !enemy.Downed && enemy.HostileTo(observer) && InTarget(enemy.Position)
                    && GenSight.LineOfSight(source, enemy.Position, map, true)
                    && !state.Observation.EnemyCells.Contains(enemy.Position))
                    state.Observation.EnemyCells.Add(enemy.Position);
        }
    }
}
