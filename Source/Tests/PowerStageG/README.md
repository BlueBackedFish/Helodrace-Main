# Actual 1.7 power checks

Use PowerShell 7 from the repository root:

```powershell
Source/Tests/PowerStageG/Start-Checks.ps1 -RuntimeRoot .codex-staging/PowerStageG/fresh-name
Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/PowerStageG/fresh-name -TimeoutSeconds 45
```

Use one fresh directory per host; the runner refuses another live game. Observe the same PID after a timeout and collect logs after exit. The runtime isolates Main, Blancas, Harmony and this test module. It uses the actual installed 1.7 engine.

The generator fixture uses real diesel/steam sources, shared shaft load, conduits and a powered lamp. It tests ten targets, native output-menu callbacks, zero load, overload, stopped sources, actual save/load and resumed output. Full 8000 W is checked with other shaft users switched off. Conduits must not overlap the transmitting generator: the native engine refunds a transmitting building on load if it would wipe those conduits. The fixture uses adjacent connections instead.

Optional `-ResumeSave <path>` starts the remaining phase from this module's `Profile/Saves/PowerStageG.rws`. A resumed run needs the original run's evidence too. Only `COMPLETE` with no `FAIL` counts as success. Review collected errors separately; a pre-existing missing Workshop item is not a new test failure, and a null process exit code does not mean zero.