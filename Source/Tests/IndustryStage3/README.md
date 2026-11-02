# Industry stage 3 actual-game checks

Run from the repository root using PowerShell 7.6 / .NET 10:

```powershell
Source/Tests/IndustryStage3/Start-Checks.ps1 -RuntimeRoot .codex-staging/IndustryStage3/fresh-name
Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/IndustryStage3/fresh-name -TimeoutSeconds 45
```

Use a fresh directory and observe the same PID after a timeout. The runner refuses to start over another live game. It uses the actual installed 1.7 engine with isolated Main/Blancas/Harmony mods and an independent test mod/profile. `-Baseline` records the high-pressure reactor's bill-giver registration before quitting.

The T2-37 regression uses real electrical generators, connected conduits, bills, job assignment, material consumption and products. It checks unpowered refusal, ordered and automatic 40-unit ammonium nitrate batches, actual save/load followed by a new automatic batch, and unchanged stirred-tank TNT production. The first worker stays drafted while the power net is prepared so automatic work cannot preempt the ordered-job check; other colonists' work is disabled. Generator spacing respects their footprint and power-network events are processed using the game's update method.

The test fills needs, suppresses random threats and fixes outdoor temperature at 21°C with a test-only prefix. `DebugSettings.noAnimals` is appropriate here because this fixture does not use caravans or pack animals. These controls isolate industrial behavior from climate, hunger and wildlife. They are not product changes. Actual save data lives at `Profile/Saves/IndustryStage3.rws`; `checks.txt` must finish with `COMPLETE` and no `FAIL`. Inspect the collected log after exit. A missing Workshop item error predates these changes; do not claim the entire log is free of errors or assume a null exit code means zero.
