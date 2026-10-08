using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace.Tactics
{
    public sealed partial class GameComponent_TacticalCommands
    {
        public static bool IsDefensePhase(LordToil toil) => toil is LordToil_DefendPoint || toil is LordToil_DefendBase;
        public static bool IsDefensiveLord(Lord lord) => lord != null && !IsAssaultLord(lord) && (IsDefensePhase(lord.CurLordToil)
            || lord.ownedPawns.Any(p => p.Faction != Faction.OfPlayer && (p.mindState?.duty?.def == DutyDefOf.Defend
                || p.mindState?.duty?.def == DutyDefOf.DefendBase)));
        public static bool IsTacticalLord(Lord lord) => IsAssaultLord(lord) || IsDefensiveLord(lord);
        public static bool CanControlMission(bool defensive, bool hostileToPlayer, bool explicitGoal) =>
            defensive || hostileToPlayer || explicitGoal;
        internal static bool IsTacticalPawn(Pawn pawn) => IsAssaultLord(pawn.GetLord())
            || pawn.Faction != Faction.OfPlayer && pawn.GetLord() != null
                && (pawn.mindState?.duty?.def == DutyDefOf.Defend || pawn.mindState?.duty?.def == DutyDefOf.DefendBase);
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private void BeginDefense(TacticalSquadCommand command, int tick)
        {
            Pawn member = command.Members.First(m => m.Pawn.Spawned).Pawn;
            IntVec3 focus = member.mindState?.duty?.focus.Cell ?? IntVec3.Invalid;
            command.DefenseAnchor = focus.InBounds(map) && focus.Standable(map) ? focus : member.Position;
            command.Goal = command.DefenseAnchor; command.Defensive = true; command.DefenseRestoring = false;
            command.Phase = TacticalCommandPhase.Defending; command.PhaseStarted = tick;
            command.MedicalWindowUntil = int.MaxValue;
        }
        // Checked at squad decision frequency, never from pawn path requests.
        private bool UpdateDefensiveMission(TacticalSquadCommand command, int tick)
        {
            bool defending = GameComponent_TacticalCommands.IsDefensiveLord(command.RaidLord);
            if (defending == command.Defensive) return false;
            if (!GameComponent_TacticalCommands.CanControlMission(defending, command.Link.Unit.Faction.HostileTo(Faction.OfPlayer), HasExplicitGoal))
            { Release(command); return true; }
            // Leave a live explosive's existing controller running, including
            // the thrower's return. Freezing it here could prevent its safety
            // condition from ever finishing after a native duty change.
            if (command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared
                || command.ChargeAction?.Detonated == true && !command.ChargeAction.EffectsCleared
                || command.ChargeAction?.Charge?.Triggered == true && !command.ChargeAction.EffectsCleared)
                return false;
            AbandonCharge(command);
            Current.Game.GetComponent<GameComponent_TacticalCommands>().Communications.Announce(command, tick, true);
            EndFieldResponse(command, tick);
            ReleaseClaims(command); command.Plan = null; command.OpeningAction = null;
            command.ContactResponse = null; command.ContactRestoring = false;
            command.RoomScan = null; command.ReturnCursor = 0; command.ReleaseAfterReturn = false;
            command.GoalSecured = false; command.FrontierBusy = false;
            command.Frontiers.Clear(); command.FrontierKeys.Clear(); command.RecoveryFrontier = null;
            command.PlanRetryAt = 0; command.Due = tick + 1;
            if (command.MedicalCare != null) command.MedicalCare.CancelRequested = true;
            if (defending) BeginDefense(command, tick);
            else
            {
                command.Defensive = command.DefenseRestoring = false;
                command.Goal = Goal(tick); command.Phase = TacticalCommandPhase.Pending;
                command.PhaseStarted = tick; command.ContactHandledAt = tick;
                command.MedicalWindowUntil = 0;
                foreach (TacticalMemberCommand member in command.Members)
                    member.Passed = member.Crossed = member.Entered = member.EntryAssignmentDone = false;
            }
            return false;
        }
        private void AdvanceDefenseIdle(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            if (!command.DefenseRestoring && active.All(m => m.Job == null)) return;
            // Native Defend/DefendBase duties already know their guard radius
            // and destination. Return owned response jobs with the shared
            // one-per-tick return budget instead of issuing another formation
            // and another set of path searches for every defender.
            command.DefenseRestoring = false; command.ReturnCursor = 0;
            command.Phase = TacticalCommandPhase.Returning; command.Due = tick + 1;
        }
    }
}
