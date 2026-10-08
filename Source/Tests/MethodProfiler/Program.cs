using System.Runtime.Serialization.Json;
using Helodrace.Profiling;

static void Check(bool value, string label) { if (!value) throw new Exception(label); }
var clock = new FakeClock();
var capture = new MethodCapture(clock, new[] { true, false }, 2);
var outer = capture.Enter(0); clock.Time = 2;
var inner = capture.Enter(0); clock.Time = 5; clock.Cpu = 20;
capture.Leave(inner, true); capture.Leave(inner, true); // Harmony finalizers must close once.
clock.Time = 10; clock.Cpu = 50; capture.Leave(outer, false);
Check(capture.Calls[0] == 2 && capture.Errors[0] == 1, "recursive close and exception");
Check(capture.Inclusive[0] == 13 && capture.TrackedSelf[0] == 10 && capture.CpuTicks[0] == 70, "inclusive/self/cpu nesting");
outer = capture.Enter(0); inner = capture.Enter(1); var overflow = capture.Enter(1);
Check(overflow.Equals(default(MethodToken)) && capture.Dropped == 1 && capture.DepthLimitCalls[1] == 1
    && capture.ForeignThreadCalls.Sum() == 0, "depth budget and rejection reason");
capture.Stop(); Check(!capture.Ready && capture.Enter(0).Equals(default(MethodToken)), "stop drains open calls");
capture.Leave(inner, false); capture.Leave(outer, false); Check(capture.Ready, "drained");
capture = new MethodCapture(clock, new[] { false });
var otherThread = new Thread(() => capture.Enter(0)); otherThread.Start(); otherThread.Join();
Check(capture.Dropped == 1 && capture.Calls[0] == 0 && capture.ForeignThreadCalls[0] == 1
    && capture.DepthLimitCalls.Sum() == 0, "foreign thread rejected with method identity");
outer = capture.Enter(0); capture.Leave(outer, false); capture.Stop();
Check(capture.Ready && capture.Calls[0] == 1, "foreign thread does not corrupt stack");
var snapshot = new ProfileSnapshot { complete = true, mainThreadWindowCpuMs = 123.5, processWindowCpuMs = 456.75,
    selectedEngine = "new", effectiveEngine = "vanilla-fallback", newEngineImplemented = false,
    methods = new[] { new ProfileMethod { calls = 2, method = "a\"한글", foreignThreadCalls = 3, depthLimitCalls = 4 } } };
using var stream = new MemoryStream();
var serializer = new DataContractJsonSerializer(typeof(ProfileSnapshot)); serializer.WriteObject(stream, snapshot); stream.Position = 0;
var roundtrip = (ProfileSnapshot)serializer.ReadObject(stream);
Check(roundtrip.methods[0].method == "a\"한글" && roundtrip.schema == 8
    && roundtrip.mainThreadWindowCpuMs == 123.5 && roundtrip.processWindowCpuMs == 456.75
    && roundtrip.selectedEngine == "new" && roundtrip.effectiveEngine == "vanilla-fallback"
    && !roundtrip.newEngineImplemented && roundtrip.methods[0].foreignThreadCalls == 3
    && roundtrip.methods[0].depthLimitCalls == 4, "structured JSON, rejection reasons and whole-window CPU roundtrip");
