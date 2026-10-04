using System;
using System.Linq;

namespace Helodrace
{
    internal sealed class TacticalCpuSamples
    {
        private readonly double[] values;
        private int next, count;
        internal long TotalSamples { get; private set; }
        internal double TotalMilliseconds { get; private set; }
        internal double Maximum { get; private set; }
        internal TacticalCpuSamples(int capacity = 2048) { values = new double[capacity]; }
        internal void Add(double milliseconds)
        {
            values[next] = milliseconds; next = (next + 1) % values.Length;
            count = Math.Min(count + 1, values.Length); TotalSamples++;
            TotalMilliseconds += milliseconds; Maximum = Math.Max(Maximum, milliseconds);
        }
        internal double[] Percentiles()
        {
            double[] sorted = values.Take(count).OrderBy(value => value).ToArray();
            double At(double quantile) => count == 0 ? 0 : sorted[(int)Math.Ceiling(quantile * count) - 1];
            return new[] { At(.5), At(.95), At(.99) };
        }
    }
}
