# 1.7 production stage 1 checks

These checks run as an additional test mod in an isolated actual RimWorld 1.7 host. They use the production assembly, loaded Defs, real pawns, work givers, jobs and mechanical networks. The test mod is not included in the shipped assembly.

```powershell
& Source/Tests/ProductionStage1/Start-Checks.ps1 -RuntimeRoot '.codex-staging/ProductionStage1/example'
& Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot '.codex-staging/ProductionStage1/example' -TimeoutSeconds 45
Get-Content '.codex-staging/ProductionStage1/example/Evidence/checks.txt'
```

Use a new runtime directory for each launch. If observation times out, inspect the same recorded process before launching another host. A run passes only when `checks.txt` ends in `COMPLETE`, contains no `FAIL`, and its captured game log contains no new production errors. A null recorded exit code is not proof of exit code zero.

`-Baseline` records the unpowered bill-giver predicate and clothing-work-giver bindings without asserting the repaired behavior. The original baseline showed `usableAfterFueling=True` and no work giver for `HD_ClothingBench`.

The suite covers:

- Missing mechanical power rejects bill creation; powered tables can create a real `DoBill` job.
- A real steam engine and line shaft drive the lathe. Removing the shaft interrupts active recipe work; reconnecting it allows one completed product without duplicate output.
- The clothing work giver finds an ordinary apparel bill and a forced order completes the actual garment.
- A Helod pawn wears an actual IBTV preset, performs a front/rear plate swap at the clothing bench, and completes an edited armor configuration through its normal job.
- A slow large steel batch actually exhausts its fuel, keeps its physical ingredients and progress, is saved and loaded, then completes after refueling.
- A second pawn can refuel an occupied furnace; a competing refueler cannot reserve its service point. Normal hauling AI also refuels it while the maker continues work.
- All six iron/steel batch recipes complete at the worker's baseline labor speed and with a test-only 0.25 speed multiplier. `batch-measurements.md` records the observed speed, elapsed ticks, actual fuel usage and exact output. Fuel usage comes from physical fuel items plus tank contents, including refill rounding loss; elapsed time includes handling and refueling.
- Loaded mechanical and clothing workbenches complete new bills after the actual save/load roundtrip.
- Three engine types run for three days. The only enabled hauler is despawned for exactly eight hours per day; other colonists have hauling disabled. Fuel and heat-pump power are sampled every game tick. A sealed 25-cell cold room uses the existing heat pump's 40 W cooling setting and stays below 5 C after initial cooling.
- Disabled auto-refueling survives load, and unavailable/forbidden fuel produces no job.

The fixture clears a small map area, keeps colonist needs full, and excludes random threats, wildlife and mental states. Backstory-free workers receive valid body types for the host renderer. These controls isolate production behavior; work givers, jobs, refueling, inventories, temperature and reservations remain the real game implementations. `Source/Verify-ClothingWorkbench.ps1` separately covers recipe bindings, editor drafts, physical part identity, cancellation and saved work-order recovery.

`-FuelOnly` skips initial worktable jobs. To replay only sustained operation, supply a fixture checkpoint made by this suite:

```powershell
& Source/Tests/ProductionStage1/Start-Checks.ps1 -RuntimeRoot '.codex-staging/ProductionStage1/sustain' -SustainOnly -ResumeSave '.codex-staging/ProductionStage1/example/Profile/Saves/ProductionStage1Roundtrip.rws'
```

The source save is copied into the new test profile. Keep the checkpoint unchanged and use a fresh runtime directory for each replay.
