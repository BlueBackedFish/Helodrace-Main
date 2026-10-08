using System.Runtime.Serialization.Json;
using System.Text.Json;
using Helodrace.Profiling;

internal static class Program
{
    private static readonly JsonSerializerOptions options = new() { IncludeFields = true, WriteIndented = true };
    private static T Read<T>(string path)
    {
        // Mono and Windows may briefly hold a destination during replacement.
        // Retry reads only; never resend a state-changing command on an IO retry.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), options);
            }
            catch (IOException) when (attempt < 40) { Thread.Sleep(25); }
        }
    }
    private static string ReadText(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 40) { Thread.Sleep(25); }
        }
    }
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, options));
    private static ProfileSnapshot[] Captures(IEnumerable<string> paths) => paths.SelectMany(path => Directory.Exists(path)
        ? Directory.GetFiles(path, "capture-*.json", SearchOption.AllDirectories) : new[] { path }).Select(Read<ProfileSnapshot>).ToArray();
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Usage: capabilities ROOT | search ROOT TEXT | hotspots CAPTURE [inclusive|self|cpu|calls] [TOP] | spikes CAPTURE [TICK] | pawn CAPTURE PAWN_ID [TICK] | compare BEFORE AFTER | engine-compare VANILLA_ROOT CANDIDATE_ROOT | aggregate ROOT... | benchmark-compare BEFORE_ROOT AFTER_ROOT | start ROOT [SECONDS] [cpu] [spikes] [threshold=MS] [pawn=ID] | stop ROOT | status ROOT");
            switch (args[0])
            {
                case "capabilities": Print(Read<ProfileSnapshot>(Path.Combine(args[1], "capabilities.json"))); break;
                case "search":
                    Print(Read<ProfileSnapshot>(Path.Combine(args[1], "capabilities.json")).methods
                        .Where(m => m.method.Contains(args.ElementAtOrDefault(2) ?? "", StringComparison.OrdinalIgnoreCase)).Take(128)); break;
                case "hotspots":
                    var capture = Read<ProfileSnapshot>(args[1]);
                    string metric = args.ElementAtOrDefault(2) ?? "self";
                    int top = Math.Clamp(int.Parse(args.ElementAtOrDefault(3) ?? "25"), 1, 128);
                    double Value(ProfileMethod m) => metric switch { "inclusive" => m.inclusiveMs, "self" => m.trackedSelfMs,
                        "cpu" => m.threadCpuMs, "calls" => m.calls, _ => throw new ArgumentException("Unknown metric.") };
                    int ticks = capture.endTick - capture.startTick;
                    Print(new { capture.label, capture.complete, capture.dropped, ticks, capture.wallSeconds,
                        capture.selectedEngine, capture.effectiveEngine, capture.newEngineImplemented,
                        capture.mainThreadWindowCpuMs, capture.processWindowCpuMs, capture.reference, capture.benchmark, capture.slowCalls,
                        warning = "Inclusive times overlap. Tracked self includes uninstrumented children and profiler overhead. CPU is coarse and only present for cpuMeasured scopes.",
                        methods = capture.methods.Where(m => m.calls > 0 && (metric != "cpu" || m.cpuMeasured)).OrderByDescending(Value).Take(top)
                            .Select(m => new { m.method, m.calls, m.exceptions, m.inclusiveMs, m.trackedSelfMs, m.maxMs, m.distributionSamples, m.p50Ms, m.p95Ms, m.p99Ms, m.cpuMeasured, m.threadCpuMs,
                                inclusiveMsPerTick = ticks > 0 ? m.inclusiveMs / ticks : (double?)null,
                                referencePercentPerCall = ReferenceMetrics.Percent(m.inclusiveMs, m.calls, capture.reference),
                                referencePercentPerTick = ReferenceMetrics.Percent(m.inclusiveMs, ticks, capture.reference),
                                p95ReferencePercent = ReferenceMetrics.Percent(m.p95Ms, 1, capture.reference),
                                p99ReferencePercent = ReferenceMetrics.Percent(m.p99Ms, 1, capture.reference),
                                selfReferencePercentPerTick = ReferenceMetrics.Percent(m.trackedSelfMs, ticks, capture.reference) }) }); break;
                case "spikes":
                    var traced = Read<ProfileSnapshot>(args[1]);
                    if (!traced.spikeTracing) throw new ArgumentException("Spike tracing was disabled. Capture with preset=spikes or start ... spikes.");
                    int? requestedTick = args.Length > 2 ? int.Parse(args[2]) : null;
                    var names = traced.methods.ToDictionary(m => m.id, m => m.method);
                    object Describe(ProfileSlowCall c) => new { method = names.GetValueOrDefault(c.methodId, "unknown"),
                        c.callId, c.parentCallId, c.rootCallId, c.depth, c.tick, c.frame, c.mapId, c.pawnId, c.squadId, c.phase, c.job,
                        c.startMs, c.milliseconds, c.trackedSelfMs, c.threadCpuMs,
                        referencePercentPerCall = ReferenceMetrics.Percent(c.milliseconds, 1, traced.reference) };
                    Print(new { traced.complete, traced.dropped, traced.spikeThresholdMs, traced.spikeCandidates, traced.spikePawnId,
                        traced.spikeCapacity, traced.spikeCallCapacity,
                        warning = "Only selected main-thread calls are recorded. Whole-tick method totals include truncated and pawn-filtered details. Inclusive durations overlap; elapsed is not CPU. Tracked self includes uninstrumented work and overhead. Root CPU is coarse. GC deltas indicate coincidence, not causation. Truncated trees may omit parents.",
                        spikes = (traced.tickSpikes ?? Array.Empty<ProfileTickSpike>()).Where(s => requestedTick == null || s.root.tick == requestedTick)
                            .Select(s => new { root = Describe(s.root), s.callsSeen, s.callsFiltered, s.detailsDropped, s.detailsComplete, s.gc0, s.gc1, s.gc2,
                                s.methodsComplete,
                                wholeTickMethods = s.methods?.OrderByDescending(m => m.trackedSelfMs).Select(m => new {
                                    method = names.GetValueOrDefault(m.methodId, "unknown"), m.calls, m.exceptions,
                                    m.inclusiveMs, m.trackedSelfMs, m.maxMs, meanMs = m.inclusiveMs / m.calls,
                                    trackedSelfPercentOfTick = s.root.milliseconds > 0 ? m.trackedSelfMs / s.root.milliseconds * 100 : (double?)null,
                                    referencePercentPerCall = ReferenceMetrics.Percent(m.inclusiveMs, m.calls, traced.reference),
                                    referencePercentPerTick = ReferenceMetrics.Percent(m.inclusiveMs, 1, traced.reference) }),
                                costlyPawnCalls = s.calls.Where(c => c.pawnId >= 0).OrderByDescending(c => c.trackedSelfMs).Take(12).Select(Describe),
                                calls = s.calls.Select(Describe) }),
                        longestCalls = (traced.slowCalls ?? Array.Empty<ProfileSlowCall>()).Where(c => requestedTick == null || c.tick == requestedTick).Select(Describe) }); break;
                case "pawn":
                    if (args.Length < 3) throw new ArgumentException("Usage: pawn CAPTURE PAWN_ID [TICK]");
                    Print(PawnDiagnostics.Describe(Read<ProfileSnapshot>(args[1]), int.Parse(args[2]),
                        args.Length > 3 ? int.Parse(args[3]) : (int?)null)); break;
                case "engine-compare": Print(EngineComparison.Compare(args[1], args[2])); break;
                case "compare":
                    var before = Read<ProfileSnapshot>(args[1]); var after = Read<ProfileSnapshot>(args[2]);
                    if (!before.complete || !after.complete || before.dropped != 0 || after.dropped != 0
                        || before.selectedEngine != after.selectedEngine || before.effectiveEngine != after.effectiveEngine
                        || before.newEngineImplemented != after.newEngineImplemented
                        || before.population != after.population || before.scenario != after.scenario
                        || before.speed != after.speed || before.gameVersion != after.gameVersion
                        || before.cpuSource != after.cpuSource || before.runtime != after.runtime || before.operatingSystem != after.operatingSystem
                        || before.spikeTracing != after.spikeTracing || before.spikeThresholdMs != after.spikeThresholdMs
                        || before.spikePawnId != after.spikePawnId
                        || (before.spikeTracing && before.schema != after.schema)
                        || !before.mods.SequenceEqual(after.mods) || before.endTick <= before.startTick || after.endTick <= after.startTick)
                        throw new ArgumentException("Comparison rejected: incomplete capture, no ticks, or differing scenario/population/speed/game/mods.");
                    bool sameTargets = before.methods.Select(m => m.method).OrderBy(name => name, StringComparer.Ordinal)
                        .SequenceEqual(after.methods.Select(m => m.method).OrderBy(name => name, StringComparer.Ordinal));
                    if (!sameTargets)
                        throw new ArgumentException("Comparison rejected: instrumentation target sets differ; use the same preset and additional targets.");
                    if (!ReferenceMetrics.Comparable(before.reference, after.reference))
                        throw new ArgumentException("Comparison rejected: missing/invalid Core reference or different reference workload/patches.");
                    if (before.benchmark != null || after.benchmark != null)
                    {
                        var a = before.benchmark; var b = after.benchmark;
                        if (a == null || b == null || a.fixtureVersion != b.fixtureVersion || a.seed != b.seed
                            || a.mapFingerprint != b.mapFingerprint || a.faction != b.faction || a.startPhases != b.startPhases
                            || a.pawnFingerprint != b.pawnFingerprint || a.workload != b.workload || a.fixtureCase != b.fixtureCase || a.requestedPopulation != b.requestedPopulation
                            || a.engine != b.engine || a.effectiveEngine != b.effectiveEngine || a.newEngineImplemented != b.newEngineImplemented
                            || a.warmupTicks != b.warmupTicks || a.sampleTicks != b.sampleTicks
                            || a.unitCount != b.unitCount || a.radioOperators != b.radioOperators)
                            throw new ArgumentException("Comparison rejected: benchmark map/phase/preparation/organization differs.");
                    }
                    var old = before.methods.ToDictionary(m => m.method);
                    Print(new { beforeBuild = before.assemblySha256, afterBuild = after.assemblySha256,
                        warning = "Scenario metadata cannot prove identical map, phase, or machine load; check audit evidence. Self comparisons require identical target sets.",
                        sameTargets, beforeReference = before.reference, afterReference = after.reference,
                        methods = after.methods.Where(m => old.ContainsKey(m.method)).Select(m => new { m.method,
                            beforeMsPerTick = old[m.method].inclusiveMs / (before.endTick - before.startTick),
                            afterMsPerTick = m.inclusiveMs / (after.endTick - after.startTick),
                            deltaMsPerTick = m.inclusiveMs / (after.endTick - after.startTick) - old[m.method].inclusiveMs / (before.endTick - before.startTick),
                            beforeReferencePercentPerTick = ReferenceMetrics.Percent(old[m.method].inclusiveMs, before.endTick - before.startTick, before.reference),
                            afterReferencePercentPerTick = ReferenceMetrics.Percent(m.inclusiveMs, after.endTick - after.startTick, after.reference),
                            deltaReferencePercentagePoints = ReferenceMetrics.Percent(m.inclusiveMs, after.endTick - after.startTick, after.reference)
                                - ReferenceMetrics.Percent(old[m.method].inclusiveMs, before.endTick - before.startTick, before.reference),
                            beforeP95Ms = old[m.method].p95Ms, afterP95Ms = m.p95Ms,
                            beforeP99Ms = old[m.method].p99Ms, afterP99Ms = m.p99Ms,
                            beforeP95ReferencePercent = ReferenceMetrics.Percent(old[m.method].p95Ms, 1, before.reference),
                            afterP95ReferencePercent = ReferenceMetrics.Percent(m.p95Ms, 1, after.reference),
                            beforeP99ReferencePercent = ReferenceMetrics.Percent(old[m.method].p99Ms, 1, before.reference),
                            afterP99ReferencePercent = ReferenceMetrics.Percent(m.p99Ms, 1, after.reference) })
                            .OrderByDescending(m => Math.Abs(m.deltaReferencePercentagePoints ?? 0)).Take(25) }); break;
                case "aggregate":
                    Print(BenchmarkAggregate.Summarize(Captures(args.Skip(1)))); break;
                case "benchmark-compare":
                    Print(BenchmarkAggregate.Compare(Captures(new[] { args[1] }), Captures(new[] { args[2] }))); break;
                case "start": case "stop": case "status":
                    var root = Path.GetFullPath(args[1]);
                    if (!File.Exists(Path.Combine(root, "capabilities.json"))) throw new ArgumentException("No profiler handshake. Launch the game with -hdMethodProfile=ROOT first.");
                    var command = new ProfileCommand { action = args[0], label = "agent-cli", cpu = args.Contains("cpu"),
                        spikes = args.Contains("spikes") ? true : (bool?)null,
                        spikeThresholdMs = args.FirstOrDefault(a => a.StartsWith("threshold=", StringComparison.Ordinal)) is string thresholdArgument
                            ? double.Parse(thresholdArgument.Substring(10), System.Globalization.CultureInfo.InvariantCulture) : (double?)null,
                        spikePawnId = args.FirstOrDefault(a => a.StartsWith("pawn=", StringComparison.Ordinal)) is string pawnArgument
                            ? int.Parse(pawnArgument.Substring(5), System.Globalization.CultureInfo.InvariantCulture) : (int?)null,
                        seconds = args.Length > 2 && args[0] == "start" ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 10 };
                    if (command.spikeThresholdMs is double thresholdValue && (!double.IsFinite(thresholdValue) || thresholdValue < .1 || thresholdValue > 1000))
                        throw new ArgumentException("Spike threshold must be finite and between 0.1 and 1000ms.");
                    if (command.spikePawnId < 0) throw new ArgumentException("Spike pawn ID must be nonnegative.");
                    string path = Path.Combine(root, "command.json"), temporary = Path.Combine(root, "command-" + Guid.NewGuid().ToString("N") + ".tmp");
                    if (File.Exists(path)) throw new InvalidOperationException("A command is pending. Do not overwrite it.");
                    if (File.Exists(Path.Combine(root, "error.json"))) File.Delete(Path.Combine(root, "error.json"));
                    string previous = ReadText(Path.Combine(root, "status.json"));
                    using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(ProfileCommand)).WriteObject(stream, command);
                    File.Move(temporary, path);
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (timeout.Elapsed.TotalSeconds < 15)
                    {
                        Thread.Sleep(100);
                        string error = Path.Combine(root, "error.json"), status = Path.Combine(root, "status.json");
                        if (File.Exists(error)) throw new InvalidOperationException(Read<ProfileSnapshot>(error).label);
                        if (!File.Exists(path) && File.Exists(status) && ReadText(status) != previous) { Print(Read<ProfileStatus>(status)); return 0; }
                    }
                    throw new TimeoutException("Game did not acknowledge the command within 15 seconds; the pending command may still execute.");
                default: throw new ArgumentException("Unknown command.");
            }
            return 0;
        }
        catch (Exception error) { Print(new { error = error.Message }); return 1; }
    }
}
