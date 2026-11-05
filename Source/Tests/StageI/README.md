# Stage I native economy and research audit

Run `Start-Checks.ps1 -RuntimeRoot .codex-staging/StageI/fresh-name`, then collect that PID using `Source/Tests/Wait-Game17Host.ps1` with a bounded timeout. This uses actual 1.7, Main, Blancas and Harmony in an isolated profile. Every test keeps the ordinary note market/trade values while checking native wealth recounts for floor, player inventory/carry, foreign inventory, fog, equal-value silver, foreign caravan stock and an actual save/load.

The same host exports every effective research definition to `research-current.json`, including source, base/scenario cost, tech penalties, prerequisites and starting tags. This captures real loaded XML and patches instead of estimating vanilla values from older versions.

Currency baseline run `.codex-staging/StageI/currency-01`: 17 PASS and COMPLETE; only the existing missing Workshop folder error. Its pre-rebalance snapshot is preserved under `Docs/Data/StageI/research_before.json` for the research comparison. The old snapshot records Electricity multiplied by seven. The current fixture tests tribal cost factors, changed base budgets, normal/explicit/debug completion, research-tab drawing and serialized scenario scope.

Research rebalance run `.codex-staging/StageI/research-01`: 323 PASS and COMPLETE, no new engine/save error. Actual loaded before/after snapshots and the 143-row comparison are preserved in `Docs/Data/StageI`. Run `python Source/Analysis/StageI/research_balance.py --check` from the repository root to reproduce the policy, route comparisons and plot.

Scenario-scoped budget run `.codex-staging/StageI/scenario-budget-215854`: 425 PASS and COMPLETE. Base-game research keeps its loaded costs outside a Helod scenario; `HD_HelodScenarioResearchBudget` applies only while the scenario has `HD_ResearchTechBaseline`, including after a reload that starts from base-game costs.
