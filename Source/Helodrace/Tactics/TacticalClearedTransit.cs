using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalClearedTransit
    {
        // Cleared rooms need connected destination space, not an assault stack
        // with LOS/axis exclusions. Each flood retains the existing 289-cell cap.
        public static bool Build(TacticalLocalPlan plan, int count,
            Func<IntVec3, bool> traversable, Func<IntVec3, bool> usable)
        {
            int Forward(IntVec3 cell) => (cell.x - plan.Opening.x) * plan.Inward.x
                + (cell.z - plan.Opening.z) * plan.Inward.z;
            var inside = TacticalDestinationFootprint.Find(plan.Inside, count,
                cell => Forward(cell) >= plan.Depth && traversable(cell),
                cell => cell != plan.Inside && usable(cell));
            if (inside.Count == 0) return false;
            var outside = new List<IntVec3>();
            if (inside.Count < count)
                outside = TacticalDestinationFootprint.Find(plan.Outside, count - inside.Count,
                    cell => Forward(cell) <= -1 && traversable(cell),
                    cell => cell != plan.Outside && usable(cell));
            if (inside.Count + outside.Count != count) return false;
            plan.Positions.Clear(); plan.Positions.AddRange(inside);
            plan.Interior.Clear(); plan.Interior.UnionWith(inside);
            // Allocation/recovery uses a full indexed fallback footprint. These
            // are destinations, never a pre-entry gathering formation.
            plan.Stack.Clear(); plan.Stack.AddRange(inside); plan.Stack.AddRange(outside);
            plan.StackMembers.Clear(); plan.EntryLane = IntVec3.Invalid;
            plan.ReportedPassage = true;
            return true;
        }
    }
    public static partial class TacticalLocalPlanner
    {
        internal static bool BuildClearedTransit(Map map, TacticalLocalPlan plan, int count, Func<IntVec3, bool> claimed) =>
            TacticalClearedTransit.Build(plan, count,
                cell => cell.InBounds(map) && cell.Walkable(map) && !(cell.GetEdifice(map) is RimWorld.Building_Door),
                cell => Free(map, cell, claimed));
    }
}
