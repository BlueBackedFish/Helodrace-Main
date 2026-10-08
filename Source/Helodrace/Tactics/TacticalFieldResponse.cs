using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalObservedMotion { Unknown, Stationary, Approaching, Crossing, Leaving }
    public enum TacticalFieldStage { Forming, Defending, Moving }

    // No enemy references, orders, threat grid or room graph. All predictions
    // are based on the two reported/observed value samples only.
    public static class TacticalFieldPolicy
    {
        public const int SightRadius = 80, FocusInterval = 90, MoveInterval = 180;
        public static TacticalObservedMotion Motion(TacticalContact contact, IntVec3 anchor)
        {
            if (contact == null || !contact.PreviousPosition.IsValid || contact.PreviousTick < 0
                || contact.SeenTick <= contact.PreviousTick
                || contact.SeenTick - contact.PreviousTick >= TacticalContactMemory.FreshTicks)
                return TacticalObservedMotion.Unknown;
            IntVec3 travel = contact.Position - contact.PreviousPosition;
            if (travel.LengthHorizontalSquared == 0) return TacticalObservedMotion.Stationary;
            IntVec3 toward = anchor - contact.PreviousPosition;
            long dot = (long)travel.x * toward.x + (long)travel.z * toward.z;
            if (dot * dot * 4 < (long)travel.LengthHorizontalSquared * toward.LengthHorizontalSquared)
                return TacticalObservedMotion.Crossing;
            return dot > 0 ? TacticalObservedMotion.Approaching : TacticalObservedMotion.Leaving;
        }
        public static IntVec3 Direction(IntVec3 from, IntVec3 to)
        {
            IntVec3 delta = to - from;
            return Math.Abs(delta.x) >= Math.Abs(delta.z)
                ? new IntVec3(Math.Sign(delta.x), 0, 0) : new IntVec3(0, 0, Math.Sign(delta.z));
        }
        public static IntVec3 Arc(IntVec3 anchor, IntVec3 forward, int slot, int count, int width = 6)
        {
            float u = count <= 1 ? 0 : 2f * slot / (count - 1) - 1f;
            IntVec3 side = new IntVec3(-forward.z, 0, forward.x);
            return anchor + side * (int)Math.Round(u * width)
                + forward * (int)Math.Round(1 - 3 * u * u);
        }
    }

    public sealed class TacticalFieldResponse
    {
        public int Started, LastSeen, EnemyId, FocusAt, Assigned, MovingTeam = -1, NextMove, MoveStarted;
        public TacticalFieldStage Stage;
        public TacticalObservedMotion Motion;
        public bool High;
        public IntVec3 Anchor, Focus, Forward;
        public readonly List<IntVec3> Posts = new List<IntVec3>();
        public readonly List<int> Order = new List<int>();
        public readonly HashSet<int> FireGroup = new HashSet<int>();
        public readonly HashSet<IntVec3> Occupied = new HashSet<IntVec3>();
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long FieldResponses, FieldResumes, FieldPostCandidates, FieldBounds, FieldGuardJobs;

        private bool AdvanceFieldResponse(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            // CQB explosives keep their own controller until safe. An indoor
            // contact or portal transit still belongs to the CQB response.
            if (command.ChargeAction != null || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared)
                return false;
            TacticalFieldResponse field = command.FieldResponse;
            if (field == null)
            {
                if (command.ContactResponse != null) return false;
                Pawn observer = active[active.Count / 2].Pawn;
                if (observer.Position.Roofed(map)) return false;
                TacticalContact nearest = null;
                foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                {
                    if (!TacticalContactMemory.Fresh(contact, tick) || contact.SeenTick <= command.ContactHandledAt
                        || contact.Door || contact.Position.Roofed(map)) continue;
                    if (nearest == null || observer.Position.DistanceToSquared(contact.Position)
                        < observer.Position.DistanceToSquared(nearest.Position)) nearest = contact;
                }
                if (nearest == null) return false;
                field = new TacticalFieldResponse { Started = tick, LastSeen = nearest.SeenTick, EnemyId = nearest.EnemyId,
                    Anchor = observer.Position, Focus = nearest.Position, FocusAt = tick, NextMove = tick + 180,
                    Forward = TacticalFieldPolicy.Direction(observer.Position, nearest.Position),
                    Motion = TacticalFieldPolicy.Motion(nearest, observer.Position),
                    High = command.Link.Unit.Faction.def.defName == "HD_HelodCivilHighFaction" };
                for (int i = 0; i < command.Members.Count; i++) field.Posts.Add(IntVec3.Invalid);
                // LOW forms one temporary group around an actual automatic
                // weapon role. This does not mutate the organization tree.
                for (int i = 0; i < command.Members.Count; i++)
                    if (command.Members[i].AutomaticWeapon && Available(command.Members[i], map)) field.Order.Add(i);
                for (int i = 0; i < command.Members.Count; i++) if (!field.Order.Contains(i)) field.Order.Add(i);
                for (int i = 0; i < Math.Min(4, field.Order.Count); i++) field.FireGroup.Add(field.Order[i]);
                command.FieldResponse = field; FieldResponses++;
                command.Link.Cooperation.LocalReady = false;
            }
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                if (contact.EnemyId == field.EnemyId && TacticalContactMemory.Fresh(contact, tick))
                {
                    if (contact.SeenTick > field.LastSeen) field.Motion = TacticalFieldPolicy.Motion(contact, field.Anchor);
                    field.LastSeen = Math.Max(field.LastSeen, contact.SeenTick);
                    if (tick - field.FocusAt >= TacticalFieldPolicy.FocusInterval)
                    {
                        field.Focus = contact.Position;
                        field.FocusAt = tick;
                    }
                }
            if (tick - field.LastSeen >= TacticalContactMemory.FreshTicks)
            { EndFieldResponse(command, tick); return false; }
            command.Due = Math.Min(command.Due, tick + 30);
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (field.Assigned < field.Order.Count && budget.TryPlan(tick))
            {
                long started = Stopwatch.GetTimestamp();
                try
                {
                    for (int count = 0; count < 2 && field.Assigned < field.Order.Count; count++)
                    {
                        int slot = field.Assigned++, index = field.Order[slot];
                        TacticalMemberCommand member = command.Members[index];
                        if (!Available(member, map)) continue;
                        IntVec3 desired = field.High
                            ? TacticalFieldPolicy.Arc(field.Anchor, field.Forward, slot, field.Order.Count, 8)
                            : slot < 4 ? TacticalFieldPolicy.Arc(field.Anchor, field.Forward, slot, Math.Min(4, field.Order.Count), 4)
                            : TacticalFieldPolicy.Arc(field.Anchor - field.Forward * 3, field.Forward,
                                slot - 4, field.Order.Count - 4, 5);
                        AssignFieldPost(command, member, field, index, desired);
                    }
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            int ready = 0, coverReady = 0, coverCount = 0, movers = 0, moversReady = 0;
            float range = 0;
            foreach (TacticalMemberCommand member in active)
            {
                int index = command.Members.IndexOf(member);
                IntVec3 post = field.Posts[index];
                bool at = post.IsValid && AtPost(member) && member.Pawn.Position == post;
                if (at) ready++;
                if (member.Fireteam == field.MovingTeam && field.MovingTeam >= 0)
                { movers++; if (at) moversReady++; }
                else { coverCount++; if (at) coverReady++; }
                range = Math.Max(range, member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps.range ?? 0);
                if (!post.IsValid) { EnsureParking(command, member, tick); continue; }
                if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalContactGuard"
                    && member.Job.targetA.Cell == post)
                { member.Job.targetB = field.Focus; continue; }
                if (member.Pawn.CurJob?.playerForced == true || tick < member.RetryTick || !CanIssue(member)) continue;
                if (Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), post, field.Focus))) FieldGuardJobs++;
            }
            if (field.Assigned < field.Order.Count) return true;
            if (field.Stage == TacticalFieldStage.Forming && ready == active.Count) field.Stage = TacticalFieldStage.Defending;
            if (field.Stage == TacticalFieldStage.Moving && (moversReady == movers || tick - field.MoveStarted >= 600))
            {
                field.Stage = TacticalFieldStage.Defending; field.NextMove = tick + TacticalFieldPolicy.MoveInterval;
            }
            // HIGH: only one real child team moves; the other teams must already
            // be at their guard posts. Never invent teams from pawn index/modulo.
            if (field.High && field.Stage == TacticalFieldStage.Defending && tick >= field.NextMove
                && ready == active.Count && coverReady == coverCount && field.Motion != TacticalObservedMotion.Approaching
                && field.Anchor.DistanceToSquared(field.Focus) > Math.Max(100, range * range * .64f))
                BeginFieldBound(command, active, field, tick);
            return true;
        }

        private void BeginFieldBound(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) return;
            int team = -1;
            for (int offset = 1; offset <= 3; offset++)
            {
                int candidate = (field.MovingTeam + offset) % 3;
                if (active.Exists(m => m.Fireteam == candidate)) { team = candidate; break; }
            }
            if (team < 0) { field.NextMove = tick + 600; return; }
            long started = Stopwatch.GetTimestamp();
            try
            {
                // A bounded team has four members in the current HIGH roster.
                // One small search per mover; no path request until job admission.
                int moved = 0;
                foreach (TacticalMemberCommand member in active)
                {
                    if (member.Fireteam != team || moved++ >= 4) continue;
                    int index = command.Members.IndexOf(member);
                    IntVec3 old = field.Posts[index];
                    if (!old.IsValid) continue;
                    AssignFieldPost(command, member, field, index, old + field.Forward * 5);
                }
                field.MovingTeam = team; field.Stage = TacticalFieldStage.Moving;
                field.MoveStarted = tick; FieldBounds++;
                // Anchor advances only after a full cover/move exchange.
                if (team == 2) field.Anchor += field.Forward * 5;
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }

        private void AssignFieldPost(TacticalSquadCommand command, TacticalMemberCommand member,
            TacticalFieldResponse field, int index, IntVec3 desired)
        {
            IntVec3 old = field.Posts[index];
            IntVec3 best = IntVec3.Invalid; float score = float.MinValue;
            // If a wall clips the bow template, search the member's own local
            // floor once instead of parking forever with an invalid assignment.
            for (int pass = 0; pass < 2 && !best.IsValid; pass++)
            for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
            {
                IntVec3 cell = (pass == 0 ? desired : member.Pawn.Position) + new IntVec3(x, 0, z); FieldPostCandidates++;
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.Roofed(map)
                    || field.Occupied.Contains(cell) && cell != old
                    || claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command) continue;
                Pawn occupant = cell.GetFirstPawn(map);
                if (occupant != null && occupant != member.Pawn) continue;
                if (command.Plan != null && !command.Plan.Direct)
                {
                    // No new field post may cross a committed CQB boundary or
                    // occupy its mandatory ingress cells.
                    TacticalLocalPlan plan = command.Plan;
                    int Depth(IntVec3 p) => (p.x - plan.Opening.x) * plan.Inward.x + (p.z - plan.Opening.z) * plan.Inward.z;
                    if ((Depth(cell) >= 1) != (Depth(member.Pawn.Position) >= 1)
                        || cell == plan.Opening || cell == plan.Outside || cell == plan.Inside || cell == plan.EntryLane) continue;
                }
                // Straight local floor is deliberately conservative. It avoids
                // native CanReach for 25 candidates and long wall detours.
                if (!GenSight.LineOfSight(member.Pawn.Position, cell, map, true,
                    p => p.Standable(map) && !(p.GetEdifice(map) is Building_Door))) continue;
                float cover = !GenSight.LineOfSight(field.Focus, cell, map, true) ? 1f
                    : CoverUtility.CalculateOverallBlockChance(cell, field.Focus, map);
                float value = cover * 16 - cell.DistanceToSquared(desired) * 2 - cell.DistanceToSquared(member.Pawn.Position) * .1f;
                if (value > score) { score = value; best = cell; }
            }
            if (!best.IsValid) return; // Keep the previous usable post.
            if (old.IsValid)
            {
                field.Occupied.Remove(old);
                if (!OriginalClaim(command.Plan, old) && claims.TryGetValue(old, out TacticalSquadCommand owner) && owner == command)
                    claims.Remove(old);
            }
            field.Posts[index] = best; field.Occupied.Add(best); claims[best] = command;
        }

        private void EndFieldResponse(TacticalSquadCommand command, int tick)
        {
            TacticalFieldResponse field = command.FieldResponse;
            if (field == null) return;
            command.PhaseStarted += tick - field.Started;
            foreach (IntVec3 cell in field.Occupied)
                if (!OriginalClaim(command.Plan, cell) && claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command)
                    claims.Remove(cell);
            foreach (TacticalMemberCommand member in command.Members)
            {
                member.Entered = member.EntryAssignmentDone = false; member.RetryTick = 0; member.LastProgressTick = tick;
            }
            if (command.OpeningAction != null)
            {
                command.OpeningAction = null;
                if (command.Phase == TacticalCommandPhase.Observe || command.Phase == TacticalCommandPhase.Support)
                    command.Phase = TacticalCommandPhase.Observe;
            }
            command.ContactRestoring = command.Phase == TacticalCommandPhase.Clear && command.Plan != null;
            command.ContactHandledAt = tick; command.FieldResponse = null; command.Due = tick + 1; FieldResumes++;
        }
    }
}
