using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed partial class MapComponent_RaidTacticalExecution
    {
        private readonly Dictionary<(RaidTacticalPlan plan, IntVec3 threat), ReactiveScores> reactiveScores =
            new Dictionary<(RaidTacticalPlan, IntVec3), ReactiveScores>();
        private sealed class ReactiveScores
        {
            internal int Tick, LastUse;
            internal IntVec3 Threat;
            internal readonly Dictionary<IntVec3, ReactiveCellScore> Cells = new Dictionary<IntVec3, ReactiveCellScore>();
        }
        private readonly struct ReactiveCellScore
        {
            internal readonly float Cover, Terrain;
            internal readonly bool Hidden;
            internal ReactiveCellScore(bool hidden, float cover, float terrain) { Hidden=hidden; Cover=cover; Terrain=terrain; }
        }
        private ReactiveScores ScoresFor(RaidTacticalPlan plan, IntVec3 threat, int tick)
        {
            var bucket = new IntVec3(threat.x / 4, 0, threat.z / 4);
            var key = (plan,bucket);
            if (!reactiveScores.TryGetValue(key, out ReactiveScores scores) || tick < scores.Tick || tick-scores.Tick >= 120)
            {
                if (!reactiveScores.ContainsKey(key) && reactiveScores.Count >= 64)
                    reactiveScores.Remove(reactiveScores.OrderBy(pair => pair.Value.LastUse).First().Key);
                reactiveScores[key] = scores = new ReactiveScores { Tick=tick, Threat=threat };
            }
            scores.LastUse=tick;
            return scores;
        }
        private ReactiveCellScore ScoreAt(ReactiveScores scores, IntVec3 cell)
        {
            if (scores.Cells.TryGetValue(cell, out ReactiveCellScore value)) return value;
            value = new ReactiveCellScore(!GenSight.LineOfSight(scores.Threat,cell,map,true),
                CoverUtility.CalculateOverallBlockChance(cell,scores.Threat,map), cell.GetTerrain(map).pathCost);
            if (scores.Cells.Count >= 1024) scores.Cells.Clear();
            scores.Cells[cell]=value;
            return value;
        }
        private IntVec3 BoundedReactivePosition(Pawn pawn, RaidTacticalPlan plan, IntVec3 threat,
            HashSet<IntVec3> occupied, bool retreat, List<Thing> dangers, float radius)
        {
            RaidStructureSnapshot structure = StructureFor(map,plan);
            int room = structure?.RoomAt(pawn.Position) ?? 0;
            ReactiveScores scores = ScoresFor(plan,threat,GenTicks.TicksGame);
            float distance = pawn.Position.DistanceTo(threat);
            var hazards = dangers?.Where(value => value.Spawned)
                .Select(value => new { Aim=ExplosionAim(value), Radius=ExplosionRadius(value.def) })
                .OrderBy(value => value.Aim.DistanceToSquared(pawn.Position)).Take(4).ToList();
            var cells = TacticalReactiveSearch.Cells(pawn.Position.x,pawn.Position.z,radius)
                .Select(value => new IntVec3(value.x,0,value.z));
            bool allowed(IntVec3 cell) => ValidReactiveCell(cell) && !occupied.Contains(cell)
                && !plan.AvoidedTrapCells.Contains(cell) && (structure?.RoomAt(cell) ?? 0)==room
                && map.pawnDestinationReservationManager.CanReserve(cell,pawn);
            (bool safe,float score) evaluate(IntVec3 cell)
            {
                ReactiveCellScore fact = ScoreAt(scores,cell);
                bool safe = hazards == null || hazards.All(value => cell.DistanceToSquared(value.Aim) > value.Radius*value.Radius
                    || !GenSight.LineOfSight(value.Aim,cell,map,true));
                int travel = Math.Abs(cell.x-pawn.Position.x)+Math.Abs(cell.z-pawn.Position.z);
                float score = (fact.Hidden ? 28f : 0f)+fact.Cover*16f-fact.Terrain*0.08f-travel*1.2f
                    +(retreat ? Math.Max(-8f,Math.Min(8f,cell.DistanceTo(threat)-distance))*2f : 0f);
                return (safe,score);
            }
            // Only two finalists reach live path/safety validation. Failure stays
            // bounded; there is no exhaustive fallback that restores the old cost.
            bool reachable(IntVec3 cell) => (dangers == null || dangers.All(value => !ExposedToExplosion(cell,value)))
                && pawn.CanReach(cell,PathEndMode.OnCell,Danger.Deadly);
            return TacticalReactiveSearch.Choose(cells,allowed,evaluate,reachable,out IntVec3 result) ? result : IntVec3.Invalid;
        }
    }
}
