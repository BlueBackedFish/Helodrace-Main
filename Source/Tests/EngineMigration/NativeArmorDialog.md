# Native modular armor dialog checks

Build Main and the shared engine bridges, then run serially from the Main repository root with actual RimWorld 1.7, Harmony 3 and the required Blancas 1.7 assembly:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas.csproj -p:BaseIntermediateOutputPath=obj/Blancas17/ -v:minimal
dotnet run --project Source/Tests/BallisticArmor/BallisticArmor.Tests.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-armor-ui -RuntimeRoot .codex-staging/migration17/armor-ui-native17 -NoWindow -Wait
```

Use a fresh runtime. If observation times out, check the recorded PID and resume `Wait-Migration.ps1` on that same runtime. Do not launch a second game while the first remains alive.

The original dialog, stats panel, preview-bounds dependency and stat-motion body are frozen from `b86b8353`. The baseline already has native image bounds from the preceding unit; the surrounding UI uses game Rect/Vector2/Color. A test-only reflection adapter calls the same internal product draft/customization entry points without widening production visibility. The new product dialog is tested as compiled.

The fixture creates an actual player clothing bench and three current apparel types: IBTV assault armor, MOLLE belt and FAST MT helmet. Actual draft creation, slot/PALS compatibility, stat conversions and original armor configuration checks run normally. Preview texture identities and bounds are controlled to avoid GPU readback; font/height and Widgets results are controlled by the shared submission harness.

The 648 full-dialog comparisons cross three armor types, four facings, three rectangles, raw/converted stats, zero/one/two preview layers and three scroll bit profiles. They compare widget/tooltip text, positions, colors/fonts, preview UV/texture identity, PALS lines, ref scroll and state. Signed zero, subnormal and adjacent float scroll inputs/outputs are checked bit-for-bit. Full-dialog size variation includes off-origin rectangles.

Eighteen actual button callbacks rotate the preview and switch stats mode. Sixty extra stats frames retarget a cloned props object's ergonomics through negative, zero, high and lower values and sample the easing/time boundaries. Motion fields, rows and style must match the baseline. Original shared Def props are not changed.

Twelve PALS input/preview frames use a small controlled panel and part with the actual product draft methods. Synthetic game mouse events execute placement, selection, moving, invalid edge/outside/other-button rejection and removal, plus inside/outside placement previews. Draft configuration, selection and event consumption must match; explicit assertions require successful placement/move/removal and rejected invalid actions. Original apparel configuration must remain unchanged.

This is CPU submission/state and synthetic-input evidence. It does not verify real font/layout rendering, GPU frames/readback, actual pointer routing through overlapping windows, physical part menus/stock consumption, export clipboard, applying a customization work order, pawn jobs or save/load. Those behaviors are not changed by this geometry unit. GUI/resource submission is intercepted. No product Scribe format changed.

Verified 2026-11-01: actual 1.7.4995 rev309 `native-armor-ui-4995b` passed seven engine checks. Total: 738 paired frames, 50,371 native-side submissions, 1,794 scroll bit pairs, 626,587 compared float components, maximum error zero. The 7,128 line and 1,296 preview-image observations count both sides. COMPLETE and process termination were observed; collected exit code is null. The only ERROR was the existing missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. The existing independent ballistic armor program passed 128 checks.

The first engine trial failed exact stat-motion state comparison because the harness stored a controlled clock value but had not patched the actual time getter. The final fixture patches Waylith.Time for both bodies; it preserves exact motion-state and input-bit checks and the existing numerical tolerance. Initial failure evidence is retained. Product input also avoids reading mouse coordinates on non-MouseDown events before the early return, preserving the previous short circuit.

Product DLL SHA-256: `2AC275E6FB9BC29121185A7F0E0FE25CB1028296F1AB1BC933E2580BF5211B00`. Raw PE audit: 71 game geometry/color fields in 31 types, including XML/current game contracts and generated types, excluding locals/signatures. This is not a remaining API count or completion percentage.
