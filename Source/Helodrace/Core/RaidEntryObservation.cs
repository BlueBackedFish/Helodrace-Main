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
        public bool ReturnComplete;
        public bool Unavailable;
        public List<IntVec3> VisibleCells = new List<IntVec3>();
        public IntVec3 EnemyCell = IntVec3.Invalid;
        public int EnemyId;
        public bool HasEnemyContact => EnemyCell.IsValid;

        public void ExposeData()
        {
            Scribe_References.Look(ref Observer, "observer");
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
            Scribe_Values.Look(ref Source, "source", IntVec3.Invalid);
            Scribe_Values.Look(ref ObservedTicks, "observedTicks");
            Scribe_Values.Look(ref Complete, "complete");
            Scribe_Values.Look(ref ReturnComplete, "returnComplete");
            Scribe_Values.Look(ref Unavailable, "unavailable");
            Scribe_Collections.Look(ref VisibleCells, "visibleCells", LookMode.Value);
            Scribe_Values.Look(ref EnemyCell, "enemyCell", IntVec3.Invalid);
            Scribe_Values.Look(ref EnemyId, "enemyId");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (VisibleCells == null) VisibleCells = new List<IntVec3>();
            }
        }

        internal bool RecordEnemy(IntVec3 position)
        {
            if (!position.IsValid || HasEnemyContact) return false;
            EnemyCell = position;
            VisibleCells.Clear();
            Complete = true;
            return true;
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

        internal static bool IncludesRoomCell(IntVec3 cell, int room, bool doorway,
            Func<IntVec3, int> roomAt)
        {
            // Door rooms have their own ID. Associate only this doorway with its
            // directly adjoining rooms, without looking into the next room.
            if (!doorway) return roomAt(cell) == room;
            return GenAdj.CardinalDirections.Any(direction => roomAt(cell + direction) == room);
        }

        internal static bool OnObservedSide(IntVec3 cell, IntVec3 breach, IntVec3 inside, bool doorway)
        {
            IntVec3 inward = inside - breach;
            int depth = (cell.x - breach.x) * inward.x + (cell.z - breach.z) * inward.z;
            return depth > 0 || doorway && depth == 0;
        }

        internal static bool EntryThrowTarget(RaidTacticalPlan plan, IntVec3 target) => target.IsValid
            && (!plan.BreachCell.IsValid || target.DistanceToSquared(plan.BreachCell) >= 4
                && OnObservedSide(target, plan.BreachCell, plan.BreachInside, false));

        internal static IEnumerable<IntVec3> ThrowTargets(RaidEntryObservation observation,
            IEnumerable<IntVec3> candidates, Func<IntVec3, bool> valid)
        {
            var seen = new HashSet<IntVec3>(observation?.VisibleCells ?? new List<IntVec3>());
            if (observation?.HasEnemyContact == true)
                return valid(observation.EnemyCell) ? new[] { observation.EnemyCell } : Enumerable.Empty<IntVec3>();
            return candidates.Where(cell => valid(cell) && !seen.Contains(cell)).Distinct();
        }
    }

    public sealed class JobDriver_RaidObserveOpening : JobDriver
    {
        private string unitId;
        private IntVec3 returnPosition = IntVec3.Invalid;
        public bool Peeking;
        public IntVec3 LeanSource => job.targetC.Cell;
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref unitId, "unitId");
            Scribe_Values.Look(ref Peeking, "peeking");
            Scribe_Values.Look(ref returnPosition, "returnPosition", IntVec3.Invalid);
        }

        private MapComponent_RaidTacticalExecution.ExecutionState Owner => unitId == null ? null
            : pawn.Map?.GetComponent<MapComponent_RaidTacticalExecution>()?.StateFor(unitId);

        internal bool OwnerStillValid() => RaidTacticalUnit.ForPawn(pawn)?.Id == unitId
            && Owner?.ActivePlan?.PlannedTick == job.count
            && Owner.Observation?.Observer == pawn
            && (Owner.Phase == RaidExecutionPhase.Breach || Owner.Phase == RaidExecutionPhase.ObserveOpening);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Dead || pawn.Downed || pawn.Faction == RimWorld.Faction.OfPlayer
                || unitId != null && !OwnerStillValid());
            this.FailOn(() => !job.targetA.Cell.InBounds(pawn.Map) || !job.targetA.Cell.Standable(pawn.Map));
            yield return new Toil { initAction = () => {
                unitId = RaidTacticalUnit.ForPawn(pawn)?.Id;
                returnPosition = Owner?.ActivePlan?.Assignments.FirstOrDefault(assignment => assignment.Pawn == pawn)
                    ?.Position ?? pawn.Position;
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
                pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>().ObserveInterior(pawn, state);
                observation.ObservedTicks++;
                if (observation.Complete || observation.ObservedTicks >= RaidEntryObservationPolicy.ObservationTicks)
                {
                    observation.Complete = true;
                    Peeking = false;
                    ReadyForNextToil();
                }
            };
            yield return observe;
            yield return new Toil { initAction = () => {
                Peeking = false;
                job.SetTarget(TargetIndex.A, returnPosition);
                job.locomotionUrgency = LocomotionUrgency.Sprint;
                MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Move, returnPosition, sprint: true);
            }, defaultCompleteMode = ToilCompleteMode.Instant };
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return new Toil { initAction = () => {
                if (Owner?.Observation != null) Owner.Observation.ReturnComplete = true;
            }, defaultCompleteMode = ToilCompleteMode.Instant };
        }
    }

    // Use the game's existing smoothed half-cell lean, without moving into the opening.
    [LegacyTactical]
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
                    state.Observation.ReturnComplete = false;
                    state.Observation.VisibleCells.Clear();
                    state.Observation.EnemyCell = IntVec3.Invalid;
                    state.Observation.EnemyId = 0;
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
            MaintainStack(members, plan, state.Observation?.Complete == true && !state.Observation.ReturnComplete
                ? state.Observation.Observer : null);
            if (state.Observation?.Complete == true && !state.Observation.ReturnComplete
                && RaidEntryObservation.Active(state.Observation.Observer) == null)
            {
                Pawn observer = state.Observation.Observer;
                RaidTacticalAssignment slot = plan.Assignments.FirstOrDefault(value => value.Pawn == observer);
                if (!members.Contains(observer) || slot == null || observer.Position == slot.Position)
                    state.Observation.ReturnComplete = true;
                else TryGoto(observer, slot.Position, sprint: true);
            }
            if (state.Observation?.Complete == true && state.Observation.ReturnComplete || state.Observation?.Unavailable == true
                || tick - state.PhaseStarted >= 300)
            {
                CancelOpeningObservation(state);
                state.SupportStatus = state.Observation?.Complete == true
                    ? state.Observation.HasEnemyContact ? $"Enemy spotted at {state.Observation.EnemyCell}; withdraw and throw at contact"
                        : "Opening observed: no enemy contact"
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
            if (structure == null || state.Observation == null || state.Observation.HasEnemyContact) return;
            int room = structure.RoomAt(plan.BreachInside);
            IntVec3 source = state.Observation.Source;
            bool InTarget(IntVec3 cell) => OpeningRoomContains(map, structure, cell, room)
                && RaidEntryObservation.OnObservedSide(cell, plan.BreachCell, plan.BreachInside,
                    IsOpeningDoorCell(map, structure, cell))
                && source.DistanceToSquared(cell) <= 196;
            // Stop at the first real sighting before collecting any more room information.
            foreach (Pawn enemy in map.mapPawns.AllPawnsSpawned)
                if (!enemy.Dead && !enemy.Downed && enemy.HostileTo(observer) && InTarget(enemy.Position)
                    && CanObserveContact(observer, source, enemy, 14))
                {
                    RecordContact(observer, enemy, state, structure, GenTicks.TicksGame);
                    state.Observation.RecordEnemy(enemy.Position);
                    state.Observation.EnemyId = enemy.thingIDNumber;
                    MapComponent_RaidTacticalTrace.Record(observer, $"Opening contact at {enemy.Position}; end peek and withdraw immediately");
                    return;
                }
            if (state.Observation.ObservedTicks % 30 != 0) return;
            var visible = new HashSet<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(source, 14f, true))
                if (InTarget(cell) && GenSight.LineOfSight(source, cell, map, true)) visible.Add(cell);
            state.Observation.VisibleCells = visible.ToList();
        }

        private static bool IsOpeningDoorCell(Map map, RaidStructureSnapshot structure, IntVec3 cell) =>
            cell.InBounds(map) && (cell.GetEdifice(map) is Building_Door
                || (structure.CachedAt(cell).Structures & TacticalStructureKind.Door) != 0);

        private static bool OpeningRoomContains(Map map, RaidStructureSnapshot structure, IntVec3 cell, int room) =>
            structure != null && cell.InBounds(map) && RaidEntryObservation.IncludesRoomCell(cell, room,
                IsOpeningDoorCell(map, structure, cell),
                adjacent => adjacent.InBounds(map) ? structure.RoomAt(adjacent) : -1);
    }
}
