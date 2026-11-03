# Stage I native economy and research audit

Run `Start-Checks.ps1 -RuntimeRoot .codex-staging/StageI/fresh-name`, then collect that PID using `Source/Tests/Wait-Game17Host.ps1` with a bounded timeout. This uses actual 1.7, Main, Blancas and Harmony in an isolated profile. Every test keeps the ordinary note market/trade values while checking native wealth recounts for floor, player inventory/carry, foreign inventory, fog, equal-value silver, foreign caravan stock and an actual save/load.

The same host exports every effective research definition to `research-before.json`, including source, base/scenario cost, tech penalties, prerequisites and starting tags. This captures real loaded XML and patches instead of estimating vanilla values from older versions.

Currency baseline run `.codex-staging/StageI/currency-01`: 17 PASS and COMPLETE; only the existing missing Workshop folder error. Its pre-rebalance snapshot is preserved under `Docs/Data/StageI/research_before.json` for the research comparison. The old scenario multiplied Electricity by seven; this fixture's expected multiplier must follow the new policy once the research phase is applied.
