using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using Verse;

namespace Helodrace.SquadAI
{
    public sealed partial class MapComponent_SquadAI
    {
        private void Plan(Squad squad, IntVec3 focus, int tick, bool rebuildTeams)
        {
            squad.Focus = focus; squad.NextPlan = tick + SquadPolicy.ReplanTicks;
            if (rebuildTeams || squad.Teams.Count == 0) BuildTeams(squad);
            List<IntVec3> taken = OtherPosts(squad);
            switch (squad.Stage)
            {
                case SquadStage.Brace:
                    foreach (SquadTeam team in squad.Teams) Hold(squad, team, squad.Centroid(team.Members), taken, tick);
                    break;
                case SquadStage.Engage: PlanEngage(squad, taken, tick); break;
                case SquadStage.Advance: PlanAdvance(squad, taken, tick); break;
            }
            ApplyDuties(squad, true);
        }

        // Radio squads keep their real fireteams; the squad leader joins the
        // smallest one so he moves with a team. Other squads start as one team
        // and split a temporary flank team off when they engage.
        // Membership changes keep each team's task, so a casualty does not
        // restart a flank or a bound that is already under way.
        private static void BuildTeams(Squad squad)
        {
            var previous = squad.Teams.ToList();
            squad.Teams.Clear();
            void Keep(SquadTeam team, SquadTeam old)
            {
                if (old == null) return;
                team.Task = old.Task; team.Flank = old.Flank; team.TaskStarted = old.TaskStarted; team.Zone = old.Zone;
            }
            foreach (CombatGroup group in squad.Unit.Group.children)
            {
                var team = new SquadTeam { Group = group };
                team.Members.AddRange(group.AllMembers.Where(squad.Live.Contains));
                if (team.Members.Count == 0) continue;
                Keep(team, previous.Find(old => old.Group == group));
                squad.Teams.Add(team);
            }
            SquadTeam oldFlank = previous.Find(old => old.Group == null && old.Flank);
            if (oldFlank != null && oldFlank.Members.Count(squad.Live.Contains) >= 2)
            {
                var flank = new SquadTeam();
                flank.Members.AddRange(oldFlank.Members.Where(squad.Live.Contains));
                Keep(flank, oldFlank);
                squad.Teams.Add(flank);
            }
            var loose = squad.Live.Where(pawn => !squad.Teams.Any(team => team.Members.Contains(pawn))).ToList();
            if (squad.Unit.Group.children.Count == 0)
            {
                var main = new SquadTeam(); main.Members.AddRange(loose);
                Keep(main, previous.Find(old => old.Group == null && !old.Flank));
                if (main.Members.Count > 0) squad.Teams.Insert(0, main);
                return;
            }
            foreach (Pawn pawn in loose)
            {
                SquadTeam smallest = squad.Teams.Where(team => !team.Flank).DefaultIfEmpty(squad.Teams.FirstOrDefault())
                    .OrderBy(team => team?.Members.Count ?? 0).FirstOrDefault();
                if (smallest == null) { smallest = new SquadTeam(); squad.Teams.Add(smallest); }
                smallest.Members.Add(pawn);
            }
        }

        private void Hold(Squad squad, SquadTeam team, IntVec3 center, List<IntVec3> taken, int tick)
        {
            IntVec3 usable = SquadPlanner.NearestUsable(center, map);
            SquadPlanner.PlaceTeam(team, usable.IsValid ? usable : center, squad.Focus, squad.Range, map, taken, tick, TeamTask.Hold);
        }

        private void PlanEngage(Squad squad, List<IntVec3> taken, int tick)
        {
            IntVec3 focus = squad.Focus;
            SquadTeam flank = squad.Teams.Find(team => team.Flank);
            if (flank != null && flank.Members.Count(squad.Live.Contains) < 2) { MergeFlank(squad, flank); flank = null; }
            if (flank == null) flank = StartFlank(squad, taken, tick);
            else if (flank.Task == TeamTask.Move)
            {
                // Re-route an unfinished flank onto the moved focus, or stop it.
                IntVec3 zone = SquadPlanner.FlankZone(focus, squad.Centroid(squad.Teams.Where(t => t != flank).SelectMany(t => t.Members)),
                    squad.Centroid(flank.Members), squad.Range, flank.Members[0], map);
                if (zone.IsValid) SquadPlanner.PlaceTeam(flank, zone, focus, squad.Range, map, taken, tick, TeamTask.Move);
                else Hold(squad, flank, squad.Centroid(flank.Members), taken, tick);
            }
            else Hold(squad, flank, flank.Zone, taken, tick);
            List<SquadTeam> bases = squad.Teams.Where(team => team != flank).ToList();
            if (bases.Count == 0) return;
            IntVec3 center = squad.Centroid(bases.SelectMany(team => team.Members));
            // Out of effective range: the base of fire steps in to standoff distance.
            if (SquadPlanner.Distance(center, focus) > squad.Range * SquadPolicy.AdvanceFactor)
                center = SquadPlanner.Along(focus, center, squad.Range * SquadPolicy.BaseStandoffFactor);
            for (int i = 0; i < bases.Count; i++)
            {
                double offset = (i - (bases.Count - 1) / 2.0) * SquadPolicy.TeamLateralSpacing;
                Hold(squad, bases[i], SquadPlanner.Lateral(center, focus, offset), taken, tick);
            }
        }

