# Native hair palette and gas-cell colors

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-colors -RuntimeRoot .codex-staging/migration17/native-colors-4995b -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-colors-4995b -TimeoutSeconds 45
```

Use a fresh root for a new run, observe the same process after timeout and collect completion once. Inspect the engine log too.

Frozen baseline `bf34952f`: full old hair palette prefix/data and gas AddCell body. The frozen gas helper is a plain map holder, not a registered SectionLayer. Actual and frozen paths share the same current game Defs, map cell indexing, scalar alias and actual grey-color helper.

Actual 1.7.4995 rev309 `native-colors-4995b`: seven migration checks, COMPLETE and observed process termination, collected exit code null. Product/Main/Blancas17 builds have no warnings/errors.

- 27,648 palette pairs: Helod/human/null, six ages, 512 seeds and natural/forced-non-grey/forced-grey policies. All 11 weighted palette colors are observed. Natural HasGreyHair executes in one phase (1,369 actual grey choices); controlled branches in the other phases ensure both paths execute (3,072 forced-grey choices). Real RandomGreyHairColor executes for grey selections. Prefix return, exact float color bits, helper call/delegation counts and following Rand.Int/Rand.Value bits match. The 18,432 human/null guards also assert unchanged incoming color and no helper/RNG consumption. This is the prefix's contract, not a complete gene-tracker override priority test.
- 256 byte-to-float normalization inputs cover every channel value. 775 float-to-byte inputs include all half-byte thresholds and adjacent floats, clamp extremes, NaN and infinities. Compare actual current Game/Waylith Color32 APIs exactly; their decompiled 1.7 implementations both use MathF.Round(value*255) then integer clamp for byte encoding, and channel/255f for decoding.
- 110,592 gas-cell pairs cover every density 0..255, actual gas Defs plus 30 synthetic alpha/index profiles (including hash wrap, out-of-range alpha and quantization boundaries), three real map cells, two altitudes and empty/three-vertex prefixes. All 608,256 vertex/UV/color entries and 829,440 triangle indices match. Each pair preserves seeded RNG continuation. Compare 3,834,880 float component bits and 2,436,124 RGBA byte components in total, all equal.

The actual gas layer uses its real SectionLayer/MapDrawLayer constructor, with a synthetic Section holding the actual map. Buffers are uninitialized LayerSubMesh objects with precisely the four CPU lists used by AddCell, avoiding Mesh/Material creation. No regeneration, gas spreading/exposure, section registration, mesh finalization/GPU/resource lifecycle or performance is claimed. Shared Defs/pawns are not changed by the comparisons; altered gas profiles are unregistered clones. Fixtures and Harmony overrides are cleaned up.

The first attempt passed palette/conversion checks but stopped because a bare gas-layer object lacked the map field initialized by MapDrawLayer's constructor. The final fixture uses the actual constructor and asserts Map binding; product math/tolerances were not weakened. The final log's only ERROR is existing missing Workshop item 3781143823; native allocation warnings remain.

The owned palette field is now Waylith Color32[]. In AddCell, native color-to-byte conversion is performed once and exported to the current host Color32 list for four vertices; removal of repeated conversion is a code-structure observation, not a measured timing/FPS claim. XML gas.color remains the current authoring contract. Product SHA-256 `3B390E2E470D46EE1A86E87B9AC9822905BA3C1DAE6F6ED417C5551445AFEE5A`; metrics `Evidence/native-colors.json`, matching-PDB audit `native-boundary-audit-colors4995.json`. Rect/declaration/source audits and Blancas B2/R7 remain unfinished.
