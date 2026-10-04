using System.Collections.Generic;
using System.Linq;

namespace Helodrace
{
    // FIFO within each service class. Repeated requests attach waiters without
    // moving an old task behind newly arriving work. No crowd-size cancellation.
    internal sealed class TacticalPreparationQueue<TKey, TWaiter> where TKey : class where TWaiter : class
    {
        private sealed class Entry
        {
            internal TKey Key;
            internal int Frame;
            internal HashSet<TWaiter> Waiters;
        }
        private readonly LinkedList<Entry> fifo = new LinkedList<Entry>();
        private readonly Dictionary<TKey, LinkedListNode<Entry>> entries = new Dictionary<TKey, LinkedListNode<Entry>>();
        private readonly Dictionary<TWaiter, TKey> waiting;
        private readonly IEqualityComparer<TWaiter> waiterComparer;
        public int Count => entries.Count;
        public int WaiterCount => waiting.Count;
        public int PeakCount { get; private set; }
        public IEnumerable<TKey> Keys => fifo.Select(entry => entry.Key);

        internal TacticalPreparationQueue(IEqualityComparer<TWaiter> comparer = null)
        {
            waiterComparer = comparer ?? EqualityComparer<TWaiter>.Default;
            waiting = new Dictionary<TWaiter, TKey>(waiterComparer);
        }
        internal void Add(TKey key, int frame)
        {
            if (entries.ContainsKey(key)) return;
            entries[key] = fifo.AddLast(new Entry { Key = key, Frame = frame, Waiters = new HashSet<TWaiter>(waiterComparer) });
            PeakCount = System.Math.Max(PeakCount, Count);
        }
        internal void WaitFor(TWaiter waiter, TKey key)
        {
            if (waiting.TryGetValue(waiter, out TKey previous) && previous == key) return;
            Forget(waiter);
            if (!entries.TryGetValue(key, out LinkedListNode<Entry> node)) return;
            waiting[waiter] = key; node.Value.Waiters.Add(waiter);
        }
        internal void Forget(TWaiter waiter)
        {
            if (!waiting.TryGetValue(waiter, out TKey key)) return;
            waiting.Remove(waiter);
            if (entries.TryGetValue(key, out LinkedListNode<Entry> node)) node.Value.Waiters.Remove(waiter);
        }
        internal IEnumerable<TKey> ServiceOrder(int maximum)
        {
            int count = 0;
            foreach (Entry entry in fifo)
                if (entry.Waiters.Count > 0)
                {
                    if (count++ >= maximum) yield break;
                    yield return entry.Key;
                }
            foreach (Entry entry in fifo)
                if (entry.Waiters.Count == 0)
                {
                    if (count++ >= maximum) yield break;
                    yield return entry.Key;
                }
        }
        internal TWaiter[] Complete(TKey key)
        {
            if (!entries.TryGetValue(key, out LinkedListNode<Entry> node)) return new TWaiter[0];
            TWaiter[] notified = node.Value.Waiters.ToArray();
            foreach (TWaiter waiter in notified) waiting.Remove(waiter);
            fifo.Remove(node); entries.Remove(key);
            return notified;
        }
        internal int OldestWaitAge(int frame) => fifo.Where(entry => entry.Waiters.Count > 0)
            .Select(entry => System.Math.Max(0, frame - entry.Frame)).DefaultIfEmpty(0).Max();
        internal void Clear() { fifo.Clear(); entries.Clear(); waiting.Clear(); }
    }
}
