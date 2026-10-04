using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace Helodrace
{
    internal sealed class RaidPawnReferenceComparer : IEqualityComparer<Pawn>
    {
        internal static readonly RaidPawnReferenceComparer Instance = new RaidPawnReferenceComparer();
        public bool Equals(Pawn a, Pawn b) => ReferenceEquals(a, b);
        public int GetHashCode(Pawn pawn) => RuntimeHelpers.GetHashCode(pawn);
    }

    // Mutable positions/progress are retained by reference. Rebuild only when
    // the authoritative list or plan changes, not on every pawn path step.
    internal sealed class RaidTacticalIndices
    {
        private RaidTacticalPlan plan;
        private List<RaidTacticalAssignment> assignments;
        private List<RaidNodeMemberProgress> nodes;
        private int assignmentCount = -1, nodeCount = -1;
        private readonly Dictionary<Pawn, RaidTacticalAssignment> byPawn =
            new Dictionary<Pawn, RaidTacticalAssignment>(RaidPawnReferenceComparer.Instance);
        private readonly Dictionary<Pawn, RaidNodeMemberProgress> nodeByPawn =
            new Dictionary<Pawn, RaidNodeMemberProgress>(RaidPawnReferenceComparer.Instance);

        internal RaidTacticalAssignment Assignment(RaidTacticalPlan current, Pawn pawn)
        {
            if (current == null || pawn == null) return null;
            if (plan != current || assignments != current.Assignments || assignmentCount != current.Assignments.Count)
            {
                plan = current; assignments = current.Assignments; assignmentCount = assignments.Count;
                byPawn.Clear();
                foreach (RaidTacticalAssignment assignment in assignments)
                    if (assignment.Pawn != null) byPawn[assignment.Pawn] = assignment;
            }
            return byPawn.TryGetValue(pawn, out RaidTacticalAssignment found) ? found : null;
        }
        internal RaidNodeMemberProgress Node(List<RaidNodeMemberProgress> current, Pawn pawn)
        {
            if (pawn == null) return null;
            if (nodes != current || nodeCount != current.Count)
            {
                nodes = current; nodeCount = current.Count; nodeByPawn.Clear();
                foreach (RaidNodeMemberProgress progress in current)
                    if (progress.Pawn != null) nodeByPawn[progress.Pawn] = progress;
            }
            return nodeByPawn.TryGetValue(pawn, out RaidNodeMemberProgress found) ? found : null;
        }
        internal void InvalidateNodes() => nodeCount = -1;
    }
}
