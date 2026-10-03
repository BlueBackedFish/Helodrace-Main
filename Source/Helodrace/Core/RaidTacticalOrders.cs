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

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref OrganizationId, "organization");
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
        }
    }

    // Orders are durable directives. Vanilla job drivers still perform movement,
    // reservations and shooting; only the duty's job selection is replaced.
    public sealed class MapComponent_RaidTacticalOrders : MapComponent
    {
        private readonly Dictionary<Pawn, RaidPawnOrder> orders = new Dictionary<Pawn, RaidPawnOrder>();
        private List<RaidPawnOrder> saved;
        internal static readonly JobGiver_RaidTacticalFight Fighter = new JobGiver_RaidTacticalFight();

        public MapComponent_RaidTacticalOrders(Map map) : base(map) { }

        internal void Forget(Pawn pawn) => orders.Remove(pawn);

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) saved = orders.Values.ToList();
            Scribe_Collections.Look(ref saved, "raidPawnOrders", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            orders.Clear();
            foreach (RaidPawnOrder order in saved ?? new List<RaidPawnOrder>())
                if (order?.Pawn != null)
                {
                    order.RefreshPending = true;
                    orders[order.Pawn] = order;
                }
            saved = null;
        }

        internal static RaidPawnOrder For(Pawn pawn) => pawn?.Spawned == true
            ? pawn.Map.GetComponent<MapComponent_RaidTacticalOrders>()?.Get(pawn) : null;

        private RaidPawnOrder Get(Pawn pawn)
        {
            if (!orders.TryGetValue(pawn, out RaidPawnOrder order)) return null;
            if (pawn.Dead || pawn.Downed || pawn.Drafted || pawn.InMentalState
                || OrganizationAPI.GetOrganization(pawn)?.id != order.OrganizationId
                || map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.ControlsPawn(pawn) != true) return null;
            return order;
        }

        public override void MapComponentTick()
        {
            if (GenTicks.TicksGame % 30 != 0) return;
            foreach (Pawn pawn in orders.Keys.ToList())
            {
                if (!pawn.Spawned || pawn.Map != map || Get(pawn) == null)
                {
                    orders.Remove(pawn);
                    continue;
                }
                RaidPawnOrder order = orders[pawn];
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
            }
        }

        public static bool Set(Pawn pawn, RaidOrderKind kind, IntVec3 destination,
            bool sprint = false, bool fightOnArrival = false, float radius = 10f, bool reactive = false)
        {
            if (pawn?.Spawned != true || pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.ControlsPawn(pawn) != true) return false;
            var owner = pawn.Map.GetComponent<MapComponent_RaidTacticalOrders>();
            if (!owner.orders.TryGetValue(pawn, out RaidPawnOrder order))
            {
                order = new RaidPawnOrder { Pawn = pawn,
                    OrganizationId = OrganizationAPI.GetOrganization(pawn).id };
                owner.orders[pawn] = order;
            }
            bool changed = order.Kind != kind || order.Destination != destination
                || order.Sprint != sprint || order.FightOnArrival != fightOnArrival
                || order.Radius != radius || order.Reactive != reactive;
            if (changed)
            {
                order.Kind = kind;
                order.Destination = destination;
                order.Sprint = sprint;
                order.FightOnArrival = fightOnArrival;
                order.Radius = radius;
                order.Reactive = reactive;
                order.Facing = Rot4.Invalid;
                order.RetryAfter = 0;
                order.RefreshPending = true;
                order.Room = pawn.Map.GetComponent<MapComponent_RaidTacticalPlans>()
                    ?.GetStructure(order.OrganizationId)?.RoomAt(destination) ?? 0;
                var state = pawn.Map.GetComponent<MapComponent_RaidTacticalExecution>().StateFor(order.OrganizationId);
                order.LeashCenter = state.ActivePlan.Start;
                order.LeashRadius = state.Maneuver == RaidTacticalManeuver.HoldAndCounterattack ? 25f : 0f;
                MapComponent_RaidTacticalTrace.Record(pawn,
                    $"directive {kind} to {destination} fightOnArrival={fightOnArrival}");
            }
            // Constant-tree emergency jobs and equipment actions finish first.
            // Returning from them goes through the same directive, not AssaultColony.
            if (RaidOrderPolicy.Refresh(order.RefreshPending, Owned(pawn.CurJob),
                Protected(pawn), pawn.stances.FullBodyBusy))
            {
                order.RefreshPending = false;
                pawn.jobs.CheckForJobOverride();
            }
            return true;
        }

        internal static bool Owned(Job job) => job?.jobGiver is JobGiver_RaidTacticalOrder;

        internal static void Escape(Pawn pawn, IntVec3 destination)
        {
            if (pawn.CurJob?.playerForced == true && pawn.CurJobDef?.defName != "HD_CASStationaryGuidance") return;
            // Install the durable destination before cancelling tool/aim jobs.
            // Selection still runs through the resolved vanilla duty node.
            Set(pawn, RaidOrderKind.Move, destination, sprint: true, radius: 1f, reactive: true);
            if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == destination
                || pawn.Position == destination && !pawn.stances.FullBodyBusy
                    && pawn.CurJobDef == JobDefOf.Wait_Combat) return;
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            pawn.stances.CancelBusyStanceHard();
            pawn.jobs.CheckForJobOverride();
        }

        internal static void Ended(Pawn pawn, JobCondition condition)
        {
            if (!Owned(pawn.CurJob) || pawn.CurJobDef != JobDefOf.Goto
                || (condition != JobCondition.Incompletable && condition != JobCondition.Errored
                    && condition != JobCondition.ErroredPather)) return;
            RaidPawnOrder order = For(pawn);
            if (order != null) order.RetryAfter = GenTicks.TicksGame + 120;
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
            if (GenTicks.TicksGame < order.RetryAfter || !order.Destination.InBounds(pawn.Map)
                || !order.Destination.Standable(pawn.Map)
                || !pawn.Map.pawnDestinationReservationManager.CanReserve(order.Destination, pawn)
                || !pawn.CanReach(order.Destination, PathEndMode.OnCell, Danger.Deadly))
            {
                if (GenTicks.TicksGame >= order.RetryAfter)
                {
                    order.RetryAfter = GenTicks.TicksGame + 120;
                    MapComponent_RaidTacticalTrace.Record(pawn, $"move deferred to {order.Destination}");
                }
                return Wait(pawn, order);
            }
            Job job = JobMaker.MakeJob(JobDefOf.Goto, order.Destination);
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
            if (MapComponent_RaidTacticalOrders.Protected(pawn) || pawn.stances.FullBodyBusy)
                return pawn.CurJob;
            if (order.Kind == RaidOrderKind.Fight)
                return MapComponent_RaidTacticalOrders.Fighter.Give(pawn);
            if (order.Kind == RaidOrderKind.Move && pawn.Position != order.Destination)
            {
                if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == order.Destination)
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
                __result = __result && RaidOrderPolicy.ContinueMove(current.targetA == j.targetA,
                    current.locomotionUrgency == j.locomotionUrgency);
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