        private SquadTeam StartFlank(Squad squad, List<IntVec3> taken, int tick)
        {
            IntVec3 focus = squad.Focus, squadCenter = squad.Centroid();
            SquadTeam flank;
            if (squad.Teams.Count >= 2)
            {
                // The first real fireteam that can still move as a team flanks.
                flank = squad.Teams.FirstOrDefault(team => team.Group != null && team.Members.Count(squad.Live.Contains) >= 2);
                if (flank == null) return null;
                IntVec3 others = squad.Centroid(squad.Teams.Where(team => team != flank).SelectMany(team => team.Members));
                IntVec3 zone = SquadPlanner.FlankZone(focus, others, squad.Centroid(flank.Members), squad.Range, flank.Members[0], map);
                if (!zone.IsValid) return null;
                flank.Flank = true;
                SquadPlanner.PlaceTeam(flank, zone, focus, squad.Range, map, taken, tick, TeamTask.Move);
                return flank;
            }
            SquadTeam main = squad.Teams[0];
            int size = SquadPolicy.FlankSize(main.Members.Count);
            if (size == 0) return null;
            Pawn leader = main.Members.FirstOrDefault(pawn => !Automatic(pawn) && pawn != squad.Unit.Commander);
            if (leader == null) return null;
            IntVec3 flankZone = SquadPlanner.FlankZone(focus, squadCenter, squadCenter, squad.Range, leader, map);
            if (!flankZone.IsValid) return null;
            // Riflemen nearest the chosen side go; automatic weapons and the
            // squad leader stay with the base of fire.
            var chosen = main.Members.Where(pawn => !Automatic(pawn) && pawn != squad.Unit.Commander)
                .OrderBy(pawn => pawn.Position.DistanceToSquared(flankZone)).Take(size).ToList();
            if (chosen.Count < 3 || main.Members.Count - chosen.Count < 2) return null;
            flank = new SquadTeam { Flank = true };
            flank.Members.AddRange(chosen);
            main.Members.RemoveAll(chosen.Contains);
            squad.Teams.Add(flank);
            SquadPlanner.PlaceTeam(flank, flankZone, focus, squad.Range, map, taken, tick, TeamTask.Move);
            return flank;
        }

        private static void MergeFlank(Squad squad, SquadTeam flank)
        {
            flank.Flank = false;
            if (flank.Group != null) return;
            squad.Teams.Remove(flank);
            squad.Teams.First().Members.AddRange(flank.Members);
        }

        private static bool Automatic(Pawn pawn)
        {
            string function = OrganizationAPI.GetGroup(pawn)?.roleAssignments.FirstOrDefault(a => a.pawn == pawn)?.combatRole?.combatFunction;
            return function == "HAR" || function == "MachineGun";
        }

        // Bounding: every team holds where it stands, then the team farthest
        // from the enemy moves forward to the next covered zone.
        private void PlanAdvance(Squad squad, List<IntVec3> taken, int tick)
        {
            foreach (SquadTeam team in squad.Teams)
                if (team.Task != TeamTask.Move || team.Posts.Count == 0) Hold(squad, team, squad.Centroid(team.Members), taken, tick);
                else SquadPlanner.PlaceTeam(team, team.Zone, squad.Focus, squad.Range, map, taken, tick, TeamTask.Move);
            NextBound(squad, taken, tick);
        }

        private bool NextBound(Squad squad, List<IntVec3> taken, int tick)
        {
            if (squad.Teams.Any(team => team.Task == TeamTask.Move)) return false;
            SquadTeam mover = null; double farthest = 0;
            foreach (SquadTeam team in squad.Teams)
            {
                double distance = SquadPlanner.Distance(squad.Centroid(team.Members), squad.Focus);
                if (distance > farthest) { farthest = distance; mover = team; }
            }
            if (mover == null || farthest <= squad.Range * SquadPolicy.AdvanceFactor) return false;
            double step = Math.Max(SquadPolicy.BoundMinimum, Math.Min(SquadPolicy.BoundMaximum, farthest - squad.Range * SquadPolicy.BaseStandoffFactor));
            IntVec3 zone = SquadPlanner.NearestUsable(SquadPlanner.Along(squad.Centroid(mover.Members), squad.Focus, step), map);
            if (!zone.IsValid) return false;
            foreach (IntVec3 post in mover.Posts.Values) taken.Remove(post);
            SquadPlanner.PlaceTeam(mover, zone, squad.Focus, squad.Range, map, taken, tick, TeamTask.Move);
            return true;
        }

        // Between plans: finish moves that arrived or timed out, and in Advance
        // start the next bound.
        private void Progress(Squad squad, int tick)
        {
            bool changed = false;
            List<IntVec3> taken = null;
            foreach (SquadTeam team in squad.Teams)
            {
                if (team.Task != TeamTask.Move) continue;
                int timeout = team.Flank ? SquadPolicy.FlankTimeoutTicks : SquadPolicy.MoveTimeoutTicks;
                if (Arrived(squad, team)) { team.Task = TeamTask.Hold; changed = true; }
                else if (tick - team.TaskStarted > timeout)
                {
                    // Stuck short of the zone: hold in cover where they are.
                    taken = taken ?? OtherPosts(squad);
                    Hold(squad, team, squad.Centroid(team.Members), taken, tick); changed = true;
                }
            }
            if (squad.Stage == SquadStage.Advance)
            {
                taken = taken ?? OtherPosts(squad);
                foreach (SquadTeam team in squad.Teams) taken.AddRange(team.Posts.Values);
                changed |= NextBound(squad, taken, tick);
            }
            if (changed) ApplyDuties(squad, true);
        }

        private static bool Arrived(Squad squad, SquadTeam team)
        {
            int live = 0, arrived = 0;
            foreach (KeyValuePair<Pawn, IntVec3> pair in team.Posts)
            {
                if (!squad.Live.Contains(pair.Key)) continue;
                live++;
                if (pair.Key.Position.InHorDistOf(pair.Value, SquadPolicy.MoveArrivalRadius)) arrived++;
            }
            return live == 0 || arrived >= Math.Ceiling(live * SquadPolicy.ArrivedShare);
        }
    }
}
