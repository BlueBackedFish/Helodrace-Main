using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalObservedMotion { Unknown, Stationary, Approaching, Crossing, Leaving }
    public enum TacticalFieldStage { Forming, Defending, Screening, Moving }

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
        public const int FirePointRadius = 2, FirePostEvaluationLimit = 16;
        private static uint Mix(int seed)
        {
            uint value = unchecked((uint)seed);
            value ^= value >> 16; value *= 0x7feb352d;
            value ^= value >> 15; value *= 0x846ca68b;
            return value ^ (value >> 16);
        }
        // Three area centers, not a slot for each pawn. The recessed base and
        // advanced lateral wings create converging fields of fire.
        public static IntVec3 FirePoint(IntVec3 anchor, IntVec3 focus, IntVec3 forward, int point, int seed)
        {
            IntVec3 side = new IntVec3(-forward.z, 0, forward.x);
            int width = 7 + (int)(Mix(seed) % 3);
            int depth = point == 1 ? -4 - (int)(Mix(seed + 1) % 2) : 4 + (int)(Mix(seed + point + 2) % 2);
            int enemyDepth = (focus.x - anchor.x) * forward.x + (focus.z - anchor.z) * forward.z;
            if (point != 1) depth = Math.Min(depth, Math.Max(0, enemyDepth - 3));
            return anchor + side * ((point - 1) * width) + forward * depth;
        }
        public static int FirePointIndex(bool high, int fireteam, bool core, int order) => high
            ? fireteam >= 0 && fireteam < 3 ? fireteam : 1
            : core ? 1 : order % 2 == 0 ? 0 : 2;
        public static IntVec3 Scatter(IntVec3 center, int pawnId, int seed)
        {
            uint bits = Mix(unchecked(pawnId * 397 ^ seed));
            uint span = 2 * FirePointRadius + 1;
            return center + new IntVec3((int)(bits % span) - FirePointRadius, 0, (int)((bits / span) % span) - FirePointRadius);
        }
        public static float FirePostScore(bool canFire, float cover, int scatterDistance, int travelDistance, int seed) =>
            (canFire ? 24f : 0f) + cover * 8f - scatterDistance * .4f - travelDistance * .03f
                + (Mix(seed) % 1000) * .004f;
        public static IntVec3 AdvanceFirePoint(IntVec3 point, IntVec3 forward, IntVec3 focus)
        {
            int depth = (focus.x - point.x) * forward.x + (focus.z - point.z) * forward.z;
            return point + forward * Math.Max(0, Math.Min(5, depth - 3));
        }
        public static int NextMover(int previous, int liveTeams, bool reserves)
        {
            if (liveTeams == 0) return reserves && previous != -2 ? -2 : -3;
            for (int step = 1; step <= 3; step++)
            {
                int team = (Math.Max(-1, previous) + step) % 3;
                if ((liveTeams & (1 << team)) != 0) return team;
            }
            return -1;
        }
    }

    public sealed partial class TacticalFieldResponse
    {
        public int Started, LastSeen, EnemyId, FocusAt, Assigned, MovingTeam = -1, NextMove, MoveStarted, SmokeRetryAt, BoundCursor;
        public bool BoundPending;
        public TacticalFieldStage Stage;
        public TacticalObservedMotion Motion;
        public bool High;
        public IntVec3 Anchor, Focus, Forward;
        public List<IntVec3> Posts = new List<IntVec3>();
        public List<IntVec3> FirePoints = new List<IntVec3>(3);
        public List<int> Order = new List<int>();
        public HashSet<int> FireGroup = new HashSet<int>();
        public HashSet<IntVec3> Occupied = new HashSet<IntVec3>();
        public TacticalFieldSmoke Screen;
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
            if (command.ContactResponse?.DirectAttack == true) return false;
            TacticalFieldResponse field = command.FieldResponse;
            bool support = WaitingForSupport(command, tick);
            if (field == null)
            {
                if (command.ContactResponse != null) return false;
                Pawn observer = active[active.Count / 2].Pawn;
                if (!support && observer.Position.Roofed(map)) return false;
                TacticalContact nearest = null;
                foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                {
                    if (!TacticalContactMemory.Fresh(contact, tick) || contact.SeenTick <= command.ContactHandledAt
                        || contact.Door || contact.Position.Roofed(map)) continue;
                    if (nearest == null || observer.Position.DistanceToSquared(contact.Position)
                        < observer.Position.DistanceToSquared(nearest.Position)) nearest = contact;
                }
                if (nearest == null && !support) return false;
                IntVec3 focus = nearest?.Position ?? command.SupportAim;
                field = new TacticalFieldResponse { Started = tick, LastSeen = nearest?.SeenTick ?? tick, EnemyId = nearest?.EnemyId ?? -1,
                    Anchor = command.Defensive ? command.DefenseAnchor : observer.Position, Focus = focus, FocusAt = tick, NextMove = tick + 180,
                    Forward = TacticalFieldPolicy.Direction(command.Defensive ? command.DefenseAnchor : observer.Position, focus),
                    Motion = TacticalFieldPolicy.Motion(nearest, observer.Position),
                    High = command.Link.Unit.Faction.def.defName == "HD_HelodCivilHighFaction"
                        && active.Exists(m => m.Fireteam >= 0) };
                for (int i = 0; i < command.Members.Count; i++) field.Posts.Add(IntVec3.Invalid);
                // LOW forms one temporary group around an actual automatic
                // weapon role. This does not mutate the organization tree.
                for (int i = 0; i < command.Members.Count; i++)
                    if (command.Members[i].AutomaticWeapon && Available(command.Members[i], map)) field.Order.Add(i);
                for (int i = 0; i < command.Members.Count; i++) if (!field.Order.Contains(i)) field.Order.Add(i);
                int coreSize = field.High ? 4 : Math.Min(4, Math.Max(1, (active.Count + 2) / 3));
                for (int i = 0; i < field.Order.Count && field.FireGroup.Count < coreSize; i++)
                    if (Available(command.Members[field.Order[i]], map)) field.FireGroup.Add(field.Order[i]);
                for (int point = 0; point < 3; point++)
                    field.FirePoints.Add(TacticalFieldPolicy.FirePoint(field.Anchor, field.Focus, field.Forward, point,
                        unchecked(field.Started ^ field.EnemyId * 397)));
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
            if (!support && (field.EnemyId < 0 || tick - field.LastSeen >= TacticalContactMemory.FreshTicks))
            { EndFieldResponse(command, tick); return false; }
            command.Due = Math.Min(command.Due, tick + 30);
            if (field.High && !active.Exists(m => m.Fireteam >= 0)) field.High = false;
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
                        AssignFieldPost(command, member, field, index, MemberFirePoint(field, member, index));
                    }
                }
                finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            if (support && field.Stage == TacticalFieldStage.Moving)
            { field.BoundPending = false; field.Stage = TacticalFieldStage.Defending; }
            if (!support) AdvanceFieldBoundPosts(command, field, tick);
            int ready = 0, coverReady = 0, coverCount = 0, movers = 0, moversReady = 0;
            float range = 0;
            foreach (TacticalMemberCommand member in active)
            {
                int index = command.Members.IndexOf(member);
                IntVec3 post = field.Posts[index];
                if (!post.IsValid && field.Assigned == field.Order.Count && budget.TryPlan(tick))
                {
                    // A previously downed member can recover after the initial
                    // template has finished. Admit one missing local assignment
                    // without rebuilding any other member's stable post.
                    long started = Stopwatch.GetTimestamp();
                    try
                    {
                        AssignFieldPost(command, member, field, index, MemberFirePoint(field, member, index));
                        post = field.Posts[index];
                    }
                    finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
                }
                bool at = post.IsValid && AtPost(member) && member.Pawn.Position == post;
                if (at) ready++;
                if (FieldMover(field, member, index))
                { movers++; if (at) moversReady++; }
                else { coverCount++; if (at) coverReady++; }
                range = Math.Max(range, member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps.range ?? 0);
                if (field.Screen?.Thrower == member.Pawn && field.Screen.ActiveJob != null
                    && !field.Screen.Returned) continue;
                if (!post.IsValid) { EnsureParking(command, member, tick); continue; }
                if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalContactGuard"
                    && member.Job.targetA.Cell == post)
                { member.Job.targetB = field.Focus; continue; }
                if (member.Pawn.CurJob?.playerForced == true || tick < member.RetryTick || !CanIssue(member)) continue;
                if (Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), post, field.Focus))) FieldGuardJobs++;
            }
            if (field.Assigned < field.Order.Count) return true;
            if (field.Stage == TacticalFieldStage.Forming && ready == active.Count) field.Stage = TacticalFieldStage.Defending;
            if (field.Stage == TacticalFieldStage.Screening)
            {
                if (AdvanceFieldSmoke(command, active, field, tick))
                {
                    if (!TacticalSupportPolicy.CanAdvance(command.Defensive, support) || field.Motion == TacticalObservedMotion.Approaching)
                    {
                        field.Screen = null; field.Stage = TacticalFieldStage.Defending;
                        field.NextMove = tick + 180; return true;
                    }
                    if (BeginFieldBound(command, active, field, tick)) FieldSmokeAdvances++;
                    else field.Stage = TacticalFieldStage.Defending;
                    field.Screen = null;
                }
                return true;
            }
            if (field.Stage == TacticalFieldStage.Moving && !field.BoundPending
                && (moversReady == movers || tick - field.MoveStarted >= 600))
            {
                field.Stage = TacticalFieldStage.Defending; field.NextMove = tick + TacticalFieldPolicy.MoveInterval;
            }
            // HIGH: only one real child team moves; the other teams must already
            // be at their guard posts. Never invent teams from pawn index/modulo.
            if (TacticalSupportPolicy.CanAdvance(command.Defensive, support) && field.Stage == TacticalFieldStage.Defending && tick >= field.NextMove
                && ready == active.Count && coverReady == coverCount && field.Motion != TacticalObservedMotion.Approaching
                && field.Anchor.DistanceToSquared(field.Focus) > Math.Max(100, range * range * .64f))
            {
                if (tick >= field.SmokeRetryAt && PlanFieldSmoke(command, active, field, tick)) return true;
                // HIGH can advance with an established covering team even if
                // smoke is unavailable. LOW keeps its defensive group instead.
                if (field.High) BeginFieldBound(command, active, field, tick);
                else field.NextMove = tick + 180;
            }
            return true;
        }

        private bool BeginFieldBound(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            int liveTeams = 0; bool reserves = false;
            foreach (TacticalMemberCommand member in active)
            {
                if (field.High && member.Fireteam >= 0 && member.Fireteam < 3) liveTeams |= 1 << member.Fireteam;
                reserves |= !field.FireGroup.Contains(command.Members.IndexOf(member));
            }
            int team = TacticalFieldPolicy.NextMover(field.MovingTeam, liveTeams, reserves);
            if (field.High && team < 0) { field.NextMove = tick + 600; return false; }
            field.MovingTeam = team;
            // Move the group's common area once. Individual posts are then
            // selected incrementally inside that area, never advanced as a line.
            bool moved = false;
            for (int point = 0; point < 3; point++)
                if (field.High ? point == team : team == -2 ? point != 1 : point == 1)
                {
                    IntVec3 next = TacticalFieldPolicy.AdvanceFirePoint(field.FirePoints[point], field.Forward, field.Focus);
                    moved |= next != field.FirePoints[point]; field.FirePoints[point] = next;
                }
            if (!moved) { field.NextMove = tick + TacticalFieldPolicy.MoveInterval; return false; }
            field.Stage = TacticalFieldStage.Moving;
            field.BoundCursor = 0; field.BoundPending = true;
            field.MoveStarted = tick; FieldBounds++; return true;
        }

        private void AdvanceFieldBoundPosts(TacticalSquadCommand command, TacticalFieldResponse field, int tick)
        {
            if (!field.BoundPending) return;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                int assigned = 0;
                while (field.BoundCursor < command.Members.Count && assigned < 2)
                {
                    int index = field.BoundCursor++;
                    TacticalMemberCommand member = command.Members[index];
                    if (!Available(member, map) || !FieldMover(field, member, index)) continue;
                    assigned++;
                    AssignFieldPost(command, member, field, index, MemberFirePoint(field, member, index));
                }
                field.BoundPending = field.BoundCursor < command.Members.Count;
                if (!field.BoundPending)
                    field.Anchor = new IntVec3((field.FirePoints[0].x + field.FirePoints[1].x + field.FirePoints[2].x) / 3, 0,
                        (field.FirePoints[0].z + field.FirePoints[1].z + field.FirePoints[2].z) / 3);
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }

        private static bool FieldMover(TacticalFieldResponse field, TacticalMemberCommand member, int index) => field.High
            ? field.MovingTeam >= 0 && member.Fireteam == field.MovingTeam
            : field.MovingTeam == -2 && !field.FireGroup.Contains(index)
                || field.MovingTeam == -3 && field.FireGroup.Contains(index);

        private static IntVec3 MemberFirePoint(TacticalFieldResponse field, TacticalMemberCommand member, int index) =>
            field.FirePoints[TacticalFieldPolicy.FirePointIndex(field.High, member.Fireteam, field.FireGroup.Contains(index), field.Order.IndexOf(index))];

        private void AssignFieldPost(TacticalSquadCommand command, TacticalMemberCommand member,
            TacticalFieldResponse field, int index, IntVec3 point)
        {
            IntVec3 old = field.Posts[index];
            IntVec3 best = IntVec3.Invalid; float score = float.MinValue; bool bestCanFire = false;
            // Pick freely in the group's area. Scatter is sampled only when
            // assigning a post, so a settled pawn never shuffles every tick.
            IntVec3 desired = TacticalFieldPolicy.Scatter(point, member.Pawn.thingIDNumber, field.Started);
            int start = desired.x - point.x + 2 + (desired.z - point.z + 2) * 5, evaluated = 0;
            for (int pass = 0; pass < 2 && !best.IsValid; pass++)
            for (int candidate = 0; candidate < 25 && evaluated < (pass + 1) * TacticalFieldPolicy.FirePostEvaluationLimit / 2; candidate++)
            {
                // Coprime stride samples the whole area instead of exhausting
                // one fixed row first. Heavy LOS/cover work has its own cap.
                int sample = (start + candidate * 7) % 25;
                IntVec3 cell = (pass == 0 ? point : member.Pawn.Position) + new IntVec3(sample % 5 - 2, 0, sample / 5 - 2); FieldPostCandidates++;
                // A blocked area has a local holding fallback; do not restore
                // exact depth rows or a distant path around an entire building.
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.Roofed(map) && command.SupportCaller == null
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
                evaluated++;
                if (!GenSight.LineOfSight(member.Pawn.Position, cell, map, true,
                    p => p.Standable(map) && !(p.GetEdifice(map) is Building_Door))) continue;
                bool canFire = GenSight.LineOfSight(cell, field.Focus, map, true);
                float cover = canFire ? CoverUtility.CalculateOverallBlockChance(cell, field.Focus, map) : 1f;
                float value = TacticalFieldPolicy.FirePostScore(canFire, cover, cell.DistanceToSquared(desired),
                    cell.DistanceToSquared(member.Pawn.Position), unchecked(field.Started ^ member.Pawn.thingIDNumber * 397 ^ cell.GetHashCode()));
                if (!best.IsValid || canFire && !bestCanFire || canFire == bestCanFire && value > score)
                { score = value; best = cell; bestCanFire = canFire; }
            }
            if (!best.IsValid) return; // Keep the previous usable post.
            if (old.IsValid)
            {
                field.Occupied.Remove(old);
                if (!OriginalClaim(command, old) && claims.TryGetValue(old, out TacticalSquadCommand owner) && owner == command)
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
                if (!OriginalClaim(command, cell) && claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command)
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
            command.DefenseRestoring |= command.Defensive;
            command.ContactHandledAt = tick; command.FieldResponse = null; command.Due = tick + 1; FieldResumes++;
        }
    }
}
