# Communication frame scheduling comparison

2026-10-07. Stage 3, before: stage-2 DLL `ef47fca080cb8b9b0bb36c804d8e70dedbe9ed2ac55ac79c6c308adff5b875f5`; after: `ba74e868266300a9293510c348174f4da47908eac0ad54b479321b894758200f`.

Separate native game runs, fixture 2, seed `hd-perf-20261007`, warmup 600 ticks, sample 900 ticks. HIGH cases 18/19/15 generate 208/403/0 pawns; LOW cases 19/15 generate 400/0. Three extra methods: RefreshFrames, ProcessTick, RaidCommunicationPolicy.Delays. The coarse default targets supply tick/component context.

`before` and `after` preserve measurements, full captures, capabilities and compressed original logs. `metrics.csv` includes totals, calls, means, maxima, p95/p99, per-tick rates and per-capture Core normalization. `*-review.json` and `*-comparison.json` retain metadata eligibility and comparison checks. All captures complete, with no dropped samples or measured method exceptions.

Core = median of 7 timed 10,000-call GenRadial.NumCellsInRadius(float) batches. Method costs are elapsed time; only DoSingleTick has actual thread CPU data. Selected communication scope union sums trackedSelf across instrumented communication/Delays methods to avoid overlapping parent-child inclusive measurements. Work moved from ProcessTick to the per-tick pump, so ProcessTick alone is not a whole-system comparison.

HIGH403 selected union: 0.3417→0.1089 ms/tick, Core125.77→40.21% (68.0% reduction). LOW400: 0.1649→0.0561 ms/tick, Core60.82→20.11% (66.9%). RefreshFrames maximum: HIGH9.64→1.32ms, LOW5.91→0.69ms. These are one before/after observation, not statistical confidence bounds.

Behavior is not identical: HIGH moved one fewer pawn in each populated case; LOW moved two fewer and ended in EntryWait instead of Support. All survived. HIGH403 transport validation increased from 0 to 720 calls. Whole-tick CPU also decreased, but its larger change cannot be attributed solely to the roughly 0.23/0.11ms per-tick communication savings.

Intermediate after3-high and the unrelated reactive benchmark are excluded from this dataset. See [application report](../../../../../Docs/전술/3순위%20통신%20분산%20갱신%20적용%20결과.md) for behavioral tradeoffs and boundary tests.
