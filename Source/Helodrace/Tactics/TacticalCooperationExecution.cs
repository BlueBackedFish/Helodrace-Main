using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class MapComponent_TacticalCommands
    {
        public long CooperationWaits, CooperationStarts, AllocatedAreasSecured, IdentificationHolds;
        private bool AdvanceCoordination(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalSquadLink link = command.Link;
            TacticalCooperationState agreement = link.Cooperation;
            if (agreement.Expired(tick) && (agreement.Active || agreement.Negotiating))
                Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications.Announce(command, tick, true);
            // Only abandon a pre-breach placement. Live explosives and ingress
            // retain ownership and their actual passage/safety history.
            if (link.ResetPlan)
            {
                link.ResetPlan = false;
                if (command.Phase == TacticalCommandPhase.Stack && command.ChargeAction == null)
                {
                    ReleaseClaims(command); command.Plan = null;
                    command.Phase = TacticalCommandPhase.Pending; command.PlanRetryAt = 0;
                    command.PhaseStarted = tick; command.Failures = 0;
                    foreach (TacticalMemberCommand member in command.Members) member.Parking = IntVec3.Invalid;
                }
            }
            bool liveAction = command.ChargeAction != null
                || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared;
            if (tick < link.IdentifyUntil && !liveAction && command.ContactResponse == null)
            {
                if (!link.IdentificationHolding)
                {
                    link.IdentificationHolding = true; IdentificationHolds++;
                    // Positions are frozen once, not recalculated every update.
                    foreach (TacticalMemberCommand member in active)
                    {
                        member.Parking = IntVec3.Invalid;
                        member.Entered = member.EntryAssignmentDone = false;
                    }
                    if (command.OpeningAction != null)
                    {
                        // Identification interrupts an unfinished observation
                        // or preparation, not ingress or an already secured room.
                        // A completed opening action can still be retained there.
                        command.OpeningAction = null;
                        if (command.Phase == TacticalCommandPhase.Observe || command.Phase == TacticalCommandPhase.Support)
                            command.Phase = TacticalCommandPhase.Observe;
                    }
                }
                foreach (TacticalMemberCommand member in active) EnsureParking(command, member, tick);
                command.Due = tick + 1; return true;
            }
            if (link.IdentificationHolding)
            {
                link.IdentificationHolding = false;
                command.PhaseStarted += System.Math.Max(0, tick - link.IdentifyStarted);
                // Only this squad's temporary positions; never scan all map claims.
                foreach (TacticalMemberCommand member in command.Members)
                {
                    IntVec3 cell = member.Parking;
                    if (cell.IsValid && !OriginalClaim(command, cell)
                        && claims.TryGetValue(cell, out TacticalSquadCommand owner) && owner == command) claims.Remove(cell);
                    member.Parking = IntVec3.Invalid;
                }
                command.ContactRestoring = command.Phase == TacticalCommandPhase.Clear && command.Plan != null;
                if (command.ContactRestoring)
                {
                    // AdvanceCore restores posts before coordination. Yield now
                    // so this newly requested restoration runs next tick before
                    // clearing can return a supposedly completed squad.
                    command.Due = tick + 1; return true;
                }
            }
            if (command.Phase == TacticalCommandPhase.Pending && (agreement.Negotiating
                || agreement.Stage == TacticalAgreementStage.None && squads.Count > 1 && tick < link.OpportunityUntil))
            {
                foreach (TacticalMemberCommand member in active) EnsureParking(command, member, tick);
                CooperationWaits++; command.Due = tick + 15; return true;
            }
            return false;
        }
        private bool CooperationReady(TacticalSquadCommand command, int tick)
        {
            TacticalCooperationState agreement = command.Link.Cooperation;
            if (!agreement.Active) return true;
            agreement.LocalReady = true;
            if (!agreement.CanStart(tick, true))
            { CooperationWaits++; command.Due = System.Math.Min(command.Due, tick + 15); return false; }
            if (agreement.Stage != TacticalAgreementStage.Started)
            { agreement.Stage = TacticalAgreementStage.Started; CooperationStarts++; }
            return true;
        }
        private static bool AllocatedAreaSecured(TacticalSquadCommand command) => command.Link.Cooperation.Active
            && command.SecuredCells.Contains(command.Link.Cooperation.Agenda.Area(command.Id));
        private static bool ResumeExpiredAllocation(TacticalSquadCommand command, int tick)
        {
            TacticalCooperationState agreement = command.Link.Cooperation;
            if (command.Phase != TacticalCommandPhase.Complete || command.Defensive || command.Plan == null
                || command.SecuredPlans.Count == 0 || agreement.Agenda == null
                || agreement.Stage != TacticalAgreementStage.Finished && agreement.Stage != TacticalAgreementStage.Aborted
                || agreement.Stage != TacticalAgreementStage.Aborted && tick < agreement.Agenda.Deadline)
                return false;
            // A local allocated area is not a promise that an unavailable peer
            // finished the mission. Use only received reports, never peer state.
            if (agreement.Stage == TacticalAgreementStage.Finished && agreement.PeerFinished
                && (command.GoalSecured || agreement.PeerGoalSecured)) return false;
            if (command.GoalSecured && !command.Frontiers.Exists(f => !command.SecuredCells.Contains(f.Inside)))
                return false;
            agreement.Abort();
            command.Phase = TacticalCommandPhase.Clear; command.PhaseStarted = tick;
            command.FrontierCursor = 0; command.FrontierBusy = false;
            command.PlanRetryAt = 0; command.Due = tick + 1;
            // The completed entry's local snapshot, secured cells and crossing
            // history survive. Do not re-register, resurvey or reclaim old posts.
            return true;
        }
        private static IntVec3 CooperativeGoal(TacticalSquadCommand command) => command.Link.Cooperation.Active
            ? command.Link.Cooperation.Agenda.Area(command.Id) : command.Goal;
    }
}
