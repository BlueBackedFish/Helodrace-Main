using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalExplosiveEvasionPolicy
    {
        public const string JobName = "HD_NewTacticalEvadeExplosion";
        public const float NotifyRadius = JobGiver_FleePotentialExplosion.FleeDist;
        public const int RecoveryCheckTicks = 90;
        public static int QuietUntil(int tick, bool engaged, bool field = false) => tick
            + (field ? 0 : engaged ? TacticalEntryCombatPolicy.QuietTicks : RecoveryCheckTicks);
        public static bool CanReturn(int tick, int quietUntil, bool checkedNow, bool engaged, bool busy) =>
            checkedNow && !engaged && !busy && tick >= quietUntil;
        public static IntVec3 ReturnPost(TacticalLocalPlan plan, TacticalCommandPhase phase, int index)
        {
            if (plan == null || index < 0 || phase == TacticalCommandPhase.Returning || phase == TacticalCommandPhase.Released)
                return IntVec3.Invalid;
            List<IntVec3> posts = TacticalEntryCombatPolicy.ArrivalPhase(phase) || phase == TacticalCommandPhase.Complete
                ? plan.Positions : plan.Stack;
            return index < posts.Count ? posts[index] : IntVec3.Invalid;
        }
        public static bool OwnsMovement(Job job) => job?.def?.defName == JobName;
        public static bool Controlled(TacticalSquadCommand command, Thing danger) => danger != null
            && (command.OpeningAction?.Projectile == danger || command.ChargeAction?.Charge?.parent == danger);
        public static bool Pending(Pawn pawn, TacticalSquadCommand command)
        {
            Thing danger = pawn.mindState?.knownExploder;
            return danger?.Spawned == true && danger.Map == pawn.Map && !pawn.Downed && !pawn.Drafted
                && !Controlled(command, danger) && !RaidSmokeUtility.IsScreeningProjectile(danger.def)
                && pawn.Position.DistanceToSquared(danger.Position) <= NotifyRadius * NotifyRadius
                && !PawnUtility.PlayerForcedJobNowOrSoon(pawn);
        }

        // Native full-distance fleeing may fail in a small room/corridor. Only
        // then find a connected local hiding/retreat cell, with fixed work caps.
        public static IntVec3 LocalFallback(IntVec3 origin, IntVec3 danger, Func<IntVec3, bool> floor,
            Func<IntVec3, bool> free, Func<IntVec3, bool> hidden)
        {
            var pending = new Queue<IntVec3>(); var seen = new HashSet<IntVec3> { origin };
            pending.Enqueue(origin);
            IntVec3 best = IntVec3.Invalid;
            int distance = origin.DistanceToSquared(danger), bestDistance = distance, sightProbes = 0;
            while (pending.Count > 0)
            {
                IntVec3 cell = pending.Dequeue();
                int away = cell.DistanceToSquared(danger);
                if (cell != origin && free(cell) && away > distance)
                {
                    if (away > NotifyRadius * NotifyRadius) return cell;
                    if (sightProbes++ < 8 && hidden(cell)) return cell;
                    if (away > bestDistance) { best = cell; bestDistance = away; }
                }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (seen.Count >= 96 || next.DistanceToSquared(origin) > 49 || !seen.Add(next)) continue;
                    if (floor(next)) pending.Enqueue(next);
                }
            }
            return best;
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        public int EvasionStarted = -1;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        internal int RecoveryQuietUntil(Pawn pawn, int tick, bool engaged) => TacticalExplosiveEvasionPolicy.QuietUntil(tick, engaged,
            byPawn.TryGetValue(pawn, out var command) && command.FieldResponse != null);
        internal bool CanNotifyExplosive(Pawn pawn, Thing danger) => byPawn.TryGetValue(pawn, out TacticalSquadCommand command)
            && !command.Terminal && !TacticalExplosiveEvasionPolicy.Controlled(command, danger);
        internal bool TryEvasionContext(Pawn pawn, out TacticalSquadCommand command) =>
            byPawn.TryGetValue(pawn, out command) && !command.Terminal;
        internal void AdoptEvasion(Pawn pawn, Job job)
        {
            if (pawn.CurJob != job || !TryEvasionContext(pawn, out TacticalSquadCommand command)) return;
            TacticalMemberCommand member = command.Members.Find(m => m.Pawn == pawn);
            if (member == null) return;
            member.RoomWatchUntil = 0;
            member.Job = job; member.RetryTick = 0;
            member.LastProgressTick = GenTicks.TicksGame; member.LastPosition = pawn.Position;
            // Interrupted unlaunched observation/preparation can be rebuilt.
            // Launched explosives retain their live action and safety record.
            if (command.OpeningAction != null && !command.OpeningAction.Launched
                && (command.OpeningAction.Observer == pawn || command.OpeningAction.Thrower == pawn))
            {
                command.OpeningAction = null;
                if (command.Phase == TacticalCommandPhase.Support) command.Phase = TacticalCommandPhase.Observe;
            }
            Wake(pawn);
        }
        private bool PauseForExplosiveEvasion(TacticalSquadCommand command, int tick)
        {
            foreach (TacticalMemberCommand member in command.Members)
                if ((member.Pawn?.mindState?.knownExploder != null || TacticalExplosiveEvasionPolicy.OwnsMovement(member.Pawn?.CurJob))
                    && member.Pawn.Spawned && member.Pawn.Map == map && !member.Pawn.Dead && !member.Pawn.Downed
                    && (TacticalExplosiveEvasionPolicy.OwnsMovement(member.Pawn.CurJob)
                        || TacticalExplosiveEvasionPolicy.Pending(member.Pawn, command)))
                {
                    if (command.EvasionStarted < 0) command.EvasionStarted = tick;
                    command.Due = tick + 15; return true;
                }
            if (command.EvasionStarted >= 0)
            {
                int duration = Math.Max(0, tick - command.EvasionStarted);
                command.PhaseStarted += duration;
                if (command.ContactResponse != null) command.ContactResponse.Started += duration;
                if (command.FieldResponse != null)
                {
                    command.FieldResponse.Started += duration;
                    if (command.FieldResponse.Maneuver != null)
                    { command.FieldResponse.Maneuver.Started += duration; command.FieldResponse.Maneuver.PhaseAt += duration; }
                }
                if (command.FocusFire != null) command.FocusFire.Started += duration;
                command.EvasionStarted = -1;
            }
            return false;
        }
    }

    public sealed class JobDriver_TacticalEvadeExplosion : TacticalJobDriver
    {
        private int nextCombatCheck, recoveryUntil = -1;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, job.targetA.Cell);
            return true;
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            MapComponent_TacticalCommands owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            owner?.AdoptEvasion(pawn, job);
            BindOwner(); job.canUseRangedWeapon = false;
            // The native giver already chose a flee destination. Keep that path
            // and stay there until this explosive disappears; do not rush back
            // into the old formation while its fuse is still running.
            yield return AdmitMovement();
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil hold = Hold(job.targetB.Cell);
            hold.tickAction = () => { if (job.targetB.Thing?.Spawned != true) ReadyForNextToil(); };
            yield return hold;
            // Despawning starts the explosion. Leave time for its propagation
            // and fragments instead of immediately returning into that effect.
            yield return Toils_General.Wait(TacticalOpeningPolicy.SettleTicks);
            Toil recovery = Hold(job.targetC.IsValid ? job.targetC.Cell : pawn.Position);
            recovery.initAction += () =>
            {
                recoveryUntil = owner?.RecoveryQuietUntil(pawn, GenTicks.TicksGame, false)
                    ?? TacticalExplosiveEvasionPolicy.QuietUntil(GenTicks.TicksGame, false);
                nextCombatCheck = 0;
            };
            bool CheckCombat(int tick, out bool engaged)
            {
                engaged = false;
                if (tick < nextCombatCheck) return false;
                bool checkedNow = owner == null || owner.TryRecoveryFire(pawn, job, tick, out engaged);
                nextCombatCheck = tick + (checkedNow ? 30 + pawn.thingIDNumber % 7 : 3 + pawn.thingIDNumber % 3);
                if (engaged) recoveryUntil = owner?.RecoveryQuietUntil(pawn, tick, true)
                    ?? TacticalExplosiveEvasionPolicy.QuietUntil(tick, true);
                return checkedNow;
            }
            recovery.tickAction = () =>
            {
                int tick = GenTicks.TicksGame;
                bool checkedNow = CheckCombat(tick, out bool engaged);
                if (TacticalExplosiveEvasionPolicy.CanReturn(tick, recoveryUntil, checkedNow, engaged, pawn.stances.FullBodyBusy))
                    ReadyForNextToil();
            };
            yield return recovery;
            if (!job.targetC.IsValid) yield break;
            Toil returnGate = AdmitMovement();
            Action admitReturn = returnGate.tickAction;
            returnGate.tickAction = () =>
            {
                if (CheckCombat(GenTicks.TicksGame, out bool engaged) && engaged) JumpToToil(recovery);
                else admitReturn();
            };
            yield return returnGate;
            Toil returning = Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            returning.AddPreInitAction(() => AtPost = false);
            returning.AddPreTickAction(() =>
            {
                // Keep one owned job while switching between return and fire.
                // A newly visible target stops the path before casting; jumping
                // to the hold does not restart the job/cancel its gun warmup.
                if (CheckCombat(GenTicks.TicksGame, out bool engaged) && engaged) JumpToToil(recovery);
            });
            yield return returning;
            Toil arrival = Hold(job.targetC.Cell);
            arrival.initAction += () => nextCombatCheck = 0;
            arrival.tickAction = () =>
            {
                if (!CheckCombat(GenTicks.TicksGame, out bool engaged)) return;
                if (engaged) JumpToToil(recovery);
                else if (!pawn.stances.FullBodyBusy) ReadyForNextToil();
            };
            yield return arrival;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref recoveryUntil, "evasionRecoveryUntil", -1);
        }
    }

    [LegacyEngine, HarmonyPatch(typeof(JobGiver_FleePotentialExplosion), "TryGiveJob")]
    public static class Patch_NewTactical_KeepExplosiveEvasion
    {
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            // This native node runs for every humanlike pawn. The common
            // no-danger case must not query a map component or squad registry.
            if (pawn.mindState?.knownExploder == null || pawn.Downed) return true;
            MapComponent_TacticalCommands owner = pawn.Map?.GetComponent<MapComponent_TacticalCommands>();
            if (owner?.TryEvasionContext(pawn, out TacticalSquadCommand command) != true) return true;
            Thing danger = pawn.mindState?.knownExploder;
            if (TacticalExplosiveEvasionPolicy.Controlled(command, danger)
                || TacticalExplosiveEvasionPolicy.OwnsMovement(pawn.CurJob) && pawn.CurJob.targetB.Thing == danger)
            { __result = null; return false; }
            return true;
        }
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null && pawn.mindState?.knownExploder == null || pawn.Downed) return;
            if (pawn.Map?.GetComponent<MapComponent_TacticalCommands>()?.TryEvasionContext(pawn, out TacticalSquadCommand command) != true) return;
            Job native = __result;
            IntVec3 destination;
            if (native != null) destination = native.targetA.Cell;
            else
            {
                if (TacticalExplosiveEvasionPolicy.OwnsMovement(pawn.CurJob)
                    || !TacticalExplosiveEvasionPolicy.Pending(pawn, command)) return;
                Thing danger = pawn.mindState.knownExploder;
                bool Floor(IntVec3 cell) => cell.InBounds(pawn.Map) && cell.Standable(pawn.Map)
                    && (!(cell.GetEdifice(pawn.Map) is Building_Door door) || door.Open);
                bool Free(IntVec3 cell) => cell.GetFirstPawn(pawn.Map) == null;
                destination = TacticalExplosiveEvasionPolicy.LocalFallback(pawn.Position, danger.Position, Floor, Free,
                    cell => !GenSight.LineOfSightToThing(cell, danger, pawn.Map, true));
                if (!destination.IsValid) return;
            }
            __result = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(TacticalExplosiveEvasionPolicy.JobName),
                destination, pawn.mindState.knownExploder);
            __result.targetC = TacticalExplosiveEvasionPolicy.ReturnPost(command.Plan, command.Phase,
                command.Members.FindIndex(member => member.Pawn == pawn));
            __result.locomotionUrgency = LocomotionUrgency.Sprint;
            if (native != null) JobMaker.ReturnToPool(native);
        }
    }

    [LegacyEngine, HarmonyPatch(typeof(GenExplosion), nameof(GenExplosion.NotifyNearbyPawnsOfDangerousExplosive))]
    public static class Patch_NewTactical_NoticeHostileGrenade
    {
        public static void Postfix(Thing exploder, DamageDef damage, Faction onlyFaction)
        {
            // Native grenade Impact warns only the launcher's faction. Extend
            // this event to nearby tactical enemies who can actually see it.
            // No periodic projectile search or whole-map pawn scan.
            if (!(exploder is Projectile) || !exploder.Spawned || onlyFaction == null
                || RaidSmokeUtility.IsScreeningProjectile(exploder.def)) return;
            Map map = exploder.Map;
            MapComponent_TacticalCommands owner = map.GetComponent<MapComponent_TacticalCommands>();
            if (owner == null || owner.OwnedPawnCount == 0) return;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(exploder.Position, TacticalExplosiveEvasionPolicy.NotifyRadius, true))
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                // Starting a job can drop a carried item into this same list.
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (i >= things.Count) continue;
                    Thing thing = things[i];
                    if (!(thing is Pawn pawn) || pawn.Faction == onlyFaction || pawn.Dead || pawn.Downed || pawn.Drafted
                        || pawn.CurJob?.playerForced == true || (int)pawn.RaceProps.intelligence < 2
                        || damage?.ExternalViolenceFor(pawn) != true || !owner.CanNotifyExplosive(pawn, exploder)
                        || !GenSight.LineOfSightToThing(pawn.Position, exploder, map, true)) continue;
                    pawn.mindState.knownExploder = exploder;
                    // Native constant-tree checks are already staggered over
                    // 30 ticks. Do not run a whole squad's flee searches here.
                    if (pawn.jobs.curDriver is JobDriver_TacticalPost) pawn.pather.StopDead();
                    owner.Wake(pawn);
                }
            }
        }
    }
}
