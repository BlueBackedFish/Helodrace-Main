using System;
using System.Linq;
using System.Runtime.Serialization;

namespace Helodrace.Profiling
{
    [DataContract]
    public sealed class ProfileReference
    {
        [DataMember] public string method, workload;
        [DataMember] public int iterations;
        [DataMember] public string[] patchOwners;
        [DataMember] public double[] sampleMs;
        public double? MedianBatchMs
        {
            get
            {
                if (sampleMs == null || sampleMs.Length == 0 || sampleMs.Any(v => v <= 0 || double.IsNaN(v) || double.IsInfinity(v))) return null;
                double[] sorted = sampleMs.OrderBy(v => v).ToArray();
                int middle = sorted.Length / 2;
                return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
            }
        }
    }
    public static class ReferenceMetrics
    {
        // 100% means the cost of one fixed Core batch, not total CPU share.
        public static double? Percent(double totalMs, long units, ProfileReference reference)
        {
            double? batch = reference?.MedianBatchMs;
            return units > 0 && batch.HasValue ? (totalMs / units) / batch.Value * 100 : (double?)null;
        }
        public static bool Comparable(ProfileReference a, ProfileReference b) => a?.MedianBatchMs != null && b?.MedianBatchMs != null
            && a.method == b.method && a.workload == b.workload && a.iterations == b.iterations
            && (a.patchOwners ?? new string[0]).OrderBy(v => v).SequenceEqual((b.patchOwners ?? new string[0]).OrderBy(v => v));
    }
}
