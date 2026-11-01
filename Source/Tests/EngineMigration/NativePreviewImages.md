# Native preview image geometry checks

Build the product and Main bridge, then run from the Main repository root with actual RimWorld 1.7, Harmony 3 and the required Blancas 1.7 assembly:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas.csproj -p:BaseIntermediateOutputPath=obj/Blancas17/ -v:minimal
dotnet run --project Source/Tests/ModularWeaponNavigation/ModularWeaponNavigation.Tests.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-preview-images -RuntimeRoot .codex-staging/migration17/preview-images-native17 -NoWindow -Wait
```

Use fresh runtime directories. An observation timeout leaves the recorded process running; poll that PID and resume `Wait-Migration.ps1` on the same runtime. Serialize game runs because early host logging shares a path.

`FrozenArmorPreviewBounds`, `FrozenWeaponImageBounds` and `FrozenWeaponPartImage` preserve the calculation bodies from `debae443`. Both sides use the current native scalar Mathf alias; the frozen geometry is the actual game's compatibility structs. Tests run against the compiled product via reflection, not a replacement implementation.

Coverage:

- 52 mask cases: rectangular/one-pixel images, empty/edge/off-center alpha, values around 7/8/9 and higher alpha, plus invalid armor masks. Armor's inclusive threshold 8 and weapon's exclusive threshold 8/two-pixel padding remain different as before.
- 2,160 armor destination/crop fits, 2,601 weapon unions, mirrored/nonuniform/zero canvas sizes, negative rectangle extents and 16 primed armor layer-set unions.
- 1,080 paired layer draws: 0/1/3/5 graphics, equal and unsorted layer order, populated/mixed/empty alpha, real and synthesized outlines, five areas including invalid extents, six zooms and three pans. GUI group/color/texture submissions and texture identities must match in order.
- Compiled armor bounds cache, texture VisibleUv, image bounds and layer sizes must use native Rect/Vector2 types.

Texture alpha/cache inputs and a small content lookup namespace are controlled. The test uses actual game textures as identities; it primes both texture-data caches and armor bounds caches. GUI submission is intercepted before GPU work. This verifies CPU geometry, ordering, boundary values and state, not readback, native resource lifetime, file content loading, pixel output or performance. The surrounding selection/group bounds adapters compile, while full dialog/input/selection and actual rendered preview checks remain separate work. No Scribe format or persistent state changes are involved.

Verified 2026-11-01: actual 1.7.4995 rev309 `native-preview-images-4995b` passed seven engine checks, including 5,832 native-side submissions and 66,932 compared float components with maximum error zero. COMPLETE and process termination were observed; the collected exit code is null. The only ERROR was the existing missing Workshop item. Product, Main bridge and Blancas17 bridge builds had zero warnings/errors. The independent navigation program passed all groups and its final 30 navigation checks; its first sandboxed run hit a temporary-file move permission error and passed through the approved elevated execution path without product changes.

The first engine trial failed when the test selected ConditionalWeakTable.Remove by name despite its .NET 10 overloads. The final test selects the exact parameter types and retains the initial failure evidence. The comparison thresholds were unchanged.

Product DLL SHA-256: `DEE7441BCA21556CCF8FD2A86A5BA9CA43A92E1106CF69BE7FF16C92AAB81D0E`. Raw PE geometry/color field audit: 75 fields in 33 types, including XML/game contracts and generated types, excluding locals and signatures. This is not a remaining API count or completion percentage.
