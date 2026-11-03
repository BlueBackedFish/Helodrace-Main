# Actual 1.7 power checks

Use PowerShell 7 from the repository root:

```powershell
Source/Tests/PowerStageG/Start-Checks.ps1 -RuntimeRoot .codex-staging/PowerStageG/fresh-name
Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/PowerStageG/fresh-name -TimeoutSeconds 45
```

Use one fresh directory per host; the runner refuses another live game. Observe the same PID after a timeout and collect logs after exit. The runtime isolates Main, Blancas, Harmony and this test module. It uses the actual installed 1.7 engine.

The generator fixture uses real diesel/steam sources, shared shaft load, conduits and a powered lamp. It tests ten targets, native output-menu callbacks, zero load, overload, stopped sources, actual save/load and resumed output. Full 8000 W is checked with other shaft users switched off. Conduits must not overlap the transmitting generator: the native engine refunds a transmitting building on load if it would wipe those conduits. The fixture uses adjacent connections instead.

Optional `-ResumeSave <path>` starts the remaining phase from this module's `Profile/Saves/PowerStageG.rws`. A resumed run needs the original run's evidence too. Only `COMPLETE` with no `FAIL` counts as success. Review collected errors separately; a pre-existing missing Workshop item is not a new test failure, and a null process exit code does not mean zero.
`-Balance` continues after the generator's actual save/load with the four- and eight-colonist workloads. Each runs exactly 60000 sampled ticks and verifies the game clock advanced by exactly the same amount. The fixture pauses every frame to prevent automatic ticks from entering the fuel measurement. Use `-Population 4` or `-Population 8` with `-Balance` for an isolated scenario; final evidence for both populations is required.

Each colony has one real refuel hauler, one or two real crafters, connected mechanical machines, 4/8 standing lamps, a radio, and one/two enclosed cold rooms. The eight-colonist colony includes a CNC lathe. Needs are held full, outdoor temperature is fixed at 21 C, and storyteller/wildlife effects are suppressed. Fuel stock is provided, not produced by the colony. Power and refrigeration must remain available all day; cold rooms must remain under 5 C after the first half-day of initial cooling. Actual bill completion, exact products, idle/active draw, refuels and physical fuel consumption are checked.

`devices.csv` exports resolved engine Def values, including mechanical W, rated RPM and derived torque. `recipe-energy.csv` uses reference labor speed 1 work unit/tick and worktable speed/default stuff, with Wd per batch and per primary product. It excludes upstream ingredient processing and idle/haul/startup costs. `colonies.csv` and `duty.csv` are actual one-day measurements and observed duty cycles. A passing run preserves all four files with its logs and `checks.txt`.