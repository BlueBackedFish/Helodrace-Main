using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Helodrace.Squads;

namespace Helodrace
{
    public static class RaidSmokeUtility
    {
        public static bool IsScreeningProjectile(ThingDef projectile) =>
            projectile?.projectile?.damageDef == DamageDefOf.Smoke
            && projectile.projectile.postExplosionGasType == GasType.BlindSmoke
            && projectile.GetModExtension<Helodrace.ModernWar.FragmentationGrenadeExtension>() == null
            && projectile.GetModExtension<Helodrace.ModernWar.FlashbangProjectileExtension>() == null
            && projectile.GetModExtension<HelodGasOnExplosionExtension>() == null
            && projectile.GetModExtension<Helodrace.ModernWar.ModernGrenadeProjectileExtension>()?.gasDef == null;

        public static bool IsSmoke(Thing item) => IsScreeningProjectile(item?.def.projectileWhenLoaded);

        public static bool ExteriorEntry(Map map, RaidTacticalPlan plan) => plan.BreachCell.IsValid
            && map.GetComponent<MapComponent_RaidTacticalPlans>().GetStructure(plan.OrganizationId)
                ?.RoomAt(plan.Entry) == 0;

        public static bool SmokeAt(Map map, IntVec3 target) => target.IsValid
            && GenRadial.RadialCellsAround(target, 3f, true).Any(cell => cell.InBounds(map)
                && map.gasGrid.DensityAt(cell, GasType.BlindSmoke) >= 32);
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        internal bool IgnoreOwnScreeningSmoke(Pawn pawn)
        {
            string id = OrganizationAPI.GetOrganization(pawn)?.id;
            if (id == null || !states.TryGetValue(id, out ExecutionState state)) return false;
            Thing known = pawn.mindState.knownExploder;
            return known != null && (known == state.SupportProjectile || known == state.ApproachSmokeProjectile)
                && RaidSmokeUtility.IsScreeningProjectile(known.def);
        }

