using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalSupportSkip { None, Policy, NoTarget, NoGrenade, NoThrowPosition, UnsafeFriendly, Formation, Budget, Interrupted, RoomSnapshot }
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
        public int GrenadeChecks, TrajectoryChecks, UnsafeChecks;
        public HashSet<IntVec3> SupportRoomCells = new HashSet<IntVec3>();
        public bool SupportRoomReady, SupportRoomTruncated;
        internal TacticalSupportRoomSurvey SupportRoomSurvey;
        public List<IntVec3> SupportTargets = new List<IntVec3>(9);
        public List<int> SupportActors = new List<int>(4);
        public TacticalSupportSkip SupportSkip;
        public Projectile Projectile;
        public ThingDef ProjectileDef;
        public bool ShowThrow => ThrowIssued && Target.IsValid && (Launched ? !EffectsCleared : SupportSkip == TacticalSupportSkip.None);
    }

    public static class TacticalOpeningPolicy
    {
        public const int ObservationTicks = 90, ReleaseTicks = 18, SettleTicks = 30;
        public const int PrepareTicks = InventoryGrenadeUtility.ThrowTicks - ReleaseTicks;
        public const int ThrowCandidatesPerSlice = 4;
        public static bool ContactStage(TacticalCommandPhase phase) => phase == TacticalCommandPhase.Breach
            || phase == TacticalCommandPhase.Observe || phase == TacticalCommandPhase.Support;
        public static bool TargetSide(TacticalLocalPlan plan, IntVec3 cell)
        {
            if (plan == null || plan.Direct || !cell.IsValid) return false;
            IntVec3 delta = cell - plan.Opening;
            int depth = delta.x * plan.Inward.x + delta.z * plan.Inward.z;
            int width = delta.z * plan.Inward.x - delta.x * plan.Inward.z;
            return depth >= 0 && depth <= 14 && Math.Abs(width) <= 14
                && (depth > 0 || Math.Abs(width) <= 1);
        }
        public static bool OwnsContact(TacticalSquadCommand command, IntVec3 cell) => ContactStage(command.Phase)
            && command.OpeningAction?.Enemy.IsValid == true && TargetSide(command.Plan, cell);
        public static bool RememberContact(TacticalSquadCommand command, IntVec3 cell, int id, int tick)
        {
            if (!ContactStage(command.Phase) || !TargetSide(command.Plan, cell)
                || command.OpeningAction == null || command.OpeningAction.Launched || command.OpeningAction.Enemy.IsValid) return false;
            command.OpeningAction.Enemy = cell; command.OpeningAction.EnemyId = id;
            command.OpeningAction.ObservationDone = true;
            if (!command.OpeningAction.ThrowIssued)
            {
                command.OpeningAction.SupportTargets.Clear(); command.OpeningAction.SupportCursor = 0;
                command.OpeningAction.SupportRoomCells.Clear(); command.OpeningAction.SupportRoomReady = false;
                command.OpeningAction.SupportRoomSurvey = null; command.OpeningAction.SupportRoomTruncated = false;
                command.OpeningAction.Target = IntVec3.Invalid;
                command.OpeningAction.GrenadeChecks = command.OpeningAction.TrajectoryChecks = command.OpeningAction.UnsafeChecks = 0;
                if (command.Phase == TacticalCommandPhase.Support) command.OpeningAction.SupportStarted = tick;
            }
            return true;
        }
        public static void CollectSupportTargets(TacticalLocalPlan plan, IntVec3 enemy,
            Func<IntVec3,bool> valid, Func<IntVec3,bool> hidden, List<IntVec3> targets)
        {
            targets.Clear();
            if (enemy.IsValid)
            {
                if (FarEnough(plan.InnerOpening, enemy) && valid(enemy)) targets.Add(enemy);
                // A sighted pawn in the door/channel is still a contact. Aim
                // just beyond it instead of discarding the observation entirely.
                else if (!FarEnough(plan.InnerOpening, enemy))
                    for (int offset = 1; offset <= 3; offset++)
                    {
                        IntVec3 cell = enemy + plan.Inward * offset;
                        if (FarEnough(plan.InnerOpening, cell) && valid(cell)) { targets.Add(cell); break; }
                    }
                return;
            }
            IntVec3 lateral = new IntVec3(-plan.Inward.z,0,plan.Inward.x);
            int hiddenCount = 0;
            for (int depth = 3; depth <= 5; depth++)
                for (int wing = 0; wing < 3; wing++)
                {
                    int width = wing == 0 ? 0 : wing == 1 ? 2 : -2;
                    IntVec3 cell = plan.InnerOpening + plan.Inward * depth + lateral * width;
                    if (!valid(cell)) continue;
                    if (hidden(cell)) targets.Insert(hiddenCount++, cell); else targets.Add(cell);
                }
        }
        public static bool ThrowInProgress(TacticalOpeningAction action) => action?.ThrowIssued == true
            && action.Thrower?.CurJob?.def.defName == "HD_NewTacticalThrow"
            && action.Thrower.CurJob.targetA.Cell == action.Target
            && !(action.Thrower.jobs.curDriver is TacticalJobDriver driver && driver.AtPost);
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

        private void RecordOpeningContact(TacticalSquadCommand command, IntVec3 cell, int id, int tick)
        {
            if (!TacticalOpeningPolicy.ContactStage(command.Phase) || !TacticalOpeningPolicy.TargetSide(command.Plan, cell)
                || command.OpeningAction?.Enemy.IsValid == true) return;
            if (command.OpeningAction == null)
                command.OpeningAction = new TacticalOpeningAction { Started = tick, Source = command.Plan.Outside,
                    Outdoors = !command.Plan.Inside.Roofed(map) };
            if (!TacticalOpeningPolicy.RememberContact(command, cell, id, tick)) return;
            // A blasted mouth can lose its roof while the sighted enemy is
            // still inside. That one observed roof sample is enough to retain
            // indoor grenade doctrine instead of requiring missing HC smoke.
            if (cell.Roofed(map)) command.OpeningAction.Outdoors = false;
            ObservationContacts++;
            if (command.OpeningAction.Observer?.jobs?.curDriver is JobDriver_TacticalObserve observer) observer.RetreatRequested = true;
            // An already open hammer/cutter breach must retreat to its stack.
            // An installed charge retains its complete safety controller first.
            if (command.Phase == TacticalCommandPhase.Breach && command.ChargeAction == null && OpeningUsable(command.Plan))
            { command.Phase = TacticalCommandPhase.Observe; command.PhaseStarted = tick; }
            command.Due = tick + 1;
        }

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
                if (enemy.Roofed(map)) command.OpeningAction.Outdoors = false;
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
            if (action.Launched && !action.EffectsCleared) command.Phase = TacticalCommandPhase.BlastWait;
            // A completed observation records one fixed enemy location. Do not
            // query that pawn's future position while preparing the throw.
            foreach (TacticalMemberCommand member in active)
            {
                if ((member.Pawn == action.Observer && member.Pawn.jobs.curDriver is JobDriver_TacticalObserve observe && !observe.AtPost)
                    || (member.Pawn == action.Thrower && TacticalOpeningPolicy.ThrowInProgress(action))) continue;
                int index = command.Members.IndexOf(member);
                if (AtPost(member)) TacticalStackSecurity.Arrived(plan,index,member.Pawn.Position);
                EnsurePost(member, plan.Stack[index], TacticalStackSecurity.Face(plan,index,plan.Stack[index]), tick);
            }
            if (command.Phase == TacticalCommandPhase.Observe)
            {
                if (!OpeningUsable(plan))
                { command.OpeningAction = null; command.Phase = TacticalCommandPhase.Breach; return; }
                if (!action.ObservationIssued && !action.ObservationDone)
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
                    else { command.CqbWait=action.ObservationDone ? TacticalCqbWait.ObserverReturn : TacticalCqbWait.Observation; return; }
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
                    if (TacticalOpeningPolicy.ThrowInProgress(action)) { command.CqbWait=TacticalCqbWait.ThrowPreparation; return; }
                    SkipOpeningSupport(command, TacticalSupportSkip.Interrupted); return;
                }
                bool high = TacticalHighCqbPolicy.High(command);
                bool combat = TacticalEntryCombatPolicy.Engaged(command.EntryCombatUntil, tick);
                if (!TacticalCqbDoctrine.Support(high, action.Outdoors, action.RoomCells, action.Enemy.IsValid, action.KnownHelodPrisoner, combat))
                { SkipOpeningSupport(command, TacticalSupportSkip.Policy); return; }
                if (action.SupportTargets.Count == 0)
                    TacticalOpeningPolicy.CollectSupportTargets(plan, action.Enemy,
                        cell => cell.InBounds(map) && cell.Standable(map),
                        cell => action.ObservationPosition.IsValid && !GenSight.LineOfSight(action.ObservationPosition, cell, map, true),
                        action.SupportTargets);
                if (action.SupportTargets.Count == 0) { SkipOpeningSupport(command, TacticalSupportSkip.NoTarget); return; }
                bool lethal = TacticalCqbDoctrine.Lethal(high, action.Enemy.IsValid, combat, action.KnownHelodPrisoner);
                if(action.SupportActors.Count==0)
                {
                    // Only throw-capable, actually gathered personnel enter the
                    // shared candidate list; distant watches do not hold it.
                    foreach(var member in active)
                    {
                        if(!AtPost(member) || member.Pawn.CurJob?.playerForced==true || SupportGrenade(member.Pawn,action.Outdoors,lethal)==null) continue;
                        int index=command.Members.IndexOf(member),at=0;
                        while(at<action.SupportActors.Count && plan.Stack[action.SupportActors[at]].DistanceToSquared(plan.Opening)
                            <=plan.Stack[index].DistanceToSquared(plan.Opening)) at++;
                        if(at>=4) continue;
                        action.SupportActors.Insert(at,index); if(action.SupportActors.Count>4) action.SupportActors.RemoveAt(4);
                    }
                    if(action.SupportActors.Count==0)
                    {
                        action.SupportSkip=TacticalSupportSkip.Formation; command.CqbWait=TacticalCqbWait.Roles;
                        if(tick-action.SupportStarted>180) SkipOpeningSupport(command,TacticalSupportSkip.NoGrenade);
                        return;
                    }
                }
                var budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                if (!budget.TryPlan(tick)) { action.SupportSkip = TacticalSupportSkip.Budget; command.Due = tick + 1; return; }
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    if (action.SupportSkip == TacticalSupportSkip.Budget) action.SupportSkip = TacticalSupportSkip.None;
                    if (!PrepareSupportRoom(command))
                    { action.SupportSkip = TacticalSupportSkip.RoomSnapshot; command.Due = tick + 1; return; }
                    int passSize = action.SupportActors.Count * action.SupportTargets.Count;
                    for (int tested = 0; tested < TacticalOpeningPolicy.ThrowCandidatesPerSlice && action.SupportCursor < passSize * 2; tested++)
                    {
                        int slot = action.SupportCursor++, index = action.SupportActors[slot % action.SupportActors.Count];
                        int preference = slot < passSize ? 2 : 0;
                        IntVec3 target = action.SupportTargets[(slot % passSize) / action.SupportActors.Count];
                        action.Target = target;
                        TacticalMemberCommand member = command.Members[index];
                        if (!active.Contains(member) || !AtPost(member) || member.Pawn.CurJob?.playerForced == true) continue;
                        IntVec3 returnPosition = plan.Stack[index];
                        Thing grenade = SupportGrenade(member.Pawn, action.Outdoors, lethal);
                        if (grenade == null || !action.Outdoors && TacticalCqbDoctrine.GrenadePreference(high, lethal, grenade.def.defName) < preference)
                        { action.SupportSkip = TacticalSupportSkip.NoGrenade; continue; }
                        action.GrenadeChecks++;
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
                                target, InventoryGrenadeUtility.ThrowRange, out _)) return false;
                            trajectory = true;
                            return SafeOpeningThrow(member.Pawn, grenade, cell, target, returnPosition);
                        });
                        if (trajectory) action.TrajectoryChecks++;
                        if (!throwPosition.IsValid)
                        {
                            if (trajectory) action.UnsafeChecks++;
                            action.SupportSkip = trajectory ? TacticalSupportSkip.UnsafeFriendly : TacticalSupportSkip.NoThrowPosition; continue;
                        }
                        if (!CanIssue(member)) { action.SupportCursor--; action.SupportSkip = TacticalSupportSkip.Budget; command.Due = tick + 1; return; }
                        Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalThrow"), target, grenade, throwPosition);
                        job.targetQueueA = new List<LocalTargetInfo> { returnPosition, plan.Opening };
                        job.canUseRangedWeapon = false;
                        action.Thrower = member.Pawn; action.ReturnPosition = returnPosition;
                        // StartJob may invoke the driver synchronously. Keep
                        // its action context ready, but commit only if admitted.
                        if (!Issue(member, job))
                        {
                            action.Thrower = null; action.ReturnPosition = IntVec3.Invalid;
                            action.SupportCursor--; action.SupportSkip = TacticalSupportSkip.Budget;
                            command.Due = tick + 1; return;
                        }
                        action.Source = throwPosition; action.ThrowIssued = true;
                        action.SupportSkip = TacticalSupportSkip.None; return;
                    }
                    if (action.SupportCursor < passSize * 2) { command.Due = tick + 1; return; }
                    action.SupportSkip = action.UnsafeChecks > 0 ? TacticalSupportSkip.UnsafeFriendly
                        : action.GrenadeChecks > 0 ? TacticalSupportSkip.NoThrowPosition : TacticalSupportSkip.NoGrenade;
                    SkipOpeningSupport(command, action.SupportSkip);
                }
                finally { budget.Account(tick, System.Diagnostics.Stopwatch.GetTimestamp() - started); }
            }
            if (command.Phase == TacticalCommandPhase.BlastWait)
            {
                if (SupportEffectsPending(command, tick)) { command.CqbWait=TacticalCqbWait.BlastSafety; SupportWaits++; return; }
                if (command.ReplanAfterSupport)
                {
                    ResetAfterSupport(command, tick);
                    return;
                }
                command.Phase = TacticalCommandPhase.Enter;
            }
        }

        private static void SkipOpeningSupport(TacticalSquadCommand command, TacticalSupportSkip reason)
        {
            command.OpeningAction.SupportSkip = reason;
            command.OpeningAction.Target = IntVec3.Invalid;
            command.Phase = TacticalCommandPhase.Enter;
        }

        private bool PrepareSupportRoom(TacticalSquadCommand command)
        {
            TacticalOpeningAction action = command.OpeningAction;
            if (action.SupportRoomReady) return true;
            if (action.Outdoors) { action.SupportRoomReady = true; return true; }
            if (action.SupportRoomSurvey == null)
            {
                TacticalLocalPlan plan = command.Plan;
                IntVec3 seed = action.SupportTargets[0];
                if (seed.GetEdifice(map) is Building_Door) seed += plan.Inward;
                bool roofed = seed.Roofed(map);
                bool Floor(IntVec3 cell)
                {
                    if (!cell.InBounds(map) || !cell.Walkable(map) || cell.Roofed(map) != roofed
                        || cell.GetEdifice(map) is Building_Door) return false;
                    for (int layer = 0; layer < plan.Depth; layer++) if (cell == plan.Opening + plan.Inward * layer) return false;
                    return true;
                }
                action.SupportRoomSurvey = new TacticalSupportRoomSurvey(seed,Floor);
            }
            if (!action.SupportRoomSurvey.Step()) return false;
            action.SupportRoomCells = action.SupportRoomSurvey.Cells;
            action.SupportRoomTruncated = action.SupportRoomSurvey.Truncated;
            action.SupportRoomReady = true; action.SupportRoomSurvey = null;
            return true;
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

        private static Thing SupportGrenade(Pawn pawn, bool smoke, bool lethal)
        {
            if (!InventoryGrenadeUtility.CanUseGrenades(pawn) || pawn.inventory == null) return null;
            return SelectSupportGrenade(pawn.inventory.innerContainer, pawn.Faction?.def, smoke, lethal);
        }
        private static Thing SelectSupportGrenade(IEnumerable<Thing> inventory, FactionDef faction, bool smoke, bool lethal)
        {
            bool high = faction?.defName == "HD_HelodCivilHighFaction";
            Thing selected = null; int best = -1;
            foreach (Thing item in inventory)
                if (InventoryGrenadeUtility.IsInventoryGrenade(item.def)
                    && TacticalGrenadePolicy.AllowsAutomatic(faction, item.def) && RaidSmokeUtility.IsSmoke(item) == smoke)
                {
                    int priority = smoke ? 0 : TacticalCqbDoctrine.GrenadePreference(high, lethal, item.def.defName);
                    if (priority > best) { selected = item; best = priority; }
                }
            return selected;
        }

        internal static bool SafeOpeningThrow(Pawn pawn, Thing grenade, IntVec3 position, IntVec3 target, IntVec3? retreat = null)
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
            float scatter = InventoryGrenadeUtility.ThrowMissRadius(pawn, position.DistanceTo(target));
            float radius = TacticalGrenadePolicy.HazardRadius(projectile,
                targetRoom != null && !targetRoom.UsesOutdoorTemperature) + scatter + .75f;
            bool throughCover = TacticalGrenadePolicy.DangerousThroughCover(projectile);
            Room RoomAt(IntVec3 cell) => cell.InBounds(pawn.Map) ? cell.GetRoom(pawn.Map) : null;
            var owner = pawn.Map.GetComponent<MapComponent_TacticalCommands>();
            TacticalSquadCommand command = null;
            TacticalLocalPlan opening = owner != null && owner.byPawn.TryGetValue(pawn, out command)
                && command.OpeningAction?.Target == target ? command.Plan : null;
            ISet<IntVec3> roomCells = opening != null && command.OpeningAction.SupportRoomReady
                && command.OpeningAction.SupportRoomCells.Count > 0 ? command.OpeningAction.SupportRoomCells : null;
            IntVec3 selfPosition = position;
            if (opening != null || retreat.HasValue)
            {
                IntVec3 returnPosition = retreat ?? command.OpeningAction.ReturnPosition;
                if (returnPosition.IsValid && returnPosition != position)
                {
                    bool Clear(IntVec3 cell) => cell.InBounds(pawn.Map) && cell.Standable(pawn.Map)
                        && (cell.GetFirstPawn(pawn.Map) == null || cell.GetFirstPawn(pawn.Map) == pawn);
                    if (!TacticalGrenadePolicy.CanRetreatBeforeFuse(projectile, position, returnPosition, pawn.TicksPerMoveCardinal, Clear)) return false;
                    selfPosition = returnPosition;
                }
            }
            foreach (Pawn ally in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (ally.Dead || ally.HostileTo(pawn)) continue;
                // The grenade has a fuse. Its thrower immediately returns to
                // known cover; treating its temporary lean/throw cell as its
                // explosion-time position vetoes otherwise safe CQB throws.
                IntVec3 cell = ally == pawn ? selfPosition : ally.Position;
                if (roofed && TacticalGrenadePolicy.FriendlyInRoom(targetRoom, cell,
                    cell.GetEdifice(pawn.Map) is Building_Door, RoomAt, opening, roomCells)) return false;
                if (cell.DistanceToSquared(target) <= radius * radius
                    && (cell.DistanceTo(target) <= scatter + .75f || throughCover
                        || GenSight.LineOfSight(target, cell, pawn.Map, true))) return false;
                if(ally!=pawn && ally.pather?.Moving==true)
                {
                    IntVec3 destination=ally.pather.Destination.Cell;
                    if(destination.IsValid && destination.InBounds(pawn.Map)
                        && (roofed && TacticalGrenadePolicy.FriendlyInRoom(targetRoom,destination,
                            destination.GetEdifice(pawn.Map) is Building_Door,RoomAt,opening,roomCells)
                            || destination.DistanceToSquared(target)<=radius*radius && (throughCover
                                || GenSight.LineOfSight(target,destination,pawn.Map,true)))) return false;
                }
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
                        if (roofed && TacticalGrenadePolicy.FriendlyInRoom(targetRoom,cell,false,RoomAt,opening,roomCells)
                            || cell.DistanceToSquared(target) <= radius * radius && GenSight.LineOfSight(target, cell, pawn.Map, true)) return false;
                }
            return true;
        }
    }
}
