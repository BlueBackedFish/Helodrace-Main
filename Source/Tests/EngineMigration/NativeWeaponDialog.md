# Native modular weapon editor geometry checks

Build the product and engine bridges, then run from the Main repository root with actual RimWorld 1.7, Harmony 3 and the required Blancas 1.7 assembly:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas.csproj -p:BaseIntermediateOutputPath=obj/Blancas17/ -v:minimal
dotnet run --project Source/Tests/ModularWeaponNavigation/ModularWeaponNavigation.Tests.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-weapon-ui -RuntimeRoot .codex-staging/migration17/weapon-ui-native17 -NoWindow -Wait
```

Use a fresh runtime directory. Observation timeout leaves the recorded process alive; poll its PID and resume `Wait-Migration.ps1` on the same runtime. Serialize game runs because early host logging shares a path.

The frozen dialog partials and their UI dependencies come from `586d610f`. They retain the previous game-geometry socket layout and surrounding window math, including the already-native camera/image geometry from earlier units. Test-only adapters call unchanged internal product helpers for gas constants, child transfer, visual layers, palette ratio and workbench entry points. Production visibility is unchanged. The unused game-vector WorldToGui overload was removed from the product after checking that all five callers already passed native points; the native arithmetic body is preserved.

Socket checks compare 240 layouts spanning four areas, 0/1/6/17/40 cards, compact/mixed/plain styles and four rail normals. Card rectangles, source-index page assignments and total page count must match. Another 81 floating sliders and 729 projection/distance/hit decisions include zero/tiny/long segments, threshold-adjacent lengths, signed zero/subnormal coordinates and the 12-pixel hit boundary. Follow offsets are also compared.

The UI fixture creates actual M16A4 and M1911 assemblies. Ninety-six full-window comparisons cross four areas (including 690x430 and off-origin rectangles), raw/converted stats, empty/matching/unmatched search and two scroll bit profiles. Further frames cover camera reset, eight synthetic wheel inputs, ten drag/release/cancel inputs and two actual selected rail attachments. Explicit assertions require expected zoom/clamps, pointer capture, a 50x30 pan, release and Ignore cancellation; matching two ignored implementations would not pass. Card/anchor data, selections, camera state, event consumption and GUI submissions must agree.

The harness controls time, text metrics, Widgets results, alpha/cache data, GUI matrix/group state, focus and hot control. Actual product helpers execute; texture reads are replaced with opaque alpha, and GUI output is intercepted. This proves CPU geometry/state and synthetic-input behavior, not GPU readback, real font/pixel rendering, physical input routing through windows, export clipboard, attachment transactions or workshop jobs. The converted workbench selection window compiles; its owner-list/job flow is not executed by this case. No product Scribe state changes.

Raw pre/post assembly exports are retained. Only the generated top-level preset defName is canonicalized because it contains the current timestamp; all configuration content remains exact. The first trial stopped on that timestamp difference after layout/UI comparisons; the established Main-state snapshot rule was applied in the test, without changing product export behavior. Trial b passed 114 UI frames; final c adds explicit drag assertions, two reset frames and selected rail controls. Synthetic Event.type reads the test event's stored field to avoid ambient host event substitution; this is not a claim of real input coverage.

Verified 2026-11-02: actual 1.7.4995 rev309 `native-weapon-ui-4995c` passed eight engine checks. Socket geometry compared 17,391 float components with maximum error zero. UI compared 118 paired frames, 45,927 native-side submissions, 236 exact scroll bit pairs and 686,130 float components with maximum error zero. Group observations 2,386 count both sides; 102 alpha reads were controlled. Original assembly configuration remained unchanged after export-name canonicalization. COMPLETE and process termination were observed; collected exit code is null. The only ERROR was the existing missing Workshop item.

Product/Main/Blancas17 builds had zero warnings/errors. The existing navigation program passed all groups and its final 30 navigation checks, including the linked native socket layout. String literals in all seven converted dialog files match the frozen baseline.

Product DLL SHA-256: `6F4E01392283529C6FBFCC9FD0EEF54C3ECCDDD8DFEC86ED731906CF5F57004E`. Raw PE geometry/color audit: 58 fields in 25 types, including XML/current game contracts and generated types, excluding locals/signatures. This is not a remaining API count or completion percentage. Attachment editor geometry, other remaining owned calculations, Blancas B2 and the R7 door gate remain separate work.
