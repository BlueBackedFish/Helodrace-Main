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
        public const int PlayerApproachRadius = 55;
        public static bool FormationAllowed(TacticalContact contact, IntVec3 anchor, int tick) => contact != null
            && TacticalContactMemory.Fresh(contact, tick)
            && (!contact.PlayerControlled || contact.Position.DistanceToSquared(anchor) <= PlayerApproachRadius * PlayerApproachRadius
                && Motion(contact, anchor) == TacticalObservedMotion.Approaching
                && contact.Position.DistanceToSquared(anchor) < contact.PreviousPosition.DistanceToSquared(anchor));
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
        public const int AreaRadius = 3, AreaLimit = 49, GatherTicks = 360, JoinInterval = 180, HighBound = 12;
        public static bool Ready(int count, int arrived) => count == 0 || arrived >= (count * 3 + 3) / 4;
        // Four-neighbour connectivity excludes isolated pockets behind walls.
        public static List<IntVec3> ConnectedArea(IntVec3 seed, Func<IntVec3, bool> floor)
        {
            var cells = new List<IntVec3>(AreaLimit);
            var seen = new HashSet<IntVec3>();
            if (!floor(seed)) return cells;
            cells.Add(seed); seen.Add(seed);
            for (int cursor = 0; cursor < cells.Count && cells.Count < AreaLimit; cursor++)
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 cell = cells[cursor] + direction;
                    if (Math.Abs(cell.x - seed.x) > AreaRadius || Math.Abs(cell.z - seed.z) > AreaRadius
                        || !seen.Add(cell) || !floor(cell)) continue;
                    cells.Add(cell);
                    if (cells.Count == AreaLimit) return cells;
                }
            return cells;
        }
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
        public static IntVec3 AdvanceFirePoint(IntVec3 point, IntVec3 forward, IntVec3 focus, int distance = 5)
        {
            int depth = (focus.x - point.x) * forward.x + (focus.z - point.z) * forward.z;
            return point + forward * Math.Max(0, Math.Min(distance, depth - 3));
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

    public sealed class TacticalFireArea : IExposable
    {
        public List<IntVec3> Cells = new List<IntVec3>();
        public int Started, Attempts;
        public bool Prepared;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Cells, "cells", LookMode.Value);
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref Attempts, "attempts");
            Scribe_Values.Look(ref Prepared, "prepared");
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
        public List<int> Groups = new List<int>();
        public List<int> JoinAt = new List<int>();
        public List<TacticalFireArea> Areas = new List<TacticalFireArea>();
        public int LeaderIndex = -1, LeaderTeam = -1;
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
            if (command.ChargeAction != null || TacticalOpeningPolicy.OwnsContact(command, command.OpeningAction?.Enemy ?? IntVec3.Invalid)
                || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared)
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
                        || contact.Door || contact.Position.Roofed(map)
                        || !TacticalFieldPolicy.FormationAllowed(contact, observer.Position, tick)) continue;
                    if (nearest == null || observer.Position.DistanceToSquared(contact.Position)
                        < observer.Position.DistanceToSquared(nearest.Position)) nearest = contact;
                }
                // A support request alone does not authorize an arc against a
                // stationary player target. Wait for an eligible observation.
                if (nearest == null) return false;
                IntVec3 focus = nearest?.Position ?? command.SupportAim;
                field = new TacticalFieldResponse { Started = tick, LastSeen = nearest?.SeenTick ?? tick, EnemyId = nearest?.EnemyId ?? -1,
                    Anchor = command.Defensive ? command.DefenseAnchor : observer.Position, Focus = focus, FocusAt = tick, NextMove = tick + 180,
                    Forward = TacticalFieldPolicy.Direction(command.Defensive ? command.DefenseAnchor : observer.Position, focus),
                    Motion = TacticalFieldPolicy.Motion(nearest, observer.Position),
                    High = command.Link.Unit.Faction.def.defName == "HD_HelodCivilHighFaction"
                        && active.Exists(m => m.Fireteam >= 0) };
                for (int i = 0; i < command.Members.Count; i++)
                { field.Posts.Add(IntVec3.Invalid); field.JoinAt.Add(0); }
                // LOW forms one temporary group around an actual automatic
                // weapon role. This does not mutate the organization tree.
                for (int i = 0; i < command.Members.Count; i++)
                    if (command.Members[i].AutomaticWeapon && Available(command.Members[i], map)) field.Order.Add(i);
                for (int i = 0; i < command.Members.Count; i++) if (!field.Order.Contains(i)) field.Order.Add(i);
                int coreSize = field.High ? 4 : Math.Min(4, Math.Max(1, (active.Count + 2) / 3));
                for (int i = 0; i < field.Order.Count && field.FireGroup.Count < coreSize; i++)
                    if (Available(command.Members[field.Order[i]], map)) field.FireGroup.Add(field.Order[i]);
                for (int point = 0; point < 3; point++)
                {
                    field.FirePoints.Add(TacticalFieldPolicy.FirePoint(field.Anchor, field.Focus, field.Forward, point,
                        unchecked(field.Started ^ field.EnemyId * 397)));
                    field.Areas.Add(new TacticalFireArea { Started = tick });
                }
                for (int i = 0; i < command.Members.Count; i++)
                    field.Groups.Add(TacticalFieldPolicy.FirePointIndex(field.High, command.Members[i].Fireteam,
                        field.FireGroup.Contains(i), field.Order.IndexOf(i)));
                command.FieldResponse = field; FieldResponses++;
                command.Link.Cooperation.LocalReady = false;
            }
            bool permitted = false;
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                if (contact.EnemyId == field.EnemyId && TacticalContactMemory.Fresh(contact, tick))
                {
                    permitted = TacticalFieldPolicy.FormationAllowed(contact, active[active.Count / 2].Pawn.Position, tick);
                    if (contact.SeenTick > field.LastSeen) field.Motion = TacticalFieldPolicy.Motion(contact, field.Anchor);
                    field.LastSeen = Math.Max(field.LastSeen, contact.SeenTick);
                    if (tick - field.FocusAt >= TacticalFieldPolicy.FocusInterval)
                    {
                        field.Focus = contact.Position;
                        field.FocusAt = tick;
                    }
                }
            if (!permitted)
            {
                var screen = field.Screen;
                bool returning = screen?.ActiveJob != null && screen.Thrower?.Spawned == true && !screen.Thrower.Dead && !screen.Thrower.Downed;
                if (returning || screen?.Projectile?.Spawned == true && !screen.Projectile.Destroyed)
                { command.Due = Math.Min(command.Due, tick + 15); return true; }
                EndFieldResponse(command, tick); return false;
            }
            command.Due = Math.Min(command.Due, tick + 30);
            if (field.High && !active.Exists(m => m.Fireteam >= 0)) field.High = false;
            RefreshFieldLeader(command, active, field, tick);
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
            int[] counts = new int[3], arrived = new int[3], shooters = new int[3];
            bool[] separated = new bool[3];
            int movers = 0, moversReady = 0;
            float range = 0;
            foreach (TacticalMemberCommand member in active)
            {
                int index = command.Members.IndexOf(member);
                IntVec3 post = field.Posts[index];
                int group = field.Groups[index];
                TacticalFireArea area = field.Areas[group];
                // Keep a usable arrival anywhere within the shared area. Tiny
                // deviations never restart a path to an exact formation slot.
                bool inArea = area.Cells.Contains(member.Pawn.Position) && FieldFloor(command, member.Pawn.Position);
                if (inArea && member.Pawn.Position != post && member.Pawn.CurJob == member.Job
                    && member.Job?.def.defName == "HD_NewTacticalContactGuard"
                    && !field.Occupied.Contains(member.Pawn.Position)
                    && (!claims.TryGetValue(member.Pawn.Position, out TacticalSquadCommand cellOwner) || cellOwner == command))
                { SetFieldPost(command, field, index, member.Pawn.Position); post = member.Pawn.Position; }
                if ((!post.IsValid || !area.Cells.Contains(post) || !FieldFloor(command, post)) && field.Assigned == field.Order.Count
                    && tick >= field.JoinAt[index] && budget.TryPlan(tick))
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
                bool at = inArea && AtPost(member);
                counts[group]++; if (at) arrived[group]++;
                if (!at && member.Pawn.Position.DistanceToSquared(field.FirePoints[group]) > 12 * 12) separated[group] = true;
                if (at && member.Pawn.equipment?.PrimaryEq?.PrimaryVerb is Verb readyVerb && !readyVerb.IsMeleeAttack)
                    shooters[group]++;
                if (FieldMover(field, member, index))
                { movers++; if (at) moversReady++; }
                range = Math.Max(range, member.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps.range ?? 0);
                if (field.Screen?.Thrower == member.Pawn && field.Screen.ActiveJob != null
                    && !field.Screen.Returned) continue;
                if (!post.IsValid || !area.Cells.Contains(post) || !FieldFloor(command, post))
                { EnsureFieldWaiting(command, member, field, tick); continue; }
                if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalContactGuard"
                    && member.Job.targetA.Cell == post)
                { member.Job.targetB = field.Focus; continue; }
                if (member.Pawn.CurJob?.playerForced == true || tick < member.RetryTick || !CanIssue(member)) continue;
                if (Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), post, field.Focus))) FieldGuardJobs++;
            }
            if (field.Assigned < field.Order.Count) return true;
            bool teamsReady = true;
            for (int group = 0; group < 3; group++)
            {
                bool ready = TacticalFieldPolicy.Ready(counts[group], arrived[group]) && !separated[group]
                    && (counts[group] == 0 || shooters[group] > 0);
                teamsReady &= ready;
                if (!ready && tick - field.Areas[group].Started >= TacticalFieldPolicy.GatherTicks)
                    RepairFieldArea(command, active, field, group, tick);
            }
            if (field.Stage == TacticalFieldStage.Forming && (teamsReady || tick - field.Started >= TacticalFieldPolicy.GatherTicks))
                field.Stage = TacticalFieldStage.Defending;
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
                && (TacticalFieldPolicy.Ready(movers, moversReady) || tick - field.MoveStarted >= 600))
            {
                field.Stage = TacticalFieldStage.Defending; field.NextMove = tick + TacticalFieldPolicy.MoveInterval;
            }
            // HIGH: only one real child team moves; the other teams must already
            // be at their guard posts. Never invent teams from pawn index/modulo.
            if (TacticalSupportPolicy.CanAdvance(command.Defensive, support) && field.Stage == TacticalFieldStage.Defending && tick >= field.NextMove
                && teamsReady && field.Motion != TacticalObservedMotion.Approaching
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
                    IntVec3 next = TacticalFieldPolicy.AdvanceFirePoint(field.FirePoints[point], field.Forward, field.Focus,
                        field.High ? TacticalFieldPolicy.HighBound : 5);
                    // Limit the separation from the covering areas. This cheap
                    // center test replaces pawn-pair distance/reach searches.
                    if (field.High)
                    {
                        for (int other = 0; other < 3; other++)
                            if (other != point && field.Areas[other].Cells.Count > 0
                                && next.DistanceToSquared(field.FirePoints[other]) > 30 * 30)
                                next = TacticalFieldPolicy.AdvanceFirePoint(field.FirePoints[point], field.Forward, field.Focus, 5);
                        for (int other = 0; other < 3; other++)
                            if (other != point && field.Areas[other].Cells.Count > 0
                                && next.DistanceToSquared(field.FirePoints[other]) > 30 * 30) next = field.FirePoints[point];
                    }
                    if (next != field.FirePoints[point])
                    { moved = true; field.FirePoints[point] = next; field.Areas[point] = new TacticalFireArea { Started = tick }; }
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
            ? field.MovingTeam >= 0 && field.Groups[index] == field.MovingTeam
            : field.MovingTeam == -2 && !field.FireGroup.Contains(index)
                || field.MovingTeam == -3 && field.FireGroup.Contains(index);

        private static IntVec3 MemberFirePoint(TacticalFieldResponse field, TacticalMemberCommand member, int index) =>
            field.FirePoints[field.Groups[index]];

        private void RefreshFieldLeader(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            if (!field.High) return;
            Pawn commander = command.Link.Unit.Commander;
            int index = command.Members.FindIndex(m => m.Pawn == commander);
            bool live = active.Exists(m => m.Fireteam == field.LeaderTeam && m.Fireteam >= 0);
            if (index == field.LeaderIndex && live) return;
            int previous = field.LeaderIndex;
            field.LeaderIndex = index; field.LeaderTeam = -1;
            int distance = int.MaxValue;
            foreach (TacticalMemberCommand member in active)
                if (member.Fireteam >= 0 && member.Fireteam < 3)
                {
                    int candidate = commander?.Position.DistanceToSquared(member.Pawn.Position) ?? 0;
                    if (candidate < distance) { distance = candidate; field.LeaderTeam = member.Fireteam; }
                }
            if (previous >= 0 && previous != index && command.Members[previous].Fireteam < 0)
                field.Groups[previous] = field.LeaderTeam < 0 ? 1 : field.LeaderTeam;
            if (index < 0 || field.LeaderTeam < 0 || command.Members[index].Fireteam >= 0) return;
            field.Groups[index] = field.LeaderTeam;
            field.JoinAt[index] = tick;
        }

        private bool FieldFloor(TacticalSquadCommand command, IntVec3 cell)
        {
            if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) is Building_Door
                || cell.Roofed(map) && command.SupportCaller == null) return false;
            TacticalLocalPlan plan = command.Plan;
            if (plan == null || plan.Direct) return true;
            int Depth(IntVec3 p) => (p.x - plan.Opening.x) * plan.Inward.x + (p.z - plan.Opening.z) * plan.Inward.z;
            return (Depth(cell) >= 1) == (Depth(command.FieldResponse.Anchor) >= 1)
                && cell != plan.Opening && cell != plan.Outside && cell != plan.Inside && cell != plan.EntryLane;
        }

        private void BuildFieldArea(TacticalSquadCommand command, TacticalFieldResponse field, int group, IntVec3 desired)
        {
            IntVec3 seed = IntVec3.Invalid;
            int distance = int.MaxValue;
            // A single bounded seed search, then a shared flood for the group.
            for (int x = -3; x <= 3; x++) for (int z = -3; z <= 3; z++)
            {
                IntVec3 cell = desired + new IntVec3(x, 0, z);
                if (x*x + z*z >= distance || !FieldFloor(command, cell)) continue;
                seed = cell; distance = x*x + z*z;
            }
            field.Areas[group].Cells = seed.IsValid
                ? TacticalFieldPolicy.ConnectedArea(seed, cell => FieldFloor(command, cell)) : new List<IntVec3>();
            field.Areas[group].Prepared = true;
            if (seed.IsValid) field.FirePoints[group] = seed;
        }

        private void RepairFieldArea(TacticalSquadCommand command, List<TacticalMemberCommand> active,
            TacticalFieldResponse field, int group, int tick)
        {
            TacticalFireArea area = field.Areas[group];
            if (area.Attempts >= 2 || field.BoundPending || field.Stage == TacticalFieldStage.Screening) return;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryPlan(tick)) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                TacticalMemberCommand representative = active.Find(m => field.Groups[command.Members.IndexOf(m)] == group);
                if (representative == null) return;
                IntVec3 from = representative.Pawn.Position;
                IntVec3 desired = field.FirePoints[group];
                // One shared intermediate join point, then a compact defense
                // near the representative if the advance is still obstructed.
                if (area.Attempts == 0)
                {
                    IntVec3 direction = TacticalFieldPolicy.Direction(from, desired);
                    for (int step = 1; step <= 6; step++)
                    {
                        IntVec3 next = from + direction;
                        if (next.DistanceToSquared(desired) >= from.DistanceToSquared(desired) || !FieldFloor(command, next)) break;
                        from = next; // stop at the first wall, never route around a building
                    }
                }
                area.Attempts++; area.Started = tick;
                BuildFieldArea(command, field, group, from);
                for (int i = 0; i < field.Groups.Count; i++) if (field.Groups[i] == group)
                { ClearFieldPost(command, field, i); field.JoinAt[i] = tick; }
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }

        private void EnsureFieldWaiting(TacticalSquadCommand command, TacticalMemberCommand member, TacticalFieldResponse field, int tick)
        {
            // Isolated fallback is only a waiting/cover job; it never counts as
            // arrival in the group's connected area.
            if (member.Pawn.CurJob == member.Job && member.Job?.def.defName == "HD_NewTacticalContactGuard")
            { member.Job.targetB = field.Focus; return; }
            if (tick >= member.RetryTick && CanIssue(member))
                Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), member.Pawn.Position, field.Focus));
        }

        private void AssignFieldPost(TacticalSquadCommand command, TacticalMemberCommand member,
            TacticalFieldResponse field, int index, IntVec3 point)
        {
            int group = field.Groups[index];
            TacticalFireArea area = field.Areas[group];
            field.JoinAt[index] = GenTicks.TicksGame + TacticalFieldPolicy.JoinInterval;
            if (!area.Prepared) BuildFieldArea(command, field, group, point);
            IntVec3 old = field.Posts[index];
            IntVec3 best = IntVec3.Invalid; float score = float.MinValue; bool bestCanFire = false;
            // Pick freely in the group's area. Scatter is sampled only when
            // assigning a post, so a settled pawn never shuffles every tick.
            IntVec3 desired = TacticalFieldPolicy.Scatter(point, member.Pawn.thingIDNumber, field.Started);
            int start = Math.Abs(member.Pawn.thingIDNumber % Math.Max(1, area.Cells.Count)), evaluated = 0;
            for (int candidate = 0; candidate < area.Cells.Count && evaluated < TacticalFieldPolicy.FirePostEvaluationLimit; candidate++)
            {
                // Coprime stride samples the whole area instead of exhausting
                // one fixed row first. Heavy LOS/cover work has its own cap.
                IntVec3 cell = area.Cells[(start + candidate) % area.Cells.Count]; FieldPostCandidates++;
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
            if (!best.IsValid) return;
            SetFieldPost(command, field, index, best);
        }

        private void ClearFieldPost(TacticalSquadCommand command, TacticalFieldResponse field, int index)
        {
            IntVec3 old = field.Posts[index];
            if (old.IsValid)
            {
                field.Occupied.Remove(old);
                if (!OriginalClaim(command, old) && claims.TryGetValue(old, out TacticalSquadCommand owner) && owner == command)
                    claims.Remove(old);
            }
            field.Posts[index] = IntVec3.Invalid;
        }

        private void SetFieldPost(TacticalSquadCommand command, TacticalFieldResponse field, int index, IntVec3 best)
        {
            ClearFieldPost(command, field, index);
            field.Posts[index] = best; field.Occupied.Add(best); claims[best] = command;
        }

        private void EndFieldResponse(TacticalSquadCommand command, int tick, bool preserveOpening = false)
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
            if (!preserveOpening && command.OpeningAction != null)
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
