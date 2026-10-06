using Helodrace.Profiling;

internal static class BenchmarkAggregate
{
    // Each run is independent. Never pretend percentiles of run-percentiles are
    // pooled call percentiles, or sum inclusive parents and their children.
    internal static object Summarize(ProfileSnapshot[] captures)
    {
        if (captures.Length == 0 || captures.Any(c => !c.complete || c.dropped != 0 || c.benchmark == null
            || c.endTick <= c.startTick || c.reference?.MedianBatchMs == null))
            throw new ArgumentException("Aggregation requires complete benchmark captures with a valid Core reference.");
        double Median(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();
            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }
        return new { warning = "Groups retain exact starting phase, map, target set, build and preparation. Ending phases can differ. p95/p99 are medians of each run's last 2048 calls, not pooled percentiles.",
            groups = captures.GroupBy(c => new { c.assemblySha256, c.population, c.scenario, c.speed, c.gameVersion, c.cpuSource, c.runtime, c.operatingSystem,
                mods = string.Join(";", c.mods), targets = string.Join(";", c.methods.Select(m => m.method).Order()),
                c.benchmark.fixtureVersion, c.benchmark.seed, c.benchmark.mapFingerprint, c.benchmark.faction,
                c.benchmark.startPhases, c.benchmark.warmupTicks, c.benchmark.sampleTicks, c.benchmark.unitCount, c.benchmark.radioOperators,
                reference = c.reference.method + ":" + c.reference.workload + ":" + c.reference.iterations + ":" + string.Join(";", c.reference.patchOwners ?? Array.Empty<string>()) })
            .Select(g => new { conditions = g.Key, runs = g.Count(), endPhases = g.Select(c => c.benchmark.endPhases).ToArray(),
                methods = g.First().methods.Select(m => new { m.method,
                    medianReferencePercentPerTick = Median(g.Select(c => ReferenceMetrics.Percent(c.methods.Single(v => v.method == m.method).inclusiveMs, c.endTick - c.startTick, c.reference)!.Value)),
                    minReferencePercentPerTick = g.Min(c => ReferenceMetrics.Percent(c.methods.Single(v => v.method == m.method).inclusiveMs, c.endTick - c.startTick, c.reference)),
                    maxReferencePercentPerTick = g.Max(c => ReferenceMetrics.Percent(c.methods.Single(v => v.method == m.method).inclusiveMs, c.endTick - c.startTick, c.reference)),
                    medianP95Ms = Median(g.Select(c => c.methods.Single(v => v.method == m.method).p95Ms)),
                    medianP99Ms = Median(g.Select(c => c.methods.Single(v => v.method == m.method).p99Ms)),
                    totalCalls = g.Sum(c => c.methods.Single(v => v.method == m.method).calls),
                    exceptions = g.Sum(c => c.methods.Single(v => v.method == m.method).exceptions) }).ToArray() }).ToArray() };
    }
}
