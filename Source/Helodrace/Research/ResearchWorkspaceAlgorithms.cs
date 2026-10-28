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
