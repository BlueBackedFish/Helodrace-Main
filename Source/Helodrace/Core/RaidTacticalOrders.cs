using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    public enum RaidOrderKind { Move, Hold, Fight }

    public sealed class RaidPawnOrder : IExposable
    {
        public Pawn Pawn;
        public string OrganizationId;
        public string UnitId;
        public string GroupId;
        public RaidOrderKind Kind;
        public IntVec3 Destination;
        public bool Sprint;
        public bool FightOnArrival;
        public float Radius = 10f;
        public Rot4 Facing = Rot4.Invalid;
        public int RetryAfter;
        public int Room;
        public IntVec3 LeashCenter;
        public float LeashRadius;
        public bool RefreshPending;
        public bool Reactive;
        public RaidPawnCommand Command = new RaidPawnCommand();
        internal int ResolveAfter;
        internal TacticalMovementPermission PathPermission;
        internal RaidExteriorIngress PathIngress;
        internal int PathRevision = -1;
        internal IntVec3 RejectedStep = IntVec3.Invalid;
        internal int RejectedStepTick = -1;
        public RaidMovementDiagnostics Movement = new RaidMovementDiagnostics();

        internal bool OwnedBy(RaidTacticalUnit unit)
        {
            if (unit == null || UnitId != unit.Id || OrganizationId != unit.OrganizationId) return false;
            CombatGroup group = OrganizationAPI.GetGroup(Pawn);
            return group != null && group.id == GroupId && RaidTacticalUnit.ForGroup(group) == unit
                && group.Members.Contains(Pawn);
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref OrganizationId, "organization");
            Scribe_Values.Look(ref UnitId, "unitId");
            Scribe_Values.Look(ref GroupId, "groupId");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Destination, "destination");
            Scribe_Values.Look(ref Sprint, "sprint");
            Scribe_Values.Look(ref FightOnArrival, "fightOnArrival");
            Scribe_Values.Look(ref Radius, "radius", 10f);
            Scribe_Values.Look(ref Facing, "facing", Rot4.Invalid);
            Scribe_Values.Look(ref RetryAfter, "retryAfter");
            Scribe_Values.Look(ref Room, "room");
            Scribe_Values.Look(ref LeashCenter, "leashCenter");
            Scribe_Values.Look(ref LeashRadius, "leashRadius");
            Scribe_Values.Look(ref Reactive, "reactive");
            Scribe_Deep.Look(ref Command, "command");
        }
    }

    // Orders are durable directives. Vanilla job drivers still perform movement,
    // reservations and shooting; only the duty's job selection is replaced.
    public sealed class MapComponent_RaidTacticalOrders : MapComponent
    {
        private readonly Dictionary<Pawn, RaidPawnOrder> orders = new Dictionary<Pawn, RaidPawnOrder>();
        private readonly TacticalDueQueue<Pawn> reviews = new TacticalDueQueue<Pawn>();
        internal long ReviewCount;
        internal int MaximumReviewDelay;
        private List<RaidPawnOrder> saved;
        internal static readonly JobGiver_RaidTacticalFight Fighter = new JobGiver_RaidTacticalFight();

        public MapComponent_RaidTacticalOrders(Map map) : base(map) { }

        internal void Forget(Pawn pawn) { orders.Remove(pawn); reviews.Remove(pawn); }
        internal static void PreparationReady(Pawn pawn)
        {
            RaidPawnOrder order = For(pawn);
            if (order == null) return;
            if (order.Movement.BlockReason == RaidMoveBlockReason.GridPreparing)
                order.Movement.Block(RaidMoveBlockReason.None, GenTicks.TicksGame);
            if (order.Kind == RaidOrderKind.Hold || !Owned(pawn.CurJob)) return;
            order.RefreshPending = true;
            if (!Protected(pawn) && !pawn.stances.FullBodyBusy)
                pawn.jobs.CheckForJobOverride();
            else order.Movement.Block(Protected(pawn) ? RaidMoveBlockReason.ProtectedJob
                : RaidMoveBlockReason.Busy, GenTicks.TicksGame);
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) saved = orders.Values.ToList();
            Scribe_Collections.Look(ref saved, "raidPawnOrders", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            orders.Clear(); reviews.Clear();
            foreach (RaidPawnOrder order in saved ?? new List<RaidPawnOrder>())
                if (order?.Pawn != null)
                {
                    order.RefreshPending = true;
                    orders[order.Pawn] = order;
                    reviews.Schedule(order.Pawn, GenTicks.TicksGame + (order.Pawn.thingIDNumber & int.MaxValue) % 30);
                }
            saved = null;
        }

        internal static RaidPawnOrder For(Pawn pawn) => pawn?.Spawned == true
            ? pawn.Map.GetComponent<MapComponent_RaidTacticalOrders>()?.Get(pawn) : null;

        // A path belongs to an already-issued tactical Job. Ordinary review and
        // Job selection validate unit control; individual tile steps only need
        // the bound actor and group, not another full unit/member lookup.
        internal RaidPawnOrder MovingOrder(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.Map != map || pawn.Dead || pawn.Downed
                || pawn.Drafted || pawn.InMentalState || !Owned(pawn.CurJob)
                || !orders.TryGetValue(pawn, out RaidPawnOrder order)) return null;
            return OrganizationAPI.GetGroup(pawn)?.id == order.GroupId ? order : null;
        }

        private RaidPawnOrder Get(Pawn pawn)
        {
            if (!orders.TryGetValue(pawn, out RaidPawnOrder order)) return null;
            RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(pawn);
            if (!order.OwnedBy(unit)
                || pawn.Dead || pawn.Downed || pawn.Drafted || pawn.InMentalState
                || map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.ControlsPawn(pawn, unit) != true) return null;
            return order;
        }

        public override void MapComponentTick()
        {
            int tick = GenTicks.TicksGame;
            for (int i = 0; i < 16 && reviews.TryTake(tick, out Pawn pawn, out int deadline); i++)
            {
                ReviewCount++;
                MaximumReviewDelay = System.Math.Max(MaximumReviewDelay, tick - deadline);
                if (!pawn.Spawned || pawn.Map != map || Get(pawn) == null)
                {
                    orders.Remove(pawn);
                    continue;
                }
                RaidPawnOrder order = orders[pawn];
                Resolve(order);
                if (RaidOrderPolicy.RecoverMove(order.Kind == RaidOrderKind.Move, pawn.Position == order.Destination,
                    pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == order.Destination,
                    GenTicks.TicksGame < order.RetryAfter))
                    order.RefreshPending = true;
                if (order.Kind == RaidOrderKind.Fight && Owned(pawn.CurJob)
                    && pawn.CurJobDef == JobDefOf.AttackMelee
                    && !Allowed(pawn, order, pawn.CurJob.targetA.Cell))
                {
                    pawn.mindState.enemyTarget = null;
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptOptional);
                }
                if (RaidOrderPolicy.Refresh(order.RefreshPending, Owned(pawn.CurJob),
                    Protected(pawn), pawn.stances.FullBodyBusy))
                {
                    order.RefreshPending = false;
                    pawn.jobs.CheckForJobOverride();
                }
                reviews.Schedule(pawn, tick + 30);
            }
        }

        internal string PendingMovesReport()
        {
            int tick = GenTicks.TicksGame;
            var pending = orders.Values.Where(order => order.Pawn?.Spawned == true && order.Pawn.Map == map
                && !order.Pawn.Dead && !order.Pawn.Downed && order.Movement.RequestedKind == RaidOrderKind.Move
                && order.Movement.RequestedTick >= 0 && order.Movement.StartedTick < 0
                && order.Pawn.Position != order.Movement.RequestedDestination
                && !(order.Pawn.CurJobDef == JobDefOf.Goto && order.Pawn.CurJob.targetA.Cell == order.Destination)).ToList();
            int age = pending.Select(order => tick - order.Movement.RequestedTick).DefaultIfEmpty(0).Max();
            return $"unstarted moves={pending.Count} max age={age} ticks reasons="
                + string.Join(",", pending.GroupBy(order => order.Movement.BlockReason)
                    .Select(group => group.Key + ":" + group.Count()));
        }

        internal string PendingMovesDetail() => string.Join(";", orders.Values.Where(order => order.Pawn?.Spawned == true
                && order.Pawn.Map == map && !order.Pawn.Dead && !order.Pawn.Downed
                && order.Command.Kind == RaidOrderKind.Move && order.Pawn.Position != order.Command.Destination
                && !(order.Pawn.CurJobDef == JobDefOf.Goto && order.Pawn.CurJob.targetA.Cell == order.Destination))
            .Take(24).Select(order => $"{order.Pawn.thingIDNumber}:{order.Command.Owner}#{order.Command.Revision}"
                + $" pos={order.Pawn.Position} goal={order.Command.Destination} leg={order.Kind}/{order.Destination}"
                + $" job={order.Pawn.CurJobDef?.defName}/{order.Pawn.CurJob?.targetA} block={order.Movement.BlockReason}"
                + $" age={GenTicks.TicksGame - order.Movement.RequestedTick} pathGeneration={order.PathRevision}"
                + $" independent={order.Command.IndependentJoin} rejectedStep={order.RejectedStep}@{order.RejectedStepTick}"
                + $" goalAllowed={(!order.Destination.InBounds(map) ? false : order.PathPermission?.Allows(map.cellIndices.CellToIndex(order.Destination)) ?? true)}"));

        public static bool Set(Pawn pawn, RaidOrderKind kind, IntVec3 destination,
            bool sprint = false, bool fightOnArrival = false, float radius = 10f, bool reactive = false, bool independentJoin = false)
        {
            using (RaidCpuProfiler.Measure(pawn?.Map, RaidCpuStage.Orders))
                return SetMeasured(pawn, kind, destination, sprint, fightOnArrival, radius, reactive, independentJoin);
        }

        private static bool SetMeasured(Pawn pawn, RaidOrderKind kind, IntVec3 destination,
            bool sprint, bool fightOnArrival, float radius, bool reactive, bool independentJoin)
        {
            if (pawn?.Spawned != true) return false;
            var execution = pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>();
            RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(pawn);
            if (execution?.ControlsPawn(pawn, unit) != true) return false;
            var owner = pawn.Map.GetComponent<MapComponent_RaidTacticalOrders>();
            if (!owner.orders.TryGetValue(pawn, out RaidPawnOrder order) || order.UnitId != unit.Id)
            {
                order = new RaidPawnOrder { Pawn = pawn, OrganizationId = unit.OrganizationId,
                    UnitId = unit.Id, GroupId = OrganizationAPI.GetGroup(pawn).id };
                owner.orders[pawn] = order;
                owner.reviews.Schedule(pawn, GenTicks.TicksGame + (pawn.thingIDNumber & int.MaxValue) % 30);
            }
            order.GroupId = OrganizationAPI.GetGroup(pawn).id;
            var state = execution.StateFor(unit.Id);
            RaidCommandOwner commandOwner = reactive ? state?.SharedOpeningWait == true
                ? RaidCommandOwner.OpeningQueue : state != null && state.ContactGuards.Any(guard => guard.Pawn == pawn && guard.Position == destination && guard.Until > GenTicks.TicksGame)
                    ? RaidCommandOwner.Guard : RaidCommandOwner.Reaction
                : state?.Indices.Assignment(state.ActivePlan, pawn)?.Task == RaidTacticalTask.Security
                    ? RaidCommandOwner.Security
                : state?.Phase == RaidExecutionPhase.CrossBreach ? RaidCommandOwner.Breach
                : state?.Phase >= RaidExecutionPhase.Assault ? RaidCommandOwner.Assault
                : RaidCommandOwner.Approach;
            independentJoin |= !reactive && order.Command.IndependentJoin && order.Command.Destination == destination
                && order.Command.Owner == commandOwner;
            RaidMovementNode connection = reactive ? null : independentJoin
                ? order.Command.IndependentJoin && order.Command.Destination == destination ? order.Command.Connection : null
                : MapComponent_RaidTacticalExecution.ConnectionFor(state, pawn);
            bool changed = order.Command.Assign(commandOwner, kind, destination, sprint, fightOnArrival,
                radius, reactive, connection, independentJoin);
            if (changed) order.RefreshPending = true;
            Resolve(order, changed);
            if (RaidOrderPolicy.Refresh(order.RefreshPending, Owned(pawn.CurJob),
                Protected(pawn), pawn.stances.FullBodyBusy))
            {
                order.RefreshPending = false;
                pawn.jobs.CheckForJobOverride();
            }
            return true;
        }

        internal static void Resolve(RaidPawnOrder order, bool force = false)
        {
            int tick = GenTicks.TicksGame;
            if (!force && tick < order.ResolveAfter) return;
            order.ResolveAfter = tick + 30;
            Pawn pawn = order.Pawn;
            RaidPawnCommand command = order.Command;
            if (!command.Destination.IsValid) return;
            RaidOrderKind kind = command.Kind;
            IntVec3 destination = command.Destination;
            bool sprint = command.Sprint, fightOnArrival = command.FightOnArrival, reactive = command.Reactive;
            float radius = command.Radius;
            var execution = pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>();
            RaidContactGuard guard = MapComponent_RaidTacticalExecution.ContactGuardFor(pawn);
            RaidMoveController controller = guard != null && (!reactive || destination == guard.Position)
                ? RaidMoveController.ContactGuard : reactive ? RaidMoveController.Reaction : RaidMoveController.Formation;
            if (guard != null && !reactive)
            {
                kind = pawn.Position == guard.Position ? RaidOrderKind.Hold : RaidOrderKind.Move;
                destination = guard.Position; sprint = false; fightOnArrival = false; radius = 1f; reactive = true;
            }
            if (!reactive && (kind == RaidOrderKind.Move || kind == RaidOrderKind.Fight)
                && execution.RedirectExteriorIngress(pawn, destination, out IntVec3 ingressDestination))
            {
                destination = ingressDestination.IsValid ? ingressDestination : pawn.Position;
                kind = destination != pawn.Position ? RaidOrderKind.Move : RaidOrderKind.Hold;
                fightOnArrival = false; controller = RaidMoveController.ExteriorIngress;
            }
            bool unknownJoin = !reactive && command.IndependentJoin && controller == RaidMoveController.Formation
                && kind != RaidOrderKind.Hold && !execution.ResolvePersonalJoin(order);
            if (unknownJoin) { kind = RaidOrderKind.Hold; destination = pawn.Position; fightOnArrival = false; }
            if (kind == RaidOrderKind.Move && destination == pawn.Position)
                kind = fightOnArrival ? RaidOrderKind.Fight : RaidOrderKind.Hold;
            bool changed = order.Kind != kind || order.Destination != destination || order.Sprint != sprint
                || order.FightOnArrival != fightOnArrival || order.Radius != radius || order.Reactive != reactive;
            order.Movement.Request(command.Kind, command.Destination, controller, tick, changed);
            if (controller == RaidMoveController.ContactGuard)
                order.Movement.Block(RaidMoveBlockReason.ContactGuard, tick);
            else if (unknownJoin) order.Movement.Block(RaidMoveBlockReason.UnknownJoin, tick);
            else if (controller == RaidMoveController.ExteriorIngress && kind == RaidOrderKind.Hold)
                order.Movement.Block(RaidMoveBlockReason.OpeningWait, tick);
            else if (changed) order.Movement.Block(RaidMoveBlockReason.None, tick);
            if (!changed) return;
            order.Kind = kind; order.Destination = destination; order.Sprint = sprint;
            order.FightOnArrival = fightOnArrival; order.Radius = radius; order.Reactive = reactive;
            order.Facing = Rot4.Invalid; order.RetryAfter = 0; order.RefreshPending = true;
            order.Room = pawn.Map.GetComponent<MapComponent_RaidTacticalPlans>()
                ?.GetStructure(order.OrganizationId)?.RoomAt(destination) ?? 0;
            var state = execution.StateFor(order.UnitId);
            order.LeashCenter = state?.ActivePlan?.Start ?? pawn.Position;
            order.LeashRadius = state?.Maneuver == RaidTacticalManeuver.HoldAndCounterattack ? 25f : 0f;
            MapComponent_RaidTacticalTrace.Record(pawn,
                $"command {command.Owner}#{command.Revision}: {kind} to {destination}");
        }
        internal static bool Owned(Job job) => job?.jobGiver is JobGiver_RaidTacticalOrder;

        internal static void Escape(Pawn pawn, IntVec3 destination)
        {
            if (pawn.CurJob?.playerForced == true && pawn.CurJobDef?.defName != "HD_CASStationaryGuidance") return;
            // Install the durable destination before cancelling tool/aim jobs.
            // Selection still runs through the resolved vanilla duty node.
            Set(pawn, pawn.Position == destination ? RaidOrderKind.Hold : RaidOrderKind.Move,
                destination, sprint: true, radius: 1f, reactive: true);
            if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == destination
                || pawn.Position == destination && pawn.CurJobDef == JobDefOf.Wait_Combat) return;
            if (pawn.CurJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            pawn.stances.CancelBusyStanceHard();
            pawn.jobs.CheckForJobOverride();
        }

        internal static void Retreat(Pawn pawn, IntVec3 destination, bool sprint = true)
        {
            if (pawn.CurJob?.playerForced == true) return;
            Set(pawn, RaidOrderKind.Fight, destination, sprint: sprint, radius: 1.5f, reactive: true);
            if (pawn.Position.DistanceToSquared(destination) <= 2.25f
                || pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == destination) return;
            if (pawn.CurJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            pawn.stances.CancelBusyStanceHard();
            pawn.jobs.CheckForJobOverride();
        }

        internal static void Ended(Pawn pawn, JobCondition condition)
        {
            if (!Owned(pawn.CurJob) || pawn.CurJobDef != JobDefOf.Goto
                || (condition != JobCondition.Incompletable && condition != JobCondition.Errored
                    && condition != JobCondition.ErroredPather)) return;
            RaidPawnOrder order = For(pawn);
            if (order != null)
            {
                order.RetryAfter = GenTicks.TicksGame + 120;
                order.Movement.Block(RaidMoveBlockReason.RetryDelay, GenTicks.TicksGame);
            }
        }

        internal static bool Protected(Pawn pawn)
        {
            Job job = pawn.CurJob;
            if (job == null) return false;
            if (job.playerForced || job.jobGiverThinkTree != null
                && job.jobGiverThinkTree == pawn.thinker?.ConstantThinkTree) return true;
            // Preserve equipment, medical and interaction jobs until they finish.
            return job.def != JobDefOf.Goto && job.def != JobDefOf.Wait_Combat
                && job.def != JobDefOf.Wait && !Owned(job)
                && (job.def.defName.StartsWith("HD_") || job.def == JobDefOf.Kidnap
                    || job.def.forceCompleteBeforeNextJob);
        }

        public static void Face(Pawn pawn, Rot4 facing)
        {
            RaidContactGuard guard = MapComponent_RaidTacticalExecution.ContactGuardFor(pawn);
            if (guard != null && guard.Focus != pawn.Position)
                facing = Rot4.FromAngleFlat((guard.Focus - pawn.Position).ToVector3().AngleFlat());
            RaidPawnOrder order = For(pawn);
            if (order == null || order.Kind != RaidOrderKind.Hold) return;
            order.Facing = facing;
            // Aim toward the target while shooting; restore the sector when idle.
            if (!pawn.stances.FullBodyBusy) pawn.rotationTracker.FaceCell(pawn.Position + facing.FacingCell);
        }

        internal static Job Wait(Pawn pawn, RaidPawnOrder order)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            job.expiryInterval = 120;
            job.checkOverrideOnExpire = true;
            return job;
        }

        internal static Job Move(Pawn pawn, RaidPawnOrder order)
        {
            var execution = pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>();
            if (order.Destination == pawn.Position) return Wait(pawn, order);
            if (pawn.Map.GetComponent<MapComponent_RaidMovementAreas>()?.ReadyFor(pawn) == false)
            {
                order.Movement.Block(RaidMoveBlockReason.GridPreparing, GenTicks.TicksGame);
                return Wait(pawn, order);
            }
            // Another job can reserve the temporary endpoint after the steering
            // update. Resolve a free endpoint in the same shared direction now,
            // rather than imposing the 120-tick retry on the rest of the squad.
            if (!pawn.Map.pawnDestinationReservationManager.CanReserve(order.Destination, pawn)
                && pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.TryNodeMoveDestination(pawn, out IntVec3 alternative) == true)
            {
                order.Destination = alternative;
                order.RetryAfter = 0;
                MapComponent_RaidTacticalTrace.Record(pawn, $"Node endpoint reserved: continue toward {alternative}");
            }
            if (GenTicks.TicksGame < order.RetryAfter || !order.Destination.InBounds(pawn.Map)
                || !order.Destination.Standable(pawn.Map)
                || !pawn.Map.pawnDestinationReservationManager.CanReserve(order.Destination, pawn)
                || !pawn.CanReach(order.Destination, PathEndMode.OnCell, Danger.Deadly))
            {
                RaidMoveBlockReason reason = GenTicks.TicksGame < order.RetryAfter ? RaidMoveBlockReason.RetryDelay
                    : !order.Destination.InBounds(pawn.Map) || !order.Destination.Standable(pawn.Map)
                        ? RaidMoveBlockReason.InvalidDestination
                    : !pawn.Map.pawnDestinationReservationManager.CanReserve(order.Destination, pawn)
                        ? RaidMoveBlockReason.DestinationReserved : RaidMoveBlockReason.Unreachable;
                order.Movement.Block(reason, GenTicks.TicksGame);
                if (GenTicks.TicksGame >= order.RetryAfter)
                {
                    order.RetryAfter = GenTicks.TicksGame + 120;
                    MapComponent_RaidTacticalTrace.Record(pawn, $"move deferred to {order.Destination}");
                }
                return Wait(pawn, order);
            }
            Job job = JobMaker.MakeJob(JobDefOf.Goto, order.Destination);
            order.Movement.Block(RaidMoveBlockReason.None, GenTicks.TicksGame);
            job.locomotionUrgency = order.Sprint ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
            return job;
        }

        internal static bool Allowed(Pawn pawn, RaidPawnOrder order, IntVec3 cell)
        {
            if (order == null || !cell.InBounds(pawn.Map) || cell.DistanceToSquared(order.Destination)
                > order.Radius * order.Radius || !TargetInLeash(order, cell)) return false;
            if (order.Room == 0 && !order.Reactive) return true;
            return pawn.Map.GetComponent<MapComponent_RaidTacticalPlans>()
                ?.GetStructure(order.OrganizationId)?.RoomAt(cell) == order.Room;
        }

        internal static bool TargetInLeash(RaidPawnOrder order, IntVec3 cell) => order != null
            && (order.LeashRadius <= 0f || cell.DistanceToSquared(order.LeashCenter)
                <= order.LeashRadius * order.LeashRadius);
    }

    [HarmonyPatch(typeof(JobGiver_FleePotentialExplosion), "TryGiveJob")]
    public static class Patch_RaidTacticalSupportFlee
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null || pawn?.Spawned != true) return;
            if (pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()?.IgnoreOwnScreeningSmoke(pawn) == true)
            {
                __result = null;
                return;
            }
            if (pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.TryEmergencyFleeDestination(pawn, out IntVec3 escape) == true)
            {
                if (escape == pawn.Position) __result = null;
                else __result.targetA = escape;
                return;
            }
            if (pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.TrySupportFleeDestination(pawn, out IntVec3 cell) != true) return;
            if (cell == pawn.Position) __result = null;
            else __result.targetA = cell;
        }
    }

    public sealed class JobGiver_RaidTacticalOrder : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
            if (order == null) return null;
            MapComponent_RaidTacticalOrders.Resolve(order, pawn.Position == order.Destination
                && order.Command.Kind == RaidOrderKind.Move && order.Command.Destination != order.Destination);
            if (MapComponent_RaidTacticalOrders.Protected(pawn) || pawn.stances.FullBodyBusy)
            {
                order.Movement.Block(MapComponent_RaidTacticalOrders.Protected(pawn)
                    ? RaidMoveBlockReason.ProtectedJob : RaidMoveBlockReason.Busy, GenTicks.TicksGame);
                return pawn.CurJob;
            }
            if (order.Kind == RaidOrderKind.Fight)
                return MapComponent_RaidTacticalOrders.Fighter.Give(pawn);
            if (order.Kind == RaidOrderKind.Move && pawn.Position != order.Destination)
            {
                if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == order.Destination
                    && order.PathRevision == order.Command.Revision)
                    return pawn.CurJob;
                return MapComponent_RaidTacticalOrders.Move(pawn, order);
            }
            if (order.Kind == RaidOrderKind.Move)
            {
                order.Kind = order.FightOnArrival ? RaidOrderKind.Fight : RaidOrderKind.Hold;
                MapComponent_RaidTacticalTrace.Record(pawn, $"arrival -> {order.Kind}");
                if (order.Kind == RaidOrderKind.Fight)
                    return MapComponent_RaidTacticalOrders.Fighter.Give(pawn);
            }
            return MapComponent_RaidTacticalOrders.Wait(pawn, order);
        }
    }

    public sealed class JobGiver_RaidTacticalFight : JobGiver_AIFightEnemy
    {
        internal Job Give(Pawn pawn)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
            if (pawn.Map.GetComponent<MapComponent_RaidMovementAreas>()?.ReadyFor(pawn) == false)
                return MapComponent_RaidTacticalOrders.Wait(pawn, order);
            if (!MapComponent_RaidTacticalOrders.Allowed(pawn, order, pawn.Position))
                return MapComponent_RaidTacticalOrders.Move(pawn, order);
            return base.TryGiveJob(pawn) ?? MapComponent_RaidTacticalOrders.Wait(pawn, order);
        }

        protected override bool ExtraTargetValidator(Pawn pawn, Thing target) =>
            target is Pawn && base.ExtraTargetValidator(pawn, target)
            && GenSight.LineOfSight(pawn.Position, target.Position, pawn.Map, true)
            && MapComponent_RaidTacticalOrders.TargetInLeash(MapComponent_RaidTacticalOrders.For(pawn), target.Position)
            && (pawn.TryGetAttackVerb(target)?.IsMeleeAttack == false
                || MapComponent_RaidTacticalOrders.Allowed(pawn,
                    MapComponent_RaidTacticalOrders.For(pawn), target.Position));

        protected override bool ShouldLoseTarget(Pawn pawn) => base.ShouldLoseTarget(pawn)
            || !ExtraTargetValidator(pawn, pawn.mindState.enemyTarget);

        protected override bool TryFindShootingPosition(Pawn pawn, out IntVec3 dest, Verb verbToUse = null)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
            Verb verb = verbToUse ?? pawn.TryGetAttackVerb(pawn.mindState.enemyTarget, true);
            if (verb == null) { dest = IntVec3.Invalid; return false; }
            // Validator avoids the vanilla locus search-rectangle bug and enforces
            // the frozen room boundary as well as the broad activity radius.
            return CastPositionFinder.TryFindCastPosition(new CastPositionRequest
            {
                caster = pawn, target = pawn.mindState.enemyTarget, verb = verb,
                maxRangeFromCaster = 12f, maxRangeFromTarget = verb.EffectiveRange,
                wantCoverFromTarget = verb.EffectiveRange > 5f,
                validator = cell => MapComponent_RaidTacticalOrders.Allowed(pawn, order, cell)
            }, out dest);
        }
    }

    [HarmonyPatch(typeof(ThinkNode_Duty), nameof(ThinkNode_Duty.TryIssueJobPackage))]
    public static class Patch_RaidTacticalDuty
    {
        public static bool Prefix(ThinkNode_Duty __instance, Pawn pawn,
            JobIssueParams jobParams, ref ThinkResult __result)
        {
            if (MapComponent_RaidTacticalOrders.For(pawn) == null) return true;
            DutyDef duty = DefDatabase<DutyDef>.GetNamed("HD_RaidTacticalControl");
            // Use the actual resolved node in the pawn's think tree so the vanilla
            // job giver save key resolves correctly after loading.
            __result = __instance.subNodes[duty.index].TryIssueJobPackage(pawn, jobParams);
            if (__result.Job != null) __result.Job.lord = pawn.GetLord();
            return false;
        }
    }

    // JobDriver's default continuation accepts any destination. For directives,
    // a new destination must actually replace the old Goto, without resetting
    // an unchanged path every coordinator tick.
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.IsContinuation))]
    public static class Patch_RaidTacticalContinuation
    {
        public static void Postfix(JobDriver __instance, Job j, ref bool __result)
        {
            Job current = __instance.pawn.CurJob;
            if (MapComponent_RaidTacticalOrders.Owned(current) && current.def == JobDefOf.Goto)
            {
                RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(__instance.pawn);
                __result = __result && order != null && order.PathRevision == order.Command.Revision
                    && RaidOrderPolicy.ContinueMove(current.targetA == j.targetA,
                        current.locomotionUrgency == j.locomotionUrgency);
            }
        }
    }

    [HarmonyPatch(typeof(ThinkNode_DutyConstant), nameof(ThinkNode_DutyConstant.TryIssueJobPackage))]
    public static class Patch_RaidTacticalDutyConstant
    {
        public static bool Prefix(Pawn pawn, ref ThinkResult __result)
        {
            if (MapComponent_RaidTacticalOrders.For(pawn) == null) return true;
            __result = ThinkResult.NoJob;
            return false;
        }
    }

    [HarmonyPatch(typeof(JobDriver_Wait), nameof(JobDriver_Wait.DecorateWaitToil))]
    public static class Patch_RaidTacticalHoldFacing
    {
        public static void Postfix(JobDriver_Wait __instance, Toil wait)
        {
            Pawn pawn = __instance.pawn;
            if (!MapComponent_RaidTacticalOrders.Owned(pawn.CurJob)) return;
            wait.handlingFacing = false;
            wait.AddPreTickAction(() =>
            {
                RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(pawn);
                if (order?.Kind == RaidOrderKind.Hold && order.Facing.IsValid
                    && !pawn.stances.FullBodyBusy)
                    pawn.rotationTracker.FaceCell(pawn.Position + order.Facing.FacingCell);
            });
        }
    }

    [HarmonyPatch(typeof(JobDriver_Wait), "CheckForAutoAttack")]
    public static class Patch_RaidTacticalPendingAttack
    {
        public static bool Prefix(JobDriver_Wait __instance)
        {
            RaidPawnOrder order = MapComponent_RaidTacticalOrders.For(__instance.pawn);
            // Let the current shot/burst finish, but don't immediately begin
            // another warmup that would starve a pending movement directive.
            return order == null || RaidOrderPolicy.AutoAttack(
                MapComponent_RaidTacticalOrders.Owned(__instance.pawn.CurJob),
                order.RefreshPending, order.Kind == RaidOrderKind.Hold);
        }
    }
}
