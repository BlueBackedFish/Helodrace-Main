using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalMissionStage { Pursuing, Searching, Moving, Blocked, Finished }
    public sealed class TacticalAssaultMission : IExposable
    {
        public TacticalMissionStage Stage;
        public IntVec3 NextGoal = IntVec3.Invalid;
        public HashSet<IntVec3> Traversed = new HashSet<IntVec3>();
        public HashSet<IntVec3> FailedDestinations = new HashSet<IntVec3>();
        public int EmptySurveys, SearchAt, FailedPlans;
        public int ForgetFailuresAt;
        internal TacticalFacadeSurvey Survey;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Stage, "stage");
            Scribe_Values.Look(ref NextGoal, "nextGoal", IntVec3.Invalid);
            Scribe_Values.Look(ref EmptySurveys, "emptySurveys");
            Scribe_Values.Look(ref SearchAt, "searchAt");
            Scribe_Values.Look(ref FailedPlans, "failedPlans");
            Scribe_Values.Look(ref ForgetFailuresAt, "forgetFailuresAt");
            Scribe_Collections.Look(ref Traversed, "traversed", LookMode.Value);
            Scribe_Collections.Look(ref FailedDestinations, "failedDestinations", LookMode.Value);
        }
    }

    // Sixteen short sight rays. Each plan allowance visits at most 32 cells;
    // actual LOS is called only at a facade, and at most twice per slice.
    public sealed class TacticalFacadeSurvey
    {
        private static readonly IntVec3[] directions = {
            new IntVec3(1,0,0), new IntVec3(2,0,1), new IntVec3(1,0,1), new IntVec3(1,0,2),
            new IntVec3(0,0,1), new IntVec3(-1,0,2), new IntVec3(-1,0,1), new IntVec3(-2,0,1),
            new IntVec3(-1,0,0), new IntVec3(-2,0,-1), new IntVec3(-1,0,-1), new IntVec3(-1,0,-2),
            new IntVec3(0,0,-1), new IntVec3(1,0,-2), new IntVec3(1,0,-1), new IntVec3(2,0,-1) };
        public readonly IntVec3 Origin;
        private int ray, distance = 1;
        public bool Finished => ray >= directions.Length;
        public TacticalFacadeSurvey(IntVec3 origin) { Origin = origin; }
        public bool Next(out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (distance > 24) { ray++; distance = 1; }
            if (Finished) return false;
            IntVec3 direction = directions[ray];
            int scale = Math.Max(Math.Abs(direction.x), Math.Abs(direction.z));
            cell = Origin + new IntVec3(direction.x * distance / scale, 0, direction.z * distance / scale);
            distance++;
            return true;
        }
        public void StopRay() { if (distance > 1) { ray++; distance = 1; } }
    }

    public sealed partial class TacticalSquadCommand
    {
        public TacticalAssaultMission Mission = new TacticalAssaultMission();
    }

    public static class TacticalMissionRouting
    {
        public static bool CompletedChannel(TacticalSquadCommand command, IntVec3 opening, IntVec3 inward)
        {
            // Check both an outer mouth and any inner layer of a completed
            // thick channel. Roof loss must not erase physical passage history.
            for (int layer = 0; layer < TacticalPortalGeometry.LayerLimit; layer++)
            {
                IntVec3 outer = opening - inward * layer;
                if (command.Mission.Traversed.Contains(outer)
                    && command.SecuredPlans.Exists(plan => plan.Opening == outer && plan.Inward == inward && plan.Depth > layer)) return true;
            }
            return false;
        }

        public static bool UnenteredRoom(TacticalSquadCommand command, TacticalRoomFrontier frontier) =>
            !frontier.Excluded && !frontier.Outdoors && !frontier.Approach
            && !command.SecuredCells.Contains(frontier.Inside)
            && !command.SecuredKnowledge.Cleared(frontier.Inside)
            && !command.Mission.Traversed.Contains(frontier.Opening)
            && (!command.Link.Cooperation.Active || command.Link.Cooperation.Agenda.OwnsSide(command.Id, frontier.Inside));

        // An owned wall facing a yard is an exit, not evidence of a new building.
        public static bool FacadeDestination(TacticalSquadCommand command, IntVec3 opening, IntVec3 inside, bool roofed) =>
            roofed && !command.SecuredCells.Contains(inside) && !command.SecuredKnowledge.Cleared(inside)
            && !command.Mission.Traversed.Contains(opening) && !command.Mission.FailedDestinations.Contains(inside);

        public static IntVec3 TransitGoal(TacticalSquadCommand command) => command.Mission.NextGoal.IsValid
            && !command.SecuredCells.Contains(command.Mission.NextGoal)
            && !command.SecuredKnowledge.Cleared(command.Mission.NextGoal)
                ? command.Mission.NextGoal : IntVec3.Invalid;

        // Only a known, already open route can serve an observed next destination.
        // Do not breach every external boundary just to explore outdoor floor.
        public static int ExitForDestination(TacticalSquadCommand command, IntVec3 goal,
            Func<TacticalRoomFrontier, bool> open)
        {
            if (command.Plan == null || !goal.IsValid || TransitGoal(command) != goal) return -1;
            int best = -1, score = int.MaxValue;
            for (int i = 0; i < command.Frontiers.Count; i++)
            {
                TacticalRoomFrontier frontier = command.Frontiers[i];
                if (!frontier.Outdoors || frontier.Approach
                    || !command.SecuredCells.Contains(frontier.Opening - frontier.Inward) || !open(frontier)) continue;
                int candidate = frontier.Inside.DistanceToSquared(goal)
                    + frontier.Opening.DistanceToSquared(command.Plan.Inside);
                if (candidate < score) { best = i; score = candidate; }
            }
            return best;
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private void FinishOutdoorPassage(TacticalSquadCommand command, int tick)
        {
            TacticalLocalPlan plan = command.Plan;
            RetireEntryApproach(command);
            if (plan.Direct && !plan.ReportedPassage && plan.Opening == command.Goal) command.GoalSecured = true;
            if (!plan.Direct && command.Mission.Traversed.Add(plan.Opening)) command.SecuredPlans.Add(plan);
            if (!plan.Direct && !plan.ReportedPassage) FlushSecuredReport(command, tick, portalOnly: true);
            // These are alternative exits from already visited rooms, not more
            // rooms to clear. Once outside, search the next building rather than
            // walking back inside to breach every remaining exterior wall tile.
            if (!plan.Direct && !plan.Inside.Roofed(map))
                foreach (TacticalRoomFrontier frontier in command.Frontiers)
                    if (frontier.Outdoors) frontier.Excluded = true;
            IntVec3 transit = TacticalMissionRouting.TransitGoal(command);
            if (transit.IsValid && (plan.ReportedPassage || !(plan.Direct ? plan.Opening : plan.Inside).Roofed(map)))
            {
                // An outdoor/previously cleared passage is one leg of the latched
                // mission. Keep its destination instead of surveying our own facade.
                SelectMissionDestination(command, transit, tick); return;
            }
            command.Mission.NextGoal = IntVec3.Invalid; command.Mission.FailedPlans = 0;
            command.Mission.Stage = TacticalMissionStage.Searching; command.Mission.Survey = null;
            command.Mission.EmptySurveys = 0; command.Mission.SearchAt = tick;
            command.RoomScan = null; command.Phase = TacticalCommandPhase.Clear;
            command.PhaseStarted = tick; command.Due = tick + 1;
        }
        private bool ContinueAssaultMission(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalAssaultMission mission = command.Mission;
            if (tick < mission.SearchAt) { command.Due = mission.SearchAt; return true; }
            if (tick >= mission.ForgetFailuresAt) mission.FailedDestinations.Clear();
            Pawn observer = PendingPlanner(active, command.Goal);
            if (mission.Survey == null) mission.Survey = new TacticalFacadeSurvey(observer.Position);
            TacticalFacadeSurvey survey = mission.Survey;
            int sightChecks = 0;
            for (int i = 0; i < 32 && survey.Next(out IntVec3 cell); i++)
            {
                if (!cell.InBounds(map)) { survey.StopRay(); continue; }
                Building barrier = cell.GetEdifice(map);
                if (!(barrier is Building_Door) && barrier?.def.IsWall != true) continue;
                IntVec3 travel = cell - survey.Origin;
                bool Support(IntVec3 c) => c.InBounds(map) && (c.GetEdifice(map)?.def.IsWall == true || c.GetEdifice(map) is Building_Door);
                if (!TacticalPortalGeometry.TryEntryFrontage(cell, travel, Support,
                    (mouth, direction) => TacticalLocalPlanner.EntryDirection(map, mouth, direction), out IntVec3 inward, out IntVec3 frontageCell))
                    inward = Math.Abs(travel.x) >= Math.Abs(travel.z)
                        ? new IntVec3(Math.Sign(travel.x), 0, 0) : new IntVec3(0, 0, Math.Sign(travel.z));
                else cell = frontageCell;
                bool hasFloor = TacticalLocalPlanner.TryDepth(map, cell, inward, out int depth);
                IntVec3 inside = cell + inward * depth;
                // An open exit is transparent. Stopping the ray at our own open
                // door would prevent seeing the next building across the yard.
                if (hasFloor && inside.InBounds(map) && !inside.Roofed(map)
                    && barrier is Building_Door door && (door.Open || DoorBreachFaultUtility.Jammed(door))) continue;
                survey.StopRay(); // No hidden building search through this facade.
                if (++sightChecks > 2) break;
                if (!GenSight.LineOfSight(observer.Position, cell, map, true) || !hasFloor) continue;
                if (!inside.InBounds(map) || !TacticalMissionRouting.FacadeDestination(command, cell, inside, inside.Roofed(map))) continue;
                if (command.Link.Cooperation.Active && !command.Link.Cooperation.Agenda.OwnsSide(command.Id, inside)) continue;
                TacticalStructureClass kind = ClassifyStructure(command, cell, inward, observer);
                if (kind == TacticalStructureClass.Pending)
                {
                    // The candidate survives a yielded classification; do not
                    // discard it because this survey already stopped its ray.
                    int index = command.Frontiers.FindIndex(f => f.Opening == cell);
                    if (index < 0)
                    {
                        command.FrontierKeys.Add(cell);
                        command.Frontiers.Add(new TacticalRoomFrontier { Opening = cell, Inward = inward, Depth = depth, Approach = true });
                        index = command.Frontiers.Count - 1;
                    }
                    else
                    {
                        TacticalRoomFrontier frontier = command.Frontiers[index];
                        frontier.Excluded = false; frontier.Approach = true;
                        frontier.Inward = inward; frontier.Depth = depth; frontier.Outdoors = false;
                    }
                    command.FrontierCursor = index; command.Due = tick + 1; return true;
                }
                if (kind != TacticalStructureClass.Target) continue;
                IntVec3 approach = cell - inward;
                SelectMissionDestination(command, inside, tick, approach.InBounds(map) && !approach.Roofed(map)); return true;
            }
            if (!survey.Finished) { command.Due = tick + 1; return true; }
            mission.Survey = null;
            if (++mission.EmptySurveys < 2) { mission.SearchAt = tick + 300; command.Due = mission.SearchAt; return true; }
            bool unresolved = command.Frontiers.Exists(f => TacticalMissionRouting.UnenteredRoom(command, f));
            if ((command.GoalSecured || command.SecuredKnowledge.Cleared(command.Goal)) && !unresolved)
            { mission.Stage = TacticalMissionStage.Finished; return false; }
            // No found candidate is not a retreat or permission to hand the
            // squad back to a vanilla raid which may choose an unrelated ruin.
            mission.Stage = TacticalMissionStage.Blocked; mission.NextGoal = IntVec3.Invalid;
            if (unresolved)
            {
                command.Phase = TacticalCommandPhase.Clear; command.FrontierCursor = 0;
                command.LastPlanFailure |= TacticalPlanFailure.Unsecured;
                mission.SearchAt = tick + 600;
            }
            else command.Phase = TacticalCommandPhase.Pending;
            command.PlanRetryAt = tick + 600;
            command.Due = command.PlanRetryAt; return true;
        }
        private void SelectMissionDestination(TacticalSquadCommand command, IntVec3 goal, int tick, bool outdoorApproach = false)
        {
            command.Mission.NextGoal = goal; command.Mission.Stage = TacticalMissionStage.Moving;
            command.Mission.Survey = null; command.Mission.EmptySurveys = 0; command.Mission.FailedPlans = 0;
            if (outdoorApproach && command.Plan != null && command.Plan.Inside.InBounds(map) && command.Plan.Inside.Roofed(map))
            {
                int exit = TacticalMissionRouting.ExitForDestination(command, goal,
                    frontier => TacticalLocalPlanner.PassageOpen(map, frontier.Opening, frontier.Inward, frontier.Depth));
                if (exit >= 0)
                {
                    command.Frontiers[exit].Excluded = false; command.FrontierCursor = exit;
                    command.RoomScan = null; command.RecoveryFrontier = null; command.RoomRecoveryUntil = 0;
                    command.Phase = TacticalCommandPhase.Clear; command.PlanRetryAt = 0;
                    command.Mission.SearchAt = tick; command.FrontierBusy = false; command.Due = tick + 1;
                    return;
                }
            }
            ReleaseClaims(command);
            command.Plan = null; command.OpeningAction = null; command.RoomScan = null;
            command.Phase = TacticalCommandPhase.Pending; command.PhaseStarted = tick;
            command.PlanRetryAt = 0; command.Failures = 0; command.Due = tick + 1;
            foreach (TacticalMemberCommand member in command.Members)
            {
                member.Passed = member.Crossed = member.Entered = member.EntryAssignmentDone = false;
                member.Parking = IntVec3.Invalid; member.RetryTick = 0;
            }
        }
    }
}
