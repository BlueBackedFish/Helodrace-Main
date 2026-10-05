using System;
using System.Collections.Generic;

namespace Helodrace
{
    // A yielded request goes to the tail; a long search cannot monopolize
    // all other squads or maps. Removed entries never execute again.
    internal sealed class TacticalSliceQueue<T>
    {
        private readonly LinkedList<T> pending = new LinkedList<T>();
        private readonly Dictionary<T, LinkedListNode<T>> nodes = new Dictionary<T, LinkedListNode<T>>();
        internal int Count => nodes.Count;
        internal void Add(T item)
        {
            if (!nodes.ContainsKey(item)) nodes[item] = pending.AddLast(item);
        }
        internal void Remove(T item)
        {
            if (!nodes.TryGetValue(item, out LinkedListNode<T> node)) return;
            pending.Remove(node); nodes.Remove(item);
        }
        internal int Run(int maxSlices, Func<bool> allowed, Func<T, bool> step)
        {
            int slices = 0;
            while (pending.Count > 0 && slices < maxSlices && allowed())
            {
                T item = pending.First.Value;
                Remove(item);
                slices++;
                if (step(item)) Add(item);
            }
            return slices;
        }
    }
}
