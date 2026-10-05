using System;
using System.Collections.Generic;

namespace Helodrace
{
    // Persistent bidirectional index. Querying one waiting pawn never rebuilds
    // a set of all other squads' destinations.
    internal sealed class TacticalQueuePositions<TActor, TCell>
    {
        private readonly Dictionary<TActor, TCell> positions = new Dictionary<TActor, TCell>();
        private readonly Dictionary<TCell, TActor> owners = new Dictionary<TCell, TActor>();
        internal int Count => positions.Count;
        internal bool TryGet(TActor actor, out TCell cell) => positions.TryGetValue(actor, out cell);
        internal bool Available(TActor actor, TCell cell) => !owners.TryGetValue(cell, out TActor owner)
            || EqualityComparer<TActor>.Default.Equals(owner, actor);
        internal bool Assign(TActor actor, TCell cell)
        {
            if (!Available(actor, cell)) return false;
            Release(actor);
            positions[actor] = cell; owners[cell] = actor; return true;
        }
        internal void Release(TActor actor)
        {
            if (!positions.TryGetValue(actor, out TCell cell)) return;
            positions.Remove(actor);
            if (owners.TryGetValue(cell, out TActor owner) && EqualityComparer<TActor>.Default.Equals(owner, actor))
                owners.Remove(cell);
        }
        internal void Prune(Func<TActor, bool> live)
        {
            foreach (TActor actor in new List<TActor>(positions.Keys))
                if (!live(actor)) Release(actor);
        }
    }
}
