using System.Collections.Generic;
using System;
using System.Linq;
using Verse;

namespace Helodrace.Tactics
{
    public static class TacticalEntryAllocation
    {
        public static bool ConnectedStack(TacticalLocalPlan plan) => TacticalStackFormation.ConnectedWings(plan.Opening, plan.Inward,
            plan.StackMembers.Count == 0 ? plan.Stack : plan.StackMembers.Select(i => plan.Stack[i]).ToList());

        public static bool BindApproach(TacticalLocalPlan plan, int count, IList<int> eligible, IList<IntVec3> guardPosts)
        {
            int entering = Math.Min(plan.Stack.Count, eligible.Count);
            if (entering == 0 || eligible.Distinct().Count() != eligible.Count
                || eligible.Any(i => i < 0 || i >= count) || guardPosts.Count != count - entering) return false;
            var stack = new IntVec3[count];
            var participants = eligible.Take(entering).ToList();
            var used = new HashSet<IntVec3>();
            for (int j = 0; j < entering; j++)
            { stack[participants[j]] = plan.Stack[j]; if (!used.Add(plan.Stack[j])) return false; }
            int guardIndex = 0;
            for (int i = 0; i < count; i++)
            {
                if (participants.Contains(i)) continue;
                IntVec3 guard = guardPosts[guardIndex++];
                if (!guard.IsValid || !used.Add(guard)) return false;
                stack[i] = guard;
            }
            plan.Stack.Clear(); plan.Stack.AddRange(stack);
            plan.StackMembers.Clear(); plan.StackMembers.AddRange(participants);
            plan.RetainedOutside.Clear();
            for (int i = 0; i < count; i++) if (!participants.Contains(i)) plan.RetainedOutside.Add(i);
            return true;
        }

        // Choose entry seats once; surplus members move to nearby covered
        // entrance flanks instead of retaining their distant original posts.
        public static bool Allocate(TacticalLocalPlan plan, IList<int> eligible, Func<IntVec3, bool> covered = null)
        {
            // Synchronous utility for isolated footprint checks; live commands
            // advance this same allocator across ticks in FinishPreparation.
            var allocation = new TacticalEntryAllocator(plan, eligible, covered);
            while (!allocation.Step()) { }
            return allocation.Valid;
        }
        public static bool Promote(TacticalLocalPlan plan, int outsideMember, int vacantInsideMember)
        {
            if (!plan.RetainedOutside.Contains(outsideMember) || plan.RetainedOutside.Contains(vacantInsideMember)
                || plan.StackMembers.Count > 0 && !plan.StackMembers.Contains(outsideMember)
                || vacantInsideMember < 0 || vacantInsideMember >= plan.Positions.Count) return false;
            IntVec3 guard = plan.Positions[outsideMember];
            plan.Positions[outsideMember] = plan.Positions[vacantInsideMember];
            plan.Positions[vacantInsideMember] = guard;
            plan.RetainedOutside.Remove(outsideMember); plan.RetainedOutside.Add(vacantInsideMember);
            return true;
        }
    }
}
