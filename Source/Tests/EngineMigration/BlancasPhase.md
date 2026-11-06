# Blancas effect/crash split checks

Blancas 1.7 splits each stimulant's high and crash stages into `BD_<Drug>High` and `BD_<Drug>Crash`. `BlancasDrugChecks.Equivalent` compares states in release form: a crash hediff at severity s equals the release effect at s in stage 0, and an effect at or above the threshold equals stage 1. The map comes from `HediffCompProperties_EndsInCrash` by reflection, so it is empty and inert in the 1.6 bridge. Every other field stays exact.

```powershell
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-phase-fixture -Legacy16 -GameRoot "$PWD/.codex-staging/migration17/game16" -BridgeAssembly "$PWD/Source/Tests/EngineMigration/bin/Blancas16/Debug/net48/Helodrace.EngineMigrationChecks.dll" -RuntimeRoot .codex-staging/migration17/phase16-FRESH -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-phase-load -FixtureRoot .codex-staging/migration17/phase16-FRESH -RuntimeRoot .codex-staging/migration17/phase17-FRESH -NoWindow -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-phase-main -FixtureRoot .codex-staging/migration17/phase16-FRESH -RuntimeRoot .codex-staging/migration17/phaseMain17-FRESH -NoWindow -Wait
```

Set `[Environment]::CurrentDirectory` to the repository root first; `RuntimeRoot` is resolved against the process directory.

The 1.6 fixture doses 29 subjects through actual `Thing.Ingested` and sets the effect to half its threshold (crash), or threshold + 0.0004 (boundary). Kinds: crash, boundary, same-drug re-dose, deep sleep, a small 0.05 addition that stays a crash, and a competing amphetamine-group dose (not Modafinil). It saves, then records the reference timeline at elapsed 1/999/1000/2000/6000/20000/60000 ticks, with events at 1000, seeded per subject and segment.

1.7 checks release-form equality of the saved state on load; before the first tick, conversion of every crash-stage effect into the crash hediff at the effect's list position and info-window values/translations for all 13 drugs; after each checkpoint, that no live effect stays below its threshold; exact release-form equality of all 203 continued states; and a roundtrip save/load.

Verified 2026-11-06 on 1.7.5000 rev1275: `blancas-phase-fixture16` (actual 1.6.4871), `blancas-phase-load17c` and `blancas-phase-main17b` passed 584 checks each. With the split build, `blancas-drug-split-load17`/`-main17` (966 each), `blancas-chronic-split-load17`/`-main17` (113 each), `blancas-sleep-split-load17` (182) and `blancas-mkultra-split-load17` (306) also passed.
