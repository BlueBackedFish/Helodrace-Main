# Native trajectory, scatter and coverage checks

Run on the actual 1.7.4995 rev309 engine:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-trajectory -RuntimeRoot .codex-staging/migration17/native-trajectory-4995b -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-trajectory-4995b -TimeoutSeconds 45
```

Use a fresh runtime for a new run. A timeout means keep observing the same process; collect completion once. Inspect the host log as well as the check output.

`FrozenTrajectory.cs` retains the relevant pre-conversion bodies from `79a88777`. Its mortar helper accepts the fetched tile centers/map size as arguments; the calculation body is unchanged. Its travel helper retains the original property expression. Frozen grenade helpers call unchanged production eligibility/source/skill rules. Both copies share the existing native scalar Mathf alias.

Coverage:

- Duplex: 2,058 endpoint pairs across zero/subnormal/small/large positions, spread clamping and sample clamping. Compare all components and exact destination cells. Ballistic: 49 real projectile property evaluations against the frozen horizontal-distance expression. Control only the host ExactPosition input and origin; assert the getter binds.
- Turret: 40 whole-map decisions using the real map, center/outside/corner/height cases and the immediately neighboring floats around the farthest-corner radius. DrawGhost/resource submission is unchanged and is not executed here.
- Mortar: 845 synthetic projections over zero/poles/tiny/oblique/scaled vectors and five map sizes including rectangular and one-cell maps; 256 real world-tile centers through both the native helper and actual world-fetching wrapper. Require exact cells, bounds and map edge membership. No strike scheduling or projectile flight is executed.
- Inventory scatter: 180 cell-target pairs across skills 0/10/20, distance, map edges and four seeds, plus 128 Thing-target pairs at opposite corners. Preserve exact target cells and original Thing identity when an out-of-bounds scatter returns the intended target.
- Inventory launch: 160 pairs with real M67/M14 items/projectiles, two target distances spanning underhand/overhand configuration, direct/four lean sources, four controlled pawn poses and two seeds. Eight additional pairs exercise source rejection, invalid target and failed spawn. Compare spawn request, launch payload, return/debit, and real modern-grenade configuration fields. Each of the 476 scatter/launch/guard pairs compares the following Rand.Int and Rand.Value bits under balanced Rand.PushState/PopState.

The fixture uses a real spawned Helod pawn, skill tracker, game map and actual Defs. It controls DrawPos and TryFindThrowSource, intercepts projectile spawn and Launch, supplies deterministic launch state, and then executes the unchanged real ConfigureInventoryThrow. These are CPU geometry/state/request checks. They do not establish natural LOS/lean selection, job/toil behavior, complete duplex companion firing, projectile physics/damage, effects/rendering, world RNG or performance. Unspawned request Things, marker, pawn, bullet and Harmony overrides are cleaned up. Float comparisons use the existing relative tolerance; discrete cells, payloads, flags, branches, debit and RNG continuation are exact.

Results are written to `Evidence/native-trajectory.json` and the normal migration check/log artifacts.

Validated result: `native-trajectory-4995b`, nine migration checks, COMPLETE and observed process termination (collected exit code null). All counts above were reached. There were 48 Thing-target fallback pairs, 9,765 float comparisons, maximum error 0 and no nonzero differences. The only ERROR in the collected engine log was the existing missing Workshop item 3781143823; native allocation warnings remain. This is not a clean GPU/lifecycle claim. The first attempt stopped during fixture setup because Harmony rejected an inherited getter reference; the final fixture patches the implemented `Projectile.ExactPosition` method and asserts the controlled result.

Product SHA-256: `747BBEDBE21B56E576CD84921E93C91F5427590BFCCCF2FA7919B176E79C00C6`. Product, Main bridge and standalone Blancas17 bridge built with no warnings/errors. ModularWeaponBalance passed all 7,190 checks, including the updated public native duplex helper call. Its placement-editor reflection checks were independently aligned with the previously migrated native API in `1f128be3`.
