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
        // Old value-only sightings may nominate a cell during recovery, but
        // its live occupant/identity and weapon LOS must still pass below.
        public static bool RecheckKnown(TacticalContact contact, int tick, bool recovery) => contact != null
            && contact.SeenTick <= tick && tick - contact.SeenTick
                < (recovery ? TacticalContactMemory.RetentionTicks : TacticalContactMemory.FreshTicks);
    }

    public static class TacticalEntryCombatPolicy
    {
        public const int QuietTicks = 180, CandidateLimit = 8, CoverCells = 13;
        public static bool ArrivalPhase(TacticalCommandPhase phase) => phase == TacticalCommandPhase.Enter || phase == TacticalCommandPhase.Clear;
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
        public int EntryCombatUntil, EntryCandidateCursor;
        public IntVec3 EntryCombatFocus = IntVec3.Invalid;
    }

    public sealed partial class TacticalLocalPlan
    {
        public HashSet<int> EntryCoverChecked = new HashSet<int>();
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        internal void NotifyEntryDamage(Pawn pawn, Job job, Pawn attacker)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)
                || !TacticalEntryCombatPolicy.ArrivalPhase(command.Phase) || pawn.CurJob != job
                || command.Members.Find(m => m.Pawn == pawn)?.Job != job || !attacker.HostileTo(pawn)) return;
            // Damage is real evidence, but does not reveal the shooter's position.
            command.EntryCombatUntil = Math.Max(command.EntryCombatUntil, GenTicks.TicksGame + TacticalEntryCombatPolicy.QuietTicks);
        }

        // False means only budget denial: the driver retries a few ticks later.
        internal bool TryEntryFire(Pawn pawn, Job job, int tick) => TryPostFire(pawn, job, tick, recovery: false, out _);
        internal bool TryRecoveryFire(Pawn pawn, Job job, int tick, out bool engaged) =>
            TryPostFire(pawn, job, tick, recovery: true, out engaged);
        private bool TryPostFire(Pawn pawn, Job job, int tick, bool recovery, out bool engaged)
        {
            engaged = false;
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command) || command.Plan == null
                || !(recovery ? TacticalExplosiveEvasionPolicy.OwnsMovement(job) : TacticalEntryCombatPolicy.ArrivalPhase(command.Phase)) || pawn.CurJob != job
                || command.Members.Find(m => m.Pawn == pawn)?.Job != job) return true;
            if (pawn.stances.FullBodyBusy)
            {
                if (pawn.stances.curStance is Stance_Busy busy && busy.focusTarg.Thing is Pawn enemy
                    && !enemy.Dead && !enemy.Downed && enemy.HostileTo(pawn))
                {
                    engaged = true;
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
                    if (enemy?.thingIDNumber == contact.EnemyId && EntryFireTarget(pawn, enemy, verb, radius))
                    { target = enemy; break; }
                }
                if (target == null)
                {
                    // Incremental actual-LOS acquisition, not an unrestricted
                    // per-pawn scan. One global observation slice per tick.
                    var pawns = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < Math.Min(TacticalEntryCombatPolicy.CandidateLimit, pawns.Count); i++)
                    {
                        if (command.EntryCandidateCursor >= pawns.Count) command.EntryCandidateCursor = 0;
                        Pawn enemy = pawns[command.EntryCandidateCursor++];
                        if (EntryFireTarget(pawn, enemy, verb, radius)) { target = enemy; break; }
                    }
                }
                if (target == null) return true;
                engaged = true;
                if (recovery) pawn.pather.StopDead();
                command.EntryCombatFocus = target.Position;
                command.EntryCombatUntil = Math.Max(command.EntryCombatUntil, tick + TacticalEntryCombatPolicy.QuietTicks);
                bool ranged = TacticalThreatEligibility.Ranged(target);
                if (ranged) RecordContact(command, target.thingIDNumber, target.Position, pawn.Position, tick);
                else ObserveMeleeThreat(command, target, pawn, tick, ranged: false);
                pawn.rotationTracker.FaceCell(target.Position);
                if (pawn.Position.DistanceToSquared(target.Position) <= 2 && pawn.meleeVerbs.TryMeleeAttack(target)) return true;
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
                if (arrived && !searched && !plan.EntryCoverChecked.Contains(index)
                    && command.EntryCombatFocus.IsValid && TacticalEntryCombatPolicy.Engaged(command.EntryCombatUntil, tick))
                {
                    TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
                    if (budget.TryPlan(tick))
                    {
                        long started = Stopwatch.GetTimestamp();
                        try
                        {
                            searched = true; plan.EntryCoverChecked.Add(index);
                            IntVec3 old = plan.Positions[index];
                            bool roofed = old.Roofed(map);
                            bool Floor(IntVec3 cell) => cell.InBounds(map) && cell.Walkable(map)
                                && !(cell.GetEdifice(map) is Building_Door) && cell.Roofed(map) == roofed
                                && (cell.x - plan.Opening.x) * plan.Inward.x + (cell.z - plan.Opening.z) * plan.Inward.z >= plan.Depth;
                            bool Free(IntVec3 cell) => cell != plan.Inside && cell != plan.EntryLane && cell.Standable(map)
                                && !claims.ContainsKey(cell) && !plan.Positions.Contains(cell) && cell.GetFirstPawn(map) == null;
                            Pawn enemy = command.EntryCombatFocus.GetFirstPawn(map);
                            Verb gun = member.Pawn.equipment?.PrimaryEq?.PrimaryVerb;
                            float Cover(IntVec3 cell)
                            {
                                float value = CoverUtility.CalculateOverallBlockChance(cell, command.EntryCombatFocus, map);
                                // Do not trade a firing post for complete hiding
                                // behind a wall when its actual target is still known.
                                if (value > 0f && enemy != null && gun != null && !gun.CanHitTargetFrom(cell, enemy)) return 0f;
                                return value;
                            }
                            IntVec3 cover = TacticalEntryCombatPolicy.FindCover(old, Floor, Free,
                                Cover);
                            if (cover != old)
                            {
                                plan.Positions[index] = cover; plan.Interior.Add(cover); claims[cover] = command;
                                if (!OriginalClaim(command, old) && claims.TryGetValue(old, out TacticalSquadCommand owner) && owner == command)
                                    claims.Remove(old);
                                member.EntryAssignmentDone = false;
                            }
                        }
                        finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                    }
                }
                if (member.Pawn.CurJob == member.Job && member.Pawn.jobs.curDriver is TacticalJobDriver current
                    && current.AtPost && member.Pawn.Position == plan.Positions[index]
                    && (current is JobDriver_TacticalIngress || member.Job.def.defName == "HD_NewTacticalPost" && member.Job.count == 1)) continue;
                EnsurePost(member, plan.Positions[index], command.EntryCombatFocus.IsValid ? command.EntryCombatFocus : command.Goal,
                    tick, entryCombat: true);
            }
        }
    }
}
