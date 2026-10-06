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
Check(overflow.Equals(default(MethodToken)) && capture.Dropped == 1, "depth budget");
capture.Stop(); Check(!capture.Ready && capture.Enter(0).Equals(default(MethodToken)), "stop drains open calls");
capture.Leave(inner, false); capture.Leave(outer, false); Check(capture.Ready, "drained");
capture = new MethodCapture(clock, new[] { false });
var otherThread = new Thread(() => capture.Enter(0)); otherThread.Start(); otherThread.Join();
Check(capture.Dropped == 1 && capture.Calls[0] == 0, "foreign thread rejected");
outer = capture.Enter(0); capture.Leave(outer, false); capture.Stop();
Check(capture.Ready && capture.Calls[0] == 1, "foreign thread does not corrupt stack");
var snapshot = new ProfileSnapshot { complete = true, methods = new[] { new ProfileMethod { calls = 2, method = "a\"한글" } } };
using var stream = new MemoryStream();
var serializer = new DataContractJsonSerializer(typeof(ProfileSnapshot)); serializer.WriteObject(stream, snapshot); stream.Position = 0;
Check(((ProfileSnapshot)serializer.ReadObject(stream)).methods[0].method == "a\"한글", "structured JSON roundtrip");
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
if (OperatingSystem.IsWindows())
{
    var windows = new WindowsMethodClock();
    long startCpu = windows.Cpu100ns(); Thread.Sleep(150);
    double sleepCpuMs = (windows.Cpu100ns() - startCpu) / 10000.0;
    startCpu = windows.Cpu100ns(); var timer = System.Diagnostics.Stopwatch.StartNew();
    while (timer.ElapsedMilliseconds < 150) { }
    double busyCpuMs = (windows.Cpu100ns() - startCpu) / 10000.0;
    Check(busyCpuMs > 20 && busyCpuMs > sleepCpuMs, "OS thread CPU distinguishes sleep and busy work");
    Console.WriteLine($"Windows CPU check passed: sleep={sleepCpuMs:0.00}ms busy={busyCpuMs:0.00}ms (each 150ms elapsed).");
}

sealed class FakeClock : IMethodClock
{
    public long Time, Cpu;
    public long Frequency => 1000;
    public long Timestamp() => Time;
    public long Cpu100ns() => Cpu;
}
