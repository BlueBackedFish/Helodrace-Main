using System;
using System.Collections.Generic;
using Verse;

namespace Helodrace.Tactics
{
    // Two bounded floods: find cover from the mouth, then fill one connected
    // component of eligible posts. Excluded posts remain traversable on approach.
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
            var attempted = new bool[CellLimit];
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
                if (status[index] == 0) status[index] = free(cell) ? (byte)2 : (byte)1;
                return status[index] >= 2;
            }
            bool Seat(IntVec3 cell)
            {
                if (!Walk(cell)) return false;
                int index = Index(cell);
                if (status[index] == 2)
                {
                    int width = (cell.x - opening.x) * tangent.x + (cell.z - opening.z) * tangent.z;
                    status[index] = width == 0 || excluded?.Invoke(cell) == true || exposed(cell) ? (byte)3 : (byte)4;
                }
                return status[index] == 4;
            }
            int Side(IntVec3 cell) => Math.Sign((cell.x - opening.x) * tangent.x + (cell.z - opening.z) * tangent.z);
            int lockedSide = side;
            var search = new Flood(Index, inward);
            var component = new Flood(Index, inward);
            int candidateSide = 0;
            Func<IntVec3, bool> approach = next => (lockedSide == 0 || Side(next) == 0 || Side(next) == lockedSide)
                && Walk(next);
            Func<IntVec3, bool> covered = next => Side(next) == candidateSide && Seat(next);
            if (!Walk(opening - inward)) return false;
            search.Add(opening - inward, 0);
            while (search.Pop(out IntVec3 seed, out int cost))
            {
                int index = Index(seed);
                candidateSide = Side(seed);
                if (!attempted[index] && (lockedSide == 0 || candidateSide == lockedSide) && Seat(seed))
                {
                    component.Reset();
                    component.Add(seed, 0);
                    while (component.Pop(out IntVec3 cell, out int distance))
                    {
                        attempted[Index(cell)] = true;
                        posts.Add(cell);
                        if (posts.Count == count) { side = candidateSide; return true; }
                        component.Expand(cell, distance, covered);
                    }
                    if (allowPartial && posts.Count > best.Count)
                    { best.Clear(); best.AddRange(posts); bestSide = candidateSide; }
                    // Never combine disconnected cover patches or opposite sides.
                    posts.Clear();
                }
                search.Expand(seed, cost, approach);
            }
            if (allowPartial && best.Count > 0)
            { posts.AddRange(best); side = bestSide; return true; }
            return false;
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
