using System;
using System.Collections.Generic;

namespace Helodrace
{
    // Value-only sampling/ranking. No game objects, full-area BFS or sorting.
    internal static class TacticalReactiveSearch
    {
        internal static IEnumerable<(int x, int z)> Cells(int x, int z, float radius)
        {
            yield return (x, z);
            var seen = new HashSet<(int, int)> { (x,z) };
            for (int ring = 1; ring <= 3; ring++)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        double scale = radius * ring / 3.0 / Math.Sqrt(dx*dx+dz*dz);
                        var cell = (x + (int)Math.Round(dx*scale), z + (int)Math.Round(dz*scale));
                        if (seen.Add(cell)) yield return cell;
                    }
        }
        internal static bool Choose<T>(IEnumerable<T> candidates, Func<T,bool> allowed,
            Func<T,(bool safe, float score)> evaluate, Func<T,bool> reachable, out T selected)
        {
            T first=default(T), second=default(T);
            (bool safe,float score) firstRank=default, secondRank=default;
            bool hasFirst=false, hasSecond=false;
            int visited=0;
            bool Better((bool safe,float score) a, (bool safe,float score) b) =>
                a.safe != b.safe ? a.safe : a.score > b.score;
            foreach (T cell in candidates)
            {
                if (visited++ >= 32) break;
                if (!allowed(cell)) continue;
                var rank = evaluate(cell);
                if (!hasFirst || Better(rank,firstRank))
                {
                    second=first; secondRank=firstRank; hasSecond=hasFirst;
                    first=cell; firstRank=rank; hasFirst=true;
                }
                else if (!hasSecond || Better(rank,secondRank))
                { second=cell; secondRank=rank; hasSecond=true; }
            }
            if (hasFirst && reachable(first)) { selected=first; return true; }
            if (hasSecond && reachable(second)) { selected=second; return true; }
            selected=default(T); return false;
        }
    }
}
