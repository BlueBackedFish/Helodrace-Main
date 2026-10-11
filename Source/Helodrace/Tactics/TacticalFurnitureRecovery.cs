using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace.Tactics
{
    public static class TacticalFurniturePolicy
    {
        public const int MaximumWorkTicks = 600, SimulationsPerAdvance = 2;
        public const string JobName = "HD_NewTacticalClearFurniture";

        public static int WorkTicks(int hitPoints, float damage, int interval) =>
            hitPoints <= 0 || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage) || interval <= 0 ? int.MaxValue
                : (int)Math.Min(int.MaxValue, Math.Ceiling(hitPoints / (double)damage) * interval);

        public static bool IsFurniture(Building building) => building != null
            && building.def.destroyable && building.def.useHitPoints && building.def.building != null
            && !building.def.IsWall && !building.def.building.isNaturalRock
            && !(building is Building_Door) && !(building is Building_Turret)
            && building.def.passability != Traversability.Standable
            && building.GetComp<CompExplosive>() == null;

        // Only the removed building's footprint is virtual. Terrain and every
        // other blocking thing still apply; these helpers never mutate the map.
        internal static bool FreeIgnoring(Map map, IntVec3 cell, Func<IntVec3, bool> claimed, Building ignored)
        {
            if (!cell.InBounds(map) || claimed(cell)) return false;
            if (ignored == null || !ignored.OccupiedRect().Contains(cell)) return cell.Standable(map);
            return map.terrainGrid.TerrainAt(cell).passability == Traversability.Standable
                && cell.GetThingList(map).All(thing => thing == ignored || thing.def.passability == Traversability.Standable);
        }
        internal static bool WalkIgnoring(Map map, IntVec3 cell, Building ignored)
        {
            if (!cell.InBounds(map)) return false;
            if (ignored == null || !ignored.OccupiedRect().Contains(cell)) return cell.Walkable(map);
            return map.terrainGrid.TerrainAt(cell).passability != Traversability.Impassable
                && cell.GetThingList(map).All(thing => thing == ignored || thing.def.passability != Traversability.Impassable);
        }
        public static bool Useful(int beforeStack, int afterStack, int beforeInside, int afterInside,
            bool mouthBlocked, int count) => afterStack > 0 && afterInside > 0
            && (mouthBlocked || beforeStack < count && afterStack > beforeStack
                || beforeInside < count && afterInside > beforeInside);
    }

    public sealed class TacticalFurnitureAction : IExposable
    {
        public Building Target;
        public Pawn Worker;
        public TacticalLocalPlan Plan;
        public IntVec3 WorkCell;
        public int Started = -1;
        public bool Failed;
        public List<IntVec3> Footprint = new List<IntVec3>();
        public List<IntVec3> TemporaryClaims = new List<IntVec3>();
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && Target?.Destroyed == true) Target = null;
            Scribe_References.Look(ref Target, "target");
            Scribe_References.Look(ref Worker, "worker");
            Scribe_Deep.Look(ref Plan, "plan");
            Scribe_Values.Look(ref WorkCell, "workCell");
            Scribe_Values.Look(ref Started, "started", -1);
            Scribe_Values.Look(ref Failed, "failed");
            Scribe_Collections.Look(ref Footprint, "footprint", LookMode.Value);
            Scribe_Collections.Look(ref TemporaryClaims, "temporaryClaims", LookMode.Value);
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        public TacticalFurnitureAction FurnitureAction;
        public TacticalLocalPlan FurnitureResumePlan;
        public HashSet<int> RejectedFurniture = new HashSet<int>();
        internal int FurnitureProbes;
        internal int FurnitureCursor;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long FurniturePlans, FurnitureCompleted, FurnitureAborted;

        private bool FurnitureApproachSecured(TacticalSquadCommand command, IntVec3 cell)
        {
            if (command.SecuredCells.Contains(cell)) return true;
            Building furniture = cell.InBounds(map) ? cell.GetEdifice(map) : null;
            return TacticalFurniturePolicy.IsFurniture(furniture)
                && GenAdj.CellsAdjacentCardinal(furniture).Any(command.SecuredCells.Contains);
        }

        private TacticalLocalPlan ResumeFurniturePlan(TacticalSquadCommand command,
            List<TacticalMemberCommand> active, Pawn leader, Func<IntVec3, bool> claimed)
        {
            TacticalLocalPlan plan = command.FurnitureResumePlan;
            if (plan == null) return null;
            command.FurnitureResumePlan = null;
            if (!TacticalLocalPlanner.SetDepth(map, plan, allowFurniture: true)) return null;
            plan.Barrier = TacticalLocalPlanner.NextBarrier(map, plan);
            plan.ExistingOpening = TacticalLocalPlanner.PassageOpen(map, plan);
            if (Enumerable.Range(0, plan.Depth).Any(layer => leases.TryGetValue(plan.Opening + plan.Inward * layer,
                    out TacticalSquadCommand owner) && owner != command)) return null;
            bool Allowed(IntVec3 cell) => cell.InBounds(map) && cell.Roofed(map) == plan.Inside.Roofed(map)
                && !command.SecuredCells.Contains(cell);
            if (TacticalLocalPlanner.Free(map, plan.Outside, claimed) && TacticalLocalPlanner.Free(map, plan.Inside, claimed)
                && TacticalLocalPlanner.BuildStack(map, plan, command.Members.Count, claimed)
                && (TacticalLocalPlanner.BuildPositions(map, plan, command.Members.Count, claimed, Allowed)
                    || plan.Positions.Count > 0)
                && leader.CanReach(plan.Outside, PathEndMode.OnCell, Danger.Deadly)) return plan;
            TryPrepareFurniture(command, active, plan, claimed, claimed, Allowed, requireFull: true);
            return null;
        }

        private bool TryPrepareFurniture(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalLocalPlan plan, Func<IntVec3, bool> outsideBlocked, Func<IntVec3, bool> insideBlocked,
            Func<IntVec3, bool> insideAllowed, bool requireFull, IntVec3? requiredCell = null)
        {
            if (command.FurnitureAction != null) return true;
            if (command.FurnitureProbes <= 0 || command.RejectedFurniture.Count >= 32
                || command.ChargeAction != null || plan.Direct) return false;
            var workers = active.Where(m => CompSledgehammerBreach.CanOperate(m.Pawn)
                && CompSledgehammerBreach.WornBy(m.Pawn) != null && m.Pawn.CurJob?.playerForced != true).ToList();
            if (workers.Count == 0) return false;
            int count = command.Members.Count;
            var baseline = new TacticalLocalPlan { Opening = plan.Opening, Inward = plan.Inward,
                Depth = plan.Depth, StackSide = plan.StackSide, Barrier = plan.Barrier };
            TacticalLocalPlanner.BuildStack(map, baseline, count, outsideBlocked, allowPartial: true);
            TacticalLocalPlanner.BuildPositions(map, baseline, count, insideBlocked, insideAllowed);
            bool mouthBlocked = !TacticalLocalPlanner.Free(map, plan.Outside, outsideBlocked)
                || !TacticalLocalPlanner.Free(map, plan.Inside, insideBlocked);
            var found = new HashSet<Building>();
            IntVec3 tangent = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
            void Probe(IntVec3 cell, Func<IntVec3, bool> blocked)
            {
                if (!cell.InBounds(map) || blocked(cell)) return;
                Building thing = cell.GetEdifice(map);
                if (TacticalFurniturePolicy.IsFurniture(thing) && !command.RejectedFurniture.Contains(thing.thingIDNumber)) found.Add(thing);
            }
            for (int depth = 1; depth <= TacticalStackFormation.Depth; depth++)
                for (int width = -TacticalStackFormation.Width; width <= TacticalStackFormation.Width; width++)
                    Probe(plan.Opening - plan.Inward * depth + tangent * width, outsideBlocked);
            for (int depth = 1; depth <= TacticalEntryFootprint.Depth; depth++)
                for (int width = -TacticalEntryFootprint.Width; width <= TacticalEntryFootprint.Width; width++)
                {
                    IntVec3 cell = plan.InnerOpening + plan.Inward * depth + tangent * width;
                    if (insideAllowed(cell)) Probe(cell, insideBlocked);
                }
            // Sort once; at most two virtual removals across the entire advance,
            // including all eight facade candidates. No pawn-per-tick searches.
            CompSledgehammerBreach tool = CompSledgehammerBreach.WornBy(workers[0].Pawn);
            bool approachOpen = TacticalLocalPlanner.NextBarrier(map, plan) == null;
            Building[] candidates = found.Where(b => TacticalFurniturePolicy.WorkTicks(b.HitPoints,
                tool.Props.hitDamage, tool.Props.hitIntervalTicks) <= TacticalFurniturePolicy.MaximumWorkTicks
                && (!requiredCell.HasValue || b.OccupiedRect().Contains(requiredCell.Value)))
                .OrderBy(b => TacticalFurniturePolicy.WorkTicks(b.HitPoints, tool.Props.hitDamage, tool.Props.hitIntervalTicks))
                .ThenBy(b => b.Position.DistanceToSquared(plan.Outside)).ToArray();
            int start = candidates.Length == 0 ? 0 : command.FurnitureCursor % candidates.Length;
            for (int checkedCandidates = 0; checkedCandidates < candidates.Length && command.FurnitureProbes > 0; checkedCandidates++)
            {
                int index = (start + checkedCandidates) % candidates.Length;
                Building target = candidates[index];
                command.FurnitureCursor = (index + 1) % candidates.Length;
                if (target.OccupiedRect().Any(c => claims.TryGetValue(c, out TacticalSquadCommand other) && other != command)) continue;
                command.FurnitureProbes--;
                var preview = new TacticalLocalPlan { Opening = plan.Opening, Inward = plan.Inward,
                    Depth = plan.Depth, StackSide = plan.StackSide, Barrier = plan.Barrier };
                if (!TacticalFurniturePolicy.FreeIgnoring(map, plan.Outside, outsideBlocked, target)
                    || !TacticalFurniturePolicy.FreeIgnoring(map, plan.Inside, insideBlocked, target)) continue;
                bool fullStack = TacticalLocalPlanner.BuildStack(map, preview, count, outsideBlocked,
                    allowPartial: !requireFull, ignored: target);
                bool fullInside = TacticalLocalPlanner.BuildPositions(map, preview, count, insideBlocked, insideAllowed, target);
                if (requireFull && (!fullStack || !fullInside)
                    || !requiredCell.HasValue && !TacticalFurniturePolicy.Useful(baseline.Stack.Count, preview.Stack.Count,
                        baseline.Positions.Count, preview.Positions.Count, mouthBlocked, count)) continue;
                TacticalMemberCommand helper = workers.OrderBy(m => m.Pawn.Position.DistanceToSquared(target.Position)).First();
                if (target.Faction != null && !target.HostileTo(helper.Pawn)) continue;
                CompSledgehammerBreach actualTool = CompSledgehammerBreach.WornBy(helper.Pawn);
                if (TacticalFurniturePolicy.WorkTicks(target.HitPoints, actualTool.Props.hitDamage, actualTool.Props.hitIntervalTicks)
                    > TacticalFurniturePolicy.MaximumWorkTicks) continue;
                // Work only from the already occupied approach space. Do not
                // send the engineer through an unrelated door to clear inside.
                IntVec3 work = GenAdj.CellsAdjacentCardinal(target)
                    .Where(c => c.InBounds(map) && c.Standable(map)
                        && !(claims.TryGetValue(c, out TacticalSquadCommand other) && other != command)
                        && (!outsideBlocked(c) || approachOpen && Enumerable.Range(0, plan.Depth)
                            .Any(layer => c == plan.Opening + plan.Inward * layer))
                        && (command.Phase == TacticalCommandPhase.Clear ? command.SecuredCells.Contains(c)
                            || approachOpen && Enumerable.Range(0, plan.Depth).Any(layer => c == plan.Opening + plan.Inward * layer)
                            : approachOpen || (c.x - plan.Opening.x) * plan.Inward.x
                                + (c.z - plan.Opening.z) * plan.Inward.z < 0)
                        && !active.Any(m => m != helper && m.Pawn.Position == c)
                        && !(command.Plan?.Positions.Where((_, i) => command.Members[i] != helper).Contains(c) ?? false)
                        && !(command.Plan?.Stack.Where((_, i) => command.Plan.Stack.Count == command.Members.Count
                            && command.Members[i] != helper).Contains(c) ?? false))
                    .OrderBy(c => c.DistanceToSquared(helper.Pawn.Position)).DefaultIfEmpty(IntVec3.Invalid).First();
                if (!work.IsValid && !approachOpen && plan != command.Plan
                    && target.OccupiedRect().Any(c => (c.x - plan.Opening.x) * plan.Inward.x
                        + (c.z - plan.Opening.z) * plan.Inward.z >= plan.Depth))
                {
                    // Do not walk around a closed wall. Keep the proven virtual
                    // inside seats and clear from this opening once it exists.
                    plan.PendingFurniture = target; plan.StackSide = preview.StackSide;
                    plan.Stack.Clear(); plan.Stack.AddRange(preview.Stack);
                    plan.Positions.Clear(); plan.Positions.AddRange(preview.Positions);
                    plan.Interior.Clear(); plan.Interior.UnionWith(preview.Interior);
                    plan.EntryLane = preview.EntryLane;
                    return true;
                }
                if (!work.IsValid || !helper.Pawn.CanReserve(target)
                    || !helper.Pawn.CanReserveAndReach(work, PathEndMode.OnCell, Danger.Deadly)) continue;
                command.FurnitureAction = new TacticalFurnitureAction { Target = target, Worker = helper.Pawn,
                    WorkCell = work, Plan = plan, Footprint = target.OccupiedRect().ToList() };
                foreach (IntVec3 cell in command.FurnitureAction.Footprint.Concat(new[] { work }))
                    if (!claims.ContainsKey(cell))
                    { claims[cell] = command; command.FurnitureAction.TemporaryClaims.Add(cell); }
                command.Due = GenTicks.TicksGame + 1; FurniturePlans++;
                return true;
            }
            return false;
        }

        private bool AwaitPendingFurniture(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalLocalPlan plan = command.Plan;
            Building pending = plan?.PendingFurniture;
            if (pending == null) return false;
            if (pending.Destroyed || !pending.Spawned) { plan.PendingFurniture = null; return false; }
            if (command.ChargeAction != null || TacticalLocalPlanner.NextBarrier(map, plan) != null) return false;
            if (command.FurnitureAction != null) return false;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) { command.DeferredWork = true; return true; }
            long started = Stopwatch.GetTimestamp();
            try
            {
                bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out TacticalSquadCommand other) && other != command;
                bool Allowed(IntVec3 cell) => cell.InBounds(map) && cell.Roofed(map) == plan.Inside.Roofed(map);
                if (TryPrepareFurniture(command, active, plan, Claimed, Claimed, Allowed, requireFull: false,
                    requiredCell: pending.Position)) return false; // Advance the queued job below.
                plan.PendingFurniture = null;
                BeginReturn(command, tick, false, replan: true);
                return true;
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }

        private bool AdvanceFurniture(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalFurnitureAction action = command.FurnitureAction;
            if (action == null) return false;
            TacticalMemberCommand helper = active.Find(m => m.Pawn == action.Worker);
            bool gone = action.Target == null || action.Target.Destroyed || !action.Target.Spawned;
            bool abort = action.Failed || helper == null || !CompSledgehammerBreach.CanOperate(action.Worker)
                || CompSledgehammerBreach.WornBy(action.Worker) == null
                || action.Started >= 0 && tick - action.Started > 1800;
            if (gone || abort)
            {
                if (helper?.Job?.def.defName == TacticalFurniturePolicy.JobName && helper.Pawn.CurJob == helper.Job)
                {
                    if (!CanIssue(helper)) return true;
                    if (!Issue(helper, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalPost"),
                        helper.Pawn.Position, action.Plan.Opening))) return true;
                }
                if (gone)
                {
                    FurnitureCompleted++;
                    if (command.Phase == TacticalCommandPhase.Pending) command.FurnitureResumePlan = action.Plan;
                    if (command.Phase == TacticalCommandPhase.Clear
                        && action.Footprint.Any(c => GenAdj.CardinalDirections.Any(d => command.SecuredCells.Contains(c + d))))
                        foreach (IntVec3 cell in action.Footprint)
                            if (cell.InBounds(map) && cell.Walkable(map)) command.SecuredCells.Add(cell);
                }
                else
                {
                    FurnitureAborted++;
                    if (action.Target != null && helper != null && CompSledgehammerBreach.WornBy(action.Worker) != null)
                        command.RejectedFurniture.Add(action.Target.thingIDNumber);
                }
                ReleaseFurnitureClaims(command, action);
                command.FurnitureAction = null; command.PlanRetryAt = 0; command.Due = tick + 1;
                return !gone; // Retry a lost worker on the next advance before normal breach/entry.
            }
            command.Due = Math.Min(command.Due, tick + 15);
            if (helper.Job?.def.defName != TacticalFurniturePolicy.JobName || helper.Pawn.CurJob != helper.Job)
            {
                if (!CanIssue(helper)) return true;
                CompSledgehammerBreach tool = CompSledgehammerBreach.WornBy(helper.Pawn);
                if (!Issue(helper, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalFurniturePolicy.JobName),
                    action.Target, action.WorkCell, tool.parent))) return true;
                if (action.Started < 0) action.Started = tick;
            }
            foreach (TacticalMemberCommand member in active)
            {
                if (member == helper) continue;
                if (command.Phase == TacticalCommandPhase.Pending) EnsureParking(command, member, tick);
                // Already assigned posts stay fixed; no new flood while working.
                else if (command.Plan != null && command.Plan.Positions.Count == command.Members.Count)
                    EnsurePost(member, command.Phase == TacticalCommandPhase.Clear || member.Crossed
                        ? command.Plan.Positions[command.Members.IndexOf(member)]
                        : command.Plan.Stack[command.Members.IndexOf(member)], action.Plan.Opening, tick);
            }
            return true;
        }

        private void ReleaseFurnitureClaims(TacticalSquadCommand command, TacticalFurnitureAction action)
        {
            foreach (IntVec3 cell in action.TemporaryClaims)
                if (claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command) claims.Remove(cell);
        }

        private bool RecoverStalledFurniture(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalMemberCommand member)
        {
            TacticalLocalPlan plan = command.Plan;
            if (plan == null || plan.Direct || command.FurnitureProbes <= 0) return false;
            bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out TacticalSquadCommand other) && other != command;
            bool Allowed(IntVec3 cell) => cell.InBounds(map) && cell.Roofed(map) == plan.Inside.Roofed(map);
            foreach (IntVec3 cell in new[] { plan.Outside, plan.Inside, plan.EntryLane, RetryPost(command, member) })
            {
                if (!cell.IsValid || !cell.InBounds(map) || cell.Standable(map)
                    || !TacticalFurniturePolicy.IsFurniture(cell.GetEdifice(map))) continue;
                int tick = GenTicks.TicksGame;
                TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) { command.DeferredWork = true; return true; }
                long started = Stopwatch.GetTimestamp();
                try
                {
                    return TryPrepareFurniture(command, active, plan, Claimed, Claimed, Allowed,
                        requireFull: false, requiredCell: cell);
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            return false;
        }
    }

    public sealed class JobDriver_TacticalClearFurniture : TacticalJobDriver
    {
        private Building Target => job.targetA.Thing as Building;
        private CompSledgehammerBreach Tool => (job.targetC.Thing as ThingWithComps)?.TryGetComp<CompSledgehammerBreach>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            BindOwner(); job.canUseRangedWeapon = false;
            this.FailOn(() => Tool?.Wearer != pawn || !CompSledgehammerBreach.CanOperate(pawn));
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil wait = Toils_General.Wait(Tool.Props.hitIntervalTicks);
            wait.handlingFacing = true;
            wait.initAction = () => pawn.rotationTracker.FaceTarget(job.targetA);
            wait.AddEndCondition(() => Target == null || Target.Destroyed || !Target.Spawned
                ? JobCondition.Succeeded : JobCondition.Ongoing);
            yield return wait;
            yield return Toils_General.Do(() =>
            {
                Building target = Target;
                if (target == null || target.Destroyed || !target.Spawned) { EndJobWith(JobCondition.Succeeded); return; }
                if (pawn.Position != job.targetB.Cell || !TacticalFurniturePolicy.IsFurniture(target)
                    || target.Faction != null && !target.HostileTo(pawn))
                { EndJobWith(JobCondition.Incompletable); return; }
                SoundDefOf.Pawn_Melee_Punch_HitBuilding_Generic.PlayOneShot(new TargetInfo(target.Position, pawn.Map));
                target.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Tool.Props.hitDamage, 0f, -1f, pawn));
                workTicks += Tool.Props.hitIntervalTicks;
                if (target.Destroyed || !target.Spawned) EndJobWith(JobCondition.Succeeded);
                else if (workTicks >= TacticalFurniturePolicy.MaximumWorkTicks) EndJobWith(JobCondition.Incompletable);
            });
            yield return Toils_Jump.Jump(wait);
        }
        private int workTicks;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref workTicks, "furnitureWorkTicks"); }
    }
}
