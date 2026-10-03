using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Helodrace.Squads;

namespace Helodrace
{
    public static class RaidSmokeUtility
    {
        public static bool IsScreeningProjectile(ThingDef projectile)
        {
            if (projectile?.projectile?.damageDef?.defName != "Smoke"
                || projectile.GetModExtension<Helodrace.ModernWar.FragmentationGrenadeExtension>() != null
                || projectile.GetModExtension<Helodrace.ModernWar.FlashbangProjectileExtension>() != null) return false;
            HelodGasDef burstGas = projectile.GetModExtension<HelodGasOnExplosionExtension>()?.gasDef;
            HelodGasDef emitterGas = projectile.GetModExtension<Helodrace.ModernWar.ModernGrenadeProjectileExtension>()?.gasDef;
            bool hcBurst = burstGas?.defName == "HD_HCSmokeGrid";
            bool hcEmitter = emitterGas?.defName == "HD_HCSmokeGrid";
            return (projectile.projectile.postExplosionGasType == GasType.BlindSmoke || hcBurst || hcEmitter)
                && (burstGas == null || hcBurst) && (emitterGas == null || hcEmitter);
        }

        // Only harmless smoke is suitable for advancing inside a screen.
        public static bool SafeSmokeAt(Map map, IntVec3 cell, byte minimumDensity = 32) => cell.InBounds(map)
            && (map.gasGrid.DensityAt(cell, GasType.BlindSmoke) >= minimumDensity
                || HelodGasStore.DensityAt(cell, map, HelodGasDefOf.HD_HCSmokeGrid) >= minimumDensity);

        // WP obscures fire too, but is never selected as a safe movement screen.
        public static bool CoveringSmokeAt(Map map, IntVec3 cell, byte minimumDensity = 64) =>
            SafeSmokeAt(map, cell, minimumDensity) || cell.InBounds(map)
                && HelodGasStore.DensityAt(cell, map, HelodGasDefOf.HD_WhitePhosphorusSmokeGrid) >= minimumDensity;

        public static bool IsSmoke(Thing item) => IsScreeningProjectile(item?.def.projectileWhenLoaded);

        public static bool OutdoorDestination(Map map, RaidTacticalPlan plan) => plan.BreachCell.IsValid
            && map.GetComponent<MapComponent_RaidTacticalPlans>().GetStructure(plan.OrganizationId)
                ?.RoomAt(plan.BreachInside) == 0;

        public static bool SmokeAt(Map map, IntVec3 target) => target.IsValid
            && GenRadial.RadialCellsAround(target, 3f, true).Any(cell => cell.InBounds(map)
                && SafeSmokeAt(map, cell));
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

