using System;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalSupportPolicy
    {
        public const int MinimumHold = 600, CheckInterval = 90;
        public static bool Hold(int tick, int until, bool strikeActive) => tick < until || strikeActive;
        public static bool CanAdvance(bool defensive, bool waitingForSupport) => !defensive && !waitingForSupport;
    }

    public sealed partial class TacticalSquadCommand
    {
        public Pawn SupportCaller;
        public IntVec3 SupportAim = IntVec3.Invalid;
        public int SupportUntil, SupportCheckAt;
        public bool SupportStrikeActive;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        // Request events only latch the caller's own known aim and wake its
        // squad. They do not expose enemy state or calculate positions here.
        public void NotifySupportRequested(Pawn caller, IntVec3 aim)
        {
            if (caller == null || !aim.InBounds(map) || !byPawn.TryGetValue(caller, out TacticalSquadCommand command)
                || command.Terminal || command.Phase == TacticalCommandPhase.Returning
                || command.Phase == TacticalCommandPhase.Complete) return;
            command.SupportCaller = caller; command.SupportAim = aim;
            command.SupportUntil = GenTicks.TicksGame + TacticalSupportPolicy.MinimumHold;
            command.SupportCheckAt = 0;
            command.Link.Cooperation.LocalReady = false;
            if (command.MedicalCare != null) command.MedicalCare.CancelRequested = true;
            Wake(caller);
        }

        private bool WaitingForSupport(TacticalSquadCommand command, int tick)
        {
            if (command.SupportCaller == null) return false;
            if (tick >= command.SupportCheckAt)
            {
                command.SupportCheckAt = tick + TacticalSupportPolicy.CheckInterval;
                command.SupportStrikeActive = map.GetComponent<MapComponent_HelodMortarSupport>()
                    ?.HasActiveStrike(command.SupportCaller) == true
                    || map.GetComponent<MapComponent_HelodCasSupport>()?.HasActiveStrike(command.SupportCaller) == true;
            }
            if (TacticalSupportPolicy.Hold(tick, command.SupportUntil, command.SupportStrikeActive)) return true;
            command.SupportCaller = null; command.SupportAim = IntVec3.Invalid;
            command.SupportStrikeActive = false;
            return false;
        }
        private static bool StationaryGuidance(TacticalMemberCommand member) =>
            member.Pawn.CurJobDef?.defName == "HD_CASStationaryGuidance";
    }
}
