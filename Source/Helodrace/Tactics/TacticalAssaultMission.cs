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
                survey.StopRay(); // No hidden building search through this facade.
                if (++sightChecks > 2) break;
                if (!GenSight.LineOfSight(observer.Position, cell, map, true)) continue;
                IntVec3 travel = cell - survey.Origin;
                bool Support(IntVec3 c) => c.InBounds(map) && (c.GetEdifice(map)?.def.IsWall == true || c.GetEdifice(map) is Building_Door);
                if (!TacticalPortalGeometry.TryWallNormal(cell, travel, Support, out IntVec3 inward))
                    inward = Math.Abs(travel.x) >= Math.Abs(travel.z)
                        ? new IntVec3(Math.Sign(travel.x), 0, 0) : new IntVec3(0, 0, Math.Sign(travel.z));
                if (!TacticalLocalPlanner.TryDepth(map, cell, inward, out int depth)) continue;
                IntVec3 inside = cell + inward * depth;
                if (!inside.InBounds(map) || command.SecuredCells.Contains(inside) || command.SecuredKnowledge.Cleared(inside) || mission.Traversed.Contains(cell)
                    || mission.FailedDestinations.Contains(inside)) continue;
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
                    else { command.Frontiers[index].Excluded = false; command.Frontiers[index].Approach = true; command.Frontiers[index].Depth = depth; }
                    command.FrontierCursor = index; command.Due = tick + 1; return true;
                }
                if (kind != TacticalStructureClass.Target) continue;
                SelectMissionDestination(command, inside, tick); return true;
            }
            if (!survey.Finished) { command.Due = tick + 1; return true; }
            mission.Survey = null;
            if (++mission.EmptySurveys < 2) { mission.SearchAt = tick + 300; command.Due = mission.SearchAt; return true; }
            bool unresolved = command.Frontiers.Exists(f => !f.Excluded && !f.Approach
                && !command.SecuredCells.Contains(f.Inside) && !mission.Traversed.Contains(f.Opening)
                && !command.SecuredKnowledge.Cleared(f.Inside)
                && (!command.Link.Cooperation.Active || command.Link.Cooperation.Agenda.OwnsSide(command.Id, f.Inside)));
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
        private void SelectMissionDestination(TacticalSquadCommand command, IntVec3 goal, int tick)
        {
            ReleaseClaims(command);
            command.Mission.NextGoal = goal; command.Mission.Stage = TacticalMissionStage.Moving;
            command.Mission.Survey = null; command.Mission.EmptySurveys = 0; command.Mission.FailedPlans = 0;
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
