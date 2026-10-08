using System;
using System.Collections.Generic;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    // One small action record per opening. No live room graph or enemy tracking.
    public sealed partial class TacticalOpeningAction
    {
        public Pawn Observer, Thrower;
        public IntVec3 ObservationPosition = IntVec3.Invalid, Source = IntVec3.Invalid, Enemy = IntVec3.Invalid,
            Target = IntVec3.Invalid, ReturnPosition = IntVec3.Invalid;
        public int EnemyId = -1, Started, SupportStarted, SettledAt = -1;
        public bool ObservationDone, ObservationIssued, StackPrepared, ThrowIssued, Launched, Returned, EffectsCleared, EntryAdjusted, Outdoors;
        public int RoomCells;
        public Projectile Projectile;
        public ThingDef ProjectileDef;
    }

    public static class TacticalOpeningPolicy
    {
        public const int ObservationTicks = 90, PrepareTicks = 30, ReleaseTicks = 18, SettleTicks = 30;
        public static bool WantsSupport(bool outdoors, int cells, bool contact) => outdoors || contact || cells > 16;
        public static bool FarEnough(IntVec3 opening, IntVec3 target) => opening.DistanceToSquared(target) >= 4;
        public static bool EffectsPending(bool live, bool explosion, bool returned, int settledAt, int now) =>
            live || explosion || !returned || settledAt < 0 || now - settledAt < SettleTicks;

        // A room-size classification only: terminate at the seventeenth cell.
        // Doors are boundaries even when open, so a tiny room stays tiny.
        public static int ClassifySize(IntVec3 inside, Func<IntVec3, bool> floor)
        {
            var seen = new HashSet<IntVec3>(); var pending = new Queue<IntVec3>();
            if (!floor(inside)) return 0;
            seen.Add(inside); pending.Enqueue(inside);
            while (pending.Count > 0 && seen.Count <= 16)
            {
                IntVec3 cell = pending.Dequeue();
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (seen.Contains(next) || !floor(next)) continue;
                    seen.Add(next); pending.Enqueue(next);
                    if (seen.Count > 16) return 17;
                }
            }
            return seen.Count;
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long Observations, ObservationContacts, SupportThrows, SupportWaits, SupportReturns, UnsafeEntries;

        internal void Observed(Pawn pawn, Job job, IntVec3 enemy, int id)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.OpeningAction?.Observer != pawn
                || pawn.CurJob != job || command.Phase != TacticalCommandPhase.Observe) return;
            command.OpeningAction.ObservationDone = true;
            command.OpeningAction.Enemy = enemy; command.OpeningAction.EnemyId = id;
            if (enemy.IsValid)
            {
                ObservationContacts++;
                RecordContact(command, id, enemy, command.OpeningAction.Source, GenTicks.TicksGame);
            }
            Wake(pawn);
        }
        internal void ObservationFinished(Pawn pawn, Job job)
        {
            if (byPawn.TryGetValue(pawn, out TacticalSquadCommand command) && command.OpeningAction?.Observer == pawn
                && pawn.CurJob == job) { command.OpeningAction.ObservationDone = true; Wake(pawn); }
        }

        internal void Launched(Pawn pawn, Job job, Projectile projectile)
        {
            if (FieldSmokeLaunched(pawn, job, projectile)) return;
            if (projectile == null || !byPawn.TryGetValue(pawn, out TacticalSquadCommand command)
                || command.OpeningAction?.Thrower != pawn || pawn.CurJob != job) return;
            TacticalOpeningAction action = command.OpeningAction;
            action.Launched = true; action.Projectile = projectile; action.ProjectileDef = projectile.def;
            action.SettledAt = -1; command.Phase = TacticalCommandPhase.BlastWait;
            SupportThrows++; Wake(pawn);
        }
        internal void SupportReturned(Pawn pawn, Job job)
        {
            if (FieldSmokeReturned(pawn, job)) return;
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.OpeningAction?.Thrower != pawn
                || pawn.CurJob != job || pawn.Position != command.OpeningAction.ReturnPosition) return;
            if (!command.OpeningAction.Returned) SupportReturns++;
            command.OpeningAction.Returned = true; Wake(pawn);
        }

        private void AdvanceOpeningAction(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalLocalPlan plan = command.Plan;
            command.Due = tick + 15;
            if (command.OpeningAction == null)
            {
                command.OpeningAction = new TacticalOpeningAction { Started = tick, Source = plan.Outside,
                    Outdoors = !plan.Inside.Roofed(map) };
                command.OpeningAction.RoomCells = TacticalOpeningPolicy.ClassifySize(plan.Inside,
                    // Furniture belongs to the room's floor area, even though
                    // its cells cannot be assigned as standing formation slots.
                    cell => cell.InBounds(map) && cell != plan.Opening && cell.Walkable(map) && !(cell.GetEdifice(map) is Building_Door));
            }
            TacticalOpeningAction action = command.OpeningAction;
            // A completed observation records one fixed enemy location. Do not
            // query that pawn's future position while preparing the throw.
            foreach (TacticalMemberCommand member in active)
            {
                if ((member.Pawn == action.Observer && member.Pawn.jobs.curDriver is JobDriver_TacticalObserve observe && !observe.AtPost)
                    || (member.Pawn == action.Thrower && member.Pawn.jobs.curDriver is JobDriver_TacticalThrow throwing && !throwing.AtPost)) continue;
                int index = command.Members.IndexOf(member);
                EnsurePost(member, plan.Stack[index], plan.Opening, tick);
            }
            if (command.Phase == TacticalCommandPhase.Observe)
            {
                if (!OpeningUsable(plan))
                { command.OpeningAction = null; command.Phase = TacticalCommandPhase.Breach; return; }
                if (!action.ObservationIssued)
                {
                    IntVec3 lateral = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
                    foreach (int side in new[] { 1, -1 })
                    {
                        IntVec3 spot = plan.Outside + lateral * side;
                        int index = plan.Stack.IndexOf(spot);
                        if (index < 0 || !spot.Standable(map) || !(spot + plan.Inward).InBounds(map)
                            || (spot + plan.Inward).CanBeSeenOver(map)) continue;
                        TacticalMemberCommand observer = command.Members[index];
                        if (!Available(observer, map) || !AtPost(observer)) continue;
                        if (!CanIssue(observer)) return;
                        Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalObserve"), spot, plan.Inside, plan.Stack[index]);
                        job.targetQueueA = new List<LocalTargetInfo> { plan.Outside, plan.Opening };
                        job.canUseRangedWeapon = false;
                        action.Observer = observer.Pawn; action.ObservationPosition = spot;
                        action.ObservationIssued = true; Observations++;
                        Issue(observer, job); return;
                    }
                    // No suitable covered slot: retain unknown interior instead
                    // of inventing an unsafe observer destination.
                    if (tick - action.Started < 180) return;
                    action.ObservationDone = true;
                }
                bool returned = action.Observer == null || active.Exists(m => m.Pawn == action.Observer && AtPost(m));
                if (!action.ObservationDone || !returned)
                {
                    if (action.Observer?.Dead == true || action.Observer?.Downed == true || tick - action.Started > 600)
                    { action.ObservationDone = true; action.Observer = null; }
                    else return;
                }
                command.Phase = TacticalCommandPhase.Support;
                action.SupportStarted = tick;
            }
            if (command.Phase == TacticalCommandPhase.Support)
            {
                if (action.ThrowIssued)
                {
                    // A preparation interrupted before launch simply returns
                    // to the held formation; never keep retrying every tick.
                    if (action.Thrower?.jobs.curDriver is JobDriver_TacticalThrow preparing && !preparing.AtPost) return;
                    command.Phase = TacticalCommandPhase.Enter; return;
                }
                if (!TacticalOpeningPolicy.WantsSupport(action.Outdoors, action.RoomCells, action.Enemy.IsValid))
                { command.Phase = TacticalCommandPhase.Enter; return; }
                IntVec3 target = action.Target.IsValid ? action.Target : ChooseSupportTarget(plan, action);
                if (!target.IsValid) { command.Phase = TacticalCommandPhase.Enter; return; }
                action.Target = target;
                if (!action.StackPrepared)
                {
                    action.StackPrepared = true;
                    if (!action.Outdoors && !PrepareThrowStack(command, target))
                    { command.Phase = TacticalCommandPhase.Enter; return; }
                }
                if (active.Exists(member => !AtPost(member)))
                {
                    if (tick - action.SupportStarted > 600) command.Phase = TacticalCommandPhase.Enter;
                    return;
                }
                foreach (TacticalMemberCommand member in active)
                {
                    if (!AtPost(member) || member.Pawn.CurJob?.playerForced == true) continue;
                    IntVec3 throwPosition = plan.Stack[command.Members.IndexOf(member)];
                    Thing grenade = SupportGrenade(member.Pawn, action.Outdoors);
                    if (grenade == null || !InventoryGrenadeUtility.TryFindThrowSourceFrom(member.Pawn, throwPosition,
                        target, InventoryGrenadeUtility.NormalThrowRange, out _)
                        || !SafeOpeningThrow(member.Pawn, grenade, throwPosition, target)) continue;
                    if (!CanIssue(member)) return;
                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalThrow"), target, grenade, throwPosition);
                    job.targetQueueA = new List<LocalTargetInfo> { plan.Stack[command.Members.IndexOf(member)], plan.Opening };
                    job.canUseRangedWeapon = false;
                    action.Thrower = member.Pawn; action.Target = target; action.ThrowIssued = true;
                    action.ReturnPosition = throwPosition;
                    Issue(member, job); return;
                }
                command.Phase = TacticalCommandPhase.Enter;
            }
            if (command.Phase == TacticalCommandPhase.BlastWait)
            {
                if (SupportEffectsPending(command, tick)) { SupportWaits++; return; }
                if (command.ReplanAfterSupport)
                {
                    ResetAfterSupport(command, tick);
                    return;
                }
                command.Phase = TacticalCommandPhase.Enter;
            }
        }

        private bool SupportEffectsPending(TacticalSquadCommand command, int tick)
        {
            TacticalOpeningAction action = command.OpeningAction;
            if (action?.Launched != true || action.EffectsCleared) return false;
            bool live = action.Projectile?.Spawned == true, explosion = false;
            if (!live)
                foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Explosion))
                    if (thing is Explosion effect && effect.Spawned && effect.instigator == action.Thrower
                        && effect.projectile == action.ProjectileDef) { explosion = true; break; }
            bool returned = command.ReleaseAfterReturn || action.Thrower == null || !action.Thrower.Spawned || action.Thrower.Dead || action.Thrower.Downed
                || action.Returned || command.Members.Exists(m => m.Pawn == action.Thrower
                    && m.Pawn.Position == action.ReturnPosition && AtPost(m));
            if (live || explosion || !returned) action.SettledAt = -1;
            else if (action.SettledAt < 0) action.SettledAt = tick;
            bool pending = TacticalOpeningPolicy.EffectsPending(live, explosion, returned, action.SettledAt, tick);
            if (!pending) action.EffectsCleared = true;
            return pending;
        }
        private void AvoidObservedOccupiedPost(TacticalSquadCommand command)
        {
            TacticalOpeningAction action = command.OpeningAction;
            if (action == null || action.EntryAdjusted) return;
            action.EntryAdjusted = true;
            if (!action.Enemy.IsValid) return;
            int index = command.Plan.Positions.IndexOf(action.Enemy);
            if (index < 0) return;
            var candidate = new TacticalLocalPlan { Opening = command.Plan.Opening, Inward = command.Plan.Inward };
            bool Busy(IntVec3 cell) => cell == action.Enemy || !command.Plan.Interior.Contains(cell) || command.Plan.Positions.Contains(cell)
                || claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command;
            if (!TacticalLocalPlanner.BuildPositions(map, candidate, 1, Busy)) return;
            if (claims.TryGetValue(action.Enemy, out TacticalSquadCommand current) && current == command) claims.Remove(action.Enemy);
            command.Plan.Positions[index] = candidate.Positions[0]; claims[candidate.Positions[0]] = command;
        }

        private IntVec3 ChooseSupportTarget(TacticalLocalPlan plan, TacticalOpeningAction action)
        {
            if (action.Enemy.IsValid)
                return TacticalOpeningPolicy.FarEnough(plan.Opening, action.Enemy) ? action.Enemy : IntVec3.Invalid;
            IntVec3 fallback = IntVec3.Invalid, lateral = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
            // Nine local cells, no scoring grid. Prefer an unseen corner.
            for (int depth = 3; depth <= 5; depth++)
                foreach (int width in new[] { 0, 2, -2 })
                {
                    IntVec3 cell = plan.Opening + plan.Inward * depth + lateral * width;
                    if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                    if (!fallback.IsValid) fallback = cell;
                    if (action.ObservationPosition.IsValid && !GenSight.LineOfSight(action.ObservationPosition, cell, map, true)) return cell;
                }
            return fallback;
        }

        private bool PrepareThrowStack(TacticalSquadCommand command, IntVec3 target)
        {
            TacticalLocalPlan plan = command.Plan;
            if (plan.Stack.TrueForAll(cell => !GenSight.LineOfSight(target, cell, map, true))) return true;
            // Rebuild only once for this throw, in a small connected patch on
            // the original wall face. Reject every exposed cell, including the
            // middle-line fallback used for non-explosive narrow-room entry.
            IntVec3 lateral = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
            var slots = new List<IntVec3>(command.Members.Count);
            var queue = new Queue<IntVec3>(); var seen = new HashSet<IntVec3>();
            bool Covered(IntVec3 cell)
            {
                IntVec3 delta = cell - plan.Opening;
                int depth = -(delta.x * plan.Inward.x + delta.z * plan.Inward.z);
                int width = delta.x * lateral.x + delta.z * lateral.z;
                if (depth < 1 || depth > 6 || System.Math.Abs(width) > 8 || !cell.InBounds(map)
                    || !cell.Standable(map) || claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command
                    || GenSight.LineOfSight(target, cell, map, true)) return false;
                for (int i = 1; i <= System.Math.Abs(width); i++)
                {
                    IntVec3 wall = plan.Opening + lateral * (i * System.Math.Sign(width));
                    Building support = wall.InBounds(map) ? wall.GetEdifice(map) : null;
                    if (support == null || !(support.def.IsWall || support is Building_Door)) return false;
                }
                return true;
            }
            foreach (IntVec3 cell in plan.Stack)
                if (Covered(cell)) { queue.Enqueue(cell); seen.Add(cell); break; }
            while (queue.Count > 0 && slots.Count < command.Members.Count && seen.Count <= 96)
            {
                IntVec3 cell = queue.Dequeue(); slots.Add(cell);
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (seen.Contains(next) || !Covered(next)) continue;
                    seen.Add(next); queue.Enqueue(next);
                }
            }
            if (slots.Count != command.Members.Count) return false;
            foreach (IntVec3 cell in plan.Stack)
                if (claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command) claims.Remove(cell);
            plan.Stack.Clear(); plan.Stack.AddRange(slots);
            TacticalEntryAllocation.SyncOutside(plan);
            foreach (IntVec3 cell in slots) claims[cell] = command;
            return true;
        }

        private static Thing SupportGrenade(Pawn pawn, bool smoke)
        {
            if (!InventoryGrenadeUtility.CanUseGrenades(pawn) || pawn.inventory == null) return null;
            foreach (Thing item in pawn.inventory.innerContainer)
                if (InventoryGrenadeUtility.IsInventoryGrenade(item.def) && RaidSmokeUtility.IsSmoke(item) == smoke
                    && (smoke || item.def.defName == "HD_Grenade_MKII" || item.def.defName == "HD_Grenade_MKIII"
                        || item.def.defName == "HD_Grenade_M84_Item" || item.def.defName == "HD_Grenade_M7A2_Item")) return item;
            return null;
        }

        internal static bool SafeOpeningThrow(Pawn pawn, Thing grenade, IntVec3 position, IntVec3 target)
        {
            ThingDef projectile = grenade?.def.projectileWhenLoaded;
            if (projectile?.projectile == null) return false;
            if (RaidSmokeUtility.IsSmoke(grenade)) return true;
            float scatter = InventoryGrenadeUtility.ThrowMissRadius(pawn, false, position.DistanceTo(target));
            FragmentationGrenadeExtension fragments = projectile.GetModExtension<FragmentationGrenadeExtension>();
            float radius = Math.Max(projectile.projectile.explosionRadius, fragments == null ? 0 :
                Math.Max(fragments.radius, fragments.longRangeFragmentFraction > 0 ? fragments.longRangeRadius : 0)) + scatter + .75f;
            foreach (Pawn ally in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                IntVec3 cell = ally == pawn ? position : ally.Position;
                if (!ally.Dead && !ally.HostileTo(pawn) && cell.DistanceToSquared(target) <= radius * radius
                    && (cell.DistanceTo(target) <= scatter + .75f || GenSight.LineOfSight(target, cell, pawn.Map, true))) return false;
            }
            return true;
        }
    }
}
