using Helodrace.Profiling;

internal static class PawnDiagnostics
{
    internal static object Describe(ProfileSnapshot capture, int pawnId, int? tick)
    {
        if (!capture.spikeTracing) throw new ArgumentException("Spike tracing was disabled.");
        if (pawnId < 0) throw new ArgumentException("Pawn ID must be nonnegative.");
        if (capture.spikePawnId.HasValue && capture.spikePawnId != pawnId)
            throw new ArgumentException("Capture details were filtered for another pawn.");
        var methods = capture.methods.ToDictionary(m => m.id, m => m.method);
        var spikes = (capture.tickSpikes ?? Array.Empty<ProfileTickSpike>())
            .Where(s => !tick.HasValue || s.root.tick == tick).ToArray();
        // Longest-call records may also exist in trees; do not count them twice.
        var calls = spikes.SelectMany(s => s.calls).Where(c => c.pawnId == pawnId).ToArray();
        var additional = (capture.slowCalls ?? Array.Empty<ProfileSlowCall>()).Where(c => c.pawnId == pawnId
            && (!tick.HasValue || c.tick == tick) && !calls.Any(d => d.callId == c.callId)).ToArray();
        object Call(ProfileSlowCall c) => new {
            method = methods.GetValueOrDefault(c.methodId, "unknown"), c.tick, c.callId, c.parentCallId, c.rootCallId,
            c.invocation,
            c.phase, c.job, c.squadId, c.startMs, c.milliseconds, c.trackedSelfMs,
            referencePercentPerCall = ReferenceMetrics.Percent(c.milliseconds, 1, capture.reference)
        };
        return new {
            capture.complete, capture.dropped, pawnId, capture.spikePawnId,
            warning = "These are retained slow-tick details, not whole-capture pawn totals. Inclusive time overlaps. Tracked self includes uninstrumented children and overhead, not self CPU. Filtered or truncated trees can omit parents. No records does not mean no cost.",
            retainedTicks = spikes.Select(s => new { tick = s.root.tick, s.detailsComplete, s.detailsDropped, s.callsFiltered }).ToArray(),
            methods = calls.GroupBy(c => new { c.methodId, c.phase, c.job, c.squadId }).Select(g => new {
                method = methods.GetValueOrDefault(g.Key.methodId, "unknown"), g.Key.phase, g.Key.job, g.Key.squadId,
                retainedCalls = g.Count(), inclusiveMs = g.Sum(c => c.milliseconds), trackedSelfMs = g.Sum(c => c.trackedSelfMs),
                meanMs = g.Average(c => c.milliseconds), maxMs = g.Max(c => c.milliseconds),
                referencePercentPerCall = ReferenceMetrics.Percent(g.Sum(c => c.milliseconds), g.Count(), capture.reference)
            }).OrderByDescending(g => g.trackedSelfMs).ToArray(),
            calls = calls.OrderBy(c => c.tick).ThenBy(c => c.startMs).ThenBy(c => c.callId).Select(Call).ToArray(),
            additionalLongestCalls = additional.Select(Call).ToArray()
        };
    }
}
