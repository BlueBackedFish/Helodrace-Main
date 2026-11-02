# Native power-cutter geometry checks

Build Main and the Main engine bridge, then run serially against actual RimWorld 1.7, Harmony 3 and mandatory Blancas:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-power-cutter -RuntimeRoot .codex-staging/migration17/power-cutter-native17 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/power-cutter-native17 -TimeoutSeconds 45
```

Use a fresh runtime. An observation timeout leaves the game running; continue waiting on that same recorded PID/runtime rather than starting another instance.

FrozenPowerCutter.cs copies d4f6b98c with the namespace changed and Harmony attributes removed. Original bodies, properties, direction/effect logic and game-vector computations remain intact; both copies use the pre-existing native scalar Mathf alias. An isolated frozen comp is added only to the fixture's actual equipped tool so each copy's typed lookup resolves its own comp/properties. The current tracker is switched to that copy's constructed job driver before each side. Current job/driver, comp ownership and position overrides are cleaned up afterward. No product visibility is widened and no duplicate game hooks are registered.

The product now owns native cardinal/up direction, contact/effect origin, spark fan/velocity, smoke/dust offsets and equipment-center calculations. Current FleckCreationData nullable game vector/color fields, grid checks, light spawn and equipment draw callback arguments are explicit host boundaries. CuttingGraphic's authored game Vector2 drawSize is passed directly to the current GraphicDatabase signature; its Vector2.one default is not owned arithmetic. No work/eligibility/destruction/sound/toil/save/tick/RNG rule changes.

The fixture uses an actual generated/spawned Helod pawn, actual owned power cutter and steel wall. DrawPos is controlled and explicitly checked to bind on both Pawn and Building. Four origins (center/edges/outside), fifteen relative vectors (zero, vertical, tiny, diagonal and angles around cardinal boundaries), four worker rotations and four cloned property profiles form 960 cases. Default, negative, zero and enlarged emission/offset profiles test existing clamps. Cardinal/up directions, contact/effect origin and null-cutter helper fallbacks are paired, and actual/frozen CuttingGraphic identities match for all four profiles.

8,640 equipment-prefix pairs cover active cutting, inactive cutting and absent driver states at signed-zero, subnormal and ordinary heights. X/Z/aim output matches; preserved Y bits and inactive non-mutation are explicitly required. The fixture constructs driver/cutting state and job targets rather than executing actual job toils.

7,688 effects pairs call actual/frozen sparks, smoke, dust and metal flash methods under equal Rand.PushState/PopState seeds, comparing all public FleckCreationData fields, light Def/cell/map/rotation requests and following Rand.Int/Rand.Value bits. Eight no-map/mismatched-target-map guards must produce nothing and consume no RNG. Metal flash retains its existing distinction between fleck creation and light-position bounds, so outside-map inputs are not assumed to be rejected uniformly by every emitter. FleckManager.CreateFleck and the Thing GenSpawn overload are intercepted; temporary unspawned lights are destroyed after each side. No particle/light/GPU lifecycle, real work reservation/toils/destruction, sustainer sound, natural world RNG or performance claim is made.

Numeric data uses existing relative 2e-5 criteria and records observed maximum/nonzero differences. Graphic/Def identity, discrete spawn data, order/counts, RNG continuation, preserved height bits and guard results use exact comparisons.

Verified 2026-11-02: actual 1.7.4995 rev309 native-power-cutter-4995 passed seven engine checks. Geometry 960, equipment 8,640, effects/RNG 7,688, guards eight, graphic checks four. Fleck data 12,954, light requests 1,448 and float components 267,892; maximum error zero, no nonzero differences. COMPLETE and process termination observed; collected exit code null. Only ERROR: the known missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. Product literals and frozen bodies match the baseline under stated namespace/attribute changes.

Product DLL SHA-256: EC0945D5E500110E74ACBBB8747BBD6332BAB2B90C10185EF77880F305BA2044. Fresh matching-PDB IL audit leaves only the current GraphicDatabase Vector2.one default and six nullable game vector/color payload constructors in this source. Owned geometry arithmetic is gone. The broader unfinished queue is maintained in Docs/1.7 remaining native calculations.md; other math/color/source boundaries, Blancas B2 and R7 remain ongoing.
