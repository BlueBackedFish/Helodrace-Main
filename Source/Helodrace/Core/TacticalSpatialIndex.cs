using System;
using System.Collections.Generic;

namespace Helodrace
{
    // Physical candidates only. Querying this index does not publish observations.
    internal sealed class TacticalSpatialIndex<T>
    {
        private readonly int size;
        private readonly Dictionary<long, List<T>> buckets = new Dictionary<long, List<T>>();
        internal TacticalSpatialIndex(int size = 16) { this.size = size; }
        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
        private int Bucket(int coordinate) => (int)Math.Floor((double)coordinate / size);
        internal void Clear() => buckets.Clear();
        internal void Add(T value, int x, int z)
        {
            long key = Key(Bucket(x), Bucket(z));
            if (!buckets.TryGetValue(key, out List<T> values)) buckets[key] = values = new List<T>();
            values.Add(value);
        }
        // Caller does exact circle/distance/hostility checks after this broad phase.
        internal IEnumerable<T> Query(int x, int z, int radius)
        {
            for (int bx = Bucket(x - radius); bx <= Bucket(x + radius); bx++)
                for (int bz = Bucket(z - radius); bz <= Bucket(z + radius); bz++)
                    if (buckets.TryGetValue(Key(bx, bz), out List<T> values))
                        foreach (T value in values) yield return value;
        }
    }
}
