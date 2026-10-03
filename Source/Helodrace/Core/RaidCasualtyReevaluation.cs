using System.Collections.Generic;
using System.Linq;
using Helodrace.Squads;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        // Coalesce multiple casualties from one explosion. Never start jobs or
        // rebuild plans inside Pawn.Kill while its corpse and Lord are changing.
        private readonly Dictionary<string, HashSet<Pawn>> pendingCasualties =
            new Dictionary<string, HashSet<Pawn>>();

        public void RequestCasualtyReevaluation(string organizationId, Pawn pawn)
        {
            if (organizationId == null || pawn == null) return;
            // Capture before WorldPawns clears the deceased's organization ID.
            RememberBreachTools(organizationId, pawn);
            if (!pendingCasualties.TryGetValue(organizationId, out HashSet<Pawn> losses))
                pendingCasualties.Add(organizationId, losses = new HashSet<Pawn>());
            losses.Add(pawn);
        }

        private void ReconcileCasualties(CombatOrganization organization, List<Pawn> members,
            ExecutionState state, int tick)
        {
            if (state.Breacher != null && !members.Contains(state.Breacher))
            {
                CompInstalledBreachCharge charge = state.BreachKind == RaidBreachKind.C4
                    ? BreachExplosiveUtility.ChargeOnWall(state.BreachTarget) : null;
                bool detonating = charge?.Triggered == true || state.Phase == RaidExecutionPhase.Detonation;
                if (!detonating) CancelPendingCharge(state);
                ResetLostBreacher(state, tick, detonating);
                MapComponent_RaidTacticalTrace.Record(members[0], detonating
                    ? "Breacher lost; waiting for already triggered demolition"
                    : "Breacher lost; select replacement at the committed opening");
            }
            if (state.Thrower != null && !members.Contains(state.Thrower))
                ResetLostSupport(state, tick);
            if (state.ApproachSmokeActive && state.ApproachSmokeThrower != null
                && !members.Contains(state.ApproachSmokeThrower) && !state.ApproachSmokeLaunched)
            {
                state.ApproachSmokeActive = false;
                state.ApproachSmokeThrower = null;
                state.NextApproachSmokeTick = tick;
            }

            RaidTacticalPlan plan = state.ActivePlan;
            if (plan == null) return;
            int removed = plan.Assignments.RemoveAll(assignment => !members.Contains(assignment.Pawn));
            state.Crossings.RemoveAll(crossing => !members.Contains(crossing.Pawn));
            int originalPersonnel = organization.rootGroups.Sum(root => root.formation?.StandardPersonnel ?? 0);
            plan.CasualtyFraction = originalPersonnel == 0 ? 0f
                : Mathf.Clamp01(1f - members.Count / (float)originalPersonnel);
            plan.CommandEfficiency = organization.rootGroups.Count == 0 ? 1f
                : organization.AllGroups.Min(group => group.CommandEfficiency);
            if (removed == 0) return;
            state.ReadySince = -1;
            ReplaceLostEntryTeam(plan, state);
            MapComponent_RaidTacticalTrace.Record(members[0],
                $"Casualty reassessment: removed {removed} unavailable assignments; retain objective and route");
        }

        internal static void ResetLostBreacher(ExecutionState state, int tick, bool detonating)
        {
            state.Breacher = null;
            if (detonating)
            {
                if (state.Phase != RaidExecutionPhase.Detonation)
                    Advance(state, RaidExecutionPhase.Detonation, tick);
                return;
            }
            state.BreachTarget = null;
            state.BreachKind = RaidBreachKind.None;
            state.WithdrawalIssued = false;
            state.BreachAttempts = 0;
            state.PhaseStarted = tick;
            if (state.Phase == RaidExecutionPhase.Breach
                || state.Phase == RaidExecutionPhase.WithdrawFromCharge
                || state.Phase == RaidExecutionPhase.Detonation)
                Advance(state, RaidExecutionPhase.Breach, tick);
        }

        internal static void ResetLostSupport(ExecutionState state, int tick)
        {
            state.SupportReturnRequired = false;
            // Preserve the deceased instigator and live grenade so explosion
            // completion is still tracked. A death must never unlock early entry.
            if (state.SupportLaunched) return;
            state.Thrower = null;
            state.SupportIssued = false;
            state.SupportProjectile = null;
            state.SupportProjectileDef = null;
            state.SupportEffectsClearedTick = -1;
            state.SupportStatus = "Thrower lost before launch; selecting replacement";
            if (state.Phase == RaidExecutionPhase.Support || state.Phase == RaidExecutionPhase.EntryWait)
                Advance(state, RaidExecutionPhase.Support, tick);
        }

        private void ReplaceLostEntryTeam(RaidTacticalPlan plan, ExecutionState state)
        {
            if (plan.IsDefensive || plan.Selected?.Maneuver == RaidTacticalManeuver.Regroup
                || plan.Selected?.Maneuver == RaidTacticalManeuver.HoldAndCounterattack
                || plan.Assignments.Any(assignment => assignment.Task == RaidTacticalTask.Entry)) return;
            RaidTacticalAssignment replacement = plan.Assignments
                .Where(assignment => assignment.Task == RaidTacticalTask.Security
                    || assignment.Task == RaidTacticalTask.FireSupport)
                .OrderBy(assignment => assignment.Pawn.Position.DistanceToSquared(plan.Entry)).FirstOrDefault();
            if (replacement == null) return;
            bool waitingOutside = state.Phase == RaidExecutionPhase.Assemble
                || state.Phase == RaidExecutionPhase.Breach || state.Phase == RaidExecutionPhase.WithdrawFromCharge
                || state.Phase == RaidExecutionPhase.Detonation || state.Phase == RaidExecutionPhase.Support
                || state.Phase == RaidExecutionPhase.EntryWait;
            if (waitingOutside && plan.BreachCell.IsValid)
            {
                var occupied = new HashSet<IntVec3>(plan.Assignments
                    .Where(assignment => assignment != replacement).Select(assignment => assignment.Position));
                IntVec3 cell = plan.SafeStackCells.Where(candidate => candidate.InBounds(map)
                    && candidate.Standable(map) && !occupied.Contains(candidate))
                    .OrderBy(candidate => candidate.DistanceToSquared(replacement.Pawn.Position))
                    .Where(candidate => replacement.Pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!cell.IsValid) return;
                replacement.Position = cell;
            }
            replacement.Task = RaidTacticalTask.Entry;
            replacement.EntryOrder = 1;
            state.DoorStateSignature = null;
            MapComponent_RaidTacticalTrace.Record(replacement.Pawn,
                "Entry team lost; reserve takes over at the existing opening");
        }
    }
}
