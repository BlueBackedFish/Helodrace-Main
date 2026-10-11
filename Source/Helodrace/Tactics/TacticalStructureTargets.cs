using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public enum TacticalStructureClass { Pending, Target, Incidental }

    // A bounded local floor/edge probe, resumed only in a plan allowance.
    // A neutral wall is not evidence; a nearby disconnected owned room is not
    // evidence either. Never flood outdoors or ask the live Room graph.
    public sealed class TacticalStructureProbe
    {
        public const int CellLimit = 256, Slice = 32;
        private readonly Queue<IntVec3> pending = new Queue<IntVec3>();
        private readonly HashSet<IntVec3> visited = new HashSet<IntVec3>();
        private readonly IntVec3 opening;
        public TacticalStructureClass Result { get; private set; }
        public TacticalStructureProbe(IntVec3 inside, IntVec3 opening)
        { this.opening = opening; pending.Enqueue(inside); visited.Add(inside); }
        public TacticalStructureClass Step(Func<IntVec3, bool> floor, Func<IntVec3, bool> evidence, ref int remaining)
        {
            while (Result == TacticalStructureClass.Pending && pending.Count > 0 && remaining > 0)
            {
                IntVec3 cell = pending.Dequeue(); remaining--;
                if (!floor(cell)) continue;
                if (evidence(cell)) { Result = TacticalStructureClass.Target; break; }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (next == opening) continue;
                    if (evidence(next)) { Result = TacticalStructureClass.Target; break; }
                    if (visited.Count < CellLimit && visited.Add(next)) pending.Enqueue(next);
                }
            }
            if (Result == TacticalStructureClass.Pending && pending.Count == 0) Result = TacticalStructureClass.Incidental;
            return Result;
        }
    }

    public sealed partial class TacticalSquadCommand
    {
        public Faction TargetFaction;
        internal readonly Dictionary<IntVec3, TacticalStructureProbe> StructureProbes = new Dictionary<IntVec3, TacticalStructureProbe>();
        internal IntVec3 StructureGoal = IntVec3.Invalid;
        internal int StructureExpiry, StructureAllowance, StructureSightAllowance;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private Faction explicitTargetFaction;
        private void PrepareStructureTargets(TacticalSquadCommand command, int tick)
        {
            if (tick >= command.SecuredRegionExpiry)
            {
                command.SecuredRegionProbes.Clear(); command.SecuredRegionExpiry = tick + 120;
            }
            command.SecuredRegionAllowance = TacticalSecuredRegionProbe.Slice;
            Faction target = explicitGoal.IsValid ? explicitTargetFaction : Faction.OfPlayer;
            if (command.StructureGoal != command.Goal || command.TargetFaction != target || tick >= command.StructureExpiry)
            {
                command.StructureProbes.Clear(); command.StructureGoal = command.Goal;
                command.TargetFaction = target; command.StructureExpiry = tick + 600;
            }
            command.StructureAllowance = TacticalStructureProbe.Slice;
            command.StructureSightAllowance = 2;
        }
        private TacticalStructureClass ClassifyStructure(TacticalSquadCommand command, IntVec3 opening, IntVec3 inward, Pawn observer, int knownDepth = 1)
        {
            if (!opening.InBounds(map)) return TacticalStructureClass.Incidental;
            if (TacticalMissionRouting.CompletedChannel(command, opening, inward)) return TacticalStructureClass.Incidental;
            Building barrier = opening.GetEdifice(map);
            if (!TacticalLocalPlanner.TryDepth(map, opening, inward, out int depth)) return TacticalStructureClass.Incidental;
            depth = Math.Max(depth, knownDepth);
            IntVec3 inside = opening + inward * depth;
            if (barrier?.Faction != null && barrier.Faction != command.TargetFaction) return TacticalStructureClass.Incidental;
            if (command.SecuredKnowledge.Cleared(inside))
                return barrier?.def.IsWall == true ? TacticalStructureClass.Incidental : TacticalStructureClass.Target;
            TacticalStructureClass secured = SecuredRegion(command, opening, inward, depth);
            if (secured == TacticalStructureClass.Pending) return secured;
            if (secured == TacticalStructureClass.Target) return TacticalStructureClass.Incidental;
            if (barrier?.Faction != null)
                return barrier.Faction == command.TargetFaction ? TacticalStructureClass.Target : TacticalStructureClass.Incidental;
            if (!inside.InBounds(map)) return TacticalStructureClass.Incidental;
            // A neutral partition shared with a secured indoor target is part
            // of that local building, but never extend this rule over a yard.
            IntVec3 outside = opening - inward;
            if (outside.InBounds(map) && outside.Roofed(map) && inside.Roofed(map)
                && command.SecuredCells.Contains(outside)) return TacticalStructureClass.Target;
            if (!command.StructureProbes.TryGetValue(inside, out TacticalStructureProbe probe))
            {
                if (command.StructureProbes.Count >= 64) return TacticalStructureClass.Incidental;
                command.StructureProbes.Add(inside, probe = new TacticalStructureProbe(inside, opening));
            }
            bool Evidence(IntVec3 cell)
            {
                if (!cell.InBounds(map)) return false;
                if (cell == command.Goal) return true; // Already supplied mission evidence.
                Building building = cell.GetEdifice(map);
                if (building?.Faction != command.TargetFaction || command.TargetFaction == null) return false;
                if (building.def.IsWall || building is Building_Door) return true;
                // Reused ruins: require actual sight of an owned bed/workplace,
                // not an invisible lamp or a radius around unrelated furniture.
                if (!(building is Building_Bed) && !(building is Building_WorkTable)
                    || observer == null || observer.Position.DistanceToSquared(cell) > 784 || command.StructureSightAllowance <= 0) return false;
                command.StructureSightAllowance--;
                return GenSight.LineOfSight(observer.Position, cell, map, true);
            }
            bool Floor(IntVec3 cell) => cell.InBounds(map) && cell.Roofed(map) && cell.Walkable(map)
                && !(cell.GetEdifice(map) is Building_Door);
            return probe.Step(Floor, Evidence, ref command.StructureAllowance);
        }
        private bool TargetPlan(TacticalSquadCommand command, TacticalLocalPlan plan, Pawn observer, out bool pending)
        {
            TacticalStructureClass result = ClassifyStructure(command, plan.Opening, plan.Inward, observer, plan.Depth);
            pending = result == TacticalStructureClass.Pending;
            return result == TacticalStructureClass.Target;
        }
    }
}
