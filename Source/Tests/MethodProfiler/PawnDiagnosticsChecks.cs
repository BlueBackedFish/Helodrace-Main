using System.Text.Json;
using Helodrace.Profiling;

internal static class PawnDiagnosticsChecks
{
    internal static void Run()
    {
        var call = new ProfileSlowCall { methodId = 1, callId = 2, pawnId = 99, tick = 42,
            phase = "Breach", job = "Hammer", milliseconds = 9, trackedSelfMs = 2 };
        var capture = new ProfileSnapshot { complete = true, spikeTracing = true,
            methods = new[] { new ProfileMethod { id = 1, method = "DriverTick" } },
            slowCalls = new[] { call, new ProfileSlowCall { methodId = 1, callId = 4, pawnId = 99, tick = 43 } },
            tickSpikes = new[] { new ProfileTickSpike { root = new ProfileSlowCall { tick = 42 }, detailsComplete = true,
                calls = new[] { call, new ProfileSlowCall { methodId = 1, callId = 3, pawnId = 7, tick = 42 } } } } };
        var json = JsonSerializer.SerializeToElement(PawnDiagnostics.Describe(capture, 99, 42));
        if (json.GetProperty("calls").GetArrayLength() != 1 || json.GetProperty("additionalLongestCalls").GetArrayLength() != 0
            || json.GetProperty("methods")[0].GetProperty("retainedCalls").GetInt32() != 1
            || json.GetProperty("methods")[0].GetProperty("trackedSelfMs").GetDouble() != 2)
            throw new Exception("Pawn query must filter ID/tick, separate self and avoid duplicate slow calls.");
        json = JsonSerializer.SerializeToElement(PawnDiagnostics.Describe(capture, 99, null));
        if (json.GetProperty("additionalLongestCalls").GetArrayLength() != 1) throw new Exception("Independent slow calls lost.");
        capture.spikePawnId = 7;
        try { PawnDiagnostics.Describe(capture, 99, null); throw new Exception("Wrong pawn filter accepted."); }
        catch (ArgumentException) { }
        capture.spikeTracing = false;
        try { PawnDiagnostics.Describe(capture, 7, null); throw new Exception("Disabled trace accepted."); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: pawn diagnosis ID/tick filters, retained accounting, deduplication and missing data guards.");
    }
}