        private bool ApproachScreen(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick, bool reacting = false)
        {
            if (state.ScreenAdvanceUntil > 0)
            {
                List<RaidReactivePosition> advancing = state.Reactions.Where(value => value.Kind == RaidReactionKind.Screen
                    && members.Contains(value.Pawn)).ToList();
                if (tick < state.ScreenAdvanceUntil && advancing.Any(value => value.Pawn.Position.DistanceToSquared(value.Destination) > 2.25f))
                {
                    foreach (RaidReactivePosition value in advancing)
                        MapComponent_RaidTacticalOrders.Set(value.Pawn, RaidOrderKind.Move, value.Destination,
                            sprint: true, fightOnArrival: true, radius: 1.5f, reactive: true);
                    PauseReaction(state, tick);
                    return true;
                }
                state.ScreenAdvanceUntil = 0;
                state.Reactions.RemoveAll(value => value.Kind == RaidReactionKind.Screen);
            }
            if (state.ApproachSmokeActive)
            {
                bool preparing = RaidGrenadePreparation.IsThrowJob(state.ApproachSmokeThrower);
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
                    state.NextApproachSmokeTick = tick + (state.ApproachSmokeLaunched ? 60 : 180);
                    MapComponent_RaidTacticalTrace.Record(state.ApproachSmokeThrower,
                        state.ApproachSmokeLaunched ? "Approach smoke released; resume movement" : "Approach smoke failed; resume movement");
                    bool advancing = state.ApproachSmokeLaunched && !plan.IsDefensive && state.DefenseUntil <= tick
                        && CommitSmokeAdvance(members, plan, state, tick);
                    if (!state.ApproachSmokeLaunched && state.SmokeFormation != null
                        && tick - state.SmokeFormation.CreatedTick < 720)
                    {
                        state.SmokeFormation.Thrower = null;
                        return PreparePlannedSmoke(members, plan, state, tick);
                    }
                    state.SmokeFormation = null;
                    if (advancing) return true;
                    return false;
                }
                CoverScreenTeam(members, plan, state, tick);
                state.ApproachProgressTick = tick;
                state.PhaseStarted = tick;
                return true;
            }
            if (state.SmokeFormation != null) return PreparePlannedSmoke(members, plan, state, tick);
            if (plan.IsDefensive && !reacting || tick < state.NextApproachSmokeTick || RaidBreachToolRecovery.Pending(members))
                return false;
            state.NextApproachSmokeTick = tick + 30;
            RaidStructureSnapshot structure = StructureFor(map, plan);
            List<Pawn> moving = plan.Assignments.Where(assignment => members.Contains(assignment.Pawn)
                && assignment.Task != RaidTacticalTask.Withdraw
                && (reacting || !AtStagingPosition(assignment, plan))
                && (structure?.RoomAt(assignment.Pawn.Position) ?? 0) == 0)
                .Select(assignment => assignment.Pawn).ToList();
            if (moving.Count == 0) return false;
            List<Pawn> threats = VisibleArmedEnemies(members, state, tick).Where(enemy => moving.Any(pawn =>
                {
                    float distance = pawn.Position.DistanceTo(enemy.Position);
                    if (distance > 70f) return false;
                    Verb weapon = enemy.equipment?.Primary?.TryGetComp<CompEquippable>()?.PrimaryVerb;
                    if (weapon == null || weapon.IsMeleeAttack || weapon.EffectiveRange < distance) return false;
                    bool observed = GenSight.LineOfSight(pawn.Position, enemy.Position, map, true);
                    if (!observed) return false;
                    bool obscured = GenSight.PointsOnLineOfSight(enemy.Position, pawn.Position)
                        .Any(cell => RaidSmokeUtility.CoveringSmokeAt(map, cell));
                    bool insideSmoke = RaidSmokeUtility.SafeSmokeAt(map, pawn.Position);
                    return RaidSmokePolicy.NeedsScreen(observed, weapon.EffectiveRange, distance, obscured && !insideSmoke);
                })).OrderBy(enemy => moving.Min(pawn => pawn.Position.DistanceToSquared(enemy.Position)))
                .Take(3).ToList();
            if (threats.Count == 0) return false;
            bool reserve = RaidSmokeUtility.OutdoorDestination(map, plan);
            if (!SmokeTeam(members, plan).Any(pawn => CompSledgehammerBreach.CanOperate(pawn)
                && InventoryGrenadeUtility.GrenadeStacks(pawn).Where(RaidSmokeUtility.IsSmoke)
                    .Sum(item => item.stackCount) > (reserve ? 1 : 0)))
            {
                state.NextApproachSmokeTick = tick + 180;
                return false;
            }
            state.SmokeFormation = PlanSmokeFormation(moving, threats, plan, tick);
            if (state.SmokeFormation == null) return false;
            state.ApproachSmokeLaunched = false;
            state.Reactions.RemoveAll(value => value.Kind == RaidReactionKind.Screen);
            MapComponent_RaidTacticalTrace.Record(moving[0], $"Smoke plan committed: rally {state.SmokeFormation.Rally}, target {state.SmokeFormation.Target}");
            return PreparePlannedSmoke(members, plan, state, tick);
        }

