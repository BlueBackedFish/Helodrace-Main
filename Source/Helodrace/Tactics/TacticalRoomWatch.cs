using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalRoomWatchPolicy
    {
        // Let admitted per-pawn firing checks settle before separating a quiet
        // watch from a live engagement. The full watch still lasts 90 ticks.
        public const int LiveTicks = 30;
        public static bool Live(int until, int tick) => tick < until - TacticalEntryCombatPolicy.QuietTicks + LiveTicks;

        public static List<TacticalMemberCommand> Select(List<TacticalMemberCommand> candidates,
            bool high, bool child, Pawn commander, Pawn breacher)
        {
            if (high)
            {
                var teams = candidates.Where(m => m.Fireteam >= 0).GroupBy(m => m.Fireteam).OrderBy(g => g.Key).ToList();
                // An independently tasked HIGH child is already one fireteam.
                if (teams.Count == 0 || !child && teams.Count < 2) return new List<TacticalMemberCommand>();
                // Keep the trailing team so the leading teams can take the
                // next entry without waiting on their normal release order.
                return teams[teams.Count - 1].ToList();
            }
            return candidates.Where(m => m.Pawn != commander && m.Pawn != breacher && !m.Medic)
                .OrderBy(m => TacticalEntryOrder.Priority(m.CombatFunction))
                .Take(Math.Min(2, Math.Max(0, candidates.Count - 1))).ToList();
        }
    }

    public sealed partial class TacticalMemberCommand
    {
        public int RoomWatchUntil;
        public IntVec3 RoomWatchCell = IntVec3.Invalid, RoomWatchFace = IntVec3.Invalid;
    }

    public sealed partial class TacticalSquadCommand { public int RoomWatchHandledUntil; }

    public sealed partial class MapComponent_TacticalCommands
    {
        private bool HoldRoomCombat(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            if (!TacticalEntryCombatPolicy.Engaged(command.EntryCombatUntil, tick)) return false;
            if (TacticalRoomWatchPolicy.Live(command.EntryCombatUntil, tick))
            {
                command.CqbWait = TacticalCqbWait.CombatQuiet;
                command.Due = Math.Min(tick + 10,
                    command.EntryCombatUntil - TacticalEntryCombatPolicy.QuietTicks + TacticalRoomWatchPolicy.LiveTicks);
                return true;
            }
            if (command.RoomWatchHandledUntil >= command.EntryCombatUntil) return false;
            command.RoomWatchHandledUntil = command.EntryCombatUntil;
            var root = command.CqbParent ?? command;
            // At most one actual HIGH fireteam watches across all child tasks.
            if (root.Members.Any(m => m.RoomWatchUntil > tick)) return false;
            bool Eligible(TacticalMemberCommand m) => Available(m, map) && ContinuingActor(m)
                && m.Pawn.Position.Roofed(map) && TacticalPostFirePolicy.CanStop(command.Plan, m.Pawn.Position);
            var candidates = active.Where(Eligible).ToList();
            var selected = TacticalRoomWatchPolicy.Select(candidates, TacticalHighCqbPolicy.High(command),
                command.CqbParent != null, root.Link.Unit?.Commander, command.Breacher);
            if (TacticalHighCqbPolicy.High(command) && selected.Count > 0
                && active.Any(m => m.Fireteam == selected[0].Fireteam && !candidates.Contains(m))) return false;
            foreach (var member in selected)
            {
                member.RoomWatchUntil = command.EntryCombatUntil;
                member.RoomWatchCell = member.Pawn.Position;
                member.RoomWatchFace = command.EntryCombatFocus.IsValid ? command.EntryCombatFocus : command.Goal;
            }
            return false;
        }

        internal bool RoomWatching(TacticalMemberCommand member, int tick)
        {
            if (member.RoomWatchUntil <= tick) return false;
            if (!byPawn.TryGetValue(member.Pawn, out var c) || c.Terminal || c.Defensive || c.Withdrawing
                || c.Phase == TacticalCommandPhase.Returning || c.FieldResponse != null
                || member.Pawn.CurJob != member.Job || member.Pawn.Position != member.RoomWatchCell
                || c.ChargeAction != null && !c.ChargeAction.EffectsCleared
                || c.OpeningAction != null && !c.OpeningAction.EffectsCleared
                || TacticalExplosiveEvasionPolicy.Pending(member.Pawn, c))
            { member.RoomWatchUntil = 0; return false; }
            return true;
        }

        private static void EntryCombatEvidence(TacticalSquadCommand command, TacticalMemberCommand member, int tick)
        {
            if (member != null && member.RoomWatchUntil > tick)
                member.RoomWatchUntil = tick + TacticalEntryCombatPolicy.QuietTicks;
            else command.EntryCombatUntil = Math.Max(command.EntryCombatUntil, tick + TacticalEntryCombatPolicy.QuietTicks);
        }
    }
}
