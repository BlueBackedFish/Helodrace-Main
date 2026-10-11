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

        // Choose entry seats once; surplus members move to nearby covered
        // entrance flanks instead of retaining their distant original posts.
        public static bool Allocate(TacticalLocalPlan plan, IList<int> eligible, Func<IntVec3, bool> covered = null)
        {
            int count = System.Math.Min(plan.Positions.Count, eligible.Count);
            if (count == 0 || eligible.Distinct().Count() != eligible.Count
                || eligible.Any(i => i < 0 || i >= plan.Stack.Count)) return false;
            var inside = plan.Positions.Take(count).ToArray();
            var positions = plan.Stack.ToArray();
            for (int i = 0; i < count; i++) positions[eligible[i]] = inside[i];
            var entering = new HashSet<int>(eligible.Take(count));
            if (entering.Count < positions.Length)
            {
                var used = new HashSet<IntVec3>(inside);
                if (used.Count != inside.Length) return false;
                IntVec3 tangent = new IntVec3(-plan.Inward.z, 0, plan.Inward.x);
                var guards = new List<Guard>();
                foreach (IntVec3 cell in plan.Stack)
                {
                    if (!used.Add(cell)) continue;
                    int side = Math.Sign((cell.x - plan.Opening.x) * tangent.x + (cell.z - plan.Opening.z) * tangent.z);
                    guards.Add(new Guard { Cell = cell, Side = side, Covered = side != 0 && covered?.Invoke(cell) == true,
                        Distance = cell.DistanceToSquared(plan.Opening) });
                }
                if (guards.Count < positions.Length - entering.Count) return false;
                guards.Sort((a, b) =>
                {
                    int compare = b.Covered.CompareTo(a.Covered);
                    if (compare == 0) compare = a.Distance.CompareTo(b.Distance);
                    if (compare == 0) compare = a.Cell.x.CompareTo(b.Cell.x);
                    if (compare == 0) compare = a.Cell.z.CompareTo(b.Cell.z);
                    return compare;
                });
                int nextSide = plan.StackSide < 0 ? -1 : 1;
                for (int i = 0; i < positions.Length; i++)
                {
                    if (entering.Contains(i)) continue;
                    bool cover = guards[0].Covered;
                    int selected = guards.FindIndex(g => g.Covered == cover && g.Side == nextSide);
                    if (selected < 0) selected = guards.FindIndex(g => g.Covered == cover && g.Side == -nextSide);
                    if (selected < 0) selected = 0;
                    Guard guard = guards[selected]; guards.RemoveAt(selected);
                    positions[i] = guard.Cell;
                    if (guard.Side != 0) nextSide = -guard.Side;
                }
            }
            if (positions.Distinct().Count() != positions.Length) return false;
            plan.Positions.Clear(); plan.Positions.AddRange(positions);
            plan.RetainedOutside.Clear();
            for (int i = 0; i < positions.Length; i++) plan.RetainedOutside.Add(i);
            for (int i = 0; i < count; i++) plan.RetainedOutside.Remove(eligible[i]);
            plan.EntryOrder.Clear(); plan.EntryOrder.AddRange(eligible.Take(count));
            for (int i = 0; i < positions.Length; i++)
                if (!plan.EntryOrder.Contains(i)) plan.EntryOrder.Add(i);
            return true;
        }
        public static bool Promote(TacticalLocalPlan plan, int outsideMember, int vacantInsideMember)
        {
            if (!plan.RetainedOutside.Contains(outsideMember) || plan.RetainedOutside.Contains(vacantInsideMember)
                || vacantInsideMember < 0 || vacantInsideMember >= plan.Positions.Count) return false;
            IntVec3 guard = plan.Positions[outsideMember];
            plan.Positions[outsideMember] = plan.Positions[vacantInsideMember];
            plan.Positions[vacantInsideMember] = guard;
            plan.RetainedOutside.Remove(outsideMember); plan.RetainedOutside.Add(vacantInsideMember);
            return true;
        }
        private struct Guard { internal IntVec3 Cell; internal int Side, Distance; internal bool Covered; }
    }
}