        private void CoverScreenTeam(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            RaidSmokeFormation formation = state.SmokeFormation;
            if (formation == null) return;
            var occupied = new HashSet<IntVec3>(state.Reactions.Where(value => members.Contains(value.Pawn)
                && (value.Kind == RaidReactionKind.Screen || value.Kind == RaidReactionKind.Sniper)
                && formation.RallyCells.Contains(value.Destination)).Select(value => value.Destination));
            occupied.Add(formation.ThrowPosition);
            foreach (Pawn pawn in SmokeTeam(members, plan).OrderBy(value => value.Position.DistanceToSquared(formation.Rally)))
            {
                if (pawn == formation.Thrower && !state.ApproachSmokeLaunched) continue;
                RaidReactivePosition reaction = state.Reactions.FirstOrDefault(value => value.Pawn == pawn && value.Kind == RaidReactionKind.Sniper)
                    ?? state.Reactions.FirstOrDefault(value => value.Pawn == pawn && value.Kind == RaidReactionKind.Screen);
                if (reaction == null)
                {
                    reaction = new RaidReactivePosition { Pawn = pawn, Kind = RaidReactionKind.Screen };
                    state.Reactions.Add(reaction);
                }
                if (!ValidReactiveCell(reaction.Destination) || !formation.RallyCells.Contains(reaction.Destination))
                {
                    IntVec3 cell = formation.RallyCells.Where(value => ValidReactiveCell(value) && !occupied.Contains(value)
                            && map.pawnDestinationReservationManager.CanReserve(value, pawn))
                        .OrderByDescending(value => (!GenSight.LineOfSight(formation.Threat, value, map, true) ? 28f : 0f)
                            + CoverUtility.CalculateOverallBlockChance(value, formation.Threat, map) * 16f
                            - value.DistanceTo(formation.Rally) * 2f - value.DistanceTo(pawn.Position) * 0.5f)
                        .Take(12).Where(value => pawn.CanReach(value, PathEndMode.OnCell, Danger.Deadly))
                        .DefaultIfEmpty(IntVec3.Invalid).First();
                    if (!cell.IsValid) continue;
                    reaction.Destination = cell;
                    reaction.SearchAfter = tick + 180;
                    occupied.Add(cell);
                }
                MapComponent_RaidTacticalOrders.Retreat(pawn, reaction.Destination);
            }
        }

        private bool CommitSmokeAdvance(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            var occupied = new HashSet<IntVec3>();
            var advancing = new List<RaidReactivePosition>();
            foreach (Pawn pawn in members)
            {
                if (pawn.CurJob?.playerForced == true || (structure?.RoomAt(pawn.Position) ?? 0) != 0) continue;
                RaidTacticalAssignment assignment = plan.Assignments.FirstOrDefault(value => value.Pawn == pawn);
                if (assignment?.Task == RaidTacticalTask.Withdraw) continue;
                IntVec3 goal = state.SmokeFormation?.Goal ?? plan.Entry;
                var local = new HashSet<IntVec3>(GenRadial.RadialCellsAround(pawn.Position, 12f, true)
                    .Where(cell => ValidReactiveCell(cell) && (structure?.RoomAt(cell) ?? 0) == 0
                        && !plan.AvoidedTrapCells.Contains(cell)));
                Dictionary<IntVec3, int> connected = RaidFormationTopology.Distances(local, pawn.Position,
                    cell => GenAdj.CardinalDirections.Select(direction => cell + direction), cell => true);
                IntVec3 destination = connected.Keys.Where(cell => connected[cell] <= 18
                        && RaidSmokeUtility.SafeSmokeAt(map, cell)
                        && !occupied.Contains(cell) && cell.DistanceToSquared(state.ApproachSmokeTarget) <= 36
                        && cell.DistanceToSquared(goal) < pawn.Position.DistanceToSquared(goal)
                        && map.pawnDestinationReservationManager.CanReserve(cell, pawn))
                    .OrderBy(cell => cell.DistanceToSquared(goal) + connected[cell] * 2f)
                    .Take(12).Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!destination.IsValid) continue;
                advancing.Add(new RaidReactivePosition { Pawn = pawn, Destination = destination,
                    Kind = RaidReactionKind.Screen, Until = tick + 240 });
                occupied.Add(destination);
            }
            if (advancing.Count == 0) return false;
            state.Reactions.RemoveAll(value => value.Kind == RaidReactionKind.Screen || value.Kind == RaidReactionKind.Sniper);
            state.Reactions.AddRange(advancing);
            state.ScreenAdvanceUntil = tick + 240;
            foreach (RaidReactivePosition value in advancing)
                MapComponent_RaidTacticalOrders.Set(value.Pawn, RaidOrderKind.Move, value.Destination,
                    sprint: true, fightOnArrival: true, radius: 1.5f, reactive: true);
            PauseReaction(state, tick);
            return true;
        }
    }
}
