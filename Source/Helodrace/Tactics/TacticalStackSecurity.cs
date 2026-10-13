using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    // One assignment per footprint. No per-pawn sweep or periodic formation scan.
    public static class TacticalStackSecurity
    {
        public static bool Participant(TacticalLocalPlan plan, int index) => plan.StackMembers.Count == 0 || plan.StackMembers.Contains(index);
        public static void Arrived(TacticalLocalPlan plan, int index, IntVec3 position)
        {
            if (Participant(plan,index) && index < plan.Stack.Count && plan.Stack[index] == position) plan.StackedMembers.Add(index);
        }
        public static bool ReadyToEnter(TacticalLocalPlan plan, int index) => plan.Direct || plan.ReportedPassage
            || Participant(plan,index) && plan.StackedMembers.Contains(index);
        public static void Assign(TacticalLocalPlan plan)
        {
            plan.StackDirections.Clear();
            for (int i = 0; i < plan.Stack.Count; i++) plan.StackDirections.Add(IntVec3.Zero);
            if (plan.Direct || plan.ReportedPassage) return;
            IntVec3 side = new IntVec3(-plan.Inward.z,0,plan.Inward.x);
            int front = -1, nearest = int.MaxValue, left = -1, right = -1;
            int leftScore = -1, rightScore = -1, guards = 0;
            for (int i = 0; i < plan.Stack.Count; i++)
            {
                if (plan.StackMembers.Count > 0 && !plan.StackMembers.Contains(i))
                {
                    plan.StackDirections[i] = guards % 3 == 0 ? -plan.Inward : guards % 3 == 1 ? side : -side;
                    guards++; continue;
                }
                int distance = plan.Stack[i].DistanceToSquared(plan.Opening);
                if (distance < nearest) { nearest = distance; front = i; }
            }
            // Preserve at least the innermost member facing the breach. The
            // end of each wing looks outwards; a third rear member looks back.
            for (int i = 0; i < plan.Stack.Count; i++)
            {
                if (i == front || plan.StackMembers.Count > 0 && !plan.StackMembers.Contains(i)) continue;
                IntVec3 delta = plan.Stack[i] - plan.Opening;
                int width = delta.x * side.x + delta.z * side.z;
                int depth = -(delta.x * plan.Inward.x + delta.z * plan.Inward.z);
                int score = Math.Abs(width) * 4 + depth;
                if (width < 0 && score > leftScore) { left = i; leftScore = score; }
                if (width > 0 && score > rightScore) { right = i; rightScore = score; }
            }
            if (left >= 0) plan.StackDirections[left] = -side;
            if (right >= 0) plan.StackDirections[right] = side;
            int rear = -1, farthest = 0;
            for (int i = 0; i < plan.Stack.Count; i++)
            {
                if (i == front || i == left || i == right || plan.StackMembers.Count > 0 && !plan.StackMembers.Contains(i)) continue;
                IntVec3 delta = plan.Stack[i] - plan.Opening;
                int depth = -(delta.x * plan.Inward.x + delta.z * plan.Inward.z);
                if (depth > farthest) { farthest = depth; rear = i; }
            }
            if (rear >= 0) plan.StackDirections[rear] = -plan.Inward;
        }
        public static IntVec3 Face(TacticalLocalPlan plan, int index, IntVec3 post) =>
            index < plan.StackDirections.Count && plan.StackDirections[index] != IntVec3.Zero
                ? post + plan.StackDirections[index] * 4 : plan.Opening;
    }

    // Follow the breach along connected already secured floor. Stop as soon
    // as enough unique guard seats are found, yielding after 64 new probes.
    public sealed class TacticalApproachGuards
    {
        public const int ProbesPerStep = 64, ProbeLimit = 512, DistanceLimit = 24;
        public readonly List<IntVec3> Posts = new List<IntVec3>();
        public bool Complete { get; private set; }
        public bool Valid => Complete && Posts.Count == count;
        private readonly Queue<IntVec3> pending = new Queue<IntVec3>();
        private readonly HashSet<IntVec3> seen = new HashSet<IntVec3>();
        private readonly Func<IntVec3,bool> walk, seat;
        private readonly IntVec3 seed;
        private readonly int count;
        private int direction;
        public TacticalApproachGuards(IntVec3 seed, int count, Func<IntVec3,bool> walk, Func<IntVec3,bool> seat)
        {
            this.seed = seed; this.count = count; this.walk = walk; this.seat = seat;
            seen.Add(seed);
            if (count == 0 || !walk(seed)) Complete = true; else pending.Enqueue(seed);
        }
        public bool Step()
        {
            int probes = 0;
            while (!Complete && pending.Count > 0 && probes < ProbesPerStep)
            {
                IntVec3 next = pending.Peek() + GenAdj.CardinalDirections[direction++];
                if (direction == 4) { pending.Dequeue(); direction = 0; }
                if (!seen.Add(next)) continue;
                if (seen.Count > ProbeLimit) { Complete = true; break; }
                probes++;
                if (next.DistanceToSquared(seed) > DistanceLimit * DistanceLimit || !walk(next)) continue;
                if (seat(next)) Posts.Add(next);
                if (Posts.Count == count) { Complete = true; break; }
                pending.Enqueue(next);
            }
            Complete |= pending.Count == 0;
            return Complete;
        }
    }
}
