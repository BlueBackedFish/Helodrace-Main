using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class RaidSmokeFormation : IExposable
    {
        public IntVec3 Anchor;
        public IntVec3 Rally;
        public IntVec3 Goal;
        public IntVec3 Target;
        public IntVec3 Threat;
        public IntVec3 ThrowPosition;
        public Pawn Thrower;
        public int CreatedTick;
        public List<IntVec3> RallyCells = new List<IntVec3>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Anchor, "anchor");
            Scribe_Values.Look(ref Rally, "rally");
            Scribe_Values.Look(ref Goal, "goal");
            Scribe_Values.Look(ref Target, "target");
            Scribe_Values.Look(ref Threat, "threat");
            Scribe_Values.Look(ref ThrowPosition, "throwPosition");
            Scribe_References.Look(ref Thrower, "thrower");
            Scribe_Values.Look(ref CreatedTick, "createdTick");
            Scribe_Collections.Look(ref RallyCells, "rallyCells", LookMode.Value);
        }
    }

    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private RaidSmokeFormation PlanSmokeFormation(List<Pawn> moving, List<Pawn> threats, RaidTacticalPlan plan, int tick)
        {
            RaidStructureSnapshot structure = StructureFor(map, plan);
            IntVec3 anchor = RaidSmokePlanning.DenseAnchor(moving.Select(pawn => pawn.Position), (a, b) => a.DistanceTo(b));
            IntVec3 threat = threats[0].Position;
            var local = new HashSet<IntVec3>(GenRadial.RadialCellsAround(anchor, 16f, true)
                .Where(cell => ValidReactiveCell(cell) && (structure?.RoomAt(cell) ?? 0) == 0
                    && !plan.AvoidedTrapCells.Contains(cell)));
            Dictionary<IntVec3, int> distances = RaidFormationTopology.Distances(local, anchor,
                cell => GenAdj.CardinalDirections.Select(direction => cell + direction), cell => true);
            if (distances.Count == 0) return null;
            IntVec3 rally = distances.Keys.Where(cell => distances[cell] <= 3)
                .OrderByDescending(cell => (!GenSight.LineOfSight(threat, cell, map, true) ? 28f : 0f)
                    + CoverUtility.CalculateOverallBlockChance(cell, threat, map) * 16f
                    + Math.Min(3f, cell.DistanceTo(threat) - anchor.DistanceTo(threat)) * 2f
                    - distances[cell] * 3f).First();
            float rallyRadius = Math.Min(6f, Math.Max(4f, (float)Math.Sqrt(moving.Count) * 0.8f));
            var rallyArea = new HashSet<IntVec3>(local.Where(cell => cell.DistanceToSquared(rally) <= rallyRadius * rallyRadius
                && GenSight.LineOfSight(rally, cell, map, true)));
            List<IntVec3> rallyCells = RaidFormationTopology.Connected(rallyArea, rally,
                cell => GenAdj.CardinalDirections.Select(direction => cell + direction), cell => true)
                .OrderBy(cell => cell.DistanceToSquared(rally)).ToList();
            if (rallyCells.Count == 0) return null;
            IntVec3 goal = plan.IsDefensive ? anchor : plan.Entry;
            IntVec3 ideal;
            if (!plan.IsDefensive && plan.ApproachPath.Count > 0
                && plan.ApproachPath.Min(cell => cell.DistanceToSquared(anchor)) <= 144)
                ideal = RaidSmokePlanning.ForwardAlong(plan.ApproachPath, anchor, 8f, (a, b) => a.DistanceTo(b));
            else
            {
                Vector3 forward = ((plan.IsDefensive ? threat : goal) - anchor).ToVector3();
                ideal = (anchor.ToVector3Shifted() + forward.normalized * Math.Min(8f, forward.magnitude)).ToIntVec3();
            }
            IntVec3 target = GenRadial.RadialCellsAround(ideal, 2f, true)
                .Where(cell => distances.TryGetValue(cell, out int travel) && travel <= 18
                    && cell != anchor && !RaidSmokeUtility.SmokeAt(map, cell)
                    && (plan.IsDefensive || cell.DistanceToSquared(goal) < anchor.DistanceToSquared(goal))
                    && rallyCells.Any(source => CanStageSmokeThrow(source, cell)))
                .OrderBy(cell => cell.DistanceToSquared(ideal)).ThenBy(cell => distances[cell])
                .DefaultIfEmpty(IntVec3.Invalid).First();
            if (!target.IsValid) return null;
            IntVec3 throwPosition = rallyCells.Where(source => CanStageSmokeThrow(source, target))
                .OrderBy(source => source.DistanceToSquared(rally)).First();
            return new RaidSmokeFormation { Anchor = anchor, Rally = rally, Goal = goal, Target = target,
                Threat = threat, ThrowPosition = throwPosition, RallyCells = rallyCells, CreatedTick = tick };
        }

        private bool CanStageSmokeThrow(IntVec3 source, IntVec3 target) => source != target
            && source.DistanceToSquared(target) <= InventoryGrenadeUtility.NormalThrowRange * InventoryGrenadeUtility.NormalThrowRange
            && GenSight.LineOfSight(source, target, map, true);

        private bool PreparePlannedSmoke(List<Pawn> members, RaidTacticalPlan plan, ExecutionState state, int tick)
        {
            RaidSmokeFormation formation = state.SmokeFormation;
            if (tick - formation.CreatedTick >= 720 || !ValidReactiveCell(formation.Target)
                || !ValidReactiveCell(formation.ThrowPosition))
            {
                state.SmokeFormation = null;
                state.NextApproachSmokeTick = tick + 180;
                return false;
            }
            bool reserve = RaidSmokeUtility.OutdoorDestination(map, plan);
            List<Pawn> team = SmokeTeam(members, plan);
            bool CanCarry(Pawn pawn) => pawn != null && team.Contains(pawn)
                && pawn.CurJob?.playerForced != true && CompSledgehammerBreach.CanOperate(pawn)
                && InventoryGrenadeUtility.CanUseGrenades(pawn)
                && (StructureFor(map, plan)?.RoomAt(pawn.Position) ?? 0) == 0
                && InventoryGrenadeUtility.GrenadeStacks(pawn).Where(RaidSmokeUtility.IsSmoke)
                    .Sum(item => item.stackCount) > (reserve ? 1 : 0);
            if (!CanCarry(formation.Thrower)) formation.Thrower = members.Where(CanCarry)
                .Where(pawn => !MapComponent_RaidTacticalOrders.Protected(pawn)
                    && plan.Assignments.Any(value => value.Pawn == pawn && value.Task != RaidTacticalTask.Withdraw))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(formation.ThrowPosition))
                .FirstOrDefault(pawn => pawn.CanReach(formation.ThrowPosition, PathEndMode.OnCell, Danger.Deadly));
            Pawn thrower = formation.Thrower;
            if (thrower == null)
            {
                state.SmokeFormation = null;
                state.NextApproachSmokeTick = tick + 180;
                return false;
            }
            CoverScreenTeam(members, plan, state, tick);
            if (tick < state.NextApproachSmokeTick) { PauseReaction(state, tick); return true; }
            bool close = InventoryGrenadeUtility.CanThrowAt(thrower, formation.Target, InventoryGrenadeUtility.CloseThrowRange);
            bool normal = close || InventoryGrenadeUtility.CanThrowAt(thrower, formation.Target, InventoryGrenadeUtility.NormalThrowRange);
            if (!normal || thrower.Position.DistanceToSquared(formation.Rally) > 36)
            {
                MapComponent_RaidTacticalOrders.Escape(thrower, formation.ThrowPosition);
                PauseReaction(state, tick);
                return true;
            }
            // Gather the main body before opening another forward smoke step.
            if (team.Count(pawn => pawn.Position.DistanceToSquared(formation.Rally) <= 36) * 5 < team.Count * 4)
            {
                MapComponent_RaidTacticalOrders.Escape(thrower, formation.ThrowPosition);
                PauseReaction(state, tick);
                return true;
            }
            if (MapComponent_RaidTacticalOrders.Protected(thrower)) { PauseReaction(state, tick); return true; }
            Thing grenade = InventoryGrenadeUtility.GrenadeStacks(thrower).First(RaidSmokeUtility.IsSmoke);
            state.ApproachSmokeActive = true;
            state.ApproachSmokeThrower = thrower;
            state.ApproachSmokeTarget = formation.Target;
            state.ApproachSmokeThreat = formation.Threat;
            state.ApproachSmokeStarted = tick;
            state.ApproachSmokeClearedTick = -1;
            state.ApproachSmokeLaunched = false;
            state.ApproachSmokeProjectile = null;
            if (!RaidGrenadePreparation.Start(thrower, grenade, formation.Target, thrower.Position, close))
            {
                state.ApproachSmokeActive = false;
                state.NextApproachSmokeTick = tick + 60;
                return false;
            }
            MapComponent_RaidTacticalTrace.Record(thrower, $"Planned smoke from squad {formation.Anchor} to {formation.Target}");
            RaidTacticalSpeech.Say(thrower, "HD_RaidTactical_Smoke");
            PauseReaction(state, tick);
            return true;
        }

        private List<Pawn> SmokeTeam(List<Pawn> members, RaidTacticalPlan plan) => members.Where(pawn =>
            (StructureFor(map, plan)?.RoomAt(pawn.Position) ?? 0) == 0
            && pawn.CurJob?.playerForced != true && !IsTaserOperation(pawn)
            && map.GetComponent<MapComponent_HelodCasSupport>()?.RequiresStationaryGuidance(pawn) != true
            && plan.Assignments.Any(value => value.Pawn == pawn && value.Task != RaidTacticalTask.Withdraw)).ToList();
    }
}
