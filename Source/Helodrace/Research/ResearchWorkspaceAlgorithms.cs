using System;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace.ResearchUI
{
    public static class ResearchPlan
    {
        public static List<T> Build<T>(IEnumerable<T> goals, Func<T, bool> finished,
            Func<T, IEnumerable<T>> prerequisites) where T : class
        {
            var result = new List<T>();
            var visiting = new HashSet<T>();
            var visited = new HashSet<T>();
            foreach (T goal in goals) Visit(goal, finished, prerequisites, visiting, visited, result);
            return result;
        }

        private static void Visit<T>(T node, Func<T, bool> finished, Func<T, IEnumerable<T>> prerequisites,
            HashSet<T> visiting, HashSet<T> visited, List<T> result) where T : class
        {
            if (node == null || finished(node) || visited.Contains(node)) return;
            if (!visiting.Add(node)) throw new InvalidOperationException("Cyclic research prerequisites");
            foreach (T prerequisite in prerequisites(node) ?? Enumerable.Empty<T>())
                Visit(prerequisite, finished, prerequisites, visiting, visited, result);
            visiting.Remove(node);
            visited.Add(node);
            result.Add(node);
        }

        // Completion preview uses a copy of the state. Each side is AND; either side can activate a link.
        public static HashSet<T> Predict<T>(T project, IEnumerable<T> completed,
            IEnumerable<Tuple<IList<T>, IList<T>>> links, Func<T, IEnumerable<T>> prerequisites) where T : class
        {
            var result = new HashSet<T>(completed);
            Action<T> finish = node =>
            {
                foreach (T ancestor in Build(new[] { node }, result.Contains, prerequisites)) result.Add(ancestor);
            };
            finish(project);
            bool changed;
            var pairs = links.ToList();
            do
            {
                int count = result.Count;
                foreach (var pair in pairs)
                {
                    if (pair.Item1.Count > 0 && pair.Item1.All(result.Contains))
                        foreach (T target in pair.Item2) finish(target);
                    if (pair.Item2.Count > 0 && pair.Item2.All(result.Contains))
                        foreach (T target in pair.Item1) finish(target);
                }
                changed = result.Count != count;
            } while (changed);
            return result;
        }
    }

    public static class ResearchZoom
    {
        public const float Minimum = 0.3f, Maximum = 1.5f;
        public static float Clamp(float value) => Math.Max(Minimum, Math.Min(Maximum, value));
        public static float Anchor(float scroll, float pivot, float before, float after) => (scroll + pivot) * after / before - pivot;
    }

    public struct ResearchPoint
    {
        public float X, Y;
        public ResearchPoint(float x, float y) { X = x; Y = y; }
    }

    public struct ResearchLine
    {
        public ResearchPoint A, B;
        public ResearchLine(ResearchPoint a, ResearchPoint b) { A = a; B = b; }
        public ResearchPoint At(float t) => new ResearchPoint(A.X + (B.X - A.X) * t, A.Y + (B.Y - A.Y) * t);
    }

    public struct ResearchBox
    {
        public float Left, Top, Right, Bottom;
        public ResearchBox(float left, float top, float right, float bottom)
        { Left = left; Top = top; Right = right; Bottom = bottom; }
        public bool Blocks(ResearchPoint a, ResearchPoint b) =>
            Math.Abs(a.Y - b.Y) < 0.001f
                ? a.Y > Top + 0.001f && a.Y < Bottom - 0.001f && Math.Max(a.X, b.X) > Left + 0.001f && Math.Min(a.X, b.X) < Right - 0.001f
                : a.X > Left + 0.001f && a.X < Right - 0.001f && Math.Max(a.Y, b.Y) > Top + 0.001f && Math.Min(a.Y, b.Y) < Bottom - 0.001f;
    }

    public struct ResearchCell
    {
        public int Column, Row;
        public ResearchCell(int column, int row) { Column = column; Row = row; }
    }

    public static class ResearchFlowLayout
    {
        private sealed class LaneBlock
        {
            public int Start, Count;
            public double Sum;
            public double Mean => Sum / Count;
        }

        // Balance a whole layer around its parents instead of pushing each later sibling farther down.
        // Ordered rows must stay at least one lane apart; unused gaps remain when families are far apart.
        private static int[] Lanes(IList<double> preferred)
        {
            var blocks = new List<LaneBlock>();
            for (int i = 0; i < preferred.Count; i++)
            {
                blocks.Add(new LaneBlock { Start = i, Count = 1, Sum = preferred[i] - i });
                while (blocks.Count > 1 && blocks[blocks.Count - 2].Mean > blocks[blocks.Count - 1].Mean)
                {
                    var right = blocks[blocks.Count - 1]; var left = blocks[blocks.Count - 2];
                    left.Count += right.Count; left.Sum += right.Sum; blocks.RemoveAt(blocks.Count - 1);
                }
            }
            var rows = new int[preferred.Count];
            foreach (var block in blocks)
                for (int i = block.Start; i < block.Start + block.Count; i++)
                    rows[i] = i + (int)Math.Round(block.Mean, MidpointRounding.AwayFromZero);
            return rows;
        }

        // Lay out the whole graph together: dependency depth moves right, and branches keep their parent lanes.
        public static Dictionary<T, ResearchCell> Arrange<T>(IList<T> projects, Func<T, IEnumerable<T>> parents) where T : class
        {
            var members = new HashSet<T>(projects);
            var remaining = new HashSet<T>(projects);
            var cells = new Dictionary<T, ResearchCell>();
            while (remaining.Count > 0)
            {
                var ready = projects.Where(p => remaining.Contains(p) && !(parents(p) ?? Enumerable.Empty<T>())
                    .Any(a => members.Contains(a) && remaining.Contains(a))).ToList();
                // Invalid third-party cycles still get a usable layout; queue validation handles their execution.
                if (ready.Count == 0) ready.Add(projects.First(remaining.Contains));
                ready = ready.OrderBy(p => (parents(p) ?? Enumerable.Empty<T>()).Where(cells.ContainsKey)
                    .Select(a => (double)cells[a].Row).DefaultIfEmpty(0).Average()).ToList();
                var desired = ready.Select(p => (parents(p) ?? Enumerable.Empty<T>()).Where(cells.ContainsKey)
                    .Select(a => (double)cells[a].Row).DefaultIfEmpty(0).Average()).ToList();
                var lanes = Lanes(desired);
                for (int i = 0; i < ready.Count; i++)
                {
                    var p = ready[i];
                    var ancestors = (parents(p) ?? Enumerable.Empty<T>()).Where(cells.ContainsKey).Select(a => cells[a]).ToList();
                    int column = ancestors.Count == 0 ? 0 : ancestors.Max(c => c.Column) + 1;
                    var used = new HashSet<int>(cells.Values.Where(c => c.Column == column).Select(c => c.Row));
                    int row = lanes[i];
                    // Only malformed cycles can revisit an already assigned depth layer.
                    while (used.Contains(row)) row++;
                    cells[p] = new ResearchCell(column, row); remaining.Remove(p);
                }
            }
            return cells;
        }
    }

    // A shared orthogonal visibility grid keeps every connection out of node bodies and section headings.
    public sealed class ResearchOrthogonalRouter
    {
        private readonly float[] xs, ys;
        private readonly List<int>[] neighbors;
        private readonly int columns;
        public ResearchOrthogonalRouter(IEnumerable<ResearchBox> obstacles, IEnumerable<ResearchPoint> ports)
        {
            var boxes = obstacles.ToList(); var ends = ports.ToList();
            xs = boxes.SelectMany(b => new[] { b.Left, b.Right }).Concat(ends.Select(p => p.X)).Distinct().OrderBy(x => x).ToArray();
            ys = boxes.SelectMany(b => new[] { b.Top, b.Bottom }).Concat(ends.Select(p => p.Y)).Distinct().OrderBy(y => y).ToArray();
            columns = xs.Length; neighbors = new List<int>[xs.Length * ys.Length];
            for (int i = 0; i < neighbors.Length; i++) neighbors[i] = new List<int>();
            for (int y = 0; y < ys.Length; y++)
                for (int x = 0; x < xs.Length; x++)
                {
                    int a = y * columns + x;
                    if (x + 1 < xs.Length) Connect(a, a + 1, boxes);
                    if (y + 1 < ys.Length) Connect(a, a + columns, boxes);
                }
        }
        private ResearchPoint Point(int index) => new ResearchPoint(xs[index % columns], ys[index / columns]);
        private void Connect(int a, int b, List<ResearchBox> boxes)
        {
            if (boxes.Any(box => box.Blocks(Point(a), Point(b)))) return;
            neighbors[a].Add(b); neighbors[b].Add(a);
        }
        public List<ResearchLine> Route(ResearchPoint from, ResearchPoint to)
        {
            int sx = Array.IndexOf(xs, from.X), sy = Array.IndexOf(ys, from.Y);
            int gx = Array.IndexOf(xs, to.X), gy = Array.IndexOf(ys, to.Y);
            if (sx < 0 || sy < 0 || gx < 0 || gy < 0) return new List<ResearchLine>();
            int start = sy * columns + sx, goal = gy * columns + gx;
            // Two states per vertex preserve incoming direction so bend penalties remain correct.
            var costs = Enumerable.Repeat(float.PositiveInfinity, neighbors.Length * 2).ToArray();
            var previous = Enumerable.Repeat(-1, costs.Length).ToArray();
            var pending = new SortedSet<Tuple<float, int, int>>(); int serial = 0;
            costs[start * 2] = 0; pending.Add(Tuple.Create(Distance(from, to), serial++, start * 2));
            int final = -1;
            while (pending.Count > 0)
            {
                var next = pending.Min; pending.Remove(next); int state = next.Item3, vertex = state / 2;
                if (next.Item1 > costs[state] + Distance(Point(vertex), to) + 0.01f) continue;
                if (vertex == goal) { final = state; break; }
                foreach (int neighbor in neighbors[vertex])
                {
                    int direction = neighbor / columns == vertex / columns ? 0 : 1;
                    int target = neighbor * 2 + direction;
                    float cost = costs[state] + Distance(Point(vertex), Point(neighbor)) + (state % 2 == direction ? 0 : 24);
                    if (cost >= costs[target]) continue;
                    costs[target] = cost; previous[target] = state;
                    pending.Add(Tuple.Create(cost + Distance(Point(neighbor), to), serial++, target));
                }
            }
            // Never fall back to a straight line through unrelated nodes.
            if (final < 0) return new List<ResearchLine>();
            var path = new List<ResearchPoint>();
            for (int at = final; at >= 0; at = previous[at]) path.Add(Point(at / 2));
            path.Reverse(); var result = new List<ResearchLine>();
            for (int i = 1; i < path.Count; i++)
            {
                var line = new ResearchLine(path[i - 1], path[i]);
                if (result.Count > 0)
                {
                    var last = result[result.Count - 1];
                    if (Math.Abs(last.A.X - last.B.X) < 0.001f && Math.Abs(line.A.X - line.B.X) < 0.001f
                        || Math.Abs(last.A.Y - last.B.Y) < 0.001f && Math.Abs(line.A.Y - line.B.Y) < 0.001f)
                    { result[result.Count - 1] = new ResearchLine(last.A, line.B); continue; }
                }
                result.Add(line);
            }
            return result;
        }
        private static float Distance(ResearchPoint a, ResearchPoint b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    public static class ResearchLineGeometry
    {
        // Remove only the lower line's crossing interval. Never paint a dark patch over unrelated nodes.
        public static List<ResearchLine> VisiblePieces(ResearchLine line, IEnumerable<ResearchLine> above, float gap)
        {
            float dx = line.B.X - line.A.X, dy = line.B.Y - line.A.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            var cuts = new List<Tuple<float, float>>();
            if (length < 0.001f) return new List<ResearchLine>();
            foreach (var other in above)
            {
                float ex = other.B.X - other.A.X, ey = other.B.Y - other.A.Y;
                float qx = other.A.X - line.A.X, qy = other.A.Y - line.A.Y;
                float cross = dx * ey - dy * ex;
                if (Math.Abs(cross) > 0.001f)
                {
                    float t = (qx * ey - qy * ex) / cross;
                    float u = (qx * dy - qy * dx) / cross;
                    // Shared endpoints are junctions, not crossings.
                    if (t > 0.001f && t < 0.999f && u >= -0.001f && u <= 1.001f)
                        cuts.Add(Tuple.Create(Math.Max(0, t - gap / length), Math.Min(1, t + gap / length)));
                }
                else if (Math.Abs(qx * dy - qy * dx) < 0.001f)
                {
                    float dot = dx * dx + dy * dy;
                    float start = (qx * dx + qy * dy) / dot;
                    float end = ((other.B.X - line.A.X) * dx + (other.B.Y - line.A.Y) * dy) / dot;
                    float lo = Math.Max(0, Math.Min(start, end)), hi = Math.Min(1, Math.Max(start, end));
                    if (hi - lo > 0.001f) cuts.Add(Tuple.Create(lo, hi));
                }
            }
            var pieces = new List<ResearchLine>();
            float cursor = 0;
            foreach (var cut in cuts.OrderBy(c => c.Item1))
            {
                if (cut.Item1 > cursor + 0.001f) pieces.Add(new ResearchLine(line.At(cursor), line.At(cut.Item1)));
                cursor = Math.Max(cursor, cut.Item2);
            }
            if (cursor < 0.999f) pieces.Add(new ResearchLine(line.At(cursor), line.B));
            return pieces;
        }
    }
}
