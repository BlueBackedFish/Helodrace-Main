# Blancas sleep migration checks

Run from the Main repository root with the preserved RimWorld 1.6 installation, current 1.7 installation and the sibling Blancas repository. This is a test bridge; its net48 assembly is not shipped with Main or Blancas.

```powershell
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas16.csproj -p:BaseIntermediateOutputPath=obj/Blancas16/ -p:LegacyGameRoot=C:/path/to/preserved/RimWorld16 -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas.csproj -p:BaseIntermediateOutputPath=obj/Blancas17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal

Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-sleep-fixture -Legacy16 -GameRoot C:/path/to/preserved/RimWorld16 -BridgeAssembly "$PWD/Source/Tests/EngineMigration/bin/Blancas16/Debug/net48/Helodrace.EngineMigrationChecks.dll" -RuntimeRoot .codex-staging/migration17/sleep-source16 -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-sleep-load -FixtureRoot .codex-staging/migration17/sleep-source16 -RuntimeRoot .codex-staging/migration17/sleep-standalone17 -NoWindow -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-sleep-main -FixtureRoot .codex-staging/migration17/sleep-source16 -RuntimeRoot .codex-staging/migration17/sleep-main17 -NoWindow -Wait
```

Use fresh runtime directories. A launcher observation timeout leaves the game running; check its recorded PID and continue `Wait-Migration.ps1` on the same runtime. Do not launch another game until that process has terminated. The default preserved game location for the net48 build is `.codex-staging/migration17/game16`; override `LegacyGameRoot` when needed. `Run-Migration.ps1` also accepts `BlancasRoot`, `HarmonyRoot` and `GameRoot` overrides.

The frozen 1.6 release creates 15 actual pawns: Sonata, Ambien and Restoril crossed with awake on floor, asleep on floor, awake in bed, asleep in bed before transition, and a completed transition. The fixture uses actual beds and LayDown jobs, setting the job's asleep/posture state to establish each condition deterministically. It does not test the AI decision to seek a bed or natural sleep onset.

The engine's HealthTick invokes the shipped Comp; no sleep predicates, removal callbacks, health methods or serializer are mocked. In 1.7, LoadedGame checks job/bed/asleep state, rest and severity bits, effect/Comp types and the saved hasTransitioned key before simulation advances. It then exercises bed-only transitions and repeated health ticks, saves under a separate name and checks the 1.7 reload before ticking again. A completed transition naturally saves DeepSleep without the removed SleepLogic Comp. The true latch is checked on the removed release Comp and its repeated callback, not represented as an attached serialized Comp.

The original save and XML snapshot are copied into each 1.7 runtime and remain untouched. Output includes `checks.txt`, `blancas-sleep.xml`, `blancas-sleep-roundtrip.xml`, `blancas-sleep-resumed.txt`, game saves, launcher hashes, engine log and result manifest. `migrationBlancasSleepRoundtrip` belongs only to the test GameComponent; no product save key changes.

Verified 2026-11-01: actual 1.6.4871 rev590 fixture creation passed 38 checks; actual 1.7.4995 rev309 standalone and Main+Blancas each passed 182. All 50 frozen Blancas distribution files still match the published manifest. This covers sleep transitions/save migration. Drug ingestion/overlap/removal, withdrawal and MKultra behavior have separate B2 work remaining; this does not prove all B2 behavior or reverse compatibility from 1.7 to 1.6.
