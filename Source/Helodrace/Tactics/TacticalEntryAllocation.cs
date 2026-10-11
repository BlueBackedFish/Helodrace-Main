using System.Collections.Generic;
using System;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalEntryAllocation
    {
        public static bool ConnectedStack(TacticalLocalPlan plan) => TacticalLocalPlanner.Connected(
            plan.StackMembers.Count == 0 ? plan.Stack : plan.StackMembers.Select(i => plan.Stack[i]).ToList());

        public static bool BindApproach(TacticalLocalPlan plan, IList<IntVec3> previous, IList<int> eligible,
            Func<IntVec3, bool> free)
        {
            int count = previous.Count, entering = Math.Min(plan.Stack.Count, eligible.Count);
            if (entering == 0 || eligible.Distinct().Count() != eligible.Count
                || eligible.Any(i => i < 0 || i >= count)) return false;
            var stack = new IntVec3[count];
            var participants = eligible.Take(entering).ToList();
            var used = new HashSet<IntVec3>();
            for (int j = 0; j < entering; j++)
            { stack[participants[j]] = plan.Stack[j]; if (!used.Add(plan.Stack[j])) return false; }
            for (int i = 0; i < count; i++)
            {
                if (participants.Contains(i)) continue;
                IntVec3 guard = previous[i];
                if (used.Contains(guard) || !free(guard))
                {
                    guard = IntVec3.Invalid;
                    foreach (IntVec3 candidate in previous)
                        if (!used.Contains(candidate) && free(candidate)) { guard = candidate; break; }
                }
                if (!guard.IsValid) return false;
                used.Add(guard); stack[i] = guard;
            }
            plan.Stack.Clear(); plan.Stack.AddRange(stack);
            plan.StackMembers.Clear(); plan.StackMembers.AddRange(participants);
            plan.RetainedOutside.Clear();
            for (int i = 0; i < count; i++) if (!participants.Contains(i)) plan.RetainedOutside.Add(i);
            return true;
        }

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
