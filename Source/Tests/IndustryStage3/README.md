# Industry stage 3 actual-game checks

Run from the repository root using PowerShell 7.6 / .NET 10:

```powershell
Source/Tests/IndustryStage3/Start-Checks.ps1 -RuntimeRoot .codex-staging/IndustryStage3/fresh-name
Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/IndustryStage3/fresh-name -TimeoutSeconds 45
```

Use a fresh directory and observe the same PID after a timeout. The runner refuses to start over another live game. It uses the actual installed 1.7 engine with isolated Main/Blancas/Harmony mods and an independent test mod/profile. `-Baseline` records the high-pressure reactor's bill-giver registration before quitting.

The T2-37 regression uses real electrical generators, connected conduits, bills, job assignment, material consumption and products. It checks unpowered refusal, ordered and automatic 40-unit ammonium nitrate batches, actual save/load followed by a new automatic batch, and unchanged stirred-tank TNT production. The first worker stays drafted while the power net is prepared so automatic work cannot preempt the ordered-job check; other colonists' work is disabled. Generator spacing respects their footprint and power-network events are processed using the game's update method.

The test fills needs, suppresses random threats and fixes outdoor temperature at 21°C with a test-only prefix. `DebugSettings.noAnimals` is appropriate here because this fixture does not use caravans or pack animals. These controls isolate industrial behavior from climate, hunger and wildlife. They are not product changes. Actual save data lives at `Profile/Saves/IndustryStage3.rws`; `checks.txt` must finish with `COMPLETE` and no `FAIL`. Inspect the collected log after exit. A missing Workshop item error predates these changes; do not claim the entire log is free of errors or assume a null exit code means zero.

T2-14 checks remove the three generic barrel items and six machining recipes from the live Def database, complete an M1861 from its folded raw-material cost using the original linked treadle-lathe requirement, and generate a bazooka gunner with its launcher. Modular weapon barrel parts are separate and remain available.

T2-09/T2-06 checks run every 1/5/10 batch in seven component families on the real worktables and verify exact physical output and material consumption. A large steam engine powers the four shaft machines; the fixture refills it between batches, so this is not a fuel-endurance measurement. Count includes floor stock, player inventories and carried items. Storyteller incidents are disabled to keep external arrivals out of the production ledger.

T2-13 reuses native Remove floor designations and actual construction work. It checks pump/rig rejection, orders made before equipment is placed, cancellation, restoration of underlying soil, immediate record deletion without a lazy FieldAt lookup, replacement by another terrain, and removed/cancelled wells after a real save/load.

T2-10 uses native uninstall and install jobs. A mill leaves the mechanical manager when packed and registers once after reinstalling. The partially completed electronic batch and its bills remain on the packed basic worktable; another packed still retains exactly 37.5 fuel. The second real save/load preserves all of these. The loaded mill moves two cells along the shaft row, the basic worktable moves one cell east and reconnects to its machining facility, and the still moves four cells south. The original interrupted batch then finishes without extra ingredients or duplicated output.

The two saves are `Profile/Saves/IndustryStage3.rws` (reactor checkpoint) and `IndustryStage3Components.rws` (packed worktables, partial batch and oil state). Optional `-ResumeSave <absolute-path>` loads a checkpoint in a new isolated profile, retaining the same fixture phase. Use a checkpoint from this fixture revision; older fixture save formats are not supported. A resumed run covers only the remaining phase, so retain the earlier run's evidence too.