        private bool ApproachScreen(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            if (state.ApproachSmokeActive)
            {
                bool preparing = state.ApproachSmokeThrower?.CurJobDef?.defName == "HD_ThrowInventoryGrenadeClose"
                    || state.ApproachSmokeThrower?.CurJobDef?.defName == "HD_ThrowInventoryGrenadeNormal";
                bool live = state.ApproachSmokeProjectile?.Spawned == true;
                if (!preparing && !live && state.ApproachSmokeLaunched && state.ApproachSmokeClearedTick < 0)
                    state.ApproachSmokeClearedTick = tick;
                bool complete = RaidSmokePolicy.ScreenComplete(state.ApproachSmokeLaunched, live, preparing,
                    RaidSmokeUtility.SmokeAt(map, state.ApproachSmokeTarget),
                    state.ApproachSmokeClearedTick >= 0 && tick - state.ApproachSmokeClearedTick >= 60,
                    tick - state.ApproachSmokeStarted >= 360);
                if (complete)
                {
                    state.ApproachSmokeActive = false;
                    state.NextApproachSmokeTick = tick + 360;
                    MapComponent_RaidTacticalTrace.Record(state.ApproachSmokeThrower,
                        state.ApproachSmokeLaunched ? "Approach smoke released; resume movement" : "Approach smoke failed; resume movement");
                    return false;
                }
                CoverScreenTeam(members);
                state.ApproachProgressTick = tick;
                state.PhaseStarted = tick;
                return true;
            }
            if (plan.IsDefensive || tick < state.NextApproachSmokeTick || RaidBreachToolRecovery.Pending(members))
                return false;
            state.NextApproachSmokeTick = tick + 90;
            RaidStructureSnapshot structure = StructureFor(map, plan);
            List<Pawn> moving = plan.Assignments.Where(assignment => members.Contains(assignment.Pawn)
                && assignment.Task != RaidTacticalTask.Withdraw
                && !AtStagingPosition(assignment, plan)
                && (structure?.RoomAt(assignment.Pawn.Position) ?? 0) == 0)
                .Select(assignment => assignment.Pawn).ToList();
            if (moving.Count == 0) return false;
            List<Pawn> threats = map.mapPawns.AllPawnsSpawned.Where(enemy => !enemy.Dead && !enemy.Downed
                && enemy.Faction != null && enemy.HostileTo(members[0])
                && moving.Any(pawn =>
                {
                    float distance = pawn.Position.DistanceTo(enemy.Position);
                    if (distance > 70f) return false;
                    Verb weapon = enemy.equipment?.Primary?.TryGetComp<CompEquippable>()?.PrimaryVerb;
                    if (weapon == null || weapon.IsMeleeAttack || weapon.EffectiveRange < distance) return false;
                    bool observed = GenSight.LineOfSight(pawn.Position, enemy.Position, map, true);
                    if (!observed) return false;
                    bool obscured = GenSight.PointsOnLineOfSight(enemy.Position, pawn.Position)
                        .Any(cell => cell.InBounds(map) && map.gasGrid.DensityAt(cell, GasType.BlindSmoke) >= 64);
                    return RaidSmokePolicy.NeedsScreen(observed, weapon.EffectiveRange, distance, obscured);
                })).OrderBy(enemy => moving.Min(pawn => pawn.Position.DistanceToSquared(enemy.Position)))
                .Take(3).ToList();
            if (threats.Count == 0) return false;
            bool reserveEntrySmoke = RaidSmokeUtility.ExteriorEntry(map, plan);
            foreach (Pawn thrower in moving.Concat(members).Distinct())
            {
                if (thrower.stances.FullBodyBusy || MapComponent_RaidTacticalOrders.Protected(thrower)
                    || !CompSledgehammerBreach.CanOperate(thrower)) continue;
                List<Thing> grenades = InventoryGrenadeUtility.GrenadeStacks(thrower).Where(RaidSmokeUtility.IsSmoke).ToList();
                if (grenades.Sum(item => item.stackCount) <= (reserveEntrySmoke ? 1 : 0)) continue;
                foreach (IntVec3 ideal in ScreenTargets(thrower, threats, plan))
                    foreach (IntVec3 target in GenRadial.RadialCellsAround(ideal, 2f, true)
                        .Where(cell => cell.InBounds(map) && cell.Standable(map)
                            && !plan.AvoidedTrapCells.Contains(cell)).OrderBy(cell => cell.DistanceToSquared(ideal)))
                    {
                        if (RaidSmokeUtility.SmokeAt(map, target)) continue;
                        bool close = InventoryGrenadeUtility.CanThrowAt(thrower, target, InventoryGrenadeUtility.CloseThrowRange);
                        if (!close && !InventoryGrenadeUtility.CanThrowAt(thrower, target, InventoryGrenadeUtility.NormalThrowRange)) continue;
                        JobDef jobDef = DefDatabase<JobDef>.GetNamed(close
                            ? "HD_ThrowInventoryGrenadeClose" : "HD_ThrowInventoryGrenadeNormal");
                        state.ApproachSmokeActive = true;
                        state.ApproachSmokeThrower = thrower;
                        state.ApproachSmokeTarget = target;
                        state.ApproachSmokeStarted = tick;
                        state.ApproachSmokeClearedTick = -1;
                        state.ApproachSmokeLaunched = false;
                        state.ApproachSmokeProjectile = null;
                        thrower.jobs.StartJob(JobMaker.MakeJob(jobDef, target, grenades[0]), JobCondition.InterruptForced);
                        CoverScreenTeam(members);
                        RaidTacticalSpeech.Say(thrower, "HD_RaidTactical_Smoke");
                        return true;
                    }
            }
            return false;
        }

        private static IEnumerable<IntVec3> ScreenTargets(Pawn thrower, List<Pawn> threats, RaidTacticalPlan plan)
        {
            foreach (Pawn enemy in threats)
            {
                Vector3 toward = (enemy.Position - thrower.Position).ToVector3().normalized;
                yield return (thrower.Position.ToVector3Shifted() + toward * 9f).ToIntVec3();
            }
            IntVec3 destination = plan.Assignments.FirstOrDefault(value => value.Pawn == thrower)?.Position ?? plan.Entry;
            Vector3 forward = (destination - thrower.Position).ToVector3().normalized;
            yield return (thrower.Position.ToVector3Shifted() + forward * 8f).ToIntVec3();
        }

        private static void CoverScreenTeam(List<Pawn> members)
        {
            foreach (Pawn pawn in members)
            {
                RaidPawnOrder current = MapComponent_RaidTacticalOrders.For(pawn);
                IntVec3 center = current?.Kind == RaidOrderKind.Fight ? current.Destination : pawn.Position;
                MapComponent_RaidTacticalOrders.Set(pawn, RaidOrderKind.Fight, center, radius: 3f);
            }
        }
    }
}
