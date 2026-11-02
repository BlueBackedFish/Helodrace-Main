# Actual 1.7 stage 2 checks

Run from the repository root in PowerShell 7.6 / .NET 10:

```powershell
Source/Tests/Stage2/Start-Checks.ps1 -RuntimeRoot .codex-staging/Stage2/run-name
Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/Stage2/run-name -TimeoutSeconds 45
```

Use a fresh runtime directory. An observation timeout leaves the same host running; inspect its PID and re-run the observer. The runner refuses to launch over another game and uses an isolated profile, Main DLL and test-only mod. It never replaces installed mods or user saves. `-Baseline` records group selection, actual roster and worn-knife cached selection weights, then exits.

The test mod creates a clear home area and controlled colonists, keeps needs full, disables random threats, and uses actual game definitions, Harmony hooks, generation, trade deals, melee attacks, jobs and save/load. The ordinary colonist is human; the knife wearer is a Helod because the knife is race-restricted. Weapons and test wallets are supplied directly to isolate trading from production. Company travel deadlines and the initial wired-installation deadline are advanced in the fixture; requests, cell consumption, financial authorization and arrival incidents use production code. Caravan travel, visiting and map exit run through native Lord jobs without forced despawning.

Coverage:

The fixture holds outdoor temperature at 21°C through a test-only Harmony prefix to avoid climate exposure during long waits; product climate code is unchanged. Native animal simulation stays enabled so pack animals can carry goods and leave normally. The ten direct low-point groups use 20 points before the authored minimum. Arrival incidents use the game's own guard-point resolution (550–1,000), independent of the input point value; the runner records both values.

- Every loaded faction's financial eligibility and ledger operations, actual gift-goodwill callbacks, LOW service rewards, external faction rejection and persisted Helod credit. Standalone `TelegraphNetwork` and `SafeConductAccess` suites cover loans, arrears and nested access separately.
- Resolved radio construction data and retained listening recreation; actual 250 STL wired installation and duplicate-payment prevention.
- Ten low-point petroleum groups, wired request, hostile wireless request with scoped safe conduct, random trader-kind arrival, physical petroleum purchases and native return of all spawned caravans.
- Four buy-only Core civilian guns (revolver, autopistol, bolt-action rifle and pump shotgun), existing Helod weapon sales, military-gun exclusion, actual sale transaction, native silver settlement and unchanged Sthaler exchange prices.
- Ordinary start and 300 slave/refugee request checks, actual human pawn generation, rejection of Helod-only initial apparel candidates, explicit Helod/scenario generation, removed wanderer hooks and inactive benchmark generation.
- Rifle plus worn knife, native DPS selection weights, 200 automatic selections, actual damage and cooldown, removal/re-wear, actual `Stage2Roundtrip` save/load, replacement and transfer without duplicate or stale verbs.

`Evidence/checks.txt` must finish with `COMPLETE` and contain no `FAIL`. Collect and inspect `engine.log` after the recorded host exits. The pre-existing missing Workshop item and translation warnings must be distinguished from new errors. `launcher.json` records process identity and Main/engine hashes; `host-result.json` may have a null exit code when the process handle has already disappeared. Test code is outside the product project and excluded from `Assemblies/Helodrace.dll`.
