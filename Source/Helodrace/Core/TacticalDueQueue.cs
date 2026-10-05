using System;
using System.Collections.Generic;

namespace Helodrace
{
    // One indexed deadline per owner. Replacing a deadline leaves no stale
    // entries behind; overdue work retains its position ahead of newer work.
    internal sealed class TacticalDueQueue<T>
    {
        private sealed class Entry
        {
            internal T Item;
            internal int Due;
            internal long Sequence;
        }
        private readonly SortedSet<Entry> due = new SortedSet<Entry>(Comparer<Entry>.Create((a, b) =>
        {
            int result = a.Due.CompareTo(b.Due);
            return result != 0 ? result : a.Sequence.CompareTo(b.Sequence);
        }));
        private readonly Dictionary<T, Entry> entries = new Dictionary<T, Entry>();
        private long sequence;
        internal int Count => entries.Count;
        internal void Clear() { entries.Clear(); due.Clear(); }
        internal void Schedule(T item, int tick)
        {
            Remove(item);
            var entry = new Entry { Item = item, Due = tick, Sequence = sequence++ };
            entries[item] = entry; due.Add(entry);
        }
        internal void Remove(T item)
        {
            if (!entries.TryGetValue(item, out Entry entry)) return;
            entries.Remove(item); due.Remove(entry);
        }
        internal bool TryTake(int tick, out T item, out int deadline)
        {
            Entry first = due.Count == 0 ? null : due.Min;
            if (first == null || first.Due > tick) { item = default(T); deadline = 0; return false; }
            item = first.Item; deadline = first.Due; Remove(item); return true;
        }
        internal int OldestDelay(int tick) => due.Count == 0 ? 0 : Math.Max(0, tick - due.Min.Due);
        internal bool HasDue(int tick) => due.Count > 0 && due.Min.Due <= tick;
    }
}
