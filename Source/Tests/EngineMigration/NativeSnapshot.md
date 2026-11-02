# Native modular snapshot and sight geometry

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-snapshot -RuntimeRoot .codex-staging/migration17/native-snapshot-4995 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-snapshot-4995 -TimeoutSeconds 45
dotnet run --project Source/Tests/ModularWeaponBalance/ModularWeaponBalance.Tests.csproj -- .
```

Use a fresh root on a new run. A timeout leaves the same process running; observe it and collect completion once. Inspect the collected engine log separately.

Frozen baseline `713096d1`: `FrozenSnapshotSights.cs` copies the full resolver. Its unchanged internal MountSurface/TakeBest dependencies are reached through reflection wrappers, and current game-vector inputs use a component-only native boundary. `FrozenSnapshotTree.cs` retains old recursive arithmetic, XML clone/mirror behavior and sorting. The instance owner/fields/repair/palette helper are accessed explicitly or through reflection so the frozen recursion can run on the same actual tree. Root/cache orchestration is a small wrapper; frozen recursion calls itself at every child. Current transform math, XML DTOs and palette conversion are shared unchanged inputs, not an independent baseline for those previously migrated functions.

Actual 1.7.4995 rev309 `native-snapshot-4995`: seven migration checks, COMPLETE and observed process termination; collected exit code null. All 21 actual presets are created/applied through production code. Each has four profiles: authored, opposite-surface/mirrored/nonuniform, near-zero/negative/fallback physical lengths, and null mount/socket transforms with non-rail sockets. Instance props and nested socket/mount/sight data are cloned for altered profiles; original props are restored. Null transforms with rails are not exercised because the old algorithm dereferences their authored position.

Results:

- 84 tree/full-resolution pairs, 1,180 ordered node pairs. Compare public node fields, transforms, parent/socket/mount/rail metadata, graphic centers and layers.
- Compare 164 sight records, 148 occupancy records and all final resolution fields/weights/members, including 147 groups and 35 active groups.
- 28,127 float components, maximum error 0 and no nonzero differences; 61,020 recursively visited values. Identity/discrete values remain exact.
- 84 balanced seeded pairs preserve following Rand.Int and Rand.Value bits. Authored socket/mount pose position/scale/angle/layer component bits are unchanged in 2,472 pose observations.
- EffectiveTransform separately compares every pair of surface enum values with explicit/null authored transforms. Its public return type is the native value pose; there is no legacy DTO overload.
- Product/Main/Blancas17 bridge builds have no warnings/errors. ModularWeaponBalance passes all 7,190 checks with the updated native mount API call.

The new socket/mount calculations use immutable native pose values, avoiding the two old XML DTO clones per traversed edge. This is a code/allocation-structure observation, not a timing/FPS or measured allocation benchmark. Authoring DTOs remain game types for current XML parsing; native math starts at their read boundary. Snapshot cache policy, tree ordering, validation, optics rules, save keys and RNG rules are unchanged. The fixture does not submit GPU work or establish full jobs, firing, damage, GUI/resource lifecycle, natural world RNG or performance. Altered profiles deliberately stress arithmetic beyond normal authored presets; they are not player configuration validation tests.

The only ERROR in the final engine log is existing missing Workshop item 3781143823; native allocation warnings remain. Metrics are in `Evidence/native-snapshot.json`. Product SHA-256 `D7B2F9A9FBB9154315FD6C3CFA0C94793C597FD959E2868E527560D2C47B598D`; matching-PDB audit `native-boundary-audit-snapshot4995.json` has no owned game-vector arithmetic in the converted tree/sight methods.
