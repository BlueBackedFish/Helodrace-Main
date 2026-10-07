using System.Text.Json;
using Helodrace.Profiling;

internal static class EngineComparison
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    private sealed record Run(ProfileSnapshot Capture, JsonElement Audit, string Path);
    private static Run[] Read(string root)
    {
        var result = Directory.GetFiles(root, "capture-*.json", SearchOption.AllDirectories).Select(path =>
        {
            var c = JsonSerializer.Deserialize<ProfileSnapshot>(File.ReadAllText(path), Options)!;
            string audit = System.IO.Path.Combine(Directory.GetParent(System.IO.Path.GetDirectoryName(path)!)!.FullName, "audit.json");
            if (!File.Exists(audit)) throw new ArgumentException("Missing isolation/progress audit: " + path);
            JsonElement a = JsonDocument.Parse(File.ReadAllText(audit)).RootElement.Clone();
            if (c.schema < 4 || !c.complete || c.dropped != 0 || c.endTick <= c.startTick || c.benchmark?.fixtureVersion is not (5 or 6 or 7 or 8 or 9)
                || c.methods == null || c.mods == null || c.methods.Select(m => m.method).Distinct().Count() != c.methods.Length
                || c.methods.Any(m => m.exceptions > 0) || !a.GetProperty("complete").GetBoolean()
                || !a.GetProperty("isolationVerified").GetBoolean() || a.GetProperty("error").ValueKind != JsonValueKind.Null
                || !Positive(c.mainThreadWindowCpuMs) || !Positive(c.processWindowCpuMs) || c.reference?.MedianBatchMs == null
                || string.IsNullOrWhiteSpace(c.assemblySha256) || string.IsNullOrWhiteSpace(c.benchmark.mapFingerprint)
                || string.IsNullOrWhiteSpace(c.benchmark.pawnFingerprint))
                throw new ArgumentException("Incomplete/invalid engine measurement: " + path);
            var b = c.benchmark;
            if (b.engine is not ("vanilla" or "legacy" or "new")
                || b.effectiveEngine != (b.engine == "new" && !b.newEngineImplemented ? "vanilla-fallback" : b.engine))
                throw new ArgumentException("Unknown/inconsistent engine: " + path);
            if (c.selectedEngine != b.engine || c.effectiveEngine != b.effectiveEngine
                || c.newEngineImplemented != b.newEngineImplemented
                || a.GetProperty("engine").GetString() != b.engine || a.GetProperty("effectiveEngine").GetString() != b.effectiveEngine
                || a.GetProperty("newEngineImplemented").GetBoolean() != b.newEngineImplemented
                || a.GetProperty("requestedPopulation").GetInt32() != b.requestedPopulation
                || a.GetProperty("population").GetInt32() != c.population || a.GetProperty("alive").GetInt32() != c.population
                || a.GetProperty("mapFingerprint").GetString() != b.mapFingerprint
                || a.GetProperty("pawnFingerprint").GetString() != b.pawnFingerprint
                || a.GetProperty("workload").GetString() != b.workload || a.GetProperty("units").GetInt32() != b.unitCount
                || a.GetProperty("radioOperators").GetInt32() != b.radioOperators
                || a.GetProperty("warmupTicks").GetInt32() != b.warmupTicks || a.GetProperty("sampleTicks").GetInt32() != b.sampleTicks
                || a.GetProperty("seed").GetString() != b.seed)
                throw new ArgumentException("Audit/profile conditions differ: " + path);
            if (c.population > 0 && a.GetProperty("moved").GetInt32() < c.population * .95)
                throw new ArgumentException("Insufficient movement; preserve failed run separately: " + path);
            if (c.population > 0 && b.workload == "sapper-wall" && a.GetProperty("sapperEligiblePawns").GetInt32() == 0)
                throw new ArgumentException("Sapper fixture has no eligible vanilla sapper: " + path);
            var tick = c.methods.Single(m => m.method.StartsWith("Verse.TickManager.DoSingleTick("));
            if (!tick.cpuMeasured || !Positive(tick.threadCpuMs) || tick.calls != c.endTick - c.startTick
                || c.endTick - c.startTick < b.sampleTicks || c.endTick - c.startTick > b.sampleTicks + 10)
                throw new ArgumentException("Missing/partial actual tick CPU: " + path);
            if (b.engine != "legacy" && (a.GetProperty("legacyComponents").GetArrayLength() != 0
                || a.GetProperty("installedLegacyHooks").GetArrayLength() != 0))
                throw new ArgumentException("Legacy engine leaked into control: " + path);
            if (b.engine == "legacy" && (a.GetProperty("legacyComponents").GetArrayLength() != 11
                || a.GetProperty("installedLegacyHooks").GetArrayLength() == 0))
                throw new ArgumentException("Legacy engine was not actually installed: " + path);
            return new Run(c, a, path);
        }).ToArray();
        if (result.Length == 0) throw new ArgumentException("No captures in " + root);
        return result;
    }
    private static bool Positive(double? value) => value.HasValue && double.IsFinite(value.Value) && value.Value > 0;
    private static string Key(ProfileSnapshot c) => JsonSerializer.Serialize(new {
        c.assemblySha256, c.population, c.scenario, c.speed, c.gameVersion, c.cpuSource, c.runtime, c.operatingSystem,
        c.spikeTracing, c.spikeThresholdMs,
        mods = string.Join(";",c.mods), targets = string.Join(";",c.methods.Select(m=>m.method).Order()),
        c.benchmark.fixtureVersion,c.benchmark.seed,c.benchmark.mapFingerprint,c.benchmark.pawnFingerprint,
        c.benchmark.faction,c.benchmark.workload,c.benchmark.requestedPopulation,c.benchmark.warmupTicks,
        c.benchmark.fixtureCase,
        c.benchmark.sampleTicks,c.benchmark.unitCount,c.benchmark.radioOperators,
        reference=c.reference.method+":"+c.reference.workload+":"+c.reference.iterations+":"+string.Join(";",(c.reference.patchOwners ?? Array.Empty<string>()).Order()) });
    private static double Median(IEnumerable<double> values)
    {
        var sorted=values.Order().ToArray();
        return sorted.Length%2==1 ? sorted[sorted.Length/2] : (sorted[sorted.Length/2-1]+sorted[sorted.Length/2])/2;
    }
    private static double TickCpu(Run r) => r.Capture.methods.Single(m=>m.method.StartsWith("Verse.TickManager.DoSingleTick(")).threadCpuMs/(r.Capture.endTick-r.Capture.startTick);
    private static double WindowCpu(Run r) => r.Capture.mainThreadWindowCpuMs!.Value/(r.Capture.endTick-r.Capture.startTick);
    private static double ProcessCpu(Run r) => r.Capture.processWindowCpuMs!.Value/(r.Capture.endTick-r.Capture.startTick);
    private static object Range(Run[] runs, Func<Run,double> value) => new {
        median=Median(runs.Select(value)), min=runs.Min(value), max=runs.Max(value), values=runs.Select(value).ToArray() };
    internal static object Compare(string baselineRoot,string candidateRoot)
    {
        var baseline=Read(baselineRoot); var candidate=Read(candidateRoot);
        if (baseline.Any(r=>r.Capture.selectedEngine!="vanilla") || candidate.Select(r=>r.Capture.selectedEngine).Distinct().Count()!=1)
            throw new ArgumentException("Baseline must be vanilla; candidate must contain one selected engine.");
        var a=baseline.GroupBy(r=>Key(r.Capture)).ToDictionary(g=>g.Key);
        var b=candidate.GroupBy(r=>Key(r.Capture)).ToDictionary(g=>g.Key);
        var keys=a.Keys.Intersect(b.Keys).ToArray();
        if (keys.Length==0) throw new ArgumentException("No matching engine conditions (build, geometry, pawns, targets, preparation). Start phases are intentionally engine-specific.");
        return new { warning="Matched fixed-window CPU comparison, not proof of equivalent tactics. New fallback cannot pass the new AI gate. Core percentages are elapsed-time normalization, not CPU occupancy.",
            baselineEngine="vanilla",candidateEngine=candidate[0].Capture.selectedEngine,
            unmatchedBaseline=a.Keys.Except(keys).ToArray(), unmatchedCandidate=b.Keys.Except(keys).ToArray(),
            groups=keys.Select(k=> {
                var av=a[k].ToArray();var bv=b[k].ToArray();double tickRatio=Median(bv.Select(TickCpu))/Median(av.Select(TickCpu));
                double windowRatio=Median(bv.Select(WindowCpu))/Median(av.Select(WindowCpu));
                bool progression=av.Concat(bv).All(r=>r.Capture.population==0 || r.Audit.GetProperty("entered").GetInt32()>0);
                bool functional=bv.All(r=>r.Capture.benchmark.fixtureVersion >= 7
                    && r.Audit.TryGetProperty("newFunctionalComplete", out var done) && done.GetBoolean()
                    && r.Audit.TryGetProperty("newPhysicalPlansValid", out var physical) && physical.GetBoolean()
                    && r.Audit.GetProperty("newCompletedUnits").GetInt32() == r.Capture.benchmark.unitCount
                    && r.Audit.GetProperty("newEnteredByOrder").GetInt32() == r.Capture.population);
                bool eligible=bv.All(r=>r.Capture.selectedEngine=="new" && r.Capture.newEngineImplemented)
                    && av.Length>=3 && bv.Length>=3 && progression && functional;
                return new {conditions=JsonSerializer.Deserialize<JsonElement>(k),baselineRuns=av.Length,candidateRuns=bv.Length,
                    baselineTickCpuMs=Median(av.Select(TickCpu)),candidateTickCpuMs=Median(bv.Select(TickCpu)),tickCpuRatio=tickRatio,
                    baselineWindowCpuMs=Median(av.Select(WindowCpu)),candidateWindowCpuMs=Median(bv.Select(WindowCpu)),windowCpuRatio=windowRatio,
                    baselineProcessCpuMs=Median(av.Select(ProcessCpu)),candidateProcessCpuMs=Median(bv.Select(ProcessCpu)),
                    cpuMsPerTickRange=new { baselineTick=Range(av,TickCpu), candidateTick=Range(bv,TickCpu),
                        baselineWindow=Range(av,WindowCpu), candidateWindow=Range(bv,WindowCpu),
                        baselineProcess=Range(av,ProcessCpu), candidateProcess=Range(bv,ProcessCpu) },
                    actualEntryProgress=progression,newAiFunctionalComplete=functional,newAiPerformanceGateEligible=eligible,
                    within2x=tickRatio<=2 && windowRatio<=2,newAiFixedWindowCpuGatePassed=eligible && tickRatio<=2 && windowRatio<=2,
                    baselineProgress=av.Select(r=>r.Audit),candidateProgress=bv.Select(r=>r.Audit),
                    methods=bv[0].Capture.methods.Select(m=>new {m.method,
                        baselineCorePercentPerTick=Median(av.Select(r=>ReferenceMetrics.Percent(r.Capture.methods.Single(x=>x.method==m.method).inclusiveMs,r.Capture.endTick-r.Capture.startTick,r.Capture.reference)!.Value)),
                        candidateCorePercentPerTick=Median(bv.Select(r=>ReferenceMetrics.Percent(r.Capture.methods.Single(x=>x.method==m.method).inclusiveMs,r.Capture.endTick-r.Capture.startTick,r.Capture.reference)!.Value)),
                        baselineCalls=av.Sum(r=>r.Capture.methods.Single(x=>x.method==m.method).calls),candidateCalls=bv.Sum(r=>r.Capture.methods.Single(x=>x.method==m.method).calls)
                    }).ToArray()};
            }).ToArray()};
    }
}
