using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    // Fill both connected wings from the same reachable approach floor.
    // Try LOS, axis-only, then unrestricted seats; safety stays mandatory.
    public static class TacticalStackFormation
    {
        public const int Width = 8, Depth = 8, CellLimit = (Width * 2 + 1) * Depth;

        public static bool Build(IntVec3 opening, IntVec3 inward, int count, ref int side,
            Func<IntVec3, bool> free, Func<IntVec3, bool> exposed, List<IntVec3> posts,
            Func<IntVec3, bool> excluded = null, bool allowPartial = false)
        {
            posts.Clear();
            if (count <= 0 || count > Width * Depth) return false;
            IntVec3 tangent = new IntVec3(-inward.z, 0, inward.x);
            var status = new byte[CellLimit];
            var sight = new byte[CellLimit];
            var attempted = new bool[CellLimit];
            var wings = new[] { new List<IntVec3>(), new List<IntVec3>(), new List<IntVec3>() };
            var candidate = new List<IntVec3>();
            List<IntVec3> best = allowPartial ? new List<IntVec3>() : null;
            int bestSide = 0;
            int Index(IntVec3 cell)
            {
                IntVec3 delta = cell - opening;
                int depth = -(delta.x * inward.x + delta.z * inward.z);
                int width = delta.x * tangent.x + delta.z * tangent.z;
                return depth < 1 || depth > Depth || Math.Abs(width) > Width
                    ? -1 : (depth - 1) * (Width * 2 + 1) + width + Width;
            }
            bool Walk(IntVec3 cell)
            {
                int index = Index(cell);
                if (index < 0) return false;
                if (status[index] == 0) status[index] = free(cell)
                    ? excluded?.Invoke(cell) == true ? (byte)3 : (byte)2 : (byte)1;
                return status[index] >= 2;
            }
            int tier = 0;
            bool Seat(IntVec3 cell)
            {
                if (!Walk(cell)) return false;
                int index = Index(cell);
                if (status[index] == 3) return false;
                if (tier == 2) return true;
                if (tier == 1) return Side(cell) != 0;
                if (sight[index] == 0) sight[index] = exposed(cell) ? (byte)1 : (byte)2;
                return sight[index] == 2;
            }
            int Side(IntVec3 cell) => Math.Sign((cell.x - opening.x) * tangent.x + (cell.z - opening.z) * tangent.z);
            // This is an observation/entry preference, never a wing restriction.
            int preferredSide = side < 0 ? -1 : 1;
            var search = new Flood(Index, inward);
            var component = new Flood(Index, inward);
            int candidateWing = 0;
            Func<IntVec3, bool> covered = next => Side(next) + 1 == candidateWing && Seat(next);
            int SelectedSide()
            {
                if (posts.Exists(c => Side(c) == preferredSide)) return preferredSide;
                if (posts.Exists(c => Side(c) == -preferredSide)) return -preferredSide;
                return 0;
            }
            void Fill()
            {
                posts.Clear();
                List<IntVec3> first = wings[preferredSide + 1], second = wings[1 - preferredSide];
                int a = 0, b = 0;
                // Alternate the wings; use spare capacity when one is shorter.
                while (posts.Count < count && (a < first.Count || b < second.Count))
                {
                    if (a < first.Count) posts.Add(first[a++]);
                    if (posts.Count < count && b < second.Count) posts.Add(second[b++]);
                }
                // Keep the axis empty whenever the two wings alone suffice.
                foreach (IntVec3 cell in wings[1])
                { if (posts.Count == count) break; posts.Add(cell); }
            }
            if (!Walk(opening - inward)) return false;
            for (tier = 0; tier < 3; tier++)
            {
                Array.Clear(attempted, 0, attempted.Length);
                foreach (List<IntVec3> wing in wings) wing.Clear();
                search.Reset(); search.Add(opening - inward, 0);
                while (search.Pop(out IntVec3 seed, out int cost))
                {
                    if (!attempted[Index(seed)] && Seat(seed))
                    {
                        candidateWing = Side(seed) + 1;
                        candidate.Clear();
                        component.Reset(); component.Add(seed, 0);
                        while (component.Pop(out IntVec3 cell, out int distance))
                        {
                            attempted[Index(cell)] = true;
                            if (candidate.Count < count) candidate.Add(cell);
                            component.Expand(cell, distance, covered);
                        }
                        // Never merge separated pockets within the same wing.
                        if (candidate.Count > wings[candidateWing].Count)
                        { wings[candidateWing].Clear(); wings[candidateWing].AddRange(candidate); }
                        if (wings[0].Count > 0 && wings[2].Count > 0 && wings[0].Count + wings[2].Count >= count)
                        { Fill(); side = SelectedSide(); return true; }
                    }
                    search.Expand(seed, cost, Walk);
                }
                Fill();
                if (posts.Count == count) { side = SelectedSide(); return true; }
                if (allowPartial && posts.Count > best.Count)
                { best.Clear(); best.AddRange(posts); bestSide = SelectedSide(); }
            }
            posts.Clear();
            if (allowPartial && best.Count > 0)
            { posts.AddRange(best); side = bestSide; return true; }
            return false;
        }

        // Each occupied strip is contiguous. The builder separately verifies
        // that both wings share the reachable floor behind the same opening;
        // the unoccupied centre lane need not contain a pawn to join them.
        public static bool ConnectedWings(IntVec3 opening, IntVec3 inward, IList<IntVec3> posts)
        {
            if (posts.Count == 0) return false;
            IntVec3 tangent = new IntVec3(-inward.z, 0, inward.x);
            var unique = new HashSet<IntVec3>();
            var wings = new[] { new List<IntVec3>(), new List<IntVec3>(), new List<IntVec3>() };
            foreach (IntVec3 cell in posts)
            {
                if (!unique.Add(cell)) return false;
                int side = Math.Sign((cell.x - opening.x) * tangent.x + (cell.z - opening.z) * tangent.z);
                wings[side + 1].Add(cell);
            }
            foreach (List<IntVec3> wing in wings)
                if (wing.Count > 0 && !TacticalLocalPlanner.Connected(wing)) return false;
            return true;
        }

        // A tiny Dijkstra heap. Wall-parallel steps cost 1; normal steps cost 4.
        // Distance/status arrays cap map probes, including rejected cells.
        internal sealed class Flood
        {
            private struct Node { internal IntVec3 Cell; internal int Cost; }
            private readonly List<Node> heap = new List<Node>();
            private readonly int[] distances = new int[CellLimit];
            private readonly Func<IntVec3, int> index;
            private readonly IntVec3 inward;
            internal Flood(Func<IntVec3, int> index, IntVec3 inward)
            { this.index = index; this.inward = inward; }
            internal void Reset() { heap.Clear(); Array.Clear(distances, 0, distances.Length); }
            internal void Add(IntVec3 cell, int cost)
            {
                int id = index(cell);
                if (id < 0 || distances[id] != 0 && distances[id] <= cost + 1) return;
                distances[id] = cost + 1;
                var node = new Node { Cell = cell, Cost = cost };
                int i = heap.Count; heap.Add(node);
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (heap[parent].Cost <= cost) break;
                    heap[i] = heap[parent]; i = parent;
                }
                heap[i] = node;
            }
            internal bool Pop(out IntVec3 cell, out int cost)
            {
                while (heap.Count > 0)
                {
                    Node first = heap[0], tail = heap[heap.Count - 1];
                    heap.RemoveAt(heap.Count - 1);
                    if (heap.Count > 0)
                    {
                        int i = 0;
                        while (i * 2 + 1 < heap.Count)
                        {
                            int child = i * 2 + 1;
                            if (child + 1 < heap.Count && heap[child + 1].Cost < heap[child].Cost) child++;
                            if (heap[child].Cost >= tail.Cost) break;
                            heap[i] = heap[child]; i = child;
                        }
                        heap[i] = tail;
                    }
                    if (distances[index(first.Cell)] != first.Cost + 1) continue;
                    cell = first.Cell; cost = first.Cost; return true;
                }
                cell = IntVec3.Invalid; cost = 0; return false;
            }
            internal void Expand(IntVec3 cell, int cost, Func<IntVec3, bool> allowed)
            {
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (index(next) >= 0 && allowed(next))
                        Add(next, cost + (direction.x * inward.x + direction.z * inward.z == 0 ? 1 : 4));
                }
            }
        }
    }
}
