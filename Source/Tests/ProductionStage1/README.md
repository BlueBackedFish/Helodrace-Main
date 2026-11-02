# 1.7 production stage 1 checks

These checks run as an additional test mod in an isolated actual RimWorld 1.7 host. They use the production assembly, loaded Defs, real pawns, work givers, jobs and mechanical networks. The test mod is not included in the shipped assembly.

```powershell
& Source/Tests/ProductionStage1/Start-Checks.ps1 -RuntimeRoot '.codex-staging/ProductionStage1/example'
& Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot '.codex-staging/ProductionStage1/example' -TimeoutSeconds 45
Get-Content '.codex-staging/ProductionStage1/example/Evidence/checks.txt'
```

Use a new runtime directory for each launch. If observation times out, inspect the same recorded process before launching another host. A run passes only when `checks.txt` ends in `COMPLETE`, contains no `FAIL`, and its captured game log contains no new production errors. A null recorded exit code is not proof of exit code zero.

`-Baseline` records the unpowered bill-giver predicate and clothing-work-giver bindings without asserting the repaired behavior. The original baseline showed `usableAfterFueling=True` and no work giver for `HD_ClothingBench`.

The worktable checks cover:

- Missing mechanical power rejects bill creation; powered tables can create a real `DoBill` job.
- A real steam engine and line shaft drive the lathe. Removing the shaft interrupts active recipe work; reconnecting it allows one completed product without duplicate output.
- The clothing work giver finds an ordinary apparel bill and a forced order completes the actual garment.
- A Helod pawn wears an actual IBTV preset, performs a front/rear plate swap at the clothing bench, and completes an edited armor configuration through its normal job.

The fixture clears a small map area and keeps the test worker's needs full to isolate production behavior. It does not replace work-giver or job logic with stubs. `Source/Verify-ClothingWorkbench.ps1` separately covers recipe bindings, editor drafts, physical part identity, cancellation and saved work-order recovery.
