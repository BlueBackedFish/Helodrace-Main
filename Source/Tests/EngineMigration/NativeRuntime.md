# Native runtime host checks

Run against the installed 1.7 host with isolated profile and required Blancas dependency:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-runtime -RuntimeRoot .codex-staging/migration17/native-runtime-FRESH -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-runtime-FRESH -TimeoutSeconds 45
```

Do not restart on an observation timeout. Observe the same PID until exit and collect once.

## 1.7.4995 rev309 result

`native-runtime-background-4995`: 19 PASS, COMPLETE, process exited; collection exit code unavailable (null). Product SHA-256 `1EDFED8F6F7577B290F217FE716B97F1C3C9296C75FABF7DF4DC59CD92630C89`. Product/Main/standalone Blancas17 bridge builds: zero warnings/errors.

Native and compatibility background getters/setters reach the same actual window state for false and true. A finally block restores the original true setting, checked before the next frame. This validates replacing the audit-only bootstrap setter with EngineWindow.RunInBackground. The existing command-line gate and Prefs assignment remain. Normal-session focus/background behavior is not exercised by this fixture.

Existing runtime checks also pass: actual host runtime identity, monotonic clock, elapsed-duration agreement (native 0.3528574 s / compatibility 0.3529849 s), six actual frame advances in each counter, five absolute host paths and persistent-path agreement. Clock epochs and begin/end-frame counter positions differ, so absolute clock/frame values are not compared.

The native error diary captures an intentional `HELODRACE_EXPECTED_NATIVE_LOG_PROBE` error and filters its warning partner. The engine log contains that expected error and the known missing Workshop item 3781143823; it is not an error-free startup claim. This case does not test rendering, resource ownership, input, gameplay ticks or performance.

The installed UnityEngine.Application body delegates to UnityRuntime.Current.App; UnityAppState.RunInBackground delegates to EngineWindow.RunInBackground. UnityAppState is defined in UnityStubs.dll despite its Waylith.UnityCompat namespace; EngineWindow uses the existing Waylith.dll reference. No extra engine reference is needed.
