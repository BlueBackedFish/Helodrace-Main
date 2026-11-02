# Blancas chronic health and chemical-need continuation

This shared fixture source builds against the preserved 1.6 release and the actual 1.7 standalone/Main bridges. Product code, drug definitions and balance are not changed.

```powershell
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas16.csproj -p:BaseIntermediateOutputPath=obj/Blancas16/ --no-restore -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas.csproj -p:BaseIntermediateOutputPath=obj/Blancas17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-chronic-fixture -Legacy16 -GameRoot "$PWD/.codex-staging/migration17/game16" -BridgeAssembly "$PWD/Source/Tests/EngineMigration/bin/Blancas16/Debug/net48/Helodrace.EngineMigrationChecks.dll" -RuntimeRoot .codex-staging/migration17/chronic16-FRESH
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/chronic16-FRESH -TimeoutSeconds 45
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-chronic-load -FixtureRoot .codex-staging/migration17/chronic16-FRESH -RuntimeRoot .codex-staging/migration17/chronic17-FRESH -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/chronic17-FRESH -TimeoutSeconds 45
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-chronic-main -FixtureRoot .codex-staging/migration17/chronic16-FRESH -RuntimeRoot .codex-staging/migration17/chronicMain17-FRESH -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/chronicMain17-FRESH -TimeoutSeconds 45
```

Wait for each build to finish before copying its bridge. Serialize games; an observation timeout leaves the same PID running. Collect each run once. Never overwrite the input save.

## Scope

38 healthy adult Baseliner subjects cover eight chemical tolerances, six withdrawal definitions, six late recovery/cure states, all thirteen non-MKultra drugs with repeat doses and five configured post-high recovery effects. Seeded generation removes generated hediffs and traits. Definitions and serializers remain real.

Tolerance starts at .04; withdrawal addiction starts at 1 with need .12, and late cure addiction starts at .04 with supplied need 1. Modafinil/Caffeine inert addiction placeholders have zero need fall, so they are not counted as actual withdrawal definitions; their tolerances and ingestibles remain covered. All three repeat doses use actual Thing.Ingested at elapsed 0/60000/120000 ticks. Recovery cohorts use actual high removal callbacks before progressing their recovery effects.

The actual 1.6 fixture advances HealthTick and HealthTickInterval(1) on each living subject, and actual chemical NeedInterval at its pawn-specific 150-tick hash boundary. It saves at elapsed 15000 ticks before producing future reference states at 30000/60000/120000/180000. The same saved IDs and initial state continue in 1.7, preserving health hash phases. RNG is seeded separately for each subject/checkpoint segment. The 152 continuation records compare exact types, stages, severity/age, cached daily severity, disappearance counters and custom Comp state. Float values use exact float32 bits except for the narrowly defined continued core chemical-need allowance below. Need absence and a dead pawn's missing needs tracker are explicit data.

LoadedGame verifies original/roundtrip subjects before simulation resumes, then pauses the TickManager. The main-thread GameComponentUpdate invokes continuation, avoiding ordinary pawn ticks between the pre-load verification and controlled progression. The 1.7 resave represents the final timeline tick, including absolute ingestion timestamps; the quarter-day 1.6 input file is never changed. The roundtrip flag is test-component state only.

Saved-state/roundtrip comparisons remain exact, including chemical-need values. Only the **continued** core RimWorld.Need_Chemical level may differ by at most one ULP of that subject's original saved need level, and it must retain the same satisfied/desire/withdrawal category. Newly created needs with no saved counterpart have no allowance. Types, age, severity and every other field remain exact. Differences are retained in raw current XML and a separate numeric report, never silently omitted. This is an explicit numerical acceptance policy for cross-engine continuation; it is not a claim that the trajectories are bit-identical or a proof of a particular JIT cause.

This is isolated health/chemical-need progression over three game days and selected late-cure initial states, not full world/AI/eating/sleep jobs, natural rest/food depletion, complete mental-state scheduling, all rare outcomes or rendered frames. Other world pawns do not advance while the controlled timeline is stepped. No performance claim is made.

## Existing release defect and fixture development

The published BD_OpioidsAddiction withdrawal stage has Consciousness setMax **-0.20** and painOffset 5. It kills the selected otherwise healthy subject on withdrawal. The current 1.7 definition retains that value. This test records the terminal path and asserts death; it does not silently change balance to obtain a surviving withdrawal subject. Late cure uses a supplied need, allowing low addiction severity to recover without first entering the lethal stage. This pre-existing defect requires a separate balance decision/fix.

The first trial incorrectly counted the old default canBeAddicted flag on both inert placeholders. Subsequent trials exposed the recorder's living-needs assumption and identified opioid withdrawal death with subject-specific diagnostics. Need absence is now retained as an exact `absent` token instead of omitting fields. One diagnostic launch copied the preceding bridge before its rebuild finished; its manifest hash identifies that stale input and it is not counted as validation. The first 1.7 continuation had one ordinary pawn tick before the component tick; pausing on LoadedGame and using main-thread Update removed that fixture timing difference without relaxing age checks. The remaining tiny core-need difference is handled by the explicit policy above. All failed runs remain separate evidence; product values are unchanged.

The installed Hediff_Addiction, Need_Chemical, HediffComp_SeverityPerDay and HediffWithComps decompile identically across the preserved 1.6 and actual 1.7 assemblies. This informs the test design; source equality alone is not runtime compatibility evidence. All 50 preserved 1.6 distribution files were rechecked against 1.6-manifest.json with zero mismatches during this work.

## Results

Actual 1.6.4871 rev590 `blancas-chronic-fixture16f`: 48 checks passed. Actual 1.7.4995 rev309 standalone `blancas-chronic-load17c` and Main integration `blancas-chronic-main17`: 113 checks each passed. All three reached COMPLETE and exited; collected exit codes are null, not exit-zero evidence. This passes the three-configuration gate for the scope described above.

The original and both copied input saves have SHA-256 `567542487B3107B79DFE980A4DF5BD7ADD0546D355CB487CB21F178A03B029C6`. All 38 original subjects and each final resave restore exact health/need/Comp state before ticking. Both final continuations have 151 exact records and one record with only a core cocaine-need level difference: expected `3.2623984E-07`, actual `3.2637035E-07`, absolute error `1.305124897044152E-10`, saved-level ULP budget `7.450580596923828E-09`. Both categories remain withdrawal. All other record values are exact. Both 1.7 current progression XML files also match each other byte-for-byte.

The main and standalone 1.7 bridges build with zero warnings/errors. The final net48 bridge builds with zero errors and one NU1900 warning because NuGet vulnerability data is unreachable. The 1.6 nographics log retains known shader/atlas/research diagnostics. The final 1.7 ERROR in each run is the known missing Workshop item 3781143823. This does not prove error-free legacy rendering or full-map migration. Main product is unchanged at the ebf148f7 build SHA-256 `57AEFF42EDEFBD8372D389E84604FE386C330EE044694EB31CF995A54CF80CA4`; Blancas 1.7 remains `79C511B29962E031AD61948885949EE4BB86855088D07B11352F08B616448246` and the published 1.6 DLL remains `7070F6E9167B7332F02B006ABFFAB69D9FE036F66BBFABEAA5B69044D1858838`.
