using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalEntryAllocation
    {
        // Called once when a following room cannot fit the whole squad. Preserve
        // member indexing and distinct outside posts; do not invent inside arrivals.
        public static bool Allocate(TacticalLocalPlan plan, IList<int> eligible)
        {
            int count = System.Math.Min(plan.Positions.Count, eligible.Count);
            if (count == 0 || eligible.Distinct().Count() != eligible.Count
                || eligible.Any(i => i < 0 || i >= plan.Stack.Count)) return false;
            var inside = plan.Positions.Take(count).ToArray();
            var positions = plan.Stack.ToArray();
            for (int i = 0; i < count; i++) positions[eligible[i]] = inside[i];
            if (positions.Distinct().Count() != positions.Length) return false;
            plan.Positions.Clear(); plan.Positions.AddRange(positions);
            plan.RetainedOutside.Clear();
            for (int i = 0; i < positions.Length; i++) plan.RetainedOutside.Add(i);
            for (int i = 0; i < count; i++) plan.RetainedOutside.Remove(eligible[i]);
            return true;
        }
        public static void SyncOutside(TacticalLocalPlan plan)
        {
            foreach (int index in plan.RetainedOutside) plan.Positions[index] = plan.Stack[index];
        }
        public static bool Promote(TacticalLocalPlan plan, int outsideMember, int vacantInsideMember)
        {
            if (!plan.RetainedOutside.Contains(outsideMember) || plan.RetainedOutside.Contains(vacantInsideMember)
                || vacantInsideMember < 0 || vacantInsideMember >= plan.Positions.Count) return false;
            plan.Positions[outsideMember] = plan.Positions[vacantInsideMember];
            // The unavailable member's guard post stays indexed to its own stack.
            plan.Positions[vacantInsideMember] = plan.Stack[vacantInsideMember];
            plan.RetainedOutside.Remove(outsideMember); plan.RetainedOutside.Add(vacantInsideMember);
            return true;
        }
    }
}
