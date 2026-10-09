using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalContactResponse
    {
        public int Started, LastSeen, Assigned, SecuredCount, FirstId, SecondId = -1, FocusUpdatedAt;
        public TacticalLocalPlan Plan;
        public TacticalCommandPhase Phase;
        public IntVec3 Anchor, First, Second = IntVec3.Invalid;
        public bool Rear, Opposed, Door;
        public List<IntVec3> Posts = new List<IntVec3>();
        public HashSet<IntVec3> Occupied = new HashSet<IntVec3>();
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long ContactResponses, RearResponses, OpposedResponses, DoorResponses, ContactResumes, ContactGuardJobs;
        public long ContactPostCandidates;
        public long ContactShots, FieldShots;
        internal void ContactShot(Pawn pawn)
        {
            if (!byPawn.TryGetValue(pawn, out TacticalSquadCommand command)) return;
            if (command.FieldResponse != null) FieldShots++; else ContactShots++;
        }
        private bool RespondToContacts(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            // Never preempt a launched throw or any C4 installation/safety
            // sequence. Its driver/controller must retain the live action.
            if (command.ChargeAction != null || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared)
                return false;
            TacticalContactResponse response = command.ContactResponse;
            if (response == null)
            {
                Pawn anchor = active[active.Count / 2].Pawn;
                IntVec3 forward = command.Plan?.Inward ?? command.Goal - anchor.Position;
                TacticalContact first = null, second = null;
                bool rear = false, opposed = false, door = false;
                foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                {
                    if (!TacticalContactMemory.Fresh(contact, tick) || contact.SeenTick <= command.ContactHandledAt) continue;
                    bool behind = TacticalContactMemory.Rear(anchor.Position, forward, contact.Position);
                    bool otherDoor = contact.Door && command.Plan != null && contact.Position != command.Plan.Opening;
                    bool close = anchor.Position.DistanceToSquared(contact.Position) <= 36;
                    if (first != null && TacticalContactMemory.Opposed(anchor.Position, first.Position, contact.Position))
                    { second = contact; opposed = true; }
                    if (first == null || behind || otherDoor)
                    { if (first != null && opposed) second = first; first = contact; }
                    rear |= behind; door |= otherDoor;
                    if (close) command.ContactCloseAt = contact.SeenTick;
                }
                if (first == null || !command.Defensive && !rear && !opposed && !door && tick - command.ContactCloseAt >= TacticalContactMemory.FreshTicks)
                    return false;
                response = new TacticalContactResponse { Started = tick, LastSeen = first.SeenTick,
                    Anchor = anchor.Position, First = first.Position, Second = second?.Position ?? IntVec3.Invalid,
                    FirstId = first.EnemyId, SecondId = second?.EnemyId ?? -1, FocusUpdatedAt = tick,
                    Rear = rear, Opposed = opposed, Door = door, Plan = command.Plan, Phase = command.Phase,
                    SecuredCount = command.SecuredCells.Count };
                for (int i = 0; i < command.Members.Count; i++) response.Posts.Add(IntVec3.Invalid);
                command.ContactResponse = response; ContactResponses++;
                if (rear) RearResponses++; if (opposed) OpposedResponses++; if (door) DoorResponses++;
            }
            // Only fresh, actually observed samples extend this response. The
            // memory itself cannot acquire a hidden pawn's new location.
            bool refreshFocus = tick - response.FocusUpdatedAt >= 90;
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
            {
                if (!TacticalContactMemory.Fresh(contact, tick)) continue;
                if (contact.EnemyId == response.FirstId || contact.EnemyId == response.SecondId)
                {
                    response.LastSeen = Math.Max(response.LastSeen, contact.SeenTick);
                    if (refreshFocus)
                    {
                        if (contact.EnemyId == response.FirstId) response.First = contact.Position;
                        else response.Second = contact.Position;
                    }
                }
                // A second direction may be discovered in the next bounded
                // observer slice, after the first response has already begun.
                if (!response.Second.IsValid && TacticalContactMemory.Opposed(response.Anchor, response.First, contact.Position))
                {
                    response.Second = contact.Position;
                    response.SecondId = contact.EnemyId;
                    if (!response.Opposed) { response.Opposed = true; OpposedResponses++; }
                }
                if (contact.Position.DistanceToSquared(response.First) <= 36
                    || response.Second.IsValid && contact.Position.DistanceToSquared(response.Second) <= 36)
                    response.LastSeen = Math.Max(response.LastSeen, contact.SeenTick);
            }
            if (refreshFocus) response.FocusUpdatedAt = tick;
            if (tick - response.LastSeen >= TacticalContactMemory.FreshTicks)
            {
                // Preserve the phase, selected portal and secured history. An
                // interrupted ingress resumes from its already crossed side.
                command.PhaseStarted += tick - response.Started;
                if (command.OpeningAction != null)
                {
                    // Unlaunched observation/throw may be interrupted. Restart
                    // that small action; never retain an issued-but-dead flag.
                    command.OpeningAction = null;
                    if (command.Phase == TacticalCommandPhase.Observe || command.Phase == TacticalCommandPhase.Support)
                        command.Phase = TacticalCommandPhase.Observe;
                }
                for (int i = 0; i < command.Members.Count; i++)
                {
                    TacticalMemberCommand member = command.Members[i];
                    if (member.Job?.def.defName != "HD_NewTacticalContactGuard") continue;
                    member.Entered = member.EntryAssignmentDone = false; member.RetryTick = 0;
                    member.LastProgressTick = tick;
                }
                foreach (IntVec3 cell in response.Occupied)
                    if (!OriginalClaim(command, cell) && claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command)
                        claims.Remove(cell);
                command.DefenseRestoring |= command.Defensive;
                command.ContactHandledAt = tick; command.ContactResponse = null; ContactResumes++;
                command.ContactRestoring = command.Phase == TacticalCommandPhase.Clear && command.Plan != null;
                command.Due = tick + 1; return false;
            }
            command.Due = Math.Min(command.Due, tick + 30);
            // At most two local assignments per advance, plus the existing
            // global job/path limits. No synchronous whole-squad cover search.
            int assigned = 0;
            while (response.Assigned < command.Members.Count && assigned++ < 2)
            {
                int index = response.Assigned++;
                TacticalMemberCommand member = command.Members[index];
                if (!Available(member, map)) continue;
                IntVec3 focus = response.Second.IsValid && index % 2 != 0 ? response.Second : response.First;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    IntVec3 post = FindContactPost(command, member, response, focus);
                    response.Posts[index] = post;
                    if (post.IsValid) { response.Occupied.Add(post); claims[post] = command; }
                }
                finally { Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.Account(tick, Stopwatch.GetTimestamp() - started); }
            }
            foreach (TacticalMemberCommand member in active)
            {
                int index = command.Members.IndexOf(member); IntVec3 post = response.Posts[index];
                if (!post.IsValid || member.Pawn.CurJob?.playerForced == true) continue;
                IntVec3 focus = response.Second.IsValid && index % 2 != 0 ? response.Second : response.First;
                if (member.Job?.def.defName == "HD_NewTacticalContactGuard" && member.Pawn.CurJob == member.Job)
                { member.Job.targetB = focus; continue; }
                if (tick < member.RetryTick || !CanIssue(member)) continue;
                if (Issue(member, JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalContactGuard"), post, focus))) ContactGuardJobs++;
            }
            return true;
        }
        private static bool OriginalClaim(TacticalSquadCommand command, IntVec3 cell)
        {
            TacticalLocalPlan plan = command.Plan;
            // The Clear transition retired the unused approach. Temporary
            // contact/identification posts must not preserve it as an old claim.
            return plan != null && (plan.Positions.Contains(cell) || command.Phase != TacticalCommandPhase.Clear
                && (plan.Stack.Contains(cell) || cell == plan.Outside || cell == plan.Inside
                    || cell == plan.Opening || cell == plan.EntryLane));
        }

        private bool RestoreContactPosts(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            if (!command.ContactRestoring) return false;
            bool ready = true;
            foreach (TacticalMemberCommand member in active)
            {
                int index = command.Members.IndexOf(member);
                TacticalLocalPlan plan = command.Plan;
                IntVec3 post = plan.Positions[index];
                bool Held() => member.Pawn.CurJob == member.Job && member.Pawn.jobs.curDriver is TacticalJobDriver driver
                    && driver.AtPost && member.Pawn.Position == post;
                if (!Held())
                {
                    if (plan.Direct || member.Crossed || plan.RetainedOutside.Contains(index)) EnsurePost(member, post, plan.Opening, tick);
                    else if ((member.Pawn.CurJob != member.Job || member.Job?.def.defName != "HD_NewTacticalIngress")
                        && member.Pawn.CurJob?.playerForced != true && tick >= member.RetryTick && CanIssue(member))
                    {
                        // A treated straggler can still be in the previous room.
                        // Resume through the same mandatory portal, not a plain
                        // post path that might use a different open entrance.
                        Job ingress = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_NewTacticalIngress"),
                            plan.Outside, plan.Opening, post);
                        ingress.targetQueueA = new List<LocalTargetInfo> { plan.Inside };
                        if (plan.EntryLane.IsValid) ingress.targetQueueA.Add(plan.EntryLane);
                        ingress.count = member.Passed ? 1 : 0;
                        Issue(member, ingress);
                    }
                }
                ready &= Held() && (plan.Direct || plan.RetainedOutside.Contains(index) || member.Passed && member.Crossed);
            }
            if (ready)
            {
                // A resumed clear-phase post is still a completed ingress.
                // Contact/care overlays clear these flags while moving, so
                // restore them from the retained crossing and actual position.
                foreach (TacticalMemberCommand member in active)
                {
                    int index = command.Members.IndexOf(member);
                    member.EntryAssignmentDone = true;
                    member.Entered = !command.Plan.RetainedOutside.Contains(index)
                        && (command.Plan.Direct || member.Passed && member.Crossed);
                    member.EverEntered |= member.Entered;
                }
                command.ContactRestoring = false; return false;
            }
            command.Due = tick + 30; return true;
        }

        private IntVec3 FindContactPost(TacticalSquadCommand command, TacticalMemberCommand member,
            TacticalContactResponse response, IntVec3 focus)
        {
            IntVec3 root = member.Pawn.Position, best = IntVec3.Invalid;
            int Depth(IntVec3 cell) => (cell.x - command.Plan.Opening.x) * command.Plan.Inward.x
                + (cell.z - command.Plan.Opening.z) * command.Plan.Inward.z;
            bool inside = command.Plan != null && Depth(root) > 0;
            float bestScore = float.MinValue;
            var pending = new Queue<IntVec3>(); var seen = new HashSet<IntVec3>();
            pending.Enqueue(root); seen.Add(root);
            for (int visited = 0; pending.Count > 0 && visited < 25; visited++)
            {
                IntVec3 cell = pending.Dequeue(); ContactPostCandidates++;
                bool blocked = response.Occupied.Contains(cell) || claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner != command;
                Pawn occupant = cell.GetFirstPawn(map);
                bool mouth = command.Plan != null && !command.Plan.Direct
                    && (cell == command.Plan.Opening || cell == command.Plan.Outside || cell == command.Plan.Inside || cell == command.Plan.EntryLane);
                if (!blocked && !mouth && (occupant == null || occupant == member.Pawn) && cell.Standable(map))
                {
                    bool hidden = !GenSight.LineOfSight(focus, cell, map, true);
                    float cover = hidden ? 1f : CoverUtility.CalculateOverallBlockChance(cell, focus, map);
                    float score = cover * 30f - cell.DistanceToSquared(root) * 2f;
                    if (cell.DistanceToSquared(focus) < root.DistanceToSquared(focus)) score -= 12f;
                    if (score > bestScore) { best = cell; bestScore = score; }
                }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    // Connected local floor only. Never cross a door, mouth or
                    // corner via an independent long native path to a cover tile.
                    if (next.DistanceToSquared(root) > 4 || !next.InBounds(map) || !next.Standable(map)
                        || next.GetEdifice(map) is Building_Door || !seen.Add(next)) continue;
                    if (command.Plan != null && !command.Plan.Direct)
                    {
                        int depth = Depth(next);
                        if (next == command.Plan.Opening || inside && depth < 1 || !inside && depth > 0) continue;
                    }
                    pending.Enqueue(next);
                }
            }
            return best;
        }
    }
}