Console.WriteLine("Method profiler checks passed: nesting, exceptions, double finalizer, depth budget, stop drain, thread rejection, JSON.");
var reference = new ProfileReference { method = "core", workload = "fixed", iterations = 10000, sampleMs = new[] { 1.0, 2.0, 100.0 } };
Check(reference.MedianBatchMs == 2 && ReferenceMetrics.Percent(10, 10, reference) == 50, "median reference percentage");
var slowerReference = new ProfileReference { method = "core", workload = "fixed", iterations = 10000, sampleMs = new[] { 2.0, 4.0, 200.0 } };
Check(ReferenceMetrics.Percent(20, 10, slowerReference) == ReferenceMetrics.Percent(10, 10, reference), "common slowdown cancels");
Check(ReferenceMetrics.Percent(10, 0, reference) == null && ReferenceMetrics.Percent(10, 1, null) == null, "missing/zero units do not divide");
Check(ReferenceMetrics.Comparable(reference, slowerReference), "same reference workload");
slowerReference.iterations = 1; Check(!ReferenceMetrics.Comparable(reference, slowerReference), "different workload rejected");
reference.sampleMs = new[] { double.NaN }; Check(reference.MedianBatchMs == null, "invalid reference rejected");
Console.WriteLine("Core reference checks passed: robust median, environmental scale, invalid data and workload validation.");
capture = new MethodCapture(clock, new[] { false });
for (int i = 1; i <= 100; i++)
{
    outer = capture.Enter(0); clock.Time += i; capture.Leave(outer, false);
}
var quantiles = capture.Percentiles(0);
Check(quantiles.SequenceEqual(new[] { 50.0, 95.0, 99.0 }), "nearest rank call distribution");
Check(capture.SlowElapsed.Count(v => v > 0) == 16 && capture.SlowElapsed.Min() == 85, "bounded longest calls");
for (int i = 0; i < MethodCapture.DistributionCapacity; i++)
{
    outer = capture.Enter(0); clock.Time += 2; capture.Leave(outer, false);
}
Check(capture.Percentiles(0).All(v => v == 2), "distribution retains last bounded calls");
Console.WriteLine("Distribution checks passed: nearest rank, bounded longest calls, ring replacement.");
clock = new FakeClock { Time = 100 };
capture = new MethodCapture(clock, new[] { true, false, false }, traceSpikes: true, originTimestamp: 100);
outer = capture.Enter(0, new ProfileCallContext { Tick = 42, Frame = 7, PawnId = -1, MapId = -1, Phase = -1 });
clock.Time += 2;
inner = capture.Enter(1, new ProfileCallContext { Tick = 41, Frame = 7, PawnId = 99, MapId = 3, Phase = 2, SquadId = "squad", Job = "Breach", Identity = true });
var child = capture.Enter(2, new ProfileCallContext { Tick = 41, Frame = 7 });
clock.Time += 6; capture.Leave(child, true); clock.Time += 1; capture.Leave(inner, false);
clock.Time += 1; clock.Cpu = 100; capture.Leave(outer, false);
var tree = capture.TickSpikes.Single(s => s.Count > 0);
var actor = tree.Calls[0]; var parent = tree.Calls[1];
Check(tree.Root.Context.Tick == 42 && actor.Context.Tick == 42 && actor.Context.PawnId == 99
    && actor.Context.SquadId == "squad" && actor.Context.Phase == 2 && actor.Context.Job == "Breach", "normalized tick and actor inheritance");
Check(actor.ParentId == parent.CallId && parent.ParentId == tree.Root.CallId && actor.RootId == tree.Root.CallId
    && actor.Start == 2 && actor.Depth == 2 && tree.Root.Self == 3 && tree.Root.Cpu == 100, "timeline nesting and accounting");
Check(tree.Count == 3 && tree.Seen == 3 && capture.Errors[2] == 1 && capture.Dropped == 0, "trace preserves exception stats");
Check(tree.MethodsComplete && tree.Methods[2].Errors == 1 && tree.Methods[1].Inclusive == 7
    && tree.Methods.Sum(m => m.Self) == tree.Root.Elapsed, "nested tick method totals partition tracked time without inclusive double counting");
for (int duration = 11; duration <= 20; duration++)
{ outer = capture.Enter(0); clock.Time += duration; capture.Leave(outer, false); }
Check(capture.SpikeCandidates == 11 && capture.TickSpikes.Count(s => s.Count > 0) == 8
    && capture.TickSpikes.Min(s => s.Root.Elapsed) == 13, "eight largest tick trees retained");
outer = capture.Enter(0);
for (int i = 0; i < 700; i++) { inner = capture.Enter(1); clock.Time++; capture.Leave(inner, false); }
capture.Stop(); capture.Leave(outer, false);
tree = capture.TickSpikes.Single(s => s.Root.Elapsed == 700);
Check(tree.Count == 512 && tree.Seen == 701 && tree.Calls[511].CallId == tree.Root.CallId
    && capture.Ready && capture.Dropped == 0, "bounded trace reserves root, truncation distinct from stats loss, stop drain");
Check(tree.MethodsComplete && tree.Methods[1].Calls == 700 && tree.Methods[1].Inclusive == 700
    && tree.Methods[1].Self == 700 && tree.Methods[1].Maximum == 1 && tree.Methods[2].Calls == 0,
    "whole-tick totals retain truncated tail and clear previous tick data");
snapshot.spikeTracing = true; snapshot.spikeCandidates = capture.SpikeCandidates;
snapshot.tickSpikes = new[] { new ProfileTickSpike { root = new ProfileSlowCall { tick = 42, pawnId = 99, phase = "Breach", parentCallId = 1, startMs = 2 },
    calls = Array.Empty<ProfileSlowCall>(), callsSeen = 701, detailsDropped = 189, detailsComplete = false,
    methodsComplete = true, methods = new[] { new ProfileTickMethod { methodId = 1, calls = 700, inclusiveMs = 700, maxMs = 1 } } } };
stream.SetLength(0); stream.Position = 0; serializer.WriteObject(stream, snapshot); stream.Position = 0;
roundtrip = (ProfileSnapshot)serializer.ReadObject(stream);
Check(roundtrip.spikeTracing && roundtrip.tickSpikes[0].root.pawnId == 99 && roundtrip.tickSpikes[0].detailsDropped == 189,
    "spike JSON roundtrip");
Check(roundtrip.tickSpikes[0].methodsComplete && roundtrip.tickSpikes[0].methods[0].calls == 700
    && roundtrip.tickSpikes[0].methods[0].maxMs == 1, "whole-tick totals JSON roundtrip");
