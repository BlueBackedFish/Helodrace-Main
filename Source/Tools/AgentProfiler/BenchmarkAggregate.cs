using System.Text.Json;
using Helodrace.Profiling;

internal static class BenchmarkAggregate
{
    private const string Warning = "Exact map, starting phase, target set and preparation are required. Ending phases can differ. p95/p99 are medians of each run's last 2048 calls, not pooled percentiles. Inclusive scopes overlap.";
    private static void Validate(ProfileSnapshot[] captures)
    {
        if (captures.Length == 0 || captures.Any(c => !c.complete || c.dropped != 0 || c.benchmark == null
            || c.endTick <= c.startTick || c.reference?.MedianBatchMs == null || c.methods == null || c.mods == null))
            throw new ArgumentException("Aggregation requires complete benchmark captures with a valid Core reference.");
    }
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
    private static string Key(ProfileSnapshot c, bool build) => JsonSerializer.Serialize(new {
        assemblySha256 = build ? c.assemblySha256 : null, c.population, c.scenario, c.speed, c.gameVersion, c.cpuSource, c.runtime, c.operatingSystem,
        c.spikeTracing, c.spikeThresholdMs, c.spikePawnId,
        traceSchema = c.spikeTracing ? c.schema : (int?)null,
        mods = string.Join(";", c.mods), targets = string.Join(";", c.methods.Select(m => m.method).Order()),
        c.benchmark.fixtureVersion, c.benchmark.seed, c.benchmark.mapFingerprint, c.benchmark.faction,
        c.benchmark.startPhases, c.benchmark.warmupTicks, c.benchmark.sampleTicks, c.benchmark.unitCount, c.benchmark.radioOperators,
        c.selectedEngine, c.effectiveEngine, c.newEngineImplemented, c.benchmark.workload, c.benchmark.fixtureCase, c.benchmark.pawnFingerprint,
        reference = c.reference.method + ":" + c.reference.workload + ":" + c.reference.iterations + ":" + string.Join(";", (c.reference.patchOwners ?? Array.Empty<string>()).Order()) });
    private static ProfileMethod Method(ProfileSnapshot c, string method) => c.methods.Single(m => m.method == method);
    private static double Percent(ProfileSnapshot c, string method) => ReferenceMetrics.Percent(Method(c, method).inclusiveMs, c.endTick - c.startTick, c.reference)!.Value;
    internal static object Summarize(ProfileSnapshot[] captures)
    {
        Validate(captures);
        return new { warning = Warning, groups = captures.GroupBy(c => Key(c, true))
            .Select(g => new { conditions = JsonSerializer.Deserialize<JsonElement>(g.Key), runs = g.Count(), endPhases = g.Select(c => c.benchmark.endPhases).ToArray(),
                methods = g.First().methods.Select(m => new { m.method,
                    medianReferencePercentPerTick = Median(g.Select(c => Percent(c, m.method))),
                    minReferencePercentPerTick = g.Min(c => Percent(c, m.method)), maxReferencePercentPerTick = g.Max(c => Percent(c, m.method)),
                    medianP95Ms = Median(g.Select(c => Method(c, m.method).p95Ms)), medianP99Ms = Median(g.Select(c => Method(c, m.method).p99Ms)),
                    medianP95ReferencePercent = Median(g.Select(c => ReferenceMetrics.Percent(Method(c, m.method).p95Ms, 1, c.reference)!.Value)),
                    medianP99ReferencePercent = Median(g.Select(c => ReferenceMetrics.Percent(Method(c, m.method).p99Ms, 1, c.reference)!.Value)),
                    totalCalls = g.Sum(c => Method(c, m.method).calls), exceptions = g.Sum(c => Method(c, m.method).exceptions) }).ToArray() }).ToArray() };
    }
    internal static object Compare(ProfileSnapshot[] before, ProfileSnapshot[] after)
    {
        Validate(before); Validate(after);
        if (before.Select(c => c.assemblySha256).Distinct().Count() != 1 || after.Select(c => c.assemblySha256).Distinct().Count() != 1)
            throw new ArgumentException("Repeated comparison requires one build per side.");
        var a = before.GroupBy(c => Key(c, false)).ToDictionary(g => g.Key);
        var b = after.GroupBy(c => Key(c, false)).ToDictionary(g => g.Key);
        var matched = a.Keys.Intersect(b.Keys).ToArray();
        if (matched.Length == 0) throw new ArgumentException("Comparison rejected: no matching benchmark conditions.");
        return new { warning = Warning, beforeBuild = before[0].assemblySha256, afterBuild = after[0].assemblySha256,
            unmatchedBefore = a.Keys.Except(b.Keys).Select(k => JsonSerializer.Deserialize<JsonElement>(k)).ToArray(),
            unmatchedAfter = b.Keys.Except(a.Keys).Select(k => JsonSerializer.Deserialize<JsonElement>(k)).ToArray(),
            groups = matched.Select(k => new { conditions = JsonSerializer.Deserialize<JsonElement>(k), beforeRuns = a[k].Count(), afterRuns = b[k].Count(),
                beforeEndPhases = a[k].Select(c => c.benchmark.endPhases).ToArray(), afterEndPhases = b[k].Select(c => c.benchmark.endPhases).ToArray(),
                methods = a[k].First().methods.Select(m => new { m.method,
                    beforeReferencePercentPerTick = Median(a[k].Select(c => Percent(c, m.method))),
                    afterReferencePercentPerTick = Median(b[k].Select(c => Percent(c, m.method))),
                    deltaReferencePercentagePoints = Median(b[k].Select(c => Percent(c, m.method))) - Median(a[k].Select(c => Percent(c, m.method))),
                    beforeCalls = a[k].Sum(c => Method(c, m.method).calls), afterCalls = b[k].Sum(c => Method(c, m.method).calls),
                    beforeP95Ms = Median(a[k].Select(c => Method(c, m.method).p95Ms)), afterP95Ms = Median(b[k].Select(c => Method(c, m.method).p95Ms)),
                    beforeP99Ms = Median(a[k].Select(c => Method(c, m.method).p99Ms)), afterP99Ms = Median(b[k].Select(c => Method(c, m.method).p99Ms)),
                    beforeP95ReferencePercent = Median(a[k].Select(c => ReferenceMetrics.Percent(Method(c, m.method).p95Ms, 1, c.reference)!.Value)),
                    afterP95ReferencePercent = Median(b[k].Select(c => ReferenceMetrics.Percent(Method(c, m.method).p95Ms, 1, c.reference)!.Value)),
                    beforeP99ReferencePercent = Median(a[k].Select(c => ReferenceMetrics.Percent(Method(c, m.method).p99Ms, 1, c.reference)!.Value)),
                    afterP99ReferencePercent = Median(b[k].Select(c => ReferenceMetrics.Percent(Method(c, m.method).p99Ms, 1, c.reference)!.Value)) }).ToArray() }).ToArray() };
    }
}
