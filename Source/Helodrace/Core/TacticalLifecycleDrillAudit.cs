using System;
using System.Linq;
using System.Runtime.Serialization;
using Helodrace.Squads;
using Helodrace.Tactics;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public string r7ExitEffectKind, r7ExitEffectPhase;
        [DataMember] public int r7ExitEffectId = -1, r7ExitEffectAt = -1, r7ExitEffectDrainedAt = -1, r7ExitEffectFragments;
        [DataMember] public bool r7ExitDuringLiveEffect, r7EffectSurvivedExit, r7EffectDetonated, r7EffectDrained, r7EffectWallDestroyed;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool LifecycleFixture => result.fixtureCase == "r7-cleanup" || result.fixtureCase == "r7-cleanup-grenade"
            || result.fixtureCase == "r7-cleanup-charge";
        private bool lifecycleExited;
        private long lifecycleIdleAdvances, lifecycleIdleChecks;
        private Projectile lifecycleProjectile;
        private CompInstalledBreachCharge lifecycleCharge;
        private Pawn lifecycleInstigator;
        private ThingDef lifecycleProjectileDef;
        private Building lifecycleWall;
        private int lifecycleEffectSettled = -1;

        private bool CaptureExitEffect(TacticalSquadCommand[] commands)
        {
            TacticalSquadCommand command;
            if (result.fixtureCase == "r7-cleanup-grenade")
            {
                command = commands.FirstOrDefault(c => c.OpeningAction?.Launched == true
                    && c.OpeningAction.Projectile?.Spawned == true);
                if (command == null) return false;
                lifecycleProjectile = command.OpeningAction.Projectile;
                lifecycleInstigator = command.OpeningAction.Thrower;
                lifecycleProjectileDef = lifecycleProjectile.def;
                result.r7ExitEffectKind = "grenade"; result.r7ExitEffectId = lifecycleProjectile.thingIDNumber;
            }
            else
            {
                command = commands.FirstOrDefault(c => c.ChargeAction?.Charge?.Triggered == true
                    && c.ChargeAction.Charge.parent.Spawned && c.ChargeAction.Charge.TargetWall?.Spawned == true);
                if (command == null) return false;
                lifecycleCharge = command.ChargeAction.Charge;
                lifecycleInstigator = lifecycleCharge.OperatorPawn; lifecycleWall = lifecycleCharge.TargetWall;
                result.r7ExitEffectKind = "charge"; result.r7ExitEffectId = lifecycleCharge.parent.thingIDNumber;
            }
            result.r7ExitEffectPhase = command.Phase.ToString(); result.r7ExitEffectAt = GenTicks.TicksGame - started;
            result.r7ExitDuringLiveEffect = true;
            return true;
        }

        private bool ExitEffectsCleared()
        {
            if (!result.r7ExitDuringLiveEffect) return result.fixtureCase == "r7-cleanup";
            bool explosion = map.listerThings.ThingsOfDef(ThingDefOf.Explosion).OfType<Explosion>()
                .Any(effect => effect.Spawned && effect.instigator == lifecycleInstigator
                    && (lifecycleProjectileDef == null || effect.projectile == lifecycleProjectileDef));
            bool live;
            if (lifecycleCharge != null)
            {
                result.r7ExitEffectFragments = Math.Max(result.r7ExitEffectFragments, lifecycleCharge.Fragments.Count);
                result.r7EffectWallDestroyed = lifecycleWall.Destroyed;
                result.r7EffectDetonated |= lifecycleCharge.parent.Destroyed && result.r7EffectWallDestroyed
                    && result.r7ExitEffectFragments > 0;
                live = lifecycleCharge.parent.Spawned || lifecycleCharge.Fragments.Any(fragment => fragment?.Spawned == true);
            }
            else
            {
                result.r7EffectDetonated |= explosion;
                live = lifecycleProjectile.Spawned;
            }
            if (live || explosion || !result.r7EffectDetonated) lifecycleEffectSettled = -1;
            else if (lifecycleEffectSettled < 0) lifecycleEffectSettled = GenTicks.TicksGame;
            result.r7EffectDrained = lifecycleEffectSettled >= 0
                && GenTicks.TicksGame - lifecycleEffectSettled >= TacticalOpeningPolicy.SettleTicks;
            if (result.r7EffectDrained && result.r7ExitEffectDrainedAt < 0) result.r7ExitEffectDrainedAt = GenTicks.TicksGame - started;
            return result.r7EffectSurvivedExit && result.r7EffectDrained;
        }
        private void ApplyLifecycleDrill()
        {
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            GameComponent_TacticalCommands scheduler = Current.Game.GetComponent<GameComponent_TacticalCommands>();
            if (service == null || scheduler == null) return;
            if (!lifecycleExited)
            {
                var commands = service.Commands.ToArray();
                if (commands.Length != result.units) return;
                if (result.fixtureCase == "r7-cleanup")
                {
                    if (commands.Any(c => c.Phase != TacticalCommandPhase.Complete || c.Members.Any(m => !m.EntryAssignmentDone))) return;
                    result.newAllCompleteTick = GenTicks.TicksGame - started;
                }
                else if (!CaptureExitEffect(commands)) return;
                result.caseTriggered = true; lifecycleExited = true;
                // Actual departure/WorldPawns hooks, not simulated flag changes.
                foreach (Pawn pawn in raiders.Where(p => p.Spawned).ToArray()) pawn.ExitMap(false, Rot4.West);
                result.r7EffectSurvivedExit = lifecycleProjectile?.Spawned == true || lifecycleCharge?.parent.Spawned == true;
                result.r7WorldOrganizationsCleared = raiders.All(p => OrganizationAPI.GetGroup(p) == null
                    && p.TryGetComp<PawnOrganizationComponent>()?.organizationId == null);
            }
            result.r7RemainingCommands = service.Commands.Count() + scheduler.ScheduledCount;
            result.r7RemainingOwners = service.OwnedPawnCount;
            result.r7RemainingClaims = service.ClaimCount; result.r7RemainingLeases = service.LeaseCount;
            result.r7RemainingOpenings = service.KnownOpeningCount;
            result.r7RemainingMessages = scheduler.Communications.PendingCount;
            bool effectsCleared = ExitEffectsCleared();
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
            result.r7CleanupComplete = empty && effectsCleared && result.r7WorldOrganizationsCleared && result.r7IdleStable
                && GenTicks.TicksGame - started - result.r7CleanupAt >= 240;
        }
    }
}
