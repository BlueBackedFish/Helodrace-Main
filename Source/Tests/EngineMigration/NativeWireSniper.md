# Native Zaper wire/impact and sniper display geometry

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-wire-sniper -RuntimeRoot .codex-staging/migration17/native-wire-sniper-4995b -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-wire-sniper-4995b -TimeoutSeconds 45
```

Use a fresh root for a new run, continue observing the same process after timeout, and collect completion once. Inspect the engine log too.

Baseline `68ea21c0`: frozen probe impact/flight-wire/sag bodies and sniper trail/layer/aim functions are copied unchanged. The wire material is fetched from the same current cache. A plain frozen tether adapter reads the current fixture fields and unchanged ConnectionIsValid, then executes the old two-wire call body; no auto-registered MapComponent is introduced. Matrix/transform primitives and game contracts are shared inputs; their earlier independent checks remain separate evidence.

Actual 1.7.4995 rev309 `native-wire-sniper-4995b`: seven migration checks, COMPLETE and observed process termination, collected exit code null. Product/Main/Blancas17 builds have no warnings/errors.

- 576 direct wire pairs over zero/vertical/tiny/threshold-adjacent/cardinal/diagonal directions, signed-zero/subnormal/large origins, lateral offsets and zero/negative widths. 18 flight-wire and 36 paired-tether calls verify the changed input boundaries and missing launcher/tick/source guards. Capture 1,206 line segments with exact material identity and compared width/vertices.
- 3,024 impact pairs cross seven rotations (including near/gimbal/negative/wrapped angles), blocked/unblocked, pawn/null/other hit and launcher, valid/null/other equipment, four damage/wound outcomes and drafted state. Actual human Zaper wearer and Helod target, actual apparel ownership, real probe Def/base fields and actual DamageInfo construction are used. Capture base impact, all 56 damage payloads and 14 probe-registration requests; assert expected gates independently. The controlled ExactRotation getter must execute twice per pair, observed 6,048 reads.
- 325 trail pairs include invalid and zero-horizontal trails, three origins, three durations and six time points. 128 direct layer pairs include angle/length/width extremes. 72 aim-icon pairs cover duration/progress and controlled target positions. Compare all 632 submitted mesh matrices, mesh/material identities and layers. Game altitude and current progress calculations execute.
- Total 4,179 paired RNG continuations, 145 empty render guards and 18,722 float components; maximum difference 0, no nonzero differences. Cleanup restores ticks, removes patches and destroys fixtures including worn device and probes.

The first fixture chose a Helod wearer, which current apparel policy correctly rejects for this device. The final fixture generates a real human, verifies allowed wear and actual ownership, and retains a Helod target. Product policy and tolerances were not changed. The frozen probe is created with an unregistered cloned Def selecting its fixture class, then uses the original Def before spawn; shared Defs are not mutated.

DrawPos, projectile rotation, drafted state and damage result are controlled. Base projectile Impact, target damage and RegisterProbeHit are intercepted; line/mesh GPU submission is suppressed. This proves CPU geometry and request/gate behavior, not complete projectile physics, armor/health damage, paired shock/sound lifetime, natural aiming/jobs, GPU/resource lifecycle, world RNG or performance. Tether connection checks execute for the fixed near spawned pawns/current actual ownership; they do not establish every gameplay connection transition.

Only ERROR in the final log is existing missing Workshop item 3781143823; native allocation warnings remain. Product SHA-256 `0CDB5F9A93789E6D2B3EA133457D41F709305F1078EFBA4BFFD2E0B60037B82E`. Metrics: `Evidence/native-wire-sniper.json`. Matching-PDB audit `native-boundary-audit-wire-sniper4995.json` has zero candidates under the narrow arithmetic-call filter; scalar component writes, Color/Rect/fields/signatures remain a separate unfinished audit.
