# Native takedown, dragging rope and held-grenade checks

Build Main and the Main engine bridge, then run serially against actual RimWorld 1.7, Harmony 3 and mandatory Blancas:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-tactical-presentation -RuntimeRoot .codex-staging/migration17/presentation-native17 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/presentation-native17 -TimeoutSeconds 45
```

Use a fresh runtime. An observation timeout leaves the game running; continue waiting on the same recorded PID/runtime and do not start another instance until it exits.

FrozenTacticalPresentation.cs contains selected original method bodies from 14cfc7bd. TakedownGeometry copies IsBehindTarget and BeginAnimation. RopeGeometry copies MapComponentDraw and DrawRopeSegment, with the same original material setup and a fixture link list. GrenadeGeometry copies the original Postfix and references the current actual job-driver types. Namespace/class/constructor scaffolding is adapted for isolated tests; the frozen rope is a plain holder of the same map/link data, so the game does not discover it as a MapComponent; no product API is widened or duplicate game hooks registered.

The product uses native vectors/SpatialMath for takedown direction/angle and rope offsets, normalization, bend and height. Held grenade offset, yaw and TRS use native BCL vectors/quaternion/matrix; only the current game draw callback/Graphics submission crosses a game-vector/matrix boundary. Rope colors use Waylith values at the existing material API boundary. Shader/material/mesh ownership and caches remain current host contracts; no native GPU/resource migration is claimed. Takedown's impactPosition is a direct game DrawPos-to-FleckMaker argument without owned arithmetic, and remains a game value deliberately.

Coverage:

- 480 takedown pairs: four target facings, three origins, ten angles including near/at/outside the rear-arc limits and four distances including zero/tiny. The rear-arc boolean and actual attacker's animation Rotation must match exactly. Pawn DrawPos is controlled; this does not execute the full eligibility, damage/kill, stun or equipment-drop flow.
- 91 rope pairs: three origins, six relative offsets (including near-coincident endpoints), empty/single/two/reversed/null-owner/null-patient links and an actual despawned-patient gate. Four line submissions per visible link compare endpoints, slack bend, altitude, core/outline width and the exact actual cached Material references/order. Links are fixture-injected; Attach/CanTreat/Downed checks, real dragging movement/jobs and saving are not exercised.
- 168 held-grenade pairs: four facings, three origins, tactical unprepared/prepared/released states, inventory held/released/missing-grenade states and portrait/normal rendering. The actual M67 Graphic/mesh/material selection runs, with actual driver objects placed in the current pawn tracker for the duration of the call. 24 visible pairs compare all 16 TRS components, mesh/material identity and layer. The previous driver is restored. These are constructed presentation states, not executing throw toils or actual inventory/target/job admission.

Actual generated/spawned Helod pawns, current map identity and actual graphics caches are used. DrawPos is controlled and line/mesh calls intercepted before submission. These are CPU geometry/state/resource-selection checks, not GPU images, real rendering/pointer input, clinical/takedown/throw behavior or performance. Fixture pawns/grenade/Harmony prefixes are cleaned up afterward. Numeric components use the existing relative 2e-5 comparison; observed maximum/nonzero differences are recorded. Branches, Rotation, resource identity, layers and submission order/count use exact comparisons. No simulation rule, RNG, tick, Scribe key or legacy reader changes.

The first run passed numeric/state checks but logged a fixture construction error: the frozen rope inherited MapComponent with an internal constructor and was discovered during map creation. The fixture now uses a plain holder of the same map/link data. Its copied draw bodies, product and comparison thresholds are unchanged; initial evidence is retained. A check COMPLETE alone is insufficient without inspecting host errors.

Verified 2026-11-02: actual 1.7.4995 rev309 final native-tactical-presentation-4995b passed six engine checks. Takedown: 480 gate/Rotation pairs, including 108 rear-arc accepts. Rope: 91 pairs and 216 line submissions. Held grenade: 168 pairs and 24 matrix/mesh/material submissions. Float components 1,896, maximum error zero, no nonzero differences. COMPLETE and process termination were observed; collected exit code is null. The only ERROR was the known missing Workshop item; the first fixture's MapComponent construction error is absent. Product/Main/Blancas17 builds had zero warnings/errors. The three product files retain their baseline string literals.

Product DLL SHA-256: DFE68F8485F5E0B2A883BD533405C4EB3A8415576C9DB2302E4826FA5EB24361. A fresh matching-PDB NativeBoundaryAudit run finds no remaining geometry-utility calls in these three sources. The broader queue remains in Docs/1.7 remaining native calculations.md; its counts include host contracts and do not represent completion. Other owned calculations, Blancas B2 and R7 remain ongoing.
