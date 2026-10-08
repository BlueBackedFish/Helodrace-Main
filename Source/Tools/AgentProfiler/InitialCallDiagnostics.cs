using Helodrace.Profiling;

internal static class InitialCallDiagnostics
{
    internal static object Describe(ProfileSnapshot capture, string filter)
    {
        if (!capture.spikeTracing || capture.initialCalls == null || capture.initialCallCapacity <= 0)
            throw new ArgumentException("Initial-call recording unavailable. Use a schema 8 capture with spike tracing enabled.");
        var retained = (capture.tickSpikes ?? Array.Empty<ProfileTickSpike>()).SelectMany(s => s.calls)
            .Concat(capture.slowCalls ?? Array.Empty<ProfileSlowCall>()).DistinctBy(c => c.callId).ToArray();
        var methods = capture.methods.Where(m => filter == null || m.method.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (methods.Length == 0) throw new ArgumentException("No selected method matches the filter.");
        int ticks = capture.endTick - capture.startTick;
        return new {
            capture.complete, capture.dropped, capture.initialCallCapacity, ticks,
            warning = "First captured invocations are not proof of JIT/cold-cache cost; game warmup may precede capture. Initial records ignore the pawn-detail filter. Later means cover all completed calls after the initial sample, but later maxima below come only from retained spike/long-call records. Inclusive times overlap; these are elapsed times, not method CPU.",
            methods = methods.Select(m => {
                var initial = capture.initialCalls.Where(c => c.methodId == m.id).OrderBy(c => c.invocation).ToArray();
                double total = initial.Sum(c => c.milliseconds);
                long laterCount = m.calls - initial.Length;
                return new {
                    m.method, m.calls, m.exceptions, m.inclusiveMs, m.maxMs,
                    callsPerTick = ticks > 0 ? m.calls / (double)ticks : (double?)null,
                    initialRecordedCalls = initial.Length, initialInclusiveMs = total,
                    initialMeanMs = initial.Length > 0 ? total / initial.Length : (double?)null,
                    initialMaxMs = initial.Length > 0 ? initial.Max(c => c.milliseconds) : (double?)null,
                    initialReferencePercentPerCall = ReferenceMetrics.Percent(total, initial.Length, capture.reference),
                    laterCalls = laterCount,
                    laterMeanMs = laterCount > 0 ? Math.Max(0, m.inclusiveMs - total) / laterCount : (double?)null,
                    laterReferencePercentPerCall = ReferenceMetrics.Percent(Math.Max(0, m.inclusiveMs - total), laterCount, capture.reference),
                    largestRetainedLaterCallMs = retained.Where(c => c.methodId == m.id && c.invocation > capture.initialCallCapacity)
                        .Select(c => (double?)c.milliseconds).DefaultIfEmpty(null).Max(),
                    initialCalls = initial.Select(c => new {
                        c.invocation, c.callId, c.parentCallId, c.rootCallId, c.tick, c.frame,
                        c.pawnId, c.mapId, c.squadId, c.phase, c.job, c.startMs, c.milliseconds, c.trackedSelfMs,
                        referencePercentPerCall = ReferenceMetrics.Percent(c.milliseconds, 1, capture.reference)
                    }).ToArray()
                };
            }).OrderByDescending(m => m.initialMaxMs).ToArray()
        };
    }
}
