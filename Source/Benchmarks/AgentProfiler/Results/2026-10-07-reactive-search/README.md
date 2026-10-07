# Bounded reactive position search

2026-10-07. Stage4 baseline DLL `621edf86e9b482df54a0dca56080ccb9d1b683e8cf7d32f0a48b9d61464250c5` contains stage3 communication changes and the same reactive fixture, with the exhaustive FindReactivePosition implementation. After DLL `f0c00031a8848205a50ba9ddb0a1a2d2aa624a1e768cf43a2678924d8099359b` uses bounded candidates and shared physical scores.

Fixture3, LOW200/400/0, seed hd-perf-20261007, same map fingerprint, warm1800/sample900. Reactive fixture adds cover and sends support requests to active units at capture start. Extra instrumentation: FindReactivePosition, CoverReactiveTeam, FieldDefense; coarse defaults supply context. Captures completed without measured exceptions or dropped samples.

**Populated baseline runs are ineligible for strict comparison because of casualties.** The automatic review preserves this and admits only the zero-population baseline. Starting/ending phases and survival differ. `metrics.csv` retains the full raw observations, including ineligible runs; its percentages are exploratory, not proof of identical work. Inclusive parent scopes overlap.

FindReactivePosition calls: before200/397, after200/400. Observed Core-normalized per-tick reduction: 83.4%/87.1%. 400-pawn total elapsed364.32→47.09ms; mean917.7→117.7us; max9.197→0.402ms. 200-pawn after maximum8.314ms exceeds before5.288ms. The 400-pawn whole tick CPU worsened13.98→15.23ms/tick; no whole-tick improvement claim.

Core normalization uses each capture's median of seven timed 10,000-call GenRadial.NumCellsInRadius batches. Only DoSingleTick uses actual thread CPU; related method times are elapsed. Short method threadCpuMs=0 is not evidence of zero CPU cost. p95/p99 refer to the last2048 calls.

`excluded` preserves the failed initial warm600 audit; no eligible active plan was available, so its empty/partial timing is excluded. `functional` holds the final native movement regression fixture, independent of these profiling comparisons.

Functional baseline and final retry passed all seven stages. The final first run failed at stage1 with one pawn lacking job/order/assignment and a remaining ContactGuard. A similar intermittent failure was recorded in stage2, but neither a common cause nor independence from this optimization is established. All three runs are preserved; intermittent Security-stall diagnosis remains unresolved. Retry success does not erase the initial failure.

The final test/deployed build has the same production source, but an updated AssemblyInformationalVersion after the separate stage3 commit (SHA73be061b194ffe705ecc54b08e22ca2e0d5b607ced923992df0c72ef92225370). See [application report](../../../../../Docs/%EC%A0%84%EC%88%A0/Archive/2026-10-07/4%EC%88%9C%EC%9C%84%20%EB%8C%80%EC%9D%91%20%EC%9C%84%EC%B9%98%20%ED%83%90%EC%83%89%20%EB%8B%A8%EC%88%9C%ED%99%94%20%EC%A0%81%EC%9A%A9%20%EA%B2%B0%EA%B3%BC.md) for tradeoffs and limitations.
