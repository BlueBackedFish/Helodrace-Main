using System.Linq;
using Helodrace.Squads;
using Helodrace.Tactics;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool LifecycleFixture => result.fixtureCase == "r7-cleanup";
        private bool lifecycleExited;
        private long lifecycleIdleAdvances, lifecycleIdleChecks;
        private void ApplyLifecycleDrill()
        {
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
            if (service == null || scheduler == null) return;
            if (!lifecycleExited)
            {
                var commands = service.Commands.ToArray();
                if (commands.Length != result.units || commands.Any(c => c.Phase != TacticalCommandPhase.Complete
                    || c.Members.Any(m => !m.EntryAssignmentDone))) return;
                result.newAllCompleteTick = GenTicks.TicksGame - started;
                result.caseTriggered = true; lifecycleExited = true;
                // Actual departure/WorldPawns hooks, not simulated flag changes.
                foreach (Pawn pawn in raiders.Where(p => p.Spawned).ToArray()) pawn.ExitMap(false, Rot4.West);
                result.r7WorldOrganizationsCleared = raiders.All(p => OrganizationAPI.GetGroup(p) == null
                    && p.TryGetComp<PawnOrganizationComponent>()?.organizationId == null);
            }
            result.r7RemainingCommands = service.Commands.Count() + scheduler.ScheduledCount;
            result.r7RemainingOwners = service.OwnedPawnCount;
            result.r7RemainingClaims = service.ClaimCount; result.r7RemainingLeases = service.LeaseCount;
            result.r7RemainingOpenings = service.KnownOpeningCount;
            result.r7RemainingMessages = scheduler.Communications.PendingCount;
            bool empty = result.r7RemainingCommands == 0 && result.r7RemainingOwners == 0 && result.r7RemainingClaims == 0
                && result.r7RemainingLeases == 0 && result.r7RemainingOpenings == 0 && result.r7RemainingMessages == 0;
            if (!empty) { result.r7CleanupAt = -1; return; }
            if (result.r7CleanupAt < 0)
            {
                result.r7CleanupAt = GenTicks.TicksGame - started;
                lifecycleIdleAdvances = scheduler.Advances; lifecycleIdleChecks = scheduler.Communications.PairChecks;
            }
            result.r7IdleStable = scheduler.Advances == lifecycleIdleAdvances
                && scheduler.Communications.PairChecks == lifecycleIdleChecks;
            result.r7CleanupComplete = empty && result.r7WorldOrganizationsCleared && result.r7IdleStable
                && GenTicks.TicksGame - started - result.r7CleanupAt >= 240;
        }
    }
}
