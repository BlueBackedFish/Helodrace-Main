using System;

namespace Helodrace
{
    internal sealed class TacticalNativeBudget
    {
        public readonly long Limit;
        public long Bytes { get; private set; }
        public long Peak { get; private set; }
        internal TacticalNativeBudget(long limit) { Limit = limit; }
        internal bool Fits(long bytes) => bytes >= 0 && bytes <= Limit && Bytes <= Limit - bytes;
        internal void Allocated(long bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            Bytes = checked(Bytes + bytes); Peak = Math.Max(Peak, Bytes);
        }
        internal void Freed(long bytes)
        {
            if (bytes < 0 || bytes > Bytes) throw new InvalidOperationException("Invalid movement-grid native memory release.");
            Bytes -= bytes;
        }
        internal static bool Retirable(bool ready, bool canRetire, int lastRequestedFrame, int frame)
            => ready && canRetire && frame - lastRequestedFrame >= 2;
    }
}
