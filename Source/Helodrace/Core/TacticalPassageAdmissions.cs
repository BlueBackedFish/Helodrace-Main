using System;
using System.Collections.Generic;

namespace Helodrace
{
    // Physical traffic only: no contacts, room knowledge or readiness reports.
    internal sealed class TacticalPassageAdmissions<TActor, TPassage>
    {
        private sealed class Entry
        {
            internal TActor Actor;
            internal TPassage Passage;
            internal int Tick;
            internal LinkedListNode<Entry> Node;
        }
        private readonly Dictionary<TActor, Entry> actors = new Dictionary<TActor, Entry>();
        private readonly Dictionary<TPassage, LinkedList<Entry>> passages = new Dictionary<TPassage, LinkedList<Entry>>();
        internal int Count => actors.Count;
        internal bool Request(TActor actor, TPassage passage, int tick)
        {
            if (actors.TryGetValue(actor, out Entry entry) && !EqualityComparer<TPassage>.Default.Equals(entry.Passage, passage))
            { Release(actor); entry = null; }
            if (entry == null)
            {
                entry = new Entry { Actor = actor, Passage = passage };
                if (!passages.TryGetValue(passage, out LinkedList<Entry> queue))
                    passages[passage] = queue = new LinkedList<Entry>();
                entry.Node = queue.AddLast(entry); actors[actor] = entry;
            }
            entry.Tick = tick;
            return First(actor, passage);
        }
        internal bool First(TActor actor, TPassage passage) => passages.TryGetValue(passage, out var queue)
            && EqualityComparer<TActor>.Default.Equals(queue.First.Value.Actor, actor);
        internal void Release(TActor actor)
        {
            if (!actors.TryGetValue(actor, out Entry entry)) return;
            LinkedList<Entry> queue = passages[entry.Passage]; queue.Remove(entry.Node);
            if (queue.Count == 0) passages.Remove(entry.Passage);
            actors.Remove(actor);
        }
        internal void Prune(int tick, Predicate<TActor> live)
        {
            var removed = new List<TActor>();
            foreach (Entry entry in actors.Values)
                if (tick - entry.Tick > 90 || !live(entry.Actor)) removed.Add(entry.Actor);
            foreach (TActor actor in removed) Release(actor);
        }
        internal void Clear() { actors.Clear(); passages.Clear(); }
    }
}
