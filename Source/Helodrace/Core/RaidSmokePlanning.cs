using System;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace
{
    public static class RaidSmokePlanning
    {
        // Use the occupied dense core, not a centroid dragged by a distant carrier.
        public static T DenseAnchor<T>(IEnumerable<T> positions, Func<T, T, float> distance)
        {
            List<T> points = positions.ToList();
            return points.OrderByDescending(point => points.Count(other => distance(point, other) <= 6f))
                .ThenBy(point => points.Sum(other => Math.Min(12f, distance(point, other)))).First();
        }

        public static T ForwardAlong<T>(IReadOnlyList<T> route, T anchor, float advance, Func<T, T, float> distance)
        {
            if (route.Count == 0) return anchor;
            int nearest = Enumerable.Range(0, route.Count).OrderBy(index => distance(anchor, route[index]))
                .ThenByDescending(index => index).First();
            float travelled = 0f;
            int next = nearest;
            while (next + 1 < route.Count && travelled < advance)
            {
                travelled += distance(route[next], route[next + 1]);
                next++;
            }
            return route[next];
        }
    }
}
