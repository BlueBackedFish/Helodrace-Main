# Native sniper-control and research wrapper rectangles

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-last-ui -RuntimeRoot .codex-staging/migration17/native-last-ui-4995b -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-last-ui-4995b -TimeoutSeconds 45
```

Use a fresh root for a new run. Keep observing the same process after timeout; collect completion once and inspect the engine log.

Frozen baseline `218fa730`: full sniper dialog and research wrapper prefix retain original game-rectangle math. Frozen research host access forwards to the same unchanged current IsActive/TryDraw interfaces through a reflection adapter. Current callback and public research bridge signatures remain game types; owned layout is native and exported at host calls.

Actual 1.7.4995 rev309 `native-last-ui-4995b`: six migration checks, COMPLETE and observed process termination, collected exit code null. Product/Main/Blancas17 builds have no warnings/errors. Results:

- 960 sniper frames cross four rectangles (normal/fractional/zero/negative), six session states (absent, ready, expired, exhausted, ready-busy, expired-busy), rejection, four selected directions and no/direction/request/suppress/kill button responses. Real map component session lookup/active-strike state, actual unregistered base and real TryRequestSupport session logic execute. Compare layout/style/text/order plus selected direction and resulting session direction/actions/time. Observe 96 request and success-message calls, 64 targeting and close calls across both sides. Fixture sessions/effects are removed without clearing other bases' data.
- 3,456 research frames cross the same rectangles, six xMax inputs, four overflow heights, three tab counts, normal/other-tab/foreign-window guards, tab-switch outcomes and canvas acceptance. Compare header/viewport/inset inputs and independently assert expected return/tab behavior. Observe 2,304 tab-boundary and 1,152 canvas calls across both sides.
- Total 4,416 pairs, 22,096 ordered submissions and 254,368 float components; maximum difference 0, no nonzero differences.

The first run completed sniper comparisons but generic TabDrawer method detours did not intercept actual calls, so the synthetic tab records entered real rendering and failed. The final fixture uses test-only transpilers at the two generic call sites in each of the native/frozen research prefixes. Four distinct adapter sites are asserted. These adapters preserve exact current game signatures, record boundary arguments and supply identical controlled overflow/tab-switch results; product rectangle arithmetic is untouched. Outside the active fixture they forward to the real generic methods. No product abstraction or runtime shipped code is introduced.

Widget/font/color output, click responses, service rejection, tab overflow/switch and canvas result are controlled. Message posting, window close and targeting are captured rather than sent to live UI. Sniper support session logic is real, but services/communications/actual targeting actions and natural mouse/keyboard/GPU/resource lifecycle/performance are not proven. Research windows/tab records are synthetic and only initialize fields used by this wrapper; this does not validate native TabDrawer layout/input itself. The underlying research canvas retains its separate earlier verification. Shared Defs are not mutated; fixture support state, patches and ticks are restored/cleaned up.

Only ERROR in the final log is existing missing Workshop item 3781143823; native allocation warnings remain. Product SHA-256 `F637CD3FAC1ED39906F7AB648F85AA5A35BE8ADFDEC7EAFC28B4D71FCE269059`; metrics `Evidence/native-last-ui.json`, audit `native-boundary-audit-last-ui4995.json`. No GenUI rectangle wrapper calls remain. Full source/declaration/runtime review and Blancas B2/R7 remain unfinished.
