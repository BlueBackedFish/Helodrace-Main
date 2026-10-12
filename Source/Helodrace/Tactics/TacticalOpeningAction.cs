using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalSupportSkip { None, Policy, NoTarget, NoGrenade, NoThrowPosition, UnsafeFriendly, Formation, Budget, Interrupted }
    // One small action record per opening. No live room graph or enemy tracking.
    public sealed partial class TacticalOpeningAction
    {
        public Pawn Observer, Thrower;
        public IntVec3 ObservationPosition = IntVec3.Invalid, Source = IntVec3.Invalid, Enemy = IntVec3.Invalid,
            Target = IntVec3.Invalid, ReturnPosition = IntVec3.Invalid;
        public int EnemyId = -1, Started, SupportStarted, SettledAt = -1;
        public bool ObservationDone, ObservationIssued, ThrowIssued, Launched, Returned, EffectsCleared, EntryAdjusted, Outdoors, KnownHelodPrisoner;
        public int RoomCells;
        public int SupportCursor;
        public TacticalSupportSkip SupportSkip;
        public Projectile Projectile;
        public ThingDef ProjectileDef;
    }

    public static class TacticalOpeningPolicy
    {
        public const int ObservationTicks = 90, PrepareTicks = 30, ReleaseTicks = 18, SettleTicks = 30;
        public const int ThrowCandidatesPerSlice = 4;
        public static IntVec3 FindThrowPosition(TacticalLocalPlan plan, IntVec3 post,
            Func<IntVec3,bool> free, Func<IntVec3,bool> canThrow)
        {
            if (free(post) && canThrow(post)) return post;
            IntVec3 lateral = new IntVec3(-plan.Inward.z,0,plan.Inward.x);
            int side = plan.StackSide < 0 ? -1 : 1;
            IntVec3 spot = plan.Outside + lateral * side;
            if (free(spot) && canThrow(spot)) return spot;
            spot = plan.Outside - lateral * side;
            return free(spot) && canThrow(spot) ? spot : IntVec3.Invalid;
        }
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

        internal bool ObservedOpeningMelee(Pawn pawn, Job job, Pawn enemy, int tick, bool ranged)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.OpeningAction?.Observer != pawn
                || pawn.CurJob != job || command.Phase != TacticalCommandPhase.Observe) return false;
            ObserveMeleeThreat(command, enemy, pawn, tick, ranged);
            return command.Contacts.Melee.Find(enemy.thingIDNumber)?.Urgency > 0;
        }
        internal void Observed(Pawn pawn, Job job, IntVec3 enemy, int id, bool ranged = true)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.OpeningAction?.Observer != pawn
                || pawn.CurJob != job || command.Phase != TacticalCommandPhase.Observe) return;
            command.OpeningAction.ObservationDone = true;
            command.OpeningAction.Enemy = enemy; command.OpeningAction.EnemyId = id;
            if (enemy.IsValid)
            {
                ObservationContacts++;
                if (ranged) RecordContact(command, id, enemy, command.OpeningAction.Source, GenTicks.TicksGame);
            }
            Wake(pawn);
        }
        internal void ObservationFinished(Pawn pawn, Job job)
        {
            if (byPawn.TryGetValue(pawn, out TacticalSquadCommand command) && command.OpeningAction?.Observer == pawn
                && pawn.CurJob == job) { command.OpeningAction.ObservationDone = true; Wake(pawn); }
        }
        internal void ObservedPrisoner(Pawn pawn, Job job)
        {
            if (byPawn.TryGetValue(pawn, out var command) && command.OpeningAction?.Observer == pawn
                && pawn.CurJob == job && command.Phase == TacticalCommandPhase.Observe)
                command.OpeningAction.KnownHelodPrisoner = true;
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
            command.Due = tick + TacticalCadence.DecisionTicks;
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
                    int side = plan.StackSide;
                    IntVec3 spot = plan.Outside + lateral * side;
                    if (side != 0 && TacticalLocalPlanner.Free(map, spot,
                        cell => claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command)
                        && (spot + plan.Inward).InBounds(map) && !(spot + plan.Inward).CanBeSeenOver(map))
                    {
                        TacticalMemberCommand observer = null;
                        int nearest = int.MaxValue;
                        foreach (TacticalMemberCommand member in active)
                        {
                            int distance = plan.Stack[command.Members.IndexOf(member)].DistanceToSquared(spot);
                            if (!Available(member, map) || !AtPost(member) || distance >= nearest) continue;
                            observer = member; nearest = distance;
                        }
                        if (observer != null)
                        {
                            if (!CanIssue(observer)) return;
                            IntVec3 returnPosition = plan.Stack[command.Members.IndexOf(observer)];
                            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalObserve"), spot, plan.Inside, returnPosition);
                            job.targetQueueA = new List<LocalTargetInfo> { plan.Outside, plan.Opening };
                            job.canUseRangedWeapon = false;
                            action.Observer = observer.Pawn; action.ObservationPosition = spot;
                            action.ObservationIssued = true; Observations++;
                            Issue(observer, job); return;
                        }
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
                    action.SupportSkip = TacticalSupportSkip.Interrupted; command.Phase = TacticalCommandPhase.Enter; return;
                }
                bool high = TacticalHighCqbPolicy.High(command);
                bool combat = TacticalEntryCombatPolicy.Engaged(command.EntryCombatUntil, tick);
                if (!TacticalCqbDoctrine.Support(high, action.Outdoors, action.RoomCells, action.Enemy.IsValid, action.KnownHelodPrisoner, combat))
                { action.SupportSkip = TacticalSupportSkip.Policy; command.Phase = TacticalCommandPhase.Enter; return; }
                IntVec3 target = action.Target.IsValid ? action.Target : ChooseSupportTarget(plan, action);
                if (!target.IsValid) { action.SupportSkip = TacticalSupportSkip.NoTarget; command.Phase = TacticalCommandPhase.Enter; return; }
                action.Target = target;
                if (active.Exists(member => !AtPost(member)))
                {
                    action.SupportSkip = TacticalSupportSkip.Formation;
                    if (tick - action.SupportStarted > 600) command.Phase = TacticalCommandPhase.Enter;
                    return;
                }
                bool lethal = TacticalCqbDoctrine.Lethal(high, action.Enemy.IsValid, combat, action.KnownHelodPrisoner);
                var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) { action.SupportSkip = TacticalSupportSkip.Budget; command.Due = tick + 1; return; }
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    if (action.SupportSkip == TacticalSupportSkip.Budget) action.SupportSkip = TacticalSupportSkip.None;
                    for (int tested = 0; tested < TacticalOpeningPolicy.ThrowCandidatesPerSlice && action.SupportCursor < command.Members.Count * 2; tested++)
                    {
                        int slot = action.SupportCursor++, index = slot % command.Members.Count;
                        int preference = slot < command.Members.Count ? 2 : 0;
                        TacticalMemberCommand member = command.Members[index];
                        if (!active.Contains(member) || !AtPost(member) || member.Pawn.CurJob?.playerForced == true) continue;
                        IntVec3 returnPosition = plan.Stack[index];
                        Thing grenade = SupportGrenade(member.Pawn, action.Outdoors, lethal);
                        if (grenade == null || !action.Outdoors && TacticalCqbDoctrine.GrenadePreference(high, lethal, grenade.def.defName) < preference)
                        { action.SupportSkip = TacticalSupportSkip.NoGrenade; continue; }
                        bool Free(IntVec3 cell)
                        {
                            if (!TacticalLocalPlanner.Free(map, cell, c => claims.TryGetValue(c, out var owner) && owner != command)) return false;
                            foreach (Thing thing in cell.GetThingList(map)) if (thing is Pawn occupant && occupant != member.Pawn) return false;
                            return true;
                        }
                        bool trajectory = false;
                        IntVec3 throwPosition = TacticalOpeningPolicy.FindThrowPosition(plan, returnPosition, Free, cell =>
                        {
                            if (!InventoryGrenadeUtility.TryFindThrowSourceFrom(member.Pawn, cell,
                                target, InventoryGrenadeUtility.NormalThrowRange, out _)) return false;
                            trajectory = true;
                            return SafeOpeningThrow(member.Pawn, grenade, cell, target);
                        });
                        if (!throwPosition.IsValid)
                        { action.SupportSkip = trajectory ? TacticalSupportSkip.UnsafeFriendly : TacticalSupportSkip.NoThrowPosition; continue; }
                        if (!CanIssue(member)) { action.SupportCursor--; action.SupportSkip = TacticalSupportSkip.Budget; command.Due = tick + 1; return; }
                        Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalThrow"), target, grenade, throwPosition);
                        job.targetQueueA = new List<LocalTargetInfo> { returnPosition, plan.Opening };
                        job.canUseRangedWeapon = false;
                        action.Thrower = member.Pawn; action.Target = target; action.ThrowIssued = true;
                        action.ReturnPosition = returnPosition; action.SupportSkip = TacticalSupportSkip.None;
                        Issue(member, job); return;
                    }
                    if (action.SupportCursor < command.Members.Count * 2) { command.Due = tick + 1; return; }
                    if (action.SupportSkip == TacticalSupportSkip.None) action.SupportSkip = TacticalSupportSkip.NoGrenade;
                    command.Phase = TacticalCommandPhase.Enter;
                }
                finally { budget.Account(tick, System.Diagnostics.Stopwatch.GetTimestamp() - started); }
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
            if (index < 0 || command.Plan.RetainedOutside.Contains(index)) return;
            var candidate = new TacticalLocalPlan { Opening = command.Plan.Opening, Inward = command.Plan.Inward, Depth = command.Plan.Depth };
            bool Busy(IntVec3 cell) => cell == action.Enemy || cell == command.Plan.Inside || cell == command.Plan.EntryLane
                || command.Plan.Positions.Contains(cell)
                || claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command;
            // The compact footprint has no spare seat. Only this observed
            // collision expands locally to find one replacement destination.
            if (!TacticalLocalPlanner.BuildPositions(map, candidate, 1, Busy,
                cell => cell.Roofed(map) == command.Plan.Inside.Roofed(map) && !command.SecuredCells.Contains(cell))) return;
            if (claims.TryGetValue(action.Enemy, out TacticalSquadCommand current) && current == command) claims.Remove(action.Enemy);
            command.Plan.Positions[index] = candidate.Positions[0]; claims[candidate.Positions[0]] = command;
            command.Plan.Interior.UnionWith(candidate.Interior);
        }

        private IntVec3 ChooseSupportTarget(TacticalLocalPlan plan, TacticalOpeningAction action)
        {
            if (action.Enemy.IsValid)
                return TacticalOpeningPolicy.FarEnough(plan.InnerOpening, action.Enemy) ? action.Enemy : IntVec3.Invalid;
            IntVec3 fallback = IntVec3.Invalid, lateral = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
            // Nine local cells, no scoring grid. Prefer an unseen corner.
            for (int depth = 3; depth <= 5; depth++)
                foreach (int width in new[] { 0, 2, -2 })
                {
                    IntVec3 cell = plan.InnerOpening + plan.Inward * depth + lateral * width;
                    if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                    if (!fallback.IsValid) fallback = cell;
                    if (action.ObservationPosition.IsValid && !GenSight.LineOfSight(action.ObservationPosition, cell, map, true)) return cell;
                }
            return fallback;
        }

        private static Thing SupportGrenade(Pawn pawn, bool smoke, bool lethal)
        {
            if (!InventoryGrenadeUtility.CanUseGrenades(pawn) || pawn.inventory == null) return null;
            bool high = pawn.Faction?.def?.defName == "HD_HelodCivilHighFaction";
            Thing selected = null; int best = -1;
            foreach (Thing item in pawn.inventory.innerContainer)
                if (InventoryGrenadeUtility.IsInventoryGrenade(item.def)
                    && TacticalGrenadePolicy.AllowsAutomatic(pawn.Faction?.def, item.def) && RaidSmokeUtility.IsSmoke(item) == smoke)
                {
                    int priority = smoke ? 0 : TacticalCqbDoctrine.GrenadePreference(high, lethal, item.def.defName);
                    if (priority > best) { selected = item; best = priority; }
                }
            return selected;
        }

        internal static bool SafeOpeningThrow(Pawn pawn, Thing grenade, IntVec3 position, IntVec3 target)
        {
            ThingDef projectile = grenade?.def.projectileWhenLoaded;
            if (projectile?.projectile == null) return false;
            if (RaidSmokeUtility.IsSmoke(grenade)) return true;
            if (pawn?.Map == null || !target.InBounds(pawn.Map)) return false;
            bool roofed = target.Roofed(pawn.Map);
            Room targetRoom = target.GetRoom(pawn.Map);
            // A structural change can temporarily leave native regions dirty.
            // Unknown indoor friendly safety is not permission to throw.
            if (roofed && targetRoom == null) return false;
            float scatter = InventoryGrenadeUtility.ThrowMissRadius(pawn, false, position.DistanceTo(target));
            float radius = TacticalGrenadePolicy.HazardRadius(projectile,
                targetRoom != null && !targetRoom.UsesOutdoorTemperature) + scatter + .75f;
            bool throughCover = TacticalGrenadePolicy.DangerousThroughCover(projectile);
            Room RoomAt(IntVec3 cell) => cell.InBounds(pawn.Map) ? cell.GetRoom(pawn.Map) : null;
            var owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            TacticalLocalPlan opening = owner != null && owner.byPawn.TryGetValue(pawn, out var command)
                && command.OpeningAction?.Target == target ? command.Plan : null;
            foreach (Pawn ally in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (ally.Dead || ally.HostileTo(pawn)) continue;
                IntVec3 cell = ally == pawn ? position : ally.Position;
                if (roofed && TacticalGrenadePolicy.FriendlyInRoom(targetRoom, cell,
                    cell.GetEdifice(pawn.Map) is Building_Door, RoomAt, opening)) return false;
                if (cell.DistanceToSquared(target) <= radius * radius
                    && (cell.DistanceTo(target) <= scatter + .75f || throughCover
                        || GenSight.LineOfSight(target, cell, pawn.Map, true))) return false;
            }
            // A currently empty target is not safe when an adjacent allied
            // team is already executing ingress into the grenade's destination.
            if (owner != null)
                foreach (var other in owner.ExecutionCommands)
                {
                    if (other.Terminal || other.Phase != TacticalCommandPhase.Enter || other.Plan == null
                        || other.Members.Exists(m => m.Pawn == pawn) || other.RaidLord?.faction == null
                        || other.RaidLord.faction.HostileTo(pawn.Faction)) continue;
                    foreach (var cell in other.Plan.Positions)
                        if (roofed && RoomAt(cell) == targetRoom
                            || cell.DistanceToSquared(target) <= radius * radius && GenSight.LineOfSight(target, cell, pawn.Map, true)) return false;
                }
            return true;
        }
    }
}
