# Native appendage, armor and installed-charge offsets

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-render-offsets -RuntimeRoot .codex-staging/migration17/native-render-offsets-4995 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-render-offsets-4995 -TimeoutSeconds 45
```

Use a fresh root for each new run; keep observing the same process after a timeout and collect completion once. Inspect the engine log separately from check results.

Frozen bodies come from `a4f805c4`. Only the appendage offset/helper methods, armor OffsetFor and installed-charge class are included. They use the same current XML types, base engine contracts and existing scalar alias as production. Unrelated rendering/visibility methods are omitted.

Actual 1.7.4995 rev309 `native-render-offsets-4995`: eight migration checks, COMPLETE and observed process termination, collected exit code null. Product/Main/Blancas17 bridge builds had no warnings/errors. Metrics:

- 1,440 appendage pairs: all three appendages, four facings, head flipping, controlled baby/child/adult stages, five settings profiles (full/empty/partial/null entries with duplicate/null settings), and four base offsets. Distinct per-direction/per-stage values exercise West anatomical ear swapping and the unchanged tail rule. Both copies must perform the expected settings reads; this prevents a missing/inlined settings override from silently bypassing fixture data.
- 9,216 armor pairs: all optional position/part/panel offsets, fixed/PALS placement, authored-graphic early return, normal/north-underlay GraphicData, four facings, three grid locations, normal/narrow/zero-row profiles, zero/negative cell sizes and narrow-panel width clamping. Definitions and nodes are synthetic and unregistered; shared product Defs are not mutated.
- 24 installed-charge pairs: six base positions and four rotations, including half-cell and signed-zero/subnormal inputs. Both getters must reach the controlled base position.
- 32,040 float comparisons: maximum difference 0, no nonzero differences. Host pivots match 63,936 component-bit comparisons, and authored-PALS early-return positions match 27,648 component-bit comparisons. Base OffsetFor is intercepted exactly 21,312 times; charge base DrawPos 48 times; appendage settings reads 4,032.

The fixture generates a real Helod pawn but controls developmental stage, settings getter, base OffsetFor/pivot and base charge DrawPos. It supplies uninitialized render nodes with precisely the fields used by these offset functions; no render-node initialization/visibility, actual age transitions, equipment installation, authored graphic selection, GPU requests, lifecycle, performance or whole-world behavior is proven. Host base offsets/pivots and authored XML/GraphicData remain current game boundaries. Fixtures and Harmony overrides are cleared; no product settings or Defs are changed.

The only ERROR in the final engine log was existing missing Workshop item 3781143823. Native allocation warnings remain; this is not a clean GPU/lifecycle claim. `Evidence/native-render-offsets.json` retains metrics. Product SHA-256 `6F28B65BCE1640B6B5587C15F8EED9A746280129E2557D85F92711359576531F`; fresh matching-PDB audit `native-boundary-audit-render-offsets4995.json` finds no geometry calls in these three offset methods. The owned appendage helpers now use native return/out vectors without a legacy overload.

## Authored selector/crop follow-up

`native-armor-bridges4995-offsets` on the same actual 1.7.4995 passed 10 checks/COMPLETE and exited (collected exit code null). Product SHA-256 `57AEFF42EDEFBD8372D389E84604FE386C330EE044694EB31CF995A54CF80CA4`. ModularArmorDrawOffsets.For now returns native Vector3; its XML fields remain game vectors. ModularArmorPreviewCrop.Rect constructs a native Rect directly from scalar XML fields. Callers no longer import those already-native results; there are no old overloads.

The existing appendage/armor/charge comparisons and all metrics above repeat unchanged, including 32,040 float components with zero difference. The frozen armor worker now contains the original d822f681 directional selector and reads fields directly. It does not call the migrated selector.

Additional direct checks cover 35 direction/invalid-facing cases and 2,401 crop combinations: signed zero, subnormal, negative extents, finite values, infinity and a noncanonical NaN payload. All 9,709 components preserve exact bits, and reflection verifies native result types. No XML shape or saved state changed.

The related `native-armor-bridges4995-ui` also passed seven checks/COMPLETE and exited (exit code null): 738 frames, 50,371 submissions, 626,587 float components with zero difference, unchanged action/input/scroll metrics. Its frozen crop construction explicitly uses the original game Rect constructor rather than the product property. See NativeArmorDialog.md for the controlled UI/texture/input scope and limits. Product/Main bridge builds had zero warnings/errors; the independent ballistic armor program passed 128 checks. Both run logs have only the known missing Workshop item ERROR. These are geometry/CPU request results, not GPU or performance measurements.
