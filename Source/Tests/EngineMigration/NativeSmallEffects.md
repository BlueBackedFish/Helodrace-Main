# Native smoke, casing, suppression and drilling effect inputs

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-small-effects -RuntimeRoot .codex-staging/migration17/native-small-effects-4995b -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-small-effects-4995b -TimeoutSeconds 45
```

Use a fresh root for each new run. Observe the same live process after a timeout; collect completion once and inspect the engine log.

`FrozenSmallEffects.cs` retains baseline `7304f521` bodies: full cigarette utility/carried prefix (patch annotation removed), M79 notification/casing methods with their required owner/state fields, steam outlet calculation, oil-development comp and suppression hediff. Its unchanged internal OilMachineOperation dependency uses a reflection wrapper. Authoring types, current game helpers and existing native scalar alias are shared. No auto-registered frozen MapComponent is introduced.

Actual 1.7.4995 rev309 `native-small-effects-4995b`: nine migration checks, COMPLETE and observed process termination, collected exit code null. Product/Main/Blancas17 bridge builds have no warnings/errors. Coverage:

- Cigarette smoke: 800 paired FleckCreationData submissions over all five habits obtained from real generated pawn IDs (no ID mutation), four facings, four origins including map edges/outside, five sizes and two seeds. Compare 1,310 ember colors including lighting cutoff adjacent floats and progress outside [0,1]. Compare 440 carried-prefix frames using constructed actual chewing state, actual carry ownership, real graphic cache and controlled CalculateCarriedDrawPos output/flip; 800 total graphic submissions preserve identity/rotation/position, including the ember height offset.
- Steam: 120 pairs over the actual authored outlet offsets plus zero/negative/synthetic offsets, all rotations, three origins and two seeds. Check real FleckMaker input and emitted data after outlet rotation/64-degree elevation projection.
- M79: 56 notification/casing profiles over zero/vertical/tiny/cardinal/diagonal directions, negative/zero/normal delays and two seeds. Verify loaded state, due tick and angle; compare 56 actual unspawned MoteThrown spawn requests and Mote/Thrown fields. Two casing guards cover missing Def and unspawned wearer. Host-clock `Mote.spawnRealTime` is deliberately excluded; IDs are not compared. Pending fixture comps are removed from their scheduler sets; no reload job or complete delayed scheduler run is claimed.
- Suppression: 480 interval pairs over timer, severity threshold/adjacent float, four facings, delta and spawned/unspawned states. Run real base/comps and compare age/severity/timer as well as light requests. The fixture does not add these temporary hediffs to the health set or establish natural long-term recovery.
- Oil: 144 pairs over three floor offsets, three intervals, two seeds, spawned/unspawned parents and working/no-power/maintenance/completed states. Power is controlled and completion/event odds are set to float.MaxValue to isolate changed dust coordinates; assert no event/completion unexpectedly occurs. Compare progress/state and real dust helper submissions. This is not a terrain/oil-field completion or maintenance-event test.

All 2,098 seeded pairs preserve following Rand.Int and Rand.Value bits under balanced PushState/PopState; 458 guards also assert zero submissions and zero RNG consumption. Capture totals: FleckCreationData 1,124, mote requests 56, FleckMaker requests 324, graphic draws 800. Float components 27,732, maximum difference 0, no nonzero differences.

DrawPos, carry presentation, mote visibility and oil power are controlled. Real FleckMaker helpers execute but particle creation is intercepted; mote spawning and Graphic.Draw are suppressed. Original job/driver, fixtures, props, ticks and patches are cleaned up. This is CPU geometry/state/request evidence, not actual job/toil scheduling, natural visibility/power, particles/physics, GPU/resource/sound lifecycle, whole-world RNG or performance evidence.

The first attempt stopped during setup because the test omitted GenView.ShouldSpawnMotesAt's bool parameter. The final fixture uses both actual three-argument overloads; product calculations/tolerances were not changed to pass. The final log's only ERROR is existing missing Workshop item 3781143823; native allocation warnings remain. Metrics are in `Evidence/native-small-effects.json`.

Product SHA-256 `C60E29247E8EB5E2D16D033EC0F1A7D76C99561068486DD58EB48B3F5735BA0A`. Matching-PDB audit `native-boundary-audit-small-effects4995.json` removes owned game-vector arithmetic from these five effect paths. Cigarette effect position/carried ember offset and native ember color were included after source review found direct component arithmetic outside the call-only queue. The current public ember helper returns Waylith Color directly, with conversion at GraphicDatabase; no legacy return API is retained. Other component-write candidates are tracked separately in the remaining-work document.
