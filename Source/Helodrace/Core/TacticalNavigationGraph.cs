using System;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace
{
    // Join precomputed regions through currently usable doors/breach holes.
    // No full-map walkability allocation or flood fill is needed per planner.
    internal sealed class TacticalNavigationGraph
    {
        private readonly TacticalGeometryResult geometry;
        private readonly Dictionary<int, int> portals = new Dictionary<int, int>();
        private readonly int[] parents;
        public TacticalNavigationGraph(TacticalGeometryResult geometry, IEnumerable<int> openCells)
        {
            this.geometry = geometry;
            int[] indices = openCells.Where(index => index >= 0 && index < geometry.Components.Length)
                .Distinct().OrderBy(index => index).ToArray();
            foreach (int index in indices)
                if (geometry.Components[index] == 0)
                    portals[index] = geometry.ComponentCount + portals.Count + 1;
            parents = new int[geometry.ComponentCount + portals.Count + 1];
            for (int i = 0; i < parents.Length; i++) parents[i] = i;
            foreach (KeyValuePair<int, int> portal in portals)
            {
                int index = portal.Key, x = index % geometry.Input.Width, z = index / geometry.Input.Width;
                if (x > 0) Join(portal.Value, Node(index - 1));
                if (x + 1 < geometry.Input.Width) Join(portal.Value, Node(index + 1));
                if (z > 0) Join(portal.Value, Node(index - geometry.Input.Width));
                if (z + 1 < geometry.Input.Height) Join(portal.Value, Node(index + geometry.Input.Width));
            }
        }
        private int Node(int index) => index < 0 || index >= geometry.Components.Length ? 0
            : geometry.Components[index] != 0 ? geometry.Components[index]
            : portals.TryGetValue(index, out int id) ? id : 0;
        private int Root(int id)
        {
            while (parents[id] != id)
            {
                parents[id] = parents[parents[id]];
                id = parents[id];
            }
            return id;
        }
        private void Join(int first, int second)
        {
            if (first == 0 || second == 0) return;
            first = Root(first);
            second = Root(second);
            if (first != second) parents[Math.Max(first, second)] = Math.Min(first, second);
        }
        public bool Connected(int first, int second)
        {
            int a = Node(first), b = Node(second);
            return a != 0 && b != 0 && Root(a) == Root(b);
        }
    }
}
