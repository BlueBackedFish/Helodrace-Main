# Native small-window and map-label geometry checks

Build Main and the Main engine bridge and run serially against actual RimWorld 1.7, Harmony 3 and required Blancas:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-small-ui -RuntimeRoot .codex-staging/migration17/small-ui-native17 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/small-ui-native17 -TimeoutSeconds 45
```

Use a fresh runtime. An observation timeout leaves the game running: inspect/wait on the same PID and runtime until it exits, without starting another instance.

The frozen three UI bodies come from 57218ad8, with unchanged game-vector/rectangle/color calculations and the shared native scalar Mathf alias. Only namespaces and trailing whitespace change. The frozen gas-store adapter calls the actual current internal store through a reflection-created delegate; no product API visibility is widened. Product Window/ITab callback geometry, Listing_Standard, Widgets/Text/GUI and current host state remain explicit boundaries. Rectangles, colors and scroll/layout arithmetic owned by these three UIs use native types.

The loadout matrix uses an actual generated Helod pawn wearing its permitted M6 harness, and verifies that ownership before comparisons. 72 paired full-window calls cover three sizes, three scroll bit profiles, four searches and collapsed/expanded groups. Another 15 calls exercise group-toggle, minus, plus and numeric 0/9999 responses at desired-count 0/1/9999; two null-loadout calls cover the empty-owner message. Both implementations start with the same desired-ammo dictionary before each side; resulting counts, text buffers, group expansion and search are compared. Current ammo Defs, masses, categories and inventory-count APIs are real. Widget responses and GUI rendering are intercepted. The text field preserves its controlled input, so search filtering is tested but physical typing is not.

The mechanical matrix invokes the actual FillTab body against a current transmitter Thing/comp with controlled networks. 42 calls cover two tab sizes, three scroll profiles, absent selection, disconnected, zero/normal/overload/surplus power and mixed node counts. SelThing is controlled; network simulation/selection input is outside this test. The actual Listing_Standard layout executes, while its host GUI/widget submissions and text measurements are intercepted.

The gas matrix invokes actual MapComponentOnGUI against the current map with controlled mouse/block state, six gas densities and screen heights 64/720/2160. Each of 12 profiles is called twice (72 calls) to cover cache reuse, each gas type, exact/barely-above 0.02 cutoff, mixed percentages/clamping, blocked/out-of-bounds/no-gas gates. Label cache fields, layout submissions and restoration of a non-default font/anchor/color are compared. Current-map filtering is exercised normally; this matrix does not construct a second map.

Scroll inputs are compared by exact component bits, including signed zero/subnormal values, and post-call native/game scroll states are paired. GUI/font/measurement data and widget responses are controlled. These are CPU layout/state/call-order checks, not GPU output, real fonts/pointer routing, hauling jobs or mechanical/gas simulation/performance. Fixture pawns/things, UI screen height and Harmony prefixes are cleaned up after the run. No save key, loadout rule or compatibility reader changes.

The first fixture used a human pawn, which correctly could not wear Helod-only apparel, and omitted the default stuff argument. It stopped at the required button-reach assertion. The fixture now generates a Helod pawn, supplies actual default stuff and asserts real equipped ownership before any UI comparison. The product and numerical thresholds were unchanged; failure evidence is retained.

The second run completed all comparisons but the final mechanical case-count assertion expected 84 instead of 2 sizes x 3 scrolls x 7 profiles = 42. That bookkeeping assertion/documentation was corrected; the matrix and all numerical/state comparisons are unchanged.

Verified 2026-11-02: actual 1.7.4995 rev309 final native-small-ui-4995c passed seven engine checks. Total 203 paired calls (89 loadout, 42 mechanical, 72 gas), 15 loadout action cases, 14,822 native-side submissions, 117 scroll input bit pairs and 151,793 float components; maximum float error zero and no nonzero differences. COMPLETE and process termination were observed; the collected exit code is null. The only ERROR was the known missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. All three product string-literal lists match the baseline; frozen bodies differ only in namespaces and trailing whitespace.

Product DLL SHA-256: 800D4A79B96E51E3390F64A208CC0E3194154613123B37AD44A46FB4757464F0. Raw PE geometry/color fields: 44 in 18 types (XML/current host contracts and generated delegate fields included, locals/signatures excluded). This is not a remaining API count or completion percentage. Tactical aim/close-strike/coordinated-aim/high-speed movement still contain owned game-vector arithmetic despite not declaring such fields; those require the next source-level audit and conversion. Blancas B2 and the R7 door gate also remain ongoing.
