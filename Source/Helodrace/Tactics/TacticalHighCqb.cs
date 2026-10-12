using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalHighCqbPolicy
    {
        public static bool High(TacticalSquadCommand command) => command.Link.Unit?.Faction?.def?.defName == "HD_HelodCivilHighFaction";

        // Keep role ordering inside a team; keep real teams together at one mouth.
        public static List<int> GroupOrder(TacticalSquadCommand command, List<int> order)
        {
            if (!High(command) || command.CqbParent != null) return order;
            int leader = order.FindIndex(i => command.Members[i].Fireteam < 0);
            int commander = leader < 0 ? -1 : order[leader];
            if (leader >= 0) order.RemoveAt(leader);
            var teams = new List<int>(); var result = new List<int>();
            foreach (int i in order)
                if (!teams.Contains(command.Members[i].Fireteam)) teams.Add(command.Members[i].Fireteam);
            for (int t = 0; t < teams.Count; t++)
            {
                foreach (int i in order) if (command.Members[i].Fireteam == teams[t]) result.Add(i);
                if (commander >= 0 && t == teams.Count / 2) result.Insert(Math.Max(0, result.Count - 2), commander);
            }
            if (commander >= 0 && teams.Count == 0) result.Add(commander);
            return result;
        }

        public static bool EntryReleased(TacticalSquadCommand command, int index)
        {
            if (command.CqbParent != null || !High(command) || command.Plan.Direct || command.Plan.ReportedPassage) return true;
            int ordinal = command.Plan.EntryOrder.IndexOf(index);
            if (ordinal < 0) return true;
            int team = command.Members[index].Fireteam;
            if (team < 0) return true;
            // Release when the preceding team's live ingress personnel physically
            // cross, not when they finish shooting or reach distant final seats.
            for (int n = 0; n < ordinal; n++)
            {
                int earlier = command.Plan.EntryOrder[n]; var member = command.Members[earlier];
                if (member.Fireteam == team || member.Fireteam < 0 || command.Plan.RetainedOutside.Contains(earlier)) continue;
                if (member.Pawn?.Dead != true && member.Pawn?.Downed != true && !member.Crossed) return false;
            }
            return true;
        }

        // Only latched, already known room boundaries. No unseen door polling.
        public static bool Conflicts(TacticalRoomFrontier a, TacticalRoomFrontier b) =>
            a.Opening == b.Opening || a.Inside == b.Inside
            || a.Inside.DistanceToSquared(b.Inside) <= 4 && a.Inward == b.Inward;

        public static HashSet<IntVec3> LocalArea(IntVec3 seed, Func<IntVec3, bool> floor)
        {
            var area = new HashSet<IntVec3>(); var pending = new Queue<IntVec3>();
            if (!floor(seed)) return area;
            area.Add(seed); pending.Enqueue(seed);
            while (pending.Count > 0 && area.Count < 64)
            {
                var cell = pending.Dequeue();
                foreach (var direction in GenAdj.CardinalDirections)
                {
                    var next = cell + direction;
                    if (area.Count < 64 && !area.Contains(next) && floor(next)) { area.Add(next); pending.Enqueue(next); }
                }
            }
            return area;
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        public List<TacticalSquadCommand> CqbTeams = new List<TacticalSquadCommand>();
        public TacticalSquadCommand CqbParent;
        public int CqbTeam = -1, CqbCursor, CqbSearchCursor, CqbPausedAt = -1, CqbPassageSequence, CqbFollowSeen;
        public bool CqbEntered, CqbCancelRequested, CqbObjectiveChanged, CqbFollow;
        internal bool CqbStepped;
        internal List<int> CqbRoster = new List<int>();
        public TacticalRoomFrontier CqbTask;
        public HashSet<IntVec3> CqbArea = new HashSet<IntVec3>();
        public TacticalLocalPlan CqbLastPassage;
        public IntVec3 CqbFollowGoal = IntVec3.Invalid;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private bool RootContext(Pawn pawn, out TacticalSquadCommand command)
        {
            if (!byPawn.TryGetValue(pawn, out command)) return false;
            command = command.CqbParent ?? command; return true;
        }
        internal IEnumerable<TacticalSquadCommand> ExecutionCommands => squads.Values.SelectMany(p =>
            new[] { p }.Concat(p.CqbTeams));
        // Children are execution records, never scheduler/communications entries.
        private static bool CqbLiveAction(TacticalSquadCommand team) => team.ChargeAction != null
            || team.OpeningAction?.Launched == true && !team.OpeningAction.EffectsCleared;

        private bool AdvanceHighCqb(TacticalSquadCommand parent, List<TacticalMemberCommand> active, int tick)
        {
            if (parent.CqbParent != null || parent.Defensive) return false;
            if (parent.CqbTeams.Count == 0)
            {
                if (!TacticalHighCqbPolicy.High(parent) || parent.Phase != TacticalCommandPhase.Clear
                    || parent.RoomScan != null || parent.Plan == null || parent.Preparation != null
                    || !parent.Frontiers.Any(f => !f.Outdoors && !f.Approach && !f.Excluded
                        && !parent.SecuredKnowledge.Cleared(f.Inside))) return false;
                StartHighCqb(parent, active, tick);
                if (parent.CqbTeams.Count < 2) { EndHighCqb(parent); return false; }
            }
            parent.CqbStepped = true;
            if (parent.CqbPausedAt >= 0)
            {
                int elapsed = tick - parent.CqbPausedAt;
                foreach (var team in parent.CqbTeams)
                {
                    team.PhaseStarted += elapsed;
                    if (team.OpeningAction != null && !team.OpeningAction.Launched)
                    { team.OpeningAction = null; if (team.Phase == TacticalCommandPhase.Support) team.Phase = TacticalCommandPhase.Observe; }
                    if (team.ContactResponse != null) team.ContactResponse.Started += elapsed;
                }
                parent.CqbPausedAt = -1;
                ReleaseClaims(parent);
                foreach (var team in parent.CqbTeams)
                    if (team.Plan != null)
                        foreach (var post in team.Plan.Positions.Concat(team.Plan.Stack))
                            if (!claims.ContainsKey(post)) claims[post] = team;
            }
            parent.Due = tick + TacticalCadence.DecisionTicks;
            int count = parent.CqbTeams.Count;
            // Exactly one subtask per admitted squad decision; the existing
            // global plan/movement/jobs allowance is shared by all three teams.
            for (int inspected = 0; inspected < count; inspected++)
            {
                var team = parent.CqbTeams[parent.CqbCursor++ % count];
                if (parent.CqbCursor >= count) parent.CqbCursor = 0;
                if (team.Terminal) continue;
                if (parent.CqbCancelRequested && team.Phase != TacticalCommandPhase.Complete
                    && team.Phase != TacticalCommandPhase.Returning && !CqbLiveAction(team)) BeginReturn(team, tick, false);
                if (team.Phase == TacticalCommandPhase.Complete)
                {
                    if (!parent.CqbCancelRequested && AssignCqbFrontier(parent, team, tick)) { parent.Due = tick + 1; return true; }
                    if (!parent.CqbCancelRequested && FollowCqbPassage(parent, team, tick)) { parent.Due = tick + 1; return true; }
                    int issued = 0;
                    for (int i = 0; team.Plan != null && i < team.Members.Count && issued < 2; i++)
                    {
                        var member = team.Members[i];
                        if (Available(member, map) && !HoldsPost(member, team.Plan.Positions[i], true))
                        { EnsurePost(member, team.Plan.Positions[i], parent.Goal, tick, entryCombat: true); issued++; }
                    }
                    continue;
                }
                if (team.Due > tick) { parent.Due = Math.Min(parent.Due, team.Due); continue; }
                if (SiblingCqbHazard(team)) continue;
                if (team.Phase == TacticalCommandPhase.Enter && team.Plan != null)
                    for (int i = 0; i < team.Members.Count; i++)
                        if (team.Members[i].Pawn.Position != team.Plan.Positions[i]) team.Members[i].Entered = team.Members[i].EntryAssignmentDone = false;
                AdvanceCore(team, tick); UpdateCqbPosts(parent);
                foreach (var pending in parent.CqbTeams)
                    if (!pending.Terminal && pending.Phase != TacticalCommandPhase.Complete)
                        parent.Due = Math.Min(parent.Due, Math.Max(tick + 1, pending.Due));
                return true;
            }
            bool idle = parent.CqbTeams.All(t => t.Phase == TacticalCommandPhase.Complete || t.Terminal);
            if (idle)
            {
                EndHighCqb(parent);
                parent.GoalSecured |= parent.SecuredKnowledge.Cleared(parent.Goal);
                parent.FrontierCursor = 0; parent.PlanRetryAt = 0; parent.Due = tick + 1;
                if (parent.CqbCancelRequested)
                {
                    parent.CqbCancelRequested = false;
                    if (!parent.CqbObjectiveChanged)
                    { BeginReturn(parent, tick, parent.ReleaseAfterReturn, replan: parent.ReplanAfterReturn); return true; }
                    parent.CqbObjectiveChanged = false; parent.Mission = new TacticalAssaultMission();
                    parent.Plan = null; parent.Phase = TacticalCommandPhase.Pending;
                }
                return false;
            }
            return true;
        }

        private void StartHighCqb(TacticalSquadCommand parent, List<TacticalMemberCommand> active, int tick)
        {
            var groups = active.Where(m => m.Fireteam >= 0).Select(m => m.Fireteam).Distinct().OrderBy(i => i).Take(3).ToList();
            if (groups.Count < 2) return;
            ReleaseClaims(parent);
            parent.CqbLastPassage = parent.Plan; parent.CqbPassageSequence = 1;
            foreach (int group in groups)
            {
                var team = new TacticalSquadCommand { Id = parent.Id + "::cqb:" + group, CqbParent = parent, CqbTeam = group,
                    Owner = this, RaidLord = parent.RaidLord, Goal = parent.Goal, TargetFaction = parent.TargetFaction,
                    Phase = TacticalCommandPhase.Complete, Due = tick, PhaseStarted = tick, HadConnectedStack = true, CqbFollowSeen = 1 };
                team.Link.Unit = parent.Link.Unit;
                foreach (var member in parent.Members) if (member.Fireteam == group) team.Members.Add(member);
                BindCqbShared(parent, team); parent.CqbTeams.Add(team);
            }
            foreach (var commander in parent.Members.Where(m => m.Fireteam < 0 || !groups.Contains(m.Fireteam)))
            {
                var escort = parent.CqbTeams.OrderBy(t => t.Members.Where(m => Available(m, map))
                    .Select(m => m.Pawn.Position.DistanceToSquared(commander.Pawn.Position)).DefaultIfEmpty(int.MaxValue).Min()).First();
                escort.Members.Add(commander);
            }
            foreach (var team in parent.CqbTeams)
            {
                var posts = new TacticalLocalPlan { Direct = true, Opening = parent.Plan.Inside, Inward = parent.Plan.Inward };
                foreach (var member in team.Members)
                {
                    int index = parent.Members.IndexOf(member); team.CqbRoster.Add(index);
                    posts.Positions.Add(parent.Plan.Positions[index]);
                    byPawn[member.Pawn] = team; claims[posts.Positions.Last()] = team;
                }
                team.Plan = posts;
            }
            UpdateCqbPosts(parent); parent.Phase = TacticalCommandPhase.Clear;
        }

        private static void BindCqbShared(TacticalSquadCommand parent, TacticalSquadCommand team)
        {
            team.CqbParent = parent; team.Owner = parent.Owner; team.Link.Unit = parent.Link.Unit;
            team.Contacts = parent.Contacts; team.SecuredCells = parent.SecuredCells;
            team.BreachTools = parent.BreachTools; team.MedicalBags = parent.MedicalBags;
        }

        private bool AssignCqbFrontier(TacticalSquadCommand parent, TacticalSquadCommand team, int tick)
        {
            // A single mouth has one owner; other teams keep their current guard
            // positions until its room is secured or another boundary is known.
            TacticalRoomFrontier selected = null;
            if (!Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget.TryPlan(tick)) return true;
            for (int inspected = 0; inspected < 12 && parent.CqbSearchCursor < parent.Frontiers.Count; inspected++)
            {
                var frontier = parent.Frontiers[parent.CqbSearchCursor++];
                if (frontier.Excluded || frontier.Outdoors || frontier.Approach || parent.SecuredKnowledge.Cleared(frontier.Inside)
                    || parent.Link.Cooperation.Active && !parent.Link.Cooperation.Agenda.OwnsSide(parent.Id, frontier.Inside)
                    || parent.CqbTeams.Any(t => t != team && !t.Terminal && t.CqbTask != null
                        && (TacticalHighCqbPolicy.Conflicts(frontier, t.CqbTask) || t.CqbArea.Contains(frontier.Inside)))) continue;
                selected = frontier; break;
            }
            if (selected == null)
            {
                if (parent.CqbSearchCursor < parent.Frontiers.Count) return true; // yielded candidate scan
                parent.CqbSearchCursor = 0; return false;
            }
            team.CancelPreparation(); team.CqbTask = selected; team.CqbEntered = false;
            team.CqbFollow = false; team.CqbFollowGoal = IntVec3.Invalid;
            team.CqbArea = TacticalHighCqbPolicy.LocalArea(selected.Inside,
                c => c.InBounds(map) && c.Walkable(map) && c.Roofed(map) && !(c.GetEdifice(map) is RimWorld.Building_Door));
            team.Frontiers.Clear(); team.Frontiers.Add(selected); team.FrontierCursor = 0;
            team.FrontierBusy = false; team.RecoveryFrontier = null; team.RoomRecoveryUntil = 0;
            team.Mission = new TacticalAssaultMission(); team.Goal = parent.Goal;
            team.GoalSecured = parent.GoalSecured; team.Phase = TacticalCommandPhase.Clear;
            team.PlanRetryAt = 0; team.Failures = 0; team.PhaseStarted = team.Due = tick;
            return true;
        }

        private void FinishCqbRoom(TacticalSquadCommand team, int tick)
        {
            var parent = team.CqbParent;
            if (!team.CqbFollow && team.Plan != null)
            { parent.CqbLastPassage = team.Plan; team.CqbFollowSeen = ++parent.CqbPassageSequence; }
            foreach (var frontier in team.Frontiers)
                if (parent.FrontierKeys.Add(frontier.Opening)) parent.Frontiers.Add(frontier);
            parent.CqbSearchCursor = 0;
            foreach (var plan in team.SecuredPlans)
                if (!parent.SecuredPlans.Any(p => p.Opening == plan.Opening)) parent.SecuredPlans.Add(plan);
            parent.GoalSecured |= team.GoalSecured;
            // Keep one stable claimed seat per member while releasing staging.
            ReleaseClaims(team);
            if (team.Plan != null) foreach (var post in team.Plan.Positions) claims[post] = team;
            team.CqbTask = null; team.CqbArea.Clear(); team.Phase = TacticalCommandPhase.Complete; team.Due = tick + 10;
            parent.Due = tick + 1;
        }

        private bool FollowCqbPassage(TacticalSquadCommand parent, TacticalSquadCommand team, int tick)
        {
            var passage = parent.CqbLastPassage;
            if (passage == null || team.CqbFollowSeen >= parent.CqbPassageSequence
                || !TacticalSharedPassagePolicy.Cleared(parent.SecuredKnowledge, passage) || !OpeningUsable(passage)) return false;
            var available = team.Members.Where(m => Available(m, map)).ToList();
            if (available.Count == 0) return false;
            team.CqbFollowSeen = parent.CqbPassageSequence; team.CqbFollow = true; team.CqbEntered = false;
            team.CqbFollowGoal = passage.Inside; team.Phase = TacticalCommandPhase.Pending; team.Due = tick;
            team.PlanRetryAt = 0; team.Failures = 0;
            foreach (var member in team.Members) member.Passed = member.Crossed = member.Entered = member.EntryAssignmentDone = false;
            bool Claimed(IntVec3 cell) => claims.TryGetValue(cell, out var owner) && owner != team;
            team.Preparation = new TacticalPlanPreparation(team, available, passage.Inside)
            {
                Stage = TacticalPreparationStage.Search,
                Search = TacticalLocalPlanner.ClearedTransitSearch(map, available[0].Pawn, passage, team.Members.Count, Claimed)
            };
            return true;
        }

        private static void UpdateCqbPosts(TacticalSquadCommand parent)
        {
            var plan = new TacticalLocalPlan { Direct = true, Opening = parent.Goal, Inward = IntVec3.North };
            foreach (var member in parent.Members)
            {
                var team = parent.CqbTeams.FirstOrDefault(t => t.Members.Contains(member));
                int index = team?.Members.IndexOf(member) ?? -1;
                IntVec3 post = TacticalExplosiveEvasionPolicy.ReturnPost(team?.Plan, team?.Phase ?? TacticalCommandPhase.Complete, index);
                plan.Positions.Add(post.IsValid ? post : member.Pawn.Position);
            }
            parent.Plan = plan;
        }

        private void EndHighCqb(TacticalSquadCommand parent)
        {
            foreach (var team in parent.CqbTeams)
            {
                team.CancelPreparation(); ReleaseClaims(team);
                foreach (var member in team.Members) byPawn[member.Pawn] = parent;
                team.Phase = TacticalCommandPhase.Released;
            }
            parent.CqbTeams.Clear(); parent.CqbPausedAt = -1;
            parent.CqbSearchCursor = 0;
            if (parent.Plan != null) foreach (var post in parent.Plan.Positions)
                if (!claims.ContainsKey(post)) claims[post] = parent;
        }

        private bool SiblingCqbHazard(TacticalSquadCommand team)
        {
            if (team.CqbParent == null || team.Plan == null || team.Phase != TacticalCommandPhase.Enter) return false;
            foreach (var other in team.CqbParent.CqbTeams)
            {
                if (other == team) continue;
                var action = other.OpeningAction;
                if (action?.Launched == true && !action.EffectsCleared && !RaidSmokeUtility.IsScreeningProjectile(action.ProjectileDef)
                    && HazardReaches(action.Target, action.ProjectileDef, team.Plan.Inside)) return true;
                if (other.ChargeAction != null && TacticalChargePolicy.UnsafeApproach(other.ChargeAction.Position,
                    other.Plan.Inward, team.Plan.Outside)) return true;
            }
            return false;
        }
        private bool HazardReaches(IntVec3 target, ThingDef projectile, IntVec3 cell)
        {
            float radius = TacticalGrenadePolicy.HazardRadius(projectile, target.InBounds(map) && target.Roofed(map));
            // Conservative short-lived radius gate; no repeated LOS probe while
            // a sibling's live explosive is waiting to settle.
            return cell.DistanceToSquared(target) <= (radius + 2) * (radius + 2);
        }
    }
}