Check(!new MethodCapture(clock, new[] { false }).Tracing, "trace buffers opt-in");
capture = new MethodCapture(clock, new[] { false, false, false }, traceSpikes: true, spikePawnId: 99);
outer = capture.Enter(0);
for (int i = 0; i < 700; i++)
{ inner = capture.Enter(1, new ProfileCallContext { Identity = true, PawnId = 7 }); clock.Time++; capture.Leave(inner, false); }
inner = capture.Enter(1, new ProfileCallContext { Identity = true, PawnId = 99, Job = "Breach", Phase = 2 });
var nested = capture.Enter(2); clock.Time += 8; capture.Leave(nested, false); clock.Time++; capture.Leave(inner, false);
capture.Leave(outer, false); capture.Stop();
tree = capture.TickSpikes.Single(s => s.Count > 0);
Check(tree.Count == 3 && tree.Seen == 3 && tree.Filtered == 700 && capture.Calls[1] == 701
    && tree.Calls.Any(c => c.Method == 2 && c.Context.PawnId == 99) && capture.Dropped == 0,
    "pawn filter preserves late calls, inherited identity, root and whole-capture stats");
Check(tree.MethodsComplete && tree.Methods[1].Calls == 701 && tree.Methods[1].Inclusive == 709
    && tree.Methods[1].Self == 701 && tree.Methods[2].Inclusive == 8 && tree.Methods.Sum(m => m.Self) == tree.Root.Elapsed,
    "whole-tick aggregates ignore pawn detail filter and retain nested partition");
snapshot.spikePawnId = 99;
stream.SetLength(0); stream.Position = 0; serializer.WriteObject(stream, snapshot); stream.Position = 0;
Check(((ProfileSnapshot)serializer.ReadObject(stream)).spikePawnId == 99, "pawn filter JSON roundtrip");
capture = new MethodCapture(clock, new[] { false, false, false }, maximumDepth: 2, traceSpikes: true);
// Expensive tail has no detail slot, but must still appear in per-tick totals.
outer = capture.Enter(0);
for (int i = 0; i < 600; i++) { inner = capture.Enter(1); clock.Time++; capture.Leave(inner, false); }
inner = capture.Enter(2); clock.Time += 50; capture.Leave(inner, true); capture.Leave(outer, false);
tree = capture.TickSpikes.Single(s => s.Count > 0);
Check(!tree.Calls.Take(tree.Count).Any(c => c.Method == 2) && tree.Methods[2].Calls == 1
    && tree.Methods[2].Maximum == 50 && tree.Methods[2].Errors == 1 && tree.MethodsComplete,
    "expensive unseen tail survives bounded detail overflow");
inner = capture.Enter(2); clock.Time += 1000; capture.Leave(inner, false); // Outside any tick.
outer = capture.Enter(0); inner = capture.Enter(1); overflow = capture.Enter(2);
clock.Time += 2000; capture.Leave(inner, false); capture.Leave(outer, false);
tree = capture.TickSpikes.Single(s => s.Root.Elapsed == 2000);
Check(!tree.MethodsComplete && tree.Methods[2].Calls == 0 && tree.Methods[1].Calls == 1,
    "lost scopes mark totals incomplete; outside-tick calls do not leak into aggregates");
Console.WriteLine("Whole-tick checks passed: truncated expensive tail, filtered calls, nested accounting, reset, scope loss and JSON.");
PawnDiagnosticsChecks.Run();
InitialCallChecks.Run();
Console.WriteLine("Spike checks passed: timeline, tick and actor inheritance, top-eight retention, bounded truncation, drain, JSON and opt-in buffers.");
if (OperatingSystem.IsWindows())
{
    var windows = new WindowsMethodClock();
    long startProcessCpu = windows.ProcessCpu100ns();
    long startCpu = windows.Cpu100ns(); Thread.Sleep(150);
    double sleepCpuMs = (windows.Cpu100ns() - startCpu) / 10000.0;
    startCpu = windows.Cpu100ns(); var timer = System.Diagnostics.Stopwatch.StartNew();
    while (timer.ElapsedMilliseconds < 150) { }
    double busyCpuMs = (windows.Cpu100ns() - startCpu) / 10000.0;
    Check(busyCpuMs > 20 && busyCpuMs > sleepCpuMs, "OS thread CPU distinguishes sleep and busy work");
    Check(windows.ProcessCpu100ns() >= startProcessCpu, "process CPU clock is monotonic");
    Console.WriteLine($"Windows CPU check passed: sleep={sleepCpuMs:0.00}ms busy={busyCpuMs:0.00}ms (each 150ms elapsed).");
}
EngineComparisonChecks.Run();

sealed class FakeClock : IMethodClock
{
    public long Time, Cpu;
    public long Frequency => 1000;
    public long Timestamp() => Time;
    public long Cpu100ns() => Cpu;
}
