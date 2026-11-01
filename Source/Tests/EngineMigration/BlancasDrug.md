# Blancas drug ingestion and overlap migration checks

Use the build setup in [BlancasSleep.md](BlancasSleep.md). The Blancas16, Blancas17 and Main test bridges all include `BlancasDrugChecks.cs`. Run from the Main repository root with the preserved 1.6 game and unchanged published Blancas 1.6 distribution.

```powershell
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-drug-fixture -Legacy16 -GameRoot C:/path/to/preserved/RimWorld16 -BridgeAssembly "$PWD/Source/Tests/EngineMigration/bin/Blancas16/Debug/net48/Helodrace.EngineMigrationChecks.dll" -RuntimeRoot .codex-staging/migration17/drug-source16 -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-drug-load -FixtureRoot .codex-staging/migration17/drug-source16 -RuntimeRoot .codex-staging/migration17/drug-standalone17 -NoWindow -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-drug-main -FixtureRoot .codex-staging/migration17/drug-source16 -RuntimeRoot .codex-staging/migration17/drug-main17 -NoWindow -Wait
```

Use fresh runtime directories. Observation timeout leaves the recorded game running; poll that PID and resume `Wait-Migration.ps1` on the same runtime. Serialize game runs because early host logging uses a shared path.

The fixture creates healthy adult Baseliner pawns with no generated addictions, relations, scars or traits, and fixed initial rest/food. All 13 published non-MKultra ingestibles go through actual `Thing.Ingested`: 13 singles and 169 ordered pairs, including repeated same-drug doses, equal strength and stimulant/sedative order. Transient Compare* Hediffs must remove themselves after every dose. Five high effects are explicitly removed; the same five also expire through actual health callbacks, which must remove the high and configured stimulant factor and produce exactly one configured recovery effect. There are 187 subjects, 356 doses and 366 intermediate state records.

For the natural-expiry boundary, severity starts at 1e-7 and the actual pawn-specific 200-tick hash boundary is selected within 200 ticks. Both preserved 1.6 and current 1.7 already expose HealthTick and HealthTickInterval; the fixture calls both and restores the original global tick afterward. The interval split and 200-tick severity schedule are not changes introduced by 1.7. The first fixture attempt incorrectly expected decay on an arbitrary single tick and failed; that evidence is retained in `blancas-drug-fixture16`.

Actual effect/Comp types, body part, stage, severity, age, cached severity-per-day and disappearance timer, ExclusiveGroup strength, OnRemoval suppression, SleepLogic latch, and rest/food/BD need levels are recorded. Comparison parses only float-valued attributes to their exact float32 bits to avoid .NET Framework/.NET text formatting differences. There is no numerical tolerance, field omission or body-part normalization. Raw XML is retained.

A 1.6 save retains all 187 subjects. In 1.7, LoadedGame checks them before the first simulation tick; fresh actions run on the main thread in GameComponentTick and all 366 action records must match the 1.6 baseline. Then all 374 old/current subjects are saved under a separate 1.7 name and checked again before ticking. The product serializer and callbacks are not mocked or changed. The roundtrip flag belongs only to the test component.

Artifacts include `blancas-drug-actions.xml`, `blancas-drug-current-actions.xml`, initial/roundtrip state XML, original/new game saves, checks, launcher hashes, log and result. Original input saves are not overwritten.

Verified 2026-11-01: actual 1.6.4871 rev590 `blancas-drug-fixture16b` passed 398 checks; actual 1.7.4995 standalone `blancas-drug-load17` and Main integration `blancas-drug-main17` each passed 966. Source and both copied input saves share SHA-256 `C1EAF99AAD0F4001DD45019D77C5B09FA7E34891F67CEFD3EFA0E883BC24829F`. All 50 frozen Blancas files still match the published manifest. All three processes completed and exited; collected result exit codes are null, not evidence of exit code zero.

This verifies the selected drug actions and saved subjects. It does not simulate long-term withdrawal/addiction, tolerance accumulation, recovery decay, rare random events, eating jobs, full-map migration or rendered frames. Main's expanded Def set caused 432 compressed-map warnings about a turret-gun Def while loading this standalone 1.6 fixture; drug subject checks passed, but this run is not evidence of clean general map migration. Both 1.7 logs also contain existing host warnings and the missing Workshop item error. The 1.6 nographics run contains known shader/atlas and frozen release research-reference diagnostics. No product code or drug balance changed; B2 and the overall migration remain in progress.
