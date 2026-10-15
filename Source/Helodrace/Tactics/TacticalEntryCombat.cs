using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalPostFirePolicy
    {
        public static bool IndividualContact(TacticalSquadCommand command, bool indoorDirect = false) =>
            !command.Defensive && command.Plan != null && (!command.Plan.Direct || indoorDirect)
                && command.Phase != TacticalCommandPhase.Pending && command.Phase != TacticalCommandPhase.Returning
                && command.Phase != TacticalCommandPhase.Released;
        public static bool SafeToFire(TacticalSquadCommand command) => !command.Terminal
            && command.Phase != TacticalCommandPhase.BlastWait && (command.ChargeAction == null || command.ChargeAction.EffectsCleared)
            && !(command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared);
        public static int CheckDelay(bool checkedNow, int pawnId) => checkedNow ? 15 + pawnId % 7 : 3 + pawnId % 3;
        public static bool CanStop(TacticalLocalPlan plan, IntVec3 cell) => plan.Direct
            || cell != plan.Opening && cell != plan.Outside && cell != plan.Inside && cell != plan.EntryLane;
        // Old value-only sightings may nominate a cell during recovery, but
        // its live occupant/identity and weapon LOS must still pass below.
        public static bool RecheckKnown(TacticalContact contact, int tick, bool recovery) => contact != null
            && contact.SeenTick <= tick && tick - contact.SeenTick
                < (recovery ? TacticalContactMemory.RetentionTicks : TacticalContactMemory.FreshTicks);
    }

    public static class TacticalEntryCombatPolicy
    {
        public const int QuietTicks = 90, CandidateLimit = 8, CoverCells = 13;
        public static bool ArrivalPhase(TacticalCommandPhase phase) => phase == TacticalCommandPhase.Enter || phase == TacticalCommandPhase.Clear;
        public static bool CommittedIngress(TacticalSquadCommand command) => !command.Defensive
            && command.Phase == TacticalCommandPhase.Enter && command.Plan != null && !command.Plan.Direct;
        public static bool Engaged(int until, int tick) => tick < until;

        // A connected diamond of at most 13 floor cells. No pathfinder or Room
        // lookup; an obstructed connecting cell cannot be crossed by the search.
        public static IntVec3 FindCover(IntVec3 origin, Func<IntVec3, bool> floor,
            Func<IntVec3, bool> free, Func<IntVec3, float> cover)
        {
            var cells = new IntVec3[CoverCells];
            int count = 1; cells[0] = origin;
            IntVec3 best = origin;
            float score = cover(origin);
            for (int cursor = 0; cursor < count; cursor++)
            {
                IntVec3 cell = cells[cursor];
                if (cell != origin && free(cell))
                {
                    float value = cover(cell) - .04f * origin.DistanceToSquared(cell);
                    if (value > score + .05f) { best = cell; score = value; }
                }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (Math.Abs(next.x - origin.x) + Math.Abs(next.z - origin.z) > 2) continue;
                    bool visited = false;
                    for (int i = 0; i < count; i++) if (cells[i] == next) { visited = true; break; }
                    if (!visited && floor(next) && count < CoverCells) cells[count++] = next;
                }
            }
            return best;
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        public int EntryCombatUntil;
        public IntVec3 EntryCombatFocus = IntVec3.Invalid;
    }

    public sealed partial class TacticalMemberCommand
    {
        public int FireCandidateCursor;
    }

    internal enum TacticalPostFireMode { Arrival, Recovery, Stack, Movement }

    public sealed partial class TacticalLocalPlan
    {
        public HashSet<int> EntryCoverChecked = new HashSet<int>();
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private bool AdvanceCommittedIngress(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            // Root reactions must not suspend the scheduler of an entering
            // HIGH child. The child uses the same exclusive execution below.
            bool children = command.CqbTeams.Exists(TacticalEntryCombatPolicy.CommittedIngress);
            if (!children && !TacticalEntryCombatPolicy.CommittedIngress(command)) return false;
            PrepareCommittedIngress(command,tick);
            if (children) { AdvanceHighCqb(command,active,tick); return true; }
            AdvanceEntryPosts(command,active,tick);
            AdvanceAssault(command,active,tick);
            return true;
        }
        private void PrepareCommittedIngress(TacticalSquadCommand command, int tick)
        {
            // Keep contact knowledge and actual crossing progress. Only remove
            // competing squad controllers; owned ingress jobs continue intact.
            if (command.ContactResponse != null)
            {
                foreach (IntVec3 cell in command.ContactResponse.Occupied)
                    if (!OriginalClaim(command,cell) && claims.TryGetValue(cell,out var owner) && owner == command) claims.Remove(cell);
                command.ContactResponse = null; ContactResumes++;
            }
            command.ContactRestoring = false;
            FinishFocusPause(command,tick);
            if (command.FieldResponse != null) EndFieldResponse(command,tick,preserveOpening:true);
            if (command.MedicalCare != null) command.MedicalCare.CancelRequested = true;
        }
        internal void NotifyEntryDamage(Pawn pawn, Job job, Pawn attacker)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)
                || !TacticalEntryCombatPolicy.ArrivalPhase(command.Phase) || pawn.CurJob != job
                || command.Members.Find(m => m.Pawn == pawn)?.Job != job || !attacker.HostileTo(pawn)) return;
            // Damage is real evidence, but does not reveal the shooter's position.
            command.EntryCombatUntil = Math.Max(command.EntryCombatUntil, GenTicks.TicksGame + TacticalEntryCombatPolicy.QuietTicks);
        }

        // False means only budget denial: the driver retries a few ticks later.
        internal bool IndividualContact(TacticalSquadCommand command) => TacticalPostFirePolicy.IndividualContact(command,
            command.Plan?.Direct == true && command.Plan.Opening.InBounds(map) && command.Plan.Opening.Roofed(map));
        internal bool TryEntryFire(Pawn pawn, Job job, int tick) => TryPostFire(pawn, job, tick, TacticalPostFireMode.Arrival, out _);
        internal bool TryStackFire(Pawn pawn, Job job, int tick) => TryPostFire(pawn, job, tick, TacticalPostFireMode.Stack, out _);
        internal bool TryMovementFire(Pawn pawn, Job job, int tick, out bool engaged) =>
            TryPostFire(pawn, job, tick, TacticalPostFireMode.Movement, out engaged);
        internal bool TryRecoveryFire(Pawn pawn, Job job, int tick, out bool engaged) =>
            TryPostFire(pawn, job, tick, TacticalPostFireMode.Recovery, out engaged);
        private bool TryPostFire(Pawn pawn, Job job, int tick, TacticalPostFireMode mode, out bool engaged)
        {
            engaged = false;
            bool recovery = mode == TacticalPostFireMode.Recovery;
            bool local = mode == TacticalPostFireMode.Stack || mode == TacticalPostFireMode.Movement;
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.Plan == null
                || job.playerForced
                || !(recovery ? TacticalExplosiveEvasionPolicy.OwnsMovement(job) : local ? IndividualContact(command)
                    : TacticalEntryCombatPolicy.ArrivalPhase(command.Phase)
                    || command.CqbParent != null && command.Phase == TacticalCommandPhase.Complete
                    || !TacticalPostFirePolicy.SafeToFire(command) && RearFireRole(command,pawn)) || pawn.CurJob != job) return true;
            TacticalMemberCommand member = command.Members.Find(m => m.Pawn == pawn);
            if (member?.Job != job || mode == TacticalPostFireMode.Movement && !TacticalPostFirePolicy.CanStop(command.Plan, pawn.Position)) return true;
            if(!TacticalPostFirePolicy.SafeToFire(command) && !RearFireRole(command,pawn))
            { if(pawn.stances.curStance is Stance_Warmup) pawn.stances.CancelBusyStanceSoft(); return true; }
            if (mode == TacticalPostFireMode.Movement)
                foreach (Thing thing in pawn.Position.GetThingList(map))
                    if (thing is Pawn other && other != pawn && !other.Dead && !other.Downed && !other.HostileTo(pawn))
                        return true; // Followers pass the shooter instead of piling onto its firing tile.
            if (pawn.stances.FullBodyBusy)
            {
                if(pawn.stances.curStance is Stance_Warmup warmup && warmup.focusTarg.Thing is Pawn aimed
                    && (OpeningOwnsShot(pawn,aimed.Position) || !CqbShotLineAllowed(command,pawn,warmup.verb,aimed)))
                { pawn.stances.CancelBusyStanceSoft(); return true; }
                if (pawn.stances.curStance is Stance_Busy busy && busy.focusTarg.Thing is Pawn enemy
                    && !enemy.Dead && !enemy.Downed && enemy.HostileTo(pawn) && !OpeningOwnsShot(pawn, enemy.Position)
                    && CqbShotLineAllowed(command,pawn,busy.verb,enemy))
                {
                    engaged = true;
                    member.LastProgressTick = tick;
                    if (TacticalEntryCombatPolicy.ArrivalPhase(command.Phase))
                        command.EntryCombatUntil = Math.Max(command.EntryCombatUntil, tick + TacticalEntryCombatPolicy.QuietTicks);
                }
                return true;
            }
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (verb == null || verb.IsMeleeAttack) return true;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryObserve(tick)) return false;
            long started = Stopwatch.GetTimestamp();
            try
            {
                int radius = recovery && !pawn.Position.Roofed(map) ? TacticalFieldPolicy.SightRadius : TacticalContactState.Radius;
                Pawn target = null;
                foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                {
                    if (!TacticalPostFirePolicy.RecheckKnown(contact, tick, recovery)) continue;
                    Pawn enemy = contact.Position.GetFirstPawn(map);
                    if (enemy?.thingIDNumber == contact.EnemyId && !OpeningOwnsShot(pawn, enemy.Position)
                        && CqbShotAllowed(command,pawn,enemy.Position) && EntryFireTarget(pawn, enemy, verb, radius))
                    { target = enemy; break; }
                }
                if (target == null)
                {
                    // Incremental actual-LOS acquisition, not an unrestricted
                    // per-pawn scan. One global observation slice per tick.
                    var pawns = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < Math.Min(TacticalEntryCombatPolicy.CandidateLimit, pawns.Count); i++)
                    {
                        if (member.FireCandidateCursor >= pawns.Count) member.FireCandidateCursor = 0;
                        Pawn enemy = pawns[member.FireCandidateCursor++];
                        if (!OpeningOwnsShot(pawn, enemy.Position) && CqbShotAllowed(command,pawn,enemy.Position)
                            && EntryFireTarget(pawn, enemy, verb, radius)) { target = enemy; break; }
                    }
                }
                if (target == null) return true;
                bool ranged = TacticalThreatEligibility.Ranged(target);
                if (ranged) RecordContact(command, target.thingIDNumber, target.Position, pawn.Position, tick);
                else ObserveMeleeThreat(command, target, pawn, tick, ranged: false);
                // A target-side sighting can create the observation/grenade
                // action above. Rear/side watches fire; that action still owns
                // retreating from the opening rather than holding a gunfight.
                if (OpeningOwnsShot(pawn, target.Position) || !CqbShotLineAllowed(command,pawn,verb,target)) return true;
                engaged = true;
                member.LastProgressTick = tick;
                if (recovery || mode == TacticalPostFireMode.Movement) pawn.pather.StopDead();
                if (TacticalEntryCombatPolicy.ArrivalPhase(command.Phase))
                {
                    command.EntryCombatFocus = target.Position;
                    command.EntryCombatUntil = Math.Max(command.EntryCombatUntil, tick + TacticalEntryCombatPolicy.QuietTicks);
                }
                pawn.rotationTracker.FaceCell(target.Position);
                if (pawn.Position.DistanceToSquared(target.Position) <= 2 && pawn.meleeVerbs.TryMeleeAttack(target)) return true;
                verb=SelectUnderbarrel(pawn,target,verb,ownsObservation:true);
                if (verb.Available() && verb.TryStartCastOn(target)) ContactShot(pawn);
                return true;
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }

        private static bool EntryFireTarget(Pawn pawn, Pawn enemy, Verb verb, int radius)
        {
            if (enemy == null || enemy.Dead || enemy.Downed || !enemy.HostileTo(pawn)
                || pawn.Position.DistanceToSquared(enemy.Position) > radius * radius) return false;
            // Harmless hostile animals are not ranged contacts. An actual melee
            // attack against this faction is still a valid close-combat target.
            bool attacking = enemy.CurJobDef == JobDefOf.AttackMelee
                && enemy.CurJob?.targetA.Thing is Pawn victim && victim.Faction == pawn.Faction;
            bool meleeCombatant = !enemy.WorkTagIsDisabled(WorkTags.Violent)
                && (enemy.RaceProps.Humanlike || enemy.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack == true);
            return (attacking || meleeCombatant || TacticalThreatEligibility.Ranged(enemy)) && verb.CanHitTarget(enemy);
        }

        private void AdvanceEntryPosts(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalLocalPlan plan = command.Plan;
            if (plan == null || !TacticalEntryCombatPolicy.ArrivalPhase(command.Phase)) return;
            bool searched = false;
            foreach (TacticalMemberCommand member in active)
            {
                int index = command.Members.IndexOf(member);
                if (index >= plan.Positions.Count || plan.RetainedOutside.Contains(index)) continue;
                bool arrived = member.Pawn.CurJob == member.Job && member.Pawn.jobs.curDriver is TacticalJobDriver driver
                    && driver.AtPost && member.Pawn.Position == plan.Positions[index];
                if (arrived && (plan.Direct || member.Crossed && member.Passed)) member.Entered = member.EverEntered = true;
                if (!member.Entered || member.Pawn.stances.FullBodyBusy) continue;
                if (arrived && !searched) searched=TryLocalEntryCover(command,member,index,tick);
                if (member.Pawn.CurJob == member.Job && member.Pawn.jobs.curDriver is TacticalJobDriver current
                    && current.AtPost && member.Pawn.Position == plan.Positions[index]
                    && (current is JobDriver_TacticalIngress || current is JobDriver_TacticalActor
                        || member.Job.def.defName == "HD_NewTacticalPost" && member.Job.count == 1)) continue;
                EnsurePost(member, plan.Positions[index], command.EntryCombatFocus.IsValid ? command.EntryCombatFocus : command.Goal,
                    tick, entryCombat: true);
            }
        }
    }
}
