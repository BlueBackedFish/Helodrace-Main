# Native attachment editor geometry checks

Build Main and both 1.7 engine bridges, then run from the Main repository root with actual RimWorld 1.7, Harmony 3 and the required Blancas 1.7 assembly:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.Blancas.csproj -p:BaseIntermediateOutputPath=obj/Blancas17/ -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-attachment-ui -RuntimeRoot .codex-staging/migration17/attachment-ui-native17 -NoWindow -Wait
```

Use a fresh runtime directory. Observation timeout leaves the recorded process running; poll its PID and resume `Wait-Migration.ps1` on that same runtime. Serialize game runs because early host logging shares a path.

The two original editor partials are frozen from `21e76c3d`. Test-only adapters call unchanged internal product liveness, rail snapshot and visual-layer methods. The product's native editor is tested as compiled; production visibility is unchanged.

Actual ParseHelper.HandlesType checks confirm that System.Numerics.Vector2/Vector3 lack registered parsers while the game Vector3 is supported. Authoring DTOs therefore retain current game field types; editor calculations import native values before arithmetic and convert at Def writes. The inspector uses a nullable immutable ModularAttachmentPose instead of a temporary game-vector transform. Undo's direct offset/scale/travel/pivot fields and per-weapon pan/scroll are native. These UI/undo values are not Scribe state.

Decompiled actual 1.7 UnityStubs Vector2/Vector3 equality operators call exact component Equals, unlike the historical Unity tolerance assumption. Native unit-scale/zero-offset export predicates preserve the current host's exact default decisions. The 605 comparisons include signed zero, subnormal, near-default, NaN and infinity inputs. No global parser or export tolerance is introduced.

The fixture uses an actual M16A4 assembly with controlled authored flashlight, laser, sight, animation and casing/feed values so all marker branches execute. Shared live Def state is captured/restored before each side, rather than letting the first editor's writes become the second editor's inputs. Whole authoring XML, UI state and event consumption must match.

Coverage includes 180 full-dialog pairs (three areas, placement/animation, ten targets and three scroll bit profiles), 30 actual marker drags across all targets/free-horizontal-vertical constraints and 30 exact undo restores. Eighteen multi-moves cover three angles, ordinary/negative/near-zero scales, two deltas and duplicate nodes sharing one Def. Actual edits must change authoring data and undo must restore the exact original XML. Two weapon switches verify cached pan/scroll/zoom restoration, including signed-zero/subnormal/adjacent-float bits.

Time, text/widget responses, GUI matrix/groups and synthetic events are controlled; submissions are intercepted. This is CPU geometry, live authoring-state and synthetic-input evidence, not GPU frames/readback, real font rendering, physical input routing, disk batch export, clipboard, persisted authoring files or full add/remove-socket workflows. The added-socket dialog's native layout compiles but its create flow is not executed here. The original fixture's shared Def data is restored before process exit.

Verified 2026-11-02: actual 1.7.4995 rev309 final `native-attachment-ui-4995c` passed eight engine checks. Total: 258 paired calls, 41,958 native-side submissions, 360 exact scroll bit pairs and 584,946 compared float components with maximum error zero. Extra XML/state comparisons cover the edit/undo/multi-move and view cases. COMPLETE and process termination were observed; collected exit code is null. The only ERROR was the existing missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. Both editor files retain the baseline's string literals.

The initial fixture passed full-window comparisons but selected RenderSnapshot()[0] for drags, assuming it was the root. Render ordering placed another part first, so Flashlight correctly performed no edit on that part. The fixture now selects path "root" explicitly and retains the edit/undo assertions. Trial b passed 240 calls plus view/equality checks; final c adds shared-Def multi-moves. The first bridge's unused view counter warning was resolved by executing actual view roundtrips. Failure evidence is retained; product behavior and numerical thresholds were not weakened.

Product DLL SHA-256: `1579AE1A0A10526E6374AB2D34C988564987A6AA66BF9B10E97BF3752B485ED0`. Raw PE geometry/color audit: 49 fields in 22 types, including XML/current game contracts and generated types, excluding locals/signatures. This is not a remaining API count or completion percentage. Remaining owned calculations/UI, Blancas B2 and the R7 door gate are separate ongoing work.
