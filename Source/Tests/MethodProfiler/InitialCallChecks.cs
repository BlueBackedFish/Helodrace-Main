using System.Runtime.Serialization.Json;
using System.Text.Json;
using Helodrace.Profiling;

internal static class InitialCallChecks
{
    internal static void Run()
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); }
        var clock = new FakeClock();
        var capture = new MethodCapture(clock, new[] { false, false }, traceSpikes: true, spikePawnId: 99);
        var outer = capture.Enter(1, new ProfileCallContext { Tick = 7, PawnId = 5, Identity = true });
        clock.Time += 1;
        var inner = capture.Enter(1); clock.Time += 2; capture.Leave(inner, true);
        clock.Time += 1; capture.Leave(outer, false);
        for (int i = 0; i < 10; i++) { var token = capture.Enter(1); clock.Time++; capture.Leave(token, false); }
        Check(capture.InitialRecords[1].Length == 8 && capture.InitialRecords[1][0].Elapsed == 4
            && capture.InitialRecords[1][0].Invocation == 1 && capture.InitialRecords[1][1].Invocation == 2
            && capture.InitialRecords[1][1].Elapsed == 2 && capture.InitialRecords[1][1].Context.PawnId == 5,
            "Initial calls retain entry order, recursion, inherited pawn and exceptions.");
        Check(capture.TickSpikes.All(s => s.Count == 0) && capture.Calls[1] == 12
            && capture.InitialRecords[1].Last().Invocation == 8,
            "First calls persist outside slow ticks and pawn filter, without replacing initial samples.");
        var draining = capture.Enter(1); capture.Stop(); capture.Leave(draining, false);
        Check(capture.Ready && capture.Calls[1] == 13, "Initial recording preserves stop/drain semantics.");
        Check(new MethodCapture(clock, new[] { false }).InitialRecords == null, "Disabled tracing allocates no initial buffers.");

        var sample = new ProfileSnapshot {
            complete = true, spikeTracing = true, initialCallCapacity = 8, startTick = 1, endTick = 3,
            reference = new ProfileReference { method = "core", workload = "fixed", iterations = 10000, sampleMs = new[] { 2.0 } },
            methods = new[] { new ProfileMethod { id = 1, method = "Need.Interval", calls = 10, inclusiveMs = 28, maxMs = 5 },
                new ProfileMethod { id = 2, method = "NeverCalled" } },
            initialCalls = Enumerable.Range(1, 8).Select(i => new ProfileSlowCall {
                methodId = 1, invocation = i, callId = i, milliseconds = 3, tick = i, pawnId = 5 }).ToArray(),
            slowCalls = new[] { new ProfileSlowCall { methodId = 1, invocation = 9, callId = 9, milliseconds = 2 } }
        };
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(typeof(ProfileSnapshot));
        serializer.WriteObject(stream, sample); stream.Position = 0;
        sample = (ProfileSnapshot)serializer.ReadObject(stream);
        Check(sample.schema == 9 && sample.initialCalls[7].invocation == 8, "Initial records serialize on game and CLI formats.");
        var json = JsonSerializer.SerializeToElement(InitialCallDiagnostics.Describe(sample, "need"));
        var method = json.GetProperty("methods")[0];
        Check(method.GetProperty("laterCalls").GetInt64() == 2 && method.GetProperty("laterMeanMs").GetDouble() == 2
            && method.GetProperty("initialReferencePercentPerCall").GetDouble() == 150
            && method.GetProperty("callsPerTick").GetDouble() == 5
            && method.GetProperty("largestRetainedLaterCallMs").GetDouble() == 2,
            "Initial query computes later average, rate and Core metrics without counting nested inclusive twice.");
        json = JsonSerializer.SerializeToElement(InitialCallDiagnostics.Describe(sample, "never"));
        Check(json.GetProperty("methods")[0].GetProperty("laterMeanMs").ValueKind == JsonValueKind.Null,
            "Unobserved methods do not pretend to have zero cost.");
        sample.endTick = sample.startTick;
        json = JsonSerializer.SerializeToElement(InitialCallDiagnostics.Describe(sample, "need"));
        Check(json.GetProperty("methods")[0].GetProperty("callsPerTick").ValueKind == JsonValueKind.Null, "Zero tick guard.");
        try { InitialCallDiagnostics.Describe(sample, "absent"); throw new Exception("Unknown method accepted."); }
        catch (ArgumentException) { }
        sample.initialCalls = null;
        try { InitialCallDiagnostics.Describe(sample, null); throw new Exception("Missing records accepted."); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: initial-call ordering, bounds, filters, drain, disabled buffers, serialization and CLI metrics.");
    }
}
