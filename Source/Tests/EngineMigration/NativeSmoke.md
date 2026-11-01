# Native smoke, explosion visuals and fragment geometry checks

Build Main and the Main engine bridge, then run serially against actual RimWorld 1.7, Harmony 3 and mandatory Blancas:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-smoke -RuntimeRoot .codex-staging/migration17/smoke-native17 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/smoke-native17 -TimeoutSeconds 45
```

Use a fresh runtime. An observation timeout leaves the game running; inspect/wait on the same recorded PID/runtime instead of starting another instance.

FrozenSmokeUtility.cs copies the 3940cabc utility with only its namespace changed. FrozenSmokeGrenadeEffects.cs copies the original fragment-verb/owner and explosion effect utility bodies, uses the current public extension DTOs and calls the frozen smoke utility. The current native scalar Mathf alias is shared as before. The frozen class scaffold does not register new product hooks/MapComponents. Product visibility is not widened.

The smoke API takes System.Numerics.Vector3 directly for origin/direction; no old game-vector overload is kept. Normalization, spread/yaw, position and velocity math use Waylith/BCL values. Explosion dust/sparks colors and positions, ring/flashbang clients and fragment scatter/target-cell calculations are native too. The current breach caller imports its IntVec3 direction at the boundary. FleckCreationData's nullable game vector/color fields, grid conversion, Projectile.Launch and game effect APIs remain explicit boundaries. No particle/graphics ownership, launch/targeting rule, RNG, tick or save policy changes.

Coverage:

- 18,432 directional/ease-out and 6,912 radial/ease-out pairs: six origins (center, map edges/outside and adjacent-float/signed-zero subcell values), eight directional inputs including zero/vertical and near the tiny-direction cutoff, counts -2/0/1/9, negative/zero/ordinary spread or jitter, four negative/zero/ordinary/large range-radius profiles, two seeds, explicit/null Defs, and radial wrapped angle offsets. Four additional null-map cases cover all helper entry points. All public FleckCreationData fields, emission counts/order and following Rand.Int/Rand.Value bits are compared. 15,844 guard cases explicitly require no particle output or RNG consumption. RadialEaseOut's existing lack of an outer origin-bounds guard is retained and compared, so an outside origin is not assumed to be an immediate exit for every helper.
- 144 expanding-ring, lingering-dust and flashbang client pairs: center/corners/outside, real/null map, two seeds and null/default/negative/extreme extension values. Smoke, dust, sparks, color and flashbang-light spawn requests are compared. Real FleckMaker helpers execute before the particle boundary; mote visibility is forced true.
- 816 fragment pairs: center/corners/outside, count 0/8/100 with existing clamps, random vs targeted fractions, two seeds, zero/tiny/cardinal/diagonal directional inputs and negative/ordinary/full/oversized cones. Origin, exact spawn/target cells, launcher fallback, flags, cover and equipment arguments are compared at the existing game API. Actual map edifice lookup/ray blocking and ShotReport construction execute. Targeted CanHitTargetFrom eligibility is controlled to admit fixture targets; six actual targeted launch requests must be observed. This is not natural eligibility/aim/hit validation.

Each pair runs inside balanced Rand.PushState/PopState, comparing continuation bits before restoring the shared RNG. FleckManager.CreateFleck, GenSpawn's Thing overload and Projectile.Launch are intercepted: creation/launch requests and their constructed Thing instances are observed, while transient unspawned Things are destroyed after each side. Flashbang lights/projectiles are not actually spawned/simulated. These are CPU geometry/data/request/RNG-under-stated-boundaries checks, not particle/physics/light/GPU/audio lifecycles, real fragmentation damage, natural world RNG or performance. Generated fixture pawns are removed and Harmony prefixes cleared at completion.

Numeric fields use the existing relative 2e-5 comparison and record actual maximum/nonzero differences; discrete cells/identity/flags/counts/order and RNG continuation use exact comparisons. The breach call-site adaptation compiles but full breach gameplay is not executed here.

Verified 2026-11-02: actual 1.7.4995 rev309 native-smoke-4995 passed eight engine checks. Total 26,308 pairs, 27,219 fleck data, 6,064 spawn requests, 6,058 launch requests (six targeted), 20,517 empty cases and 437,681 float components. Maximum difference zero; no nonzero differences. COMPLETE and process termination observed; collected exit code null. Only ERROR: the known missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. Product string literals and frozen bodies match the baseline apart from their stated scaffolding/imports.

Product DLL SHA-256: 8FCD4D207CE35C06943F9C8D8DA666C232BBEA3CF86EEA8F64242CF2B622EAD0. Fresh matching-PDB IL audit leaves only six nullable game-vector/color payload constructors in the two sources, with no owned geometry-utility arithmetic. They are required by current FleckCreationData fields. The broader unfinished queue remains in Docs/1.7 remaining native calculations.md; other math/color/source boundaries, Blancas B2 and R7 remain ongoing.
